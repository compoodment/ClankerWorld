---
title: Current Product State
type: product-status
status: active
updated: 2026-09-29
---

# Current product state

This is the canonical answer to **what the ClankerWorld prototype actually does
today**. It
describes the default private-world runtime and Godot client, not only schemas,
contracts or isolated fixtures.

## Status vocabulary

- **Playable** — connected to the default private world and observable or
  controllable through the normal Godot client.
- **Integrated but thin** — participates in the live save/runtime, but does not
  yet form a rich or recurring gameplay loop.
- **Verified primitive** — implemented and tested in a bounded fixture or API,
  but not meaningfully connected to normal play.
- **Planned** — accepted direction without the required implementation.

“Implemented” never means “the type exists.” The player must be able to
encounter the behavior through the normal game path before this document calls
it playable.

## Playable path

The supported product path is an unsigned Windows 11 x64 Godot client paired to
one private headless .NET host. The paired client observes the complete world
and submits signed requests. The server validates and commits all state.

The host remains reachable continuously, but simulation ticks and hosted-model
calls run only while at least one authenticated client has a current presence
lease. The last disconnect closes the gate after five seconds. Reconnect does
not simulate offline time or clear an explicit manual pause.

The static web assets retained by the HTTP host are legacy protocol-diagnostic
infrastructure. They are not a supported game client or an alternative owner
interface.

In the repository build, hosted model work now runs **between** world ticks.
The agent's unresolved decision remains in the saved cognition queue while
the world and other agents advance; urgent food and exposure routines
can continue, but new projects wait for a valid answer. Pause, client absence,
provider changes and quit discard the external call, not the queued decision.
Late answers are admitted only against the same request/provider/run epoch and
a still-legal action. Deterministic decisions remain in the isolated tick.
The server build is installed on the live VPS. The live world may change during
computment's current playtest; inspect it at runtime before operational changes.
Pairing and provider credentials remain configured.
The matching Windows artifact has not been tested on computment's laptop; a
live model-wait playtest is still outstanding.

In the repository client, agent map markers persist across observation
refreshes (so hover is not interrupted), occupy bounded non-overlapping
regions of their tile at supported zoom, and take priority over the new square
ground-tile hover outline. Selected-agent warmth/illness/diet and equipment
appear in a fixed card area. These client repairs await a new Windows artifact
and computment's laptop check; the live host was not changed.

Save schema 18 removed persisted energy and sleep state; schema 19 and the v2
private-world checkpoint format store terrain in verified 64×64 byte chunks
instead of per-tile JSON. Older v1 checkpoints remain loadable and migrate
atomically on load. Pre-layer generated maps retain their original resource
layout and are checked against the historical generator and known settlement
package digest before migration. Damaged chunks fail closed without replacing
the save. Schema 21 adds saved first-Town membership and borders, migrating
compatible founder-setup saves without inventing post-start membership. Schema
22 adds owner-specific memory-compaction indexes. Schema 23 adds agent-owned
map facts and physical field-map/field-record artifacts; schema-21 and
schema-22 saves migrate with empty personal knowledge. Schema 24 stores
world-owned Roads; schema 25 optionally stores a selected first-Town origin,
while older Town saves keep their camp-derived border.
Legacy bedroll markers remain hidden
compatibility map data in those worlds; they reserve their historical tile but
have no rest effect. Their maps still undergo deterministic regeneration
checks. Historical bedding inventory can remain in a save, but bedding recipes
can no longer start. The schema also preserves the per-world Jev switch and incomplete
four-agent setup; bounded private thoughts from accepted personal-model
decisions, deceased records, optional bounded directed trust and work practice
alongside biological
life-clock anchors, parenthood preparation, practical lessons and council
policy/ballots, survival conditions, fuel deadlines, work projects and additive
settlement resources;
the schema-4 history mechanism bounds hot histories
and archives older events with verified hashes; reconnect explicitly resets
stale cursors. See [architecture](architecture.md#authority-and-failure-boundaries)
for the save and recovery boundary.

Named-save listing now isolates unusable metadata entries so sound checkpoints
remain reachable and autosave rotation can continue. It preserves damaged files
and emits a safe warning once per bad entry until repaired; it does not repair
corrupt checkpoints or silently delete their metadata. Windows playtesting of
the damaged-entry case remains pending.

Signed polling uses process-local one-use challenges rather than rewriting
the authority file; old challenges fail closed after restart.

The event stream now stores a location for actor-associated events. The Godot
Events panel marks those entries as navigable and moves the camera to the
recorded event location when clicked. Out-of-view event pop-ups and their
settings have been removed; important events stay in the Event Log. The
selected agent card shows when a decision is queued and a scrollable history
of recent private thoughts supplied by accepted personal-model decisions. Archived thoughts and
saved social memories remain inspectable on deceased profiles without
continuing to update. Global events have no map destination.

## Capability matrix

| Area | Status | What exists now | Important limitation |
| --- | --- | --- | --- |
| World host and persistence | **Playable** | Persistent private checkpoint, pause and client-presence gate; signed Main Menu New World and Load World select independently saved worlds. Load World reports evidence-based compatible/incompatible/unknown status and blocks only proven-unloadable archived worlds. Current-world named manual saves can be newly created or selected by ID and overwritten after confirmation; the old selected checkpoint is retained as a separate recovery save. Rotating autosaves remain in World Settings. Provider/model/key-slot assignments, Town membership/borders, and autosave choices follow each world; API keys and device pairing stay installation-local | New World offers Small/Medium, seed, wrapping, water share, initial climate choices and Sparse/Normal/Abundant resources; continent controls, Large/Huge/Mega playable storage and player-local packaging remain unfinished. The active recovery checkpoint remains automatic even when rotating autosaves are off. Broader save migration and compatibility policy is open |
| Owner security | **Playable** | Windows device key, host-approved pairing, signed/replay-resistant owner requests, revocation and origin pinning | Private single-owner model only |
| Map and movement | **Playable** | Deterministic Small/Medium generated maps without pre-placed camp objects or a Town, with rivers/lakes/ocean, no-build mountain/peak terrain, four-agent setup, camera-visible terrain drawing and a draggable top-left overview. Wrapped worlds pan continuously east/west across the seam, while non-wrapped camera edges stay bounded. Opening a world centers the camera on its first Town, then living agents or camp objects; a new unsettled world instead starts over nearby dry land rather than possibly over water. Small/Medium zoom-out keeps at most roughly 70% of each map axis visible, avoiding black north/south space; larger maps retain a shared 8 px overview floor. Zoom-in frames roughly 14 world rows across 720p and 1440p render sizes. These client camera bounds are still subject to playtest and do not change host simulation work. Generated foot routes and interaction range also honor east/west wrapping, including restored worlds. Foot routes use legal diagonal steps with longer deterministic cost; both corner shoulders must be clear, including of other agents at movement time. Agents may cross one-tile-wide rivers bank-to-bank and traverse mountains at half dry-ground speed, but cannot build there; river tiles cannot be used as a longitudinal or diagonal footpath. Generated maps save climate, elevation, hydrology, surface and vegetation cover separately. Ground distinguishes grass, forest floor, beach sand, dry scrub, rocky upland, snow and fertile-soil sites; dry climates can show cactus cover. Fertile-soil surface marks only a saved fertile-land resource site and is not a per-tile fertility measurement. Surface/cover plus water/elevation relief drive camera and overview drawing; the Godot client draws provisional code-generated 32×32 pixel-art ground textures (16×16 at mid zoom) from 16 px tiles upward, with two calm variants per surface and cover style, seamless water that repeats only as a 16×16-tile block with scattered wave crests and sparkles (and faint light ripples in shallow water) over its flat color, top-down mountain and snowy-peak relief, and rounded shores where water meets land: the neighboring land reaches the same kind of narrow wandering band into each water tile, with a lighter shallow band and, on coasts and lakes, a thin foam line following it (rivers keep a quieter bank without foam); where two kinds of water meet, the lighter one (lake, then river, shallow water, ocean) fans the same narrow band into the darker, such as a river mouth into the sea; where two land surfaces meet, the higher one in a fixed order (sand, dry scrub, soil, grass, tundra, forest, rock, snow, then mountain relief) reaches a wandering band of at most a quarter tile into its lower neighbor, with half-transparent rim pixels, a shared reach at every map corner so boundaries continue across tiles and wrapped seams, and rounded corner pieces, while each tile keeps a square body of its own ground; overview zoom keeps flat one-pixel-per-tile colors. The rejected grain, striped transition marks and circular weather clouds stay removed. Camera-bounded rain/snow marks remain. Hydrology/elevation drive movement, and water/elevation/surface drive building eligibility. Terrain kind remains the manifest/save compatibility summary and the fallback for fixture/legacy maps. Generated broadleaf/conifer wood trees and generic orchard fruit trees are individual objects on forest and suitable meadow tiles, at most one tree per tile. The top-down client draws mature, stump and planted-sapling wood stages plus fruiting, picked and growing orchard stages in camera-visible terrain, as provisional generated pixel sprites from 12 px tiles upward (simple dots when zoomed further out); natural food, fiber, stone, ore, clay, seed and soil sites and their depleted/regrowing states use matching sprites. Older camp resources that record only a resource kind (food, construction wood, stone, fiber, seed, fertile land) reuse those sprites, with a log pile for wood; the camp cooking fire uses the hearth art, the camp path draws as flagstones and a legacy bedroll as a blanket. Sparse resource sites extend beyond the camp and belong to their actual 64×64 chunks. The older 6×5 world remains selectable. Generated terrain travels in compact row-major owner observations; signed routine reconnects omit unchanged terrain and layers after the client caches the matching world, terrain manifest and separate layer digest | Ground and object shapes are code-drawn prototypes, not production textures or final sprites; cultivated fields and orchard species remain unfinished. Initial and changed maps still transfer in full, and control receipts still contain terrain; viewport/chunk deltas are unfinished. Large/Huge/Mega exist only in compact geography generation, not playable world hosts |
| Survival | **Playable** | Hunger, warmth, exposure illness/recovery, fresh food, source-based diet variety, clothing, fuelled heat and weather-protective shelter; illness slows work to 75%, 50% or 25% by severity and adds bounded travel delay without suppressing social choices; food, nearby shelter, dependent caregiving and direct infant care support recovery; no energy meter, passive energy drain or sleep action | Diet variety and illness treatment are thin; Bandage, Medicine and Clinic are catalog-only identifiers; recipes and treatment effects remain undecided |
| Observation and control | **Playable** | World view, top-bar living-agent count and World Info, inhabitants, needs, intentions, inventories, relationships, located event-log jumps, pause/resume, separated Game/World Settings panels and suggestive/must-do instructions. The first Town appears only after the player accepts a paused five-building site; placed founders and later adults placed within its saved border are recorded residents. Its amber border is drawn on the map by default; the top-bar Filters panel can hide it or tint only saved household-owned building footprints, with unclaimed land left untinted. World Info and selected-tile inspection show saved Town state only, and tile inspection names any recorded household building owner independently of filter visibility. Town membership and borders persist through save/load; newly deceased inhabitants retain an inspectable last-state record across save/reload. The selected-agent card lets the paired player rename an agent without changing its identity or relationships. The family-tree panel links accepted parentage and partnerships, lets the player inspect living or deceased relatives, and excludes household-only links. Game Settings persists date-order (DD-MM-YYYY, MM-DD-YYYY or YYYY-MM-DD), 24-hour or AM/PM time display, Window Size, Render Resolution and UI Scale locally. Window Size offers 1280×720, 1600×900 and 1920×1080 for windowed mode. Render Resolution defaults to Automatic, matching the current physical window or fullscreen display, and offers fixed sizes up to the detected display resolution (including 2560×1440 on a 1440p screen); fixed choices scale the full game/UI with letterboxing for differing aspect ratios. UI Scale offers 100%, 125%, 150%, 175% and 200%, enlarging interface text, controls and major panel widths without changing the selected map render resolution or terrain detail. Older explicit non-default render choices remain selected. The game starts fullscreen by default and remembers an explicit windowed choice; Window Size is disabled during fullscreen, which uses the display size. Main Menu Settings keeps the title backdrop, exposes only Game Settings and returns via a compact header back button; selecting an active category keeps its page open. Escape backs out of the newest open panel, map-click mode or menu and otherwise opens the Pause Menu; the mouse wheel zooms toward the pointer. In a world with no menu open and no text field focused, Space/P use the pause button, N/Shift+N select and center the next or previous living agent, C centers the selected agent, H returns to the first Town (or the opening view), +/− zoom, and M/F/I/R/T/E toggle the Map, Filters, World Info, Agents, Town and Event Log panels exactly as their top-bar buttons do; F1 or ? opens a controls list, which World Info points to. The Agents, Town, Event Log, World Info, Filters, World Map and controls panels close from a × in their heading. The Town panel groups shared stores, projects, any household council and social notes under headings with explicit empty-state lines; the Event Log groups the newest 30 player-facing events by day. The selected-agent card opens beside its agent when it cannot fit above or below, and its profile scrolls within the view on short screens. Once the world has started, a click-through badge marks paused time; a click-through bottom-right readout names the hovered tile's surface or water, vegetation, first building or resource, Road, Town and coordinates. The Agents list shows each living agent's current activity and hunger ahead of the deceased; choosing someone, or the agent card's Find button, centers the map on them. Personal-model agents show a bounded scrollable private-thought history; the Memories and Maps panel shows up to 16 recent saved memories, owner-private beliefs, and personal map facts each, plus the contents of up to eight physical field maps/records held by that agent, including historical profiles. Beliefs show firsthand/hearsay/inference provenance, confidence and correction status without entering the event log. Quit Game requires confirmation. Selected ground tiles open an inspection card backed by signed terrain, generated climate/elevation/hydrology/surface/vegetation, object/resource/building, and regional weather/moisture observations. Agent markers and placement mode retain click priority; the card updates as observations change. Fertility and exact temperature are omitted until measured rather than inferred, and absent Town/property/Road/object facts are omitted instead of shown as empty placeholders | Historical records currently cover deaths after this archive is introduced, not earlier lost physical state; thoughts only exist for new accepted personal-model decisions, not past play or model-internal reasoning. The runtime can store and correct owner-private beliefs, but cognition does not yet create them from conversations or observations; Jev-assisted salience scoring is available; automatic experience capture and generated narrative summaries remain unbuilt. World Info shows current facts, not future ownership/capability discovery; fertility, exact temperature and a richer biome model are not yet projected; settings contain only existing prototype controls and some diagnostics remain operator-only |
| Cognition | **Playable** | World defaults and one personal provider/model selection per inhabitant, applied to routine and planning decisions; independently named API-key slots allow different agents to use different keys for the same provider. The selected agent card opens these controls directly. New worlds require a personal provider/model/key for each starting agent. After starting, the top-bar Add Agent flow accepts a stored or new key and previews the saved Town border and household-owned building footprint under the pointer. The person and model assignment persist; placement on a recorded owned footprint joins that household, including on an occupied House tile, while unclaimed Town placement creates no household and placement outside a Town creates an independent one-person household. Initial placement within the saved first-Town border establishes Town membership; later walking does not change it. Fresh starting agents and added adults may supply their own name with an accepted personal-model decision, without a separate call; a player rename takes precedence over a pending answer. A saved per-world, pause-only Jev switch, validation, fallback, retry, safe logs and selection-card telemetry also operate. Personal-model decisions receive at most four relevant, owner-private excerpts from existing, non-forgotten social memories and beliefs, preserving provenance, confidence and correction status. Each agent's cognition also receives at most 16 of that agent's recently learned map facts; records shared or traded teach only their recorded sites. Optional Jev can score up to twelve previously unassessed records during a routine call; only source-linked salience/confidence metadata is saved, with no extra provider request. A separate ledger retains each belief’s provenance, confidence, owner and correction link for agent-card inspection; dialogue and event observation do not yet create beliefs automatically | The current map has no land-claim records beyond owned building footprints. Unclaimed Town placement is household-free, without access to another household’s private supplies; later voluntary household changes and a path to suitable existing housing remain unbuilt. A model that omits or fails to provide a valid name leaves the placeholder until a later decision or player rename. The memory path ranks existing records but does not automatically capture experiences or generate narrative summaries; Jev assistance only runs as a bounded part of its existing routine call. Legal planning covers projects, material help, barter and bounded building proposals, not free-form social reasoning |
| Inventory and ownership | **Playable** | Carried items and shared stores, gathering wood/stone/fiber/seeds, material requests, household sharing and food pickup | Negotiated barter and a broader economy remain incomplete |
| Buildings and production | **Playable** | Persistent acquisition/work projects; newly placed player buildings near the first Town border and buildings proposed by a current Town resident receive that Town assignment; an assigned building can grow the saved border. Hearth fuel, shelter insulation, storehouse preservation, clothing insulation, tool benefits and bounded building/farming/crafting practice earned from completed work | A newly added adult on an owned building footprint joins its household; on unclaimed Town land the adult has no household, while outside Town borders the adult starts an unrelated one-person household. Seeking an existing suitable home before a new House project remains unbuilt. The single first-Town assignment and rectangular border-growth rules remain prototypes, not final land claims. The shared layout service offers up to five ranked legal sites per building with clear-footprint, route, compact-growth, nearby-material and related-purpose reasons. An agent accepts a specific site by choosing its candidate, or declines the offered sites by choosing another action; no project starts on a decline, and the next planning decision regenerates the ranking from current world state (an unchanged safe-idle choice is reconsidered after 300 ticks, sooner if its legal candidate set or urgent needs change). The chosen tile stays fixed with an accepted project. If it becomes illegal, the project blocks and fresh ranked choices are offered after 60 ticks. Ranking weights are provisional; ownership claims, multiple Towns, wrapped Town-border geometry and a player-reviewable layout panel are not represented. Legacy worlds without saved Town state consider nearby reachable sites rather than scanning the whole map on every decision; saved distant site selections remain routable. Shelters affect nearby exposure but are not reserved as individual homes. Equipment durability, repair and sophisticated logistics remain incomplete |
| Trade and economy | **Integrated but thin** | Inhabitants offer personal surplus for needed items, including physical field maps/records; each party independently accepts or refuses through planning cognition. Trading a map teaches its recorded facts to the recipient and preserves the original discoverer/source. Agents can also share a held record nearby without giving up their copy. Expiry/cancellation releases reservations; exchanges leave visible public memories | One-for-one barter, not negotiated pricing or an autonomous currency economy; opportunities depend on actual personal surplus |
| Relationships and households | **Integrated but thin** | Households, visible directed trust earned from completed cooperation, majority-voted food access, steward succession, practical apprenticeships, work practice and independently accepted/refused adult partnerships with unilateral withdrawal | Trust is bounded and affects barter-partner order and partnership eligibility; resentment, reconciliation and rich conflict remain incomplete |
| Family, aging and death | **Integrated but thin** | Separate parental consent, preparation, dependent infants and actual caregiver food/warmth delivery; infants cannot queue personal cognition. After infancy, a born child without an explicit personal model uses local safe decisions instead of a paid world default; the agent-card model editor shows the missing selection and can assign a model through the existing player flow. Household adults can volunteer to replace lost carers; older dependents accept/refuse independently. Children can choose bounded talk/play encounters, learn by observing an adult, and carry spare food to household storage; encounters save as memories and trust. Authoritative age checks exclude children from adult work, trade, partnerships, council and parenthood even for malformed proposals or owner instructions. Fresh host-created worlds start without a camp or Town and require the player to choose a first-Town site and place four unrelated starting agents before time begins. They use six-minute days, a 40-day year, and day-based stages: child at day 3, adult at day 15, elder at day 45, death by day 60 from birth. The card shows age in days. Freshly placed adults may choose their own name on an accepted ordinary personal-model decision; a player rename takes precedence. A pause-only prototype aging override remains under Developer tools | Parents do not yet choose and persist their child's model at birth; automatic births lack that conversation/selection protocol. Childhood social actions are single-agent bounded encounters, not yet joint model conversations or a full curriculum; child helping is limited to spare-food carrying. Initial personality/aspirations and deeper stage-specific social capability remain thin; lifespan balance needs playtesting; legal guardianship and inheritance remain thin |
| Ecology and weather | **Integrated but thin** | Renewable resources and seasons persist. Newly generated worlds have visible, inspectable berry bushes, wild greens, fiber plants/reeds, stone outcrops, iron/gold/diamond outcrops and clay banks, alongside seed and fertile-land sites; abundance settings also distribute food, fiber, seed, stone and regrowing wood sites beyond the starting area. Natural sites carry deterministic resource IDs, object kinds and available/depleted/regrowing presentation where their resource can change state. Sparse/Normal/Abundant changes generated site counts and appears in New World preview. Individual generated wood trees supply project wood once before becoming stumps; spring regrowth or an agent's seed replanting restores them. Orchard trees yield a separate edible fruit item before showing picked and growing stages. All tree stages and an agent’s orchard-fruit meal state are saved. Earlier generated saves retain their original resource layout and remain loadable. Generated worlds have deterministic 32×32-tile weather regions weighted by saved local climate; weather affects agents' local warmth/clothing/fire choices, exposure illness and crop yields at the field. Recent rain and dry days produce a bounded three-day local soil-moisture estimate that can modestly improve or reduce food-crop yields. The top bar follows camera-local weather; World Info also shows nearby soil moisture. The top-down map animates the same regional weather over the camera, including across a wrapped seam: rain falls as short drops that land with small splash rings over faintly wetter-looking ground, storms darken the sky with heavier slanted rain and a soft flash every several seconds, and snow drifts down. Each square weather region is drawn with a warped, softened edge that wanders and creeps slowly, so storms and showers read as irregular shapes rather than tile-aligned blocks. A very light cloud haze drifts over the map, thicker over cloudy, rainy or snowy regions and often absent over clear ones; the rejected large circular cloud overlay stays removed. The client can already switch the haze and lightning flashes off, but no setting exposes those switches yet | Natural mineral/clay outcrops are inspectable finite sites, not yet connected to recipes, tools or an ore-processing economy; natural art and site densities are prototype quality. Replanting currently uses the prototype's generic seed on existing depleted wood-tree sites, not new tiles. Orchard species, yield, seasonality and cultivation are provisional; generated fruit trees are not yet an agent-managed orchard. Cultivated grain, potato and greens fields remain separate unfinished families. Soil moisture is a short derived estimate, not per-tile hydrology. Rich biome weighting and weather fronts that actually move across regions remain unfinished; the drifting edges are presentation only. Broader ecosystems and balance also need work |
| Factions, law, currency and culture | **Integrated but thin** | A household council can change shared-food access by majority vote; persistent faction/currency/culture contracts exist | Broader institutions, currency circulation and contested law remain incomplete |
| Content governance | **Host-backed, player controls unfinished** | The host retains bounded agent-authored building proposals and separate validation/approval/staging rules. The Pause Menu's Mod Library lists this world's recorded packages and their authors read-only | The obsolete player-facing Create workbench is gone, so the normal client has no review/approval controls yet. Personal library, import/export, general invention, and committed-content migration remain unfinished |
| Asset governance | **Verified primitive** | Provenance, rights metadata, quotas, cache/reservation accounting, preview contracts and artifact envelopes | No end-to-end creator/approval experience and no production art pipeline |
| Client presentation | **Playable** | The interface uses the Timber & Parchment look: code-generated wooden frames around parchment panels, ink text, bevelled pixel buttons, fields, checkboxes, switches and scrollbars, with a Light, Dark or Match system Theme in Game Settings that restyles the open window at once and is remembered; Match system follows the operating system where it reports a dark mode. Startup Main Menu with Continue, New World, Load World, Settings, pairing and Quit Game. New World automatically refreshes an exact signed terrain/camp preview when its seed or selected options change, including balanced/uniform/dominant climate and Sparse/Normal/Abundant resources; Create World stays disabled until the current preview succeeds and then opens the matching paused camp; at 1280×720 and above its captioned options sit beside a large fixed-size preview with Back and Create World in view, wrapping to one scrolling column on narrow or scaled-up screens; Load World rows show name, current marker, save time and only incompatible/unchecked status, and double-click opens a world; starting-agent setup and Start World happen in-world. The compact Pause Menu shows Save World, Settings, read-only Mod Library, and Quit to Menu in that order; Game and World are Settings categories, and the obsolete Create workbench is absent. Save World still contains current-world manual saves. In-world panels cover shared stores, work, social notes and agent settings. The repository client draws gapless square terrain, highlights hovered ground tiles, and keeps agent hover/selection targets inside their tiles. Placed buildings and the campfire/shelter/storage/workshop camp objects are drawn as provisional code-generated top-down pixel-art roofs over their full footprint, chosen by recorded building tags (House, Warehouse, farmhouse, smithy, shelter tent, storehouse, workshop, weaving frame, hearth), with a flat roof color below 12 px tiles; Roads draw as a worn edge with a lighter center. Agents are generated top-down sprites with six stable appearance variants per life stage (infant, child, adult, elder); hover or selection rings the agent and shows a name tag, which also shows for a lone agent at close zoom on larger render sizes | The Mod Library is currently a read-only world-package list, not the intended global/import/export experience. Continent controls and the intended player-chosen rough camp site, suitability overlay, and generated starter layout remain missing. Prototype visuals: building, Road and agent art are code-drawn placeholders, not production sprites, and buildings under construction are not drawn differently. No dedicated economy/project management screen |
| Multiplayer/public worlds | **Excluded** | Single-player only by owner decision | Multiple paired owner devices are not multiplayer |
| Executable generated mods | **Planned/disabled** | Data-only packages are fail-closed | No sandbox has been selected; arbitrary generated code does not run on the host |

The private-world death path now freezes the deceased agent's personally owned
inventory lots and offers one final decision to that agent's explicitly assigned
planning model. The bounded first contract can leave the estate on the existing
household-beneficiary path or name one living agent for the whole estate. The
selected recipient and original lot IDs/quantities are validated by society
authority; unavailable, invalid, cancelled, timed-out, or interrupted calls
fall back to the household path. Pending work is never reissued after a save
reload, and completed estate settlement remains idempotent. The deceased card
shows will status/heir, and the Event Log shows the final outcome. Per-lot
bequests, debt, minors, no-household policy, and inheritance-specific law are
still open; no inheritance-law type is implemented yet.

## Decided-vision reconciliation

This is the current code audit against [decided finished-game behavior](vision-interview.md),
not a claim that all interview proposals should be implemented at once.

| Priority | Decided behavior | Current playable behavior / remaining work |
| --- | --- | --- |
| 1 | A slow or unavailable personal model does not stop unrelated agents or invent an important choice | The private-world host dispatches hosted decisions between committed ticks; unrelated agents and world systems keep advancing. Failed or low-confidence requests can select only `safe_idle`, never complete an instruction or invent a strategic choice. Pending decisions survive save/reload and stale answers after pause/provider changes are rejected. A live Windows/VPS model-wait playtest remains to be done. |
| 2 | New World creates a map; the player chooses a rough site, and generation creates the first Town with two Houses, a Warehouse, Roads, minimum food and some tools; four configured starting agents are added, then Start World begins time | The signed host and Main Menu now preview, create and select separately saved Small/Medium worlds with initial climate-mode/family/latitude choices. Preview is read-only even while the current world is running; creation still pauses it before switching. Generated worlds open paused with built-in content active; the player clicks a rough map site to place the saved five-building first Town and connected Roads, and can choose another site before placing founders. The two Houses belong to the two starting households, while Farmhouse and Blacksmith remain unclaimed. The chosen site anchors a provisional rectangular Town border; founder placement records membership, and later assigned-building additions can grow the border. A placed founder can be selected and moved to another passable tile before Start World without changing their identity, household, Town membership or model assignment. The last placement can be undone before Start World; it removes that founder and their model assignment but retains shared keys for reuse. Starting-agent placement and explicit Start World are shared with the previous development world. The existing household food stocks are located at their Houses and one usable wooden axe/pickaxe pair is communal stock at the Warehouse. Suitability guidance, a separate inspect-before-accept layout step, player-selected supplies, deeper generation controls and scalable physical-map persistence are missing; Large/Huge/Mega cannot be played yet. |
| 3 | Playtest a six-minute day and custom 40-day/four-season year, with at most six hours of life from birth | Fresh host-created worlds save 360 ticks/day, 40 days/year and four 10-day seasons; day-based stages and a hard 60-day lifespan use the same saved pace. The host still aims for one tick per real second, subject to load. The live VPS has a paused development world with this calendar; its former save is archived. The decided pacing and starting-agent setup still need a Windows/VPS playtest. |
| 4 | Each agent owns a personal model and key; Jev is an optional per-world support layer | The selected agent card and World Settings can choose one provider/model and a named saved key for an inhabitant, applying to both routine and planning decisions. The starting-agent setup uses that same private key routing before Start World. Several keys from the same provider can coexist in private host storage. Personal-model decisions retrieve a bounded, relevant set of the agent's own saved experiences and beliefs. Existing Jev routine calls may score a small batch of that agent's records, retaining source-linked evidence labels; the Jev-off path still uses local retrieval. Automatic experience capture and generated memory summaries remain incomplete. |
| 5 | One continuous zoomable pixel-art world view with an always-available draggable overview and inspection controls | The Godot Main Menu offers Continue, New World and Load World when paired. The view has single-view zoom, camera-bounded terrain drawing, overview navigation, located Event Log entries, private thoughts and a family tree. Large playable maps, land-claim overlays and automatic belief/memory formation remain unbuilt. Town-border and household-owned-building filters are available, but there are no saved land claims to display. Art is prototype-quality. |
| 6 | Local Windows install runs the authoritative simulation and keeps saves/keys on that PC | The Godot client currently requires the private VPS host. Preserve it for development, then package the same simulation locally and prove a fresh install without the VPS. |
| 7 | Regional weather, bounded inventions and mods, cross-generation social life | Generated worlds now use local weather regions for survival, work and display, with a short rain-derived soil-moisture effect on crops; seasonal effects and data-only building proposals remain thin. The Pause Menu now lists recorded packages read-only, but the full Mod Library, import/export, review flow, restricted scripted content, deep conversation/memory and full inheritance are not yet connected. |

The deterministic first-Town layout planner is connected to paused New World
setup. For a buildable rough tile it can seek the five accepted building footprints and
connect their anchors with dry-land Roads without occupying camp objects or
resource sites. Its search radius, order and route rules are provisional; it
does not yet rank site suitability or preview an unaccepted plan. The signed
site action persists or replaces the layout before the first founder is placed.
A newly created generated world has its seven shipped starter/settlement/House/Warehouse/Farmhouse/
Blacksmith/cooking packages active at tick zero; this makes their definitions
available before Start World. Choosing a site creates the five starter
footprints and Roads. The two Houses are assigned to the existing starting
households, with each household's existing food stock physically located at its
House. One wooden axe and one wooden pickaxe are available from the Town
Warehouse to residents; replacing the site does not multiply either tool.
Productive-building claims, supply choices and final quantities remain open.

## Current cognition behavior

The server generates a bounded observation and legal candidate list. Provider
output can choose one candidate; it cannot invent an undeclared world mutation.
Provider failure or low confidence now permits only the explicit `safe_idle`
fallback; without that candidate the request is rejected rather than choosing
a strategic action. A failed request cannot complete a pending
instruction. Hosted requests wait outside tick commits, so they do not stall
unrelated agents or world systems.

The hosted model is currently a one-shot selector, not yet an in-world
conversation partner. Its prompt includes legal choices, a fullness value
misnamed `hunger_basis_points`, up to four personal memory excerpts and bounded
known-map facts, but omits the agent's name, personality, aspiration, current
condition, relationships and recent thoughts. It does not explain that higher
"hunger" values mean *more full*, or establish a survival-settlement role.
These omissions are confirmed prompt gaps; whether they caused a particular
playtest behavior has not been measured. Jev's routine-choice payload is
smaller and different from the personal-model payload. Provider response
confidence is used for low-confidence fallback; its probability map does not
choose the action. No dedicated conversation-turn, free-form invention-proposal
or free-form will call is connected to normal play. See
[architecture](architecture.md) for the current provider boundary and
[known gaps](bugs.md) for follow-up evidence.

| Role | Options | Typical current work |
| --- | --- | --- |
| Routine survival and local curiosity | Deterministic, Jev | Eat, gather, move, wear clothing, tend fire, seek warmth, make a bounded scouting outing or idle |
| Planning and work | Deterministic, OpenAI, Ollama Cloud | Projects, material help, barter, bounded building proposals, household policy choices and apprenticeship requests/acceptance/refusal |

The local exploration prototype offers stable adults a short optional outing.
Each step is chosen from adjacent passable ground, and a bounded per-agent
ledger records actually visited terrain and resource sites across reloads. A
returning outing can produce a physical field record or map; agents can share
one nearby or barter it, and only the recipient learns its recorded sites.
These local records do not yet drive purposeful distant resource/Town searches,
create shared Town knowledge, or generate exploration-derived beliefs. The
player's visible map is not copied into agents' personal knowledge.

The host stages the built-in starter package through the validated content
registry on the first client-present, unpaused tick. It activates at the tick
boundary and supplies legal building/recipe choices. This also works for old
saves; already registered, rolled-back or quarantined starter packages are not
silently reinstalled. A manually paused save is not migrated merely by starting
the service.

A dependency-linked settlement supplement adds hearth/weaving/grain content
and stone, fiber and seed sources on free, passable cells. It never replaces
the starter package or silently reactivates quarantined content. Map restore
verifies both the original generator output and the bounded registered resource
additions. Work projects persist their phase, accumulated work and production
job; urgent needs interrupt work, and missing materials create requests that
other inhabitants can help fulfil. Blocked projects become eligible for
reconsideration after 60 ticks. Food production feeds ordinary eating. Carried
tools double work progress; clothing and shelter reduce exposure. Hearths
consume wood for 120 ticks of heat. Critical cold interrupts projects, while
illness alone never gates conversation, trade, learning or project selection.
Well-fed inhabitants recover illness at 12 basis points per tick above 60%
warmth and at 24 near a shelter; cold or severe hunger can instead add 8 per
tick. A fresh meal relieves 100 points (spoiled food worsens illness by 300),
and a caregiver's completed infant or accepted-dependent care relieves 250
while adding warmth.
Illness slows project work in a deterministic four-tick schedule (100%, 75%,
50% or 25%) and adds one travel cooldown tick from 25% through 74% illness, or
two at 75% and above; it never prevents work or travel. These bounded rates are
prototype defaults, not a finished health model. Bandage, Medicine and Clinic
are catalog-only content identifiers; no costs, recipes or treatment effects
have been assigned. Food decays
without affecting non-perishables, and storehouses halve household decay.
Production stock targets reduce surplus equipment work. Snow halves crop food
yield, storms retain three quarters, and bad weather can worsen exposure illness.
Regional storm rolls now become ordinary rain after at most three-quarters of
the saved world day, consistently for survival, crop work, owner observations
and save/reload. Weather still rolls by day. Away from buildings, an agent can
now seek nearby forest or a standing tree in a storm; cover reduces exposure
at the agent's actual tile. A Town-assigned building can now lay persistent,
world-owned Road tiles along a bounded dry-land route to the camp or existing
Road network. New Roads meet a building beside its footprint at a passable
entrance; they do not paint through that building. First-Town setup uses the
same entrance rule and connected orthogonal branches, and future construction
sites exclude existing Roads. Roads appear in the
world/overview and tile inspection, bias foot
routing and remove a diagonal wait tick where both tiles have Road. The current
70% step-cost factor is provisional; ordinary walking never creates Roads.
Unroutable buildings remain disconnected rather than painting Roads over water
or mountains. Inter-Town links and bridges remain absent.
A first 1×1 House design now has saved household
ownership and only its household receives nearby refuge and house-fire warmth;
its construction consumes that household's materials rather than the prototype
camp stockpile. House cooking now reserves only ingredients stored at that
specific House and returns food to that household; remote camp supplies must
be hauled there before cooking. Cooked output records its House location and
appears in that building's inspected stock. Agents enter the House tile to
collect located food/equipment, cook, or receive refuge. Multiple household
members can share that tile without an occupancy cap; children carry spare food
to their household House when one exists. Adults can carry bounded loads of
older unlocated household stock from camp to the House; stock stays with the
carrier through save/reload and only appears in the House after delivery.
Adults with more than one personally carried food serving and sufficient
fullness can also take a bounded surplus load to their own House, keeping one
serving; that food becomes household stock only on arrival and remains located
there after reload. A resident without a House has no such deposit route.
Members gathering materials for their own household project deliver carried
inputs at their House tile; only then does the material become household stock
stored there. Projects without a member-accessible House retain the camp route.
Members helping with a project in their own household likewise deliver their
carried contribution at that House; outside helpers still hand off at camp
until guest-entry rules are decided.
The current four-unit load is a prototype rule, not an accepted carrying
capacity. House access requires recorded household membership even when an
unassigned resident can still use the old camp fallback.
A household with an existing House no longer proposes a second House;
in-progress duplicate House plans end without spending supplies. A newcomer
placed on that House's property joins its household and can use the home, but
unaffiliated residents do not yet negotiate entry or decide to form a new
household. The invitation and permission rules remain open.
Other gathering, cross-household helper and production paths do not yet
physically deliver goods to House storage. Legacy Shelters remain loadable.
Two Houses are generated when the first-Town site is chosen, each holding its
starting household's food; expansion and guest entry are unfinished.
Its current eight-wood construction cost and the meal's food/wood quantities
reuse prototype values; they are not accepted finished-game recipes or
storage-capacity decisions.
An additive 2×2 Warehouse definition now activates for Town-founded worlds.
One Warehouse may be built per Town within or adjacent to the current saved
border; its footprint expands that border. The Town ID holds its communal
inventory for this prototype, without deciding formal legal ownership. Adults
can carry personally held surplus wood, stone, fiber or seeds to the building;
only on arrival does the stock become Town-held and appear in building
inspection. Resident adults from any household can physically collect stored
non-food equipment/materials when a task needs them. Food placed in a Warehouse
is rejected on save/load. Residency is checked at pickup, so leaving a Town
would not erase its Warehouse stock or grant ongoing access. The twelve-wood
cost, four-unit delivery load, single-Warehouse rule and unbounded storage are
prototype values, not accepted balance or final access law. Initial Town
site acceptance now places a Warehouse with one usable wooden axe and one
wooden pickaxe as communal starter equipment. The 2×3 expansion, whole-inventory
logistics, border-change policy and formal ownership remain open. Legacy
Storehouses remain in old saves; agents stop proposing new ones after Warehouse
content activates.
An additive provisional 1×1 Farmhouse can be claimed by a household when
placed or built. A universal-grain fertile-land recipe yields distinct grain
and seed stock for the actual worker's household, instead of the prototype
alpha-household fallback. Members physically carry unlocated household grain
from camp (or already located grain from their House) to their own Farmhouse;
the grain remains carried through save/reload and appears in Farmhouse
inspection only after arrival. Milling consumes only grain physically at that
Farmhouse and leaves flour there as private household stock. Unclaimed
Farmhouses cannot process, and another household cannot work at a claimed
Farmhouse. A member can also carry a bounded flour load from their Farmhouse
to their House; it appears in House inspection only on arrival, and the
in-transit load and delivered stock survive reload. Grain and flour are
excluded from communal Warehouse stock. The
Farmhouse's wood/stone cost, 1×1 footprint, grain/seed yields and flour recipe
are provisional pending balance/footprint decisions. The first-Town layout
places one unclaimed Farmhouse; field stages, the adjacent private Silo, crop
variety and sale of flour remain unfinished. Flour-based House cooking is not
implemented.
An additive provisional 1×2 Blacksmith can be claimed by a household when
placed or built. Household members physically move bounded wood and iron-ore
loads from their other stock to the Blacksmith; an adult can also mine a
reachable natural iron outcrop and deliver the carried ore directly. They
then use only ingredients located there to craft distinct wooden axes or
pickaxes and refine iron ore
into a separate iron item. Products remain private, on site, inspectable and
saved; an unclaimed Blacksmith cannot produce and other households cannot
work at a claimed one. A carried wooden axe improves wood gathering yield,
and a carried wooden pickaxe improves stone and iron-ore gathering yield, from
four to six units per consumed resource-site unit. These are provisional
effects without wear/durability. This building and the two tools are not yet
claimed by a starting household yet; the Blacksmith's footprint, costs,
recipes, tool tiers, direct sale and requests remain unfinished.
Food provenance distinguishes foraging, crops, cooked meals and camp rations;
inhabitants prefer a different available source, while monotonous diets reduce
their diet score. Spoiled reserved ingredients cancel
the affected production job and release its remaining inputs rather than
stalling the world. These are bounded first survival rules, not a finished
nutrition/health/ecology model.

Worker death cancels unfinished production and crop jobs before the next
completion pass, releasing unused reservations without undoing completed work.
The operator journal records `production_cancelled` with tick, job and worker
IDs, never private inhabitant text. Regression coverage includes death exactly
at the completion boundary and save/reload afterwards.

Resource markers display authoritative stock/capacity and stable hover help
for finite deposits or seasonal regrowth. Legacy hosts without these optional
fields show unknown details rather than fabricated quantities. Engine checks
exercise hover, stock refresh and marker removal alongside menu geometry.

Content rollback is conservative: placed buildings, production/crop history
and recorded settlement projects block removal before any mutation. Unused
packages can still enter quarantine without cancelling unrelated work. The
signed endpoint reports a conflict for referenced content; `content_rollback`
logs contain only tick, package ID and outcome. A reference-preserving conversion
or executable-content suspension workflow is not implemented by this guard.

After three completed buildings, a builder may choose among bounded shelter,
storehouse and hearth proposals. Practice can reduce the ordinary wood cost by
at most three units; size, effects and allowed tags remain the existing safe
workbench format. Each author may propose a purpose once, with at least 300
ticks between different ideas. The content registry keeps the author as a
durable governance event, while safe host logs contain only inhabitant and
package IDs. The proposal appears with its author's readable name in the
Pause Menu's read-only Mod Library. It remains `proposed`: only the owner can
validate, approve and stage it at the host governance boundary, but the normal
client no longer offers those controls after the pre-revision player workbench
was removed. Pause still blocks activation.

An additive forestry package supplies managed coppice: two seeds and a fertile
plot are committed for 1,440 world ticks, producing 24 wood and two replacement
seeds. This competes with food cultivation and leaves depleted wild timber
depleted. Activation waits for an unpaused tick and an active settlement
package; rollback is respected across restart. Sleep and bedroll recovery have
been removed; shelter still protects against weather exposure. Shared-food
choices require reachable pickup.
Workers retain a valid current building site, and pending teaching requests
give busy mentors a decision point without forcing acceptance.
Descendant building IDs use a stable canonical hash when society IDs contain
separators; existing founder-building IDs remain unchanged. The controlled
generation regression verifies birth, feeding, two save/reloads, adulthood,
earned training and completed construction with deterministic providers and
optional fast aging. This is a connected path, not population-balance proof.

Barter candidates require personal surplus and a useful different item held by
another inhabitant. Offers reserve one unit from each side for at most 120 ticks;
no ownership changes until both independently accept. Either party can decline
or withdraw. Spoilage cancels pending settlement safely, and a pair cooldown
prevents repeated requests. Pending decisions and completed exchange memories
appear in social notes; the host emits bounded `settlement_trade` outcomes.
This is a small barter loop, not negotiated pricing or currency circulation.

Completed material help, mutually accepted barter and finished teaching now
increase bounded directed trust. The client shows each positive score; barter
opportunities prefer trusted partners and partnership proposals require prior
trust. Existing public cooperation memories project their old meaning until a
new event persists schema 12. Refusal, withdrawal and policy disagreement do
not reduce trust. The world still lacks justified harm, resentment and
reconciliation mechanics, so this is a positive-cooperation consequence rather
than a complete relationship simulation.

After settlement activation, public contributions qualify a household steward.
The steward may propose reserving scarce shared food or reopening abundant
stores, but each adult member independently votes. A majority is required;
refusal or expiry retains the previous policy. The reserve rule blocks pickup
by members with hunger at least 4,500 only while shared stock is at or below one
serving per inhabitant; hungrier members retain access. It does not confiscate
personal food. The current rule persists when a living successor replaces a
departed steward. The Settlement panel shows leadership, policy and ballot
counts; `settlement_council` logs report bounded transitions, not free-form text.

Adult traders and unassigned adults can request basic builder/farmer training.
A practitioner in that role or a teacher must independently accept. Twenty
joint work ticks near camp grant the requested role, enabling its ordinary
building/crop choices. Food and urgent exposure interrupt progress;
refusal, cancellation, expiry or mentor death grants no role. Each mentor has
one active learner. Lessons survive pause/restart, appear on inhabitant cards,
and create public gratitude when completed. This is not a general skill tree.

The selected agent card opens an in-place model/key editor; World Settings can
also select either **World defaults** or a named inhabitant. World defaults
retain separate routine and planning roles; a named inhabitant selects one
personal provider/model for both roles or inherits the defaults. A hosted agent
can use the provider's default key, an existing named key, or add a new named
key for that same provider. Keys remain in installation-local host storage;
only non-secret slot labels and IDs are returned to the owner. Removing a
provider's default key removes only assignments that depended on that default;
named-key assignments remain. The owner can delete a selected named key after
switching all agents in the active world away from it; the signed deletion
removes its key from the current installation-local provider file and refreshes
the visible key list. Older world/checkpoint assignments that refer to a
deleted key safely restore as deterministic rather than inheriting a different
hosted account's credential. Filesystem backups and storage-device remnants are
outside this in-app deletion guarantee. Personal selections are signed,
target-bound and durable.

Unchanged idle intentions are reused for up to 300 ticks, including across
reloads. Changed legal choices or urgent need bands trigger reconsideration;
an observation with only `safe_idle` does not need a provider call. The selected
inhabitant card shows the last accepted decision, with available usage/model/
latency details in a tooltip. Missing model/latency is shown as unavailable;
adapter token counts can be zero when the provider omits usage.

World Settings shows an **installation-lifetime** model-call meter with a plain
used/limit count, provider/model subtotals, and detailed outcome/token counts
in its tooltip. It explains that reaching the limit pauses time. The unit of
the optional limit is **hosted call attempts**, not token cost or currency:
retries, failed calls and calls abandoned on pause/quit each consume an
allowance even when the provider returns no token count. The limit is off by
default. At its cap the host persists an explicit world pause before another
paid request; ordinary deterministic choices remain free. The owner can grant
100 further paid attempts in World Settings, then resume the world separately,
or remove the cap.
The installation-local meter persists across worlds and host restarts but does
not claim to reconcile against a provider invoice; token counts may be zero
for attempts that never returned usage. At most 64 named provider/model/role
combinations are retained, with later combinations grouped as `other`. This
call-attempt unit and lifetime period are the current playtest implementation,
not a finished-game billing policy chosen in the vision ledger.
The repository host now defaults the meter to the directory of the configured
private provider state, and a failed reservation write leaves the in-memory
attempt total unchanged before any model request is sent. The currently
running VPS process predates this fix; its writable-directory mitigation has
restored model decisions, but its meter requires correction at deployment for
phantom reservations from earlier failed writes.

## Evidence boundary

The repository's executable evidence is the merged code, automated tests,
Godot startup/export checks and exact-commit CI. Retired phase documents are
available in Git history, not active status sources. The
[vision ledger](vision-interview.md) describes the intended finished game,
which is not interchangeable with this prototype capability matrix.

Future changes must update this document when a capability moves between
planned, verified primitive, integrated or playable status.
