---
title: Wild forage measurements
type: performance-report
status: complete
updated: 2026-10-08
---

# Wild forage measurements

Wild-animal forage selection reads nearby patches through the current ecology
state's ID index. The measured eight-animal searches allocate about 99% fewer
bytes after warming the index. This measures filtering and ordering, not a
complete feeding action, world tick or Windows frame.

## Native query measurements

The same generated Small/Medium seed, `wild-forage-cost-audit`, follows first-Town
setup, four founder placements, Start World and 12 ordinary ticks with local
idle providers. These worlds naturally contain eight untamed animals. Generation,
native ticks, checkpoint encoding and replay are outside timed searches.

Each phase has five warmups and 50 synchronous samples. A search enumerates every
animal's ordered candidate IDs. Cold-index searches create a fresh ecology state
with the same resources each time; their measurement includes index construction.
Warm searches reuse one unchanged ecology state. Real feeding replaces that state
after consumption, so it can require another index.

| World | Original median / p95 (ms) | Warm indexed median / p95 (ms) | Original / warm bytes | Cold indexed median / p95 (ms) | Cold bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Small, 1,973 resources | 0.571 / 0.678 | 0.057 / 0.095 | 515,528 / 7,376 | 0.131 / 0.190 | 72,884 |
| Medium, 6,740 resources | 7.033 / 22.511 | 0.558 / 0.656 | 1,749,096 / 7,280 | 0.783 / 0.936 | 203,851 |

[Raw samples, ordered candidates and source hashes](measurements/1197-wild-forage.json)
retain all 50 samples for each phase. The original predicate is also measured
in the optimized process as a paired control. The native baseline allocation
regressions fail on the original production query and pass after the fix.

## Outcome and replay controls

Candidate IDs and their order agree exactly for every animal. The baseline and
optimized tick-12 and tick-16 checkpoint hashes agree for both sizes. Within each
run, strict reload preserves checkpoint bytes and four subsequent native ticks
agree byte for byte with an independently restored runtime.

A separate native scenario gives two cows a patch with three feed units. Only
the first spends its two units. The second sees the remaining one as insufficient;
a newborn also cannot consume it. Rejected ticks leave the checkpoint unchanged,
and live/restored continuations agree. Missing source records remain unavailable.
The existing crowded-forage tests preserve alternate reachable patches, occupancy
and physical feed costs; animal pipeline checks cover care, births and products.

## Repeat and limits

Run the committed query regression cases in Release and inspect their JSON
test output in the TRX:

```sh
dotnet restore --locked-mode
DOTNET_PROCESSOR_COUNT=4 dotnet test \
  tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj \
  --configuration Release --no-restore \
  --filter FullyQualifiedName~GeneratedForageSearchPreservesOrderedChoicesWithoutMapSizedPredicateAllocations \
  --results-directory /tmp/wild-forage --logger "trx;LogFileName=tests.trx"
```

Base: main `ae02683dc171e4b8ccf8c51b5380eb7f2e23cde3`, schema 100.
The baseline moves the original predicate into the same query helper, retaining
its nested ecology scan and native outcomes.
Machine: shared Linux, AMD EPYC 9V74, four effective processors, SDK 10.0.401 and
runtime 10.0.12. No other build or test from this session overlaps the dedicated
measurement run. Shared-machine timing noise limits conclusions. These results
do not establish a full-tick speedup or Windows frame-rate improvement.
