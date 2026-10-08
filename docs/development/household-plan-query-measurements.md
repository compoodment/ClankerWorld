---
title: Household plan query measurements
type: performance-report
status: complete
updated: 2026-10-08
---

# Household plan query measurements

Household planning now computes a proposed building's identity once per design,
before scanning placed buildings for it. Previously the scan recomputed the same
identity for every building, including name hashing and reserved-identity checks.
This is a bounded follow-up to [#998](https://github.com/compoodment/ClankerWorld/issues/998).
The wider construction, layout and input-query cost remains above the experiment's
rough guidance and needs fresh profiling after this change merges.

## Source and workload

Baseline is current main `621899dcc50974c367a039f331f93dac4e1ca757`, schema 100.
Only `SettlementHouseholdPlanning.cs` differs among the 297 runtime inputs whose
hashes are recorded in [the raw data](household-plan-query-probe/samples.json).
Every synchronous query still obtains its identity from the current actor,
design and reservation state. Candidate eligibility, ranking and action-time
checks use their existing native paths.

Fresh inputs come from the committed [construction probes](construction-query-measurements.md#repeat-the-comparison).
Generated Small geography uses `town-project-real-donation`; the Market is
Council-approved, supplied and completed through native actions. Additional
households and starting stock are controlled, with native House/workshop/Road
placement. These are authored growth fixtures, not naturally evolved saves.

Actual resident/Road/Town-building/House counts are 4/100/24/2,
16/96/35/14 and 16/390/98/14. Historical case names remain `4-96`, `16-96`
and `16-384`. Every path restores the same input, warms a separate world for
four ticks, and measures 64 native ticks from 256 to 320. Setup and warmup
are excluded. Built-in recording delegates the real deterministic provider;
the local personal fixture returns candidate-bound replies from that chooser
with the personal-provider kind. Both use normal native admission, without
contacting a hosted model. Matched replays call `AdvanceOneTickAsync`; the
standalone reproduction uses the host's nonblocking tick method.

Runs are sequential on shared Debian 13, Intel Xeon Platinum 8370C,
four effective processors/cgroup quota `400000 100000`, SDK 10.0.401 and
runtime 10.0.12. The original nonblocking reproduction and all matched replay
samples, machine metadata, source/probe/input digests and results are retained
in the data file. Rendering, save-file writes and remote model latency are
excluded. Windows gameplay remains unverified.

## Ordinary timings

Values are the probe's lower median / p95 tick milliseconds. Profiled runs
are excluded.

| Fixture | Provider | Main median / p95 | Identity repair median / p95 | Admissions per path |
| --- | --- | ---: | ---: | ---: |
| 4-96 | Built-in | 162.6 / 285.7 | 104.4 / 377.0 | 12 |
| 4-96 | Local personal | 105.3 / 196.6 | 95.4 / 113.4 | 12 |
| 16-96 | Built-in | 431.2 / 550.6 | 401.3 / 557.0 | 178 |
| 16-96 | Local personal | 427.1 / 559.7 | 410.7 / 557.0 | 178 |
| 16-384 | Built-in | 524.4 / 615.1 | 470.0 / 574.9 | 87 |
| 16-384 | Local personal | 486.7 / 606.1 | 461.3 / 569.4 | 87 |

Largest medians improve about 10.4% built-in and 5.2% local personal in this
run. The middle fixture improves less, and its built-in p95 rises slightly.
The first small built-in p95 also rises. A lower median does not establish
uniformly faster ticks.

## Fresh-process repeats and allocations

The same small and dense inputs run again in fresh processes. The added
allocation probe records process-wide managed allocation deltas around each
native tick call. Totals cover all 64 measured ticks, including the same
provider recording and diagnostic readouts in both variants; they are not
peak live memory or player save sizes.

| Fixture | Provider | Main median / p95 | Repair median / p95 | Total allocated MiB, main → repair |
| --- | --- | ---: | ---: | ---: |
| 4-96 | Built-in | 143.8 / 352.4 | 104.3 / 299.0 | 3003.6 → 2925.6 |
| 4-96 | Local personal | 92.9 / 115.8 | 89.0 / 104.8 | 2996.9 → 2912.8 |
| 16-384 | Built-in | 516.0 / 620.5 | 454.5 / 565.4 | 17738.3 → 16401.4 |
| 16-384 | Local personal | 496.4 / 589.8 | 463.3 / 564.5 | 17737.4 → 16400.6 |

Both dense repeats retain lower medians and allocate about 7.5% fewer managed
bytes. The small repeats allocate about 2.6–2.8% less, and their p95s fall,
although the first small sweep's p95 rose. All original and repeat samples
remain in the data; shared-host noise and first-use branches limit conclusions
about any single sample or Windows play.

## Native equivalence and rollback

All six ordinary pairs retain complete byte-identical ordered candidate lists,
admissions and final codec state, with zero fallbacks across 768 measured ticks.
Four repeat pairs add 512 measured ticks and preserve those captures too.
Twelve state-change pairs cover both provider kinds under a control, resident
movement, shared stock, household land, Road removal and actual public Workshop
removal. Strict reload and a paired subsequent tick agree. The chosen Workshop
removal changes state but leaves offered candidates unchanged in this fixture.

A separate twelve-pair extension also refuses an actually prepared native tick
at the commit callback, checks that checkpoint bytes remain unchanged, then
strictly reloads and compares the subsequent native continuation. All extension
candidate and final captures match. Both sampled profile paths retain the same
87 admissions and full candidate/admission/final captures as their ordinary
counterparts. In total 81 full before/after capture files are compared, including
three from the profiled pair. Full synthetic inputs and captures, original logs,
TRX and trace files are retained in `.evidence/keep/current-layout-998/` locally.

No new implementation-count or timing-threshold unit test is added. Existing
native building/rebuilding, reserved identity, construction, layout and replay
checks protect those contracts; these matched probes independently check the
exact offered choices and effects of the optimization.

## Measured-phase profile

The sampler attaches after fixture construction and warmup, at the probe's
ready/go gate, and samples only 64 dense built-in ticks. Inclusive sampled
tick-thread time is 36,429.0 ms on main and 40,346.0 ms in the optimized capture.
`BuildInstanceId` accounts for 1,801.3 ms (4.95%) before and 88.4 ms (0.22%) after.
Methods overlap and may include waits/GC: these numbers are neither additive
nor exact CPU savings. Profiled timing samples are excluded from ordinary
comparisons, and the higher overall profiled thread time is retained. The
independent ordinary repeats and allocation deltas support the bounded change;
the profiles identify the repeated identity work and its reduction.

## Repeat the comparison

Use the existing `construction-query-probe/run.sh` against a clean baseline and
the optimized checkout. Run setup once on baseline to create fresh same-schema
inputs, then replay and changes separately on each tree. Run each measurement
alone, without another local build, test or sampler. The [construction comparison
instructions](construction-query-measurements.md#repeat-the-comparison) describe
those commands and how to compare full captures.

For the additional runs, create a temporary net10.0 console project referencing
the selected checkout's Simulation project and copy one of these committed
sources to `Program.cs`:

| Source | Arguments, in order |
| --- | --- |
| [Allocation repeat](household-plan-query-probe/allocation.cs.txt) | Checkpoint directory; fresh output prefix. |
| [Rollback extension](household-plan-query-probe/rollback.cs.txt) | `start-16-96.json`; fresh output prefix. |
| [Measured-phase profile](household-plan-query-probe/profile.cs.txt) | Checkpoint directory; fresh output/gate prefix. |

For the profile, wait for `<prefix>.ready`, attach `dotnet-trace` 10.0.745401
with `--profile dotnet-sampled-thread-time --format Speedscope` to its recorded
PID, and create `<prefix>.go` only after the collector reports its output file.
Compare ordinary capture files byte for byte before interpreting timings.
The saved report records both the unchanged public probes' digests and the
three exact extension sources' digests.

The [paired Windows/F12 check](../../playtest/998-household-plan-query.md) remains
pending. These Linux measurements do not establish Windows frame presentation,
a hosted-model speedup or completion of the wider performance issue.
