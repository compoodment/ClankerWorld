---
title: Chunk validation measurements
type: performance-report
status: complete
updated: 2026-10-08
---

# Chunk validation measurements

[#1200](https://github.com/compoodment/ClankerWorld/issues/1200) removes repeated
canonical hashing of unchanged world-systems chunk manifests. Native subsystem
advances allocate about 59% less on Small and 68% less on Medium in both runs.
Timing varies: the first Small comparison is slower, and explicit canonical
digest calls remain slower. These measurements do not establish a Windows or
whole-game tick speedup.

## Source and boundaries

Baseline main is `a0cf9070c45f9f973f0923a6d39d0673af41a86c`. Of 355 recorded
simulation, host and build inputs, only `WorldSystemsContracts.cs` changes.
[Raw samples, input hashes and full-byte comparison results](chunk-validation-probe/samples.json)
and [the exact disposable probe](chunk-validation-probe/probe.cs.txt) accompany
this report. The probe is separate from the committed regression tests.

Both versions generate real Small and Medium worlds with public seed
`terrain-encode-cost-audit`, Founder setup and zero residents. Small has 32,768
tiles, 2,094 resources and eight manifests; Medium has 131,072 tiles, 6,563
resources and 32 manifests. Each case warms three `WorldSystemsRules.AdvanceOneTick`
calls, then measures 20 calls. Geography generation, world creation, replay,
serialization and save/reload are outside that boundary. These are native
world-systems advances, not complete private-world ticks or inhabited gameplay.

A separate control warms three explicit digest passes, then measures 30 passes
across every manifest. Each pass includes the probe's digest equality assertions.
The digest API still performs full structural validation, sorting, encoding and
SHA-256. This control is deliberately not a measurement of the reuse path.

Runs use fresh test processes, Release code, SDK 10.0.401 and runtime 10.0.12 on
shared Debian 13.6, Intel Xeon Platinum 8370C, four effective processors and a
16 GiB cgroup memory limit. Diagnostic measurements disable analyzers; final
verification uses normal analyzers. Allocation is managed bytes allocated on
the measured thread, not retained memory. Values below use the ordinary median
and nearest-rank p95. Repeats were separate fresh-process executions on each
version; run-to-run timing variation remains visible.

## Native subsystem advances

| Run / map | Main median / p95 ms | Reuse median / p95 ms | Main / reuse bytes per call |
| --- | --- | --- | --- |
| first / Small | 14.8933 / 22.4129 | 16.0282 / 27.3217 | 5,436,548.8 / 2,257,242.8 |
| first / Medium | 15.9665 / 17.9754 | 8.7180 / 13.1401 | 14,590,963.2 / 4,655,628.8 |
| repeat / Small | 16.2058 / 27.0585 | 8.0998 / 13.3550 | 5,436,548.4 / 2,257,320.4 |
| repeat / Medium | 26.4100 / 27.9594 | 9.5013 / 15.1595 | 14,590,936.0 / 4,655,436.8 |

The allocation reduction repeats on both sizes. Medium advances are faster in
both runs; the first Small median and p95 increase, while the fresh Small repeat
improves. The sample does not support a uniform timing claim.

## Explicit canonical digest control

| Run / map | Main median / p95 ms | Reuse median / p95 ms | Main / reuse bytes per pass |
| --- | --- | --- | --- |
| first / Small | 2.2931 / 4.7083 | 4.5517 / 6.6101 | 1,329,904 / 1,330,416 |
| first / Medium | 3.6457 / 4.9461 | 6.4100 / 8.7336 | 4,187,920 / 4,189,968 |
| repeat / Small | 2.3681 / 5.5802 | 2.6950 / 7.6417 | 1,329,904 / 1,330,416 |
| repeat / Medium | 3.7335 / 4.9853 | 4.1219 / 5.8218 | 4,187,920 / 4,189,968 |

Explicit hashing is slower in these samples. The owned resource wrapper adds
64 allocated bytes per manifest per pass (512 on Small, 2,048 on Medium).
Cold construction and changed manifests also pay for their resource snapshot
and full validation. The repair targets repeated validation of existing immutable
manifests; it does not optimize the canonical encoding API.

## Integrity and ownership

`ChunkManifest` snapshots constructor and `with` resource input, including a
caller-owned read-only view. A private immutable resource collection may be
shared by unchanged clones. Successful full structural and digest validation is
remembered by manifest object identity in a weak table. A new, changed or loaded
manifest requires full validation. Failed validation never creates a cache entry.
Current-world chunk/resource limits and duplicate chunk coordinates are checked
every time; weather, ecology and the other changing world systems remain checked.
The cache is derived process data and is never serialized.

The regression counts resource reads across 16 real subsystem advances: baseline
main rereads a three-resource source 480 times after warm-up. The repair performs
no further reads from that borrowed source. Twelve additional cases cover caller
aliases, changed manifests, current bounds, duplicate coordinates/resources and
strict loaded-digest rejection.

Every measured case independently replays all 23 advances and compares exact
world-systems bytes, preserves the original chunk-list identity, and performs
full private-world file save, strict reload and resave with identical bytes.
Eight actual baseline/repaired byte comparisons pass across both runs: Small
and Medium initial full checkpoints and their advanced world-systems envelopes.
The captured checkpoint sizes are 1,233,689 and 3,990,928 bytes; advanced systems
are 727,375 and 2,298,606 bytes. Hashes and comparison sizes are in the data file.
Private checkpoint schema 100 and the world-systems format remain unchanged.

## Repeat the comparison

Use isolated baseline and repaired worktrees and the pinned SDK. Copy the
committed `probe.cs.txt` into each test project's `Simulation/World/ChunkCostProbe.cs`,
restore with `dotnet restore ClankerWorld.sln --locked-mode`, then run:

```sh
CLANKERWORLD_CHUNK_PROBE_ROOT=/absolute/capture/directory dotnet test tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ChunkCostProbe --logger 'trx;LogFileName=chunk-cost.trx'
```

Use a separate capture directory for each version/run and compare every actual
`.bin` file byte for byte. Read the `CHUNK_COST` JSON records from TRX output.
Remove the disposable test source when finished; the probe is not a permanent
CI benchmark. [How it works](how-it-works.md) describes runtime ownership, and
[Saves and replay](saves-and-replay.md) describes the load boundary.
