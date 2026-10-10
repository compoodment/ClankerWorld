---
title: Automated verification — 2026-10-10
type: development-reference
status: history
updated: 2026-10-10
---

# Automated verification — 2026-10-10

## Outcome

**75 automated cases passed, zero failed or skipped. The Linux headless Godot client/UI check passed. Three disposable generated worlds passed 12 exact save/reload-and-continue comparisons; a separate timing control passed two more.**

No new Bug issue was opened. A repeatable early-tick timing spike was recorded as related evidence on [the existing latency investigation #998](https://github.com/compoodment/ClankerWorld/issues/998#issuecomment-6091528761), without claiming its root cause or a Windows reproduction.

These are automated and native headless checks, **not Windows hands-on playtests**. No checklist bullets were removed; the [Windows playtest list](../../playtest/README.md) remains pending. The owner's running world, Windows desktop, credentials and deployments were untouched.

This is a historical verification record, not a statement about current main, a release gate, or a hands-on playtest sign-off. Codex session `a7eb5c1b` recorded it at the owner's request after inspecting the retained TRX counters, UI log and native summaries.

## Build and scope

- Tested source: main snapshot [`31aae86932fdb9f6faa8b05c76e3c97d6608cbce`](https://github.com/compoodment/ClankerWorld/commit/31aae86932fdb9f6faa8b05c76e3c97d6608cbce), repository version `0.1.0-dev`.
- Environment: Linux VPS, .NET SDK `10.0.401`; pinned Godot `4.7.2` .NET Linux editor, headless. Focused runtime runs used `DOTNET_PROCESSOR_COUNT=2`.
- The original campaign read the tested snapshot's `AGENTS.md`, all of `CONTRIBUTING.md`, `playtest/README.md`, the find-bugs skill and relevant build/test and test-audit guidance before testing.
- Worked in a clean isolated checkout. Production source and the playtest list were not edited; scratch probes and evidence stayed outside the Git diff.
- Main moved to [`d2f92e75`](https://github.com/compoodment/ClankerWorld/commit/d2f92e7551ab14d7bddaef6487688ab349a68b8d) while testing, adding marriage orders. **That newer runtime is not covered by this report.** Repository/job instructions did not change in that intervening commit.
- At the initial snapshot, the pending Windows list had 320 checklist files and 941 bullets. All remain untouched by this run; the later marriage-order change adds another file with three checks.

## Automated results

The actual TRX case outcomes were inspected, not inferred from command exit status.

| Area | Passed cases | What was checked | Recorded evidence |
| --- | ---: | --- | --- |
| Swimming | 11 | River/lake crossings, travel delay, warmth loss, reload, eligibility refusals, safe return, sea refusal | [#1318](https://github.com/compoodment/ClankerWorld/issues/1318#issuecomment-6091492553) |
| Save branches | 13 | Earlier-save branching, continuation, overwrite, rotation, deletion/restart, malformed metadata and failed-load rollback | [#1320](https://github.com/compoodment/ClankerWorld/issues/1320#issuecomment-6091492979) |
| Chunk validation | 13 | Valid unchanged manifests and rejection of malformed/changed resource and chunk data | [#1200](https://github.com/compoodment/ClankerWorld/issues/1200#issuecomment-6091495703) |
| Cached map refresh | 3 | Small/Medium refresh budgets, live fields/events, terrain after reload and world switch | [#1195](https://github.com/compoodment/ClankerWorld/issues/1195#issuecomment-6091495414) |
| Load World cache | 3 | Replaced checkpoint bytes, corrupt/repaired history, credential changes and world selection | [#584](https://github.com/compoodment/ClankerWorld/issues/584#issuecomment-6091494966) |
| Startup/checkpoint recovery | 10 | Signed recovery requests, unusable autosaves, refused-byte preservation, no-autosave refusal, retry and paused recovery | [#1319](https://github.com/compoodment/ClankerWorld/issues/1319#issuecomment-6091493435) |
| Save-disk warnings | 10 | Low/unknown space, multiple volumes, stale/stalled samples, startup recovery and continued save attempts | [#1321](https://github.com/compoodment/ClankerWorld/issues/1321#issuecomment-6091493916) |
| Recovery cleanup | 10 | Exact signed previews, stale preview refusal, retained named/latest saves and malformed provenance | [#1322](https://github.com/compoodment/ClankerWorld/issues/1322#issuecomment-6091494528) |
| Generation API | 2 | Balanced preview acceptance and exact signed generation options shared by preview/create | TRX and retained case list |
| **Total** | **75** | **40 runtime cases + 35 HTTP/persistence cases; zero failures or skips** | Retained TRX files |

Low-space conditions used disposable test hosts and injected disk queries. No live disk was filled. Signed API cases used synthetic disposable registrations, not the owner's device credentials.

## Headless client/UI result

`bash scripts/verify-godot-client.sh` completed with exit 0 and its final **UI checks passed** result. It built the C# scripts, started the scene, and ran the existing UI smoke scenarios. Those scenarios exercise menus/settings, pause/continue, New World preview and reroll, save branches/timeline, cleanup and startup-recovery dialogs, disk-warning display, keyboard navigation, map hover/drag/zoom, agent panels, family tree, long names, and rejected name changes.

Expected warnings came from deliberate connection/write/name-conflict controls. The ordinary Linux startup also reported unavailable Windows-only protected key storage. None was treated as a confirmed game bug or as evidence of Windows usability. No physical display, Windows export, Windows credential storage or paired live-world reconnect was inspected.

## Disposable-world exploration

Adapted `CreateGenerated` from `scripts/compare-tick-equivalence.cs`: default Small geography, feasible first Town, four placed founders and Start World; built-in decisions; no terrain/stock edits or model-service calls.

For each seed, the original world advanced 20 steps. Four checkpoints were encoded/decoded/restored and validated; checkpoint bytes matched exactly. After each restore, the uninterrupted world and its reloaded copy each advanced another step and their complete checkpoint bytes matched exactly. Thus 60 original-world steps plus 12 reloaded-copy steps were exercised in the main exploration.

| Seed | End step | Living founders | Deaths | Buildings | Events | Exact reload/continuation comparisons |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `autonomous-playtest-20261010-a` | 20 | 4 | 0 | 5 | 136 | 4 |
| `autonomous-playtest-20261010-b` | 20 | 4 | 0 | 5 | 144 | 4 |
| `autonomous-playtest-20261010-c` | 20 | 4 | 0 | 5 | 134 | 4 |

This was a short early-world run, not a long-season, parenthood, population-growth or personal-model playtest.

### Timing observation and control

The main probe recorded 16 ordinary advance-call timings per seed; four additional original-world continuation steps were checked but not timed. Medians below are calculated from the retained logged samples, rather than the probe's upper-middle summary value.

| Seed suffix | Median ordinary step | Maximum ordinary step |
| --- | ---: | ---: |
| `a` | 1141.4 ms | 10506.5 ms |
| `b` | 305.1 ms | 701.1 ms |
| `c` | 278.2 ms | 588.4 ms |

A fresh short control reversed the first two seeds (`b` then `a`), five original-world steps per seed, one exact reload/continuation comparison each. It passed both comparisons. Seed `a`'s fourth step still took **18,482.7 ms** after seed `b` had run, versus **10,506.5 ms** in the initial process. Seed `b`'s fourth step was **3,024.2 ms** when first in a fresh process versus **290.4 ms** when second in the initial process. Both seed/workload and cold-process effects warrant investigation; these shared-VPS samples do not identify construction as the cause or prove a Windows slowdown.

For seed `a`, the Town origin is `(136, 67)`. At step 4 there are four living founders, five buildings and eight Road tiles. Reproduce with the native Town/founder setup above and time the first four `AdvanceOneTickNonBlockingAsync` calls. [Full observation on #998](https://github.com/compoodment/ClankerWorld/issues/998#issuecomment-6091528761).

## Interrupted exploratory attempts

- The first native-shell test invocation ended with code 143 before producing a test report. It is not counted as passing. The explicit-lifetime rerun produced the 40 passing cases above.
- An initial blocking-tick probe exceeded its 30-second guard during concurrent compilation. No completed scenario was claimed from it.
- A nonblocking exploratory attempt was stopped to bound the campaign; its partial log was kept.
- Another exploratory probe reused one 30-second token for two sequential continuation calls. That was a harness mistake. Each continuation now has its own unchanged 30-second guard; the successful final run and control used the corrected harness after the focused suites finished. Earlier failures remain retained and are not presented as game defects explained by the later pass.

## Reproduction and retained evidence

Locked restore passed. The focused commands were:

```bash
dotnet restore --locked-mode
bash scripts/verify-godot-client.sh
DOTNET_PROCESSOR_COUNT=2 dotnet test tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj --configuration Release --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~SwimmingRuntimeTests|FullyQualifiedName~SaveBranchTests|FullyQualifiedName~ChunkValidationReuseTests|FullyQualifiedName~CachedTerrainObservationTests' --logger 'trx;LogFileName=autonomous-core.trx' --results-directory .evidence/keep
```

The HTTP run selected the 19 methods listed in `http-scenarios.json` plus `FullyQualifiedName~RecoveryCleanupDeletionTests`, with Release `--no-build --no-restore`, TRX `autonomous-http.trx`, and two processors. Selection is by method: most HTTP files belong to the shared partial `ViewerHttpTests` class, so their filenames alone are not valid filters. The exact filter is retained as `http-filter.txt`.

Both scratch console projects retain their executed source. Each targets .NET 10 and references Simulation. They were run in Release with `BuildProjectReferences=false`, `UseSharedCompilation=false`, two processors and isolated output directories. No production test hooks were introduced. `git diff --check` passed and the worktree diff was empty.

Evidence is retained locally in the original campaign checkout under `.evidence/keep/`; the raw TRX files, logs, synthetic saves and scratch probe sources are not committed or hosted by this documentation PR. The linked GitHub comments above provide the published observations. This is a record of the original run, not a rerun.

Retained files include this report, build context, TRX outcomes and all case names, command logs, native probe/control source, native JSON summaries, synthetic checkpoints and verified GitHub comment receipts. Cleanup was scoped to this run's own rebuildable output; unrelated worktrees and shared tools were left alone.

## Still pending

- Windows hands-on bullets, visual/animation feel, physical keyboard/mouse usability, themes on a real display and Windows credential/reconnect behavior.
- Full Release suite and Windows export/storage checks: not run; this was a focused autonomous campaign, not a release gate.
- Newly merged marriage-order runtime `d2f92e75`: not retested.
- Current-main profiling of the recorded timing input; no fix, merge, deployment, live-save mutation or new Bug issue was performed.
