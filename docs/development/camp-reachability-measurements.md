---
title: Camp reachability measurements
type: performance-report
status: complete
updated: 2026-10-08
---

# Camp reachability measurements

[#1205](https://github.com/compoodment/ClankerWorld/issues/1205) removes the repeated
camp flood-fill from checkpoint map acceptance when actual topology and origin
are unchanged. Both runs reduce map-acceptance allocation by about 24% on the
supported sizes. Durable-save timings remain variable, including a worse first
Medium p95. The repair retains every other map check and the normal durable save.

## Source and native fixture

Baseline main is `1d8453a2ceb5400a8ab873bf202fbecdae7cf758`. Of 355 recorded
simulation, host and build inputs, only `SeededWorldHarness.cs` changes.
[Raw samples, source/helper/input hashes and full-byte comparisons](camp-reachability-probe/samples.json)
and [the exact disposable probe](camp-reachability-probe/probe.cs.txt) accompany
this report. These measurements are separate from permanent regression tests.

The normal generated Founder Setup path creates Small and Medium worlds with
public seed `checkpoint-reachability-cost-audit`, a feasible first Town layout
and four founders with stable IDs. Built-in local decisions advance five native
ticks, then pause. Fixtures were created on `a4afb62f`; baseline and repair both
strictly load the same paused tick-5 checkpoint on the source above. The generator
and fixture helpers are unchanged. No player save or hosted provider is used.

Small has 32,768 tiles, 1,550 resources and 15,230 camp-reachable positions;
Medium has 131,072 tiles, 7,490 resources and 65,717 reachable positions. Each
requires five starting-resource positions. Paused inputs are 1,196,957 and
4,718,165 bytes; their hashes are in the data file.

## Boundaries and environment

Each phase warms five calls. Map acceptance measures 20 complete
`MapAcceptance.Validate(map, true)` calls, including normal layer, placement,
route and manifest checks. A separate control measures 20 explicit
`ReachableFrom` traversals and their count assertion. Twenty warmed cached
required-position checks exercise the existing camp query separately. Ten
measured `PrivateWorldStateFile.Save` calls include normal encoding and durable
publication; file-byte/map-identity assertions run after the measured boundary
on every warm and measured save. Creation, strict load, continuation, replay and
other assertions are outside these phases. These are save/acceptance measurements,
not client frames or a complete tick-speed attribution.

Runs use fresh test processes, Release code, SDK 10.0.401 and runtime 10.0.12 on
shared Debian 13.6, Intel Xeon Platinum 8370C, four effective processors and a
16 GiB cgroup memory limit. Diagnostic measurements disable analyzers; final
verification uses normal analyzers. Allocation counts managed bytes on the
measured thread, not retained memory. Tables use ordinary median and nearest-rank
p95. Main and repaired runs execute separately without another local compiler or
probe running; the fresh repeats remain visible below.

## Complete map acceptance

| Run / map | Main median / p95 ms | Reuse median / p95 ms | Main / reuse bytes per call |
| --- | --- | --- | --- |
| first / Small | 35.9926 / 78.8271 | 14.5877 / 19.0535 | 7,965,758.4 / 6,069,582.8 |
| first / Medium | 105.9002 / 120.8085 | 27.8978 / 51.5122 | 33,976,526.0 / 25,803,066.4 |
| repeat / Small | 33.2255 / 242.6567 | 15.8581 / 26.2794 | 7,965,734.8 / 6,069,846.0 |
| repeat / Medium | 116.9360 / 123.8506 | 35.7583 / 59.8379 | 33,976,497.2 / 25,802,566.0 |

## Durable checkpoint saves

| Run / map | Main median / p95 ms | Reuse median / p95 ms | Main / reuse bytes per call |
| --- | --- | --- | --- |
| first / Small | 61.3317 / 98.5975 | 33.8797 / 55.4180 | 15,332,413.6 / 13,459,776.8 |
| first / Medium | 144.7466 / 160.1170 | 124.1782 / 435.8252 | 54,375,256.8 / 46,229,745.6 |
| repeat / Small | 53.3693 / 64.1845 | 34.2026 / 83.4404 | 15,340,817.6 / 13,444,208.8 |
| repeat / Medium | 155.4830 / 167.1768 | 116.9442 / 153.1970 | 54,374,813.6 / 46,201,824.0 |

## Explicit fresh traversal control

| Run / map | Main median / p95 ms | Reuse median / p95 ms | Main / reuse bytes per call |
| --- | --- | --- | --- |
| first / Small | 20.2684 / 23.6203 | 17.6171 / 32.2942 | 1,896,168.0 / 1,896,254.0 |
| first / Medium | 72.0343 / 76.0936 | 85.7116 / 99.6511 | 8,175,032.0 / 8,175,234.0 |
| repeat / Small | 19.0490 / 19.5180 | 19.3504 / 22.9349 | 1,896,168.0 / 1,896,168.0 |
| repeat / Medium | 80.1236 / 89.9745 | 87.7950 / 92.0194 | 8,175,032.0 / 8,175,032.0 |

Explicit traversal remains a full flood-fill and has essentially unchanged
allocation. Its timings vary between processes, including slower Medium control
timings in the first repaired run. Warmed checks of the five required positions
allocate zero bytes in all runs; they are a separate control, not a replacement
for full map acceptance. Do not subtract separately measured phases to claim an
exact share of total tick cost. The first Medium durable-save p95 increases to
435.8252 ms despite its lower median; the Small repeat p95 also increases from
64.1845 to 83.4404 ms. No uniform latency improvement is claimed.

## Integrity, invalidation and cost

A process-local weak table keeps a map's last origin, owned topology snapshot
and reachable-position set. Before reuse, it compares actual tile values, water,
elevation and surface bytes, dimensions, wrapping, and every bridge key/axis.
This also checks borrowed arrays and read-only collection views by content.
Changed topology or origin triggers a fresh snapshot and traversal. The snapshot
gets its own terrain index, so a stale index on a caller-mutated source cannot
hide a new barrier. Resource targets and camp placements are checked each time;
climate/vegetation validation and canonical manifest hashing remain unchanged.

Cold or changed maps pay for the snapshot copies and full traversal. The snapshot
and reachable set retain memory while their map is live; weak ownership releases
them with the map. This report measures allocation, not retained heap size. The
warm path still compares topology contents and performs the other acceptance
checks, so it is neither constant-time validation nor a zero-allocation save.
No cache fields enter checkpoints, and no schema or compatibility change is made.

The 1,024-tile allocation regression fails on main: five unchanged validations
allocate 1,775,120 bytes, essentially the fresh-map 1,780,944 bytes; a traversal
alone allocates 156,224. The repair avoids that repeated traversal. Separate
controls exercise edits to borrowed water/elevation/surface arrays, tile arrays,
bridge axes and east/west wrapping. The small bridge-axis fixture tests foot-step
constraints directly; it is not claimed as a playable saved bridge layout.

Every native case verifies each durable save, strict reload and resave against
its paused checkpoint. It resumes, refuses a fully prepared tick without changing
committed bytes, then advances 15 ticks and independently replays those ticks
from the same input to exact full checkpoint bytes. Eight actual baseline/repaired
captured-file comparisons pass across both runs: two paused checkpoints and two
complete continuations per run. Continuations are 1,216,595 and 4,739,526 bytes.
The exact sizes, hashes and comparison results are in the data file.

## Repeat the comparison

Use isolated baseline and repaired worktrees and the pinned SDK. Copy the
committed `probe.cs.txt` into the test project's
`Simulation/Harness/CheckpointCampCostProbe.cs`. Restore with
`dotnet restore ClankerWorld.sln --locked-mode`, then run:

```sh
CLANKERWORLD_CAMP_INPUT_ROOT=/absolute/shared-input/directory CLANKERWORLD_CAMP_PROBE_ROOT=/absolute/version-capture/directory dotnet test tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~CheckpointCampCostProbe --logger 'trx;LogFileName=camp-cost.trx'
```

Start with an empty public-fixture input directory on baseline; subsequent runs
strictly load its same paused checkpoints. Use separate capture/output directories
per version/run and compare every actual `.bin` file byte for byte. Read the
`CAMP_COST` JSON records from TRX output. Remove the disposable source afterward;
it is not a permanent CI benchmark. [How it works](how-it-works.md) describes
cache ownership; [Saves and replay](saves-and-replay.md) describes load integrity.
