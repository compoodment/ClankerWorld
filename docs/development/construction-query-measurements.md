---
title: Construction query measurements
type: performance-report
status: complete
updated: 2026-10-05
---

# Construction query measurements

This change reduces repeated site scoring, shared-tool route lookup and idle
continuation queries. The measured grown Town is faster, but still exceeds
[#998](https://github.com/compoodment/ClankerWorld/issues/998)'s rough 200 ms
guidance. That issue remains open for the remaining layout and route costs;
these measurements do not establish a player-facing timing promise.

## Matched native measurements

Each row compares the same checkpoint, 64 native ticks from 256 to 320, the
same provider fixture and the same probe source. Times are milliseconds.

| Requested residents/Roads | Provider | Main median | Optimized median | Main p95 | Optimized p95 | Admissions |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 4-96 | Built-in | 98.1 | 92.4 | 231.8 | 279.4 | 13 |
| 4-96 | Personal fixture | 89.3 | 87.1 | 147.7 | 128.9 | 13 |
| 16-96 | Built-in | 409.6 | 357.7 | 524.7 | 426.5 | 24 |
| 16-96 | Personal fixture | 402.3 | 329.9 | 505.0 | 394.8 | 62 |
| 16-384 | Built-in | 741.3 | 545.3 | 833.5 | 586.6 | 41 |
| 16-384 | Personal fixture | 668.2 | 502.0 | 831.2 | 594.8 | 74 |

The requested 4/96 fixture actually has four residents, two Houses, 100 Roads
and 24 Town buildings. The 16/96 fixture has sixteen residents, fourteen Houses,
96 Roads and 35 Town buildings. The 16/384 fixture has sixteen residents,
fourteen Houses, 390 Roads and 98 Town buildings. All have a paid native Market.
Road-heavy fixtures also have more private workshops; Road count is not an
isolated variable. The small built-in fixture's p95 rises in this run; a lower
median is not proof that every tick became faster.

Every before/after pair has byte-identical recorded candidate lists, admitted
decisions and final codec state, with zero fallbacks. Candidate records include
actor/tick and all ordered candidate fields, including IDs, descriptions,
priorities and destinations. Built-in and personal fixtures differ in their
workload, so they are compared only within their own matched pair.

## Method and limits

Baseline production is main `bfef85e8802d63ff2bccf506cd61d304416735ec`.
[Raw samples and metadata](construction-query-probe/samples.json) preserve all
768 ordinary tick/wall samples, starting-state and candidate/final digests,
production/probe source hashes, actual fixture counts and state-change results.

Setup uses the portable native growth probe from
[#997](https://github.com/compoodment/ClankerWorld/pull/997), with one checkpoint
export added before timing. Generated Small geography uses the public seed
`town-project-real-donation`. Household and starting stock are controlled;
Houses, workshops and Roads use actual native owner placement. The Market is
Council-approved, supplied and constructed through native actions. This is an
authored growth workload, not a naturally evolved private save.

Each measured run restores its exact starting checkpoint. Four separate warmup
ticks precede a fresh restore; setup and warmup are excluded. The replay uses
`AdvanceOneTickAsync`, while the original issue's reproduction used the
nonblocking native method. Provider recording happens identically in both
variants. Validation and codec roundtrips run before/after the measured phase.
Rendering, save-file writes and remote model latency are excluded.

The built-in recorder delegates the real `DeterministicDecisionProvider`. The
personal fixture reports `LargeLanguageModel` and returns fresh, candidate-bound
responses chosen by that same built-in provider. It exercises personal-provider
runtime admission/continuation, not a remote model or its behavior/latency.

Machine: shared Debian 13 CaaS, AMD EPYC 9V74, four effective processors/cgroup
quota `400000 100000`, SDK 10.0.401, runtime 10.0.12, schema 92. No other local
build/test runs overlap ordinary sampling. Shared-host noise and first-use
branches can affect individual samples; no Windows playtest is claimed.

## Fresh state and replay checks

Twelve native variants compare both provider kinds under a control, resident
movement, shared stock changes, household land changes, a Road removed from an
actually offered lantern entrance, and actual public Workshop removal. Stock,
movement, land and Road inputs are controlled saved-state variants; Workshop
removal uses the normal public API. All initial/final states validate.

The movement, stock, land and Road variants change recorded candidates from
their controls. The chosen Workshop removal leaves offered candidates unchanged;
this confirms equivalence under that mutation, not a different ranked offer.
Main and optimized candidate/final digests agree in every variant. A checkpoint
roundtrip and paired subsequent native tick agree byte for byte in each world.

Ten quick scoring cases preserve exact material distances/bonuses, wrapping,
unavailable resources, construction-as-wood, Town/tag/ID purpose ties and rounded
border growth matching actual placement. They pass with both unchanged main and
the optimization: they protect equivalence, while native timings measure the
performance regression without a flaky unit-test time threshold. Sixty-nine
focused checks pass, including tool stock, expansion and retained decisions.

## Repeat the comparison

The archived [setup](construction-query-probe/setup.cs.txt),
[replay](construction-query-probe/replay.cs.txt) and
[state-change](construction-query-probe/changes.cs.txt) sources compile against
the selected checkout's real Simulation project. The
[runner](construction-query-probe/run.sh) writes only disposable synthetic
checkpoints and reports. Use the pinned SDK and fresh output paths.

```sh
# BASE_ROOT is a clean worktree at bfef85e8; FIXED_ROOT is this PR's checkout.
# Run each timed command alone, with no other local builds or tests.
probe="$FIXED_ROOT/docs/development/construction-query-probe/run.sh"
DOTNET_PROCESSOR_COUNT=4 bash "$probe" setup "$BASE_ROOT" /tmp/cw-input /tmp/cw-result/setup
DOTNET_PROCESSOR_COUNT=4 bash "$probe" replay "$BASE_ROOT" /tmp/cw-input /tmp/cw-result/main
DOTNET_PROCESSOR_COUNT=4 bash "$probe" replay "$FIXED_ROOT" /tmp/cw-input /tmp/cw-result/optimized
DOTNET_PROCESSOR_COUNT=4 bash "$probe" changes "$BASE_ROOT" /tmp/cw-input /tmp/cw-result/main-changes
DOTNET_PROCESSOR_COUNT=4 bash "$probe" changes "$FIXED_ROOT" /tmp/cw-input /tmp/cw-result/optimized-changes
```

Compare each pair's `*-requests.json`, `*-admissions.json` and `*-final.json`
byte for byte, then its 64 timing samples. The state-change probe selects its
Road from this exact seed/checkpoint's observed lantern proposals; it is not a
general player-save mutator. Keep outputs outside the repository diff.
