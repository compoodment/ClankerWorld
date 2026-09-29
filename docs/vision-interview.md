---
title: ClankerWorld Vision Interview and Decision Ledger
type: product-vision
status: active
updated: 2026-09-28
---

# ClankerWorld Vision Interview — Current Working Picture

> Last reconciled: 2026-09-28. This is the **current** interview notebook, not a
> finished specification, implementation claim, or approval to modify the game.
> It separates decisions from proposals and unresolved questions. The
> [historical interview record](archive/vision-interview-history.md)
> preserves earlier answers and Clanker's proposals, including old “Open” labels
> that have since been resolved. **When they disagree, this file wins.**

This file is the **living ledger of computment's intended finished-game
experience** during the interview. A decision here is not a claim that the
current ClankerWorld prototype implements it, nor an instruction to build every
accepted feature immediately. The repository's current-state and architecture
documents must describe what actually runs; this ledger guides implementation
order. This ledger is versioned in the game repository as the single authority
for owner intent, with the former workspace path retained as a pointer for
continuity.
Historical interview material remains separate and subordinate.
Obsolete phase-by-phase implementation documents were removed from the active
documentation during repository cleanup; Git retains their history. Operative
build, pairing and release rules remain in focused references. This ledger
still distinguishes the finished-game vision from the present prototype.

## How to read this

- **Decided** means computment chose or accepted the direction. Exact numbers or
  implementation details may still need playtesting.
- **Preferred** means a strong current leaning, but not yet a final choice.
- **Proposed** means Clanker's suggestion, not yet accepted as canon.
- **Open** means we really have not settled the question.
- The finished-game vision is distinct from what the existing ClankerWorld
  prototype currently implements. Repository code and documents are evidence,
  not automatic authority over ClankerWorld's future design.

## What ClankerWorld is

**Decided:** ClankerWorld is a pixel-art world simulation about AI agents
surviving, socializing, building civilization, and eventually inventing things
that can change their own world. The simulated people are called **agents**. The
personal LLM is integral to each agent's thinking and social identity, not a
cosmetic narrator. Deterministic world systems enforce what can physically and
legally happen. Jev may be enabled as a per-world support layer; it does not
replace agents' personal models.

The game should allow observation and player intervention without making
survival relentlessly harsh or conversations rare. There is no arbitrary
fictional population cap. Whether to add an explicit player-configurable limit
on automatic births is **deferred until playtesting**, not currently decided.
Hardware, provider capacity, and cost still impose practical limits.

## Where the game runs

### Decided direction

- **During development and computment's own playtesting:** keep the current
  Godot desktop client connected to the private VPS-hosted simulation. This
  allows Clanker to inspect server logs, observe the running world, debug, and
  tune behavior. There is no need to move the current development world just
  to settle the eventual distribution architecture.
- **For the first finished game distributed to other players:** provide a
  simple PC install that runs the game and its authoritative simulation on
  that player's own computer. Their saves and provider credentials belong on
  their computer; playing must not depend on access to computment's VPS or a
  mandatory hosted game account. If they choose cloud-hosted LLMs, those
  providers still require an internet connection and applicable credentials;
  local game hosting does not mean all model inference is offline.
- **The first finished release supports Windows only.** Computment has a
  Windows laptop for actual desktop playtesting, but no Linux or macOS desktop
  testing setup. Linux/macOS support is not a launch obligation; interested
  contributors could help add and verify those platforms later. A Linux
  server machine does not count as testing a Linux desktop game.
- **Players bring their own model-provider API keys and pay their own provider
  charges.** The game does not include developer-funded AI usage for other
  players. Provider credentials are stored per installation, separate from
  shareable world saves; they are not bundled with the game. Opening the game
  does **not** require a key or a first-launch provider setup gate. Credentials
  are supplied or selected when assigning a provider/model to an agent.
- Keep one simulation/game-rules implementation across the private-VPS and
  local-PC deployments. The player-facing Godot client should not become the
  authority merely because the host runs locally. Closing the game still
  freezes the world, without offline catch-up.

### Current prototype evidence (not a finished-game constraint)

The supported playable path is an unsigned Windows 11 x64 **Godot desktop
client** communicating by signed HTTPS with a private headless .NET world host
on the VPS. The host owns simulation, saves, cognition, and provider adapters.
The existing static web assets are legacy diagnostics, **not a browser game**.
The host stops ticks and model calls shortly after the last authenticated
client disconnects. This arrangement is useful development scaffolding, not
the intended required infrastructure for other players.

### Open implementation details

Whether the local host is embedded in the installed game or bundled as a
background companion process; installer and update design; exact Windows
version/architecture support; local credential storage and diagnostics export;
save migration between deployments; performance requirements and packaging
tests; on-demand credential validation and the new-world starting-agent setup when no
key exists yet. These are implementation choices to prove, not reasons to
reopen the decided player-local distribution goal.

## Main Menu, world view, and controls

### Decided

- Launch into the **Main Menu** with **New World**, **Load World** when worlds
  exist, **Settings**, **Quit Game**, and a separate **Continue** action for the
  most recently played world.
- **New World** offers world size and climate choices, advanced generation
  controls, preview/reroll, wrapping, and an optional early survival grace
  period. The ordinary game has **one supported first-Town setup mode**. The
  nothing-start challenge mode was removed from the plan for now. Generating
  the world creates the map and opens it paused; four starting agents must be
  configured before starting time. The first-Town siting and generated
  building layout flow are specified below. API keys are not a prerequisite for
  reaching the world view.
- Enter a visually coherent generated world. The player can see its whole
  geography from the start; there is no player fog of war. Individual agents
  may know only what they have experienced or learned.
- **Decided after reviewing the tile-inspection prototype:** selecting ground
  should open an inspection panel with meaningful, authoritative world facts.
  Keep terrain kind separate from generated climate, elevation, hydrology,
  surface and vegetation cover; show present objects, resources, buildings,
  regional weather and soil moisture from their own observations. Omit absent
  or unprojected facts such as fertility and exact temperature from the panel;
  never infer them from terrain color or fill the panel with `none` and
  `unavailable` labels. Player inspection does not teach an
  agent those facts. The exact final layout and future biome/fertility fields
  remain open as those systems become real.
- One continuous pixel-art world view supports mouse-wheel zoom and WASD
  panning. On an east/west-wrapped world, the camera should pan continuously
  across the seam in either direction. There is **no separate simplified
  regional view or second regional texture set**. Zoom-out stops at a
  readability/performance limit. After the September 29 playtest, the cap must
  also keep black space beyond the north/south map edges out of view on Small
  maps, and maximum zoom-in should feel consistent across render resolutions.
  Computment suggested Small/Medium zoom-out around 70% of map-fit scale and
  a shared performance cap for Large/Huge/Mega; that exact percentage and
  visible-tile budget remain preferred starting points for tuning, not fixed
  finished-game numbers.
- Hovering a ground tile shows a square tile highlight. When an agent overlaps
  that pointer location, **agent hover/selection takes priority** over the
  ground tile. This is pointer hit-testing priority, distinct from making the
  agent's clickable footprint spill into neighboring tiles.
- An always-accessible **top-left Map button** expands a world overview. It
  shows the current camera rectangle; dragging it or clicking the overview
  moves the main camera. The overview can be drawn from world data rather than
  a second library of game textures.
- The top bar also shows a **pause/resume-time control**, in-world date and
  time, agent population, a **World Info** button, a **Filters** button, an
  **Event Log** button, an **Add Agent** button near the menu button, and the
  menu button at top-right. Date display format and 24-hour/AM-PM time format
  belong in UI Settings.
- **Decided after the September 29 style review:** the Event Log button shows
  how many events arrived since the player last opened the log. Because a
  world can hold several Towns, the top bar has no single-Town button; Town
  facts belong in World Info.
- Important events belong in the **Event Log**, not optional out-of-view
  event pop-ups/notices. Clicking the top-bar Event Log button opens the log;
  clicking a located event moves the camera to where it happened. Opening the
  log does not pause the world. This supersedes the earlier configurable
  notification proposal; an agent's inspectable info panel is not an event
  notice.
- World Info should let the player inspect discovered capabilities and other
  world information. Filters should reveal established household property
  borders, Town borders, and similar world facts. The UI must not invent
  ownership or borders that agents have not established.
- The top-right menu button opens the **Pause Menu** and pauses the world. It
  contains **Save World**, **Settings**, **Mod Library**, then **Quit to Menu**
  at the bottom. There is **no Quit Game action in the Pause Menu**; Quit Game
  belongs to the Main Menu. Settings use categories on the left and selected
  controls on the right. Quit to Menu and Main Menu Quit Game ask for
  confirmation.
- Main Menu Settings must retain the **Main Menu background/context**, not
  reveal the in-world UI underneath. Selecting the already-active Settings
  category leaves its controls visible; category buttons are selectors, not
  open/close toggles.
- **Reaffirmed after the September playtest:** the Pause Menu is compact, not
  the current three-row button grid. Keep the order above so Quit to Menu is
  always last/bottom. It has one **Settings** entry; **Game**
  and **World** remain selectable categories inside Settings for a loaded world.
  Main Menu Settings still exposes Game only. **Remove the current player-facing
  `Create` building-design workbench entirely**; computment considers it a
  prototype from before the revised agent-driven invention vision. This does
  not remove agents' ability to invent or settle any future way to inspect
  their designs.
- Game Settings should offer **Window Size** and **Render Resolution** as
  separate controls. Changing only the window dimensions must not be presented
  as changing the game's render resolution. Exact presets, scaling behavior,
  pixel-art/UI layout rules remain open. Start in fullscreen by default, while
  respecting a saved player choice to use a window. UI Scale must enlarge the
  usable controls and panels, not only their fonts, especially at higher
  render resolutions. Main Menu Settings needs one compact back chevron in
  the close-button position; omit duplicate large back navigation and basic
  explanatory paragraphs beneath Render Resolution and UI Scale. Load World
  does not need the pause/save helper sentence. Player-facing wording should
  be short and ordinary; `Agent model` is preferred over `Inhabitant cognition`,
  and the optional model-call limit needs a clear unit and consequence.
- **Decided after the September 29 style review:** the interface uses the
  **Timber & Parchment** look chosen from three mockup directions: wooden
  frames around parchment panels, ink text, bevelled pixel buttons and a green
  main action. Game Settings offers a **Theme** of **Light**, **Dark** (dark
  wood with cream text) or **Match system**. The exact font, icon set and
  remaining accessibility treatment stay open.
- Adding an adult agent opens a flow to select a provider, one of its stored
  API credentials or a newly entered one, and a model, then place the agent in
  the world. Existing credentials can be reused by multiple agents. A player
  may also add another key for the **same provider** and choose which key that
  agent uses; there is no single-key-per-provider restriction. The agent
  chooses its own name after placement; the player can rename it later. Agents
  should choose full names with a surname and may choose a middle name. Do not
  include every existing agent name in the naming prompt merely to avoid
  collisions. The world should reject an already-taken full name and let the
  agent choose again; similar but distinct names are allowed. Exact duplicate
  matching, retries/failure fallback, player renaming collisions and cultural
  naming context remain open.
- Selecting an agent opens an **interactive info popup near that agent**. The
  player can change that agent's provider and model there, including choosing
  an appropriate stored/new credential when needed. Agent inspection does not
  pause the simulation. This is a selected-agent panel, not a fleeting
  mouse-hover tooltip that disappears when reaching for its controls. Its core
  view shows name, age/life stage, household, current activity, needs, and the
  agent's latest **private** thought or intention. A **small scrollable history
  of recent private thoughts** is available there too. The player can inspect
  them, but other agents do not automatically know them. A separate
  **Memories** section lets the player inspect what this agent remembers or
  believes happened, including mistaken beliefs; it is not the authoritative
  world event log. Inventory, relationships, and model settings can expand
  from the same popup. A **Family Tree** action opens a larger interactive
  graphical view; selecting a person in the tree opens that person's agent
  info popup, including for deceased relatives. The tree distinguishes
  parent-child ancestry and partnerships; household membership is displayed
  separately, never as proof of biological family. Unrelated starter
  housemates must not be drawn as relatives.

### Preferred, pending confirmation

Game Settings apply across worlds/on this installation:
UI date and time display formats, graphics/display preferences, and stored
provider credentials. The Main Menu's Settings entry opens
Game Settings. World Settings belong to the current save: autosave on/off,
interval and rotation; the optional AI-usage meter/limit; and Jev's per-world
configuration. Selecting either category within Pause Menu Settings keeps the
world paused.
World generation choices such as size, climate and wrapping are chosen before
creation and should be inspectable afterward, not silently mutable settings.
Jev's on/off switch for an existing world is accepted; the exact settings
categories and transition behavior for an in-flight Jev task remain open.
World Settings must be absent from Main Menu Settings while no world is loaded;
opening Main Menu settings must never enter a world.

In the **Add Agent** placement view, show existing household property and
Town borders so computment can see the new agent's initial affiliation.
Placement on household-owned tiles **forces the new agent into that household**
(and its enclosing Town, if any); this player setup action does not seek
household consent. Placement elsewhere within a
Town joins the Town but no household; placement on unclaimed land
outside both starts an independent agent. Location establishes **starting
social membership**, not biological ancestry or permanent membership based on
where the agent later walks. A player-added adult could be a new unrelated
family line even when placed inside an existing household. Exact overlap,
invalid-placement rules, and later voluntary household changes remain open.
This forced Add Agent membership is distinct from inviting a nonmember to
visit a House. The agreed first-Town household setup below is separate from
this later Add Agent placement rule.

### Open

Exact top-bar layout on small screens; the final zoom-out/visible-tile cap;
which overview and filter layers ship first; display of disputed or overlapping
claims; the precise event categories, filter UI,
event retention, and handling of events without a single map location; custom
month/season names and date presentation; and the detailed player-control/
observer boundary beyond adding and renaming agents. Computment may provide a
UI drawing.

The agent info popup's exact layout, pin/expand behavior, thought-history
retention count, how memories are grouped/searched/labeled, family-tree
line styles/navigation and any future guardianship/adoption links, and what
happens to a pending model call when its provider/model is changed are open.
A proposed low-cost implementation is to show a short, timestamped,
in-character thought/intent supplied alongside an agent's ordinary decision,
rather than generating a continuous stream or
presenting inaccessible model-internal reasoning as the agent's thoughts.
The recent history should persist with the world save but remain bounded.

## World time, pausing, and slow models

### Decided

- **The world freezes when the game is closed.** It resumes from the saved
  state when reopened, without unattended catch-up simulation or provider
  spending. The supported current private-world prototype already stops ticks
  after the last authenticated client disconnects; a much older design-log
  direction for unattended simulation was superseded in the repository.
- While the game is open, the **pause button and Pause Menu are the only
  ordinary player-opened controls that pause the simulation**. Opening the map,
  World Info, agent inspection, or a conversation panel leaves time running.
  Reaching an optional AI-usage hard limit is a separate automatic pause.
- **Fast-forward/speed controls are not on the current roadmap.** They may be
  reconsidered later, but faster clock progression does not make model calls
  return faster.
- If one agent's model is slow or unavailable, the rest of the world and other
  agents continue. The affected agent may wait or do safe routine activity;
  the game must not fabricate that agent's important choices. If its provider
  fails or its key runs out of credit, the game shows the problem and **does
  not automatically switch that agent to another paid model**. The player may
  repair the credential or explicitly choose a new provider/model from the
  agent popup. Jev cannot impersonate the affected agent's personal model.
- When the player closes the world/game while a model request is in flight,
  save and exit promptly rather than waiting for that answer. Cancel or discard
  the unfinished request, commit **no partial agent action**, and retain the
  unresolved decision point. On reopening, the agent re-evaluates it if still
  relevant, which may require another provider call. The provider might charge
  for both the abandoned call and its later retry; this is an accepted
  trade-off for responsive quitting and no background model work.
- The finished game offers an **optional AI-usage limit** alongside
  a visible usage meter. When the limit is reached, it pauses the world and
  asks before making further paid model calls; it does not cap the fictional
  population. Whether the limit applies per world or across the installation,
  its accounting unit, period/reset behavior, warning levels, and
  provider-bill accuracy remain to be designed. The playtest UI's `Model limit`
  wording is confusing; describe it in ordinary terms as a count of paid
  model-call attempts, clarify that reaching it pauses time, and do not imply
  a precise currency or token budget.
- **The current prototype's 24-real-minute day is rejected as ClankerWorld's
  finished-game pace.** It was an implementation choice, not a user-approved
  design decision.
- **Accepted starting pace for playtesting:** a custom **40-day year** with
  four **10-day seasons**, initially one six-real-minute day, one real hour per
  season, and four real hours per year. Four 10-day months can support the
  chosen numeric date display. A six-hour maximum life from birth would then
  span at most 60 world days, 1.5 years, or six seasons. These numbers may be
  changed after playtesting; they are not final performance or pacing promises.
  This custom calendar supersedes the earlier 365-day preference.
- **Preferred:** biological age corresponds to elapsed world/calendar time,
  but its display and life-stage milestones need rethinking for short lives.
  Night should occupy more of each cycle relative to daylight than in the
  earlier proposed split; its exact share is not decided. Night affects
  temperature and weather, without a sleep/energy requirement or a separate
  night-only travel, visibility, work or social restriction. Weather itself
  can still affect agents under the ordinary weather rules.

### Current prototype evidence (not a finished-game decision)

The supported **integrated private-world host** schedules one world tick per
real second. Newly created worlds now save the accepted 360-tick day and
40-day year; the existing paused development save still carries its older
1,440-tick/365-day calendar. Neither pace is guaranteed under load. The
repository build dispatches hosted cognition outside the tick so a slow
provider does not hold unrelated agents or the clock; the VPS server has been
updated, but a resumed paired-client playtest remains. It gates ticks and provider calls on authenticated
client presence; the last disconnect closes that gate after about five
seconds, with no offline catch-up. The separate small fixture/kernel schedules
six ticks per second (**four real minutes per day**) and must **not** be
mistaken for the playable private-world pace. Older design documents propose
**2:40 daylight / 1:20 night** for that four-minute target. A source search
found no integrated sunrise/sunset or daylight/night cutoff, so that split is
not verified as current game behavior.

### Open

Playtest the accepted starting pace and revise it if days, seasons, or agent
lives feel rushed or slow. Still open: month/season names, the daylight/night
split, detailed stage effects, sunrise/sunset and seasonal variation, provider
work at pause/quit boundaries, safe routine activity for a stalled agent, and
how to communicate provider delays without freezing the world.

Provider cost is unknown until agent call rates, token use, model choices, and
population are measured. Representative tests can measure this before the
entire game is complete; nominal calendar speed alone does not determine
model spend. The usage meter and optional stop are accepted, but their
accounting details need design and playtesting.

## World generation, geography, and ecology

### Decided

- World-size presets are **Small, Medium, Large, Huge, Mega**. They should feel
  roughly like one Town, several Towns across a region, one major continent, two or three
  continents, and a planet respectively. The proposed logical dimensions are
  **256×128, 512×256, 1024×512, 2048×1024, 4096×2048**—initial benchmark
  targets, **not locked constants**.
- Use **64×64 logical tiles per chunk** as the initial organization target.
  Chunks help storage, loading, and rendering without forcing an entire chunk
  into one giant texture. Rendering only what the camera sees is distinct from
  simulating what happens elsewhere. Agents, crops, and Towns do not stop
  existing or progressing outside the camera. Distant work may be event-driven
  or coarser only if outcomes remain credible.
- A world may enable **east/west wrapping** with real northern and southern
  polar regions, or choose no wrapping. The default climate has a warmer
  equator and colder poles; an Advanced Setting can disable latitude cooling
  for unusual worlds. North/south wrapping into a torus is not intended.
- Climate choices include **uniform**, **dominant**, and **balanced** modes,
  with advanced controls such as water percentage, continent count, and
  resource abundance. Keep simple presets, plus **advanced sliders for
  meaningful geography** such as forest cover, mountain relief, water/rivers
  and resources; not every internal generator parameter needs a control. A
  responsive, **exact preview of the selected world seed and settings** updates
  when those choices change, so accepting it creates that geography rather
  than a different approximation. Exact sliders, ranges, dependencies and
  performance strategy remain open.
- **Small and Medium each have one continent**; a continent-count control is
  available only for **Large, Huge, and Mega**. The selected count is a loose
  generation target, not a promise of that many ocean-separated landmasses:
  generated land may form distinct continents or connections between them.
- **Simple regional weather is accepted.** Weather is not synchronized across
  the planet. Different regions can experience different conditions, with the
  local climate influencing how likely rain, snow, and other conditions are.
  Visible clouds/rain/snow and rain affecting soil moisture/crops remain the
  starting direction. Severe weather matters, but should be bounded in duration
  and should not monopolize an agent's life. The degree to which ordinary
  cold, heat, wetness, shelter, clothing, and travel/work penalties drive
  behavior is **reopened** after the survival-heavy playtest. Floods,
  disasters, and elaborate weather physics are not assumed for the first
  complete game.
- Model **climate zone**, **elevation**, **surface**, **hydrology**, **vegetation
  cover**, and **objects** separately. The generator makes plausible forests,
  grass, stone, snow, water depths, and transitions for their locations.
  Each tile holds **at most one tree**; do not depict several harvestable trees
  as one stand on a tile. Trees yield wood when harvested, leave stumps that can
  regrow, and can also be replanted from seeds when forest resources are depleted.
- **September 29 terrain direction:** sand is not a compulsory strip along
  every coast and especially not along every river. A forest-floor tile should
  visibly contain a tree or plant, mostly trees; grass-surfaced forest tiles
  should instead have scattered trees. Trees and ordinary plants should not
  grow on sand. Computment now disfavors cacti entirely, though that exclusion
  was phrased tentatively; avoid expanding cactus content until settled. Hills
  forming a readable base around mountain regions are preferred; their exact
  elevation thresholds and passability remain open.
- Generated geography includes **rivers** as well as oceans, shores and lakes.
  Rivers belong to the 2D, top-down tile world and its hydrology layer; they
  must remain continuous across an enabled east/west world seam. A noise
  function may sample three input coordinates to make a seamless **2D** map;
  that does not imply 3D graphics or 3D gameplay.
- Agents can reshape some terrain through activity. They can cross water with
  crafted boats and shore-connected ports and can later invent improvements.
  Roads exist and influence travel and building placement. **Mountain and peak
  tiles cannot hold construction**—including buildings, farms and roads.
  Agents may cross **mountain** tiles, but more slowly; **peak** tiles are
  impassable. Exact mountain travel cost remains open.
- In the intended finished game, agents can **move diagonally** on the 2D tile
  map, and roads can also run diagonally. The playable foot route finder now
  supports diagonal steps at 141% of cardinal entry cost and requires both
  orthogonal shoulder tiles to be passable; occupied shoulders also block a
  live diagonal move. Diagonal Roads remain unimplemented.

### Decided crossings and preferred default terrain visibility

At default settings, generated worlds should visibly include forests and
mountain regions rather than relying on rare seeds to reveal them. The
attainable guarantee for each world size/climate and preview update performance
remain open.

Computment wants agents to cross **one-tile-wide rivers on foot, more slowly**
than dry ground. The world automatically adds bridges at sufficiently used
crossings. A generated Road may also **create a bridge immediately** where its
route meets a bridgeable river; it need not wait for traffic there. Once a
bridge is placed, a no-other-bridge radius prevents a
redundant bridge appearing right next to it on the **same crossing/river**.
It does not block a needed bridge over a separate nearby stream. Exact radius,
traffic threshold, bridge
materials/work, and wider/deeper river crossing rules remain open. Town site
planning and Road generation must be designed together; Road persistence is
specified in the building/Town section below.

### Open

Exact terrain/vegetation/object mechanics beyond the accepted base
[asset roster](finished-game-asset-roster.md); exact climate-generation
formulas, map topology at polar edges, biome transitions, water and elevation
rules, resource distributions, continent-count variation and incidental islands,
travel times, world-size performance, and limits
on agent-caused terrain changes. **Regional weather details remain open:**
how large/coherent weather regions are, how events move or change, how long
they last, and the exact strength/mechanics of the agreed initial effects.
Climate's long-run
rainfall/moisture is distinct from any individual rain event. The 64×64 chunk
and preset dimensions need
benchmarks before becoming implementation promises.

**Proposed river-generation approach, not yet a locked algorithm:** generate
elevation and long-run rainfall, route water downhill toward coasts or inland
lakes, accumulate upstream flow, and mark sufficiently fed channels as rivers.
Handle trapped low areas as lakes/outlets and use east/west-wrapped neighbors
when wrapping is enabled. River abundance, width, crossings, seasonal behavior,
and exact effects on farms and Towns remain open. Noise is a candidate
for the terrain fields, not a substitute for drainage routing. Relevant
references: [Red Blob's noise-map guide](https://www.redblobgames.com/maps/terrain-from-noise/),
[the polygon-map guide](https://xenon.stanford.edu/~amitp/game-programming/polygon-map-generation/),
[the Voronoi river tutorial](https://www.redblobgames.com/x/2022-voronoi-maps-tutorial/),
[Mapgen4's rivers and rainfall](https://www.redblobgames.com/maps/mapgen4/),
and the [FastNoiseLite library](https://github.com/Auburn/FastNoiseLite).

Regional weather can be updated by world systems rather than requiring an LLM
call for each weather change.

## Pixel-art assets and generated art

### Decided

- **Pixel art** is the visual style. **32×32-pixel ground tiles** and **PNG
  runtime assets** are the initial standard. Taller agents/trees and multi-tile
  buildings can use larger transparent images anchored to logical tiles.
- **Straight top-down** is the chosen art perspective for now: show the tops of
  objects, not isometric sides. Isometric would need a visual comparison before
  reconsideration, and no second isometric asset library is currently planned.
- For a given terrain surface, start with roughly **two texture tiles**—for
  example, clean grass and a subtly different grass tile. Small ground details
  belong mainly in those textures, rather than being a large set of separate
  decorative world objects. Functional transitions/edges are separate.
- The September 29 prototype grain, striped transition texture, and large
  circular rain/storm overlays are rejected. Keep terrain visually calm and
  weather readable without opaque repeating circles; final replacement art
  remains open.
- Ground remains square-tiled, but permanent **black tile-border grid lines**
  are not part of the intended presentation; they would clash with textures.
- Most production textures will likely be created with AI help, including work
  with Clanker, then adapted to a coherent game style. Aseprite is an optional
  editor/source format, not a requirement for computment or for playing the game.
- Agent inventions can include generated art. An exported mod carries its
  approved assets with it; art must not become executable authority.
- Visual/content variety must be **bounded and supportable** across the game,
  not only for buildings. Do not imply that every theoretical combination of
  shape, material, style, state, and invention needs bespoke art or a bespoke
  simulation rule. This is a production constraint, not a ban on agents making
  genuinely new things.
- For the **first complete game**, each supported design can have **one
  standard appearance**. Two agents building the same size/type of house need
  not get different roof colors, decorations, or whole new sprites. Extra
  visual variants across buildings and other categories are optional work for
  after that complete game, not part of its required art workload. Agents are
  the exception: a few appearance variants are wanted for their population.
- Agent inventions may introduce **genuinely new designs** rather than being
  permanently limited to the original art catalogue. Each new design still
  needs a valid visual/gameplay representation; this does not authorize every
  theoretical combination.
- Worn everyday, cold or wet clothing **does not change an agent's map sprite
  appearance**. Clothing has item icons and gameplay effects. Armor imagery
  may be prompted as new art later; whether worn armor is shown on agents and
  the exact generation/approval workflow remain open.

### Proposed, not yet settled

Use editable `.aseprite` or layered PNG sources where useful; export PNG
spritesheets/atlases plus metadata for asset ID, footprint, anchor, layer,
collision, frames/timing, style/biome, creator, rights, and version. Build a small
visual reference scene before locking a palette or mass-producing textures.
For autonomous inventions, first try approved component assembly/recoloring;
optionally request new AI imagery under player-controlled cost limits. Normalize
it to the pixel grid, check technical/provenance/performance rules, preview it,
and use a legible fallback sprite if generation fails. Aesthetically odd art
should not automatically erase a mechanically valid invention.

Use a small set of supported visual families for built-in content, then add a
new family when an invention truly needs a new silhouette. Reusing an image
need not mean two creations behave identically. The exact reuse/generation
method remains Clanker's proposal, not an accepted implementation rule.

### Open

Exact palette and style guide; animation standards;
AI-generation provider and spending controls; how much visual cleanup can be
automated; quality criteria; asset size budgets; and the final art/content
metadata contract. Also open: the minimum distinct designs each category
needs, and how an agent invention technically expands the supported visual
vocabulary. The first complete game does **not** require extra cosmetic
variants just for variety. Existing code has partial PNG validation, **not**
the full finished-game autonomous art pipeline.

## Audio and dialogue presentation

### Decided for the first complete game

- Agent conversations remain readable as text in their chat bubbles and
  expandable conversation view; generated voices are not required.
- **Audio is deliberately outside the first complete game's scope.** This is
  not only a resource constraint: computment does not expect sound in general
  to add much to ClankerWorld. They also have no resources to produce music or
  effects. Important information and interaction must remain fully
  understandable without sound.

There is no active audio plan. Music, environmental effects, or voices can be
revisited later only if the design case changes; none is on the required
first-complete-game roadmap.

## Starting world, agents, and family continuity

### Decided

- A civilization-oriented new world first generates its map without agents.
  During paused setup, the player chooses a rough site and Town generation
  creates the **first Town** with its initial border and buildings. The player
  adds and configures **four biologically unrelated starting agents** using
  the per-agent provider/model/credential flow. They are grouped 2+2 into two
  starting households, not forced couples; family lines develop later through
  relationships and children. All four begin as agents of the first Town.
  The simulation cannot begin until this setup is complete. The exact order
  of site/layout acceptance and agent configuration remains open.
- Once all four starting agents are configured and placed, the player explicitly
  presses **Start World**; the simulation must not begin automatically on the
  fourth placement. An incomplete starting-agent setup is saved so the player can
  quit and finish it later. Before Start World, the game should clearly show
  progress toward the required four agents. During this paused setup, agent
  placement can be moved or undone. They should start near the first Town's
  buildings; exact clustering, layout-editing controls, and prior 8/12-tile
  playtest radii must be revisited with the generated layout.
- Each agent independently has a chosen provider/model, private memory and
  context, goals/personality, call schedule, usage, and failure state. Different
  agents may use the same stored key. Agents choose their own personality,
  aspirations, skills, and initial identity. The player can add adults freely.
- Agents have no genders. Children have **two parents**. Parents choose the
  child's name and provider/model, and must choose **one of their own surnames**
  as the child's surname. Children inherit tendencies, abilities,
  culture, and provider/model settings; baby appearance uses baby art. Close
  biological relatives cannot pair.
- Agents can become a couple and then marry, but **a couple may have a baby
  without marrying first**. Marriage is not a birth requirement. When agents
  marry, both partners must share one of their existing surnames, chosen by them
  in a dedicated conversation. That
  conversation is initiated even if they are far apart in the world: a narrow
  exception to ordinary proximity-bound conversation, not a general remote
  communication ability. A completed marriage must not be left without a
  shared surname. The conversation must be bounded; if the partners do not
  agree within that bound, a disclosed tie-break rule selects one of their two
  surnames so the marriage can finalize. The tie-break method, turn limit, and
  provider-failure behavior remain open. Unmarried parents may have different
  surnames, so their child's surname choice is meaningful; married parents
  already share one surname, so either parent's surname is the same choice.
  The low-population continuity rule may encourage marriage but must not make
  it a prerequisite for having a child.
- **A child uses their own selected personal LLM once they leave infancy.**
  Infants do not make calls to their personal model. The parents' provider/model
  choice can be stored at birth, then used when that agent enters the child
  stage. This resolves the earlier open question about *whether* children use
  their own models; initial age-up thresholds are below. Jev must not be
  required for infant care, because Jev is optional per world.
- **Children are real social agents, not silent placeholders.** Their personal
  models can converse, play, learn, form friendships, and choose age-appropriate
  simple helping tasks. Adult-only decisions such as land deals and parenthood
  wait until adulthood. The world system, not merely a model prompt, enforces
  those age-based permissions.
- **Current lifespan anchor:** an agent can live no more than **six hours of
  unpaused world time from birth**. They may die earlier. This is a maximum,
  not a promise that everyone dies at exactly six hours or that adding an
  already-adult agent grants six further hours. Detailed stage effects and
  variation in age at death remain open; pacing must be playtested.
- **Death and inheritance:** by default, a deceased agent's personal belongings
  pass to their household. Computment wants the deceased agent's own model to
  be explicitly told that the agent has died and then produce a final will or
  inheritance instruction **after death**. This is part of the intended first
  complete game, not a post-launch feature. It is a final estate decision, not
  the dead agent resuming ordinary physical actions. A valid will or later
  established inheritance rule can change the default household distribution.
- Deceased agents remain **inspectable to the player**, including through the
  interactive family tree. Their agent popup becomes a historical profile with
  their saved thoughts and memories, age/circumstances of death, and final will
  when available. It does not generate ongoing new thoughts or offer live
  provider/model controls; the exceptional post-death will turn above is not
  ordinary continued life. **Memories do not transfer to descendants at death.**
  Preserving them for player inspection does not make them known to living
  agents. A child learns only what they were told, taught, read, witnessed, or
  later discovered. For example, a hidden tool's location remains unknown to
  the child if the parent never shared or recorded it.
- Parenthood normally requires consent and is optional when civilization is
  secure. At low population, this world has an explicit **continuity rule**:
  refusal is not fully autonomous, preventing civilization from ending solely
  because models decline reproduction. The game must communicate that rule
  honestly rather than claiming unrestricted consent. A simple population
  threshold may be the first implementation, but the finished system should
  assess **continuity risk**—eligible unrelated adults, family lines, children,
  expected deaths, and care/resources—not just count heads.

### Decided and open after the survival and town-layout playtest

The player chooses a **rough site for the first Town** in New World, with the
best suitable areas shown. Town-generation rules place its starting buildings
and Roads; the player can accept that layout or remove it and redo it. The **only
guaranteed starter buildings are **two Houses, a Warehouse, a Farmhouse, and a
Blacksmith**, connected by generated Roads. The two starting households can
claim the Farmhouse and Blacksmith so each has a productive role. How those
initial claims are offered/assigned is open. Clothing-making place and Workshop
are not guaranteed starters; agents may develop them later.
The player chooses starter supplies during New World setup, but **food portions
in the Houses, at least one usable wooden axe and one usable wooden pickaxe**
are guaranteed so the first Town can begin. Exact counts beyond these minima,
other items, suitability scoring, placement controls, what
a redo preserves, available supply choices, and whether any nonguaranteed
building can appear at start remain open. The
[finished-game asset roster](finished-game-asset-roster.md) records the accepted
base tool/item/food/object/art set and explicitly marked remaining choices;
the [current-state report](current-state.md) describes the playable prototype.

### Open

Expected/variable lifespan below the six-hour cap, starting agents' ages,
the exact UI for assigning the first four agents to the two starting
households, the detailed limits on child tasks and elder capabilities, whether
older childhood needs a separate phase, age display, relationship and
inheritance mechanics, continuity threshold and exit conditions,
pregnancy/birth and childcare rules, care/resource eligibility, Jev's
optional role in childhood, and the identity implications of player renaming.
First-Town placement rules and interface remain open: its order relative to
agent placement, physically valid terrain and footprints, how fertile land
is ensured or assessed, the placement boundary's exact distance measurement
near tile/footprint edges, whether geometric proximity also requires a walkable
route, and how redo affects assigned starting households and configuration.
Warehouse details remain open: formal ownership, how Town residency is
determined, access when the building is outside all Town borders or borders
change, and how its hard access gate relates to any future crime system. The
optional survival grace period still
needs a duration and precise effects. Also open:
whether unrelated newcomers can arrive without player action or are only
introduced through Add Agent. A configurable automatic-birth limit was
considered, briefly accepted, then explicitly
reopened: computment will decide **after playtesting actual birth frequency,
population growth, and model cost** whether a cap belongs in the game. Do not
assume a cap or its precedence over the continuity safeguard before that
decision.
Computment briefly considered removing the close-relative pairing ban, then
retracted that thought; the ban still stands. A model-usage meter and optional
AI-usage limit have since been accepted; neither is a population cap.

For inheritance, still open: the exact final-model-turn contract; which assets
are personal versus already household-owned; conflicts between a will and
agent-made law; minors, multiple heirs, debts, and no-household cases; and
whether a final message beyond the will is part of the death event. Clanker's
proposed safety rule is to freeze the estate at death, validate the model's
instructions against real ownership/law, and apply the household default if
the model is unavailable or gives no valid instruction. Computment said this
flow feels right; treat it as accepted direction, while timeouts, conflicts,
crash recovery, and exact transfer rules still need design.

### Accepted age-up chart for playtesting

Using the current six-real-minute day and 60-world-day maximum lifespan:

| Stage | World age | Unpaused time since birth | Own LLM? |
|---|---:|---:|---|
| Infant | day 0 to before day 3 | 0–18 minutes | No |
| Child | day 3 to before day 15 | 18–90 minutes | Yes, age-appropriate options |
| Adult | day 15 to before day 45 | 1½–4½ hours | Yes |
| Elder | day 45 to before day 60 | 4½–6 hours | Yes |

An agent may die earlier; these are stage thresholds, not scheduled death
times, and the thresholds can change after playtesting. An adult added to the
world starts at a nonzero age. Because adulthood precedes the first 40-day
birthday, Clanker recommends displaying age in world days plus life stage,
not only whole years; this UI choice is not yet settled. A separate adolescent
stage and exact capabilities are still open, but are not assumed to require
another sprite family at first.

## Social life and agent cognition

### Decided or accepted for playtesting

- Early conversation is face-to-face and proximity-bound; agents may later
  invent long-distance communication. Agents should chat **regularly in
  context** without socializing to the exclusion of work and life.
- Personal models generate meaningful dialogue and decisions about trade,
  buying/selling, organizing, land, invention, relationships, and other goals.
  Agents can lie, misunderstand, gossip, keep secrets, and have differing
  knowledge. **Private thoughts and spoken dialogue are distinct:** another
  agent learns something only if it is said, observed, or otherwise conveyed
  in-world; inspecting a thought as the player does not broadcast it to anyone.
- A conversation is a real joint activity with a reason, turns, and a chance to
  conclude, disagree, withdraw, or postpone. Ordinary job scheduling should
  not cut it off mid-sentence. Danger or urgent needs may interrupt; a bounded
  final wrap-up round can avoid endless looping. If unresolved, say so rather
  than fabricate agreement, and allow later resumption. The marriage-surname
  conversation above is a special case: it must be started even across the
  world, must be bounded, and a marriage cannot complete with its surname
  undecided. Loop protection is accepted as a design direction, **not yet a
  specified or implemented turn limit**.
- Socializing agents display a chat bubble above their sprites. Clicking opens
  a nearby popup with a summary; expanding reveals the complete conversation.
- Jev may notice social moments, retrieve memories, summarize, or route cheap
  routine decisions while each agent retains its personal LLM. Jev is optional
  per world, not individually toggled per agent. The player can **turn Jev on
  or off in an existing world**; the choice is not locked at world creation.
  Disabling it must not erase existing agent memories or make the world depend
  on Jev to continue functioning.
- Each agent remembers what they experienced or were told, not everything the
  player can see. Minor details may fade or be misremembered; major life
  events, relationships, learned skills, and unresolved commitments should
  remain dependable. An agent's belief can be wrong without changing the
  simulation's record of what actually happened. Neither family relation nor
  another agent's death grants access to that person's private memories.
- **Preferred memory architecture:** when enabled, Jev can help compact many
  experiences into shorter memories and retrieve relevant ones for that
  agent's next decision. It must preserve who witnessed or said something,
  distinguish firsthand events from rumors and uncertain beliefs, and never
  expose one agent's private memories to another. Jev does not replace the
  agent's personal model or become mandatory for memory to work. With Jev off,
  the game still needs a functioning memory/retrieval path.
- Fully generated languages/dialects are **deferred**, not planned now, due to
  uncertain gameplay value and token cost.

### Open

Conversation frequency and token/turn budgets, group-planning mechanics,
interrupt/resume behavior, memory importance/retention rules, compaction
triggers and fallback method without Jev, and Jev's exact role
need implementation and playtesting. The proposed lifecycle is accepted as a
direction, not proof that it will feel right in the finished game.
The September 29 external code review raised additional **questions, not
decisions**: whether personality is fixed at creation or changes through
experience; whether a model sees numeric needs or descriptive bands; how
routine and dialogue budgets are split; whether each participant's own model
speaks its own conversation turns; and whether spoken promises become tracked
commitments whose fulfilment or breach can be observed. Resolve these against
the accepted per-agent identity, truth/provenance and spending principles
before specifying a conversation-call protocol. The review's proposed schemas
and token settings are implementation options, not finished-game canon.

## Survival and exploration

### Decided finished-game direction

- **Remove energy and sleeping as mechanics entirely.** No energy meter,
  routine energy drain, sleep action, bed-based recovery loop, or energy gate
  on projects and social/exploration actions. This is a change to the intended
  game; the repository runtime now implements this removal, though the live
  installed host is unchanged until a separate deployment.
- **Food still matters; severe weather still matters.** Survival should not
  consume nearly every decision. Agents choose priorities based on needs rather
  than a universal rigid hierarchy. Better meals, farming, trade, and food
  businesses should make nourishment part of civilization's growth, not only
  a repetitive emergency. Food belongs in household Houses, not the Town's
  resource Warehouse.
- A severe weather event lasts **no more than three-quarters of a game day**.
  An agent may shelter from storms in their own household's House, or use
  natural cover such as a forest or tree when away from home. Whether an
  invited guest can use another household's House as storm refuge is open.
  Weather may sometimes cause illness; food and care support recovery. Illness
  slows work and travel, not personality or normal conversation; staying home
  speeds recovery but confinement is not required. Exact penalties, care and
  recovery rates remain open.
- Exploration should have real motives: locating resources, terrain and other
  Towns, with occasional curiosity also possible. Agents may travel as far as
  they choose. Knowledge of what they discover can become maps, records and
  books that agents can trade; individual agents do not automatically know
  the player's fully visible map.

### Current prototype evidence (not finished-game intent)

There is no separate numeric **safety** need in the playable runtime. Agents
now have hunger, warmth/exposure and illness, weather-protective shelter,
weather responses, and a `safe_idle` fallback. Energy and sleep are removed.
Hunger and urgent exposure can pause projects or restrict available adult actions.
Generated-world weather is currently chosen per **32×32-tile region per world
day**. A storm roll now lasts at most three-quarters of that saved world's day,
then becomes ordinary rain for the remaining quarter. The regional weather
transition model and household House refuge are not finished. Agents can now
seek nearby forest or a standing tree during a storm and gain bounded
protection there; the exact storm-seeking balance needs playtesting.
The current candidate list includes a bounded local curiosity outing. It now
records visited terrain/resource sites in the explorer's own ledger, creates a
bounded physical map/record artifact on return, and can share or trade that
artifact without teaching unrelated agents. It does not yet seek distant
resources or Towns purposefully.

The illness prototype now uses a bounded four-tick work schedule: healthy
agents retain full project speed, while illness bands permit 75%, 50%, or 25%
of ordinary work opportunities; travel gains one extra cooldown tick from 25%
through 74% illness and two at 75% or higher. Neither ordinary decisions nor
conversation are gated by illness. Warmth above 60% and hunger above 35% allow
12 basis points of recovery per tick, doubled near any shelter as a temporary
home proxy; a fresh meal relieves 100 points, and completed infant or
accepted-dependent caregiving relieves 250 while adding warmth. Exact finished-game
rates and treatment rules remain open. An internal accepted-content catalog
records distinct Bandage, Medicine and Clinic identifiers (Clinic footprints
1×1 and 1×2) only; these are not active inventory/building content. No
recipes, costs or treatment effects are specified.

### Open after the September playtest

Computment finds that agents spend too much time seeking food, rest and warmth
or trying to feel safe, and wants more room for exploration, social life,
building and invention. Define food scarcity and routine upkeep so a daily
food economy matters without monopolizing action selection. Decide what an
agent without any House does during a storm, how much natural cover protects,
and which illness penalties and care/recovery rates work well without
recreating an energy meter. Exploration, knowledge recording and trade need
actual actions and information boundaries rather than an idle-label change.

## Buildings, land, Towns, and animals

### Decided

- Each building **type has a few supported shapes of its own**, rather than
  choosing freely from every shape/material/floor/state combination. There is
  initially **one standard appearance per supported design**; duplicate houses
  of the same type and size may look alike. The decided footprints are listed
  below and in the [accepted asset roster](finished-game-asset-roster.md). Agents
  may invent new designs, including unusual shapes if the art and world rules
  can represent them; larger, irregular, or multi-floor buildings are therefore
  possibilities, not automatic unlocks. **House** replaces Shelter as the
  residential building. It has household-exclusive storage/inventory, usable
  by household members while physically inside; food is kept here. It provides
  cooking, storm refuge, childcare and household property functions. Agents
  outside the household may enter **when invited**; invitation does not grant
  access to the household's private inventory. **There is no fixed occupant
  limit for a House**, including invited visitors; prior capacity and crowding
  ideas are superseded. It has no bed or sleep-recovery role. A selected
  building exposes inspectable occupants, stock and ownership in a panel;
  there are **no visible/enterable room interiors**.
- **Storage-driven building expansion:** a House starts at **1×1** and can
  expand to **1×2** or **2×2** when its household needs more storage.
  This increases storage, not its unlimited occupant count. A Warehouse starts
  at **2×2** and can expand to **2×3** for more storage. A Store may be **1×1**
  or **1×2**, likewise tied to storage. Exact capacity per footprint, costs,
  expansion triggers and other building footprints belong in the full content
  catalogue; do not invent those values yet.
- Non-residential buildings have distinct physical occupancy, workstation,
  storage, and other type-specific limits. Agents can reserve space when
  practical, queue or choose alternatives when full, and retain the blocked
  goal for later retry. The simulation owns that memory; Jev can help choose an
  alternative but is not responsible for remembering the task.
- Building sites should come from understandable legal options considering
  access, terrain, resources, ownership, and Town context. Land can first
  be claimed, shared, granted, or disputed. Monetary land values and purchase
  prices become meaningful after currencies exist. Agents can later buy/sell
  property. Roads help travel and influence site choice.
- The world system must validate hard physical building constraints such as
  terrain, footprint and overlap with existing objects/resources/buildings.
  Access, ownership/claims and Town context also matter, without turning
  every agent-created law into an unbreakable physical rule. Agents request a
  building; Town planning ranks legal sites; agents accept or reject rather
  than choosing tiles.
- **Housing priority for a newly added adult (decided):** when the adult has no
  home, seek suitable existing household housing first; start a new House
  project only if none is suitable. How they join an existing household and
  gain its permission, or form a new household, remains open. There is no
  occupancy-capacity gate.
- **Town(s)** replaces “settlement” in player-facing terminology. There are
  no village or city place classes: every such place is a Town. The first Town
  already exists during paused New World setup, and its four starting agents
  are Town residents with access to its Warehouse. Towns have generated,
  inspectable borders with room to grow. The border follows the
  Town's assigned buildings, includes spare space around them, and expands
  when new buildings join that Town; exact margin, connected geometry,
  assignment and overlap handling remain open.
- **Warehouse** replaces Storehouse. It holds actual inspectable resource stock
  at its location for agents/households resident in its Town, within that
  Town's borders. Food belongs at home instead. Residency and border-change
  access cases remain open.
- **Workshop** remains for agent inventions/mods and is usable by outsiders.
  Its footprint is **2×2**. Its mechanic/code is a **late-development phase**:
  first establish a functioning simulation and broader base asset set, then
  build the invention/mod loop on that foundation. This is staging, not removal
  from the intended finished game. Remove separate Cooking fire/Campfire and
  Stone hearth buildings; House handles cooking. Remove Weaving frame and
  bed/bedroll content. A dedicated **Tailor Shop** makes clothing at **1×1 or
  2×2**; its exact production chain remains open.
- **Roads and bridges:** the world system generates infrastructure, not the
  player or individual agent. The full agreed road rule is below; the starter
  Path becomes a Road in intended content.
- **Livestock and mounts** belong in the finished game. Hostile predators do
  **not** belong in the current plan, though they may be revisited later.
  The accepted base roster is **chickens (eggs), sheep (wool), cows (milk), and
  horses (mounts)**. Leather/wool and milk/egg products are late-development
  items alongside animal husbandry. Their detailed mechanics are deliberately
  deferred to late development; slaughter/hunting is not assumed.

### Accepted finished-game base roster and building sizes

Computment accepted all unmentioned entries of the initial
[asset-roster review](finished-game-asset-roster.md) and amended specific rows
as recorded here. The roster's numbered entries are the detailed accepted
**base content**; open recipes, costs, capacities, animation budgets and
invented content are not silently approved. In particular:

- Terrain uses roughly two subtle textures per surface, with most little
  grass/stone/leaf details baked into texture variation. A tile has at most
  one tree. Iron, gold and diamond deposits are visibly distinct
  **ore-bearing outcrops**, not ground veins.
- The farm roster includes **universal grain** (no named grain species),
  **potatoes**, a cultivated leafy green distinct from wild greens that
  satisfies more hunger, and orchard fruit. Flour is a sellable Farmhouse
  intermediate, including at a Market; cloth is likewise a real intermediate
  item.
- The Blacksmith refines **iron ore into a separate metal item**, then uses it to
  make tools. Selling spare refined metal directly from the Blacksmith is
  a strong proposed extension awaiting final confirmation. The Blacksmith
  already sells tools directly and takes tool-making requests—no separate
  Store is required for its own products.
- The accepted medical goods include **bandages and medicine**. Exact
  ingredients, healing effects, care actions and supply chain are open.
- Farmhouse **1×1 or 1×2**; adjacent private Silo **1×1**; Blacksmith
  **1×2 or 2×2**; Tailor Shop **1×1 or 2×2**; Workshop **2×2**;
  Restaurant **1×2 or 2×2**; Clinic/healer's shop **1×1 or 1×2**.
  Market's main building is **2×2** with separate **1×1 stalls** and an
  approximately **10×12 clear stall-reservation area**. Town Hall is
  **3×4**. These are building/plot footprints, not interior rooms.
- Port is **2×4**, rotatable to all four cardinal directions. One tile of its
  four-tile length rests on land; three extend over water. Keep clear docking
  space along both long sides of that three-tile water section. Exact placement
  clearance and boat docking/queue rules remain open.
- Combat gear includes a spear, sword, shield and armor alongside the
  dual-purpose axe. Clothing does not change an agent's map appearance; the
  visual treatment of worn armor remains open.
  Maps, written records and books are accepted knowledge goods; law, claims,
  ownership and wills are mainly inspectable **data/gameplay**, not a demand
  for separate physical document objects.

The accepted roster includes proposed-but-now-chosen families such as clay,
pottery, rope, orchard fruit and carry aids. Their exact gameplay value and
recipe are still to be worked out, not grounds to relabel them unaccepted.

### Decided incremental Town layout; open ranking and retry details

Agents identify a building need and do the work. The Town layout system offers
**several ranked viable sites** rather than letting an agent search every tile.
The agent can accept or reject; after rejection, they explain what was wrong
and the layout system re-ranks sites. The retry/stop limit and what happens if
no acceptable legal site exists remain open. Site ranking should account for
terrain, resources, existing buildings, ownership, access, other Towns, room
for growth and building purpose. Town appearance/layout should vary by
Town/culture. Exact weights and when another offer appears are open.

The intended building roles now include House, Warehouse, Workshop,
Farmhouse, farm fields, an adjacent private farm Silo, household-run Store,
household-run Blacksmith with internal work stock and direct sales, Tailor Shop,
public Market and stalls, Town Hall, Port, Clinic, and optional agent-founded
Restaurant. A
Farmhouse processes crops; its owning household places fertile fields, plants
seeds, tends, harvests, and sells/trades the produce. Farm count responds to
**Town population and farm yields**; a shortage or reduced yield can justify
more farming rather than a hard cap blocking recovery. The exact formula is
open. Farm work stock
is private to its household; its Silo is distinct from the public Town
Warehouse. The Blacksmith makes and sells tools on site and can accept
specific tool-making requests, with stock inside the building. A Store
supports a household selling its products. Goods must be physically carried
to the Store and kept in its own stock before sale; a Store cannot sell from
a remote House, farm, or Warehouse inventory. A Market admits traders from any
Town; a Restaurant buys ingredients, cooks and sells potentially better meals.
Town Hall supports governance and Port supports boats. Exact stocks, recipes,
ownership edge cases, prices and trade remain open. See the
[accepted asset roster](finished-game-asset-roster.md) for specific content and
[current state](current-state.md) for what exists in the prototype.

Wood, stone, iron, gold, diamond and many other crafting resources are wanted.
Better tools should gate harvesting/mining more advanced resources, creating
a progression incentive. The agreed initial ladder is **wood tools → stone →
stone tools → iron → iron tools → rarer materials**. Gold, diamond and other
materials belong in the wider catalogue; their exact mining/crafting tiers
remain open. Wood and stone should both be useful for House construction;
specific costs are open.

**Clanker's still-open implementation proposal:** use one incremental layout
service for starter arrangement and later construction requests. Filter
impossible footprints, rank sites by purpose/access/terrain/resources/Town
shape/future growth, and generate a connected Road layout with each accepted
building. The generator should not preselect a fixed lifetime building count.

### Decided Road scope; formation details open

The baseline has **one Road type**, with no extra categories required yet.
Roads appear immediately with Town buildings and automatically link Towns
**when a legal land route exists**. If geography blocks a legal route, the
Towns remain unconnected by Road; boats or later transport may still connect
their travelers. Roads are not generated by footsteps or a separate building
project. Agents and player do not paint Road tiles. Roads speed up travel.
Roads connect to adjacent building entrances and must not occupy a building's
footprint. The player's September 29 sketch indicates a connected spine with
short branches as a useful layout direction, not a mandatory fixed street map.
**Bridge placement responds to traffic** at narrow river crossings, even
without a planned Road. A Road generation pass also builds a bridge immediately
if its legal route encounters a bridgeable river. Either case excludes a
redundant nearby bridge over the same crossing/river, not a necessary bridge
over a different nearby stream.
Generated Roads **remain after the supporting building is removed or a Town
is abandoned**. Whether any
other event can remove a Road—and thus whether the earlier temporary versus
permanent distinction still means anything—remains open. Exact inter-Town
route timing, layout, bridge thresholds/radius/materials and rendering remain
open. Diagonal travel/Roads remain in scope; diagonal moves must not pass
  through blocked corners. Playable foot movement now uses the strict
  two-clear-shoulder rule and a 141% diagonal route cost; diagonal Road
  construction/visuals remain open.

### Open

Further structure effects; exact configurations and unchosen footprints;
building
inspection fields, invitation and non-residential access/reservations/queues;
land claims and disputes; Town borders and governance; currency/land pricing;
transport progression; Road permanence and bridges, other terrain eligibility,
travel effects and junction/diagonal visuals; advanced resource/tool tiers,
farming workflow and farm-cap formula, private versus public stock, business
economics; and later livestock/wildlife detail.

## Town laws and governance

### Decided direction

- Agents may establish Town laws and later change them. These are
  **in-world social rules**, not unbreakable physics. An agent can violate a
  rule—for example, cut a tree in a protected grove—and the world can record
  the act for discovery, dispute, and consequences. The game should not simply
  refuse every illegal action, because crime and enforcement belong to the
  simulation.
- The authoritative simulation still protects fixed world facts and action
  rules: agents cannot create goods, erase physical constraints, or silently
  rewrite formal ownership by declaring a new law. Unlawful use or occupation
  can be represented as an action/dispute without automatically changing the
  underlying ownership record. Laws may guide or contest transfers through
  validated mechanisms, but cannot bypass those mechanisms.
- A newly founded Town starts with a **simple council** rather than a
  single starting agent automatically ruling everyone. **Every adult resident** sits
  on this initial council and can propose laws and vote. Towns may later
  change their governing arrangement through in-world decisions; the council
  is the starting form, not a universal permanent government.

### Preferred starting election trigger

Once a Town reaches **eight adult residents**—double the four-agent
starting population—it begins electing a smaller representative council
instead of keeping every adult as a council member. Count adults in that
Town, not across the world. Eight is an initial threshold to playtest,
not a claim that every Town must forever follow this exact rule. All
adult residents should retain a vote in those elections.

### Open

What qualifies an adult as a Town resident, elected council size,
election timing/terms, vote threshold/quorum, proposal/repeal procedure, and
how governments may change; which laws apply to whom and where; how a
violation is witnessed, investigated, enforced, or punished; claims and land
disputes;
taxes, inheritance, and interaction between conflicting Towns. Agents
should know only laws or violations they have learned about in-world.

## Combat

### Decided

- Interpersonal combat belongs in the finished game for **self-defense, crime,
  personal feuds, and organized war**. Hunting and sport/duels were not chosen
  in that discussion.
- Combat can turn lethal, but agents should not be dying constantly. Personal
  models choose intentions; authoritative simulation resolves reach, movement,
  timing, protection, injury, and outcomes. Health already exists in the
  prototype, but the integrated game **does not yet have a combat loop,
  weapons, armor, combat injuries, or combat AI**.

### Preferred, not finalized

Weapons and armor should fit the general item/equipment/crafting/invention
system rather than be a disconnected special system. An axe could be both a
work tool and a weapon; armor could be wearable protective equipment. Combat
still needs its own actions and balancing. Computment's agreement here was
qualified (“if I get what you mean”).

### Open

Injuries, medicine, escape/surrender, law enforcement, war declarations and
peace, lethality balance, combat equipment progression, and whether animal
slaughter or hunting ever becomes part of the game.

## Inventions, mods, and technology

### Decided

The Workshop and broader invention/mod mechanics are **late-development work**:
first make the base simulation functional and its asset set fuller. They remain
part of the intended finished game, including imported mods; this is a build
sequence, not a removal or automatic post-launch deferral. Art generation for
new inventions, fallback representation, and prompted armor visuals will be
designed in that phase rather than assumed solved by the base roster.

- Agents can eventually create buildings, tools, crops, machines, art, laws,
  currencies, and other economic/cultural systems. They cannot invent or alter
  natural biomes. Needs, curiosity, aspirations, social standing, requests,
  and expertise may motivate invention. Agents may use, teach, hide, sell,
  improve, or compete with creations.
- An invention's novel behavior does not automatically imply a limitless new
  shape/material/animation combination. Its art must fit a supported visual
  configuration or pass a new-design path. Most inventions should combine
  familiar world actions—such as storing, crafting, cooking, transporting, or
  protecting—in new ways. Truly new classes of behavior are possible but rarer
  and require extra game-system support. The exact boundary remains open.
- The design direction combines **declarative content** with **sandboxed
  executable scripts**. Valid inventions can activate in the running world at
  a safe boundary. Agent-generated code must not run as arbitrary trusted code
  in the main game process, access credentials/files/network freely, or change
  protected physical and ownership laws. A test/validation stage, runtime
  limits, quarantine/rollback, failure feedback, and cooldown are part of the
  accepted direction.
- An **invention** originates in a particular world's fiction. A **mod** is a
  portable technical package. Inventions and active mods belong to their
  world save; the player may explicitly export an invention into a personal
  library and import it into another world. Nothing transfers silently.
- Human-authored or community-made mods can also be **explicitly imported** in
  the first complete game. They use the same package validation, content
  permissions, and restricted script sandbox as agent-made creations; there
  is no separate trusted-code shortcut for downloaded mods. A package can
  declare content and assets and, where supported, bounded scripted behavior.
  Import is a deliberate player action into a chosen world, not an automatic
  download or silent global installation. The exact file format and package
  user interface remain open.
- Both the Pause Menu and Main Menu have a **Mod Library** surface. The in-world
  Pause Menu entry sits immediately above its bottom **Quit to Menu** action;
  the Pause Menu does not offer Quit Game. The in-world view shows
  active/developing/failed creations, authors, and dependencies. The
  global view browses the latest save for each world and handles the personal
  library and imports/exports. Tabs such as This World, Personal Library,
  Import/Export, and History are accepted as an initial organization.
- The player wants **discovered capabilities inspectable** through World Info.
  This must not falsely imply that every agent knows every discovery.

### Preferred, not finalized

Use a **capability/prerequisite graph**, not a rigid visible technology tree or
unrestricted “invent a car out of sticks” system. Computment likes this
recommendation, but did not explicitly answer the separate final yes/no
question. The graph should show discoveries, not spoil a fixed future list.

### Open

Exact sandbox/runtime and APIs; immutable world-law boundary; validation and
activation/rollback semantics; art generation/cost; model invention prompts;
materials/prerequisite logic; who knows which capabilities; package
compatibility/dependencies/rights; external-mod import UX; and how the
library handles revisions and conflicting imports.

## Saves and persistence

### Decided

- Every world has its own save state. **Autosave is on by default**, initially
  every **5 minutes**, with **5 rotating autosaves** by default. Settings offer
  autosave off/on, intervals of 1, 2, 5, 10, 15, or 30 minutes, and rotation
  off/3/5/10. These are initial choices subject to performance playtesting;
  their proposed home is the current world's **World Settings**.
- Named manual saves are unlimited. Emergency recovery is automatic in the
  background. Saving on Quit to Menu/Game is the accepted direction, while
  quit confirmation remains.
- The player must be able to **intentionally overwrite an existing named
  manual save** rather than accumulating a new checkpoint every time. In Save
  World, select an existing checkpoint and confirm overwriting that specific
  checkpoint; creating a new save remains a separate action. Typing a matching
  name alone must not replace an existing save. Recovery/backup behavior after
  overwrite remains open.
- Load World should show an existing save's assessed compatibility and warn
  before selection when it cannot load. The assessment must use actual
  save/version and required-content checks.
  **Block loading only when an actual compatibility check establishes that
  the save cannot be loaded**; a version difference or old timestamp alone is
  not evidence of incompatibility. Preserve a blocked save rather than
  deleting or overwriting it. Development playtest saves may legitimately
  become incompatible as formats change; migration support is a later design
  goal, not a current requirement. How to present an unknown assessment and
  the exact compatibility contract remain open.
- World-created inventions and active mods travel with that world's save;
  provider credentials and graphical/device settings are global. The global
  library reads the latest save for each world rather than silently merging
  their creations.

### Open

Whether manual saves are checkpoints or divergent branches; restore/rewind
behavior; disk-space warnings and retention; exactly when autosave occurs
relative to model/conversation work; crash-recovery guarantees; and save
compatibility across game/mod versions.

**Development playtest policy (decided):** computment does not require old
playtest worlds to remain loadable as the New World flow and save format change;
they expect to create fresh worlds to test new features. This is not permission
to delete or overwrite an active save and does not settle finished-release
compatibility policy.

## Design tensions and next architecture decisions

These are **open questions**, not changes to the decisions above:

1. **Population continuity and ancestry.** Four unrelated starting agents in two
   households, a close-kin pairing ban, and a low-population safeguard may
   eventually leave no eligible unrelated adults even if everyone wants
   children. Computment now prefers player-controlled addition of unrelated
   adults, with placement seeding household/Town membership. Decide
   explicitly whether those additions are the **only** source of unrelated
   newcomers or whether autonomous arrivals also exist; define exactly how
   close is too close. Whether automatic births need a configurable limit is
   deferred until measured playtesting, rather than part of the current design.
2. **Law-making and enforcement details.** The core distinction is settled:
   agent-created laws can be broken, while the simulation protects physical
   facts and validated ownership changes. Define how laws are adopted,
   discovered, enforced, and disputed—including conflicting inheritance rules
   and illegal occupation—without granting models authority over engine facts.
3. **Pending model work versus continuous simulation/saves.** One slow model
   must not freeze the world, but conversations, inventions, and post-death
   wills can remain unresolved while ticks and autosaves continue. Quit now
   has an accepted cancel/discard-and-reconsider rule; still define durable
   pending states, autosave/manual-pause behavior, deadlines/fallbacks, and
   idempotent completion so crashes cannot duplicate or lose estate transfers
   or other important actions.
4. **No fictional population cap versus finite provider cost.** Each agent's
   personal model is central, including child agents; frequent socializing
   and unlimited player-added adults increase call volume. Measure calls,
   tokens, latency, and cost in representative play before choosing decision
   cadence, budgets, and how resource pressure is shown to the player. This is
   a scalability constraint, not permission to replace agents with Jev.
5. **Local packaging and VPS parity.** The finished distribution target is
   player-local and Windows-only at first, while development stays VPS-backed.
   Prove the same simulation and save behavior in both; choose the local
   process/installer shape without creating a second, divergent game.
6. **Marriage-surname procedure.** Unmarried couples may have children, and
   the parents choose one of their surnames for the child. The continuity rule
   may encourage marriage but cannot require it for birth. Still decide the
   bounded marriage-surname conversation's tie-break method, turn limit, and
   provider-failure behavior.
7. **Starter economy and tool bootstrap.** The first Town guarantees two
   Houses, a Warehouse, Farmhouse and Blacksmith, plus food at the Houses and
   at least one usable wooden axe and wooden pickaxe. Decide the remaining
   starter quantities/items, initial business-claim flow,
   and how first-tier tools are made when the Blacksmith is unavailable.
   More farms may answer food shortages when
   yields fall; the population-and-yield planning rule remains open.
8. **Automatic infrastructure details.** Towns connect by Road where a legal
   land route exists and can remain disconnected otherwise. Bridge spacing
   prevents redundant crossings on the same river but does not block a needed
   bridge on a separate nearby stream. A generated Road may bridge a legal
   crossing immediately. Decide the actual spacing/traffic
   thresholds, whether Roads or bridges consume materials, and whether
   anything besides explicit world edits can remove a Road. Roads remain after
   buildings or Towns disappear, so a separate temporary/permanent distinction
   may be unnecessary.
9. **Home invitations and later membership.** Add Agent placement on household
   land forcibly assigns starting membership, without a consent step. Guests
   may enter by invitation but cannot use private inventory. Decide guest
   cooking/storm access and how agents later leave or change households.
10. **Physical stocks and trade.** Store goods must be transported there and
    stored on site before sale. Decide transport and ownership-transfer details
    for Stores, Restaurants and Markets; location-specific inventories cannot
    be an invisible shared pool.
11. **Building storage catalogue.** Houses start at 1×1 and may expand to
    1×2 or 2×2, Warehouses 2×2 → 2×3, and Stores may be 1×1 or 1×2. These
    choices raise storage, not House occupancy. The other chosen building
    footprints are in the accepted roster above. Decide capacities, costs,
    expansion triggers, Market reservation behavior and Port clearance details.
12. **Night and weather details.** Night affects temperature and weather, with
    no sleep/energy gate or independent night-only restrictions. Decide exact
    temperature and weather effects alongside the day/night split.

Other substantive open topics: normal-session/player-intervention boundaries;
memory and knowledge across generations; Town government, economy,
medicine and injuries; art reference/style and autonomous invention assets;
save branches and mod compatibility. Resolve the finished-game experience
before choosing an implementation sequence or narrowing it to the current
prototype.
