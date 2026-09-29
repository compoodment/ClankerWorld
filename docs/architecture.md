---
title: ClankerWorld Current Architecture
type: architecture
status: active
updated: 2026-09-29
---

# Current architecture

This describes the **existing playable prototype** and the technical
boundaries to preserve while it evolves. It does not turn the
[finished-game vision](vision-interview.md) into an implementation claim. See
[current state](current-state.md) for capability status.

## Today: Godot client and private VPS

```text
Windows Godot desktop client
        ↕ paired, signed HTTPS requests
private headless .NET host on the VPS
        ├── authoritative simulation and world clock
        ├── saves, event history and recovery
        ├── cognition scheduler and provider adapters
        └── validation of owner, agent and content requests
```

The supported game client is Godot, not the legacy diagnostic web assets. The
client renders observations and sends requests; only the host commits world
state. The simulation library does not depend on Godot or a live model
provider. The host remains reachable, but world ticks and hosted-model calls
require an authenticated client presence lease. Closing the last client stops
them after a short grace period; reconnecting does not simulate missed time.

The Godot terrain layer draws only camera-visible tiles from a compact local
terrain index. Its top-left overview samples that index into a small atlas;
neither path creates a Control per tile or a separate regional art set. The
host sends packed terrain for generated worlds. Signed reconnects advertise a
held world ID and map digest, so unchanged terrain is omitted and the client
reuses its verified map; first connection, world switch and digest changes
send a full map. This whole-map delta avoids routine retransmission, but
viewport/chunk transfer and control-receipt caching remain large-world work.
The simulation projects Small/Medium deterministic generated geography into
its existing physical-map contract. Fresh maps contain no pre-placed camp
objects or Town; the player-accepted site creates the first Town. The previous
generator remains available to verify older saves against their original map
identity. Generation options survive save/reload and starting-agent setup.
It persists separate row-major climate-zone, elevation, hydrology, surface,
and vegetation-cover layers beside the stable v1 terrain projection. Individual
trees remain resources/objects, not vegetation-cover values. Generated movement
uses hydrology and elevation; building eligibility also reads surface. Camera
and overview ground drawing use surface, vegetation cover, hydrology and
elevation, while the v1 terrain projection remains the manifest/save
compatibility summary and the fallback for fixture/legacy maps. Its manifest
digest and byte encoding remain unchanged for Small/Medium save compatibility.
Older generated saves recover missing layers from their saved deterministic
generation options on restore. Owner observations and New World previews carry
the layers independently; signed reconnects reuse them only with a matching
layer digest as well as the existing world/terrain claim. Signed New World
preview/create options select balanced, uniform or dominant climate and
optional latitude cooling. The larger geography presets remain compact
generator outputs, not playable-map promises.
Generated maps also choose bounded resource sites per 16×16 ground
cell, with food/fiber/seed/stone/wood biased by their surface and vegetation
layers. Starter-area resources remain reachable; remote sites need not be
reachable on foot.
The simulation creates 64×64 chunk manifests across the generated map, each
holding only its own resource metadata; the tiny fixture retains one chunk.
Food and project-material selection currently consider the starting area's
foot-accessible ground component, so resources across water or mountains do
not masquerade as immediately available. A boat-access model must broaden this later.
This is not yet a detailed vegetation/object model.
The optional abundance setting uses a provisional density rule over those
16×16 cells: Sparse visits alternating cells, Normal tries one site per cell,
and Abundant tries two. Placement remains bounded by each chunk's resource
limit, and preview/create share the same options and deterministic map digest.
Generated-world weather is derived deterministically by 32×32-tile region,
day and the saved climate zone at each region's center from the world seed and
calendar. The saved climate's
weather value remains the reference condition for the old tiny fixture; no
per-tick weather grid is serialized. Owner observations include the current
regional conditions so the Godot HUD can show camera-local weather. Survival,
travel and crop completion query the relevant location, not the reference
condition. The selected climate shifts rain/snow probabilities and clear-day
exposure but uses
provisional weights; weather fronts, persisted per-tile moisture and detailed
biome-specific profiles are not implemented yet.
Food-crop completion uses a bounded soil-moisture estimate derived from the
three most recent local weather days. The estimate is recomputed from the
world seed, calendar and region instead of saving a mutable per-tile moisture
grid. Wet/dry harvest adjustments are deliberately modest; detailed soil,
runoff and irrigation are not implemented.
The physical map now indexes terrain for constant-time passability/build-site
checks. An internally captured proposed tick reuses its committed map rather
than regenerating generated geography; external save loads still validate and
regenerate it before acceptance. Private-world checkpoint v2 now stores
verified 64×64 terrain-byte chunks; v1 per-tile JSON remains readable and
migrates atomically on load. Owner observations still send the whole terrain,
so compact saves alone do not make huge worlds viable.
For generated maps, the owner projection now carries terrain as row-major
terrain-kind bytes encoded in base64; the matching Godot client decodes those
into its compact camera/overview index. The existing 6×5 world still emits
the former tile list for the currently paired Windows build. Generated-world
reconnect snapshots omit unchanged packed terrain through a versioned cache
claim; viewport chunk requests and partial-map deltas are not implemented yet.

The private host keeps one active runtime object so existing signed controls
remain directed at the selected world. A private catalog archives each world's
checkpoint and saves the active world before a paused switch. Its world IDs,
names, generation seeds, provider-slot assignments and autosave settings are
separate; installation-level API keys and device pairing are not copied into
world files. The active recovery checkpoint wins if a crash interrupts the
catalog selection update. Named manual saves are listed only for their world.
World creation and selection require the paired owner's signed request and
leave the selected world paused.
The signed preview regenerates the same deterministic Small/Medium map and
returns its packed terrain, suggested starting point and manifest digest
without changing the active world or catalog. The client draws that data atlas
before Create World is enabled; changed generation options invalidate the preview.
The existing paired-device authority ID remains installation-stable across
world selection; it does not become the selected simulation world's ID. A
restart checks the server authority and restores the paired key even when the
active simulation world has changed.

The current host schedules one world tick per real second. Newly created
private worlds start paused and save the accepted playtest pace: 360 ticks per day and a 40-day
year with four 10-day seasons. The same world setup saves day-based lifecycle
thresholds (3/15/45/60 days). A day is therefore nominally six real minutes,
subject to provider/host load. The former 1,440-tick/365-day development save
is archived, not silently reinterpreted as a new-world save.
Owner observations report the saved world-system ticks per day and days per
year; the client uses those values for clock/date presentation rather than
assuming one fixed tick length. The 365-day prototype and new 40-day calendar
have display mappings; profiles show age in years or days according to the
saved lifecycle. Matching society/world-system calendar values are validated
on restore.

In the repository's private-host path, hosted cognition is dispatched after a
committed tick and resolved at a later tick boundary. The saved scheduler queue
is the unresolved decision point; the external HTTP task itself is never save
authority. Admission checks request ID, provider epoch, run epoch and current
candidate legality. Other agents and world systems continue while it waits.
Pause, lost client presence and quit cancel the host task without inventing a
strategic answer; a restored world can retry its saved queue entry. Historical
fixture methods still support synchronous provider dispatch for isolated tests.
The private save also owns Jev availability. Changing it requires a paused
world, cancels pending hosted work and changes the provider epoch. If Jev is
off, decisions previously routed to Jev use the inhabitant's personal planning
provider, then the world planning provider, then deterministic safe action.
The provider credentials remain installation-local and are not erased by this
world switch. An older saved world retains its prior format and default-on
behavior until the setting changes; the first change writes private-world
schema 15 so older hosts cannot silently discard the world choice.
When Jev handles a routine request, that same bounded request may also score up
to 12 previously unassessed source records belonging to that inhabitant. The
runtime saves only the salience/confidence index and source links; it does not
ask Jev to generate prose or make a separate billable request. Personal-model
observations retrieve at most four relevant records from that inhabitant's own
social memories and belief ledger. Each excerpt retains its source kind,
provenance, confidence, correction state, and any Jev salience estimate. The
local relevance path remains available when Jev is disabled. Source records
remain unchanged and inspectable; beliefs do not become world facts through
compaction. This is bounded relevance assistance, not automatic experience
capture or generated narrative summaries.

The current provider contract is a **single legal-candidate choice**, not a
dialogue turn. OpenAI-compatible adapters receive an ID, tick/epoch bookkeeping,
an unlabeled `hunger_basis_points` value that actually measures fullness (0 =
starving, 10,000 = full), legal candidates, up to four retrieved memories and
bounded known-map facts. They do not receive the agent's name, life stage,
personality, aspiration, household, current activity, nearby people or
relationships, weather/warmth/illness, inventory, or recent thoughts. The
system message describes JSON and memory provenance, not the agent's world or
role. Jev receives a different, smaller choice payload plus optional memory
compaction candidates; it is not a persona/dialogue adapter. These are current
implementation limits, not an intended definition of personal-model identity.
The selected candidate must be legal and response confidence below 0.5 invokes
only `safe_idle`; probabilities are validated/retained but do not choose the
action. Provider-owned model text cannot directly mutate world state.

The active recovery checkpoint is re-encoded and fsync-written after each
advanced one-second tick. History is compacted into digest-addressed segments,
so the active event list is bounded, but segment retention and large-world
write cost have not been measured. This durability/performance trade-off must
be resolved before claiming Large/Huge/Mega runtime support.

## Authority and failure boundaries

- A model chooses among legal intentions; deterministic world code validates
  and executes movement, work, resources, occupancy, ownership and effects.
  Text from a model, mod, client or save file is never authority to change
  state directly.
- Hosted provider work runs between committed ticks. A rejected, cancelled
  or superseded answer cannot leave a half-applied action. Pausing or losing
  client presence invalidates in-flight work in the current host.
  Failure or low confidence selects only the explicit `safe_idle` fallback;
  it cannot execute a strategic candidate or complete an instruction. Other
  agents and world systems advance while a hosted request remains unresolved.
- API credentials are stored separately from world saves and must not be
  returned in observations or written to telemetry. Operational logs are
  bounded, structured outcome records, not raw prompts or secret-bearing
  responses.
  The installation-local provider store also owns named key slots for agent
  assignments. Signed owner actions bind the selected slot ID and new-key
  label; owner status returns only non-secret IDs and labels. One personal
  provider/model selection produces routine and planning assignments together.
- A separate installation-local usage sidecar durably reserves each hosted
  attempt before provider HTTP work. Its atomic reservation enforces the
  optional installation-lifetime call-attempt cap across concurrent agents;
  retry, failure and abandonment keep their spent allowance. Completed
  responses add only known token counts. Reaching the cap persists an explicit
  world pause; the signed owner limit action can add allowance or disable the
  cap before the normal signed Resume action succeeds. Deterministic provider
  work does not reserve an attempt. The bounded sidecar stores no prompts,
  responses, hidden reasoning or credentials and is not part of world saves.
- Agents have bounded personal knowledge. A fact in the world or visible to
  the player is not automatically known to every agent. Accepted personal-model
  decisions can include a short in-character private thought; the last eight
  are saved and projected only into that agent's owner-visible profile. They
  are not hidden model reasoning, public dialogue, or knowledge transferred to
  another agent. The owner can inspect up to 16 recent, non-tombstoned social
  memories per agent, including private records and deceased profiles; this is
  distinct from public dialogue and the authoritative event log. A separate
  owner-private belief ledger stores bounded statements with firsthand,
  hearsay, or inference provenance and confidence. Corrections retain the old
  belief as superseded history without appending a public event or changing
  world facts; the owner inspection projection shows that evidence and status.
  Belief telemetry records a safe-format belief ID, owner ID, provenance,
  confidence and outcome, never statements or source prose.
  Personal-model requests retrieve at most four owner-only social-memory or
  belief excerpts with their evidence labels. Jev can score at most twelve
  previously unassessed records in an existing routine call; scores link to the
  unchanged sources and are used only as retrieval weights. This adds bounded
  input to an already-reserved provider attempt, not another provider call.
  Accepted index changes travel with the committed tick result and produce
  owner/count-only host telemetry; no source prose or source IDs are logged.
  Beliefs are not yet generated from conversations or shared automatically;
  automatic experience capture and generated narrative summaries remain
  unimplemented.
- Explored map facts are saved per agent in private-world schema 23; a fact
  visible to the player remains absent from agents who have neither visited it
  nor learned it from another person or a physical record. Each bounded outing
  can create a one-site field record or a map of up to nine sites. Their
  inventory lots can be shared nearby without transfer or exchanged through
  existing barter; recipients gain their own fact entries with the original
  discoverer and source agent retained. Personal-model requests receive only
  up to 16 of the current agent's recent map facts. Owner observations expose
  each agent's bounded knowledge and the records they physically hold, not a
  shared global knowledge ledger. Cognition observations include only the
  current agent's bounded facts. Operational events log transition IDs and
  counts, never map contents or model prose.
- Live physical actors remain separate from saved deceased records. On death,
  the runtime archives the last physical state and frozen age alongside the
  society death record, then removes the actor from active movement and work.
  Owner observations expose the archive for inspection without treating it as
  a living map entity. Earlier deaths with no physical archive cannot be
  reconstructed from an old save.
- Society freezes personal inventory lot IDs, kinds and quantities when death
  moves those lots into estate escrow. After the committed death tick, the
  private-world runtime can dispatch one cancellable final choice through the
  deceased agent's personal planning provider, outside the tick transaction.
  Society validates the candidate, living recipient, and still-escrowed frozen
  estate before a will can replace household beneficiaries. The persisted
  pending marker is never reissued on restore: interrupted work resolves to
  the default on the next active tick. A deadline or provider failure does the
  same. Estate settlement waits for a pending will and commits only once.
- Saves are versioned and atomically replaced after committed state changes.
  Restores must fail visibly on unsupported or mismatched state rather than
  silently substitute content or credentials. The current host keeps bounded
  hot history and hashed older event segments; backups must include the save
  and its referenced history. Named manual checkpoints live beside the active
  save in a private `.manual` directory; their snapshots reference the same
  history archive. Checkpoint metadata records per-agent provider/model and
  credential-slot IDs and that world's autosave preferences, never API-key
  bytes. A full backup must retain the
  active save, `.history`, `.manual`, pairing authority and global provider
  configuration together. An adjacent `.autosave.json` file stores the
  current world's rotating-snapshot schedule and last saved tick; include it
  in backups. Rotating snapshots use the same private checkpoint directory as
  named saves and are trimmed only after a newer copy exists.
- Private-world schema 21 saves Town identity, founding state, resident IDs,
  Town-assigned building IDs, and the inspectable border tiles. A New World
  founder-setup runtime contains exactly one `First Town`: it is `founding`
  while paused setup is in progress and becomes `founded` when Start World is
  accepted. Every placed founder joins it. After start, an adult placed within
  its current border joins; walking later does not rewrite membership. A child
  inherits the resident parent's Town, and death removes the resident. A
  player-placed building is assigned only when its footprint is in or directly
  adjacent to the current border; a building proposed by a Town resident uses
  that resident's Town. The Town-assigned building footprint expands its
  border if needed.
- Private-world schema 22 additionally permits Jev-assisted memory-compaction
  indexes; schema-21 Town saves remain loadable without inventing an index.
- This is a **provisional single-Town implementation**, not a final land-claim
  decision: the initial border is the starter-camp object bounding rectangle
  plus one tile of margin; added assigned buildings grow the rectangle to
  include their footprint plus one tile, clipped at map edges. It does not
  bridge wrapped seams, create overlapping claims, add later Towns, or offer a
  separate map-filter control. Schema-20-and-earlier founder-setup saves
  reconstruct only the known first Town and its founding members; existing
  post-start adults and buildings remain unassigned rather than having
  affiliation inferred from current position. Old worlds without founder
  setup keep no invented Town. The owner projection sends saved facts only;
  the Godot client draws the saved border and World Info/tile inspection show
  its current recorded state. Stable `town_transition` telemetry reports only
  Town ID, transition, resident/building counts and border size, never names or
  provider text.
- **TownLayoutService** is the shared legal-site and ranking boundary for
  resident building projects. It takes one immutable layout context per
  inhabitant decision, returns at most five ranked legal building sites by
  default, and attaches stable reason codes/text for buildable footprint,
  unoccupied route, Town compactness/growth, nearby available materials, open
  meadow/forest terrain and related building purpose. A provider accepts a
  site only by selecting its site-specific legal intention; choosing another
  action starts no project. The accepted tile is encoded in the existing
  project candidate ID, so this change adds no checkpoint field; an older
  accepted building project without a site binds once to the current top legal
  option. If the chosen tile becomes illegal, the project is blocked and fresh
  ranked candidates return after 60 ticks; a declined offer is rebuilt from
  current facts at the next normal cognition reevaluation. Ranking weights are
  provisional.
- Site scope deliberately follows the current #165 geometry/membership model:
  a resident's single saved Town rectangle plus its one-tile margin; no wrap-
  seam extension, ownership-claim graph or second-Town conflict check exists.
  Legacy worlds with no saved Town offer a bounded set of nearby reachable
  sites; an already-saved distant choice still receives an exact route check. Material
  proximity and related-building purpose are ranked only from current map
  resources and placed definitions; absent future claims/Towns are not inferred.
- The current content path accepts bounded, validated data-only designs.
  Arbitrary executable agent code is disabled. The vision includes a
  restricted script sandbox and explicit human-mod import; neither is a
  claim about today's playable path.

These are implementation boundaries, not a second list of product decisions.
The detailed past phase contracts were retired from the active documentation;
the corresponding code and tests remain the executable proof.

## Finished Windows distribution target

For development and computment's playtesting, the VPS arrangement remains
useful for live observation and debugging. The first finished release for
other players instead packages the **same authoritative simulation** on that
player's Windows PC, with local saves and local provider-credential storage.
It must not depend on computment's VPS or a hosted game account. Cloud model
providers still require network access and the player's own keys. Whether the
local host is embedded or bundled as a companion process is open.

The vision ledger owns future behavior, including the custom calendar,
in-world starting-agent setup, private-memory inspection, optional AI-usage stop,
Jev toggle, safe agent inventions and external mods. [Current state](current-state.md)
reports what is connected to normal play.

## 2D geography prototype (not the live world)

`GeographyGenerator` is a separate, deterministic tile-map primitive; it does
not replace the current 6×5 playable map or its save format. It samples a
vendored [FastNoiseLite](../src/ClankerWorld.Simulation/ThirdParty/FastNoiseLite/README.md)
field for broad elevation and long-run rainfall. Wrapped worlds sample a circle
in noise-input space and use east/west-wrapped tile neighbors; the game map
itself remains two-dimensional. A water-coverage threshold, connected-water
classification and ocean-outward priority drainage then identify oceans,
lakes and upstream-accumulated river channels. The candidate size dimensions
and river threshold are tuning targets, not finished-game promises.

The design follows the separation between local noise and global hydrology in
[Red Blob's noise guide](https://www.redblobgames.com/maps/terrain-from-noise/),
the ocean/lake and river-flow methods in the original
[polygon guide](https://xenon.stanford.edu/~amitp/game-programming/polygon-map-generation/)
and its [Mapgen2 source](https://github.com/redblobgames/mapgen2), and the
ocean-outward drainage / rainfall accumulation in
[Mapgen4 source](https://github.com/redblobgames/mapgen4/blob/master/map.ts).
The [Voronoi tutorial's source](https://www.redblobgames.com/x/2022-voronoi-maps-tutorial/voronoi-maps-tutorial.js)
also makes the local-minimum failure mode explicit. ClankerWorld uses tile
neighbors rather than importing those projects' polygon meshes. This is an
implementation inference, not a locked terrain design: continent layout,
climate modes, biome/object placement, chunk streaming and river
appearance still need integration and playtesting.

## Deliberate pre-release identifier reset

The application, .NET projects, Godot title, Windows executable, save/content
headers, hash domains, built-in package IDs, owner-request proof domains,
Windows `user://` directory and CNG key name now use **ClankerWorld**. This is
an intentional pre-release compatibility break approved for a fresh development
world and new owner pairing, not an in-place migration of old saves or keys.
The [deployment template](../deploy/clankerworld-viewer.service) uses new
installation and state paths. Keep the previous private world and pairing
authority only in a root-only rollback backup; copy provider credentials to the
new protected state path without printing them. Ignored old export/build
artifacts are historical binaries, not current source names.
