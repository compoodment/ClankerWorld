---
title: Layout neighbor query measurements
type: experiment-report
status: active
updated: 2026-10-11
---

# Layout neighbor query measurements

Layout foot-cost searches use a value-type neighbor enumerator instead of
allocating the public iterator for each visited tile. Both paths share the
same offset order, narrow-wrap duplicate suppression and live `CanFootStep`
validation. No neighbor result, terrain or bridge legality is cached between
steps or searches. Occupied corners, Road costs, queue order, stopping rules
and action-time checks retain their existing authority.

The [recorded data](layout-neighbor-query-probe/conformance.json) contains a
fresh profile collected before the first optimization edit and native
state-change comparisons. This dataset establishes candidate and checkpoint
equivalence for those cases; it establishes neither a latency nor an
allocation improvement. Wider construction and layout costs remain in
[#998](https://github.com/ClankerWorldOrg/ClankerWorld/issues/998).

## Profile before the edit

Unmodified main `a7bd16422706852a8030cac53b6c54ed82aa1cef`, schema 122, runs
the portable native probe with 16 residents, 100 actual Roads, 36 Town
buildings and 14 Houses. The Market is Council-approved, supplied and built
through native actions. Extra households and starting stock are controlled;
Houses, workshops and Roads use native placement. This is authored growth,
not a naturally evolved private save.

The collector accepted its EventPipe session before opening the ready/go
gate. Setup and warmup are excluded. The 64 nonblocking native ticks,
256→320, admit 124 decisions with no fallback. Sampled thread time under
the native tick stack totals 53,896.7 ms. Inclusive shares are 96.41% for
candidate creation, 52.33% for Town project proposals, 23.48% for layout
foot-cost search, 15.74% for neighbor enumeration, 15.95% for `CanFootStep`,
13.14% for site evaluation and 2.04% for layout-context construction.
These nested spans overlap and must not be added or read as exact CPU costs.
Profiled timings are excluded from ordinary comparisons.

The shared machine is Debian 13, Intel Xeon Platinum 8370C, SDK 10.0.401 and
.NET 10.0.12, with CPU quota `400000 100000`. Profiling uses four effective
processors. A separate one-processor regression suite is running; this
profile is evidence of the sampled call paths, not an idle-machine benchmark.

## State changes, rollback and reload

Fresh checkpoints are authored on main
`b1de860531cd7c2b97fd00d65b2d28c3b3d9d47e`; its Simulation, Viewer and
toolchain inputs are identical to profiled main. Actual resident/Road/
Town-building/House counts are 4/99/24/2, 16/100/36/14 and 16/194/55/14.
The dense target is 192 Roads; the separate 384-Road fixture failure remains
tracked by #1599. No setup guard is bypassed.

The existing [mutation probe](construction-input-query-probe/changes.cs.txt)
uses the middle checkpoint on both versions. Eight cases cover control,
resident movement, added household stock, reserved stock, depleted resources,
household land, removed Roads and native workshop removal. Each runs with the
built-in chooser and local personal-provider admission using replies from that
same chooser. No hosted model is called. The affected resident is actually
queried in every case.

All sixteen baseline/repair pairs agree in complete ordered request/candidate
captures and canonical final checkpoints: 32 full-file comparisons. Each
version checks sixteen refused ticks against the preceding checkpoint bytes
and sixteen paired save/reload continuations. Every advanced tick has no
admission fallback. Some mutations retain the control's offered choices;
this check does not require every state change to alter a candidate.

These correctness runs use two effective processors while the separate
regression suite continues. Their timings are excluded from performance
comparisons. The report records every capture size and SHA-256, three fresh
checkpoint hashes, probe hashes and exact copied production DLL hashes. Of
413 Simulation, Viewer, linked Shared and toolchain input files, only the two intended
production files differ. The candidate DLL was built from the C# patch
committed as `54eb5831`; later candidate commits merge or add documentation
without changing those C# inputs.

The 150 focused terrain, route, bridge, layout, land and construction checks
pass, with no skips. New literal checks cover first-offset order on one- and
two-column wrapped maps and elevation changing after enumeration starts,
including blocked diagonal shoulders. Five documentation checks, locked
restore, Release build, full formatting verification and whitespace checks
pass. No public test hook or timing-threshold test is added.

## Reproduce the correctness comparison

Use the existing [probe runner](construction-input-query-probe/run.sh) and
one shared checkpoint directory. Generate the inputs once on the baseline,
then run `changes` on both checkouts and compare full captures with
[the comparison script](construction-input-query-probe/compare-captures.py).
The runner's `replay` and `repeat` modes additionally capture native samples,
allocations, ordered admissions and per-tick checkpoint/event digests. For
performance comparisons, run sequentially without another local build, test
or sampler, as described in [Measuring Town tick time](town-tick-measurements.md).

Full checkpoints, capture files, trace, Speedscope conversion and logs are
retained under `.evidence/keep/998-current/` in Codex session e0f464b8's
workspace. Existing [Windows/F12 checks](../../playtest/998-route-step-costs.md)
remain pending; these Linux correctness and profiling records establish no
Windows or hosted-model speedup.
