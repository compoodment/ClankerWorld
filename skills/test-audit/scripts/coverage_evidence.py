"""Validate the evidence behind one per-test coverage measurement."""

import math
import re
import xml.etree.ElementTree as ET


def count(element, name, minimum=0):
    value = int(element.attrib[name])
    if value < minimum:
        raise ValueError(f"{name} must be at least {minimum}")
    return value


def validate_report(path):
    """Return measured XML, including valid reports whose lines all have zero hits.

    Collector branch totals can include branches with no source line. They
    must cover the mapped branches, but need not equal their sum. Full-suite
    scope and coverage comparisons still belong to compare_coverage.py.
    """
    try:
        root = ET.parse(path).getroot()
        if root.tag != "coverage":
            raise ValueError("expected a Cobertura coverage root")
        totals = {name: count(root, name) for name in
                  ("lines-covered", "lines-valid", "branches-covered", "branches-valid")}
        for dimension in ("lines", "branches"):
            if totals[f"{dimension}-covered"] > totals[f"{dimension}-valid"]:
                raise ValueError(f"covered {dimension} exceed valid {dimension}")

        lines, packages = {}, set()
        for package in root.findall("./packages/package"):
            name = package.attrib["name"]
            if not name.strip() or name in packages:
                raise ValueError("missing or duplicate package name")
            packages.add(name)
            for element in (root, package, *package.findall("./classes/class")):
                for attribute in ("line-rate", "branch-rate"):
                    if attribute in element.attrib:
                        rate = float(element.attrib[attribute])
                        if not math.isfinite(rate) or not 0 <= rate <= 1:
                            raise ValueError(f"invalid {attribute}")
            for cls in package.findall("./classes/class"):
                filename = cls.attrib["filename"]
                if not filename.strip():
                    raise ValueError("missing source filename")
                for line in cls.findall("./lines/line"):
                    key = (name, filename, count(line, "number", minimum=1))
                    hits = count(line, "hits")
                    branch = line.get("branch", "false").lower()
                    if branch not in ("true", "false"):
                        raise ValueError(f"invalid branch flag for {key}")
                    covered, valid = 0, 0
                    if branch == "true":
                        metadata = re.fullmatch(r"(\d+(?:\.\d+)?)%\s*\((\d+)/(\d+)\)",
                                                line.get("condition-coverage", ""))
                        if metadata is None or not 0 <= float(metadata[1]) <= 100:
                            raise ValueError(f"missing or invalid branch metadata for {key}")
                        covered, valid = int(metadata[2]), int(metadata[3])
                        if valid == 0 or covered > valid or hits == 0 and covered > 0:
                            raise ValueError(f"invalid branch counts for {key}")
                    elif "condition-coverage" in line.attrib:
                        raise ValueError(f"branch metadata without a branch for {key}")
                    data = (hits > 0, covered, valid)
                    if key in lines and lines[key] != data:
                        raise ValueError(f"conflicting duplicate source line {key}")
                    lines[key] = data
        if not lines:
            raise ValueError("coverage report has no measured source lines")
        if totals["lines-valid"] != len(lines) or totals["lines-covered"] != sum(hit for hit, _, _ in lines.values()):
            raise ValueError("aggregate line counters disagree with measured source lines")
        if totals["branches-covered"] < sum(covered for _, covered, _ in lines.values()) or \
                totals["branches-valid"] < sum(valid for _, _, valid in lines.values()):
            raise ValueError("aggregate branch counters are smaller than mapped branches")
        return root
    except (OSError, ET.ParseError, KeyError, ValueError) as error:
        raise ValueError(f"Invalid per-test coverage report: {path}; {error}; rerun per_test_coverage.py") from error


def measurement_report(folder):
    reports = sorted(folder.rglob("coverage.cobertura.xml"))
    if not reports:
        raise ValueError(f"Invalid per-test measurement: {folder}; no coverage.cobertura.xml; "
                         "rerun per_test_coverage.py")
    if len(reports) > 1:
        try:
            measured = reports[0].read_bytes()
            if any(report.read_bytes() != measured for report in reports[1:]):
                raise ValueError(f"Invalid per-test measurement: {folder}; found {len(reports)} differing "
                                 "coverage.cobertura.xml reports; rerun per_test_coverage.py")
        except OSError as error:
            raise ValueError(f"Invalid per-test measurement: {folder}; {error}; rerun per_test_coverage.py") from error
    # VSTest can copy the collector attachment into its TRX results directory.
    # Byte-identical copies are one measurement; differing reports are ambiguous.
    return validate_report(reports[0])
