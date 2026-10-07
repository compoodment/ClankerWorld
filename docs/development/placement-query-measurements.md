---
title: Placement query measurements
type: performance-report
status: complete
updated: 2026-10-07
---

# Placement query measurements

Gathering placement obstacles once per synchronous Town proposal query and
renting tentative layout-search costs reduces repeated work in the measured
grown Towns. Candidate lists, admissions and final saved states remain exactly
equal. The largest fixture still takes about 470 ms per native tick;
[#998](https://github.com/compoodment/ClankerWorld/issues/998) remains open for
the remaining layout, route and input-query cost. These authored measurements
do not establish a gameplay timing promise.

## Matched native measurements

Each row starts from the same fresh schema-95 checkpoint and advances 64
native ticks from 256 to 320. Times are milliseconds.

| Requested residents/Roads | Provider | Main median | Optimized median | Main p95 | Optimized p95 | Admissions |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 4/96 | Built-in | 113.5 | 102.5 | 287.0 | 316.4 | 12 |
| 4/96 | Personal fixture | 102.2 | 94.6 | 123.5 | 111.1 | 12 |
| 16/96 | Built-in | 374.3 | 338.7 | 474.9 | 430.5 | 48 |
| 16/96 | Personal fixture | 390.9 | 356.6 | 500.9 | 456.0 | 48 |
| 16/384 | Built-in | 554.9 | 470.2 | 692.1 | 572.5 | 54 |
| 16/384 | Personal fixture | 551.7 | 468.3 | 682.5 | 578.7 | 54 |

The largest fixture's medians fall by about 15%. The small built-in fixture's
p95 rises in this run; the median improvement does not mean every tick is
faster. Shared-host noise and first-use branches can affect individual samples.

The actual fixtures contain four residents, two Houses, 100 Roads and 24 Town
buildings; sixteen residents, fourteen Houses, 96 Roads and 35 Town buildings;
and sixteen residents, fourteen Houses, 390 Roads and 98 Town buildings.
Each has a genuinely approved, supplied and constructed Market. Road-heavy
fixtures also contain more private workshops, so Road count is not isolated.

All six pairs have byte-identical ordered candidate records, admissions and
complete final codec state, with zero fallbacks. Records include the actor,
tick and every offered candidate field. The personal fixture reports
`LargeLanguageModel` and returns fresh candidate-bound replies selected by the
real built-in provider. It exercises that runtime path, without a remote model
or its behavior and latency.

## What changed

Each proposal query gathers physical placement obstacles once, when a legal
site first needs them. This includes resources, camp objects, fields, Roads,
bridge ends, protected project sites, active expansions, placed footprints and
Port docks. Market tiles stay separate: an extra stall may occupy its own slot,
but that exception never removes another object's protection. Ordinary owner
placement and action-time checks gather fresh facts. No placement snapshot
survives its synchronous query.

Layout foot searches rent an array for tentative costs instead of allocating
a second map-sized dictionary. The settled-cost dictionary still contains
exactly the same tiles and costs. Queue ties, Road costs, occupied diagonal
corners, selected-site termination and the nearest-32 legacy anchor limit are
unchanged. The rented array is initialized before use and returned in `finally`.

A separate current-main measured-phase .NET sample identified candidate
generation, layout foot costs and repeated placement gathering. Inclusive
samples overlap and include GC or waiting; they do not assign exact CPU costs
to individual methods. That capture excludes setup, and its timings are
excluded from the ordinary table.

## Fresh state and replay checks

Twelve native variants cover both provider kinds under a control, resident
movement, shared stock changes, household land changes, removal of a Road used
by an actually offered lantern proposal, and public Workshop removal. Main
and optimized runs have byte-identical complete request and final-state files
in every variant. Every state validates, roundtrips through the codec, and
continues identically after reload and a paired subsequent tick.

Movement, stock, land and Road variants change candidates from their controls.
The selected Workshop removal leaves candidates unchanged; that result proves
equivalence under the mutation, rather than a changed offer.

Existing layout, paid construction, illegal-site, reservation, household
planning, Market and Port checks protect placement and action-time authority.
No timing threshold is added to unit tests.

## Method and repeatability

Baseline is main `ab35fdcfea799aa57e65280ee2eda2bd8fd84200`, including the
earlier [construction query repair](construction-query-measurements.md).
Optimized production is `2a5da3bea7adaf947168e68cb562ccb81b522722`.
[Raw samples and digests](placement-query-probe/samples.json) retain all 768
ordinary samples, input and production hashes, exact equivalence results and
state-change results. Full local captures are retained in
`.evidence/keep/layout-998`.

The unchanged committed [setup, replay and state-change probes](construction-query-measurements.md#repeat-the-comparison)
create generated Small geography from the public `town-project-real-donation`
seed. Household and starting stock are controlled; Houses, workshops and Roads
use actual owner placement. This is an authored growth workload, not natural
private-save growth. Setup and four warmup ticks are excluded; each measured
run restores its exact checkpoint. Codec validation happens before and after
sampling. No other local build or test runs overlap ordinary sampling.

Machine: shared Debian 13 CaaS, Intel Xeon Platinum 8370C, four effective
processors/cgroup quota `400000 100000`, SDK 10.0.401 and runtime 10.0.12.
Rendering, file saves and remote latency are excluded. No Windows playtest is
claimed. Use the existing runner against a clean baseline and this PR's
checkout, with fresh checkpoint/output paths, then compare each pair's request,
admission and final-state files byte for byte. Keep profiled runs separate.

The pending Windows/F12 check is in the
[playtest list](../../playtest/998-placement-layout-costs.md).
