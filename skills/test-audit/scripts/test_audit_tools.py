"""Regression checks for coverage evidence and conservative pruning decisions."""

import contextlib
import io
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import per_test_coverage as runner
import unique_coverage as unique
import trx_summary


def coverage_xml(hits=1, branch=True):
    covered = int(hits > 0)
    branches = 2 if branch else 0
    detail = f' branch="true" condition-coverage="{covered * 50}% ({covered}/2)"' if branch else ''
    return f'''<coverage lines-covered="{covered}" lines-valid="1"
        branches-covered="{covered if branch else 0}" branches-valid="{branches}">
        <packages><package name="Example" branch-rate="{covered / 2 if branch else 0}">
        <classes><class name="Example" filename="src/Example.cs"><lines>
        <line number="1" hits="{hits}"{detail}/>
        </lines></class></classes></package></packages></coverage>'''


class TrxSummaryTests(unittest.TestCase):
    def test_skipped_rows_are_reported_but_not_counted_as_executed(self):
        with tempfile.TemporaryDirectory() as folder:
            trx = Path(folder) / "tests.trx"
            trx.write_text('''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                <TestDefinitions><UnitTest id="1"><TestMethod className="Example" name="Case"/>
                </UnitTest></TestDefinitions><Results>
                <UnitTestResult testId="1" outcome="Passed"/>
                <UnitTestResult testId="1" outcome="NotExecuted"/>
                </Results></TestRun>''')
            output = io.StringIO()
            with patch("sys.argv", ["trx_summary.py", str(trx)]), contextlib.redirect_stdout(output):
                self.assertEqual(0, trx_summary.main())
            summary = json.loads(output.getvalue())
            self.assertEqual(2, summary["reported_cases"])
            self.assertEqual(1, summary["executed_cases"])
            self.assertEqual(1, summary["declarations"])


class BranchEvidenceTests(unittest.TestCase):
    def coverage(self, counts, total=2):
        universe = unique.Universe()
        index = universe.id(("Example", "src/Example.cs", 1))
        universe.branch_totals[index] = total
        return unique.Coverage(universe, {name: (1, {index: count}) for name, count in counts.items()})

    def test_equal_partial_counts_do_not_prove_redundant_branches(self):
        # These reports fit tests that take opposite branches. A zero-loss
        # claim would silently discard independently covered behavior.
        coverage = self.coverage({"left": 1, "right": 1})
        self.assertEqual((0, 1), coverage.unique("left"))
        self.assertEqual((0, 1), coverage.unique("right"))

    def test_full_remaining_coverage_proves_branch_redundancy(self):
        coverage = self.coverage({"partial": 1, "complete": 2})
        self.assertEqual((0, 0), coverage.unique("partial"))
        coverage.remove("partial")
        self.assertEqual((1, 2), coverage.unique("complete"))

    def test_duplicate_removal_does_not_remove_another_tests_evidence(self):
        coverage = self.coverage({"first": 2, "second": 2})
        coverage.remove("first")
        coverage.remove("first")
        self.assertEqual(1, coverage.totals()["lines_covered"])
        self.assertEqual(2, coverage.totals()["branches_covered_at_least"])


class MeasurementTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        output = self.root / "bin" / "Release" / "test-target"
        output.mkdir(parents=True)
        self.assembly = output / "Example.Tests.dll"
        self.assembly.write_bytes(b"test assembly fixture")
        self.adapter = self.root / "adapter"
        self.adapter.mkdir()
        self.settings = self.root / "coverage.runsettings"
        self.settings.write_text("<RunSettings/>")
        self.methods = self.root / "methods.txt"
        self.method = "Example.Tests.Case"
        self.methods.write_text(self.method + "\n")
        self.out = self.root / "evidence"
        self.arguments = ["per_test_coverage.py", "--assembly", str(self.assembly),
                          "--settings", str(self.settings), "--methods", str(self.methods),
                          "--out", str(self.out), "--adapter-path", str(self.adapter), "--workers", "1"]

    def tearDown(self):
        self.temporary.cleanup()

    def run_measurement(self, command, arguments=None):
        with patch("sys.argv", arguments or self.arguments), patch.object(runner.subprocess, "run", command), \
                contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return runner.main()

    @staticmethod
    def report(command, returncode=0, xml=None):
        folder = Path(command[command.index("--results-directory") + 1])
        (folder / "coverage.cobertura.xml").write_text(coverage_xml() if xml is None else xml)
        return SimpleNamespace(returncode=returncode, stdout="test output", stderr="")

    def test_invalid_reports_cannot_issue_or_reuse_receipts_or_enter_a_plan(self):
        valid = coverage_xml()
        cases = {
            "empty": "<coverage/>",
            "malformed XML": "<coverage>",
            "wrong root": valid.replace("coverage ", "report ", 1).replace("</coverage>", "</report>"),
            "no lines": valid.replace('<line number="1" hits="1" branch="true" condition-coverage="50% (1/2)"/>', ''),
            "missing filename": valid.replace('filename="src/Example.cs"', ''),
            "invalid line number": valid.replace('number="1"', 'number="0"'),
            "negative hits": valid.replace('hits="1"', 'hits="-1"'),
            "noninteger hits": valid.replace('hits="1"', 'hits="one"'),
            "missing branch counts": valid.replace('condition-coverage="50% (1/2)"', ''),
            "invalid branch counts": valid.replace('50% (1/2)', '50% (3/2)'),
            "invalid branch flag": valid.replace('branch="true"', 'branch="perhaps"'),
            "nonfinite rate": valid.replace('branch-rate="0.5"', 'branch-rate="NaN"'),
            "missing aggregate": valid.replace('lines-valid="1"', ''),
            "inconsistent aggregate": valid.replace('lines-valid="1"', 'lines-valid="2"'),
            "negative aggregate": valid.replace('branches-valid="2"', 'branches-valid="-2"'),
            "too few aggregate branches": valid.replace('branches-valid="2"', 'branches-valid="1"'),
            "conflicting duplicate": valid.replace('</lines>', '<line number="1" hits="0"/></lines>'),
        }
        for name, xml in cases.items():
            with self.subTest(name=name):
                self.assertEqual(1, self.run_measurement(lambda args, **kwargs: self.report(args, xml=xml)))
                folder = self.out / runner.slug(self.method)
                self.assertFalse((folder / "completion.json").exists())
                log = (folder / "run.log").read_text()
                self.assertIn(str(folder / "coverage.cobertura.xml"), log)
                self.assertIn("rerun per_test_coverage.py", log)
                # A receipt from an older runner must not bless invalid XML.
                (folder / "completion.json").write_text(json.dumps({"method": self.method, "success": True}))
                self.assertFalse(runner.completed_measurement(folder, self.method))
                with self.assertRaisesRegex(ValueError, "Invalid.*rerun per_test_coverage.py"):
                    unique.load(self.out)

    def test_invalid_resumed_report_is_remeasured_then_resumes_normally(self):
        self.assertEqual(0, self.run_measurement(lambda args, **kwargs: self.report(args)))
        folder = self.out / runner.slug(self.method)
        (folder / "coverage.cobertura.xml").write_text("<coverage/>")
        calls = []

        def command(arguments, **options):
            calls.append(arguments)
            return self.report(arguments)

        self.assertEqual(0, self.run_measurement(command))
        self.assertEqual(0, self.run_measurement(command))
        self.assertEqual(1, len(calls))
        universe, tests = unique.load(self.out)
        self.assertEqual(1, unique.Coverage(universe, tests).totals()["lines_covered"])

    def test_conflicting_source_aliases_cannot_issue_or_reuse_receipts_or_enter_a_plan(self):
        aliases = [("/repo", "src/Example.cs", "/repo/src/Example.cs"),
                   (r"C:\repo", r"src\Example.cs", "C:/repo/src/Example.cs")]
        for source, relative, absolute in aliases:
            with self.subTest(source=source):
                xml = f'''<coverage lines-covered="1" lines-valid="2" branches-covered="0" branches-valid="0">
                    <sources><source>{source}</source></sources><packages><package name="Example"><classes>
                    <class name="A" filename="{relative}"><lines><line number="1" hits="1"/></lines></class>
                    <class name="B" filename="{absolute}"><lines><line number="1" hits="0"/></lines></class>
                    </classes></package></packages></coverage>'''
                self.assertEqual(1, self.run_measurement(lambda args, **kwargs: self.report(args, xml=xml)))
                folder = self.out / runner.slug(self.method)
                self.assertFalse((folder / "completion.json").exists())
                (folder / "completion.json").write_text(json.dumps({"method": self.method, "success": True}))
                self.assertFalse(runner.completed_measurement(folder, self.method))
                with self.assertRaisesRegex(ValueError, "conflicting duplicate source line"):
                    unique.load(self.out)
                result = subprocess.run([sys.executable, str(Path(unique.__file__)), str(self.out), "--plan"],
                                        capture_output=True, text=True)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", result.stdout)
                self.assertIn(str(folder / "coverage.cobertura.xml"), result.stderr)
                self.assertIn("rerun per_test_coverage.py", result.stderr)
                self.assertNotIn("Traceback", result.stderr)

    def test_matching_source_aliases_with_consistent_totals_remain_valid(self):
        xml = '''<coverage lines-covered="1" lines-valid="1" branches-covered="0" branches-valid="0">
            <sources><source>C:\\repo</source></sources><packages><package name="Example"><classes>
            <class name="A" filename="src\\Example.cs"><lines><line number="1" hits="1"/></lines></class>
            <class name="B" filename="C:/repo/src/Example.cs"><lines><line number="1" hits="1"/></lines></class>
            </classes></package></packages></coverage>'''
        self.assertEqual(0, self.run_measurement(lambda args, **kwargs: self.report(args, xml=xml)))
        folder = self.out / runner.slug(self.method)
        self.assertTrue(runner.completed_measurement(folder, self.method))
        universe, tests = unique.load(self.out)
        totals = unique.Coverage(universe, tests).totals()
        self.assertEqual(1, totals["lines_valid"])
        self.assertEqual(1, totals["lines_covered"])

    def test_differing_reports_are_ambiguous_even_when_each_is_valid(self):
        def command(arguments, **options):
            result = self.report(arguments)
            folder = Path(arguments[arguments.index("--results-directory") + 1]) / "another-run"
            folder.mkdir()
            (folder / "coverage.cobertura.xml").write_text(coverage_xml(hits=0))
            return result

        self.assertEqual(1, self.run_measurement(command))
        folder = self.out / runner.slug(self.method)
        self.assertFalse((folder / "completion.json").exists())
        self.assertIn("differing", (folder / "run.log").read_text())
        (folder / "completion.json").write_text(json.dumps({"method": self.method, "success": True}))
        self.assertFalse(runner.completed_measurement(folder, self.method))
        with self.assertRaisesRegex(ValueError, "found 2.*rerun"):
            unique.load(self.out)

    def test_identical_collector_attachment_copies_are_one_valid_measurement(self):
        calls = []

        def command(arguments, **options):
            calls.append(arguments)
            result = self.report(arguments)
            folder = Path(arguments[arguments.index("--results-directory") + 1])
            attachment = folder / "_host_timestamp" / "In" / "host"
            attachment.mkdir(parents=True)
            shutil.copyfile(folder / "coverage.cobertura.xml", attachment / "coverage.cobertura.xml")
            return result

        self.assertEqual(0, self.run_measurement(command))
        self.assertEqual(0, self.run_measurement(command))
        self.assertEqual(1, len(calls))
        universe, tests = unique.load(self.out)
        self.assertEqual(1, unique.Coverage(universe, tests).totals()["lines_covered"])

    def test_measured_zero_hits_branchless_and_unmapped_branches_remain_valid(self):
        cases = [coverage_xml(hits=0), coverage_xml(hits=0, branch=False),
                 coverage_xml().replace('branches-covered="1" branches-valid="2"',
                                        'branches-covered="2" branches-valid="4"'),
                 coverage_xml().replace('</lines>',
                     '<line number="1" hits="1" branch="true" condition-coverage="50% (1/2)"/></lines>'),
                 coverage_xml().replace('</packages>', '<package name="Empty"><classes/></package></packages>')]
        for xml in cases:
            with self.subTest(xml=xml):
                folder = self.out / runner.slug(self.method)
                if folder.exists():
                    (folder / "completion.json").unlink()
                self.assertEqual(0, self.run_measurement(lambda args, **kwargs: self.report(args, xml=xml)))
                self.assertTrue(runner.completed_measurement(folder, self.method))
                universe, tests = unique.load(self.out)
                totals = unique.Coverage(universe, tests).totals()
                self.assertEqual(1, totals["lines_valid"])
                self.assertEqual(int('hits="1"' in xml), totals["lines_covered"])

    def test_planner_cli_refuses_empty_report_with_actionable_error(self):
        self.out.mkdir()
        (self.out / "methods.json").write_text(json.dumps([self.method]))
        folder = self.out / runner.slug(self.method)
        folder.mkdir()
        (folder / "completion.json").write_text(json.dumps({"method": self.method, "success": True}))
        (folder / "coverage.cobertura.xml").write_text("<coverage/>")
        result = subprocess.run([sys.executable, str(Path(unique.__file__)), str(self.out), "--plan"],
                                capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual("", result.stdout)
        self.assertIn(str(folder / "coverage.cobertura.xml"), result.stderr)
        self.assertIn("rerun per_test_coverage.py", result.stderr)
        self.assertNotIn("Traceback", result.stderr)

    def test_failed_measurement_with_report_is_retried_then_success_can_resume(self):
        calls = []

        def command(arguments, **options):
            calls.append(arguments)
            return self.report(arguments, returncode=1 if len(calls) == 1 else 0)

        self.assertEqual(1, self.run_measurement(command))
        self.assertEqual(0, self.run_measurement(command))
        self.assertEqual(2, len(calls), "A failed command's report must not count as a completed measurement")
        self.assertEqual(0, self.run_measurement(command))
        self.assertEqual(2, len(calls), "A successful completed measurement should resume without rerunning")

    def test_worker_setup_failure_is_a_failed_run(self):
        with patch.object(runner.shutil, "copytree", side_effect=OSError("fixture copy failed")):
            self.assertEqual(1, self.run_measurement(lambda *args, **kwargs: self.report(args[0])))

    def test_planner_rejects_missing_pending_directory_after_setup_failure(self):
        self.assertEqual(0, self.run_measurement(lambda args, **kwargs: self.report(args)))
        self.methods.write_text(self.method + "\nExample.Tests.Pending\n")
        with patch.object(runner.shutil, "copytree", side_effect=OSError("fixture copy failed")):
            self.assertEqual(1, self.run_measurement(lambda args, **kwargs: self.report(args)))
        with self.assertRaisesRegex(ValueError, "[Ii]ncomplete"):
            unique.load(self.out)

    def test_command_start_failure_is_a_failed_run(self):
        def unavailable(*args, **kwargs):
            raise OSError("fixture command unavailable")
        self.assertEqual(1, self.run_measurement(unavailable))

    def test_zero_workers_cannot_report_success_without_running(self):
        with self.assertRaises(SystemExit) as caught:
            self.run_measurement(lambda *args, **kwargs: self.report(args[0]), self.arguments[:-1] + ["0"])
        self.assertEqual(2, caught.exception.code)

    def test_planner_refuses_an_unfinished_measurement(self):
        folder = self.out / runner.slug(self.method)
        folder.mkdir(parents=True)
        (folder / "test.txt").write_text(self.method)
        (folder / "coverage.cobertura.xml").write_text("<coverage/>")
        with self.assertRaisesRegex(ValueError, "[Ii]ncomplete|[Uu]nfinished"):
            unique.load(self.out)


if __name__ == "__main__":
    unittest.main()
