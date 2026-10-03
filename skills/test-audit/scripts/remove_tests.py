"""Remove whole xUnit test methods, or chosen theory rows, from C# test files.

Reads a ledger: a JSON list of entries such as
  {"test": "Namespace.Class.Method", "decision": "delete"}
  {"test": "Namespace.Class.Method", "decision": "delete_rows", "rows": ["[InlineData(1, 2)]"]}
Entries with any other decision are ignored. A "file" field is used when it
holds the method; otherwise the method is found under --root by its class and
declaration, because a class split across files (partial) can't be found by
its name alone. Removes each method with its attributes, doc comment and one
blank line, or only the listed rows. Brace matching ignores comments and
string, verbatim, interpolated, raw and character literals.
"""

import argparse
import json
import re
from pathlib import Path


def code_mask(text):
    """For each character, True when it is code rather than a comment or literal."""
    mask = [True] * len(text)
    i, n = 0, len(text)

    def blank(start, end):
        for k in range(start, min(end, n)):
            mask[k] = False

    while i < n:
        if text.startswith("//", i):
            end = text.find("\n", i)
            end = n if end < 0 else end
            blank(i, end)
            i = end
        elif text.startswith("/*", i):
            end = text.find("*/", i + 2)
            end = n if end < 0 else end + 2
            blank(i, end)
            i = end
        elif text[i] == "'":
            j = i + 1
            while j < n and text[j] != "'":
                j += 2 if text[j] == "\\" else 1
            blank(i, j + 1)
            i = j + 1
        elif text[i] == '"' or (text[i] in "@$" and i + 1 < n and text[i + 1] in '"@$'):
            start, prefix = i, ""
            while i < n and text[i] in "@$":
                prefix += text[i]
                i += 1
            quotes = 0
            while i < n and text[i] == '"' and quotes < 3:
                quotes += 1
                i += 1
            if quotes == 3:
                while i < n and text[i] == '"':
                    quotes += 1
                    i += 1
                end = text.find('"' * quotes, i)
                i = n if end < 0 else end + quotes
            elif quotes == 2 and "@" not in prefix:
                pass  # an empty regular string
            else:
                verbatim, interpolated, depth = "@" in prefix, "$" in prefix, 0
                if quotes == 2:  # a verbatim string that starts with an escaped quote
                    i -= 1
                while i < n:
                    c = text[i]
                    if not verbatim and c == "\\":
                        i += 2
                        continue
                    if interpolated and c == "{":
                        if text.startswith("{{", i) and depth == 0:
                            i += 2
                            continue
                        depth += 1
                    elif interpolated and c == "}" and depth:
                        depth -= 1
                    elif c == '"' and depth == 0:
                        if verbatim and text.startswith('""', i):
                            i += 2
                            continue
                        i += 1
                        break
                    i += 1
            blank(start, i)
        else:
            i += 1
    return mask


def declarations(text, mask, name):
    pattern = re.compile(r"^[ \t]*(?:public|private|internal|protected)\b[^\n;={}]*?\b" + re.escape(name) + r"\s*\(", re.M)
    return [match for match in pattern.finditer(text) if mask[match.end() - 1]]


def method_span(text, mask, match):
    """Start and end of a method, from its first attribute or comment line to its last line."""
    i, depth = match.end() - 1, 0
    while i < len(text):  # skip the parameter list
        if mask[i]:
            depth += {"(": 1, ")": -1}.get(text[i], 0)
            if depth == 0:
                break
        i += 1
    i += 1
    while i < len(text) and not (mask[i] and (text[i] == "{" or text.startswith("=>", i))):
        i += 1
    depth = 0
    if text[i] == "{":
        while i < len(text):
            if mask[i]:
                depth += {"{": 1, "}": -1}.get(text[i], 0)
                if depth == 0:
                    break
            i += 1
    else:  # an expression body ends at the first ';' outside any brackets
        while i < len(text) and not (mask[i] and text[i] == ";" and depth == 0):
            if mask[i]:
                depth += {"(": 1, "[": 1, "{": 1, ")": -1, "]": -1, "}": -1}.get(text[i], 0)
            i += 1
    newline = text.find("\n", i)
    end = len(text) if newline < 0 else newline + 1
    # Attributes, doc comments and comments sit between the previous member and this one.
    start = text.rfind("\n", 0, match.start()) + 1
    while start > 0:
        previous = text.rfind("\n", 0, start - 1) + 1
        line = text[previous:start - 1].strip()
        if not line or line.endswith(("}", "{", ";")):
            break
        start = previous
    if text[end:end + 1] == "\n":
        end += 1
    elif start >= 2 and text[start - 2:start] == "\n\n":
        start -= 1
    return start, end


def locate(entry, root):
    """The one file under root that declares the entry's method in its class."""
    *_, cls, name = entry["test"].split(".")
    candidates = [Path(entry["file"])] if entry.get("file") else []
    candidates += sorted(path for path in root.rglob("*.cs") if "/obj/" not in path.as_posix() and "/bin/" not in path.as_posix())
    for path in dict.fromkeys(candidates):
        if not path.is_file():
            continue
        text = path.read_text()
        if re.search(r"\b(?:class|record)\s+" + re.escape(cls) + r"\b", text) and declarations(text, code_mask(text), name):
            return path
    raise ValueError(f"{entry['test']}: no file under {root} declares it")


def remove_entry(text, entry):
    name = entry["test"].split(".")[-1]
    mask = code_mask(text)
    found = declarations(text, mask, name)
    if len(found) != 1:
        raise ValueError(f"{entry['test']}: found {len(found)} declarations")
    if entry["decision"] == "delete":
        start, end = method_span(text, mask, found[0])
        return text[:start] + text[end:]
    start, _ = method_span(text, mask, found[0])
    head = text[start:found[0].start()]
    for row in entry["rows"]:
        lines = [line for line in head.split("\n") if row.strip() in line]
        if len(lines) != 1:
            raise ValueError(f"{entry['test']}: row {row!r} matched {len(lines)} lines")
        head = head.replace(lines[0] + "\n", "", 1)
    return text[:start] + head + text[found[0].start():]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("ledger", type=Path)
    parser.add_argument("--root", type=Path, default=Path("tests"), help="where to look for test files")
    args = parser.parse_args()
    entries = [entry for entry in json.loads(args.ledger.read_text()) if entry["decision"] in ("delete", "delete_rows")]
    for entry in entries:
        path = locate(entry, args.root)
        path.write_text(remove_entry(path.read_text(), entry))
        rows = f"{len(entry['rows'])} rows of " if entry["decision"] == "delete_rows" else ""
        print(f"removed {rows}{entry['test']} from {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
