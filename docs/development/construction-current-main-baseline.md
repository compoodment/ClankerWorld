---
title: Current construction query baseline
type: experiment-report
status: active
updated: 2026-10-10
---

# Current construction query baseline

The remaining construction-query work in
[#998](https://github.com/ClankerWorldOrg/ClankerWorld/issues/998) still reproduces
on main `d5cc9f0885864f918f524948a720b28a530f2c36`, schema 122. This report records
an ordinary native baseline before profiling or production changes. It does not
report an optimization or a completed fixture sweep.

The unmodified [portable probe](town-tick-measurements.md) ran with
`DOTNET_PROCESSOR_COUNT=4`, target `16:96`, start tick 256 and 64 samples.
The actual generated fixture has 16 residents, 100 Roads, 36 Town buildings,
14 Houses and a Council-approved, supplied and completed Market. The
[raw report](measurements/998-2026-10-10-main.json) preserves every sample,
probe digest, actual counts and machine information.

Median native tick time was 475.4 ms; p95 was 700.4 ms. The run admitted 124
decisions with zero fallbacks. Its 139.99-second setup is excluded. The machine
was shared Debian 13 Linux with four effective processors, CPU quota
`400000 100000`, pinned SDK 10.0.401 and runtime 10.0.12. No other local build,
test or sampler ran during measurement. These authored native timings do not
establish Windows presentation or hosted-model latency.

## Incomplete dense setup

The unchanged [fresh-checkpoint setup](footprint-query-probe/setup.cs.txt) then
completed the `4:96` and `16:96` fixtures. Its `16:384` fixture stopped at the
256-workshop guard with 310 Roads and 276 buildings. No dense checkpoint or
measured-phase trace was produced. The one-tick setup samples are not ordinary
timing comparisons.

Current-main profiling needs an achievable dense fixture before choosing the
next bounded repair. Matched ordinary and local personal-provider captures,
resident/stock/land/building/Road changes, rollback, reload and post-change
measurements remain to be done. Full local setup output and generated checkpoint
evidence are retained in `.evidence/keep/998-native/` by Codex session 9b395a8e.
Existing Windows/F12 checks remain pending.
