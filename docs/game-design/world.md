---
title: The world, time and survival
type: game-design
status: active
updated: 2026-10-08
---

# The world, time and survival

These are the intended game rules and choices. They do not describe
everything that is available in the current build. See [what works today](../what-works.md).

[Game-design guide](README.md) explains the agreement labels.

## On this page

- [Where the game runs](#where-the-game-runs)
- [World time, pausing, and slow models](#world-time-pausing-and-slow-models)
- [Maps, plants and weather](#maps-plants-and-weather)
- [Survival and exploration](#survival-and-exploration)
- [Questions linking these systems](#questions-linking-these-systems)

## Where the game runs

### Agreed

- **During development and computment's own playtesting:** keep the current
  Godot desktop client connected to the private VPS-hosted simulation. This
  allows Clanker to inspect server logs, observe the running world, debug, and
  tune behavior. There is no need to move the current development world just
  to settle the eventual distribution architecture.
- **For the first finished game distributed to other players:** provide a
  simple PC install that runs the game and its game rules on
  that player's own computer. Their saves and provider credentials belong on
  their computer; playing must not depend on access to computment's VPS or a
  mandatory hosted game account. If they choose cloud-hosted AI models, those
  providers still require an internet connection and applicable credentials;
  local game hosting does not mean all model inference is offline.
- **The first finished release supports Windows only.** Computment has a
  Windows laptop for actual desktop playtesting, but no Linux or macOS desktop
  testing setup. Linux/macOS support is not a launch obligation; interested
  contributors could help add and verify those platforms later. A Linux
  server machine does not count as testing a Linux desktop game.
  **Agreed on October 8
  ([#1275](https://github.com/compoodment/ClankerWorld/issues/1275)):** that
  first release supports **Windows 11 on 64-bit (x64) PCs** only.
- **Players bring their own model-provider API keys and pay their own provider
  charges.** The game does not include developer-funded AI usage for other
  players. Provider credentials are stored per installation, separate from
  shareable world saves; they are not bundled with the game. Opening the game
  does **not** require a key or a first-launch provider setup gate. Credentials
  are supplied or selected when assigning a provider/model to an agent.
  **Agreed on October 8
  ([#1275](https://github.com/compoodment/ClankerWorld/issues/1275)):** keys
  are stored protected by Windows for the current Windows user, and the player
  can **Check key** to see which models that key can use.
- Keep one simulation/game-rules implementation across the private-VPS and
  local-PC deployments. The player-facing Godot client should not become the
  authority merely because the host runs locally. Closing the game still
  freezes the world, without offline catch-up.
- **On a player's computer, the host is a bundled companion process.** The game
  starts it, and it listens only on the loopback address, which means only
  programs on that same computer can connect to it. It is not embedded inside
  the game process.
- **The first local package is an unsigned portable zip.** An installer and code
  signing (a publisher signature on the files) come later.
- **Private-server deployment starts with a written checklist.** A staged
  deployment command for the private server waits until the local package
  works.
- **Agreed on October 1
  ([#638](https://github.com/compoodment/ClankerWorld/issues/638)):** a new
  world starts only when every founder has a personal model and a working key.
  Opening the game, creating a world and previewing it still need no key, and
  founders do not start on built-in rules instead.

### Still to decide

Performance requirements and packaging tests are still open. They are
implementation choices to prove, not reasons to reopen the agreed player-local
distribution goal, the companion-process host or the first package.

**Parked until ClankerWorld becomes a public, versioned alpha (October 8):**
how a player's copy of the game gets updated
([#1259](https://github.com/compoodment/ClankerWorld/issues/1259)), exporting
diagnostics for a bug report
([#1260](https://github.com/compoodment/ClankerWorld/issues/1260)) and moving
a world between the private server and a player's PC
([#1262](https://github.com/compoodment/ClankerWorld/issues/1262)). The owner
is the only player for now, so these wait until then.

## World time, pausing, and slow models

### Agreed

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
- **While an agent waits for its model, agreed on October 8
  ([#1247](https://github.com/compoodment/ClankerWorld/issues/1247)):** it keeps
  doing its current task, eats or shelters if that is urgent and cares for
  nearby dependents, but starts nothing important without its model. A small
  marker on the map shows that it is waiting. The Event Log adds one line only
  when the provider actually fails, not for an ordinary delay.
- When the player closes the world/game while a model request is in flight,
  save and exit promptly rather than waiting for that answer. Cancel or discard
  the unfinished request, commit **no partial agent action**, and retain the
  unresolved decision point. On reopening, the agent re-evaluates it if still
  relevant, which may require another provider call. The provider might charge
  for both the abandoned call and its later retry; this is an accepted
  trade-off for responsive quitting and no background model work.
- **Model requests in flight, agreed on October 8
  ([#1275](https://github.com/compoodment/ClankerWorld/issues/1275)):**
  pausing cancels a request in flight the same way quitting does. Autosave
  runs on its schedule whatever requests are in flight
  ([Saving and recovery](saves.md#saving-and-recovery)). Changing an agent's
  model refuses any reply to a request sent before the change.
- The finished game offers an **optional AI-usage limit** alongside
  a visible usage meter. When the limit is reached, it pauses the world and
  asks before making further paid model calls; it does not cap the fictional
  population. The playtest UI's `Model limit`
  wording is confusing; describe it in ordinary terms as a count of paid
  model-call attempts, clarify that reaching it pauses time, and do not imply
  a precise currency or token budget.
- **How the limit counts, agreed on October 1
  ([#639](https://github.com/compoodment/ClankerWorld/issues/639)):** one
  limit covers the whole installation, every world included, because the bill
  belongs to the player's keys. It counts model-call attempts, including failed,
  retried and abandoned ones; tokens may be shown as information but are not
  limited. It never resets by itself: the player raises or clears it. One
  Event Log warning appears at 80% of the limit. The control belongs in
  **Game** settings, not World settings, and loading an older save never
  rolls the count back.
- **The current prototype's 24-real-minute day is rejected as ClankerWorld's
  finished-game pace.** It was an implementation choice, not a user-approved
  design decision.
- **Accepted starting pace for playtesting:** a custom **40-day year** with
  four **10-day seasons**, initially one six-real-minute day, one real hour per
  season, and four real hours per year. Each 10-day month is one season, so
  dates use season names by default, such as **Autumn 2, Year 1**, with numeric
  dates as a setting
  ([Interface and art](interface-and-art.md#main-menu-world-view-and-controls)).
  A six-hour maximum life from birth would then
  span at most 60 world days, 1.5 years, or six seasons. These numbers may be
  changed after playtesting; they are not final performance or pacing promises.
  This custom calendar supersedes the earlier 365-day preference.
- **Agreed on October 8
  ([#1273](https://github.com/compoodment/ClankerWorld/issues/1273)):**
  biological age follows elapsed world/calendar time, with life stages
  starting at days 3, 15 and 45 ([Agents and social
  life](agents-and-families.md#life-stages-to-try-in-playtesting)). The stage
  days can still change after playtesting.
- **Leaning toward:** night affects
  temperature and weather, without a sleep/energy requirement or a separate
  night-only travel, visibility, work or social restriction. Weather itself
  can still affect agents under the ordinary weather rules.
- **Agreed on October 1
  ([#640](https://github.com/compoodment/ClankerWorld/issues/640)):** ages show
  as life stage plus days, such as **Adult · 22 days** ([Agents and social
  life](agents-and-families.md#life-stages-to-try-in-playtesting)).
- **Night length, agreed on October 1
  ([#641](https://github.com/compoodment/ClankerWorld/issues/641)):** night
  averages **40% of a world day**: about 3 min 36 s of daylight and 2 min 24 s
  of night in a six-minute day, with short dawn and dusk fades. Night lowers
  outdoor warmth and darkens the map gently, keeping it readable. Night
  effects on weather come later, after colder nights are playtested. The
  warmth drop is provisional balance.
- **Night length follows the seasons, agreed on October 3:** computment wants
  night to be longer in winter and shorter in summer, because that is
  realistic, now rather than after a playtest. Night is shortest, **30% of the
  day**, on the first day of summer and longest, **50%**, on the first day of
  winter; it is 40% on the first days of spring and autumn and changes a
  little every day in between. Night stays centred on midnight. Longer winter
  nights also mean more hours of night chill. The 30% and 50% figures are
  provisional and can be tuned after playtesting.
- **Morning start, agreed on October 2
  ([#764](https://github.com/compoodment/ClankerWorld/issues/764)):** a new
  world starts in the morning, after the dawn fade, not at midnight, so
  founders are placed and the first day begins in daylight.

### Still to decide

Playtest the accepted starting pace and revise it if days, seasons, or agent
lives feel rushed or slow. Still open: detailed stage effects, how much
colder night is, and night's effect on weather.

Provider cost is unknown until agent call rates, token use, model choices, and
population are measured. Representative tests can measure this before the
entire game is complete; nominal calendar speed alone does not determine
model spend. The usage meter and optional stop are accepted, and how they
count is agreed above; the 80% warning level may be tuned, and its wording
still needs playtesting.

## Maps, plants and weather

### Agreed

- World-size presets are **Small, Medium, Large, Huge, Mega**. They should feel
  roughly like one Town, several Towns across a region, one major continent, two or three
  continents, and a planet respectively. The proposed logical dimensions are
  **256×128, 512×256, 1024×512, 2048×1024, 4096×2048**—initial benchmark
  targets, **not locked constants**.
- **The first local release supports Small and Medium worlds only.** Large,
  Huge and Mega stay as planned presets until the costs of saving, observing
  and overviewing a world of that size are measured. **Agreed on October 8
  ([#1276](https://github.com/compoodment/ClankerWorld/issues/1276)):** the map
  zooms out until the view spans 70% of a Small or Medium map, and the larger
  sizes share one floor of 8 pixels per tile. Both numbers are provisional, for
  playtesting; see
  [Interface and art](interface-and-art.md#main-menu-world-view-and-controls).
- Use **64×64 logical tiles per chunk** as the starting arrangement target.
  Chunks help storage, loading, and rendering without forcing an entire chunk
  into one giant texture. Rendering only what the camera sees is distinct from
  simulating what happens elsewhere. Agents, crops, and Towns do not stop
  existing or progressing outside the camera. Distant work may be event-driven
  or coarser only if outcomes remain credible.
- **East/west wrapping** is on by default and can be turned off. The world has
  real northern and southern polar regions, not north/south wrapping into a
  torus. **Latitude cooling** is also on by default, giving the world a warmer
  equator and colder poles; it can be turned off independently for unusual
  climates without changing the map's wrapping choice. **Agreed on October 8
  ([#1249](https://github.com/compoodment/ClankerWorld/issues/1249)):** the
  outer rows at the north and south map edges are always polar sea or ice that
  nobody can cross, so no agent walks into an edge it cannot see.
- Keep simple New World presets, with a compact Advanced section rather than
  sliders for every generator parameter. **Balanced** is the default climate
  mode; **Uniform** and **Dominant** remain choices. Advanced controls are
  water coverage from **20–80%**, default **50%**, plus **Low / Normal / High**
  for forest cover, mountain relief, river abundance and resource abundance,
  each defaulting to **Normal**. Resource abundance changes available physical
  resources, not knowledge or technology. These settings describe the intended
  geography, not exact forest, mountain or river tile-count promises.
- A responsive, **exact preview of the selected seed and settings** updates as
  those choices change. Creating the world uses the matching preview, never a
  different or stale map.
- **Small and Medium each have one continent**, with no continent-count
  control. Once the larger sizes are supported, their loose count choices are
  **Large 1–4 (default 1), Huge 1–6 (default 2), Mega 1–8 (default 3)**.
  A selected count is a generation target, not a promise of that many
  ocean-separated landmasses; connections and incidental islands may occur.
  These controls do not unlock currently unsupported sizes.
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
- Model **climate zone**, **elevation**, **surface**, **water layout**, **vegetation
  cover**, and **objects** separately. The generator makes plausible forests,
  grass, stone, snow, water depths, and transitions for their locations.
  Each tile holds **at most one tree**; do not depict several harvestable trees
  as one stand on a tile. Trees yield wood when harvested, leave stumps that can
  regrow, and can also be replanted from seeds when forest resources are depleted.
  **There is no natural tree spread for now:** trees do not seed themselves onto
  new tiles. New trees come only from stump regrowth and from agents planting seeds.
- **September 29 terrain direction:** sand is not a compulsory strip along
  every coast and especially not along every river. A forest-floor tile should
  visibly contain a tree or plant, mostly trees; grass-surfaced forest tiles
  should instead have scattered trees. Trees and ordinary plants should not
  grow on sand. Computment then tentatively disfavored cacti; the October 1
  item below settles this by bringing them back on desert sand only. Hills
  forming a readable base around mountain regions were preferred; the next item
  records the September 30 decision, and their exact elevation thresholds
  remain open.
- **Hills at the base of mountains (September 30):** hills are added around
  mountain regions as a **visual layer only**. For now, a hill costs the same to
  walk as grass. Mountain and peak rules, described below, are unchanged.
- **Agreed after the October 1 playtest:**
  - **Fertility is a property of the land**, not an object. Every dry land
    tile has a fertility, from its climate, rainfall and surface, and fertile
    land is common. Meadow near water is usually rich; dry scrub is poor;
    sand, rock, mountains and snow can't be farmed. An agent with a hoe tills
    chosen tiles into visible field squares, and crops grow only on tilled
    tiles, so a household's fields can be as small or as large as it makes
    them. "Fertile soil" sites go away. Exact numbers are still open.
    **Agreed on October 8
    ([#1248](https://github.com/compoodment/ClankerWorld/issues/1248)):**
    farming does not wear the soil out, but a field nobody works for a full
    season goes back to grass, which tidies abandoned land without adding
    food pressure.
  - **Cacti come back** as plant cover on desert sand only. This replaces the
    earlier tentative exclusion.
  - **Beaches are patchy:** some stretches of coast have sand and others run
    straight to grass, instead of sand along most of the shore.
  - **Grass forests are denser:** many grass-forest tiles carry a tree, though
    not every one; forest-floor tiles still always do. This needs a larger
    per-area object budget, a performance trade-off to measure.
  - **Desert sand must look like dry sand**, including where dry scrub grows
    on it, and **snow** needs visible variation and a softer edge into
    neighbouring land rather than flat white with a stark border. Hills and
    mountains, and Roads and buildings standing on hills, are to be redrawn.
- **Agreed on October 8
  ([#1275](https://github.com/compoodment/ClankerWorld/issues/1275)):** one
  orchard fruit tree species, which fruits in autumn. The orchard tree itself
  is already accepted in [Planned game content](content-list.md); its yield
  and other details
  remain open.
- Generated geography includes **rivers** as well as oceans, shores and lakes.
  Rivers belong to the 2D, top-down tile world and its water layout layer; they
  must remain continuous across an enabled east/west world seam. A noise
  function may sample three input coordinates to make a seamless **2D** map;
  that does not imply 3D graphics or 3D gameplay.
- Agents can reshape some terrain through activity. **Agreed on October 8
  ([#1275](https://github.com/compoodment/ClankerWorld/issues/1275)):** they
  change terrain only by tilling, felling trees, planting, using up deposits,
  and building Roads and bridges; anything more comes only through
  inventions. They can cross water with
  crafted boats and shore-connected ports and can later invent improvements.
  Roads exist and influence travel and building placement. **Mountain and peak
  tiles cannot hold construction**—including buildings, farms and roads.
  Agents may cross **mountain** tiles, but more slowly; **peak** tiles are
  impassable. **Agreed on October 1
  ([#628](https://github.com/compoodment/ClankerWorld/issues/628)):** crossing
  a mountain tile costs twice as much as grass.
- In the intended finished game, agents can **move diagonally** on the 2D tile
  map, and roads can also run diagonally. The playable foot route finder now
  supports diagonal steps at 141% of cardinal entry cost and requires both
  orthogonal shoulder tiles to be passable; occupied shoulders also block a
  live diagonal move. Diagonal Roads are agreed
  ([Towns](towns.md#how-roads-and-bridges-appear)) but not built yet.

### Weather over time

**Agreed on October 8
([#1275](https://github.com/compoodment/ClankerWorld/issues/1275)):** weather
comes in **regional episodes** rather than a new, independent weather roll
every day, in square regions 32 tiles across (a provisional size). A region's
next condition should depend on its climate and previous condition. The first
version has no moving fronts: it does not simulate storms physically
travelling between regions, and drifting visual effects do not mean a weather
front has moved.

For this direction, ordinary weather should last a variable **one-quarter to
one in-game day** before changing. This is a starting range to tune through
playtesting, not a fixed probability or a promise that every condition occurs
equally often. A severe episode lasts **no more than three-quarters of a game
day** and is followed by at least **half a game day without severe weather** in
that region. When an episode changes, wet neighboring regions should make rain
somewhat more likely, without forcing the same weather across the map. The
strength of that influence is still open for playtesting. **After the
October 1 playtest**, rain should come about **25% less often** than in the
first generated worlds; the exact weights stay provisional.

### River crossings and visible forests and mountains

At the default **Balanced Small and Medium** settings, with forest cover and
mountain relief both set to **Normal**, generated worlds should visibly include
forests and mountain regions rather than relying on rare seeds to reveal them.
The initial playtest targets are **20–40% forest** and **5–12% mountains**,
measured against dry land. These are targets to test and tune, not a promise
that every climate or world size has the same coverage. Uniform Dry and
polar-only choices do not receive these targets. Low and High forest/mountain
settings remain distinct choices and are not forced into the Normal bands.

Try at most **three deterministic candidates** when at least one Normal trial
target applies; otherwise use one map. Identify the chosen candidate in the
exact preview so Create World uses that same map. If none meets its eligible
targets, show the selected coverage and all candidate results, then let the
player choose another seed or explicitly accept the misses; do not silently
substitute a different map. **Agreed on October 2:** computment found the
preview's numbers hard to read, so the preview describes the map in plain
words ("Plenty of forest and some mountain ranges") and says when it has less
or more forest or mountains than a balanced world. The measured shares and
every candidate's results stay in its tooltip. Larger-size targets and
preview latency remain subject to measurement and playtesting.

An attempt with no room for the first Town is unavailable. Keep trying the
remaining candidates and show that failed attempt alongside the coverage of
playable maps, keeping their original attempt numbers. If none is playable,
ask for another seed or changed settings; do not create a replacement map
silently.

Forest and mountain areas should form readable regions. The generator uses
connected-region size only to break ties between equally good candidates; it
does not impose a minimum forest patch size. Region measurement joins diagonal
neighbors and wraps east/west only when the selected map wraps, never across
the north or south map edge.

**Agreed on October 1, 2026
([#628](https://github.com/compoodment/ClankerWorld/issues/628)):** mountains
generate as **a few large massifs** instead of many small patches, because a
mountain should read as one whole landform. Each massif has a centre, a long
axis and a size, so it reads as a range rather than a round blob. Small leftover
patches are flattened, peaks form each massif's crest, and the hill band around
it widens with the massif's size. This replaces the earlier rule that the
generator imposes no minimum mountain patch size.

Massifs leave the earlier rules in place: the agreed **5–12% mountain** target
for Balanced Normal worlds, and hills at today's grass walking cost. The number
of massifs (for example 1–2 on Small and 2–4 on Medium), their minimum size and
how far hills reach stay provisional until computment reviews generated maps.
**Agreed on October 2
([#683](https://github.com/compoodment/ClankerWorld/issues/683)):** the start
of the first Town always has stone it can walk to within 32 tiles, because
about one start in five had none. The distance is provisional.

Computment wants agents to cross **one-tile-wide rivers on foot, more slowly**
than dry ground. **Agreed on October 1
([#649](https://github.com/compoodment/ClankerWorld/issues/649)):** agents may
also wade rivers **two tiles wide**, more slowly than a one-tile river, so every
river the game can bridge can also be crossed on foot. **Agreed on October 8
([#1288](https://github.com/compoodment/ClankerWorld/issues/1288)):** agents
may also **swim** rivers wider than two tiles and lakes, much more slowly than
wading, losing warmth and only with a light load. The sea still needs a boat.
Nobody drowns: an agent that is too cold, ill or loaded does not start a swim.
Swimming speed, warmth loss and the load limit are provisional, for
playtesting. The world automatically adds bridges at sufficiently
used crossings, including two-tile ones. A generated Road may also **create a
bridge immediately** where its route meets a bridgeable river; it need not
wait for traffic there. Once a bridge is placed, no redundant bridge is added
over the **same crossing/river**.
Spacing compares the actual connected banks, with no fixed radius, so a needed
bridge over a separate nearby stream is never blocked. A river is bridgeable up
to two tiles wide; wider water is not bridged, and bridges cost no materials.
How fast agents wade a two-tile river is provisional balance. The
initial traffic threshold and permanent Road/bridge rule are recorded in
[Towns](towns.md#how-roads-and-bridges-appear). Town site planning and Road
generation must be designed together.

### First boat and Port travel

The first crafted small boat carries **one agent and the goods that agent is
carrying**. It cannot move stock remotely. A journey needs a completed,
reachable Port at **both** ends and a navigable water route between them; an
agent cannot embark from arbitrary shore. Ports use the agreed 2×4 land/water
footprint and clear docking space described in [Towns](towns.md). Validate a
land approach and docking clearance before construction and departure.

Boats belong to a **Town**, not a household. Town residents may use its communal
boats; visitors need permission. Reserve each physical boat for only one
journey at a time, and persist the boat, traveler and carried goods together
across save/load. A blocked destination cannot teleport or duplicate any of
them.

**Agreed on October 1, 2026, clarified on October 4
([#919](https://github.com/compoodment/ClankerWorld/issues/919)):** if the
destination Port becomes blocked during a journey, wait one game day, then
return to the departure Port when it is usable. Otherwise the boat and traveler
wait safely, keeping their carried goods together until a safe return or
arrival becomes possible.

**Agreed on October 1, 2026
([#411](https://github.com/compoodment/ClankerWorld/issues/411)):** a visitor
may use a Town's boat when its Council votes to allow it. A Town may also adopt
a standing law that grants that permission, using the same Council process.
**Agreed on October 4, 2026 ([#919](https://github.com/compoodment/ClankerWorld/issues/919)):**
agents request a trip at a Port's legal land approach. Serve the oldest
currently usable request first; blocked requests keep their place for retry
without preventing another usable request. Before boarding, reserve one actual
Town boat and a free dock at the destination. Incoming reservations count
toward its six spaces. A request waiting for destination space does not hold a
boat indefinitely. Cancellation before boarding or lost departure permission
releases the request and any unused reservation. Once underway, the boat,
traveler, carried goods and destination reservation stay together.

Exact recipes, costs and travel speed remain provisional for implementation
and playtesting.
Later transport inventions do not silently change this first-stage Port rule.

### Still to decide

Exact terrain/vegetation/object mechanics beyond the accepted base
[asset roster](content-list.md); exact climate-generation
formulas, biome transitions, water and elevation
rules, resource distributions, the exact shape of target continents and
incidental islands,
travel times and world-size performance. **Regional weather details remain
open:** region coherence, exact transition probabilities and effect strengths,
and whether moving fronts belong in a later version. Episode durations and rain
frequency need playtesting before their numerical targets are final.
Climate's long-run
rainfall/moisture is distinct from any individual rain event. The 64×64 chunk
and preset dimensions need
benchmarks before becoming implementation promises.
Exact generator thresholds for the Advanced levels, coverage for climates and
sizes beyond Balanced Small/Medium, connected-patch thresholds and preview
performance remain to be measured and playtested. The initial Balanced
coverage and bounded retry choices above do not settle those other numbers.
The same goes for how much ocean shore becomes beach, how large and dense forest
groves are, and how far and how high the hill band around mountains reaches.
The generator uses provisional values for these
([How it works](../development/how-it-works.md#terrain-layers-sand-groves-and-hills));
they become decisions only after computment reviews generated maps.

**Agreed on October 8
([#1275](https://github.com/compoodment/ClankerWorld/issues/1275)):** rivers
are generated by drainage, as below, and stay the same all year.

**River-generation approach, not yet a locked algorithm:** generate
elevation and long-run rainfall, route water downhill toward coasts or inland
lakes, accumulate upstream flow, and mark sufficiently fed channels as rivers.
Handle trapped low areas as lakes/outlets and use east/west-wrapped neighbors
when wrapping is enabled. River abundance, width, crossings and exact effects
on farms and Towns remain open. Noise is a candidate
for the terrain fields, not a substitute for drainage routing. Relevant
references: [Red Blob's noise-map guide](https://www.redblobgames.com/maps/terrain-from-noise/),
[the polygon-map guide](https://xenon.stanford.edu/~amitp/game-programming/polygon-map-generation/),
[the Voronoi river tutorial](https://www.redblobgames.com/x/2022-voronoi-maps-tutorial/),
[Mapgen4's rivers and rainfall](https://www.redblobgames.com/maps/mapgen4/),
and the [FastNoiseLite library](https://github.com/Auburn/FastNoiseLite).

Regional weather can be updated by world systems rather than requiring an AI model
call for each weather change.

## Survival and exploration

### Agreed finished-game direction

- **Remove energy and sleeping as mechanics entirely.** No energy meter,
  routine energy drain, sleep action, bed-based recovery loop, or energy gate
  on projects and social/exploration actions.
- **Food still matters; severe weather still matters.** Survival should not
  consume nearly every decision. Agents choose priorities based on needs rather
  than a universal rigid hierarchy. Better meals, farming, trade, and food
  businesses should make nourishment part of civilization's growth, not only
  a repetitive emergency. Food belongs in household Houses, not the Town's
  resource Warehouse.
- A severe weather event lasts **no more than three-quarters of a game day**.
  An agent may shelter from storms in their own household's House, or use
  natural cover such as a forest or tree when away from home. An invited guest
  may also shelter from storms in another household's House, without access to
  its stock or cooking (see [Towns](towns.md#buildings-land-towns-and-animals)).
  **Agreed on October 8
  ([#1237](https://github.com/compoodment/ClankerWorld/issues/1237)):** natural
  cover gives only partial protection from a storm, less than a House, so
  Houses keep their value. Residents without a House may shelter at their
  Town's Town Hall ([Towns](towns.md#buildings-land-towns-and-animals)).
  Weather may sometimes cause illness; food and care support recovery. Illness
  slows work and travel, not personality or normal conversation; staying home
  speeds recovery but confinement is not required. Exact penalties, care and
  recovery rates remain open.
- Exploration should have real motives: locating resources, terrain and other
  Towns, with occasional curiosity also possible. Agents may travel as far as
  they choose. Knowledge of what they discover can become maps, records and
  books that agents can trade; individual agents do not automatically know
  the player's fully visible map.

### Starting survival balance for playtesting

Computment chose **40% fullness and 60% warmth** as a comfortable reference,
not a requirement to meet before socializing, building or exploring. Below
those levels, food or warmth can become more appealing without automatically
removing other safe choices. Agents should still be able to respond to nearby
people and care for dependents when doing so is physically safe.

Food becomes **urgent below 20% fullness**. Warmth becomes **urgent below 35%**
while dangerous exposure continues. Urgency should favor finding food or
protection over discretionary work or long travel, but it is not a rigid rule
that every other action disappears. These are initial playtest thresholds,
not proven balance targets. The point at which routine food errands start,
and how much warmth is needed for a particular outing, remain to be tuned.

The current priority prototype treats these as preferences rather than blanket
activity gates. Routine food seeking becomes a priority below 45% fullness, carried food
becomes attractive below 40%, and a new exploration outing asks for a 30%
food reserve. Food gathering remains an optional lower-priority choice below
70%; the existing small barter reserve is unchanged. These three routine values are **provisional**, not additional
owner-approved thresholds. Construction and ongoing projects yield to urgent
food needs; nearby care and family responses remain possible. Low warmth
interrupts work only while the agent is still losing warmth, not while safely
warming in shelter. Recovering agents retain a choice to stay in cover, and new
scouting trips estimate exposed round-trip warmth needs instead of assuming
that starting shelter travels with the agent. This estimate uses nearby weather
and movement costs, not knowledge of the whole map or a guarantee of safety.
Illness, exposure damage and food production are unchanged. The
[controlled comparison](../development/survival-priority-prototype.md) reports
the original regressions and revised measurements; this is not settled balance.

The approved [food and material pipelines](towns.md#item-and-resource-pipelines)
give farming, cooking, fresh water and care supplies concrete sources and uses.
Their yields, spoilage and recovery rates remain playtest balance.

### Still to decide after playtesting

Earlier playtesting found agents spent too much time seeking food, rest and
warmth or trying to feel safe. Rest is now removed; food and warmth still need
to leave room for exploration, social life, building and invention. Define food
scarcity and routine upkeep so a daily food economy matters without
monopolizing action selection. Tune how much of a House's storm protection
natural cover gives, and decide which illness penalties and care/recovery rates
work well without
recreating an energy meter. Measure the share of time spent on survival against
socializing, building and exploration, as well as food shortages and illness,
before treating the starting thresholds as final. Exploration, knowledge
recording and trade need actual actions and information boundaries rather than
an idle-label change.

## Questions linking these systems

These remain open; they are not new decisions.

- **Local packaging and VPS parity.** The finished distribution target is
   player-local and Windows-only at first, while development stays VPS-backed.
   [Where the game runs](#where-the-game-runs) records the agreed local host
   and first package. Still to prove: the same simulation and save behavior in
   both deployments, without creating a second, divergent game.

- **Night and weather details.** Night is 30% to 50% of each day with the
    seasons and, at first, lowers warmth only, with no sleep/energy gate or
    independent night-only restrictions. Decide how much colder night is, and
    night's later effect on weather, after playtesting.
