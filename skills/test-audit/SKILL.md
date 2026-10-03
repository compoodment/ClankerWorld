---
name: test-audit
description: Audit, prune or add ClankerWorld xUnit tests using evidence of independent behavior and measured coverage. Use for test-quality reviews, requests to remove redundant or low-value tests (including a numeric target such as "remove a third of the tests"), coverage comparisons, and deciding whether a new test earns its place.
---

# Test audit

Adapted from OpenClaw's [test-audit](https://github.com/openclaw/openclaw/blob/80930af448ebabc84174146b56bc106d37fab3b4/.agents/skills/test-audit/SKILL.md) skill and its campaign workflow. ClankerWorld's own rules come first: nothing here overrides `AGENTS.md`, `CONTRIBUTING.md` or an instruction from the owner.

This file deliberately names no versions, test counts, assemblies or slow tests. They change every week. Discover them from the repository each time, and never reuse numbers from an earlier session or an earlier audit.

## 1. Orient

- Read `AGENTS.md`, `CONTRIBUTING.md` and `docs/development/build-and-test.md`. Read the documents that state the contracts tests protect, starting with the core rules in `docs/development/how-it-works.md`, `docs/development/saves-and-replay.md` and `docs/development/device-pairing.md`.
- Check the working tree, open pull requests and recent merges. A removal conflicts with an open pull request only where both change the same lines, so don't rule out whole files; check each removal before pushing (section 6).
- Work in an isolated checkout, such as a `git worktree`, and record its base commit. Keep reports, ledgers and scratch output outside the repository diff.
- Discover the current setup:
  - the SDK, from `global.json`;
  - the test projects, under `tests/`;
  - the production projects, under `src/`;
  - the production source each test project links, from its `<Compile Include>` items;
  - the CI gates, in `.github/workflows/`;
  - per-test timings, from CI artifacts or a local TRX file.

  If the local SDK differs from `global.json`, use the same one for every measurement and say so in the report.

## 2. Measure the baseline

Run commands from the repository root. Restore with `dotnet restore --locked-mode`, build in Release, then run the full suite once with coverage and a TRX log. Never edit a checkout while it builds, tests or collects coverage.

```bash
dotnet test tests/<TestProject>/<TestProject>.csproj --configuration Release --no-build \
  --settings skills/test-audit/references/coverage.runsettings --collect "XPlat Code Coverage" \
  --results-directory <evidence>/baseline --logger "trx;LogFileName=tests.trx"
```

[coverage.runsettings](references/coverage.runsettings) measures every `ClankerWorld.*` assembly. It excludes test code by path, so all that remains of a test assembly is the production source it links, and it excludes the test SDK's generated entry point. Use the same settings, build configuration and operating system for every run, and never narrow them to improve a result.

- Investigate any baseline failure separately. Deleting a failing test is not a product fix.
- Count with `python skills/test-audit/scripts/trx_summary.py <evidence>/baseline`. It reports executed cases (each theory row is one case), declarations (test methods), outcomes and the slowest methods. The Godot headless smoke checks are separate from this count.
- For a numeric target, state the denominator, which is normally the executed cases at the base commit, and the rounded target before removing anything. Disabling a test, skipping it, filtering it out of discovery, merging cases into one test or moving tests elsewhere does not count as removal.

## 3. Find which tests add unique coverage

Whole-suite coverage can't show which tests are redundant. Measure each test method alone:

```bash
python skills/test-audit/scripts/trx_summary.py <evidence>/baseline --methods > <evidence>/methods.txt
python skills/test-audit/scripts/per_test_coverage.py --assembly tests/<TestProject>/bin/Release/<tfm>/<TestProject>.dll \
  --settings skills/test-audit/references/coverage.runsettings --methods <evidence>/methods.txt --out <evidence>/per-test
python skills/test-audit/scripts/unique_coverage.py <evidence>/per-test --timings <evidence>/baseline \
  --table <evidence>/unique.tsv --plan --keep <evidence>/keep.txt
```

- `per_test_coverage.py` runs each method in its own process, several at a time. Each worker gets its own copy of the test output folder, because the coverage collector rewrites assemblies while it runs. It finds the coverage collector from the test project's restore data, and can resume after an interruption. Only measurements with a successful completion record are reused; failed or unfinished measurements are rerun. Use a new evidence folder whenever the build or settings change, and resume only with the same inputs. Older reports without completion records must be measured again. It takes roughly the suite's own time plus several seconds of start-up per method, so start it early and read code while it runs. `dotnet` picks its SDK from the `global.json` in the folder it runs in: if the baseline ran from elsewhere, pass that folder as `--cwd`.
- `unique_coverage.py` reports each test's covered and unique lines. With `--plan`, it lists tests that can go one after another without losing any line or mapped branch, fewest unique lines first and the slowest first among equals. With `--remove FILE`, it simulates removing a chosen list. Branch unions are reported as a lower bound. The removal estimate is a conservative upper bound on mapped branches that could be lost: equal partial branch counts do not establish that two tests cover the same branches. Coverlet also counts unmapped branches that this planner cannot attribute to individual tests; the full-suite comparison below must verify total branch coverage. The planner refuses incomplete per-test measurements.
- Zero unique coverage means removing that test alone loses no coverage. It does not make the test useless: it may assert behavior that other tests only run through. The plan is a list of candidates to read, not a list to delete.
- Theory rows share one method's report. Judge rows by reading them.
- A test skipped on this operating system, such as one that needs native Windows, shows no coverage here. List it in `--keep` rather than reading it as redundant.

## 4. Decide what earns a place

**Keep independent proof of every contract the documentation names.** Examples include deterministic ticks and replay, terrain wrapping, model output treated as untrusted, server authority, rejection of bad signatures and replayed requests, pairing expiry, secret redaction and protected Windows credentials, save generation, rollback, corruption and deletion recovery, provider accounting, and client cancellation and reconnection. Check the documents for the current list rather than relying on this one.

- Check `docs/development/saves-and-replay.md` for which save compatibility is currently promised. Tests for compatibility the project no longer promises are candidates. Integrity of the current format, and refusing and preserving a save that can't load, stay.
- Separate contracts need separate proof even when they look alike, such as installation credentials and display preferences.
- Static guards can be valid tests: content IDs, scene paths and their UID companions, linked-source contracts, documentation links, dependency boundaries and export licences.
- Slow or static alone never makes a test dispensable. Among equally redundant tests, though, removing the slowest helps most, because every merge waits for the slowest CI job.

**Common candidates:**
- repeated happy paths that a richer integration scenario already exercises;
- outcomes a mock supplies rather than the code under test;
- expected values computed by the subject itself;
- brittle checks of private structure;
- theory rows that exercise the same branch with equivalent values.

Judge what the assertions actually detect, not what the name suggests. Prefer a remaining real HTTP or runtime scenario only when it exercises the same input, effect and failure mode.

Audit by production ownership, so parallel reviewers read distinct lanes: world, kernel, cognition and content in Simulation; the playtest runtime and settlement; Viewer HTTP, control, persistence and providers; the Godot protocol, pairing, client state and UI; documentation and architecture. Read whole test bodies and theory data, the owning implementation and its callers, overlapping suites, CI filters and the relevant Git history before choosing a removal.

**Running the review with several reviewers:**
- Find each candidate's file from its method declaration, not its class name: a class split across files (`partial`) puts its tests in several files. `remove_tests.py` locates them this way.
- Give each reviewer its candidates together with the kept tests that overlap each one most, by the share of the candidate's covered lines they also cover. The per-test reports hold this.
- Reviewers only read. Don't edit the checkout they're reading; try removals in a separate worktree.
- A reviewer never names another candidate as retained proof, because that one may go too. Once every ledger is in, reconcile: a test kept only because its proof was a candidate in another lane can go if that other test was kept.
- Expect reviewers to keep a large share of the candidates. Zero unique coverage often still hides a distinct assertion, refusal or edge case.

## 5. Keep a ledger

Before deleting anything, write one ledger entry per candidate:

- path, test name and exact rows;
- the regression it detects, if any;
- the production code it reaches;
- the retained test that proves the same thing, by name;
- why it was added, from Git history;
- test support or seams its removal frees, or none;
- risk, and the focused command that checks the retained proof.

Mark each entry retain, repair, consolidate or delete. Cover every file in scope, and get a second reader on decisions that cross ownership lanes. Keep the full ledger outside the repository diff, and put its summary in the pull request.

## 6. Remove, then verify

- Remove evidence-backed candidates in coherent batches, one ownership lane at a time. Move any distinct assertion into the test that stays before deleting its duplicate. `python skills/test-audit/scripts/remove_tests.py <ledger.json>` removes the methods and theory rows a ledger marks `delete` or `delete_rows`, with their attributes and doc comments.
- Delete test support that becomes unused. Change a production seam only when its callers are proven dead and focused checks pass. Never add production changes just to reach a quota. To find what the removals freed:
  - Add a temporary `.editorconfig` beside the test project that raises IDE0051, IDE0052, IDE0060 and IDE0005 to warnings.
  - Build before and after with `-p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=false` and `--no-incremental`.
  - Clean up only warnings that are new. IDE0005 reports one warning per file however many `using` lines are unnecessary, so repeat until no new warning appears.
  - Unused private nested types aren't reported, so search for any helper class the reviewers named. Delete test files left without tests.
  - Remove the temporary `.editorconfig`.
- Before pushing, check the removals against every open pull request that changes tests. Commit them, then run `git merge-tree --write-tree --name-only HEAD <pr-head>` and the same against the commit before the removals. A file that conflicts only in the first holds a removal that conflicts. Put back those removals, trying them one at a time so the rest can stay. Usually that pull request is changing the test itself, so list the removal in a follow-up issue blocked by it, rather than deleting a test someone is editing.
- Never weaken assertions, coverage settings, CI routing or a supported contract to meet a number. If the evidence can't support the requested count, stop and report the measured limit.
- Run the retained sibling tests, then the full Release suite with coverage into `<evidence>/final`, then compare:

  ```bash
  python skills/test-audit/scripts/compare_coverage.py <evidence>/baseline/<run>/coverage.cobertura.xml \
    <evidence>/final/<run>/coverage.cobertura.xml --repo <checkout>
  ```

  The comparator refuses empty reports, malformed counts, test assemblies that report anything other than their linked source, and a changed set of files, lines or branches. It gates total line and branch coverage; `--threshold-scope each` gates every assembly as well. Read "within N%" as at most N percentage points unless told otherwise, and report the relative change too. Read every lost line and branch, not just the totals. Unchanged coverage is necessary evidence, not proof that every behavioral assertion survives.
- For each lost line, look up in the per-test reports which tests reached it. If a kept test reaches it on its own, the difference is run-to-run variation in a timing-dependent test, and the report should say so. Otherwise restore the removed test that reached it, or explain why losing it is acceptable.
- Use the collector's summary counters for total branches: Coverlet counts some branches with no mapped source line, and the assembly branch rates in Cobertura are rounded. Don't substitute a sum of visible branch fractions.
- If the comparator reports a scope change between two builds of identical production code, generated source such as logging is usually ordering differently. Confirm the production and configuration trees are identical, copy the same production DLL and PDB bytes into both test output folders, and repeat with `--no-build` every run whose production bytes differ. For example, build the baseline's tests in a worktree at the base commit, copy in the final build's production DLLs and PDBs, and re-run the baseline. Record the hashes. Never bypass the scope check.
- If instrumentation makes an unmodified test fail on timing, keep the test and investigate. Repeat both full runs with the same documented scheduling, such as `-- xUnit.MaxParallelThreads=4`, rather than dropping cases or relaxing assertions.
- Where it's unclear whether a retained test really covers a removed one, make a temporary production mutation that the removed test caught. Check that a retained test fails too, then restore the source byte for byte.
- Run `dotnet format --verify-no-changes --no-restore`, `git diff --check`, and the repository's native Windows and Godot smoke and export gates that apply. Have a reviewer who did not choose the deletions compare them with the retained proof.

## 7. Report

Report:

- cases and declarations before and after;
- removed categories, and the contracts that still have proof;
- coverage per production assembly and in total, with lost lines;
- lines of test, support and production code removed;
- checks actually run, and any that were not;
- pull request and merge state.

Follow the repository's current review and merge rules. Running an audit does not authorize a release or operational changes.

## Adding or changing a test

Before writing a test, name the externally observable result it checks, a plausible regression it would catch, the proof the suite is missing today, and the production boundary that exercises it. Avoid test-only public hooks when a real boundary works. A bug's regression test should fail with the bug present. Keep each test quick, and start it as close as possible to the moment it checks.

## Changing this skill

Follow [skills/README.md](../README.md). The scripts have regression checks: run `python -m unittest test_audit_tools` in `skills/test-audit/scripts`, and add a check when you change a script's behavior.
