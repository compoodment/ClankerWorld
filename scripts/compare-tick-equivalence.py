"""Report the first native checkpoint, event, or observation difference."""
import gzip
import json
from pathlib import Path
import sys
import zlib


def first_difference(left, right, path="$"):
    if type(left) is not type(right):
        return f"{path}: {left!r} != {right!r}"
    if isinstance(left, dict):
        if left.keys() != right.keys():
            return f"{path}: object keys differ"
        for key in left:
            difference = first_difference(left[key], right[key], f"{path}.{key}")
            if difference:
                return difference
    elif isinstance(left, list):
        for index, (a, b) in enumerate(zip(left, right)):
            difference = first_difference(a, b, f"{path}[{index}]")
            if difference:
                return difference
        if len(left) != len(right):
            return f"{path}: lengths {len(left)} != {len(right)}"
    elif left != right:
        return f"{path}: {left!r} != {right!r}"
    return None


def compare(base, candidate):
    baseline = json.loads((base / "run.json").read_text())
    current = json.loads((candidate / "run.json").read_text())
    for run in (baseline, current):
        seeds, ticks, modes = run["Seeds"], run["Ticks"], run["Modes"]
        if (not isinstance(seeds, list) or not 1 <= len(seeds) <= 16 or
                any(not isinstance(seed, str) or not seed.strip() for seed in seeds) or
                type(ticks) is not int or not 1 <= ticks <= 4096 or
                not isinstance(modes, list) or not 1 <= len(modes) <= 2 or
                any(mode not in ("generated", "legacy") for mode in modes) or
                len(set(modes)) != len(modes) or not isinstance(run["Commit"], str)):
            raise ValueError("Invalid probe inputs")
    for key in ("Seeds", "Ticks", "Modes"):
        if baseline[key] != current[key]:
            raise ValueError(f"Probe inputs differ: {key}")
    frames = 0
    for mode in baseline["Modes"]:
        for ordinal, seed in enumerate(baseline["Seeds"]):
            directory = f"{mode}-{ordinal:02}"
            for tick in range(baseline["Ticks"] + 1):
                prefix = f"tick-{tick:06}"
                differences = []
                left = gzip.decompress((base / directory / (prefix + ".checkpoint.gz")).read_bytes())
                right = gzip.decompress((candidate / directory / (prefix + ".checkpoint.gz")).read_bytes())
                if left != right:
                    offset = next((i for i, (a, b) in enumerate(zip(left, right)) if a != b), min(len(left), len(right)))
                    differences.append(f"checkpoint bytes: first offset {offset}; base {len(left)} bytes, candidate {len(right)} bytes")
                for component in ("events", "digests"):
                    name = prefix + f".{component}.json"
                    a = json.loads((base / directory / name).read_text())
                    b = json.loads((candidate / directory / name).read_text())
                    difference = first_difference(a, b)
                    if difference:
                        differences.append(f"{component}: {difference}")
                if differences:
                    print(f"First difference: mode={mode}, seed={seed!r}, tick={tick}")
                    for difference in differences:
                        print(difference)
                    return 1
                frames += 1
    print(f"Equivalent: {frames} native frames; checkpoint bytes, events and observation digests match.")
    print(f"Base {baseline['Commit']}; candidate {current['Commit']}. Working-tree status is retained in each run.json.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(compare(Path(sys.argv[1]), Path(sys.argv[2])))
    except (OSError, ValueError, KeyError, IndexError, TypeError, EOFError, zlib.error) as error:
        print(f"Comparison failed: {error}", file=sys.stderr)
        sys.exit(2)
