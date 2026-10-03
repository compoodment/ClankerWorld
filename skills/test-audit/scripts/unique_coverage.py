"""Find which tests add coverage no other test gives.

Reads the per-test Cobertura reports that per_test_coverage.py writes (one
folder per test method, each with a test.txt naming it) and reports, for every
test, the lines and branches it covers and how many of them no other test
covers. It can then:

  --plan        list tests that can go one after another without losing any
                line or mapped branch, cheapest proof first (fewest unique lines, then
                the slowest), skipping any test in --keep;
  --remove FILE simulate removing the tests listed in FILE and report the
                coverage left and the lines lost.

Line coverage is exact. Cobertura gives only a count of branches taken per
line, not which ones, so a branch union is estimated as the most any single
remaining test took on that line: a lower bound. Removal risk uses an upper
bound on branches the candidate could cover exclusively, so partial counts
alone cannot prove zero loss. These estimates cover source-mapped branches;
Coverlet's additional unmapped branches cannot be attributed here. Confirm any
plan with a full-suite coverage run and compare_coverage.py, which also checks
the collector's total branch counts.
"""

import argparse
import json
import re
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

from coverage_evidence import source_key
from per_test_coverage import completed_report, slug


class Universe:
    """Numbers every instrumented source line, so a test's lines fit in one integer bitmask."""

    def __init__(self):
        self.ids, self.keys, self.branch_totals = {}, [], {}

    def id(self, key):
        if key not in self.ids:
            self.ids[key] = len(self.keys)
            self.keys.append(key)
        return self.ids[key]


def read_report(root, universe):
    mask, branches = 0, {}
    sources = [item.text for item in root.iterfind("./sources/source") if item.text]
    for package in root.iterfind("./packages/package"):
        for cls in package.iterfind("./classes/class"):
            filename = source_key(cls.attrib["filename"], sources)
            for line in cls.iterfind("./lines/line"):
                index = universe.id((package.attrib["name"], filename, int(line.attrib["number"])))
                branch = re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage", ""))
                if branch:
                    universe.branch_totals[index] = max(universe.branch_totals.get(index, 0), int(branch[2]))
                if int(line.attrib["hits"]) > 0:
                    mask |= 1 << index
                if branch and int(branch[1]) > 0:
                    branches[index] = max(branches.get(index, 0), int(branch[1]))
    return mask, branches


def load(folder):
    universe, tests = Universe(), {}
    try:
        methods = json.loads((folder / "methods.json").read_text())
    except (OSError, ValueError) as error:
        raise ValueError(f"Incomplete per-test evidence: {folder}; missing valid methods.json") from error
    if not isinstance(methods, list) or not methods or not all(isinstance(name, str) and name for name in methods):
        raise ValueError(f"Incomplete per-test evidence: {folder}; invalid methods.json")
    for name in sorted(set(methods)):
        directory = folder / slug(name)
        tests[name] = read_report(completed_report(directory, name), universe)
    if not tests:
        raise SystemExit(f"No per-test coverage reports under {folder}")
    return universe, tests


def bits(mask):
    while mask:
        low = mask & -mask
        yield low.bit_length() - 1
        mask ^= low


def read_durations(paths):
    if not paths:
        return {}
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    durations = defaultdict(float)
    for path in paths:
        for file in ([path] if path.is_file() else path.rglob("*.trx")):
            root = ET.parse(file).getroot()
            names = {test.attrib["id"]: f"{m.attrib['className']}.{m.attrib['name']}"
                     for test in root.iterfind(".//t:UnitTest", ns)
                     if (m := test.find("t:TestMethod", ns)) is not None}
            for result in root.iterfind(".//t:UnitTestResult", ns):
                match = re.fullmatch(r"(\d+):(\d+):(\d+(?:\.\d+)?)", result.attrib.get("duration", ""))
                if match and result.attrib.get("testId") in names:
                    durations[names[result.attrib["testId"]]] += int(match[1]) * 3600 + int(match[2]) * 60 + float(match[3])
    return durations


class Coverage:
    """How many remaining tests cover each line, and the branch lower bound per line."""

    def __init__(self, universe, tests):
        self.universe, self.tests = universe, tests
        self.remaining = set(tests)
        self.counts = [0] * len(universe.keys)
        # Per branch line: how many remaining tests took each number of branches there.
        self.branch_levels = defaultdict(lambda: defaultdict(int))
        for mask, branches in tests.values():
            for index in bits(mask):
                self.counts[index] += 1
            for index, hits in branches.items():
                self.branch_levels[index][hits] += 1

    def unique(self, name):
        mask, branches = self.tests[name]
        lines = sum(1 for index in bits(mask) if self.counts[index] == 1)
        branch_loss = 0
        for index, hits in branches.items():
            levels = self.branch_levels[index]
            others = max((value for value, count in levels.items() if count - (value == hits) > 0), default=0)
            branch_loss += min(hits, self.universe.branch_totals[index] - others)
        return lines, branch_loss

    def remove(self, name):
        if name not in self.remaining:
            return
        mask, branches = self.tests[name]
        for index in bits(mask):
            self.counts[index] -= 1
        for index, hits in branches.items():
            levels = self.branch_levels[index]
            levels[hits] -= 1
            if levels[hits] == 0:
                del levels[hits]
        self.remaining.discard(name)

    def totals(self):
        lines_valid = len(self.universe.keys)
        lines_covered = sum(1 for count in self.counts if count > 0)
        branches_valid = sum(self.universe.branch_totals.values())
        branches_covered = sum(max(levels, default=0) for levels in self.branch_levels.values())
        return {
            "tests": len(self.remaining),
            "lines_covered": lines_covered, "lines_valid": lines_valid,
            "line_percent": round(100 * lines_covered / lines_valid, 3) if lines_valid else None,
            "branches_covered_at_least": branches_covered, "branches_valid": branches_valid,
            "branch_percent_at_least": round(100 * branches_covered / branches_valid, 3) if branches_valid else None,
        }


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("reports", type=Path, help="folder of per-test coverage folders")
    parser.add_argument("--timings", type=Path, nargs="*", help="TRX files or folders, to prefer removing slow tests")
    parser.add_argument("--keep", type=Path, help="tests the plan must not remove, one per line")
    parser.add_argument("--plan", action="store_true")
    parser.add_argument("--remove", type=Path, help="tests to simulate removing, one per line")
    parser.add_argument("--table", type=Path, help="write a per-test TSV here")
    args = parser.parse_args()

    try:
        universe, tests = load(args.reports)
    except ValueError as error:
        parser.error(str(error))
    durations = read_durations(args.timings)
    coverage = Coverage(universe, tests)
    result = {"measured_tests": len(tests), "baseline": coverage.totals()}

    if args.table:
        rows = ["test\tlines\tunique_lines\tpotential_branch_loss\tseconds"]
        for name in sorted(tests):
            lines, branch_loss = coverage.unique(name)
            rows.append(f"{name}\t{tests[name][0].bit_count()}\t{lines}\t{branch_loss}\t{durations.get(name, 0):.1f}")
        args.table.write_text("\n".join(rows) + "\n")

    if args.remove:
        wanted = [line.strip() for line in args.remove.read_text().splitlines() if line.strip()]
        unknown = [name for name in wanted if name not in tests]
        removed = 0
        for name in wanted:
            if name in tests:
                coverage.remove(name)
                removed |= tests[name][0]
        lost = sorted(universe.keys[index] for index in bits(removed) if coverage.counts[index] == 0)
        result["after_removal"] = coverage.totals()
        result["unknown_tests"] = unknown
        result["lost_lines"] = [{"assembly": key[0], "file": key[1], "line": key[2]} for key in lost]

    if args.plan:
        keep = set()
        if args.keep:
            keep = {line.strip() for line in args.keep.read_text().splitlines() if line.strip()}
        order = sorted((name for name in coverage.remaining if name not in keep),
                       key=lambda name: (coverage.unique(name)[0], -durations.get(name, 0), name))
        removable = []
        for name in order:
            if coverage.unique(name) == (0, 0):
                coverage.remove(name)
                removable.append({"test": name, "seconds": round(durations.get(name, 0), 1)})
        result["zero_loss_removable"] = removable
        result["after_plan"] = coverage.totals()

    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
