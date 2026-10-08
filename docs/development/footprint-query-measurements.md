---
title: Footprint query measurements
type: performance-report
status: complete
updated: 2026-10-08
---

# Footprint query measurements

Construction-site evaluation rejects an illegal origin before allocating its
full footprint. The origin belongs to every rectangular footprint; custom
shapes that omit it keep their earlier evaluation path. Permitted Market Roads
and protected tiles retain the same checks. This bounded follow-up reduces
managed allocations in the measured workload. It does not complete
[#998](https://github.com/compoodment/ClankerWorld/issues/998): the wider layout,
route and input-query costs remain above its rough 200 ms guidance.

## Source and workload

Baseline is main `14715cc3e04ef69cf8e486e09b4df2820b78ab06`, schema 101.
Only `TownLayoutService.cs` differs among 298 recorded runtime inputs.
[Raw samples and metadata](footprint-query-probe/samples.json) retain the exact
before/after source hashes, probe and checkpoint hashes, ordinary samples,
allocation deltas, profile samples and capture comparison manifest. The measured
optimized sources were uncommitted changes on that baseline; their hashes
identify the actual measured code. No persistent cache or saved field is added.

Fresh generated Small geography uses `town-project-real-donation`. Its Market
is Council-approved, supplied and constructed through native actions. Additional
households and starting stock are controlled; Houses, workshops and Roads use
native owner placement. Actual resident/Road/Town-building/House counts are
4/100/24/2, 16/96/35/14 and 16/390/98/14. Historical fixture names remain
`4-96`, `16-96` and `16-384`. This is authored growth, not a naturally evolved
private save.

Each path restores the same fresh checkpoint, warms a separate world for four
ticks, restores again, and measures 64 native ticks from 256 to 320. The built-in
recorder delegates the real deterministic provider. The local personal fixture
uses the personal-provider kind with candidate-bound replies from that same
chooser, exercising native admission without a hosted model call. Matched runs
use `AdvanceOneTickAsync`; the separate unmodified nonblocking reproduction
measured a 391.2 ms median and 505.9 ms p95 for the middle fixture.

All timed runs are sequential, without another local build, test or sampler.
Machine metadata records shared Debian 13 Linux, Intel Xeon Platinum 8370C,
four effective processors/cgroup quota `400000 100000`, SDK 10.0.401 and
runtime 10.0.12. Setup, warmup, rendering, save-file writes and hosted-model
latency are excluded. Shared-host noise and first-use branches limit timing
conclusions. No Windows gameplay or frame-rate improvement is established.

## Ordinary timings

Values are lower median / p95 tick milliseconds. Profiled runs are excluded.

| Fixture | Provider | Main median / p95 | Early refusal median / p95 | Admissions per path |
| --- | --- | ---: | ---: | ---: |
| 4-96 | Built-in | 108.7 / 375.6 | 160.9 / 330.0 | 12 |
| 4-96 | Local personal | 87.5 / 110.7 | 96.1 / 117.1 | 12 |
| 16-96 | Built-in | 405.3 / 523.5 | 409.2 / 559.2 | 178 |
| 16-96 | Local personal | 419.6 / 539.9 | 443.0 / 567.9 | 178 |
| 16-384 | Built-in | 449.0 / 567.9 | 472.4 / 924.1 | 87 |
| 16-384 | Local personal | 449.2 / 554.7 | 450.3 / 542.2 | 87 |

This first sweep establishes no overall timing improvement. Several medians
rise, and dense built-in p95 rises substantially. Those samples are retained.

## Fresh-process repeats and allocations

The small and dense checkpoints run again in fresh processes. Around each
tick call, the identical probe records process-wide managed allocation deltas
with `GC.GetTotalAllocatedBytes(precise: true)`. Provider recording is included
in both variants; diagnostic export and report bookkeeping happen outside the
allocation interval. Totals cover 64 ticks and describe cumulative allocations,
not retained heap size, peak memory or a leak.

| Fixture | Provider | Main median / p95 | Early refusal median / p95 | Allocated MiB, main → early refusal |
| --- | --- | ---: | ---: | ---: |
| 4-96 | Built-in | 108.8 / 269.7 | 103.9 / 399.3 | 2975.7 → 2776.5 |
| 4-96 | Local personal | 93.3 / 116.6 | 86.4 / 104.6 | 2956.2 → 2799.1 |
| 16-384 | Built-in | 467.5 / 558.8 | 448.0 / 529.5 | 16422.6 → 14603.0 |
| 16-384 | Local personal | 457.3 / 578.6 | 421.6 / 525.4 | 16421.7 → 14602.1 |

Managed allocations fall about 5.3–6.7% in the small fixture and 11.1% in both
dense paths. Repeat medians improve, while small built-in p95 still increases.
These repeats and the mixed first sweep support an allocation reduction, not
a uniform tick-speed promise.

## Native equivalence and rollback

Six ordinary pairs preserve complete ordered candidate lists, admissions and
final codec state byte for byte: 768 measured ticks and zero fallbacks. Four
repeat pairs add 512 ticks and preserve those full captures too. Twelve fresh
state-change pairs cover both provider kinds under a control, resident movement,
shared stock, household land, Road removal and native public Workshop removal.
Every variant validates, refuses a prepared native tick without changing its
checkpoint bytes, strictly reloads, and compares the next native continuation.
Workshop removal changes state but leaves the offered candidates unchanged
in this fixture.

The dense measured-phase profiles preserve their complete candidate, admission
and final-state captures too. In total, 57 full before/after files agree; the
manifest records each size and SHA-256. Full synthetic checkpoints, captures,
logs, TRX and traces remain in `.evidence/keep/current-layout-998-oct8/` locally.
The repository retains numeric data, hashes and runnable sources; the large
checkpoint/capture files and traces are not committed.

A focused public-layout case checks a custom footprint that omits the blocked
origin, refusal when its covered tile is occupied, a permitted Road origin,
and refusal when that Road is protected. Existing full Market footprint and
aisle checks protect their exceptions. Native stone and hanging lantern checks
also pass. There is no timing-threshold assertion or new public test hook.

## Measured-phase profile

The sampler attaches after setup and warmup, at a ready/go gate, and samples
only 64 dense built-in ticks. Inclusive sampled thread time under the native
tick core is 40,775.3 ms on main and 35,493.7 ms after early refusal.
`TownLayoutService.TryEvaluate` spans 5,569.9 ms (13.66%) before and
3,419.7 ms (9.63%) after. `CreateCandidates` remains about 94% of sampled
tick time. These intervals overlap and may include waits/GC; they are not
additive or exact CPU savings. Profiled tick medians are 612.0 and 539.7 ms,
and are excluded from ordinary timing comparisons.

## Repeat the comparison

Use clean worktrees at the baseline and this PR. The exact fresh setup source
is [archived here](footprint-query-probe/setup.cs.txt); its invariant numeric
parsing allows normal repository analyzers. Create a temporary net10.0 console
project referencing the selected checkout's Simulation project, copy the
source to `Program.cs`, and run with these arguments:

```sh
# Use the same pinned SDK, with DOTNET_PROCESSOR_COUNT=4.
dotnet run --project /tmp/setup/Probe.csproj -c Release -- \
  /tmp/cw-input/setup.json 14715cc3e04ef69cf8e486e09b4df2820b78ab06 \
  4:96,16:96,16:384 256 1 /tmp/setup/Program.cs ""
```

The [ordinary replay runner](construction-query-measurements.md#repeat-the-comparison)
then runs against each checkout using that same checkpoint directory. Run
each measurement alone, with fresh output prefixes. The unchanged committed
extension sources are the exact sources used here:

| Source | Arguments, in order |
| --- | --- |
| [Allocation repeat](household-plan-query-probe/allocation.cs.txt) | Checkpoint directory; output prefix `main-repeat` or `optimized-repeat`. |
| [Rollback extension](household-plan-query-probe/rollback.cs.txt) | `start-16-96.json`; output prefix `main-rollback` or `optimized-rollback`. |
| [Measured-phase profile](household-plan-query-probe/profile.cs.txt) | Checkpoint directory; fresh gate/output prefix `main-measured` or `optimized-measured`. |

For profiling, wait for `<prefix>.ready`, attach `dotnet-trace` 10.0.745401 to
that PID with `--profile dotnet-sampled-thread-time --format Speedscope`, then
create `<prefix>.go` only after the collector identifies the process and
output file. The [analysis script](footprint-query-probe/analyze-profile.py)
checks event-stack nesting and counts inclusive intervals under the tick core.

The [capture comparer](footprint-query-probe/compare-captures.py) accepts the
output directory, `main` and `optimized`; all 57 matching capture files must
exist before running it. Compare full bytes before interpreting timings.
The [paired Windows/F12 check](../../playtest/998-skip-blocked-footprints.md)
remains pending. Further work should profile current main after merge and
preserve eligibility, ranks, ownership and action-time authority.
