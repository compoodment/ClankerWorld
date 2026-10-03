"""Regression checks for coverage evidence and conservative pruning decisions."""

import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import per_test_coverage as runner
import remove_tests
import unique_coverage as unique
import trx_summary


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
    def report(command, returncode=0):
        folder = Path(command[command.index("--results-directory") + 1])
        (folder / "coverage.cobertura.xml").write_text("<coverage/>")
        return SimpleNamespace(returncode=returncode, stdout="test output", stderr="")

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


class RemoveTestsTests(unittest.TestCase):
    SOURCE = """namespace Example;

public sealed partial class SampleTests
{
    /// <summary>Braces in literals and comments must not end the method early.</summary>
    [Fact]
    public void Keeps()
    {
        Assert.Equal("}", "}");
    }

    /// <summary>Removed with its comment and attribute.</summary>
    [Fact]
    public void Removed()
    {
        var text = $"{{ {1} }}" + @"}" + "\\"}"; // }
        /* } */
        Assert.Equal('}', '}');
    }

    [Fact]
    public void Expression() => Run(() =>
    {
        Step();
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Rows(int value) => Assert.True(value > 0);

    private static void Run(Action action) => action();

    private static void Step() { }
}
"""

    def test_a_removed_method_takes_its_comment_and_attributes_and_nothing_else(self):
        text = remove_tests.remove_entry(self.SOURCE, {"test": "Example.SampleTests.Removed", "decision": "delete"})
        self.assertNotIn("Removed", text)
        self.assertIn("public void Keeps()\n    {\n        Assert.Equal", text)
        self.assertIn("    }\n\n    [Fact]\n    public void Expression()", text)

    def test_an_expression_body_ends_after_its_lambda_block(self):
        text = remove_tests.remove_entry(self.SOURCE, {"test": "Example.SampleTests.Expression", "decision": "delete"})
        self.assertNotIn("Step();", text)
        self.assertIn("[Theory]", text)
        self.assertIn("private static void Step() { }", text)

    def test_rows_go_one_at_a_time(self):
        text = remove_tests.remove_entry(self.SOURCE, {"test": "Example.SampleTests.Rows", "decision": "delete_rows",
                                                       "rows": ["[InlineData(2)]"]})
        self.assertIn("[InlineData(1)]", text)
        self.assertNotIn("[InlineData(2)]", text)
        self.assertIn("public void Rows(int value)", text)

    def test_a_method_of_a_split_class_is_found_in_its_own_file(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "SampleTests.cs").write_text(self.SOURCE)
            (root / "SampleTests.More.cs").write_text(
                "public sealed partial class SampleTests\n{\n    [Fact]\n    public void Elsewhere() { }\n}\n")
            entry = {"test": "Example.SampleTests.Elsewhere", "decision": "delete", "file": str(root / "SampleTests.cs")}
            self.assertEqual(root / "SampleTests.More.cs", remove_tests.locate(entry, root))


if __name__ == "__main__":
    unittest.main()
