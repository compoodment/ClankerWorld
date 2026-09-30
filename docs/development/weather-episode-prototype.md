---
title: Regional weather episode experiment
type: experiment-report
status: proposal
updated: 2026-09-30
---

# Regional weather episode experiment

This implements the experiment in [#375](https://github.com/compoodment/ClankerWorld/issues/375),
not a final rain-frequency decision. [#204](https://github.com/compoodment/ClankerWorld/issues/204)
stays open for the owner's playtest and tuning choices.

## Provisional rules

Regions remain 32×32. Ordinary episodes last uniformly from a quarter to one
saved game day; storms last from a quarter to three-quarters of a day. A storm's
end reserves at least half a day without another storm. Durations use whole
ticks, rounding the minimum up and the severe maximum down. One-tick fixture
days cannot generate a storm.

Climate/season weights are inherited from the daily baseline. Each transition
moves one-quarter of each precipitation weight (rounded down) into Clear.
Each wet cardinal neighbor adds one rain point, taken from Clear, up to four;
a zero rain weight stays zero. The prior non-storm condition gains three points.
These are experimental weights, not accepted percentages. Horizontal neighbors
wrap only on maps that already wrap. Every region sees the same prior snapshot.

Initial conditions preserve the existing day-zero roll. Old saves preserve their
visible current condition; the first resumed tick imports it. An imported storm
keeps its original age, while other imported conditions reserve a conservative
half-day storm-free window. Pausing or observing the map does not reroll weather.

## Fixed-sample comparison

Reproduce with:

```sh
dotnet test tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj --filter FullyQualifiedName~ReportPrototypeDistributionAgainstDailyBaseline --logger 'console;verbosity=detailed'
```

The sample uses six seeds (`episode-distribution-0` through `-5`), all five climate
zones, eight regions per map, all four ten-day seasons, and 16 ticks per day.
Both paths start from the same seed/profile. Rainy time includes Rain and Storm;
severe time includes Storm. Switch counts count actual condition changes, not
episode boundaries that keep the same condition.

| Climate | Baseline rainy % | Episode rainy % | Baseline severe % | Episode severe % | Baseline switches | Episode switches |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Tropical | 43.65 | 33.71 | 4.61 | 3.68 | 1372 | 2071 |
| Dry | 14.69 | 11.93 | 2.27 | 2.32 | 1124 | 1660 |
| Temperate | 29.01 | 21.51 | 5.04 | 3.98 | 1382 | 1995 |
| Cold | 19.43 | 14.57 | 4.18 | 3.93 | 1420 | 2001 |
| Polar | 10.83 | 8.27 | 5.04 | 3.72 | 1432 | 2056 |

Rainy time fell in every sampled climate; actual switches increased. Severe
time fell except in Dry, where it rose by 0.05 percentage points in this sample.
That small increase is reported, not treated as approved balance. These are
fixed-sample comparisons, not confidence intervals or a claim about every seed.

## Limits and remaining evidence

This is deterministic simulation and save-boundary evidence, not proof that
weather feels right in the game. The owner still needs to playtest rain frequency,
durations and neighbor strength. There are no moving fronts, new weather effects,
model calls or live deployment. Crop soil moisture still uses the older daily
three-day estimate; only existing current-weather exposure reads the episode.
