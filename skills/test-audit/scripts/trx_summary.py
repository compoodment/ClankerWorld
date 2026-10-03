"""Count and time the tests in one or more TRX files.

Prints executed cases (each theory row is one case), declarations (test
methods), outcomes, and seconds per method. With --methods, prints one
fully qualified method name per line instead, for per-test coverage runs.
"""

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def seconds(duration):
    match = re.fullmatch(r"(\d+):(\d+):(\d+(?:\.\d+)?)", duration or "")
    return int(match[1]) * 3600 + int(match[2]) * 60 + float(match[3]) if match else 0.0


def read(paths):
    methods, outcomes, cases = {}, Counter(), defaultdict(int)
    durations = defaultdict(float)
    for path in paths:
        root = ET.parse(path).getroot()
        for test in root.iterfind(".//t:UnitTest", NS):
            method = test.find("t:TestMethod", NS)
            if method is not None:
                methods[test.attrib["id"]] = f"{method.attrib['className']}.{method.attrib['name']}"
        for result in root.iterfind(".//t:UnitTestResult", NS):
            name = methods.get(result.attrib.get("testId"))
            if name is None:
                continue
            outcomes[result.attrib.get("outcome", "Unknown")] += 1
            cases[name] += 1
            durations[name] += seconds(result.attrib.get("duration"))
    return cases, durations, outcomes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trx", nargs="+", type=Path)
    parser.add_argument("--methods", action="store_true", help="print method names only")
    parser.add_argument("--top", type=int, default=10, help="slowest methods to list")
    args = parser.parse_args()
    files = [file for path in args.trx for file in ([path] if path.is_file() else sorted(path.rglob("*.trx")))]
    if not files:
        parser.error("no TRX files found")
    cases, durations, outcomes = read(files)
    if args.methods:
        sys.stdout.write("".join(f"{name}\n" for name in sorted(cases)))
        return 0
    slowest = sorted(durations.items(), key=lambda item: -item[1])[: args.top]
    print(json.dumps({
        "files": [str(file) for file in files],
        "executed_cases": sum(cases.values()),
        "declarations": len(cases),
        "outcomes": dict(outcomes),
        "total_seconds": round(sum(durations.values()), 1),
        "slowest": [{"method": name, "seconds": round(value, 1), "cases": cases[name]} for name, value in slowest],
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
