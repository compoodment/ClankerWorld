---
title: Route step cost measurements
type: experiment-report
status: active
updated: 2026-10-09
---

# Route step cost measurements

Layout and shared route searches receive legal edges from `FootNeighbors`.
Their Road cost calculation now reuses that immediately established legality,
avoiding a second `CanFootStep` call. Public `FootStepCost` and movement callers
retain the complete validation. Terrain costs, diagonal rounding, Road discounts,
occupancy, diagonal shoulders, priority order and action-time checks are unchanged.
There is no new cache, saved field or schema change.

This bounded repair follows a fresh measured-phase profile of current main
`215ecf995496da8147e2583e34e1ba8f5803c5e0`, schema 110, before any production edit.
The [raw report](route-cost-probe/samples.json) records 308 Simulation/project/toolchain
input hashes; only the four intended production files differ. It includes every
ordinary and repeat sample, input/probe digests, complete-capture comparison manifest
and trace provenance. Larger native ticks remain above the rough 200 ms guidance in
[#998](https://github.com/compoodment/ClankerWorld/issues/998).

## Workload and machine

Fresh Small geography uses `town-project-real-donation`. The Market is approved,
supplied and completed through native Council actions. Extra residents and initial
stock are controlled; Houses, Workshops and Roads use native placement. Actual
resident/Road/Town-building/House counts are 4/100/24/2, 16/96/35/14 and
16/390/98/14. Fixture names retain `4-96`, `16-96` and `16-384`.
This is authored native growth, not a naturally evolved private save.

Each ordinary pair restores the identical fresh checkpoint, warms a separate world
for four ticks, restores again and measures 64 blocking native ticks, 256→320.
Built-in recording delegates the real deterministic chooser. The local personal
fixture uses personal-provider admission with replies from that same chooser;
no hosted model is called. All timed runs are sequential, without another local
build, test or sampler. Setup, warmup, file writes, rendering and model latency
are excluded. The separate unmodified nonblocking portable reproduction measures
469.9 ms median / 520.6 ms p95 in the middle fixture, with 67 admissions and no
fallbacks; its 102.8-second setup is excluded.

The machine is shared Debian 13 Linux, AMD EPYC 9V74, SDK 10.0.401 and runtime
10.0.12, with four effective processors and cgroup quota `400000 100000`.
Shared-host noise and first-use branches limit timing conclusions. These are
native simulation timings, not Windows frame or live hosted-model measurements.

## Ordinary timings

Values are lower median / p95 tick milliseconds. Profiled runs are excluded.

| Fixture | Provider | Main median / p95 | Reused legality median / p95 | Admissions per path |
| --- | --- | ---: | ---: | ---: |
| 4-96 | Built-in | 85.3 / 253.1 | 82.4 / 203.6 | 12 |
| 4-96 | Local personal | 76.0 / 102.6 | 73.5 / 111.8 | 12 |
| 16-96 | Built-in | 467.4 / 547.9 | 437.0 / 503.1 | 67 |
| 16-96 | Local personal | 474.7 / 547.8 | 407.7 / 519.9 | 67 |
| 16-384 | Built-in | 603.8 / 658.3 | 541.7 / 597.5 | 103 |
| 16-384 | Local personal | 605.8 / 681.0 | 592.2 / 638.9 | 103 |

All six medians improve in this sweep. Small personal p95 increases;
the raw samples retain that result. This is not a uniform latency guarantee.

## Fresh-process repeats

Small and dense checkpoints run again in fresh processes. The identical allocation
probe records process-wide `GC.GetTotalAllocatedBytes(precise: true)` around each
native tick. Provider recording is included on both sides; diagnostics and report
bookkeeping are outside the interval. Totals are cumulative allocations over 64
ticks, not retained heap, peak memory or a leak.

| Fixture | Provider | Main median / p95 | Reused legality median / p95 | Allocated MiB, main → repair |
| --- | --- | ---: | ---: | ---: |
| 4-96 | Built-in | 100.1 / 287.7 | 75.7 / 256.1 | 2404.3 → 2415.0 |
| 4-96 | Local personal | 77.6 / 124.6 | 69.4 / 85.3 | 2399.9 → 2400.9 |
| 16-384 | Built-in | 635.4 / 827.1 | 546.5 / 607.8 | 20838.3 → 20868.9 |
| 16-384 | Local personal | 606.3 / 667.4 | 573.7 / 624.3 | 20837.5 → 20868.1 |

All four repeat medians and p95 values fall in this run. Allocation totals rise
0.04–0.45%; the complete samples retain those small increases. The repair
avoids repeated step checks without demonstrating a managed-allocation reduction.

## Native equivalence and rollback

The six ordinary pairs preserve all ordered candidates, admissions and final codec
state byte for byte: 768 measured ticks, zero fallbacks. Four repeat pairs add 512
ordinary ticks and preserve those full captures. Two measured-phase profiles add
128 native ticks with the same complete captures and zero fallbacks.

The inherited mutation probe passes twelve paired variants: control, resident
movement, shared stock, household land, Road removal and native Workshop removal,
with both provider kinds. Every variant validates, refuses a prepared tick without
changing checkpoint bytes, strictly reloads, and matches the next native continuation.
Its resident 07 receives no recorded request in this short window. Movement, Road
and Workshop removal change candidates; stock and land change saved state but leave
those offered candidates unchanged. These limits are retained rather than credited
as direct stock/land eligibility checks.

A second [observed-resident probe](route-cost-probe/observed-rollback.cs.txt) repeats
all twelve pairs with resident 05 and asserts that the resident is actually queried.
It also asserts no fallback in both post-load continuation ticks. These are the only
changes from the inherited source; Road (110,46) is confirmed in a real offered
lantern candidate before removal. All five observed-resident mutations change the ordered candidate captures from
control. Both provider variants preserve those changed candidates and final states
across baseline/repair, refused-tick rollback and strict reload.
The report records all 81 complete before/after file comparisons, sizes and SHA-256.
Full checkpoints, captures, traces and logs stay in the worktrees under
`.evidence/keep/973eb221/998-current/` and `.evidence/keep/973eb221/998-after/`.
The repository retains numeric data, hashes and reproducible sources.

Existing route tests now compare the legal-step cost path against fresh searches
using public validated costs on generated terrain and wrapped/unwrapped maps, with
Road discounts and crowded diagonal corners. Existing terrain cases check literal
141/200/100/282 costs and public refusal of blocked diagonals, including the seam.
No timing-threshold test or new public test hook is added.

## Measured-phase profile

The sampler attaches after setup and warmup at a ready/go gate and samples only the
64 dense built-in ticks. Inclusive sampled thread time under native tick core is
43,316.7 ms before and 39,710.0 ms after. Layout foot-cost search spans
9,081.7 → 6,806.4 ms (20.97% → 17.14%); shared route search spans
5,017.5 → 3,863.5 ms (11.58% → 9.73%). These intervals overlap and may include
waits or GC; they cannot be added or read as exact CPU costs. Candidate creation
still spans about 96.6% of sampled tick time. The wider workload remains in #998.

## Tested-main integration

After the initial measurements, main advanced to
`33e6429ddabe6065b38971e937d1f3def0d78394` with the Restaurant borrowed-stock fix
in #1401. Its exact-main Verify 37901467122 passed all 14 jobs before it was
merged into this branch. The initial optimized measurements remain tied to
`e2f1e4ec8dd9321eaf856055171f5c7cd9bd7df5`; their source hashes describe that
measured tree rather than the later integration.

A new sequential six-pair ordinary replay uses that tested main and the merged
repair with the same schema-110 checkpoints: 768 additional native ticks, zero
fallbacks and all 18 full candidate/admission/final captures identical. The report
records both sets of samples and 308 fresh integration input hashes, with only the
same four production files differing. All updated-main capture files also match the original-main captures; this
Restaurant fix leaves the recorded fixture workload unchanged.
Together with the initial evidence, 99 complete before/after capture files agree.
The integration captures remain under `.evidence/keep/973eb221/998-integration/`
on the tested-main worktree and `.evidence/keep/973eb221/998-after/` on the repair.

| Fixture | Provider | Updated main median / p95 | Merged repair median / p95 |
| --- | --- | ---: | ---: |
| 4-96 | Built-in | 81.5 / 257.3 | 80.7 / 149.3 |
| 4-96 | Local personal | 80.9 / 97.6 | 70.8 / 83.7 |
| 16-96 | Built-in | 459.7 / 588.6 | 423.6 / 511.0 |
| 16-96 | Local personal | 456.3 / 519.0 | 459.4 / 535.0 |
| 16-384 | Built-in | 602.3 / 658.3 | 605.3 / 686.4 |
| 16-384 | Local personal | 626.0 / 712.4 | 564.0 / 623.7 |

Updated-main results are mixed. Dense built-in median is effectively flat and
p95 rises; dense personal timings improve. All samples remain in the report,
and the repair establishes no uniform native latency or Windows speed guarantee.

## Reproduce

Use the pinned SDK and a Release `net10.0` console project referencing each
worktree's Simulation project. Keep the baseline checkout unmodified and use the
same fresh checkpoints on both paths. Set `DOTNET_PROCESSOR_COUNT=4` for these runs.

| Source | Arguments |
| --- | --- |
| [Fresh setup](footprint-query-probe/setup.cs.txt) | Output report; baseline commit; `4:96,16:96,16:384`; `256`; `1`; source path; empty final argument. |
| [Ordinary replay](construction-query-probe/replay.cs.txt) | Checkpoint directory; fresh output prefix. |
| [Fresh-process repeat](household-plan-query-probe/allocation.cs.txt) | Checkpoint directory; fresh output prefix. |
| [Inherited rollback](household-plan-query-probe/rollback.cs.txt) | `start-16-96.json`; fresh output prefix. |
| [Observed rollback](route-cost-probe/observed-rollback.cs.txt) | `start-16-96.json`; fresh output prefix. |
| [Measured-phase profile](household-plan-query-probe/profile.cs.txt) | Checkpoint directory; fresh output/gate prefix. |

For profiling, wait for `<prefix>.ready`, attach `dotnet-trace` 10.0.750501 with
`--profile dotnet-sampled-thread-time --format Speedscope` to its PID, then create
`<prefix>.go` after the collector identifies its output file. Analyse using the
[existing sampled-stack script](footprint-query-probe/analyze-profile.py).
Compare full candidate/admission/final files byte for byte before interpreting
numbers. The [paired Windows/F12 check](../../playtest/998-route-step-costs.md)
remains pending.
