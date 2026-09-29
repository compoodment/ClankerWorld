---
title: Known Bugs and Product Gaps
type: defect-register
status: active
updated: 2026-09-29
---

# Known bugs and product gaps

This is the canonical durable list of confirmed current problems. GitHub issues
may track active implementation work, but closing an issue does not remove an
entry here until the fix is merged and verified through the normal product
path.

## Live verification pending

The provider-held tick fix is installed on the live VPS host. Computment is
playtesting a paired Windows New World, and model calls began succeeding after
the meter-directory permission repair below. Whether a deliberately slow model
can be left pending while the live world remains responsive still needs a
specific paired-client check; see the resolution evidence below.

## September 29 external-review triage (checked against `ff9ee9e`)

The review was written against older commit `6237154`; the entries below are
**source-confirmed current gaps**, not claims that a Windows client or the live
VPS reproduced every failure. The reviewer reports one Windows test failure;
that run was not repeated here. Prioritize the prompt/identity and playtest
reliability work alongside the existing terrain/UI reports, without treating
proposed fixes as owner decisions.

| Area | Confirmed current gap | Next evidence / repair boundary |
| --- | --- | --- |
| Windows checkout/test | `DocumentationFilesDeclareTheirAuthorityMetadata` requires LF front matter and the repository has no `.gitattributes`; CRLF checkout can fail despite valid docs. CI runs tests only on Ubuntu. `global.json` pins SDK 10.0.401 with `latestPatch`. | Make the documentation test newline-neutral or guarantee LF checkout, then run it on Windows. Choose SDK roll-forward deliberately; a lower feature band is not guaranteed compatible. Add a Windows test job when the supported client platform can be exercised in CI. |
| Personal-model needs and identity | The provider receives fullness as unlabeled `hunger_basis_points` (0 starving, 10,000 full), and no name, life stage, personality, aspiration, household, current activity, warmth/illness, inventory, nearby relationships or recent thought. The system prompt has output/memory rules but no settlement role frame. | Give the model a compact, truthful self/state/nearby context and plain-language need scale. Replay representative observations and compare legal choices, idling, food decisions and thought quality before attributing current pacing complaints to this cause. Preserve per-agent knowledge boundaries. |
| Prompt shape and cost | Internal epoch/tick and Jev-salience fields are sent to the personal model; candidate lists are code-ordered and may include several site-coordinate variants. The response asks for a full probability map and confidence, and sets no explicit output-token or temperature limit. | Simplify model-facing text and bound output/candidate volume; measure quality and cost. **Keep a confidence policy or replace it explicitly:** confidence below 0.5 currently invokes `safe_idle`. Probabilities do not select the action. Do not assume `response_format: json_object` works uniformly across endpoints. |
| Conversation and memory | The legal-candidate interface has no free-form adult dialogue turn, gossip/lie propagation, generated invention proposal or free-form will. Child conversation is a fixed result. Recent thoughts are not fed back, and experiences are not automatically turned into memories. | Design separately bounded dialogue/planning/invention/will call contracts with validated structured effects and a safe failure path; preserve speech-versus-truth provenance. This is a finished-game vision gap, not authorization for arbitrary text to mutate world state. |
| Provider parity and observability | Jev's choice payload differs from the personal-model payload and lacks retrieved memories/map facts/persona. A malformed hosted response falls to the safe fallback without an adapter-level repair retry. Player-visible reason for an idle agent is still weak after a provider failure. | Define Jev's limited role, expose a clear agent-level waiting/failure/limit state, and assess a bounded repair retry against extra paid attempts. Record safe, bounded outcome telemetry; never raw prompts, responses or keys. |
| Tick failure and recovery | The background one-second loop awaits `TryAdvanceOnceAsync` without a catch around the main tick or active recovery save. Only rotating autosave failure is caught. A thrown tick/save exception can stop the hosted service under its default behavior. | Prove the failure with a tiny host or injected save/tick error; hold the world and report a recoverable halt without silently discarding state. Distinguish corrupt persisted state from a transient write failure. |
| Persistence scale and retention | Each advanced tick re-encodes the active world and fsyncs a replacement file. Manual-save overwrite creates a full recovery copy every time; only autosaves rotate. History segments are digest-addressed, with no retention policy found in the state-file path. | Measure bytes and encode/fsync latency on a representative larger world before changing cadence; preserve pause/quit/crash guarantees. Decide backup and history retention with the save policy, not an arbitrary silent deletion rule. |
| Save-list isolation | `ManualWorldSaveStore.List` deserializes all metadata in one LINQ sequence; one malformed file can abort the entire list and autosave rotation. | Isolate and report a bad entry while keeping sound saves reachable; verify with one damaged metadata file beside a valid save. |
| Usage-meter durability | `ProviderUsageStore` throws on an unreadable existing meter during construction, preventing host startup; its fixed `.tmp` write has no fsync. This is separate from the already-repaired reservation write rollback and the live meter-path incident above. | Start with hosted calls blocked and an actionable owner error on unreadable accounting; use durable atomic replacement and test restart after an interrupted write. Never reset spent attempts silently. |
| Client presence/network | Only authenticated reconnect renews the default five-second presence lease. The client polls every second with the default 100-second `HttpClient` timeout and no refresh cancellation; a hung refresh suppresses later polls and gates UI actions. | Exercise a stalled signed reconnect on Windows. Bound/cancel refresh, show connection state and keep pause available; evaluate a cheap signed heartbeat or a measured lease change without permitting unattended calls. |
| Pairing/API limits | Eight ten-minute pending pairings can fill the unauthenticated creation capacity; the loopback-bound public listener has no explicit request-body/rate policy. | Assess reachable tailnet threat and legitimate retry flow, then bound pairing/request volume without weakening local-only approval or blocking the real owner. This is hardening, not a demonstrated exploit. |
| Local credential storage | Provider keys are written as JSON in an installation-local file; Unix file modes are tightened, but Windows has no equivalent encryption in this store. | Before the player-local Windows release, use appropriate current-user protected storage and a clear forget-key flow while preserving save/credential separation. Existing in-app deletion does not erase backups. |
| Event parsing | Event `Detail` is a delimiter-joined string parsed by the host and client; the client humanizes unknown kinds in the player Event Log. | Replace fragile positional parsing with typed payloads as event contracts change and explicitly select player-visible kinds; assess actual IDs/text before claiming a current misparse. |
| Delivery drift | The repository has verification scripts and a systemd template, but no checked-in end-to-end live deployment/meter-migration command. The current live process predates several repository fixes. | Make the next authorized deployment repeatable with preflight, meter migration, save/credential preservation and rollback checks; do not redeploy during the active playtest without owner authorization. |
| Map readability | Marker captions truncate every name after seven characters; most activities share `○`; resource names remain uppercase text overlays alongside prototype glyphs. Full names already appear in marker tooltips. | Try given name or initials at map scale, distinct activity categories and zoom-aware resource icons without losing selection/hover facts. Do visual review on the Windows client before claiming a specific layout/contrast defect. |
| Client feedback/input/layout | `FriendlyFailure` exposes most exception messages; several controls/tooltips use technical copy; many panels use fixed pixel minimums; camera panning occurs per key event rather than frame time. The existing UI-scale report above remains open. | Map errors to plain states with separate technical detail, trim help copy, test 720p/200% scale and keyboard/focus paths on Windows. Evaluate Escape/Space and smooth diagonal pan with actual UI interaction. |

Two recommendations in the review are **not adopted as stated**: removing
response confidence would change the current low-confidence safety gate, and
off-screen pop-up event banners would conflict with the vision's Event Log
preference. The suggested first-run checklist partly exists as in-world
founder-progress text; test discoverability before adding a second guide.
Manual-save overwrite already has an explicit confirmation dialog, so that
recommendation is not an open defect; its growing recovery-copy count is.
Large-file/panel extraction and repeated owner-route structure are code-health
work, not independently confirmed player defects. The earlier claim that every
unauthenticated request writes the authority file was already corrected in the
review and is not carried forward.

## September 29 New World playtest

- **Agents appeared idle and had no thoughts — live cause found and mitigated:**
  the host's usage-meter default pointed into a root-owned application
  directory. Every model reservation threw `UnauthorizedAccessException`
  before reaching the configured provider, so four founders fell back to idle.
  Making the existing meter directory writable to the service user, without a
  restart, allowed accepted model decisions and saved private thoughts for all
  four agents. The repository fix moves the default meter next to the private
  provider state and makes reservation writes transactional. The live process
  still uses the old path until a later deployment. Its meter contains 54
  phantom pending reservations accumulated in memory while writes failed;
  preserve and correct that meter at deployment before counting those as paid
  calls. Model behavior and pacing after the repair still need playtesting.
- **New World still shows an old camp alongside the chosen Town — confirmed in
  playtest build:** generated maps retained six legacy camp objects and a
  provisional camp-derived Town border before site selection. The source
  change now begins without either and retains the previous generator for
  old-save validation; the active playtest save remains unmodified. Confirm
  the fresh-world behavior on the next Windows build before closing this gap.
- **Starter Roads cross building footprints — confirmed in playtest build:**
  the deployed planner seeds and joins routes at building anchor tiles. New
  Roads need to stay outside each footprint and meet adjacent entrances; the
  player's sketch favors a central spine with short branches, not a rigid
  blueprint. Existing Road speed and generated ownership rules remain. The
  source repair now keeps new starter/growth Roads beside entrances and bars
  later construction on Roads; previously saved overlapping Road tiles still
  require a compatibility-safe repair and the running world is not rewritten.
- **Terrain distribution and art need revision — partly traced:** current
  surface classification makes every low-elevation bank beside any water
  sandy, including rivers; vegetation is classified independently so a tree
  can land on sand. Sparse resource trees leave many forest-floor tiles empty.
  The player wants less coastal sand and little or no river sand; forest floor
  should visibly hold a tree or plant (mostly trees), while forest grass has
  scattered trees. Cacti are not wanted in the current art direction. The
  texture grain, striped transitions and large circular weather clouds are
  rejected. Hills should visibly lead into mountain regions; there is no hill
  terrain kind yet. Generator changes must not invalidate the running world.
- **Display and camera need playtest-driven bounds:** UI Scale enlarges text
  but leaves several window/panel dimensions small; at high render resolution
  the interface feels undersized. Start fullscreen by default. Small maps can
  zoom out beyond their north/south edges to show black; Small/Medium should
  use a map-relative cap (roughly 70% of fitting scale is the player's starting
  suggestion), while larger presets need a shared visible-tile/performance
  cap. Maximum zoom-in should have a consistent world-space feel across
  screen resolutions.
- **Settings, inspection and agent UI wording/layout need an audit:** Main
  Menu Settings should use a compact back chevron in the close-button position,
  not a second large Back to Main Menu button. Remove explanatory paragraphs
  beneath Render Resolution and UI Scale and the Load World pause/save helper.
  Tile inspection should omit unavailable or inapplicable facts such as
  fertility rather than filling the card with `none`/`unavailable`. Replace
  technical/AI wording throughout player-facing screens (for example,
  `Inhabitant cognition` with `Agent model`), explain the model-call limit in
  ordinary language, and tighten oversized empty agent/settings panels. The
  placeholder household names `Camp Alpha`/`Camp Beta` are disliked; the
  naming flow needs a player-facing replacement without changing membership.

## September 29 client UI audit

A live audit of the Godot client against a local host found these defects.
Each is fixed in the repository build and covered by the headless UI smoke
test; computment's Windows playtest remains pending.

- **Town panel and reselected agent text blank — fixed in repository build,
  laptop check pending:** refreshes cleared a text panel and then re-assigned
  identical text, which Godot ignores, so the Town panel emptied while paused
  and a reselected agent lost relationships, thoughts and memories. Panels now
  replace only changed text, which also keeps the Event Log scroll position.
  Five "Initial content activated" entries no longer open every Event Log.
- **Status messages vanished within a second — fixed in repository build,
  laptop check pending:** each one-second observation refresh erased the
  toast, including Choose Town site and Move founder instructions. Results now
  stay readable, mode instructions stay while the mode is active, refused
  requests are no longer reported as disconnections, and messages show above
  Main Menu Settings.
- **New worlds opened over open sea — fixed in repository build, laptop check
  pending:** the camera started at the map's geometric center; it now frames
  the first Town, then living agents, then camp objects, across a wrapped seam.
  A new, unsettled world instead opens over nearby dry land; this is a camera
  hint, not a guarantee that the host will accept a Town layout at that tile.
- **Tile card clipped and garbled map labels — fixed in repository build,
  laptop check pending:** the selected-tile card was measured while hidden and
  overflowed the screen bottom; zoomed-out marker names clipped into fragments
  such as "rehou". The card now fits its facts inside the view, and markers
  too small for a name show their glyph only.
- **Top bar live behind modal menus — fixed in repository build, laptop check
  pending:** Start World, Play/Pause and founder edits stayed clickable behind
  the Pause Menu; the top bar is now shaded and blocked with the world view.
  Town-site mode shows Cancel Town site while active. The Connect/Pair screen
  no longer shows both a close button and a large Back to Main Menu button, and
  misaligned settings captions now share one column.

## September playtest reports and confirmed product gaps

- **Disconnected-land agent placement broke observation — deployed on
  2026-09-28, pending player verification:** after adding an
  adult on land with no foot route to the camp's berry patch, route preview
  threw during every reconnect. The preview now reports no reachable food route
  without taking down the world view. The client also accepts the host's pause
  receipt when leaving for the Main Menu, even if the subsequent refresh fails.
  The deployment gate additionally caught and repaired an old generated-map
  compatibility rejection before the live service was changed. The live save
  migrated from schema 17 to 20 with its tick, population and map identity
  preserved.
- **Final zoom-out cap needs playtest selection:** the client can now zoom to
  8 px terrain tiles (roughly 50% wider coverage than the old 12 px minimum on
  a 256-tile-wide map). Confirm that tile readability and frame cost are right
  on the player's target hardware before treating this as the final cap.
- **Bridges absent — current capability gap:**
  agents can cross one-tile-wide river tiles on foot at half dry-ground speed;
  wider river sections remain impassable. Town-assigned building placement now
  lays a provisional dry-land Road connection, but the initial Town layout,
  inter-Town links, traffic-triggered bridges and Road-generated bridges are
  missing. Walking alone does not paint Roads. A legal bridge should not block
  a needed crossing over a separate nearby stream; exact routes, bridge radius,
  speed and materials remain open.
- **Agents rarely pursue non-survival activity — playtest report, partly
  addressed:** stable adults now have a bounded local curiosity outing that
  remembers visited tiles and returns; whether that feels sufficiently useful
  requires owner playtesting. Visited terrain and resource sites now enter
  only that agent's bounded knowledge, and returning from an outing can create
  a physical map or field record that can be shared or bartered. This does not
  yet search for distant resources or Towns; purpose-driven exploration remains
  the next direction after the owner judges its pacing. Low hunger or urgent
  weather exposure can still pause projects and narrow adult choices to routine
  survival. There is no separate numeric safety need; `safe_idle` is a fallback
  action, not a safety meter. The current runtime now removes energy and sleeping; food and urgent
  weather exposure still constrain some actions.
- **Save compatibility policy remains open:** Load World now preflights saved
  checkpoints and required local model credentials, labels compatible,
  incompatible or unknown, and preserves and blocks only proven-unloadable
  entries. Unknown entries can still be tried. Longer-term migration and
  cross-release/mod compatibility policy remain undecided.
- **Generated vegetation art and propagation remain provisional — confirmed
  gap:** generated broadleaf/conifer and generic orchard-fruit tree objects now
  occupy at most one tree per tile. Wood trees visibly become stumps or
  replanted saplings; orchard trees show fruiting, picked and growing stages.
  The Godot client draws simple top-down shapes, not approved final textures.
  Orchard species, yield, seasonality and cultivation are not finalized;
  replanting spends the prototype generic seed on an existing depleted wood
  tree site, while planting on new tiles and species-specific seed/art remain
  unbuilt.
- **Mountains/peaks not seen in playtest — unverified occurrence, confirmed
  visibility gap:** generation does classify dry-land elevation ≥215 as
  mountain and ≥245 as peak, so the terrain kinds exist. Their distribution
  depends on seed and water classification, and the current overview has no
  elevation inspection. Check the reported world's seed/map before calling
  this an absence or changing thresholds.
- **Agent-chosen names feel too similar — playtest report, cause not proven:**
  the provider prompt now asks for a full name with a given name and
  family/surname, with a middle name optional, matching the accepted name
  format. World-specific cultural context remains undecided, and the whole
  existing-name list is intentionally not sent to the model. The accepted goal
  is exact full-name rejection while allowing similar-but-distinct names. The
  runtime only trims chosen names and rejects empty, control-character or over-48-character
  values; it does not reject duplicate full names or implement explicit
  collision retry/fallback. Exact-comparison normalization and retry/fallback
  details remain unresolved before implementation.

## Original reports and resolution evidence

- **New World preview HTTP 409 — fixed in server build, paired retry pending:**
  preview was rejected when the previously selected world was not paused,
  even though generating the preview does not change that world. The preview
  pause gate is removed; Create World still asks the host to pause before it
  switches worlds. A signed HTTP regression covers preview from a running
  world and confirms no world/catalog mutation. Computment's Windows retry
  remains the final live check.
- **Main Menu Settings clicks blocked — fixed in repository build, laptop check
  pending:** the title overlay stayed visible above Settings in input order,
  swallowing clicks even though the Settings panel drew in front. It now passes
  pointer input through only while Main Menu Settings is open. A headless Godot
  pointer-click smoke failed on the original path and passes with the fix;
  computment's Windows playtest remains pending.
- **Ground hover and marker hitboxes — fixed in repository build, laptop check
  pending:** bare ground gains a square tile outline; agent markers take hover
  and selection priority. Four same-tile markers fit into separate bounded
  cells at 12–48 px test zoom, so neighboring tiles do not hit them. The Godot
  UI smoke exercises the client map path; the revised Windows build still
  needs computment's playtest.
- **Main Menu World Settings leak — fixed in repository build, laptop check
  pending:** during a generated-world playtest, Main Menu → Settings exposed
  World Settings and its click entered the world unexpectedly. Main Menu
  Settings now hides the category and refuses world-specific settings without
  a loaded world. The Godot UI smoke checks both visibility and blocked
  navigation; the revised Windows build still needs computment's playtest.
- **Black terrain grid — fixed in repository build, laptop check pending:**
  removing the tile gap keeps square terrain tiles but eliminates the dark
  lines between them. Future textured terrain needs its own art review.
- **Agent hover and condition flicker — fixed in repository build, laptop
  check pending:** observation refreshes recreated every agent button each
  second, terminating hovered tooltips. Agent markers now update in place.
  Warmth/illness/diet/equipment are pinned outside the refreshed scrolling
  details so the condition block remains visible in the selected-agent card.

- **Provider-held tick — fixed in build, live verification pending:** hosted
  provider calls now sit outside the tick transaction. Tests prove an
  indefinitely pending model does not hold the world or deterministic agents,
  and pause/reload cannot admit the old answer. The VPS server has been updated
  without changing the paused save, pairing authority or provider settings;
  a resumed paired-client playtest remains before calling the defect fully closed.

- **AW-B020 — fixed:** rollback deleted placed buildings and production history
  belonging to the package, despite committed costs and outputs. Referenced
  packages now reject removal before any mutation; recorded settlement projects
  also block removal. A compatible conversion/migration workflow remains future
  work, not a hidden destructive fallback.
- **AW-B021 — fixed:** removing an unrelated unused package changed every
  remaining running production/crop job to cancelled. Unused withdrawal now
  preserves those jobs and reservations; regressions prove normal completion
  and restart afterwards.

- **AW-B018 — fixed:** household-owned production reservations outlived a dead
  worker, allowing unfinished crafting and crops to complete. Running jobs now
  cancel before completion and release remaining inputs. Six regressions cover
  both job types, completion-boundary death, already-finished work, safe logs
  and restart preservation.
- **AW-B019 — fixed:** map object labels ignored mouse hover and were recreated
  on every observation refresh. Resource/object labels now accept hover and
  retain identity across updates. An engine check verifies actual mouse entry,
  updated stock/help and removal of absent markers.

The reports below describe the original defects; their resolution is recorded
in the following repair evidence, not implied to remain open.

| ID | Priority | Area | Original problem | Acceptance condition |
| --- | --- | --- | --- | --- |
| AW-B001 | High | Content/gameplay | A fresh/default private world has no active starter package, leaving zero building and recipe definitions and no meaningful planning-provider work. | A new and existing private world receive a versioned starter content pack with useful materials, tools, shelter, storage, fire/workshop and crop/food recipes; planning candidates occur in normal play. |
| AW-B002 | High | Cognition/cost | Routine cognition can repeatedly pay for `safe_idle` when no meaningful observation changed. | Idle decisions are reused/coalesced until relevant state changes or a bounded reevaluation deadline; telemetry proves materially fewer no-op calls. |
| AW-B003 | Medium | Settings UI | “Inhabitant cognition” is visually dense and too wordy for a normal settings screen. | The panel uses compact role/provider/model/key rows, progressive help and clear saved/error state without losing the two-role distinction. |
| AW-B004 | Medium | Menu layout | With no inhabitant selected, the pause/menu content drops toward the bottom of the screen. | Menu position and layout remain stable regardless of world selection state and supported window size. |
| AW-B005 | Medium | Player observability | World Settings now shows installation-lifetime paid-call and token totals by provider/model, while the selected-agent tooltip shows the last accepted decision. A compact activity history of accepted/fallback outcomes is still missing. | A compact in-game activity view shows provider role/model, accepted/fallback outcome and bounded usage/latency without exposing prompts, raw responses or secrets. |

## First repair increment

- **AW-B001 — implemented:** normal host ticks now stage a validated,
  versioned starter package for both fresh and old saves. Four buildings and
  three recipes lead to construction, production and household food pickup.
  A dependency-linked supplement adds stone/fiber/seeds and hearth/weaving/grain
  content; persistent projects acquire inputs and other inhabitants share
  requested materials. The ordinary 1,000-tick settlement regression covers
  three-input gathering, sharing, completion, eating and public gratitude.
- **AW-B002 — implemented:** persisted idle observation keys prevent repeated
  calls for unchanged choices; a 300-tick deadline bounds reuse. A regression
  test proves four calls remain four through 61 ticks and a reload.
- **AW-B003 — implemented:** compact controls, progressive help, and explicit
  world/inhabitant target selection retain both cognition roles.
- **AW-B004 — implemented:** container-owned centering replaces cached-height
  placement. Godot checks selection and settings toggles at three window sizes.
- **AW-B005 — implemented:** selected-inhabitant cards show accepted provider,
  action and fallback; tooltips show model, role and available usage/latency.
  Existing server logs remain the detailed operator diagnosis path.

These are implementation evidence, not a claim that Living Settlement's whole
acceptance gate is complete. Live migration remains deferred while the owner
keeps the existing world manually paused.

## Survival integration regressions

- **AW-B006 — fixed:** production could reserve fresh ingredients that spoiled
  before completion, causing the tick to throw repeatedly. Completion now
  cancels that job, releases remaining reservations and emits
  `production_input_unusable`; the regression verifies subsequent ticks advance.
- Food availability, consumption and production input selection all exclude
  spoiled/ruined lots, so an older unusable lot cannot shadow usable stock.
- **AW-B007 — fixed:** construction could repeatedly select a site occupied by
  an idle inhabitant. Candidate sites now exclude other inhabitants and require
  a reachable route. An urgent-cold bootstrap regression proves protective
  construction and heating remain possible instead of permanent path retries.

## Runtime audit issues

- **AW-B017 — fixed:** descendant IDs contain
  colons, making directly interpolated building IDs invalid. Placement rejected
  after completed work. Noncanonical society IDs now produce stable hashed
  building IDs; existing canonical founder IDs retain their original mapping.
- **AW-B012 — fixed:** continuing project work
  could suppress a mentor's decision to answer a teaching request until expiry.
  Pending requests now interrupt project continuation for an independent response.
- **AW-B013 — fixed:** unreachable shared food could outrank nearby berries.
  Shared-food candidates now require a reachable pickup point. The former
  shelter/bedding rest defect became obsolete when sleep was removed.
- **AW-B014 — fixed:** construction rescanned
  sites during work and could abandon a legal current site when another person
  vacated an earlier tile. The current legal site now takes precedence.
- **AW-B015 — fixed:** finite wild timber left
  later generations without renewable building/fuel inputs. A dependency-linked
  coppice package adds delayed cultivation without refilling depleted wild nodes.
- **AW-B016 — fixed:** activity/condition logs
  split descendant IDs at the first colon. Known complete IDs now resolve event
  ownership; private prose remains excluded.

Focused regressions cover mentor interruption, legacy bedroll-save migration, no-sleep survival,
current-site completion, additive forestry activation/rollback and descendant
condition logs. A controlled parenthood scenario with opt-in fast biological
aging verifies birth, real feeding, two save/reloads, adulthood, earned training
and completed construction within 12,000 ticks, using deterministic providers.
It is not a claim that arbitrary seeds or population growth are balanced.

- **AW-B010 — fixed:** partnership kinship checks discarded death-ended
  parentage and only checked direct parents, allowing siblings after parental
  death and missing grandparents. Historical parentage now supplies sibling
  and full direct-ancestor checks; runtime scenarios cover living/dead
  intermediate relatives and incorrectly revoked legacy parentage.
- **AW-B011 — fixed:** generic relationship revocation could revoke biological
  parentage despite the immutable-history contract. Revocation and acceptance
  of fabricated parentage proposals now reject without changing edges/births;
  proposal creation remains restricted to the birth transaction.

- **AW-B009 — fixed:** revoking one caregiver relationship removed the adult
  from household caregiver tracking even when another dependent still had an
  accepted care edge. Projection now retains the adult until the last relevant
  obligation ends; a two-dependent regression covers both transitions.

- **AW-B008 — fixed:** an idle decision could cache choices created by another
  inhabitant after its observation was taken, suppressing a response to a new
  offer. The cache is now bound at observation enqueue; barter tests prove the
  recipient sees the new choice on the next tick, including across restart.

The following confirmed GitHub reports are tracked here as well; their issue
pages retain reproductions and regression requirements.

| Issues | Status in implementation | Defect |
| --- | --- | --- |
| [#107](https://github.com/compoodment/ClankerWorld/issues/107) | Fixed; activation and rollback regressions | Dependency quarantine/activation order; active dependents must be rolled back first |
| [#108](https://github.com/compoodment/ClankerWorld/issues/108) | Implemented; archive/restart and stale-cursor regression coverage | Unbounded checkpoint event history and rewrite cost |
| [#109](https://github.com/compoodment/ClankerWorld/issues/109), [#116](https://github.com/compoodment/ClankerWorld/issues/116) | Fixed; stalled-provider, pause, cancellation and concurrent-tick tests | Partial ticks and provider-held authoritative locks |
| [#110](https://github.com/compoodment/ClankerWorld/issues/110), [#111](https://github.com/compoodment/ClankerWorld/issues/111), [#112](https://github.com/compoodment/ClankerWorld/issues/112) | Fixed; boundary and clock-advance regressions | Expired barter acceptance and reservation expiry |
| [#113](https://github.com/compoodment/ClankerWorld/issues/113), [#114](https://github.com/compoodment/ClankerWorld/issues/114), [#115](https://github.com/compoodment/ClankerWorld/issues/115) | Fixed; atomic rejection and restore regressions | Asset ordering, conflicting shared charges and overflow |
| [#117](https://github.com/compoodment/ClankerWorld/issues/117), [#118](https://github.com/compoodment/ClankerWorld/issues/118) | Fixed; projection and restore regressions | Missing crop jobs and empty-population observation crash |
| [#119](https://github.com/compoodment/ClankerWorld/issues/119) | Fixed; declared/chunked/misreported size tests | Unbounded provider response buffering |
| [#120](https://github.com/compoodment/ClankerWorld/issues/120) | Fixed; 600 signed polls with no authority writes | Idle authority write amplification |

- **AW-B022 — fixed:** the client ignored placed-building observations, so
  completed structures were absent from the map. Stable markers now show their
  names, purpose and actual footprint; engine tests cover rendering and removal.

## Register maintenance

- Record only reproduced defects or demonstrated product gaps.
- Put intended features in the [vision ledger](vision-interview.md), not here.
- Never store credentials, private world contents or raw provider payloads in a
  bug report.
- When a fix lands, update the affected canonical current-state document in
  the same change and preserve detailed evidence in tests or the relevant
  implementation ledger.
