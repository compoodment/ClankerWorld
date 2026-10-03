"""Compare identical production scopes in two Coverlet Cobertura reports.

The assemblies, files and lines measured must be the same in both reports;
only whether each one ran may differ. Test assemblies may report only the
production source their test project links with <Compile Include>, which is
read from every test project under the repository's tests/ folder.
"""

import argparse
import json
import math
import re
import xml.etree.ElementTree as ET
from pathlib import Path


class CoverageReport(dict):
    """Source locations plus the collector's authoritative aggregate counters."""

    aggregate = None
    package_branch_rates = None


def source_key(filename, sources=()):
    """A repository-relative path such as src/Project/File.cs. Cobertura file
    names may be absolute or relative to one of the report's <source> roots."""
    normalized = filename.replace("\\", "/")
    if not normalized.startswith("/") and not re.match(r"^[A-Za-z]:/", normalized):
        roots = [root.replace("\\", "/").rstrip("/") + "/" for root in sources]
        existing = [root for root in roots if Path(root + normalized).exists()]
        if existing or roots:
            normalized = (existing or roots)[0] + normalized
    if "/src/" in normalized:
        return "src/" + normalized.split("/src/", 1)[1]
    return normalized


def is_test_sdk_entry_point(filename):
    # The test SDK generates a Program.cs entry point inside the test assembly.
    # coverage.runsettings excludes it; drop it here too, whatever its version,
    # so reports collected without that exclusion still compare.
    return "/microsoft.net.test.sdk/" in filename.lower() and filename.endswith("/Microsoft.NET.Test.Sdk.Program.cs")


def load_report(path):
    root = ET.parse(path).getroot()
    sources = [item.text for item in root.findall("./sources/source") if item.text]
    packages = CoverageReport()
    packages.package_branch_rates = {}
    omitted = {"lines_covered": 0, "lines_valid": 0, "branches_covered": 0, "branches_valid": 0}
    for package in root.findall("./packages/package"):
        rate = float(package.attrib["branch-rate"])
        if not math.isfinite(rate) or not 0 <= rate <= 1:
            raise ValueError("Invalid assembly branch rate")
        packages.package_branch_rates[package.attrib["name"]] = 100 * rate
        lines = {}
        for cls in package.findall("./classes/class"):
            filename = source_key(cls.attrib["filename"], sources)
            if is_test_sdk_entry_point(filename):
                for line in cls.findall("./lines/line"):
                    omitted["lines_valid"] += 1
                    omitted["lines_covered"] += int(line.attrib["hits"]) > 0
                    branch = re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage", ""))
                    if branch:
                        omitted["branches_covered"] += int(branch.group(1))
                        omitted["branches_valid"] += int(branch.group(2))
                continue
            if not filename.startswith("src/"):
                raise ValueError(f"Coverage includes non-production source: {filename}")
            for line in cls.findall("./lines/line"):
                key = (filename, int(line.attrib["number"]))
                branch = re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage", ""))
                if line.get("branch", "false").lower() == "true" and branch is None:
                    raise ValueError(f"Missing or invalid branch metadata for {key}")
                data = {
                    "hit": int(line.attrib["hits"]) > 0,
                    "branches_hit": int(branch.group(1)) if branch else 0,
                    "branches_total": int(branch.group(2)) if branch else 0,
                }
                if int(line.attrib["hits"]) < 0 or not 0 <= data["branches_hit"] <= data["branches_total"]:
                    raise ValueError(f"Invalid coverage counts for {key}")
                if key in lines and lines[key] != data:
                    raise ValueError(f"Conflicting duplicate coverage for {key}")
                lines[key] = data
        packages[package.attrib["name"]] = lines
    if not packages or not any(packages.values()):
        raise ValueError("Coverage report is empty")
    packages.aggregate = {name: int(root.attrib[name.replace("_", "-")]) - omitted[name] for name in omitted}
    for dimension in ("lines", "branches"):
        covered, valid = packages.aggregate[f"{dimension}_covered"], packages.aggregate[f"{dimension}_valid"]
        if not 0 <= covered <= valid or valid == 0:
            raise ValueError("Invalid or empty aggregate coverage counters")
    source_metrics = [metrics(lines) for lines in packages.values()]
    for name in ("lines_covered", "lines_valid"):
        if packages.aggregate[name] != sum(item[name] for item in source_metrics):
            raise ValueError("Aggregate line counters disagree with production source scope")
    for name in ("branches_covered", "branches_valid"):
        if packages.aggregate[name] < sum(item[name] for item in source_metrics):
            raise ValueError("Aggregate branch counters are smaller than mapped source branches")
    packages.aggregate["line_percent"] = 100 * packages.aggregate["lines_covered"] / packages.aggregate["lines_valid"]
    packages.aggregate["branch_percent"] = 100 * packages.aggregate["branches_covered"] / packages.aggregate["branches_valid"]
    return packages


def test_projects(repo):
    """Each test project's assembly name and the production files it links."""
    projects = {}
    for project in sorted((repo / "tests").rglob("*.csproj")):
        root = ET.parse(project).getroot()
        name = next((item.text for item in root.iter("AssemblyName") if item.text), project.stem)
        projects[name] = {
            source_key(str((project.parent / item.attrib["Include"].replace("\\", "/")).resolve()))
            for item in root.iter("Compile") if "Include" in item.attrib
        }
    if not projects:
        raise ValueError("Run from the repository root or pass --repo: no test projects under tests/")
    return projects


def validate_linked_sources(packages, repo):
    for assembly, expected in test_projects(repo).items():
        actual = {file for file, _ in packages.get(assembly, {})}
        if expected != actual:
            raise ValueError(f"{assembly} coverage differs from the source its project links: "
                             f"missing={sorted(expected - actual)}, extra={sorted(actual - expected)}")


def metrics(lines):
    valid = len(lines)
    hit = sum(line["hit"] for line in lines.values())
    branches_valid = sum(line["branches_total"] for line in lines.values())
    branches_hit = sum(line["branches_hit"] for line in lines.values())
    return {
        "lines_covered": hit,
        "lines_valid": valid,
        "line_percent": 100 * hit / valid if valid else None,
        "branches_covered": branches_hit,
        "branches_valid": branches_valid,
        "branch_percent": 100 * branches_hit / branches_valid if branches_valid else None,
    }


def compare(before, after):
    if before.keys() != after.keys():
        raise ValueError("Production assembly scope changed")
    result = {}
    all_before, all_after = {}, {}
    for package in before:
        old, new = before[package], after[package]
        if old.keys() != new.keys():
            raise ValueError(f"Production line scope changed in {package}")
        if any(old[key]["branches_total"] != new[key]["branches_total"] for key in old):
            raise ValueError(f"Production branch scope changed in {package}")
        result[package] = comparison(old, new)
        all_before.update({(package, *key): value for key, value in old.items()})
        all_after.update({(package, *key): value for key, value in new.items()})
    result["total"] = comparison(all_before, all_after, detail=False)
    if isinstance(before, CoverageReport) and isinstance(after, CoverageReport):
        for name in ("lines_valid", "branches_valid"):
            if before.aggregate[name] != after.aggregate[name]:
                raise ValueError("Collector aggregate denominator changed")
        result["total"] = delta_summary(before.aggregate, after.aggregate)
        for package in before:
            entry = result[package]
            for side, report in (("baseline", before), ("final", after)):
                values = entry[side]
                values["mapped_branches_covered"] = values.pop("branches_covered")
                values["mapped_branches_valid"] = values.pop("branches_valid")
                values["branch_percent"] = report.package_branch_rates[package]
            entry.update(delta_summary(entry["baseline"], entry["final"]))
    return result


def comparison(old, new, detail=True):
    base, final = metrics(old), metrics(new)
    result = delta_summary(base, final)
    if detail:
        result["lost_lines"] = [{"file": key[0], "line": key[1]} for key in old if old[key]["hit"] and not new[key]["hit"]]
        result["reduced_branch_lines"] = [{"file": key[0], "line": key[1], "before": old[key]["branches_hit"], "after": new[key]["branches_hit"]} for key in old if old[key]["branches_hit"] > new[key]["branches_hit"]]
    return result


def delta_summary(base, final):
    result = {"baseline": base, "final": final}
    for metric in ("line", "branch"):
        first, last = base[f"{metric}_percent"], final[f"{metric}_percent"]
        result[f"{metric}_delta_pp"] = last - first if first is not None and last is not None else None
        result[f"{metric}_relative_change_percent"] = 100 * (last - first) / first if first and last is not None else None
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("final", type=Path)
    parser.add_argument("--max-drop-pp", type=float, default=2.0)
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--threshold-scope", choices=("total", "each"), default="total")
    args = parser.parse_args()
    if not math.isfinite(args.max_drop_pp) or args.max_drop_pp < 0:
        parser.error("--max-drop-pp must be finite and nonnegative")
    baseline, final = load_report(args.baseline), load_report(args.final)
    validate_linked_sources(baseline, args.repo)
    validate_linked_sources(final, args.repo)
    result = compare(baseline, final)
    print(json.dumps(result, indent=2))
    checked = result.values() if args.threshold_scope == "each" else [result["total"]]
    return int(any(item.get(f"{metric}_delta_pp") is not None and item[f"{metric}_delta_pp"] < -args.max_drop_pp for item in checked for metric in ("line", "branch")))


if __name__ == "__main__":
    raise SystemExit(main())
