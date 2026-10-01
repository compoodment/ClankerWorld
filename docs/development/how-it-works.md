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
  work. A failed or unusable reply uses only explicit `safe_idle`;
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
`Suggestive`) or orders (**Order**, `MustDo`). The next ordinary personal
planning request for that agent can include the exact original words with an
outside-observer label. The request contains only messages for its target
agent. Guidance bypasses Jev's routine route, while adults still use their
normal planning assignment or inherited world planning provider. A child
without an explicit personal-model choice and an agent whose planning model is
set to deterministic stay local; neither receives a forced hosted call.

`InstructionCandidate` recognizes only a bounded food-task set: eating food,
seeking a food source, and harvesting food. Harvest and travel orders must name
food (or a supported food resource); explicit resource names must match a
complete identifier, and food kinds must match that resource. Unsupported
objects or operations, mixed tasks, unknown explicit targets, and invalid
quantities are rejected as not understood rather than mapped to a nearby
candidate. A recognized order retains the player's original text and the
understood action, but the simulation still checks legal choices and requires
the requested physical effect before recording progress. Names in the prompt
do not create map knowledge. Optional observer replies are tied to the exact
message ID and stored separately from private thoughts and conversation
speech. Local deterministic decisions do not mark messages as heard.

A MustDo with no recognized action is closed when it is submitted: it is added
to the completed instructions with an `instruction_not_understood` event
(`<agent ID>:<instruction ID>`), which the Event Log shows. Each tick repeats
this check before scheduling, which also closes an order queued under earlier
matching rules. A closed order requests no decision and no longer blocks later
instructions to that agent.

Recognized MustDo instructions complete only when their requested legal action
actually progresses: acquiring food or orchard fruit, eating, or taking a travel
step. An unrelated action, blocked movement or unavailable food leaves the
instruction pending, including across reload. Travel completion here is one
step, not a full-route goal. Recognized orders to one agent apply in submission
order, so a pending order holds later orders back. Suggestions do not block
orders. A suggestion completes only after the addressed personal model accepts
a request containing it; local choices and provider failures do not claim it
was heard.

A newly submitted message triggers one fresh cognition request. Its prompt
marker only prevents a new request every tick; it is not a read receipt. The
same pending message remains available on the agent's later ordinary planning
requests until a personal-model result is accepted. After the fresh request,
`NeedsCognition` applies its usual rules. For example, active agents reevaluate
every 30 ticks, and idle agents reevaluate when their legal choices change or
after 300 ticks. A blocked order therefore cannot request a paid model call on
every tick.

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
carried inventory and current activity are not provided.

Newly placed adults get one opportunity to choose personality and aspiration
in the existing first personal-model action reply. Optional `chosen_personality`
and `chosen_aspiration` strings are trimmed, limited to 256 characters and
refused if they contain control characters. An accepted personal reply consumes
the opportunity even if either field is missing or invalid; the placeholder
stays without an additional model attempt. Later replies cannot overwrite it.
The pending opportunity is checkpointed, so pause/reload discards late replies
and preserves an unconsumed choice. It selects the personal planner rather than
Jev's routine router. Choice events contain only the agent ID; chosen text stays
in that agent's saved state and later self context, not runtime logs. Children
and identity changes later in life remain separate work.

Request text uses the game's own words (*agent*, *Town*, *House*), not the older
*inhabitant*, *settlement* and *camp*, including plurals. Both adapters omit the
clock and run/decision counters; admission uses them on the server. Households
and Towns use their recorded names. Candidate destinations use a readable name
where one is available, while the legal option IDs remain unchanged. Jev stays
narrow: choosing a legal action and scoring existing memories, without persona.
Retrieval ranks which memories to include before sending the request. Its Jev
importance scores are not sent to the personal model: those scores select the
context, rather than adding facts about the remembered event. Belief provenance
and belief confidence remain explicit.
When an unnamed agent is asked to choose a full name, the personal-model
request includes a soft first-letter hint derived from that agent's stable ID.
The hint stays the same if the request is retried, is computed per agent, and
does not reveal anyone else's name. If a current reply supplies a valid full
name already used by another agent, the scheduler queues one extra metered
personal-model request. That request marks `name_retry` and says the chosen
name is taken, but it still does not include anyone else's name. Names are
compared after Unicode normalization, case folding and collapsing whitespace;
deceased agents count too. A second duplicate, a missing or invalid name, or an
unusable retry reply leaves the placeholder for the player to rename. The
retry marker uses the existing saved cognition queue trigger list, so it
survives pause and restore without a new per-agent save field. The name check
is separate from action admission: a valid name from a current legal-choice,
low-confidence or rejected-action reply is kept, while malformed replies and
stale replies cannot name the agent.

The response must select a legal candidate. Any finite confidence from 0 to 1
is accepted; confidence does not veto the choice or a valid chosen name.
The personal prompt asks for no probability map. Both parsers accept its
absence; a supplied valid map is retained in the existing response format,
but does not select the action. A current reply that names a candidate never offered to it, or has
invalid response values such as confidence outside 0–1, also completes with
safe idle instead of repeatedly spending calls on the same decision. A choice
that was offered but is no longer legal stays stale; request, provider and run
identity checks still reject late replies without applying fallback.
A reply for a different request cannot cancel the agent's current pending choice
or replace its last accepted intention.
No adapter can turn provider prose directly into a world mutation.
Jev has a separate, smaller routine payload; it is not a persona/dialogue adapter.

Each queued decision makes one hosted attempt. Malformed replies, timeouts and
transport failures do not trigger a repair call or an unstructured alternate
request. Every billable attempt is reserved in the durable usage store before
HTTP; an absent key or exhausted limit sends no request. Future world decisions
still follow the normal reevaluation rules. The separate, explicitly agreed
duplicate-name request in [#456](https://github.com/compoodment/ClankerWorld/issues/456)
is not a repair of an action reply.

The owner agent card reports a bounded model status: ready, waiting, canceled,
missing key, limit reached, unusable reply, timed out or unavailable. The latest
attempt and last accepted model choice are saved separately, so safe-idle
fallback does not erase the previous accepted choice. Pause/disconnect records
cancellation; refreshed views and current-format reload preserve it. Exception
messages, provider bodies and keys do not enter these fields.

The personal adapter sends JSON-object response mode and omits temperature and
output-token limits. The owner's 16-call sample in
[#457](https://github.com/compoodment/ClankerWorld/issues/457) used 44,285 output
tokens (about 2,768 per call), nearly its 43,576 input tokens. This does not
establish a safe universal output cap, so the cap stays unset; output billing
and truncation remain model-dependent. There is no smaller candidate list or
wider Jev role in this change.

Each agent retains up to eight private thoughts and exposes up to sixteen recent
non-forgotten social memories to owner inspection, including deceased profiles.
A separate belief record retains firsthand/hearsay/inference evidence and
superseded corrections. Inspection never broadcasts these records or turns
them into public events.

Ordinary conversation is a host-controlled activity, not free-form world
commands. The normal provider router requires an explicit personal planning
assignment for the current speaker and sends only that speaker's identity and
the accepted public history. A proposal and acceptance each use that agent's
saved daily allowance. The host admits at most two pending conversation calls
per world tick and six alternating public turns per conversation, followed by
one wrap-up. Responses must match the saved conversation revision, run epoch,
request and speaker; stale or malformed replies are not admitted. Public turns
carry a bounded list of agents who were actually close enough to hear. A
separate memory extraction gives those listeners hearsay claims tied to the
turn ID, with duplicate and owner checks. Private thoughts, provider payloads
and unaccepted replies never enter that history. Prose has no world effect; the
only ordinary-dialogue effect is mutual trust, offered during wrap-up and
applied only after both participants accept the same proposal.

The saved revision, status and next-speaker fields are the conversation cursor.
The checkpoint keeps the complete admitted history (up to six public turns and
one accepted wrap-up), and each later provider request receives the committed
public history so far. The wrap-up request receives the six public turns; a
pending answer is never included or saved.

Saving by itself keeps an unaccepted invitation pending, with its original
deadline and the inviter's saved daily allowance. It has no current speaker;
only acceptance assigns the first turn. When the 64-session save limit is
reached, the oldest closed session is removed before a new one starts. A
listener's private hearsay claim, its correction links and any memory-compaction
reference remain saved. Only the link from that private claim to the removed
public turn is cleared; its statement and speaker attribution stay private to
its owner, while the old turn text leaves the shared conversation history.

Pausing, disconnect, a provider failure, urgent need, separation or an
unavailable participant interrupts or closes the activity according to its
state. A save contains accepted bounded turns and daily allowances, not a live
provider task. Restore leaves accepted active conversations suspended and clears
any earlier resume choice; no model request starts until both participants choose
to resume again. A pending invitation retains its original deadline and still
requires normal acceptance and the invitee's daily allowance. Both agents receive
the same public wrap-up and proposed effect before accepting. Lessons wait while
either participant is talking. Conversation logs record only purpose, bounded
turn count, latency and usage totals, including reported usage for rejected speech.

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

**Test model** is a separate owner action in Add Agent and an agent's Model
panel. It sends one request through the same OpenAI-compatible personal
decision adapter used in play, including the required JSON response format.
It sends no temperature or output-token limit. The host durably reserves a
paid-call allowance before sending; every result after that point, including a
timeout or rejected format, counts as one attempt. The check never tries an
alternate request format, changes provider settings, or saves a pasted key.
The response contains only a bounded result such as ready, missing key, usage
limit, unsupported format, timed out, unavailable, or unusable reply. It does
not include provider error text or response bodies.

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
A built bridge makes its river tiles walkable at dry-ground speed, end to end
along the bridge only (see [Roads and bridges](#roads-and-bridges)).
Mountains are slower to cross and cannot be built on; peaks are impassable.

Resources are placed in bounded 16×16 cells with climate and cover biases, then
recorded in their actual 64×64 chunks. Sparse/Normal/Abundant provisionally
attempt alternating cells, one site per cell or two sites per cell. Food choices
use the actor's foot-accessible terrain component and skip sites whose current
occupied routes cannot reach harvesting range. Adjacent food needs no route
search. Candidate generation searches only when gathering or a food instruction
needs a source, and stops at the first reachable site. Caregivers use the same
selection. When fetching food for an infant, caregivers also skip household
food stores whose collection point has no currently unoccupied route. They try
other household food before gathering wild food; inaccessible stock is left
untouched. This does not change household ownership or food-policy rules.
Immutable map connectivity is cached once per map; movement rechecks
occupancy before each step. An agent gathering for its own project, and the check
that a project's inputs exist, still need a route from the original camp. Heating,
helping another agent's project and Blacksmith ore use the actor's current
reachable area (see [Material gathering](#material-gathering)). Boat access
remains unfinished. Trees and planting are described in
[Trees and planting](#trees-and-planting).

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

The October 1 default preset reduces rain, storm and snow weights by exactly
one quarter after climate and latitude adjustments. The removed share goes to
clear and cloudy weather. Integer weights use four units per old weight point,
so small weights keep the same exact reduction. Explicit custom weather
profiles retain their declared weights. Episode neighbor and persistence
bonuses use the same weight scale. Further tuning remains provisional in
[#204](https://github.com/compoodment/ClankerWorld/issues/204).
Wet neighbors add at most four rain-weight points;
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

### Terrain layers, sand, groves and hills

`GeneratedCampMapGenerator` builds a map in four separate, deterministic passes,
so each can be checked on its own
([#461](https://github.com/compoodment/ClankerWorld/issues/461)):

1. **Elevation and water** come from the geography generator.
2. **Surface.** Sand comes from two explicit rules, never from simply touching
   water: *desert* (dry climate, rainfall 42 or less) and *beach* (land below
   elevation 175, beside the ocean, where a seeded beach-noise field reaches
   0.3). Rivers and lakes keep grass banks except where a desert reaches them. *Forest floor* marks groves: forest
   tiles whose seeded grove noise reaches 0.15, keeping only the 24 strongest in
   each 64×64 chunk and none in the 6×5 starting clearing.
3. **Vegetation eligibility.** Cover follows climate and the surface beneath
   it, so a beach carries no forest or grass cover. Dry scrub and cactus cover
   on desert sand follow the dry climate; cacti additionally need hot desert
   sand, never dry scrub, ordinary beaches, water or rock.
4. **Object placement.** The starter berry patch, tree and grain seed patch must
   stand off sand. Every forest-floor tile then gets a tree. Wild sites come
   next, then rare deposits, scattered trees and orchards. A tree or plant
   picks another tile rather than stand on sand, water or mountain rock, and
   map acceptance refuses one that does. Stone outcrops and clay banks are not
   plants and may stand on sand. Eligible forest-grass tiles have a provisional
   35% tree chance; meadows have a 2% chance. Seeded ranking spreads trees
   through any chunk that reaches its budget instead of filling one edge.

Generated sites use at most 1,016 of a chunk's 1,024 resource slots; the validated
configuration ceiling is 2,048. The remaining eight slots stay
free for sites the running world adds, such as the three settlement sites
beside the first Town. Grove surfaces still use the 24-tile limit; the larger
budget supplies the denser trees on forest grass.

**Hills** are dry land below mountain height (215), at least 190 high and within
three tiles (counting diagonal steps as one) of a mountain or peak. They are a
visual layer only: nothing is saved for them, they keep their own surface, and
they cost the same to walk and build on as grass. `SeededMap.IsHillAt` and the
Godot client's `WorldTerrainMap` apply the same rule to the saved elevation and
water layers. The client draws a relief overlay on hill tiles, warms their
overview color and shows "Landform: Hills" in tile inspection. Hill travel cost
and passability are not decided.

All of these numbers are **provisional**. They were chosen from fixed-seed
measurements, not owner-reviewed maps, and live in `TerrainPlacementRules`.
Current fixed-world tests require 25–45% forest-grass tree coverage, and every
forest-floor tile holds a tree. Hills remain about 0.3–2% of dry land at Normal
mountain relief.

The October 1 measurements used .NET 10.0.401 and Godot 4.7.2 under WSL, with
two .NET processors. Both Small maps are 256×128, use current hydrology and
the Balanced visibility rules, and were measured before and after this change:

| Seed | Resources before → after | Most resources in a chunk before → after | Forest-grass tiles with trees before → after | Ocean shore with sand before → after | Median generation ms before → after |
| --- | --- | --- | --- | --- | --- |
| `probe-a` | 391 → 1,748 | 56 → 420 | 76/3,913 (1.9%) → 1,377/3,949 (34.9%) | 195/851 (22.9%) → 73/851 (8.6%) | 119.5 → 207.8 |
| `probe-b` | 364 → 1,969 | 56 → 558 | 87/4,500 (1.9%) → 1,636/4,586 (35.7%) | 246/1,226 (20.1%) → 90/1,226 (7.3%) | 146.5 → 116.5 |

Generation used one warmup and five timed runs, reporting the median. Times
include shared-machine scheduling noise; the faster second map does not prove
a generation speedup. The separate uniform-Dry fixture had 39 cactus tiles,
all on desert sand, compared with 48 before, nine outside desert sand.

Godot measurements execute the actual terrain layer's `_Draw` and time CPU
draw-command submission. Each camera uses five warmups and 20 measured draws:

| Seed | Full overview median ms before → after | Detail median ms before → after |
| --- | --- | --- |
| `probe-a` | 3.61 → 6.26 | 6.73 → 6.07 |
| `probe-b` | 3.14 → 6.65 | 8.56 → 8.51 |

The overview covers all 32,768 tiles at size 2; detail covers 96×48 tiles at
size 16. These headless measurements exclude GPU work and frame presentation;
they do not establish Windows frame rates or a hands-on playtest. Denser
resources increased overview CPU cost in this sample.

To repeat generation measurements and write public map fixtures:

```bash
bash scripts/measure-map-generation.sh /tmp/clankerworld-map-measurements current
godot --headless --path src/ClankerWorld.GodotClient -- --measure-map-draw /tmp/clankerworld-map-measurements/current-probe-a.json
```

Use the pinned engine and build the client scripts first. The draw probe exits
with an error if the engine never calls `_Draw`. It does not start a host or
use private saves. Across a separate four-seed, 160-day episode sample, wet
time fell to 78.1% of the previous Tropical level, 70.9% Dry, 75.4% Temperate,
73.3% Cold and 75.7% Polar. Episode durations and neighboring weather explain
why time spent wet is not exactly the same as the base-weight reduction.
`TerrainPlacementTests` reports terrain coverage for shoreline, inland-river,
wrap-seam and mountain-edge cases. The script and Godot probe above report
generation and draw timings.

The [terrain comparison](assets/terrain-placement-comparison.png) shows a
90×50-tile area of seed `river-world-a` (Small, 50% water, wrapped): the
previous generator on the left, these rules on the right. Dark green is forest
grass, the darker patches with dots are groves, beige is sand, brown is dry
scrub, grey is mountain and the warm band around it is hills. Dark dots are
trees, red dots plants and grey dots stone. It is a generator-layer rendering, not a Godot
screenshot or native playtest.

## Towns, building sites and death

Paused founder setup creates one First Town when the owner accepts its five-building
site. Founders become residents; Start World changes founding state to founded.
Later placement inside the saved border establishes residence; walking does
not change it. Children inherit the resident parent's Town; death removes the
resident. Owned-building placement establishes household membership.

`FirstTownLayoutPlanner` lays the first Town street first, using
`TownStreets`. A main road runs both ways from the chosen site along its most
open line, bending in 45° steps, never more than 45° from the heading it set out
on. Side streets leave it every three to five tiles, mostly at right angles. A
street takes a diagonal step only where both corner tiles are clear ground, and
keeps a one-tile gap from other streets. The five buildings then take the
nearest lots whose door opens onto a street, with a one-tile gap between
buildings. Finally every dead end is cut back to three tiles past the last door
on it, and branches with no door are removed. The same site on the same map
always gives the same layout.

The Town border is all land within about three tiles of the Town's buildings
and Roads (three straight out, rounded at the corners). It leaves out water and
is clipped at map edges. It grows around each building that joins the Town and
around that building's new Road. The saved border is authoritative: loading
checks that it lies on the map and covers the Town's origin and every assigned
building. Older worlds keep their saved rectangular border. There is no
wrapped-seam claim geometry, competing-claim graph or automatic second-Town
founding. Filters display saved Town and household-building facts, not invented
general land ownership.

A building that joins a Town later is joined to the Road network from one of the
tiles directly beside its footprint. That starting tile is saved as the
building's entrance, and the map draws the door on that side. The first Town's
planner saves the entrance of each lot it chose. A building without a Road has
no entrance and shows its door in the middle of its south side.

Towns grow along their streets. `TownLayoutService` gives a site whose door can
face an existing Road a `road_frontage` bonus. A building beside a Road needs
no new Road. Otherwise a new side street runs to the nearest Road. It stays
inside the (already grown) border where it can, may step diagonally where both
corner tiles are clear, and pays extra for each tile beside an existing Road,
so it meets streets rather than running alongside them. Then every dead end
fewer than three tiles past its nearest door carries on in its own direction
where the land allows (`town_road_extended`). The border grows around all the
new Road tiles.

`TownLayoutService` captures one immutable layout context per decision and
normally offers at most five legal sites with reasons for footprint, route,
resources, purpose and compact growth. A model selects a site-specific candidate
or chooses another action; refusal starts no project. Accepted projects retain
their tile. If it becomes illegal, the project blocks and retries after sixty
ticks. An unchanged idle choice is reconsidered after 300 ticks, sooner if
urgent needs or legal choices change. Weights and retry values are provisional.
Building plans follow what a household needs, not a role. An adult whose
household lacks a House, Farmhouse, Blacksmith, Silo or Tailor Shop is offered ranked sites
for it once the household has the build costs in hand: stock the household
owns anywhere, plus what its members carry. Each kind is planned at most once
at a time and a household never holds two of a kind; a second member choosing
the same kind in the same tick is refused, and a project stops if its household
comes to hold that kind. Only a household holding a Farmhouse plans a Silo, and
its sites must lie within two tiles of that Farmhouse, counting diagonals, with
touching sites ranked first. This provisional reading of "next to" keeps a Silo
possible when Roads, resources or later buildings take the tiles beside it. While the first building it still needs lacks a material, one
adult at a time is offered to gather it from a reachable source. Buildings the
Town shares, including a new Warehouse, are never offered to a household. The
kinds are listed in `HouseholdBuildingKinds`, which already names the Store so
it follows the same rules once its content exists.
Harvests remain household-owned lots on their actual field tile. An adult
carries a load of at most four raw crops or planting items to the household's Farmhouse or Silo.
Each holds a provisional 96 items, counting deliveries already on their way;
pickup and delivery both check remaining space. Grain prefers the Farmhouse,
while other farm stock prefers the Silo. Ready-to-eat greens and fruit go to
the household's House. Neither stock nor ownership moves
remotely.

**Housing requests** (`SettlementHousing`). An adult whose household holds no
House has a saved `Housing` record on their physical state: a pending request,
recent refusals and the current blocker. Each tick `MaintainHousing` resolves
requests, then recomputes the blocker and appends `housing_blocked` when it
changes. The blocker codes are `no_household`, `no_authorized_home` (the
household can plan or is building a House), `missing_materials`,
`no_legal_site` (the household has the build costs but `TownLayoutService`
ranks no site) and `awaiting_answer`. The code is shown on the owner's agent
card and sent to the agent's own model as a `housing` line in its self context.
An adult with no household is offered `household_ask:{household}` for each
household that holds a House in the same Town, has an adult who can answer and
has not refused within the last two world days. Asking records that household's
adult members in the request, which expires after 120 ticks like other
proposals. Adults who join the household or reach adulthood while it is pending
must also answer; existing answers are retained and adults who die or leave no
longer need to answer. Each current adult is offered `household_admit:{applicant}` and
`household_refuse:{applicant}` and cannot continue a project or lesson until
they answer. An ongoing lesson waits while either participant owes a housing
answer, retaining its progress and already learned skills.
One refusal by a living member ends the request; when every living
member has agreed, `SocietyFixture.JoinHousehold` records the membership and
`household_joined` is appended. A refusal or an unanswered request is remembered
as a refusal for the cooldown. The request grants nothing while pending: stock,
shelter and route rules still check household membership. Adults who already
have a household are never offered a request in the current implementation.
[Household departure and solo formation](../game-design/towns.md#household-membership),
including [ownership, collection access, the food allowance and dependent care](../game-design/towns.md#household-goods-and-departure),
are agreed but remain implementation work in
[#593](https://github.com/compoodment/ClankerWorld/issues/593).
The related [resident limits](../game-design/towns.md#house-resident-capacity-and-relocation)
and overcrowding relocation are follow-ups in
[#598](https://github.com/compoodment/ClankerWorld/issues/598) and
[#599](https://github.com/compoodment/ClankerWorld/issues/599).

A recipe project that finds its work site busy waits with the blocker "Waiting
for a free work site". While anyone waits, no one else is offered a new recipe
for the same workstation design, so the waiting agent gets the next turn
instead of losing it each time the site frees. Field work uses its own physical
tile and cannot be started through a zero-input crop recipe.

Recipes do not depend on a role or on personality or aspiration text. An adult
resident is offered field work only when their household holds a Farmhouse,
and a workstation recipe only at a building their household holds or at a
communal one, which has no holding household. A Farmhouse or Blacksmith that no
household holds is nobody's workstation. First-Town setup gives the Farmhouse
to the first starting household and the Blacksmith to the second. The offline
check in `HouseholdBuildingUseCoverageTests` builds generated worlds the normal
way and runs two world days with the built-in rule-based chooser and no model
calls, failing if these offers disappear or cross households. The owner's direct
production request checks the same workstation ownership and age. Crop
recipes are refused; crops need tilled land, a hoe and real planting stock.

For workstation recipes, site selection checks the actor’s current walking route as well as
ownership and unused production capacity. An occupied or inaccessible workstation
does not hide another reachable one. Movement’s existing diagonal and household
access rules still apply. If no site is reachable, an existing project uses its
blocked/reconsideration path rather than travelling toward an unreachable tile.

Recipe preparation and production must use the same actor/building owner.
Direct production requests use the same age restrictions as autonomous choices:
only adults and elders may start workstation recipes or field work. A request for an
infant, child or adolescent is refused before reserving inputs or changing jobs.
Household workstation inputs must be present at the actual building; stock
elsewhere in the household is not on-site stock. Missing inputs block the
project under its existing retry rules, without granting another household's
materials or implicitly transporting remote goods.

If an unpaid household recipe remains blocked for 60 ticks and no household
member has an actionable way to supply its missing ingredients, the runtime
pauses its saved plan and stops trying to continue it automatically. The adult
can choose other work. After ingredients return to the building, choosing the
recipe again resumes the saved plan. This does not interrupt a running
production job.

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
compatibility. Approved owner building designs stay active but are not household
kinds, so agents do not plan them; they wait for shared buildings. House fires supply heat. General invention is later Workshop work.

Clothing comes from a household's Tailor Shop (`clankerworld-tailor-v1`), which
replaced the Weaving frame and its "Woven clothing" recipe outright. The shop
weaves 3 fiber into 1 cloth in 20 ticks and sews 2 cloth into 1 clothing in 24
ticks, and costs 8 wood and 2 fiber to build; all of these are provisional
values. First-Town setup stores each starting agent's garment in their
household's House. A package is staged for older worlds on the first tick, but
the settlement package's digest changed when the Weaving frame was removed, so
saves made before this change are refused.

Workstation recipes use only stock already at the building. A household
building without its own dedicated hauling (every kind except the House,
Farmhouse and Blacksmith, so today the Tailor Shop) is kept stocked by the
`supply_workstation:<item>` choice. It is offered to an adult of the holding
household while the building holds less of an input than two batches of the
largest recipe that needs it, counting loads already on their way. The adult
delivers what they carry, picks up the household's spare stock from its House
or Silo (the existing delivery step then carries it in), or gathers from a
reachable source. Stock already set aside at another workstation is left
alone.

## Fertility and household fields

`LandFertility` derives each land tile's fertility from the world seed,
rainfall, climate, surface and nearby rivers or lakes. It is derived data,
not a saved object or a resource lot. Grass and meadow commonly support
farming; dry scrub is poor, and sand, rock, snow, mountains and water cannot
be farmed. Inspection uses Poor, Fair, Good and Rich rather than a score.

An adult or elder in a household holding a Farmhouse must stand on a free,
farmable tile with a carried wooden hoe to start tilling. Roads, resource
objects, buildings, reserved expansion tiles and existing fields are
excluded. Tilling takes eight work ticks; planting, tending and harvesting
take four each. These times, growth, yields, storage and planning targets are
provisional. Neighbouring tiles make a field without a fixed shape or size.

Each authoritative field records its household, crop, stage, work, growth
times and replanting reserve. Planting reserves and consumes one carried
grain seed, cultivated-green seed or potato. Moving away, death, lost tools
or urgent needs cancel unfinished work and release its planting input.
Completed harvests remain intact. Fertility and weather affect crop growth
or yield. Harvesting creates grain, potatoes or cultivated greens on the
field, and grain and greens also yield two replacement seeds. One usable
planting item is reserved before surplus can be traded. Picking it up for
the next planting releases the reserve and physically carries that item.

Planning compares population and available ready-to-eat food with a trial reserve of two
days at two meals per resident per day, then estimates fields from expected
yield. It picks free reachable soil by fertility and distance, favouring
tiles beside the household's existing fields. It can expand during shortages;
the existing household-building planner can establish another Farmhouse for a
household that lacks one and has the materials. Raw grain and potatoes cannot
satisfy this food reserve while their cooking paths remain unfinished, so they
do not stop farmers planting fresh greens. Grain is milled into flour
at the Farmhouse, one grain to one flour. Prepared meals and tool tiers are
separate work.

Wild berries and greens replenish. Orchard fruit appears in autumn after a
planted orchard matures. Harvesting fruit also produces a distinct orchard
seed with a reserved planting unit. An adult carries that seed to legal free
land and plants a sapling; tree growth, fruiting and the reserve survive reload.
Ordinary wood-tree seeds remain distinct.

The Godot map draws worked soil and crop growth above the terrain, including
wrapped map edges; the overview marks field tiles. Fields join the household
property display, and inspection shows the household, stage, crop, worker and
actual ground stock. This is current household use, not a separate land-title
system.

## Roads and bridges

Roads and bridges are world-owned and permanent. Walking never adds a Road
tile. The agreed rules are in
[Towns](../game-design/towns.md#how-roads-and-bridges-appear); this section
describes how the current code applies them.

**One saved crossing for everything.** A bridge is a saved record with a stable
ID built from its position (for example `bridge-90-3-ew-2`), a design
(`plank_span_1` or `plank_span_2`), what built it (`road` or `traffic`), both
entrance tiles, its river tiles and the tick it was built. A Road bridge also
names its route, `road:<Town>:<building>`. Movement does not read terrain
alone: the runtime turns the saved bridges into passable decks on the map
(`SeededMap.BridgeDecks`). A deck is walked end to end along the bridge, never
sideways into the river or diagonally, at dry-ground cost. A Road bridge's deck
counts as Road for the Road speed factor; a traffic bridge's does not. The
observation sends the same records, so Godot draws the deck and the tile card
and hover say "Bridge" from exactly what movement uses.

**What can be bridged.** A crossing starts on buildable ground, runs straight
across one or two river tiles and lands on buildable ground. Lake, ocean,
coast, a third water tile, a mountain bank or an existing deck ends the search,
so those are never bridged. Crossings can run across the east/west seam of a
wrapping map.

**Streets that meet a river.** Town growth (above) can cross a river up to two
tiles wide on a new bridge, in two places:

- A new side street is found by `RoadRoutePlanner`, which applies the side-street
  rules above and searches at most 32,768 tiles. Besides ground steps it may
  cross an existing bridge, or a new crossing, in a straight cardinal line. A
  new crossing costs 200 per river tile, like wading, so an existing bridge is
  cheaper. Ties break by cost, then row, then column, then discovery order.
- A street running on past a door (`TownStreets.Wander`) that heads straight at
  a river may cross it. The far bank must be clear, and it counts as one step
  of the run-on. The first Town's starting layout does not use this rule and
  keeps its streets on dry land.

Each proposal is checked whole before anything is saved: every new Road tile is
clear buildable ground, each step is a legal street step or a bridge, and every
new crossing is still legal, shares no water with another and is not redundant.
Road tiles, the building's entrance and new bridges are then committed together
in the same tick, and the border grows around the new Road tiles on both banks.
If there is no legal side street, nothing changes and a `town_road_unconnected`
event records the reason (`no_entrance`, `route_unavailable` or
`redundant_crossing`). A bridge with Road at both ends joins its two streets,
so the run-on rule does not treat either end as a dead end.

**Same connected banks.** Two crossings join the same banks only when they
cross the same river, joined through its water, and each end of one reaches an
opposite end of the other by walking along that water's shore without crossing
it. A tributary mouth or a separate stream breaks the shore, so a bridge over a
different nearby stream never blocks another. There is no distance limit; the
comparison examines at most 4,096 river tiles and, if that runs out, does not
treat the banks as the same. Along one unbranched stretch of river this allows
one bridge, however long the stretch. A new crossing over the same banks as an
existing bridge is not built; a Road uses the existing bridge instead, and one
route never builds two bridges over the same banks.

**Traffic bridges.** Only a committed foot step counts. Stepping from a bank
onto an unbridged one-tile crossing starts a wade; the next step onto the
opposite bank completes a crossing. Route previews, blocked moves, waiting and
turning back add nothing, and walking on Roads or bridges is not wading. The
evidence is saved: open wades, and completed crossings from the last two world
days, at most five per agent per crossing, which is enough to decide the rule
exactly. At the end of each tick, a crossing with six completed crossings by at
least two agents gets a `traffic` bridge. It is not built, and its evidence is
cleared, if a bank holds a building, resource or camp object, or an existing
bridge already joins the same banks. A traffic bridge adds no Road tiles.

Events are `bridge_built` (`road:<bridge>:<route>` or `traffic:<bridge>`),
`traffic_bridge_not_built`, `town_road_unconnected` and, if a run-on fails its
final check, `town_road_extension_refused`. The host logs them as
`bridge` and `road_route` lines with IDs and reason codes only.

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
with local saves and keys, using a bundled companion host process
([#468](https://github.com/compoodment/ClankerWorld/issues/468)).
See [where the game runs](../game-design/world.md#where-the-game-runs).

## Material gathering

Material gathering selects available resources reachable from the acting agent,
then checks an actual unoccupied route into harvest range. Heating, help with
another agent's project and Blacksmith ore use this selector; they do not
require a path to the original map anchor. Gathering for one's own project does
not use it yet. This does not change fuel duration or harvest yields.

Shared fuel and equipment also require an unoccupied route to their collection
point. Unreachable stock stays untouched and does not prevent an agent from
using reachable supplies or gathering local fuel instead.

## Trees and planting

Each tree is one map resource with one saved growth record
(`EcologyResource`). Growth, harvest, tile inspection and map art all read that
same record: `TreeGrowthRules.StageOf` turns it into the stage the host sends
as `TreeStage`, and the client draws whatever the host sends. Every tree number
lives in `TreeGrowthRules` and is provisional ([#462](https://github.com/compoodment/ClankerWorld/issues/462)).

- **Wood trees** are `sapling`, `mature` or `stump`. Felling a mature tree gives
  wood and one `tree_seed`; the stump regrows in spring. One tree-seed item
  serves broadleaf and conifer.
- **Planting** is the typed `PlantTree` action. It checks, in order, the
  species (broadleaf or conifer only; orchard propagation is still open), that
  the planter is an adult, that the seed lot is a tree seed they own with one
  free, the ground (grass, forest floor or fertile soil; never water, sand,
  rock, snow or dry scrub), buildings, Roads and existing objects, that the
  planter stands on or next to the tile, and the chunk's resource budget. A
  refusal returns a `TreePlantingRefusal` and a one-line reason and changes
  nothing. Success consumes exactly one seed and adds a `planted-tree-{x}-{y}`
  map resource with a sapling growth record, in the same tick.
- **Agents** are offered `plant_tree` while they hold a tree seed. The built-in
  site is the nearest reachable open tile outside every Town border, so trees
  do not block building sites. The species follows the nearest wood tree.
  `replant_tree` also uses a tree seed.
- **Orchard trees** are `growing`, `fruiting` or `picked`. Fruit is seasonal in
  `EcologyRules`: it ripens only in the tree's recorded season (autumn for new
  worlds) and falls when that season ends. New worlds start in spring, so
  orchards start without fruit.
- **Saves.** Planted trees are part of the saved map. On load, the map must
  still match regeneration apart from the settlement's staged sites and valid
  planted trees; each planted tree must be a plantable species on legal ground,
  off Roads and buildings, with its growth record. See
  [saves and replay](saves-and-replay.md#current-formats-and-older-worlds).
- **Art.** `UI/Graphics/TreeArtManifest.cs` in the client is the one list of
  tree art: species, stage, asset ID, sprite, source, licence and review
  status. The map reads its sprites and stage names from it. Every entry is a
  provisional code-drawn placeholder; the tree-seed item has no art yet.
- **Logs.** The host logs `tree_planting` outcomes (planted, refused,
  replanted, seed collected) with the agent ID and a bounded detail.

## Advanced generation controls

New World defaults to 50% water with a 20–80% range. `GenerationAmount`
controls forest cover, mountain relief and river abundance independently;
Normal is zero and omitted from saved JSON. Low/High use forest rainfall
thresholds 175/125 and river catchment thresholds 288/72. Low mountain relief
subtracts half the elevation above 130; High adds that full amount for Balanced
Small/Medium worlds and half elsewhere. Outside the visibility trial, Normal
keeps a 150 rainfall threshold, zero relief shift and river threshold 144.
Resource abundance retains its existing Sparse/Normal/Abundant saved values;
the UI labels them Low/Normal/High.

For Balanced Small/Medium worlds, each feature's target applies only while its
own control is Normal. The versioned Normal trial uses a 135 rainfall threshold
and adds one third of upper elevation as mountain relief. Low and High remain
separate controls. `GeographyCandidateSelector` tries at most three candidates
derived from the requested seed. It selects by unmet target count, normalized
distance from the 20–40% forest and 5–12% mountain dry-land bands, then largest
connected-region share as a tie-break; attempt number is the final stable
tie-break. Connected regions use diagonal neighbors, east/west wrapping when
enabled, and no north/south wrapping. The tie-break has no minimum region-size
threshold. Incompatible climate modes have no trial target and use one
candidate. Coverage is measured and returned for all settings.

Owner world-creation signing uses payload v3 to bind all settings and, for
Create, the candidate attempt, terrain and map-layer digests, and explicit
acceptance of unmet trial targets. Preview reports the selected candidate and
coverage for each attempt. Create reruns the bounded selector, checks those
signed identities, and builds the world from its selected map. It refuses a
changed preview or an unaccepted miss. The selected attempt is saved in
`GeographyOptions` with the visibility algorithm version and in the map
manifest; restore regenerates that attempt without searching again. Unsupported
Balanced Small/Medium visibility versions are refused rather than replayed with
different terrain rules. Small and Medium remain the only playable sizes; no
continent-count control is exposed for them. Existing saved water settings are
not rewritten.

## Skills and practical lessons

Agents start with no skills, including added adults and newborn children.
Finishing construction first records building; finishing a Farmhouse recipe
records farming, a Blacksmith recipe records smithing, and other production
records crafting. The record keeps the first learning time. Learning by work
has no teacher; completing an accepted lesson records the teacher's agent ID.

Lesson candidates use saved skills and the existing food/warmth readiness
rules, independently of work roles. A mentor cannot be working on an active
project, handling another social decision, or reserved for another lesson.
Request, refusal, acceptance, cancellation and pause/reload retain their normal
flow. A completed lesson changes neither the agent's role nor work proficiency.
Stored skills currently affect only teaching availability, never ordinary
action access or work speed. The agent card and Event Log describe the record.
The owner observation keeps its existing JSON `role` field for the lesson's
skill name so older clients can still display it; new code calls it `Skill`.
This is separate from the saved lesson record, which now stores a skill.

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
first opening can display them. The host checks inactive checkpoints once per
unchanged file in each process, then reuses only that structural result. It
hashes the file bytes to notice replacements and still checks required history
and model configuration every time; selecting a world performs a fresh restore.
When a private host starts, a background task makes those checks for every
inactive world, so the first list can reuse them. It holds the world-mutation
lock only to read the catalog and each file, never while decoding or restoring,
and a world it cannot check is left for the list to report as usual.
Open captures the chosen world's ID before
pausing, so a later catalog refresh cannot change its target. Open, Create and
Delete share the owner-action gate; selecting a different row cannot re-enable
Open or Delete until the current action finishes. Cleanup checks the current
selection rather than a row retained across an await.

Client cancellation stops the host between world assessments. It does not
interrupt one restore already holding the mutation lock.

World Settings reads autosaves for its current opening, paired device and
observed world. Closing Settings, choosing Game Settings or changing worlds
invalidates the read and disables Apply. Late replies and failures cannot
replace a newer opening's controls. A reply must name the observed world
before Apply becomes available, and Apply checks that context again before
sending the displayed interval and rotation.
