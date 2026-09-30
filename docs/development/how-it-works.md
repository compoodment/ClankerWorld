---
title: How the game works
type: architecture
status: active
updated: 2026-09-30
---

# How the game works

The current private alpha has three components. These are implementation
boundaries; [game design](../game-design/README.md) owns the intended experience.

| Component | Owns |
| --- | --- |
| `src/ClankerWorld.Simulation` | Rules, accepted world state, content, society, persistence and replay |
| `src/ClankerWorld.Viewer` | Headless server, signed owner API, clock, provider adapters and private installation stores |
| `src/ClankerWorld.GodotClient` | Windows game view, local preferences, observations and signed requests |

The host references the simulation, never the reverse. The simulation runs and
tests without Godot, a window manager, a live model or network access after
dependencies are restored. Godot does not reference the simulation: it uses the
host's versioned HTTP contract. Legacy web assets are diagnostic tools.

## Core rules

- **The server commits world state.** Client requests, model replies, mods and
  save files are untrusted input. Deterministic rules validate movement, work,
  resources, occupancy, ownership and effects. Use seeded random paths and
  keep external I/O outside authoritative transitions.
- **A tick commits all its accepted changes together.** A tick is one step of
  world time. Rejected, cancelled or stale model work cannot leave a half-applied
  action. Pause and loss of authenticated client presence invalidate in-flight
  work. A failed or low-confidence reply uses only explicit `safe_idle`;
  it cannot execute a strategic candidate or complete an instruction.
- **Time and spending require presence.** The host remains reachable while
  paused. Ticks and hosted model calls require an authenticated presence lease;
  the last disconnect closes the gate after about five seconds. Reconnecting
  performs no offline catch-up and does not clear a manual pause.
- **Knowledge belongs to each agent.** The player's map and observations are
  not automatically agent knowledge. Preserve ownership, source, confidence
  and correction history. A belief can be wrong without changing world facts.
  Long descendant identities remain intact in saved discovery provenance;
  knowledge keys and model-facing discoverer references use stable hashes
  only when the original identifier exceeds their size limits.
- **Credentials are installation state.** Keep keys and device authority out
  of saves, exports, observations and telemetry. See
  [device pairing](device-pairing.md) for transport and access rules.
- **Content is bounded and validated.** Current content definitions are data-only.
  Arbitrary executable agent code is disabled. The intended restricted script
  system remains design work, not permission to run generated code today.
- **Recovery preserves evidence.** Unsupported or corrupt saves must fail
  visibly without replacing valid state or inventing replacement credentials.
  [Saves and replay](saves-and-replay.md) owns formats, migration and backup rules.

## Clock and asynchronous decisions

The current host aims for one tick per real second. New worlds save 360 ticks
per day and a 40-day year with four ten-day seasons; lifecycle thresholds are
3/15/45/60 days. Load can affect real-time pace. The old development calendar
is not silently reinterpreted; the observation carries the saved clock values.

Hosted requests are dispatched after a committed tick and resolved at a later
tick boundary. The unresolved queue entry is saved; the HTTP task is not save
authority. Admission checks the request ID, provider/run epochs and current
candidate legality. An epoch is a generation marker that makes replies from
an earlier configuration or run obsolete. Other agents continue while one waits.

Owner instructions are suggestions (**Suggest** on the agent card,
`Suggestive`) or orders (**Order**, `MustDo`).
The model does not receive their text. `InstructionCandidate` reads whole words
only: *harvest* or *gather* means `harvest_food`; *berry* means `seek_food`;
*eat*, *food* or *hungry* means `consume_food`; and *go*, *travel* or *move*
means `seek_food`, so travel always heads toward food. A few plain inflections
such as *gathering* and *berries* also count.

A MustDo with no recognized action is closed when it is submitted: it is added
to the completed instructions with an `instruction_not_understood` event
(`<agent ID>:<instruction ID>`), which the Event Log shows. Each tick repeats
this check before scheduling, which also closes an order queued under earlier
matching rules. A closed order requests no decision and no longer blocks later
instructions to that agent.

Recognized MustDo instructions complete only when their requested legal action
actually progresses: acquiring food or orchard fruit, eating, or taking a travel
step. An unrelated action, blocked movement or unavailable food leaves the instruction pending,
including across reload. Travel completion here is one step, not a full-route
goal. Instructions to one agent apply in submission order, so a pending order
holds later ones back. A Suggestion completes at the agent's next accepted
decision, whatever that decision is.

A pending instruction prompts one fresh decision: it schedules cognition only
until the agent has an accepted intention observed after the submission tick.
After that, `NeedsCognition` applies its usual rules. For example, active agents
reevaluate every 30 ticks, and idle agents reevaluate when their legal choices
change or after 300 ticks. An order that cannot progress therefore cannot
request a decision, or a paid model call, on every tick.

Pause, quit and loss of presence cancel external work without inventing an
answer. Restore can retry a still-relevant saved decision. Synchronous fixture
methods remain for isolated tests; they are not the live scheduling path.

Tick-boundary faults hold the current in-memory state and pause the world.
Active checkpoint I/O/access failures retry only the retained save while paused;
success still requires a later explicit Resume. Other faults latch for operator
inspection. Startup corrupt-save failures remain separate. Logs use bounded
category/type/tick fields, not raw exceptions or file paths. Do not restart a
held process before preserving its unsaved state. Filesystem fault injection
does not establish arbitrary mid-tick rollback or crash durability.

## Model inputs, usage and memories

A personal-model request selects one legal candidate, not a free-form dialogue
turn. It now includes bounded actor-owned self context: name, life stage,
personality, aspiration, household, available warmth/illness and the latest
private thought. Absent fields remain unknown. Need scales are explained;
`hunger_basis_points` measures fullness (0 starving, 10,000 full).
Self context is included in the queued-observation digest. Nearby relationships,
carried inventory and current activity are not all provided by this slice.

Request text uses the game's own words (*agent*, *Town*, *House*), not the older
*inhabitant*, *settlement* and *camp*. The personal-model request does not send
the clock or the run and decision counters; admission uses them on the server.
The request still names households by their internal ID, and the Jev request
still carries those counters, because Jev's live service cannot be checked offline.

The response must select a legal candidate. Confidence below 0.5 permits only
the safe-idle fallback; probabilities are validated/retained but do not select
the action. A current reply that names a candidate never offered to it, or has
invalid response values such as confidence outside 0–1, also completes with
safe idle instead of repeatedly spending calls on the same decision. A choice
that was offered but is no longer legal stays stale; request, provider and run
identity checks still reject late replies without applying fallback.
A reply for a different request cannot cancel the agent's current pending choice
or replace its last accepted intention.
No adapter can turn provider prose directly into a world mutation.
Jev has a separate, smaller routine payload; it is not a persona/dialogue adapter.

Each agent retains up to eight private thoughts and exposes up to sixteen recent
non-forgotten social memories to owner inspection, including deceased profiles.
A separate belief record retains firsthand/hearsay/inference evidence and
superseded corrections. Inspection never broadcasts these records or turns
them into public events.

Personal requests retrieve at most four relevant own-memory/belief excerpts and
sixteen recent own-map facts. An existing Jev routine call may score up to twelve
previously unassessed records; only linked salience/confidence values are saved.
It adds no separate paid request or generated prose. Local retrieval works with
Jev off. Automatic experience capture and narrative summarization are unfinished.

Exploration can create a one-site field record or a map of up to nine sites.
Sharing nearby or bartering teaches only those sites to the recipient, retaining
the discoverer and source agent. It does not grant access to unrelated knowledge.

If intervening legal movement interrupts outward scouting, a new outward path
starts at the actual position without inventing missing steps. On the return
leg, recorded visited tiles are waypoints: the actor routes from its current
position around occupants without restarting the outing. Completion requires
reaching the original start, including after reload. A return that remains
blocked still ends through the existing bounded wait/abort behavior. Visited
facts remain personal knowledge.

The owner's model picker shows the game's own list for each provider,
`ProviderModelCatalog.Curated`: newest generation first and, within a generation,
larger models first. Add new models there in their place. When a key is known,
the host checks it against the provider's model-list route (OpenAI
`/v1/models`; Ollama Cloud `/api/tags`, then `/v1/models`) using
the saved key, a named key slot, or a key pasted for that check only, which is
not stored. Keys never return to the client. Listed models the key's route
doesn't include are marked unavailable; names are compared without Ollama's
`:cloud`, `-cloud` or `:latest` endings. If the key can't be checked, the whole
list stays usable and the reason is shown. A new agent starts on the provider's
default model when the key can use it. If the key can't use a new agent's
starting model, the picker selects nothing and asks the owner to choose, so no
other model, possibly a costlier one, is chosen for them. An existing or
hand-picked model the key can't use stays shown, greyed, with the same request.
Checks are cached per key for ten minutes, time out after eight seconds and are
not model calls, so they do not count toward the usage cap below.

An installation-local usage file reserves every hosted attempt before HTTP work.
Concurrent requests share its optional lifetime attempt cap. Failure, retry and
abandonment keep their spent allowance; only known token counts are added.
Deterministic choices consume no attempt. Reaching the cap persists a pause;
changing allowance and resuming are separate owner actions.

Unreadable or inconsistent accounting leaves the host reachable with paid work
blocked. Preserve the damaged file; changing the cap cannot bypass it. Writes
use unique private temporary files flushed before replacement. Failed reservation
writes do not increment the in-memory total or dispatch a model call. This does
not promise directory-fsync power-loss durability or provider-invoice parity.

## Maps, movement and terrain
Small and Medium generated maps are connected to the normal world path. The
older tiny map remains a compatibility fixture/world; generation is no longer
merely a separate primitive. Large/Huge/Mega generator outputs do not imply
playable storage, observation or performance support.

Generation saves climate zone, elevation, hydrology, surface and vegetation
cover separately beside the stable terrain summary. New World preview and
creation select saved hydrology revision 1: inland lakes retain a connected
low basin of at most 0.5% of map area, with displaced open-water area added
along ocean shores. This is provisional visual tuning, not a new player slider.
Rivers terminate at lakes or oceans; this revision does not invent lake outlets.
The [Small/Medium comparison](assets/inland-water-comparison.png) shows seed
`inland-water-0` at 45% water with wrapping (historical left, revised right).
It is a generator-layer rendering, not a Godot screenshot or native playtest.
Revision 0 (including absent revision fields) retains the historical generator;
unsupported revisions are rejected rather than substituted. Individual trees are
objects/resources. Old generated worlds recover missing layers from saved
deterministic options and retain their original resource layout.

Movement reads water and elevation; build eligibility also reads surface.
Diagonal foot steps cost 141% of cardinal entry and require both shoulder tiles
to be passable, including clear occupancy during a move. One-tile rivers allow
bank-to-bank crossing at half dry-ground speed, not travel along the river.
Mountains are slower to cross and cannot be built on; peaks are impassable.

Resources are placed in bounded 16×16 cells with surface/cover biases, then
recorded in their actual 64×64 chunks. Sparse/Normal/Abundant provisionally
attempt alternating cells, one site per cell or two sites per cell. Food choices
use the actor's foot-accessible terrain component and skip sites whose current
occupied routes cannot reach harvesting range. Adjacent food needs no route
search. Candidate generation searches only when gathering or a food instruction
needs a source, and stops at the first reachable site. Caregivers use the same
selection. Immutable map connectivity is cached once per map; movement rechecks
occupancy before each step. Project resources likewise use the actor's current
position and reachable harvesting range. Boat access remains unfinished.

Godot draws camera-visible tiles from a compact terrain index and samples it
for the overview. It does not create a Control per tile. Generated terrain uses
row-major packed bytes, with separate layer digests. Signed cache claims omit
unchanged map data only when world and digests match. Initial/changed maps and
some control receipts still send the whole map. Viewport/chunk transfer remains
unfinished.

The regional-weather prototype saves an episode for each 32×32 region. Ordinary
episodes last one-quarter to one saved day; storms last at most three-quarters
of a day and allow no new storm for at least half a day afterward. Small fixture
calendars round durations to whole ticks. Conditions use the region-center
climate, previous condition, seed and a shared pre-transition neighbor snapshot.
No model call or camera state participates; drifting visuals are not fronts.

Weights are provisional for [the episode experiment](https://github.com/compoodment/ClankerWorld/issues/375),
not approved rain balance. Wet neighbors add at most four rain-weight points;
a reduction to base precipitation weights offsets that bonus. The fixed-seed
comparison is recorded in [the prototype report](weather-episode-prototype.md).
Local survival and crop exposure use the active episode. Soil moisture retains
the older three-day daily-weather estimate in this first experiment; it is not
a saved moisture grid or an episode-integrated rainfall model. Existing saves
keep their active weather when loaded; the first resumed tick imports it into
an episode. See [save handling](saves-and-replay.md#regional-weather-episodes).

The generator vendors [FastNoiseLite](../../src/ClankerWorld.Simulation/ThirdParty/FastNoiseLite/README.md).
Its drainage approach draws on [Red Blob's noise guide](https://www.redblobgames.com/maps/terrain-from-noise/),
[Mapgen2](https://github.com/redblobgames/mapgen2) and
[Mapgen4](https://github.com/redblobgames/mapgen4/blob/master/map.ts). These sources
explain the approach; they do not settle open terrain design or performance.

## Towns, building sites and death

Paused founder setup creates one First Town when the owner accepts its five-building
site. Founders become residents; Start World changes founding state to founded.
Later placement inside the saved border establishes residence; walking does
not change it. Children inherit the resident parent's Town; death removes the
resident. Owned-building placement establishes household membership.

The current Town is provisional and uses a rectangular border. The chosen
origin is saved where present; older worlds retain their camp-derived border.
Assigned buildings expand it with a one-tile margin, clipped at map edges.
There is no wrapped-seam claim geometry, competing-claim graph or automatic
second-Town founding. Filters display saved Town and household-building facts,
not invented general land ownership.

`TownLayoutService` captures one immutable layout context per decision and
normally offers at most five legal sites with reasons for footprint, route,
resources, purpose and compact growth. A model selects a site-specific candidate
or chooses another action; refusal starts no project. Accepted projects retain
their tile. If it becomes illegal, the project blocks and retries after sixty
ticks. An unchanged idle choice is reconsidered after 300 ticks, sooner if
urgent needs or legal choices change. Weights and retry values are provisional.

For workstation recipes, site selection checks the actor’s current walking route as well as
ownership and unused production capacity. An occupied or inaccessible workstation
does not hide another reachable one. Movement’s existing diagonal and household
access rules still apply. If no site is reachable, an existing project uses its
blocked/reconsideration path rather than travelling toward an unreachable tile.

Recipe preparation and production must use the same actor/building owner.
Direct production requests use the same age restrictions as autonomous choices:
only adults and elders may start workstation recipes or crops. A request for an
infant, child or adolescent is refused before reserving inputs or changing jobs.
Household workstation inputs must be present at the actual building; stock
elsewhere in the household is not on-site stock. Missing inputs block the
project under its existing retry rules, without granting another household's
materials or implicitly transporting remote goods.

Barter choices and offer creation require both agents to be adults or elders.
Infants, children and adolescents cannot receive an offer that reserves their
belongings while they have no legal trade response. The society transaction
checks age before reserving either party's stock. Household and organization
parties keep their existing inventory rules. Previously saved offers retain
their normal withdrawal and expiry behavior; loading does not rewrite them.

Death archives the last physical state and frozen age, then removes the active
actor. Existing personal inventory can be frozen in estate escrow. One bounded
post-death model decision can choose a living heir for the whole estate; society
checks the frozen lots and recipient. Failed/interrupted choices use the default
household path. Settlement applies once. See [saves and replay](saves-and-replay.md)
for pending-will restore behavior.

New proposals for Shelters, Storehouses, Cooking fires and Stone hearths are
retired. Existing buildings, projects and recorded proposals remain for old-world
compatibility. House fires supply heat; the Weaving frame remains the temporary
clothing source while Tailor Shop production is undecided. General invention is
later Workshop work.

## Approved authored assets

Paused authoring can reference only an exact normalized `assetId` and lowercase
`sha256:<64-hex>` digest in the immutable host-approved catalog loaded at startup.
A missing catalog denies all references. Malformed, ambiguous or unknown-schema
catalogs prevent startup. Device requests cannot edit it or upload asset bytes.
This is a narrow reference rule, not the future complete art-generation pipeline.

## Runtime logging

Changed gameplay loops, provider adapters, persistence boundaries and lifecycle
gates need low-noise structured logs at meaningful outcomes. An operator should
be able to tell what was attempted, accepted, rejected or fell back, and why.
Use stable event names and named fields such as tick, entity/request ID,
provider role/model, legal intention, outcome, latency and bounded usage.
Log transitions and decisions, not render frames or idle polls. The player
Event Log and unread count select known player-facing events and public
milestones explicitly; unknown diagnostics do not become player text.

Never log API keys, authorization headers, signatures, credential-bearing URLs,
prompts, raw provider request/response bodies, hidden reasoning or unbounded
player/model text. Memory/belief/map telemetry uses safe IDs, counts and outcome
metadata, never private prose or map contents. Logs are derived telemetry, never
simulation authority or required save state. Observability tests must prove both
useful signal and absence of representative secrets.

Event descriptions resolve complete agent and Town IDs from the owner snapshot;
colons inside those IDs are part of the identity. Food yields and Town membership
fields are read separately. Existing entries use the current saved name, including
deceased profiles. Hosted-decision and Town telemetry likewise keep complete IDs.
These readers do not rewrite accepted event details or change save/replay formats.

## Development and finished distribution

Development currently uses the private server. The intended first finished
release runs the same authoritative simulation on the player's Windows PC,
with local saves and keys. Embedded host versus companion process remains open.
See [where the game runs](../game-design/world.md#where-the-game-runs).

## Material gathering

Material gathering selects available resources reachable from the acting agent,
then checks an actual unoccupied route into harvest range. Heating, project
assistance and Blacksmith ore use this selector; they do not require a path to
the original map anchor. This does not change fuel duration or harvest yields.

## Advanced generation controls

New World defaults to 50% water with a 20–80% range. `GenerationAmount`
controls forest cover, mountain relief and river abundance independently;
Normal is zero and omitted from saved JSON, preserving historical default
settings. Low/High adjust the forest rainfall threshold (175/125), upper
elevation relief, and river catchment threshold (288/72). Normal keeps
150 and 144 respectively. These are relative presets, not promises of exact
forest or mountain percentages. Resource abundance retains its existing
Sparse/Normal/Abundant saved values; the UI labels them Low/Normal/High.

Owner world-creation signing uses payload v2 to bind all settings. Preview and
Create use the same validated options and digest; old clients need an update.
Small and Medium remain the only playable sizes; no continent-count control
is exposed for them. Existing saved water settings are not rewritten.

## World-list requests

The manual Save World and Load Save dialog owns one list read per opening.
Closing it, creating, overwriting or deleting a save, or starting another opening
cancels the previous read. Late success and failure replies cannot replace current rows,
selection or status, including across a world or pairing change. Clearing or
rebuilding rows recomputes Load, Overwrite and Delete availability. Creating a
new save remains available while listing is slow.

Load World keeps a visible checking state until its signed catalog request
finishes. Back cancels the client request; a late response cannot overwrite a
newer list or New World screen. Results trigger layout after population so the
first opening can display them. Compatibility still comes from the host's
checkpoint/history/configuration assessment; no compatibility cache or unchecked
"compatible" shortcut was added. Open captures the chosen world's ID before
pausing, so a later catalog refresh cannot change its target. Open, Create and
Delete share the owner-action gate; selecting a different row cannot re-enable
Open or Delete until the current action finishes. Cleanup checks the current
selection rather than a row retained across an await.

Client cancellation does not interrupt a host
assessment that already holds its mutation lock.
