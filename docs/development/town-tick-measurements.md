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

The [raw dataset](measurements/944-town-ticks.json) preserves every ordinary
timing sample, actual world counts and admitted actions. The comparison uses
ticks 257–320 on Debian 13, SDK 10.0.401, .NET 10.0.12 and an AMD EPYC 9V74
shared host with a four-core CPU quota and `DOTNET_PROCESSOR_COUNT=4`. Runs
are sequential. Founder-start clock settings match across all seven runtimes
(360 ticks per world day, calendar offset 90); source digests are in the dataset.
Values below are median tick milliseconds; the columns are
requested residents/Roads. Medians and p95 use nearest-rank order statistics.

| Version | 4/96 | 4/384 | 16/96 | 16/384 |
| --- | ---: | ---: | ---: | ---: |
| Before Markets (`0875e37c`) | 58.0 | 75.8 | 414.6 | 616.8 |
| After Markets (`a52b6ea8`) | 88.1 | 116.3 | 484.9 | 766.3 |
| Before lanterns (`e00bb4b3`) | 96.0 | 131.9 | 432.5 | 724.5 |
| After lanterns (`477c50f7`) | 180.2 | 266.4 | 681.4 | 1,310.2 |
| Before scan fix (`96108882`) | 175.7 | 255.8 | 738.2 | 1,363.4 |
| After scan fix (`1a000c30`) | 81.4 | 112.7 | 412.1 | 651.7 |
| Current runtime (`bfef85e8`) | 83.4 | 130.3 | 408.4 | 729.8 |

The current-runtime row measures the simulation sources from main `bfef85e8`
on measurement branch head `8413a130`; the complete checkout SHA is
recorded in JSON. This branch changes development tooling and evidence only.
Each historical checkout uses its native content and save schema.

Actual Road counts range from 96–100 and 385–392. World counts and map
digests describe the final native state after tick 320. More Roads also mean
more buildings here. Versions can admit different actions and alter the map,
even with the same starting seed and sampling interval. Read those differences
with the timing values; these sweeps do not isolate a pure feature cost.

### Fresh-process repeats

Each cell lists the original sweep and two additional fresh-process medians
in execution order, with 64 ticks per run. The p95 values remain in the raw
dataset alongside every sample.

| Fixture | Median tick milliseconds |
| --- | --- |
| Before scan fix, 4 residents / 384 Roads | 255.8, 246.3, 258.3 |
| After scan fix, 4 residents / 384 Roads | 112.7, 111.3, 114.8 |
| Current runtime, 16 residents / 384 Roads | 729.8, 720.1, 751.5 |

### Assessment and remaining work

The scan fix reduces the medians in all four matched fixtures. Its pair has
matching actual resident, Road and Town-building counts and admitted action
families. The repeated road-heavy pair preserves that improvement across fresh
processes on this shared host. The Market and lantern sweeps are useful
historical comparisons, with differing native content and decision workloads.

Using #944’s rough thresholds, no canonical fixture has a median below 50 ms;
all sixteen-resident fixtures exceed 200 ms. The largest current median is close
to the pre-lantern median; the pre-Market version was already expensive at
sixteen residents. Growing Roads from about 100 to about 390 increases current
tick time, and quadrupling residents increases it
further. The large current-runtime fixture remains expensive after the lantern
scan fix. These are tick measurements; a Windows/F12 and visible-stutter check
is still pending, so they do not establish a frame-performance threshold.

A separate measured-phase profile on unchanged main (`bfef85e8`), with
16 residents and 390 Roads, sampled native managed thread time. Candidate
generation appeared in approximately 96% of the sampled native tick stack
time. Civic project proposals, private building choices, layout/route
construction and project-input/warehouse/shared-tool access account for
substantial inclusive samples. These methods overlap; the figures are neither additive
nor exact CPU measurements, and may include GC or waiting. The profile had
41 deterministic admissions and no admission fallbacks. Profiled timing
samples are excluded from the ordinary comparison tables.

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
