---
title: Measuring Town tick time
type: development-reference
status: active
updated: 2026-10-05
---

# Measuring Town tick time

Use `scripts/measure-town-ticks.sh` to compare native private-world ticks on
this checkout or a historical one. It reads `LastTickMilliseconds`, the same
committed-tick readout shown in [Developer tools](how-it-works.md#developer-tools-readouts).
It also records wall time around the host's nonblocking tick call. Neither
includes rendering, save-file writes or a remote model's response time.

The measurements and open findings belong in [#944](https://github.com/compoodment/ClankerWorld/issues/944).
These are controlled headless fixtures, not a Windows playtest or a promise
about every grown Town.

## Run a comparison

With the repository's .NET SDK available, run:

```sh
DOTNET_PROCESSOR_COUNT=4 bash scripts/measure-town-ticks.sh \
  /tmp/clankerworld-town-timings current . \
  4:96,4:384,16:96,16:384 256 64
```

Arguments, in order, are the output directory, a simple variant name, the
checkout to measure, comma-separated `residents:Roads` targets, the tick at
which sampling starts, and the number of sample ticks. Defaults measure
four, eight and sixteen residents with 96, 192 and 384 Roads, starting after
256 ticks and sampling 64 more. Use the same arguments and processor limit
for every version. Run one measurement at a time, without concurrent builds,
tests or another probe.

The script compiles its probe against the selected checkout. It does not
change that checkout's source, load a newer save into an older schema, or
call a model service. It writes `<variant>.json` with every timing sample,
the source commit, probe digest, runtime and machine information, and actual
world counts. `<variant>.probe.cs` preserves the exact probe source. A later
failed fixture leaves the earlier completed rows available; do not call
that a completed sweep.

For a historical comparison, create a detached checkout and pass its path:

```sh
git worktree add --detach /tmp/clankerworld-before-markets 0875e37c
bash scripts/measure-town-ticks.sh /tmp/clankerworld-town-timings \
  before-markets /tmp/clankerworld-before-markets 4:96,4:384,16:96,16:384 256 64
```

Repeat interesting rows in fresh processes. Compare the median, 95th
percentile, decision count and actual fixture counts; a single noisy run is
not enough to attribute a change to one feature. Preserve the raw samples
and note the machine and any other load.

## What the fixture controls

Every world uses the public `town-project-real-donation` seed, generated Small
geography, normal first-Town setup and four placed founders. Versions with
Markets use offered Council decisions, real votes, supplied Town stock and
completed native construction to build one. Earlier versions have no Market;
that difference is reported rather than inventing an unsupported building.

Additional adults enter through normal agent placement. Controlled household
creation and starting material lots let the owner-placement path add their
Houses. Paid construction, eligibility, routing and placement validation
remain native. Extra household Blacksmiths grow the Road network through
normal placement and Road generation; reserved Market and project tiles
remain protected. Starting food is controlled so setup does not depend on
whether a particular agent first gathers berries.

The Road count is a target: a generated street can overshoot it. The report
includes actual Roads, Houses, Town buildings, residents, map digest and
resource count. Larger Road fixtures also have more buildings. This is a
repeatable growth workload, not an isolated microbenchmark of Road lookup.
The fixture has no long trading history, large family tree or remote model
traffic; those need their own measurements.

Sampling uses the playable host's nonblocking tick method and built-in
choices. The report records admitted provider and action families and any
fallbacks so a change in decision workload is visible. Fixture construction
and setup ticks are excluded from the timing samples. The script validates
the prepared world before advancing it.

## Recorded comparison

The [raw dataset](measurements/944-town-ticks.json) preserves all timing samples,
actual world counts and admitted actions. The comparison uses ticks 257–320
on Debian 13, .NET 10.0.12 and an AMD EPYC 9V74 shared host with a four-core
CPU quota and `DOTNET_PROCESSOR_COUNT=4`. Runs are sequential. Values below
are median tick milliseconds; the columns are requested residents/Roads.

| Version | 4/96 | 4/384 | 16/96 | 16/384 |
| --- | ---: | ---: | ---: | ---: |
| Before Markets (`0875e37c`) | 58.0 | 75.8 | 414.6 | 616.8 |
| After Markets (`a52b6ea8`) | 88.1 | 116.3 | 484.9 | 766.3 |
| Before lanterns (`e00bb4b3`) | 96.0 | 131.9 | 432.5 | 724.5 |
| After lanterns (`477c50f7`) | 180.2 | 266.4 | 681.4 | 1,310.2 |
| Before scan fix (`96108882`) | 175.7 | 255.8 | 738.2 | 1,363.4 |

The after-scan-fix and current-main sweeps, representative
repeats and final assessment are still in progress. Actual Road counts in
these completed sweeps range from 96–100 and 385–392. World counts and map
digests describe the final native state after tick 320. Versions can admit
different actions and alter the map, even with the same starting seed and
sampling interval. Read those differences with the timing values.

A separate measured-phase profile on unchanged main (`bfef85e8`), with
16 residents and 390 Roads, sampled native managed thread time. Candidate
generation appeared in approximately 96% of the sampled native tick stack
time. Civic project proposals, private building choices, layout/route
construction and project-input/warehouse enumeration account for substantial
inclusive samples. These methods overlap; the figures are neither additive
nor exact CPU measurements, and may include GC or waiting. The profile had
41 deterministic admissions and no admission fallbacks.

[Bug #998](https://github.com/compoodment/ClankerWorld/issues/998) records this
remaining construction-choice workload and the reproduction. Existing
[Bug #953](https://github.com/compoodment/ClankerWorld/issues/953) records
built-in actors selecting personal model tool requests. The lantern scan
itself was addressed by [#946](https://github.com/compoodment/ClankerWorld/pull/946).
This measurement does not change simulation behavior.

## Profile only the measured phase

An optional seventh argument is a fresh gate-file prefix. The probe writes
`<prefix>.ready` with its process ID after setup, then waits for
`<prefix>.go`. Attach a .NET sampler to that process and create the `.go`
file after the collector attaches. Use one fixture per profiling run and
keep profiled timings separate from ordinary timings; the collector adds
overhead. The default run does not wait on a gate or require a profiler.

## Check in the game

On Windows, use a grown private world and record F12's tick readout alongside
the world size, Roads, residents, buildings, seed, game time and machine.
Repeat the same point on the builds being compared. Also note visible
stutter: tick time alone does not measure frame presentation. The pending
hands-on checks are in [the #944 playtest list](../../playtest/944-grown-town-tick-times.md).
