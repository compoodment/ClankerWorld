"""Check that every removal in a ledger still has a retained proof that runs.

Reads the ledger (a JSON list of entries with "test", "decision" and
"retained_proof", a list of fully qualified test names) and the tests that
run after the removals: a `dotnet test --list-tests` output saved to a file,
a TRX file, or a folder of TRX files. For each entry marked "delete" or
"delete_rows" it reports:

  - a named proof that doesn't run, and whether this ledger removes it too;
  - a named proof that runs only under another class name, as a class split
    across files (partial) does: check that it is the same test;
  - a deleted test that still runs, or a test whose rows were trimmed that no
    longer runs at all;
  - with --held, a proof that a follow-up still plans to remove, so the
    follow-up knows which removals rely on it.

Exits 1 when a named proof doesn't run or a removal didn't happen. Fix the
ledger, or put the test back, before pushing.
"""

import argparse
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

import trx_summary

LISTED_TEST = re.compile(r"^\s*([A-Za-z_][\w`+]*(?:\.[A-Za-z_][\w`+]*)+)(?:\(.*\))?\s*$")


def short(name):
    """Class.Method, without namespace or theory arguments."""
    return ".".join(name.split("(", 1)[0].strip().split(".")[-2:])


def read_tests(path):
    """Short names of the tests that run, from --list-tests output or TRX files."""
    if path.is_dir() or path.suffix.lower() == ".trx":
        files = [path] if path.is_file() else sorted(path.rglob("*.trx"))
        if not files:
            raise SystemExit(f"No TRX files under {path}")
        cases, _, _ = trx_summary.read(files)
        names = cases
    else:
        names = [match[1] for line in path.read_text().splitlines() if (match := LISTED_TEST.match(line))]
    tests = {short(name) for name in names}
    if not tests:
        raise SystemExit(f"No test names found in {path}")
    return tests


def check(ledger, tests, held=()):
    by_method = defaultdict(set)
    for name in tests:
        by_method[name.split(".")[-1]].add(name)
    removals = [entry for entry in ledger if entry.get("decision") in ("delete", "delete_rows")]
    deleted = {short(entry["test"]) for entry in removals if entry["decision"] == "delete"}
    held = {short(name) for name in held}
    report = {"missing_proofs": [], "other_class": [], "not_removed": [], "trimmed_but_gone": [], "relies_on_held": []}
    for entry in removals:
        test = short(entry["test"])
        if entry["decision"] == "delete" and test in tests:
            report["not_removed"].append(test)
        if entry["decision"] == "delete_rows" and test not in tests:
            report["trimmed_but_gone"].append(test)
        for proof in entry.get("retained_proof") or []:
            name = short(proof)
            if name in held:
                report["relies_on_held"].append({"test": test, "proof": name})
            if name in tests:
                continue
            elsewhere = sorted(by_method.get(name.split(".")[-1], set()))
            if elsewhere and name not in deleted:
                report["other_class"].append({"test": test, "proof": name, "runs_as": elsewhere})
            else:
                report["missing_proofs"].append({
                    "test": test, "proof": name,
                    "removed_by_this_ledger": name in deleted,
                    "other_proofs_that_run": sorted(short(other) for other in entry.get("retained_proof") or []
                                                    if short(other) in tests),
                })
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("ledger", type=Path)
    parser.add_argument("--tests", required=True, type=Path,
                        help="--list-tests output, a TRX file or a folder of TRX files from after the removals")
    parser.add_argument("--held", type=Path, help="tests a follow-up still plans to remove, one per line")
    args = parser.parse_args()
    held = [line.strip() for line in args.held.read_text().splitlines() if line.strip()] if args.held else []
    report = check(json.loads(args.ledger.read_text()), read_tests(args.tests), held)
    print(json.dumps(report, indent=2))
    failed = report["missing_proofs"] or report["not_removed"] or report["trimmed_but_gone"]
    if failed:
        print("A removal has a named proof that doesn't run, or didn't happen as the ledger says.", file=sys.stderr)
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
