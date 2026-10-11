---
title: Construction input query measurements
type: experiment-report
status: active
updated: 2026-10-11
---

# Construction input query measurements

Project-input checks now reject an unrelated resource kind before looking up
its current availability by ID. Wood still accepts construction sources.
Matching sources retain their current ecology quantity, ownership, stock,
foot or swimming reachability and gathering-tool checks. The change adds no
cache, field, event or schema version.

This bounded repair follows a fresh measured-phase profile of main
`0c68c3dc427f90a836c953d63213498c1e53b86f`, schema 122. Wider construction,
layout and route costs remain in
[#998](https://github.com/ClankerWorldOrg/ClankerWorld/issues/998).
The [October 10 baseline](construction-current-main-baseline.md) remains
historical evidence from the earlier author of this draft.

## Workload and machine

Fresh Small geography uses `town-project-real-donation`. The Market is
Council-approved, supplied and completed through native actions. Extra
residents, households and initial stock are controlled fixtures; Houses,
workshops and Roads use native placement. Actual resident/Road/Town-building/
House counts are 4/99/24/2, 16/100/36/14 and 16/194/55/14. Fixture names retain
`4-96`, `16-96` and `16-192`. The 192-Road target avoids the separately tracked
384-Road setup failure; it does not fix that fixture.

Each ordinary run restores the same fresh checkpoint, warms a separate world
for four ticks, restores again and measures 64 blocking native ticks,
256→320. Built-in recording delegates the real deterministic chooser. The
local personal fixture uses personal-provider admission with replies from
that chooser; no hosted model is called. Every tick also captures checkpoint
and event digests after the timed call. These checks run between samples and
can affect GC timing; both versions use the identical probe. Setup, warmup,
encoding, diagnostics and file writes are outside the native-call interval.
Native allocation totals include provider recording, with diagnostics and
capture bookkeeping outside the allocation interval.

The machine is shared Debian 13 Linux, Intel Xeon Platinum 8370C, SDK 10.0.401
and .NET 10.0.12, with four effective processors and CPU quota `400000 100000`.
Timed runs are sequential, with no other local build, test or sampler.
Shared-host noise limits timing conclusions. These are authored native
simulation fixtures, not naturally evolved private saves, Windows frame
measurements or live hosted-model timings.

## Profile before choosing the repair

The largest fixture's 64 measured ticks admitted 124 decisions with no
fallback. `DiagnosticsClient.StartEventPipeSession` accepted sampling before
opening the measurement gate; setup and warmup are excluded. The retained
trace converts successfully to Speedscope. Under the native tick stack,
CreateCandidates spans 95.97% inclusive sampled thread time, Town project
proposals 43.62%, CanAcquireProjectInputs 16.62%, layout foot-cost search 15.65%,
site evaluation 13.48%, and layout-context constructors 3.22%. Nested inclusive
samples overlap and are not exact CPU measurements. Profiled native times
(711.4 ms median/1323 ms p95) are excluded from ordinary comparisons.

## Ordinary comparison

| Fixture | Provider | Main median / p95 ms | Kind first median / p95 ms | Admissions |
| --- | --- | ---: | ---: | ---: |
| 4-96 | Built-in | 114.3 / 308.5 | 113.4 / 354.4 | 10 |
| 4-96 | Local personal | 104.2 / 183.9 | 160.6 / 495.7 | 10 |
| 16-96 | Built-in | 504.9 / 777.3 | 480.3 / 644.0 | 124 |
| 16-96 | Local personal | 499.0 / 657.9 | 467.1 / 825.9 | 124 |
| 16-192 | Built-in | 548.6 / 736.9 | 532.4 / 738.7 | 124 |
| 16-192 | Local personal | 556.5 / 724.7 | 515.6 / 687.4 | 124 |

All four larger-fixture medians improve by 3.0–7.3% in this sweep. The small
personal median and several p95 values increase. The raw samples retain those
results; the sweep does not establish a uniform speedup. All 24 full ordinary
capture files match: ordered candidates and observation digests, admissions,
64 checkpoint/event-digest frames per pair and final canonical checkpoints.
Both paths admit the same choices with zero fallbacks across 768 measured
native ticks. Strict final reloads preserve each checkpoint exactly.

## State changes, rollback and reload

The [mutation probe](construction-input-query-probe/changes.cs.txt) covers
control, a moved resident, added household stock, reserved stock, depleted
wood/construction sources, household land, a removed Road and a native removed
workshop. Each case runs through built-in and local personal admission and
asserts that the affected resident was actually queried. All sixteen pairs
match complete ordered request/candidate captures and final canonical bytes:
32 capture-file comparisons. Each version also checks sixteen refused ticks
against the prior checkpoint bytes and sixteen paired save/reload continuations.

The resource and stock changes are controlled starting-state mutations; the
workshop removal uses the real native removal action. These checks preserve
current authority and availability rules; they do not assert that every
mutation changes an offered choice. No new persistent cache needs invalidation.

## Fresh-process repeats

| Fixture | Provider | Main median / p95 ms | Kind first median / p95 ms | Allocated MiB, main → kind first |
| --- | --- | ---: | ---: | ---: |
| 4-96 | Built-in | 126.5 / 306.6 | 114.5 / 658.8 | 2539.8 → 2561.4 |
| 4-96 | Local personal | 110.1 / 183.0 | 93.2 / 130.1 | 2553.4 → 2563.1 |
| 16-192 | Built-in | 607.5 / 784.0 | 583.3 / 759.9 | 15524.2 → 15585.7 |
| 16-192 | Local personal | 627.1 / 836.5 | 572.3 / 709.0 | 15523.8 → 15585.3 |

All four repeated medians improve, including the small personal case that
regressed in the first sweep. Small built-in p95 increases. Repeat allocation totals
increase by 0.4–0.85%; this repair avoids lookups and claims no memory reduction.
All sixteen repeat capture files match, with zero fallbacks across another
512 measured ticks. The four pre-change profile capture files also match the
ordinary baseline exactly. These repeats support the bounded improvement
without explaining every shared-host outlier or promising Windows latency.

The [raw report](construction-input-query-probe/samples.json) retains every
ordinary/repeat sample, fresh setup counts, source/probe/checkpoint hashes,
compiled assembly hashes and all 76 before/after or profile capture-file
comparisons. Only SettlementProjects.cs differs among the 332 Simulation and
project/toolchain inputs. Full native captures, checkpoints, trace, profile
analysis and preflight-failure notes are retained in
`.evidence/keep/998/` in the baseline and fixing worktrees of Codex session
c57361c5.

## Repeat the native comparison

The [probe sources](construction-input-query-probe/run.sh) build against the
selected checkout. Use one shared checkpoint directory; never regenerate the
inputs separately for the two versions.

```bash
DOTNET_PROCESSOR_COUNT=4 bash docs/development/construction-input-query-probe/run.sh setup /path/to/main /tmp/input-fixtures /tmp/input-main/setup
DOTNET_PROCESSOR_COUNT=4 bash docs/development/construction-input-query-probe/run.sh replay /path/to/main /tmp/input-fixtures /tmp/input-main/main-ordinary
DOTNET_PROCESSOR_COUNT=4 bash docs/development/construction-input-query-probe/run.sh replay /path/to/fixed /tmp/input-fixtures /tmp/input-fixed/fixed-ordinary
DOTNET_PROCESSOR_COUNT=4 bash docs/development/construction-input-query-probe/run.sh changes /path/to/main /tmp/input-fixtures /tmp/input-main/main-changes
DOTNET_PROCESSOR_COUNT=4 bash docs/development/construction-input-query-probe/run.sh changes /path/to/fixed /tmp/input-fixtures /tmp/input-fixed/fixed-changes
python3 docs/development/construction-input-query-probe/compare-captures.py /tmp/input-main /tmp/input-fixed main-ordinary fixed-ordinary
python3 docs/development/construction-input-query-probe/compare-captures.py /tmp/input-main /tmp/input-fixed main-changes fixed-changes
```

For the measured-phase profile, build `profile.cs.txt` against the selected
Simulation project and pass the checkpoint directory and a fresh gate prefix.
Build `collector.cs.txt` with Microsoft.Diagnostics.NETCore.Client 0.2.750501
and pass that same prefix and the trace path. The collector waits for the
ready marker and opens the go marker only after sampling starts. Convert the
completed trace with dotnet-trace 10.0.750501 and the existing
[analysis script](footprint-query-probe/analyze-profile.py).

An initial collector build missed an invariant-culture argument and was fixed
before profiling. The first mutation preflight used actor 5, which had no native
request in its first three ticks; the controls instead use actor 10, actually
queried at ticks 257–258 in the unchanged ordinary baseline. Neither preflight
failure is a game failure or an excluded performance sample.

The existing [Windows/F12 checks](../../playtest/944-grown-town-tick-times.md)
remain pending. This report does not claim a universal speedup or completion
of the broader latency investigation.
