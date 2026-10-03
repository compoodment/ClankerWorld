"""Measure coverage for each test method on its own.

Runs every method listed in --methods in its own `dotnet test` process with
coverage, several at a time. The coverage collector rewrites the assemblies in
the test output folder while it runs, so each worker gets its own copy of that
folder, made next to the original so tests that look for the repository root
still find it. Each method's results go to <out>/<method>/ with a test.txt
naming it. A method whose folder already holds a Cobertura report is skipped,
so an interrupted run can be resumed.
"""

import argparse
import hashlib
import json
import os
import queue
import re
import shutil
import subprocess
import sys
import threading
from pathlib import Path


def slug(name):
    safe = re.sub(r"[^A-Za-z0-9._+-]", "_", name)
    if len(safe) <= 150:
        return safe
    return safe[:130] + "-" + hashlib.sha256(name.encode()).hexdigest()[:16]


def filter_for(name):
    # VSTest filter syntax treats these characters specially.
    return "FullyQualifiedName=" + re.sub(r"([\\()&|=!~])", r"\\\1", name)


def collector_path(assembly):
    """The coverage collector's folder, from the test project's restore data.

    `dotnet test` on a project passes this automatically; on an assembly it has
    to be given. The project is the folder above bin/ that holds obj/project.assets.json.
    """
    for folder in assembly.parents:
        assets = folder / "obj" / "project.assets.json"
        if assets.is_file():
            data = json.loads(assets.read_text())
            library = next((key for key in data.get("libraries", {}) if key.lower().startswith("coverlet.collector/")), None)
            if library is None:
                break
            for root in data.get("packageFolders", {}):
                build = Path(root) / data["libraries"][library]["path"] / "build"
                candidates = sorted(path for path in build.glob("*") if path.is_dir())
                if candidates:
                    return candidates[0]
            break
    return None


def has_report(folder):
    return folder.is_dir() and any(folder.rglob("coverage.cobertura.xml"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", required=True, type=Path, help="the built test assembly (.dll)")
    parser.add_argument("--settings", required=True, type=Path, help="coverage .runsettings")
    parser.add_argument("--methods", required=True, type=Path, help="one fully qualified method per line")
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--workers", type=int, default=max(1, min(4, os.cpu_count() or 1)))
    parser.add_argument("--timeout", type=int, default=3600, help="seconds allowed per method")
    parser.add_argument("--adapter-path", type=Path,
                        help="folder holding the coverlet collector; found from the test project if omitted")
    parser.add_argument("--cwd", type=Path, default=Path.cwd(),
                        help="where to run dotnet; it picks the SDK from global.json there, so use "
                             "the folder the baseline was measured from")
    args = parser.parse_args()

    assembly = args.assembly.resolve()
    if not assembly.is_file():
        parser.error(f"{assembly} does not exist; build the test project first")
    adapter = args.adapter_path or collector_path(assembly)
    if adapter is None or not adapter.is_dir():
        parser.error("cannot find the coverlet collector; restore the test project or pass --adapter-path")
    methods = [line.strip() for line in args.methods.read_text().splitlines() if line.strip()]
    args.out.mkdir(parents=True, exist_ok=True)
    pending = [name for name in methods if not has_report(args.out / slug(name))]
    print(f"{len(methods)} methods, {len(methods) - len(pending)} already measured, {len(pending)} to run", flush=True)

    work = queue.Queue()
    for name in pending:
        work.put(name)
    failures, lock, done = [], threading.Lock(), [0]

    def worker(index):
        copy = assembly.parent.with_name(f"{assembly.parent.name}.coverage-worker{index}")
        shutil.rmtree(copy, ignore_errors=True)
        shutil.copytree(assembly.parent, copy)
        try:
            while True:
                try:
                    name = work.get_nowait()
                except queue.Empty:
                    return
                folder = args.out / slug(name)
                shutil.rmtree(folder, ignore_errors=True)
                folder.mkdir(parents=True)
                (folder / "test.txt").write_text(name + "\n")
                command = ["dotnet", "test", str(copy / assembly.name), "--settings", str(args.settings.resolve()),
                           "--collect", "XPlat Code Coverage", "--test-adapter-path", str(adapter),
                           "--filter", filter_for(name),
                           "--results-directory", str(folder.resolve()), "--logger", "trx;LogFileName=test.trx"]
                try:
                    result = subprocess.run(command, capture_output=True, text=True, timeout=args.timeout, cwd=args.cwd)
                    ok = result.returncode == 0 and has_report(folder)
                    output = result.stdout[-2000:] + result.stderr[-2000:]
                except subprocess.TimeoutExpired:
                    ok, output = False, "timed out"
                (folder / "run.log").write_text(output)
                with lock:
                    done[0] += 1
                    if not ok:
                        failures.append(name)
                    if done[0] % 25 == 0 or not ok:
                        print(f"{done[0]}/{len(pending)}{'' if ok else ' FAILED ' + name}", flush=True)
        finally:
            shutil.rmtree(copy, ignore_errors=True)

    threads = [threading.Thread(target=worker, args=(index,)) for index in range(args.workers)]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    print(f"finished: {len(pending) - len(failures)} measured, {len(failures)} failed", flush=True)
    for name in failures:
        print(f"failed: {name}", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
