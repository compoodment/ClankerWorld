---
title: Warehouse query measurements
type: performance-report
status: complete
updated: 2026-10-07
---

# Warehouse query measurements

This is a bounded follow-up to [#998](https://github.com/compoodment/ClankerWorld/issues/998).
It removes repeated Warehouse discovery during recipe and expansion planning.
It preserves the candidate set, ranks, admissions and resulting world. The
broader layout, route and input-query cost remains open; the experiment's
rough 50/200 ms guidance is still unmet and is not a gameplay promise.

## Source and workload

Baseline main is `32298e77`, after the first placement optimization and the
farm-hauling storage repair. Both versions use fresh schema-97 checkpoints
from the unmodified committed [setup, replay and state-change probes](construction-query-measurements.md#repeat-the-comparison).
Generated Small geography, actual Council-approved Market construction,
native placement and controlled household stock produce three authored growth
fixtures. These are not naturally evolved player saves.

The fixtures have 4/16/16 residents, 100/96/390 actual Roads, 24/35/98 Town
buildings and 2/14/14 Houses. The historical case names `4-96`, `16-96` and
`16-384` remain the probe's identifiers. Each replay restores the same input,
warms a separate world for four ticks and measures 64 ticks starting at 256.
Built-in and candidate-bound local personal providers use normal native
admission; neither contacts a hosted model.

Measurements ran alone on shared Debian 13, four effective processors/cgroup
cores, SDK 10.0.401 and runtime 10.0.12. They do not establish Windows, Godot
rendering or remote-model performance. Source/probe/input digests, raw timing
samples and comparison results are in [the data file](warehouse-query-probe/samples.json).

## Ordinary native timings

Values are the committed probe's lower median and p95 in milliseconds.
Profiled runs and fixture construction are excluded.

| Fixture and provider | Main median / p95 | Warehouse repair median / p95 |
| --- | --- | --- |
| 4-96 built-in | 107.8 / 296.1 | 150.6 / 360.7 |
| 4-96 local personal | 102.2 / 121.4 | 98.5 / 184.7 |
| 16-96 built-in | 354.6 / 448.2 | 346.1 / 434.0 |
| 16-96 local personal | 356.0 / 450.0 | 345.0 / 441.8 |
| 16-384 built-in | 499.7 / 608.3 | 470.7 / 602.5 |
| 16-384 local personal | 505.8 / 616.8 | 466.5 / 576.2 |

The largest fixture's medians improve about 6–8%; the middle fixture improves
about 2–3%. The first small built-in run is slower, and both small-case p95s
increase. A clean baseline and unchanged repair repeat of that small fixture
measured 108.2/234.1→106.3/257.0 ms built-in and
94.3/111.3→88.9/108.8 ms local personal. Its earlier median increase did not
recur, but the built-in p95 still increases. All runs are retained; this does
not prove uniform speed improvement or a cause for the transient increase.

All six ordinary before/after candidate, admission and final-state files match
byte for byte, with zero fallbacks over 768 ordinary ticks. The small repeats
add 256 ticks and preserve those captures. All twelve control/state-change
pairs match candidate and final bytes after movement, shared stock, household
land, Road removal and building removal. Their normal admission checks,
strict reload and paired subsequent tick also pass.

## Measured-phase profiles

Separate .NET sampled-thread captures cover only the dense fixture's measured
64 built-in ticks, after setup and warm-up. Their candidate, admission and
final captures match the corresponding ordinary runs. Inclusive sampled
native-tick thread time is about 39.0 seconds on main and 37.1 seconds with
the repair. Candidate generation spans about 95% in both.

`WarehousesAccessibleTo` spans about 7.4→0.9 seconds inclusive. This confirms
less sampled time in the targeted discovery path; it is not an exact CPU
measurement or an independently additive saving. Ancestors overlap, inlining
can change attribution, and waits/GC may contribute. Town proposal/layout and
remaining project-input work still dominate. The profile's timings are not
substituted for the ordinary table.

## Implementation and limits

One synchronous inhabitant query lazily collects its resident/abandoned-Town
Warehouse list and shares it between recipe and expansion input checks. Stock
quantities and reservations are still evaluated per input. Calls outside that
query create a fresh list. Warehouse definition matching uses a freshly built
ordinal set; ordering and abandoned/foreign-Town access rules are unchanged.
No list survives a query, tick, movement, membership/content change or reload.
Physical collection and project execution retain current-state validation.

No saved field, event format, schema version or migration changes. This repair
needs independent review and a [paired Windows/F12 check](../../playtest/998-warehouse-query-costs.md).
After it merges, profile current main before choosing the next #998 repair.

## Repeat

Use the existing runner against clean baseline and repair worktrees, with
fresh schema-97 inputs and no other local builds or tests during timing runs.
Compare every `*-requests.json`, `*-admissions.json` and `*-final.json`
ordinary pair, then the state-change candidate/final pairs and raw samples.
Keep sampled profiling separate. The [portable commands](construction-query-measurements.md#repeat-the-comparison)
and their sources are unchanged.
