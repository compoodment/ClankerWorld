---
title: Survival priority prototype measurements
type: prototype-report
status: proposal
updated: 2026-09-30
---

# Survival priority prototype measurements

This is experimental evidence for [#378](https://github.com/compoodment/ClankerWorld/issues/378),
not approved final balance. The [agreed starting references](../game-design/world.md#starting-survival-balance-for-playtesting)
remain 40% fullness / 60% warmth, with urgent food below 20% and urgent warmth
below 35% during continued exposure. [#140](https://github.com/compoodment/ClankerWorld/issues/140)
stays open. There is no approved target action share.

## Method and limitations

`SurvivalPriorityPrototypeTests.ReportFixedSeedSurvivalPriorities` uses the normal
private-world decision and settlement paths with the real deterministic provider.
Three compatibility-map worlds use seeds `survival-priority-0`, `-1`, and `-2`.
After three idle-provider setup ticks, each starts with identical food stock (32),
45% fullness and 55% warmth. Each runs 360 ticks with fixed clear, rain or storm
weather respectively. Seasonal profiles are held constant to isolate priorities;
this does not include the separate regional-weather proposal. The baseline is
main `77f2aaf`; the prototype uses the same fixture and seeds.

These are short, small-map controlled runs, not Windows playtests, large generated
worlds, model-backed social behavior, or statistical proof of balance. Selected
**decision shares are not elapsed-time shares**: long projects and travel can
continue without a new choice. Different numbers of decisions are reported.
No model service or paid calls are used. Each final state is encoded and restored.

## Selected decisions

Shares within each run; parentheses give counts. “Other” includes hauling,
production, idle and activities outside the four measured categories.

| Weather | Version | Decisions | Survival | Social/care | Building | Exploration | Other |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Clear | Before | 115 | 13.9% (16) | 7.0% (8) | 20.0% (23) | 4.3% (5) | 54.8% (63) |
| Clear | Prototype | 101 | 7.9% (8) | 7.9% (8) | 22.8% (23) | 8.9% (9) | 52.5% (53) |
| Rain | Before | 123 | 27.6% (34) | 7.3% (9) | 19.5% (24) | 4.1% (5) | 41.5% (51) |
| Rain | Prototype | 88 | 38.6% (34) | 8.0% (7) | 23.9% (21) | 3.4% (3) | 26.1% (23) |
| Storm | Before | 114 | 48.2% (55) | 3.5% (4) | 19.3% (22) | 3.5% (4) | 25.4% (29) |
| Storm | Prototype | 154 | 47.4% (73) | 0.6% (1) | 13.6% (21) | 13.6% (21) | 24.7% (38) |

## Outcomes

Food is initial/minimum/final edible inventory. Illness is the highest individual
illness reached, not a count of sick agents. No deaths occurred, so these short
runs cannot establish preventable-death risk. Project completions include
construction and production, not only Houses.

| Weather | Version | Food | Peak illness | Deaths | Meals | Exploration moves | Project completions |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| Clear | Before | 32/24/24 | 0.00% | 0 | 8 | 29 | 20 |
| Clear | Prototype | 32/28/28 | 0.00% | 0 | 4 | 83 | 21 |
| Rain | Before | 32/24/24 | 0.00% | 0 | 8 | 34 | 23 |
| Rain | Prototype | 32/28/28 | 2.64% | 0 | 4 | 21 | 17 |
| Storm | Before | 32/24/24 | 2.64% | 0 | 8 | 28 | 19 |
| Storm | Prototype | 32/28/28 | 16.84% | 0 | 4 | 49 | 19 |

## Available but unchosen opportunities

Counts of decisions where a category was offered but a different category won.
A decision can count in several columns; these are opportunities, not blocked
agent time or proof that every offered action would complete.

| Weather | Version | Social/care | Building | Exploration | Other |
| --- | --- | ---: | ---: | ---: | ---: |
| Clear | Before | 49 | 28 | 72 | 52 |
| Clear | Prototype | 53 | 30 | 66 | 48 |
| Rain | Before | 73 | 41 | 76 | 72 |
| Rain | Prototype | 41 | 28 | 47 | 65 |
| Storm | Before | 40 | 46 | 71 | 85 |
| Storm | Prototype | 33 | 45 | 72 | 116 |

## Interpretation and next playtest

The comfortable references no longer veto safe construction, curiosity or nearby
care. Food remains available, and fewer meals are taken. Clear-weather survival
choice share falls and exploration grows. **This does not solve weather pressure:**
rain survival share rises, rain exploration falls, and storm social/care choices
fall. Peak illness increases in both wet-weather runs; the storm increase is
material. No-death results over 360 ticks are not reassurance about longer runs.

The prototype uses provisional routine food seeking below 45%, carried eating
below 40%, and a 30% reserve for starting exploration. These are not additional
owner decisions. Food production, illness rates, exposure physics, no-energy /
no-sleep behavior and save schema are unchanged. Safe nearby responses remain
possible under urgent food; long food-gathering trips for child care do not.

Do not call this settled balance or close #140. Before adopting the tuning,
playtest longer wet-weather runs and inspect whether warmth choices make useful
progress, whether outings leave adequate protection, and whether nearby social
opportunities actually get selected. Compare generated worlds and household
food production as well as these small controlled fixtures. The prototype is
reviewable code and measurements, not deployment or native game verification.


## Repair of the first prototype

The original tables above remain evidence for the rejected first revision.
The repair separates optional food access from urgent food errands and keeps
the established small barter reserve. Safe warmth recovery remains selectable
after reaching cover. Scouting estimates exposed out-and-back warmth using
nearby weather, clothing and movement costs; starting shelter is not carried
protection. This is a local estimate, not a prediction of the whole route.
No exposure, illness, production or weather rates changed.

The first CI run had 16 failing cases. Investigation separated these causes:

- Seven barter cases and one trust-log case never created an offer because
  the prototype accidentally applied the food-errand cutoff to trade reserves.
  The original trade tests are retained unchanged.
- The council food-access case lost its optional collect candidate. Optional
  food collection is restored at a lower preference above the routine threshold.
  The memory-relevance case also passes with food options preserved; its privacy,
  relevance and truncation assertions are unchanged.
- Natural storm-cover behavior was a real regression: reaching cover removed
  the protective choice, allowing the next choice to walk away immediately.
  The existing movement/exposure regression is retained unchanged.
- History archival now deliberately starts with hungry agents so the fixture
  has a real inventory event to archive, instead of assuming every first tick
  consumes food. Archive, restart and monotonic-ID checks remain.
- Checkpoint round-trip uses its existing exact canonical-byte comparison,
  which checks nested exploration contents rather than array/list reference
  identity. The population and tick assertions remain.
- Unused-content rollback explicitly selects idle during activation so a new
  building project cannot turn the fixture into an in-use-content rollback.
  Production's explicit-migration guard is unchanged.
- The starter-content run observes 900 ticks instead of 300, retaining all
  building, collection and consumption assertions with no injected stock.
- The island-food test starts with an agent needing an errand; it still checks
  actual harvesting in the disconnected foot component and save restoration.

The sheltered-agent boundary also follows 20 recovery ticks, checking that
warmth rises without abandoning the shelter. Clear-weather curiosity and
construction below comfortable references remain covered.

### Revised controlled runs

Same three seeds, initial 32 food, tick 3, fullness 45% and warmth 55%.
Both 360- and 1,200-tick runs use the normal deterministic decision path.
The longer run can include births, so final population need not remain four.
Storm traces record actual position, warmth, illness, project stage and choice
every 120 ticks. All six final states passed encode/restore checks.

| Ticks | Weather | Decisions | Survival | Social/care | Building | Exploration | Other | Food | Peak illness | Deaths | Meals | Exploration moves | Projects completed |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1200 | Clear | 145 | 11.7% (17) | 10.3% (15) | 17.2% (25) | 15.9% (23) | 44.8% (65) | 32/21/25 | 0.00% | 0 | 7 | 187 | 23 |
| 1200 | Rain | 171 | 38.6% (66) | 8.2% (14) | 15.8% (27) | 7.6% (13) | 29.8% (51) | 32/21/23 | 0.00% | 0 | 7 | 109 | 25 |
| 1200 | Storm | 195 | 33.3% (65) | 13.3% (26) | 15.4% (30) | 6.7% (13) | 31.3% (61) | 32/17/17 | 2.48% | 0 | 7 | 104 | 22 |
| 360 | Clear | 104 | 10.6% (11) | 7.7% (8) | 22.1% (23) | 8.7% (9) | 51.0% (53) | 32/28/28 | 0.00% | 0 | 4 | 83 | 21 |
| 360 | Rain | 103 | 55.3% (57) | 6.8% (7) | 16.5% (17) | 2.9% (3) | 18.4% (19) | 32/28/28 | 0.00% | 0 | 4 | 21 | 15 |
| 360 | Storm | 103 | 42.7% (44) | 3.9% (4) | 22.3% (23) | 1.9% (2) | 29.1% (30) | 32/28/28 | 2.48% | 0 | 4 | 16 | 18 |

These results remove the original short-run wet-weather illness regression,
but do not establish final balance. Rain survival decisions still increase
and construction completions decrease in the 360-tick comparison. Staying in
cover now counts as a protective decision, not an additional trip; decision
share still cannot establish elapsed time spent on survival. Generated-world,
native Windows and model-backed playtests remain required before adoption.

### Baseline and integration verification

The unchanged `77f2aaf` baseline was rerun with the identical extended fixture.
At 1,200 ticks, clear/rain/storm peak illness was 0%/0%/2.64%, with no deaths.
Baseline decisions were 161/183/189; survival counts 22/44/66; social/care
16/20/23; building 25/24/23; exploration 20/18/21; other 78/77/56.
Food initial/minimum/final was 32/17/21, 32/17/17 and 32/17/17; each run had
11 meals. Exploration moves were 128/141/139 and completed projects 23/24/22.
Both baseline measurement cases passed; the below-35%-exposure priority case
failed on main as expected. That is a negative control for the changed urgency
policy, not a claim that the baseline recovery-in-shelter behavior was broken.

The revised table above is the matched pre-integration `71cf659` comparison.
Main then gained the independent reachable-Workshop fix (`eeecaee`). After
integration (`e7b8d0e`), both measurement cases passed again: peak illness
remained 0%/0%/2.48% at both lengths, without deaths. Activity counts changed
with the Workshop fix, so they must not be attributed only to survival tuning.

Full CI at `e7b8d0e` passed 651 Release tests (2 skipped), formatting, Godot
headless checks, Windows checks and Windows export. The local integration run
completed its two measurement cases, then aborted with AccessViolationException
in a later boundary test; it was not a successful whole run. The clean CI suite
is the final full-suite verification. Independent review and native playtests
remain outstanding.
