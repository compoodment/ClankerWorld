---
title: The world, time and survival
type: game-design
status: active
updated: 2026-09-30
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
  simple PC install that runs the game and its the game rules on
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

### Still to decide

Whether the local host is embedded in the installed game or bundled as a
background companion process; installer and update design; exact Windows
version/architecture support; local credential storage and diagnostics export;
save migration between deployments; performance requirements and packaging
tests; on-demand credential validation and the new-world starting-agent setup when no
key exists yet. These are implementation choices to prove, not reasons to
reopen the decided player-local distribution goal.

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
- **Leaning toward:** biological age corresponds to elapsed world/calendar time,
  but its display and life-stage milestones need rethinking for short lives.
  Night should occupy more of each cycle relative to daylight than in the
  earlier proposed split; its exact share is not decided. Night affects
  temperature and weather, without a sleep/energy requirement or a separate
  night-only travel, visibility, work or social restriction. Weather itself
  can still affect agents under the ordinary weather rules.

### Still to decide

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

## Maps, plants and weather

### Agreed

- World-size presets are **Small, Medium, Large, Huge, Mega**. They should feel
  roughly like one Town, several Towns across a region, one major continent, two or three
  continents, and a planet respectively. The proposed logical dimensions are
  **256×128, 512×256, 1024×512, 2048×1024, 4096×2048**—initial benchmark
  targets, **not locked constants**.
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
  climates without changing the map's wrapping choice.
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
- **September 29 terrain direction:** sand is not a compulsory strip along
  every coast and especially not along every river. A forest-floor tile should
  visibly contain a tree or plant, mostly trees; grass-surfaced forest tiles
  should instead have scattered trees. Trees and ordinary plants should not
  grow on sand. Computment now disfavors cacti entirely, though that exclusion
  was phrased tentatively; avoid expanding cactus content until settled. Hills
  forming a readable base around mountain regions are preferred; their exact
  elevation thresholds and passability remain open.
- Generated geography includes **rivers** as well as oceans, shores and lakes.
  Rivers belong to the 2D, top-down tile world and its water layout layer; they
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

### Weather over time

Computment is **leaning toward regional weather episodes** rather than a new,
independent weather roll every day. A region's next condition should depend on
its climate and previous condition. The first version should not simulate
storms physically travelling between regions; drifting visual effects do not
mean a weather front has moved.

For this direction, ordinary weather should last a variable **one-quarter to
one in-game day** before changing. This is a starting range to tune through
playtesting, not a fixed probability or a promise that every condition occurs
equally often. A severe episode lasts **no more than three-quarters of a game
day** and is followed by at least **half a game day without severe weather** in
that region. When an episode changes, wet neighboring regions should make rain
somewhat more likely, without forcing the same weather across the map. The
strength of that influence and the overall rain frequency are still open for
playtesting.

### River crossings and visible forests and mountains

At default settings, generated worlds should visibly include forests and
mountain regions rather than relying on rare seeds to reveal them. For
**Balanced Small and Medium**, use initial playtest targets of **20–40% forest**
and **5–12% mountains**, measured against dry land. These are targets to test
and tune, not a promise that every climate or world size has the same coverage.
Uniform Dry and polar regions must not acquire inappropriate trees just to
meet a forest target. Forest and mountain areas should form readable regions;
their exact connected-patch minimum remains to be tuned.

Try at most **three deterministic candidates** for the selected seed and
settings. Identify the chosen candidate in the exact preview so Create World
uses that same map. If none meets its eligible targets, show what was missed
and let the player choose another seed or explicitly accept the result; do not
silently substitute a different map. Larger-size targets and preview latency
remain subject to measurement and playtesting.

Computment wants agents to cross **one-tile-wide rivers on foot, more slowly**
than dry ground. The world automatically adds bridges at sufficiently used
crossings. A generated Road may also **create a bridge immediately** where its
route meets a bridgeable river; it need not wait for traffic there. Once a
bridge is placed, a no-other-bridge radius prevents a
redundant bridge appearing right next to it on the **same crossing/river**.
It does not block a needed bridge over a separate nearby stream. Exact radius,
bridge materials/work, and wider/deeper river crossing rules remain open. The
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
them. Exact recipes, costs, travel speed, queueing and recovery when a Port
becomes unavailable remain open for implementation and playtesting. Later
transport inventions do not silently change this first-stage Port rule.

### Still to decide

Exact terrain/vegetation/object mechanics beyond the accepted base
[asset roster](content-list.md); exact climate-generation
formulas, map topology at polar edges, biome transitions, water and elevation
rules, resource distributions, the exact shape of target continents and
incidental islands,
travel times, world-size performance, and limits
on agent-caused terrain changes. **Regional weather details remain open:**
region size and coherence, exact transition probabilities and effect strengths,
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

**Suggestion river-generation approach, not yet a locked algorithm:** generate
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

### Still to decide after playtesting

Earlier playtesting found agents spent too much time seeking food, rest and
warmth or trying to feel safe. Rest is now removed; food and warmth still need
to leave room for exploration, social life, building and invention. Define food
scarcity and routine upkeep so a daily food economy matters without
monopolizing action selection. Decide what an
agent without any House does during a storm, how much natural cover protects,
and which illness penalties and care/recovery rates work well without
recreating an energy meter. Measure the share of time spent on survival against
socializing, building and exploration, as well as food shortages and illness,
before treating the starting thresholds as final. Exploration, knowledge
recording and trade need actual actions and information boundaries rather than
an idle-label change.

## Questions linking these systems

These remain open; they are not new decisions.

- **Local packaging and VPS parity.** The finished distribution target is
   player-local and Windows-only at first, while development stays VPS-backed.
   Prove the same simulation and save behavior in both; choose the local
   process/installer shape without creating a second, divergent game.

- **Night and weather details.** Night affects temperature and weather, with
    no sleep/energy gate or independent night-only restrictions. Decide exact
    temperature and weather effects alongside the day/night split.
