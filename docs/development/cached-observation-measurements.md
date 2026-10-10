---
title: Cached observation measurements
type: performance-report
status: complete
updated: 2026-10-08
---

# Cached observation measurements

Private-world observations reuse a map-derived fertility index and layer digest.
The measured cached refresh allocates about 48% fewer bytes on Small worlds and
45% fewer on Medium worlds. It still rebuilds dynamic observations. Timing is
mixed: the Small median is nearly flat and its p95 increases in this run.

## Matched native measurements

Each phase has five warmups and 100 synchronous samples. Cached projection uses
`GetReconnectBaseline` with both digests already held by the client. Elapsed
times are milliseconds; allocation is bytes per refresh on the current thread.

| World | Main median / p95 | Optimized median / p95 | Main bytes | Optimized bytes |
| --- | ---: | ---: | ---: | ---: |
| Small, 32,768 tiles | 2.964 / 4.949 | 2.928 / 5.685 | 1,290,000 | 665,300 |
| Medium, 131,072 tiles | 7.346 / 13.305 | 7.052 / 9.694 | 5,550,979 | 3,066,912 |

[Raw samples and input metadata](measurements/1195-cached-observations.json)
retain every timing and allocation sample, the direct layer-digest and fertility
constructor phases, checkpoint digests and source hashes. Those direct phases
are measured separately; their times are not exact attribution of a refresh.

## Method and limits

Baseline production is main `d50fe2aebfb53edacbf6a1f6407dbf564d821d3c`, schema
100. Both variants use the same native probe and generated Small/Medium seed
`cached-observation-cost-audit`, paused at Founder setup without agents or field
jobs. Generation, initial full snapshots, JSON serialization and save/reload
validation are outside the measured phases. No rendering, remote model calls or
advancing ticks are timed.

All eight complete captures agree byte for byte: initial snapshot, cached
snapshot, layer-cache miss and checkpoint for each size. Repeated cached output
is identical, observations preserve world bytes, and strict native reload
preserves both checkpoint bytes and the full snapshot. The regression control
also uses real field work after warming the projection, reloads an earlier
checkpoint through the same store, and switches to a different generated world.

Machine: shared Linux, AMD EPYC 9V74, four effective processors and cgroup quota
`400000 100000`, SDK 10.0.401 and runtime 10.0.12. No other build or test from this
session overlaps sampling. Shared-machine noise limits timing conclusions;
these results do not establish a Windows frame-rate improvement or a general
refresh latency promise.

## Repeat

Run the same probe source against each checkout, using separate variant names:

```sh
DOTNET_PROCESSOR_COUNT=4 bash scripts/measure-cached-observations.sh \
  /tmp/cached-observations main /path/to/base-checkout
DOTNET_PROCESSOR_COUNT=4 bash scripts/measure-cached-observations.sh \
  /tmp/cached-observations optimized /path/to/optimized-checkout
```

The probe writes full captures and raw measurements into that output directory.
Allocation regression budgets leave room for the remaining dynamic projection
and detect either repeated terrain-index construction or repeated layer hashing;
they do not impose a wall-clock test threshold.
