---
title: ClankerWorld Vision Interview History
type: historical-interview
status: history
updated: 2026-09-26
---

# ClankerWorld Vision Interview — Historical Record

> This is the chronological interview record, preserved for traceability. Some
> early “Open” labels were resolved by later answers and are intentionally not
> edited here. For the current decisions and remaining questions, use
> [current vision ledger](../game-design/README.md).

> This is archived interview evidence, not the current decision authority or
> implementation status. Earlier notes below may describe superseded plans.

## Interview status

- Interviewing the intended finished game first.
- Alpha scope, architecture, repository cleanup, and documentation restructuring come later.
- **Confirmed** means computment explicitly chose the direction.
- **Leaning** means computment currently prefers it but requested explanation or comparison.
- **Candidate** means an idea worth developing, not yet an accepted design.
- **Open** means the answer is incomplete, ambiguous, or conflicts with another goal.
- Existing code and repository documents are evidence and prior design context.
  When they conflict with this notebook, the conflict must be surfaced rather
  than silently treating the repository as authoritative product intent.

## Confirmed terminology

- The game is now called **ClankerWorld**, replacing AgentWorld.
- The simulated people are called **agents**, replacing inhabitants.
- The launch screen is the **Main Menu**.
- The in-world overlay is the **Pause Menu**.

## Answers captured so far

### Intended game flow

1. Launching ClankerWorld opens a Main Menu.
2. The Main Menu includes:
   - **New World**;
   - **Load World** when saved worlds exist;
   - **Settings**;
   - **Quit Game**.
3. New World opens a setup flow where the player chooses at least:
   - world size;
   - world climate;
   - whether a base camp is generated for the first agents;
   - potentially other world-generation settings to be decided.
4. The game generates a visually coherent map with geographical structure such as continents, islands, plains, biomes, forests, and lakes.
5. After generation, the player enters the world view.

### In-world interface

- A top bar is visible in the world view.
- The top-right menu button opens the Pause Menu and pauses the simulation.
- The Pause Menu includes:
  - **Save World**;
  - **Settings**;
  - **Quit to Menu**.
- Save World and Settings may open larger dedicated popups.
- The Settings popup should use categories on the left and the selected settings on the right.
- Quit to Menu should require confirmation if the world has not been saved properly; autosave behavior remains open for discussion.
- The world camera supports mouse-wheel zoom and WASD panning.

### Adding agents

- A plus button beside the menu button opens the Add Agent flow.
- The player chooses a provider, supplies or selects an API key, and chooses a model.
- Previously stored API credentials should be reusable rather than repeatedly re-entered.
- Every agent has its own independently selected provider/model.
- After configuration, the player places the new agent somewhere in the world.
- The agent chooses its own name after being placed.
- Agents should use full names, including a surname and possibly a middle name, to reduce collisions.
- Further identity, memory, personality, appearance, and provider-isolation details remain open.

### Agents, families, and population

- Agents do not have genders.
- When only two agents exist, they will create a baby agent so civilization can continue rather than ending because both independently decline parenthood.
- The exact reproduction, relationship, inheritance, childhood, and child-model rules remain open.

### Buildings and settlements

- Buildings can have different footprints: 1x1, 1x2, 1x3, 2x2, 3x3, and potentially other shapes.
- A 3x3 maximum currently sounds reasonable but is not yet a final rule.
- The currently chosen structure catalogue must be reconsidered against the intended finished game.
- Residential scale relates to household storage: larger houses provide more storage.
- The relationship between residential households and communal/non-residential buildings remains open.

### Core game fantasy

- Agents survive, but survival should not be excessively harsh or dominate everything.
- Agents socialize and build villages, towns, or larger settlements.
- Agents should ultimately be able to code or create their own things.
- The incentive model for invention and creation needs collaborative design.

## Finished-game questionnaire — batch 2 answers

Source response received 2026-09-23. The wording below preserves the design
meaning while separating decisions from unresolved details.

### World creation and geography

1. **Confirmed:** world-size presets are Small, Medium, Large, Huge, and Mega.
2. **Confirmed conceptual scale:** Small supports a town; Medium a city; Large
   one continent; Huge two or three continents; Mega a planet. **Open:** exact
   tile dimensions, simulation capacity, travel time, and whether these labels
   describe physical area or expected settlement capacity.
3. **Confirmed direction:** the finite map wraps like a spherical world.
   **Open:** the projection/topology and how polar distortion or diagonal paths
   work on a rectangular tile renderer.
4. **Confirmed direction:** climate generation offers uniform-world,
   dominant-climate, and balanced modes. Detailed choices and consequences are
   still open.
5. **Confirmed model:** every tile has terrain; terrain availability depends on
   biome. Elevation includes hills, mountains, and peaks. Water includes shallow,
   ordinary/deeper inland water, and deep water, used by oceans, rivers, lakes,
   and shores as appropriate. Trees are resource-bearing world objects on top
   of terrain, animated, and leave trunks/stumps when cut. One tile may depict
   sparse, medium, or dense tree cover (roughly one, two, or three trees).
   **Open:** the complete terrain/biome taxonomy, naming, transitions, elevation,
   hydrology, and whether density is several individual trees or one aggregate.
6. **Confirmed:** generation controls may include water percentage, continent
   count, and resource abundance. Raw rainfall and temperature sliders are not
   desired; temperature follows biome. **Open:** whether moisture is still an
   internal generator input and what other player-facing controls exist.
7. **Confirmed:** generated worlds can be previewed and rerolled before play.
8. **Confirmed:** the player sees the complete geography immediately. There is
   no player fog of war; agents may still possess limited personal knowledge.
9. **Confirmed:** agents can cross water using crafted boats and shore-connected
   ports. Agents may later invent improved boats, ports, and related buildings.
10. **Confirmed:** terrain can change through simulation and agent activity.
    Exact mechanisms and protected limits remain open.
11. **Confirmed:** an enabled base camp provides a furnished residential house
    with beds, food, storage, tools, fire, and seeds. Interiors can contain
    abstracted items not individually drawn on the world map. A house is
    clickable to inspect occupants, household, storage, and contents. Multiple
    agents can occupy it. Guests may request permission to sleep over, creating
    real social interaction.
12. **Confirmed direction:** without a base camp, agents begin with nothing and
    must bootstrap survival and technology themselves. **Open/possible conflict:**
    they cannot code or craft an axe/building without some reachable primitive
    resource and primitive no-tool actions; the zero-start bootstrap loop needs
    an explicit design.

### Main Menu and saves

13. **Confirmed:** Continue appears separately and opens the most recent world.
14. **Confirmed:** ClankerWorld supports both autosaving and manual saving. Each
    world has its own autosave; one world's autosave never overwrites another's.
15. **Confirmed:** manual saves are named. **Open:** rotating autosave history and
    crash/emergency recovery were not understood and need explanation.
16. **Open:** the distinction between one living world save, manual checkpoints,
    and multiple divergent save branches needs explanation and a decision.
17. **Confirmed:** Quit to Menu and Quit Game always ask for confirmation.
18. **Confirmed direction:** mods/content belong to the world save that created
    or installed them. Provider credentials and graphical settings are global.
    Graphics settings are device/display-aware, including resolution, refresh
    rate, and multiple monitors. **Open:** whether mods can also be exported and
    intentionally imported into other worlds.

### Creating agents

19. **Confirmed:** every agent independently owns its selected provider/model,
    persistent private memory/context, personality/goals, call schedule and usage
    record, and failure state.
20. **Confirmed:** several independent agents may reuse the same provider/API key.
21. **Confirmed:** provider credentials are stored globally and can be selected
    for later agents. The player can optionally replace or enter another key.
22. **Confirmed:** the agent decides its own personality, aspirations, skills,
    and related identity rather than the human configuring them at creation.
23. **Confirmed:** the player can rename an agent.
24. **Confirmed direction:** appearances are randomly assembled from authored
    assets. The production asset set does not exist yet.
25. **Confirmed:** the human may add adult agents at any time at no in-world cost.
26. **Confirmed:** there is no arbitrary player-facing population cap; practical
    limits come from resources, housing, and compute/provider capacity.

### Agent communication

27. **Confirmed:** initial communication is face-to-face and proximity-bound.
    Agents may later invent communication technologies for distance.
28. **Leaning:** real model turns should produce conversations; computment asked
    for alternatives, pros/cons, and a recommendation.
29. **Confirmed:** the UI shows a summary first and allows expansion to the full
    conversation.
30. **Confirmed:** agents can lie, misunderstand, keep secrets, gossip, and hold
    memories that other agents do not know.
31. **Candidate:** language and dialects may emerge, but feasibility and actual
    gameplay value with LLMs are unresolved.
32. **Open:** group planning needs bounded multi-agent conversation that avoids
    infinite loops but reaches a conclusion, plan, or explicit failure rather
    than stopping arbitrarily.

### Genderless families and children

33. **Confirmed direction:** when total population is low (candidate threshold:
    six or fewer), pair formation and births must occur so civilization cannot
    end solely because models refuse. Close biological relatives cannot pair.
    Starting with at least four unrelated agents/two households is the expected
    civilization-oriented setup. **Open/possible conflict:** mandatory pairing
    and birth must be reconciled with agent autonomy and required consent.
34. **Confirmed direction:** outside the low-population safeguard, parenthood is
    optional. Exact entry/exit thresholds and hysteresis remain open.
35. **Confirmed:** parenthood requires consent but not romantic partnership.
36. **Confirmed:** every baby has two parents.
37. **Confirmed:** children inherit personality tendencies, abilities, culture,
    and provider/model settings. Appearance uses baby texture/assets rather than
    inherited generated appearance. Biological/family restrictions persist.
38. **Open:** Jev may handle childhood cognition. Jev is a per-world facility,
    not individually enabled/disabled for agents. Its exact role and the age at
    which a child receives a full independent model remain undecided.
39. **Confirmed:** the parents select the child's provider/model.
40. **Confirmed:** the parents choose the child's name.

### Buildings, households, and settlements

41. **Confirmed clarification:** one household per building applies to residential
    buildings. The purposes and gameplay loops for workshops, farms, warehouses,
    markets, and town halls need explicit design. In particular, the function of
    a workshop is currently unclear to computment.
42. **Confirmed direction:** a household may expand into multiple connected
    buildings and own land. **Candidate:** land can be bought/sold, and a mayor
    or settlement may acquire and sell land. Land value and ownership need design.
43. **Confirmed:** every building has type-appropriate internal storage. Farm
    buildings hold/produce agricultural goods; warehouses hold broad or
    specialized inventories; other types have fitting limits and functions.
44. **Confirmed:** larger homes support more occupants, storage, and comfort.
45. **Confirmed:** 3x3 is a sensible early maximum; agents may invent larger
    structures later.
46. **Confirmed direction:** irregular footprints and multiple floors exist,
    likely unlocked through invention. Extensions/connection details are open.
47. **Confirmed direction:** agents choose building locations. Good land use,
    roads, and accessibility should influence choices without creating an
    incomprehensible optimization burden. Roads exist and pathfinding prefers
    them. **Candidate:** land value may inform placement.
48. **Confirmed:** agents formally declare settlements and borders.
49. **Confirmed:** village/town/city classification is population-based.
    Thresholds remain open for Clanker's proposal and computment's approval.

Additional open expansion ideas raised here: roads, bicycles, cars, horses,
wildlife, livestock, and food animals. None are confirmed merely by being raised.

### Agent-created content and code

50. **Confirmed examples:** buildings, tools, crops, machines, art, laws,
    currencies, and related cultural/economic systems. Recipe scope and a
    complete capability taxonomy remain open. Agents do not invent/change the
    world's natural biomes.
51. **Leaning:** agents eventually generate executable game logic, not only
    predefined declarative data. The declarative/executable distinction,
    feasibility, safety, and gameplay consequences need explanation before a
    final decision.
52. **Confirmed goal:** agents can autonomously invent and activate creations in
    their own world save rather than requiring routine human approval.
53. **Confirmed:** inventions can include generated art and textures.
54. **Confirmed:** some simulation rules can change, but protected physical laws
    remain untouchable. The constitutional boundary is not yet enumerated.
55. **Confirmed direction:** unmet needs, curiosity, aspirations, social status,
    requests, and accumulated expertise can all motivate invention.
56. **Confirmed direction:** agents may teach, conceal, sell, improve, and create
    competing variants of inventions according to their own choices.

## Questions computment returned to Clanker

Clanker must provide reasoned proposals—not silently choose—for:

- exact tile dimensions and performance meaning of all five world sizes;
- the detailed climate, biome, terrain, elevation, water, forest, and resource model;
- autosave rotation, crash recovery, manual checkpoints, and save branching;
- global versus world-specific mod/content libraries;
- direct model conversations versus simulated social actions;
- practical emergent language/dialect mechanics;
- bounded multi-agent conversations that still reach conclusions;
- the best use of Jev, especially for children and cheap cognition;
- low-population continuity without fake consent or incest;
- land ownership, land value, mayors, roads, transport, wildlife, and livestock;
- useful functions for workshops and the complete structure catalogue;
- population thresholds for village, town, and city;
- declarative content versus sandboxed executable logic;
- a detailed invention taxonomy, motivation loop, and protected physical laws.

## Contradictions and pressure points to resolve

1. **Nothing-start bootstrap:** agents begin with nothing, yet tools/buildings
   require materials and production means. Primitive hand-gathering and a first
   invention path must prevent an impossible starting state.
2. **Consent versus mandatory continuity:** parenthood requires consent, but low
   population must produce couples and children. The game cannot honestly claim
   both unrestricted refusal and guaranteed birth without a distinct world rule.
3. **Agent autonomy versus human renaming:** agents choose their identity, but the
   human can rename them. We need to decide whether this is editing display text,
   a must-do intervention, or literal control over identity.
4. **No arbitrary population cap versus compute limits:** the fiction has no cap,
   but model cost and hardware are finite. Background cognition and explicit
   capacity feedback must exist without pretending the limit is biological.
5. **Autonomous executable code versus protected reality:** agents activate code
   without approval, while physical laws and the player's device must remain safe.
   This requires an actual capability sandbox, not merely validation wording.
6. **Planet scale versus full simulation:** a Mega planet cannot plausibly run
   every tile and agent at full fidelity. Chunking and distant simulation are
   product behavior, not only technical implementation details.
7. **World-specific creations versus sharing:** inventions belong to one save,
   but it remains open whether players can deliberately export/import them.

## Next finished-game interview topics

- Resolve world-size tile counts, topology, zoom levels, generation controls,
  terrain/biome taxonomy, and distant simulation.
- Resolve the zero-start survival and invention bootstrap loop.
- Design conversation turns, group planning, secrecy, memory, language, and Jev.
- Resolve low-population continuity, consent, households, ancestry, and children.
- Define structures, interiors, land, roads, settlement borders, transport,
  wildlife, farming, storage, and settlement classification.
- Define invention capabilities, sandbox limits, motivation, diffusion, and
  protected reality.

## Finished-game questionnaire — batch 3 answers

Source response received 2026-09-23 after Clanker's first design proposals.

### World rendering, simulation, and navigation

- **Accepted as candidates:** the proposed Small through Mega tile ranges are
  liked, pending a plain-English explanation of compact storage, chunks,
  rendering, active simulation, distant simulation, and logical versus visual
  tile size.
- **Confirmed:** world wrapping is a New World option. Wrapped worlds use the
  proposed seamless east/west model with polar regions; non-wrapped worlds are
  also supported.
- **Confirmed direction:** the world UI needs a full-world overview/minimap that
  shows the current camera rectangle. The player can drag that rectangle or use
  the map to jump quickly around the world. A maximum detailed zoom-out is
  expected. Exact placement is likely the left side of the top bar but awaits a
  future UI sketch.
- **Confirmed invariant:** agents, crops, settlements, and other meaningful
  processes do not cease to exist merely because the camera is elsewhere.
  Rendering and simulation must remain distinct.
- **Open:** whether colder north/south poles are mandatory, the default, or a
  configurable world-generation option.

### Climate, ecology, and art

- **Accepted with terminology correction:** climate/temperature, terrain,
  vegetation/forest cover, water, and objects should be separate layers. The
  generator decides where sparse/medium/dense forests and biome-appropriate
  objects occur. A desert may produce cacti rather than ordinary trees.
- **Confirmed:** sparse/medium/dense tree stands are aggregate world objects;
  their texture depicts density and their simulation value stores available
  resources.
- **Confirmed art direction:** ClankerWorld uses pixel art.
- **Open:** the full climate/terrain/vegetation/object catalogue, tile resolution,
  asset source format, runtime format, animation format, palette, and texture
  production workflow.

### Starting mode and survival

- **Changed decision:** the no-base-camp/nothing-start mode is removed from the
  planned finished game for now. ClankerWorld starts with one supported base-camp
  mode. A nothing-start challenge may be reconsidered only after the core game.
- **Confirmed:** a short survival grace period exists as an optional New World
  setting. Exact duration/effects remain open.
- **Open:** the base camp's buildings and supplied necessities must be redesigned
  around intended founder count, households, structures, and basic needs.

### Saving and recovery

- **Confirmed:** Autosave Settings allow autosave on/off, selectable interval,
  and selectable rotating-autosave count including none. Emergency recovery is
  automatic background protection. Manual named saves are unlimited.
- **Open:** recommended interval choices, save-on-quit behavior, retention limits,
  storage warnings, and whether autosave happens during provider/conversation work.

### Mod and invention library

- **Confirmed:** the player has a personal library and may export inventions
  from one world and import them elsewhere.
- **Confirmed direction:** the in-world Pause Menu contains a **Mod Library**
  showing active, developing, failed, or other agent creations. The Main Menu
  also exposes a global Mod Library that can browse each world's latest save and
  supports import/export.
- **Open:** the precise difference between an in-world invention and a portable
  mod, external player-authored mods, package trust, versioning, dependencies,
  cross-world compatibility, and whether “Mod Library” contains multiple tabs.

### Agent conversations and decisions

- **Confirmed emphasis:** agents are social and should converse regularly, not
  only in rare scripted moments, while still spending time on work and life.
  The LLM is integral to the agent and chooses economic, organizational, land,
  and other meaningful actions; deterministic simulation enforces reality.
- **Confirmed UI:** socializing agents display a chat bubble above their sprites.
  Clicking it opens a small local popup showing a conversation summary; the
  player can expand it into the full conversation.
- **Open:** how conversations end naturally without Jev/simulation casually
  aborting them, while still allowing urgent interruption, preventing loops,
  controlling token use, and supporting later resumption.
- **Deferred:** emergent languages/dialects are not currently planned because of
  token cost and uncertain value, but remain a possible future feature.
- **Accepted:** Jev is an optional per-world support layer while every agent
  retains its personal model.

### Population continuity

- **Accepted:** use the continuity-instinct plus civilization-preset approach.
  Four unrelated founders/two family lines are strongly recommended for a
  civilization world. Below the chosen population threshold, parenthood/birth
  refusal is not fully autonomous; full parenthood consent becomes meaningful
  once the continuity threshold has been met.
- **Open:** exact population threshold, eligible-pair selection, household
  formation, age/resource requirements, and how the UI communicates this world
  law honestly.

### Land, structures, and capacity

- **Accepted:** begin with simple land claims and deterministic desirability;
  monetary land value appears only after currency exists.
- **Accepted:** a workshop stores tools/materials and supports crafting, repair,
  prototypes, machines, and specialized production.
- **Confirmed addition:** non-residential buildings have occupancy limits based
  on type and size. An agent blocked by a full destination retains the reason for
  going, chooses a temporary alternative or waits, and retries later rather than
  forgetting the goal.
- **Possible contradiction:** computment described an entire household as able to
  fit inside its residential building regardless of size, while earlier answers
  said larger homes support more occupants, storage, and comfort. Residential
  occupancy/overcrowding needs clarification.
- **Open:** whether farms are occupiable buildings, outdoor work zones, or both;
  reservations/queues for building access; and whether Jev participates in
  blocked-task replanning.
- **Open:** rethink all generated starter structures and remove furniture as a
  presumed core category unless it serves an actual gameplay effect.

### Executable invention and runtime activation

- **Accepted:** use declarative components plus sandboxed executable scripts.
- **Confirmed:** safe successful inventions can activate while the world is
  running. Agents use what they invent in addition to teaching, hiding, selling,
  copying, or improving it.
- **Confirmed failure behavior:** agents are told why an invention failed and may
  redesign it after a cooldown. Research/invention must not monopolize their life
  or let them slowly die while repeatedly coding.
- **Open:** exact sandbox/runtime, hot activation boundary, state migration,
  rollback, script API, test-world duration, cooldown, and how failed creations
  affect memory/motivation.

### Wildlife, combat, and technology

- **Confirmed:** livestock and mounts exist in the finished game. Other wildlife
  categories remain open.
- **Confirmed:** interpersonal combat exists in the finished game.
- **Current-code evidence:** agents have numeric health and general death-cause
  primitives, but the integrated game has no combat loop, attacks, weapons,
  armor, combat injuries, or combat AI. Existing tools/clothing are work and
  weather equipment, not weapons/armor.
- **Open:** combat purposes, lethality, hunting/predators, weapons, armor,
  medicine, policing/law, surrender/fleeing, war, and agent-created combat tech.
- **Open:** predefined technology tree versus prerequisite/capability-driven
  invention. Computment requested pros, cons, a recommendation, and an example.

## Questions returned after batch 3

- Explain world chunks/rendering/simulation/logical tiles in plain English.
- Design maximum zoom-out plus draggable minimap/world overview.
- Decide polar climate defaults/options.
- Correct and fully specify climate, terrain, vegetation, biome, object, and
  texture terminology.
- Recommend pixel-art formats and optimization strategy.
- Recommend autosave intervals and recovery behavior.
- Define invention versus mod and design both Mod Library surfaces.
- Make conversation frequency, endings, interruption, summaries, and resumption
  comprehensible and robust.
- Confirm Jev and population-continuity details.
- Redesign generated base camp structures/basic supplies.
- Explain live sandbox activation.
- Design occupancy, blocked destinations, remembered tasks, and Jev's role.
- Audit and propose the health/combat/equipment model.
- Compare tech tree versus open invention and recommend a model.

## Later interview topics

- Definition and exit criteria for a playable alpha.
- Intended local-device versus development-VPS architecture.
- Art direction and production asset pipeline.
- Repository structure, naming, obsolete code, generated artifacts, and cleanup.
- Documentation consolidation and authority.
- Development cadence and what counts as a complete feature.

## Finished-game questionnaire — batch 4 answers

Source response received 2026-09-24 after the plain-English rendering and
simulation explanation. This section supersedes older open/candidate statements
where they conflict; earlier sections remain as interview history.

### World chunks, camera, map, and climate

- **Confirmed initial target:** 64×64 logical tiles per world chunk. This is a
  storage/streaming/simulation organization choice, not a requirement to create
  one giant texture for each chunk. Benchmark and tune before locking it.
- **Corrected/confirmed:** there is one continuous pixel-art world view, zoomed
  in and out up to a readability/performance limit. No separate regional visual
  mode, duplicate regional texture set, or level-of-detail art system is wanted.
- **Confirmed UI:** a map button is always accessible at the top-left of the top
  bar and expands the world overview/minimap when clicked. The overview depicts
  the current camera rectangle and allows dragging/jumping the camera. It may be
  generated from world data without a second set of authored textures.
- **Accepted:** the revised separation of climate zone, elevation, surface,
  hydrology, vegetation cover, and objects.
- **Confirmed:** natural colder poles are enabled by default; an Advanced World
  Setting may disable latitude-based cooling for unusual worlds.

### Pixel-art production

- **Confirmed initial standard:** 32×32-pixel ground tiles and PNG runtime
  assets. Aseprite or layered source files are acceptable, but computment has
  not used Aseprite and does not require it as the sole editing tool.
- **Confirmed intent:** most textures will likely be AI-generated collaboratively
  with Clanker, then adapted into a coherent pixel-art game style.
- **Confirmed requirement:** agent-created inventions and portable mods should
  also be able to obtain acceptable art through an automated, governed pipeline.
- **Candidate pipeline for approval/playtest:** AI draft or reusable-component
  assembly → pixel-grid cleanup/style normalization → PNG frames/atlas plus
  canonical metadata → technical/provenance/performance validation → in-game
  preview → activation. Art failure should leave a legible fallback icon/sprite
  and actionable feedback, not destroy the invention or the world.
- **Still open:** exact palette/style guide, animation frame standards,
  AI-generation provider/cost controls, art-quality criteria, when generated art
  can auto-activate, and the final asset schema.
- **Clanker's detailed candidate, not yet approved:** keep terrain bases at
  32×32, while taller trees/agents and multi-tile buildings may have larger
  transparent canvases anchored to logical tiles. Editable sources may be
  `.aseprite` or layered PNG; runtime output is PNG sheets/atlases and metadata
  identifying asset ID, category, tile footprint, pixel bounds, anchor, draw
  layer, collision, frame timing, biome/style tags, provenance, and version.
  Build art may be curated from AI candidates, whereas autonomous agent art
  first uses approved components/recoloring and only optionally requests new
  generated imagery under player-configured cost limits. Technical validation
  and preview are required; aesthetic oddness alone is not grounds to erase an
  in-world invention. A fallback sprite/icon keeps valid gameplay usable when
  art generation fails.

### Starter world, saves, libraries, and social systems

- **Accepted:** a generated starter camp for four unrelated founders/two family
  lines has two small houses, shared storehouse, fire/cooking area, workshop,
  nearby fertile land, basic food/seeds/tools/clothing, and a connecting path.
  Furniture may be abstracted as inspectable building contents/capabilities;
  every chair need not appear on the world map.
- **Accepted:** autosave is **on by default**. The proposed five-minute interval
  and five rotating autosaves are accepted as initial defaults, alongside the
  selectable intervals/counts, automatic save on quit, unlimited named manual
  saves, and background emergency recovery. Exact performance/storage behavior
  remains subject to implementation and playtesting.
- **Accepted:** an invention originates inside a world; a mod is a portable
  package. The Mod Library direction and its in-world/global surfaces are liked.
- **Accepted provisionally:** the proposed conversation lifecycle, common but
  contextual frequency, summaries/full transcript UI, urgent interruption and
  graceful conclusion. Computment wants to judge its feel during playtesting.
  Unresolved conversations should become eligible to resume later.
- **Accepted:** continuity-risk logic is preferable to only a raw population
  threshold in the finished game; the four-founder recommendation stands.
- **Accepted:** simple legal site choices and land claims first; monetary land
  value only once currency exists.
- **Accepted:** separate occupancy/workstation/storage/sleeping/household
  capacities, reservations when practical, queues or alternatives when full,
  authoritative blocked-task memory, and Jev as a helper rather than authority.
  A household may shelter together despite comfortable-capacity limits, with
  overcrowding consequences. "Farm" in the occupancy discussion means the
  farmhouse, barn, silo, etc., not the outdoor field/work zone.
- **Accepted direction:** isolated invention test → safe-tick activation →
  quarantine/rollback on failure, with useful feedback and cooldown. Runtime
  selection and exact guarantees are still open.

### Combat and invention progression

- **Confirmed combat purposes:** self-defense, crime, personal feuds, and
  organized war. Hunting and sport/duels were not selected here.
- **Confirmed tone:** combat may become lethal, but ordinary violence should not
  kill agents constantly. Exact injury, escape, medicine, and death balancing
  are open.
- **Confirmed exclusion for now:** no hostile predators in the planned finished
  game, though they may be reconsidered later. Livestock and mounts remain.
- **Leaning/accepted in principle:** weapons and armor use the general item,
  equipment, crafting, and invention framework rather than a disconnected
  special system; computment's answer was tentative because the distinction
  was not yet fully clear.
- **Leaning strongly:** computment likes the capability/prerequisite graph over
  a rigid visible technology tree, but did not explicitly answer the separate
  yes/no decision. Preserve this as a recommendation, not a finalized rule.
- **Confirmed UI desire:** players can inspect discovered capabilities through
  a top-bar World Info panel. This should show what exists/has been discovered
  and who knows or uses it, without implying agents all share knowledge or
  exposing a fixed future invention tree.

### Top bar and world information

- **Confirmed:** the top bar has a dedicated pause/resume-time control; an
  in-world date and time display; and an agent-population count.
- **Confirmed UI settings:** date format can be changed (DD-MM-YYYY is an
  example/default candidate), and time format can be 24-hour or AM/PM.
- **Confirmed direction:** a top-bar World Info button opens inspectable world
  information, including capabilities. A filter button opens selectable map
  overlays such as household property borders and settlement boundaries.
- **Invariant:** borders/ownership overlays depict established simulation facts
  made by agents and validated by the world system; the UI does not invent
  claims on behalf of the agents.
- **Open:** exact top-bar layout/responsive behavior, world-calendar rules,
  filter catalogue, overlay styling, and representation of disputed/overlapping
  claims.

## Follow-up — bounded configurations (2026-09-24)

Computment corrected the earlier broad building-footprint direction: the game
should support a **limited number of configurations**, because art requirements
multiply rapidly across combinations. This concern applies more broadly than
buildings. The current notebook now treats arbitrary building shapes and other
unbounded content combinations as superseded; exact catalogues, variation
methods, and invention exceptions remain open for interview.

Computment's answers to the subsequent five questions clarified the current
direction: each building type gets a few shapes; the first complete game needs
one standard look for each supported design rather than cosmetic variants;
agent inventions may add new designs; inventions should mostly recombine
familiar world actions, with wholly new behavior rarer; and simple art is
acceptable across categories for that first complete game. Additional art
variety may be added afterward if computment wants to make it.

## Follow-up — world time and pauses (2026-09-24)

Computment chose to freeze the world when the game is closed; keep the world
running while map, World Info, and agent/conversation inspection panels are
open; reserve true pause for the pause button and Pause Menu; leave fast-forward
off the current roadmap; and keep other agents/world systems moving when one
model is slow or unavailable. The existing overall day pace feels roughly
right, but the current day/night split feels too daylight-heavy. An example
four-minute day plus three-minute night was offered, without yet deciding
whether the entire cycle should lengthen.

The older AgentWorld design-log direction for unattended simulation had
already been superseded by a later client-presence decision and implementation.
The supported integrated private-world host schedules one in-world minute per
real second (**nominally 24 real minutes per day**) and stops after the last
client's approximately five-second presence lease expires. The separate
fixture/kernel's six-ticks-per-second rate (**four real minutes per day**) and
the documented 2:40/1:20 daylight/night target should not be confused with
the playable private-world clock. No implemented daylight cutoff was found in
integrated source. This corrects Clanker's initial mistaken four-minute answer
in the same follow-up.

### Correction — the prototype's day length is not the design (2026-09-24)

After learning that the supported playable prototype has a nominal
24-real-minute day, computment explicitly rejected that as the finished-game
pace: they had never agreed to it and want faster days so reaching a year is
practical. This supersedes the earlier tentative statement that the existing
overall day pace felt roughly right, which had been made before the actual
prototype pace was clarified. No exact replacement duration was chosen. The
four-minute daylight plus three-minute night example remains illustrative,
not a seven-minute-day decision. Work backward from desired real playtime to
a calendar year, then choose day duration and day/night balance.

### Year pacing and biological aging (2026-09-24)

Computment chose approximately **two hours of unpaused play per in-world
calendar year** and tentatively agreed that agent biological aging tracks
calendar years. At 365 days/year this would mean about 20 real seconds per
day, which appears incompatible with the intended visible daily routine;
the number of days per year and real-time day length remain open. A shorter
game calendar is a proposal, not yet accepted. Exact life-stage durations
also remain open.

### Reopening pacing; regional weather concern (2026-09-24)

Computment reconsidered the two-hour-year target after seeing that a short
year also compresses days and seasons. They want time to feel like it advances
without days, seasons, and weather cycling too quickly. The two-hour target is
therefore no longer a decided value; the earlier 24-minute prototype day
remains rejected. They also spotted that rainfall cannot sensibly occur across
an entire world at once when it has different continents and climates, and
questioned whether weather should exist at all or occur only in appropriate
biomes. Regional, climate-biased weather is a proposal for discussion, not an
accepted decision. Distinguish a region's long-term rainfall/climate from
individual rain events; settle their duration and gameplay significance later.

### Familiar calendar, measurable costs, and regional weather (2026-09-24)

Computment prefers a **365-day calendar** over an invented short calendar and
is comfortable with a two-hour play session remaining within one season.
These are preferences, not exact day/season/year durations. They noted that
the right pacing and affordability are unknown before testing real model
choices, call rates, and population; they plan to use inexpensive models for
agent brains and Jev. Clanker should distinguish unknown exact cost from the
ability to measure representative model-call usage before finishing the whole
game. Computment accepted **simple regional weather** rather than global
synchronous rain and asked whether weather effects had been defined. The
interview had not yet agreed on specific effects; crop moisture, exposure,
travel/work, and visuals are candidate effects, not decisions.

### Six-hour lifespan thought experiment; weather effects accepted (2026-09-24)

Computment suggested, as an example to reason backward from, that an agent
would definitely die after at most six hours of unpaused play. It is not yet
clear that this is a final lifespan decision. With their preferred 365-day
calendar and a season of at least two hours, an agent would live at most three
seasons; if a season were exactly two hours, a day would be about 79 seconds.
With five-minute days, a season would exceed seven and a half hours, longer
than such an agent's life. Reproduction/maturation and whether agents should
experience birthdays/seasons must be resolved together. Computment separately
accepted Clanker's proposed simple regional weather effects: visual
conditions, rain affecting soil moisture/crops, gentle exposure effects that
make shelter/clothing useful, and mild outdoor work/travel effects in heavy
rain or snow. Extreme disasters and detailed weather physics are outside that
initial agreed scope.

### Calendar direction reversed around six-hour maximum life (2026-09-24)

Computment clarified that six hours of unpaused play is the current **maximum
agent lifespan** to design around, and now leans toward a **custom calendar**
rather than the previously preferred 365-day year. The 365-day preference is
superseded in the current decision view, not erased from history. Six hours is
a ceiling from birth, not necessarily each agent's exact death time; the exact
day, season, year, life-stage, fertility, and founder-age pacing remains open.
Clanker's illustrative option is six real minutes/day, ten days/season, four
seasons/year: 40 days and four real hours/year; six hours = 60 days, 1.5 years,
or six seasons. That example is not yet accepted. Earlier acceptance of a
two-hour session staying in one season was permission, not a hard requirement.

### Initial custom calendar accepted for playtesting (2026-09-24)

Computment accepted the illustrative calendar as a **starting point that can
change through playtesting**: one six-real-minute world day, ten days per
season, four seasons/40 days per year, yielding one real hour per season and
four real hours per year. With a six-hour maximum lifespan from birth, an
agent could live at most 60 world days, 1.5 years, or six seasons. Four
ten-day months can retain numeric date display. Exact daylight/night split,
life-stage milestones, and final pacing remain open. This acceptance
supersedes the prior history note saying the example had not been accepted.

### Child personal model; first age-up chart proposal (2026-09-24)

Computment confirmed that children should begin using their own personal LLM
when they become children, not while infants. This resolves the earlier
whether/when-model question at the stage level; the exact infant-to-child age
is not yet decided. Parents still select the provider/model at birth; Jev is
optional and cannot be required to make infant care function. Clanker proposed
for playtesting four stages with the six-minute day and six-hour maximum life:
infant days 0–<3 (0–18 real minutes, no personal model), child days 3–<15
(18–90 minutes, personal model begins), adult days 15–<45 (1.5–4.5 hours),
elder days 45–<60 (4.5–6 hours). These thresholds and any adolescent stage
are **not yet accepted**. Because adulthood comes before the first 40-day
calendar year, age display should show world days and stage rather than only
whole years; this is a proposal pending discussion.

### Age-up chart accepted for playtesting (2026-09-24)

Computment accepted the proposed four-stage age-up timings as reasonable for
playtesting: infant days 0–<3 (first 18 real minutes, no personal LLM), child
days 3–<15 (18–90 minutes, personal LLM starts), adult days 15–<45
(1.5–4.5 hours), elder days 45–<60 (4.5–6 hours). Earlier death remains
possible and 60 days/six real hours is a ceiling, not a fixed death time.
Stage-specific abilities, any adolescent substage, founder ages, and whether
to show age in days plus stage rather than only whole years remain open.

### Children's social agency and adult boundaries (2026-09-24)

Computment accepted that children with their own personal models are genuine
social characters: they can converse, play, learn, form friendships, and help
with simple age-appropriate tasks. Adult decisions such as land transactions
and parenthood wait until adulthood. The simulation must enforce those
permissions rather than relying only on model instructions. Exact chore
catalogues, additional child restrictions, elder effects, and any adolescent
phase remain open.

### Top-bar Event Log and located events (2026-09-24)

Computment accepted small notifications and an event log for important
out-of-view events such as births, deaths, inventions, and settlement founding,
while routine activities remain quiet. The **Event Log button belongs on the
top bar**; clicking it opens the log, and clicking a located event moves the
camera to its location. Only the pause control and Pause Menu pause the
simulation, so opening the log leaves time running. Exact notification
categories, priorities, filters, retention, and events without a location
remain open.

### Event pop-up controls (2026-09-24)

Computment accepted per-event-type controls for which important events show
pop-up notifications as a world grows. Turning off a pop-up leaves the event
in the complete Event Log. Default choices, filtering UI, retention, and
priority rules remain open.

### Audio scope (2026-09-24)

Computment accepted text presentation for conversations and clarified they
have no resources to make game audio. The first complete game therefore does
not require music, environmental sound effects, or generated agent voices;
being fully understandable without sound is intentional scope, not a missing
launch requirement. Audio may be reconsidered later, but no future audio work
is promised.

### Audio's limited value, not only limited resources (2026-09-24)

Computment clarified that sound in general would not add much to ClankerWorld.
This strengthens the no-audio direction from a production compromise to a
design preference. There is no active audio plan; text and visual feedback
carry the game's meaningful information.

### Post-death will and household fallback (2026-09-25)

Computment accepted the household as the default recipient of a deceased
agent's personal belongings, unless a will or developed inheritance rule says
otherwise. They clarified that the agent's own model should be informed **after
the agent dies** and then write a will/final inheritance instruction. This is
intended for the first complete finished game, not a post-launch add-on or a
will drafted only while alive. The final turn does not imply resumed physical
agency. Clanker proposed freezing the estate, validating asset transfers, and
using the household default if the model fails or produces nothing valid;
those exact mechanics still await confirmation. Shared household assets,
conflicting rules, minors, multiple heirs, debts, and agents with no household
remain open.

### Architecture and contradiction audit (2026-09-25)

Computment asked for real contradictions, untouched areas, and architecture
questions after accepting the post-death will/fallback flow as feeling right.
The current notebook now distinguishes five open tensions: limited founders
and kin restrictions may exhaust eligible mates without autonomous unrelated
arrivals; agent-made social/inheritance laws need separation from protected
ownership invariants; slow/pending model work and final wills must survive
concurrent ticks, saves, quit, and crashes; personal models plus social activity
need measured budgets despite no fictional population cap; and the finished
game's local/VPS/hybrid runtime placement is not chosen by today's prototype.
These are questions for the next interview, not newly accepted solutions.

### Player-placed agents, affiliation, and population cost (2026-09-25)

After the founder/kinship audit, computment briefly considered dropping the
close-relative pairing ban, then said “nvm”; the ban is not removed. They
proposed that the Add Agent placement flow show household-property and
settlement overlays. Placement on unclaimed land would make an independent
newcomer; placement inside a settlement would join that settlement; placement
on household-owned tiles would join the household. This is a preferred
direction pending details, not yet a final implemented rule. Clanker recommends
separating biological ancestry from social affiliation: newly player-added
adults can be unrelated to existing agents regardless of where they are
placed. Placement should establish initial membership, not continually
recompute membership from movement. Computment emphasized wanting player
control over population/provider expense. Still open: whether unrelated adults
ever arrive autonomously, how automatic births/continuity interact with a
budget or cap, and how placement handles overlaps, consent, and capacity.

### Starter founders before land claims (2026-09-25)

Computment noticed that a pure placement-by-owned-tile rule would leave every
agent spawned at the start unaffiliated if no tile has yet been claimed. The
current decision already calls for four **biologically unrelated** founders
and two starter houses/family lines, but it has not defined how those founders
join households before property claims exist. Biological kinship, household
membership, and land ownership must be kept distinct. Possible solutions are
preassigning two starter households independent of land claims, or assigning
initial ownership to starter-house footprints; neither was accepted yet.

### Starter households approved (2026-09-25)

Computment accepted four biologically unrelated founders grouped 2+2 into
two starter households, with two houses but no formal land claims or forced
couples. Household membership is a social starting arrangement separate from
ancestry and land ownership. Family lines can develop through later
relationships and children. This resolves the previous bootstrap question;
the proposed placement overlay for later player-added agents and automatic
population/cost rules remain open.

### Configurable automatic-birth limit (2026-09-25)

Computment accepted a configurable population limit on **automatic births**
to control growth and model expense. It is an explicit player setting, not a
fixed biological limit inside the fiction. The exact default, whether it can
be disabled, whether manual Add Agent may exceed it, what counts toward the
limit, and which rule wins when it conflicts with the previously accepted
low-population continuity safeguard remain open. Clanker's separate proposal
for a usage meter and spending limit has not yet been answered.

### Automatic-birth limit reopened for playtesting (2026-09-25)

Computment reconsidered the just-accepted configurable limit and said they
may not want one. They will decide **after playtesting** how often births
actually occur and how population/model costs evolve. The current design
therefore has **no accepted automatic-birth cap**; any cap, default, override,
and precedence against the low-population safeguard remain deferred. The
earlier acceptance above remains as history, not the current decision.

### Development VPS versus finished local install (2026-09-25)

Computment clarified that the current Godot-client/private-VPS-host arrangement
is valuable for development and their own playtesting because Clanker can see
the running simulation, inspect logs, debug, and tune it. They want to keep
that flow for now. The first finished game for other players should be a simple
install whose game and simulation run on the player's own PC, not a game that
requires access to computment's VPS. Clanker verified that today's supported
client is a Windows Godot desktop build, not a browser UI; the private .NET
host owns simulation, saves, cognition and provider adapters. The target is
one authoritative simulation across both deployments. Exact local packaging,
OS support, credential storage, and migration remain open. Cloud LLM choices
would still require internet access and their own provider setup.

### First finished release operating-system target (2026-09-25)

Computment chose **Windows only** for the first finished release. They can
test on a Windows laptop but have no Linux or macOS desktop machine available
for game testing; a Mac mini running a Linux server OS does not fill that gap.
Linux/macOS support may come later with contributors who can develop and test
those platforms. No promise of cross-platform support is implied.

### Player-paid model providers (2026-09-25)

Computment explicitly decided that other players supply their own API keys
and pay their own model-provider charges. ClankerWorld does not provide
developer-funded model access. First-run credential setup and the experience
without a configured model remain open.

### No first-launch credential gate; per-agent credentials (2026-09-25)

Computment corrected Clanker's proposed first-launch key wizard. A player can
open the game without an API key. The already-agreed Add Agent flow chooses a
provider and model and lets the player enter a key then or select a previously
stored one. More than one key may be stored for the same provider; the agent
uses the selected credential. The distinction between keyless game launch and
creating a new world with four model-driven founders still needs resolution.

### Empty base camp and in-world founder setup (2026-09-25)

Computment resolved the keyless New World edge case: world generation creates
the terrain and base camp **without agents**, then opens that world. The player
adds/configures four founder agents in-world through the ordinary per-agent
provider/model/credential flow. Simulation does not begin until all four are
added. This supersedes earlier wording that the world *begins with* four
precreated agents. The four unrelated founders still form two starter
households of two each; precise assignment UI and whether time begins
automatically after the fourth agent remain to be designed.

### Explicit world start and selected-agent info popup (2026-09-25)

Computment accepted an explicit **Start World** action after the four founder
agents have been added; the fourth placement must not silently start time.
They also accepted saving an incomplete founder setup for later resumption.
Computment specified that selecting an agent should bring up an agent info
popup near that agent, where the player can change that agent's provider and
model. Credential choice follows the established per-agent provider flow.
The popup's full contents and its behavior when a model call is in progress
remain open.

### Agent popup core information and thoughts (2026-09-25)

Computment accepted Clanker's proposed popup core: name, age/life stage,
household, current activity and needs, with expandable inventory,
relationships and model settings. They also want the agent's thoughts in the
popup. The current interpretation is an inspectable latest thought/intention;
exact wording, history and presentation remain open. Clanker proposed a
timestamped, in-character thought supplied with an ordinary model decision,
not a separate continuous model-call stream or purported raw hidden reasoning.

### Recent thought history in agent popup (2026-09-25)

Computment chose a small scrollable history of recent thoughts in each
selected agent's info popup, not only the most recent thought. Keep the history
bounded and save it with that world; the exact retention count and wording
remain to be tuned.

### Thoughts are private to the agent, inspectable by the player (2026-09-25)

Computment clarified that the thoughts in an agent's info popup are **private
thoughts**, not a second form of public conversation. Spoken dialogue is what
another agent can hear; a private thought is visible to the player as an
observer but is not automatically shared with other agents or placed in their
model context. An agent can of course choose to say something they had been
thinking. Clanker's earlier term “public thoughts” was imprecise.

### Forgetting, memory, and Jev-assisted compaction (2026-09-25)

Computment accepted that agents can forget or misremember minor details while
major relationships, life events, skills and commitments remain dependable.
They asked whether Jev could help with this, especially compaction. Clanker's
recommended direction is yes when Jev is enabled: Jev may summarize many
experiences into shorter agent-specific memories and retrieve relevant ones.
It must not turn a rumor into an objective fact, leak another agent's private
memories, or replace the personal LLM's decisions. Because Jev is optional per
world, memory must still work without it. Exact triggers, storage, and fallback
compaction method remain open.

### Agent memory inspection (2026-09-25)

Computment accepted a **Memories** section in the selected agent's info popup,
distinct from the recent private Thoughts view. It shows that agent's
remembered experiences and beliefs rather than the objective world event log;
some recollections can be incomplete or mistaken. Exact organization and
source/uncertainty labels remain open.

### Interactive family tree and deceased agent profiles (2026-09-25)

Computment decided that dead agents remain inspectable. From an agent popup,
the player can open a larger graphical, interactive family tree; clicking any
person in it opens their agent popup, including dead relatives. A deceased
agent's popup is a historical profile with archived private thoughts and
memories, not a source of fresh ordinary thoughts or editable live model
settings. The previously decided exceptional post-death final-will turn
remains separate. Exact tree relationship lines (biological parents, partners,
guardians/households) and navigation are not yet settled. Keeping the dead
agent's record for player inspection does not by itself establish what living
agents know or inherit from it.

### Family tree is not household membership (2026-09-25)

Computment agreed that sharing a house does not make people family. The
interactive tree should show parent-child ancestry and partnerships, while
household membership is a separate label or layer. In particular, the four
unrelated founders grouped into two starter households must not appear as
biological relatives merely because they live together.

### No automatic memory inheritance (2026-09-25)

Computment answered the concrete example: if a parent hid a valuable tool and
never told the child or wrote down its location, the child remains unaware
after the parent's death. The player can still inspect the dead parent's
archived memories, but descendants receive no automatic copy. Knowledge must
pass through an in-world act such as telling, teaching, writing, witnessing,
or later discovery.

### Breakable settlement laws versus engine rules (2026-09-26)

Computment accepted the distinction: agent-created settlement laws are social
rules that agents may violate. A town prohibition on cutting trees need not
physically prevent chopping; an illegal act can happen and lead to discovery,
dispute, or punishment. The authoritative simulation still protects physical
facts and validated ownership changes, so a model cannot simply declare a
rule that creates goods or grants it someone else's property. Exact law-making
and enforcement institutions remain open.

### Simple founding settlement council (2026-09-26)

Computment accepted a simple council as the starting form of government for
new settlements, rather than automatically giving one founder sole rule.
Adult members can propose and vote on laws, and settlements may develop
different arrangements later. Exact council membership, election/selection,
vote threshold, and procedure remain open.

### Full adult council, then elections around eight adults (2026-09-26)

Computment decided every adult resident initially participates in the
settlement council. They proposed starting representative elections once the
adult population doubles from the four-founder baseline. Clanker recommends
using **eight adult residents in that settlement**, not eight adults across the
whole world, as the first playtest threshold; then all adults vote for a
smaller council. Council size, terms, residency, and election procedure remain
open, and the numeric threshold can change after playtesting.

### Remove obsolete phase docs; quit during an AI call (2026-09-26)

Computment wants the upcoming repository cleanup to remove obsolete
phase-by-phase implementation docs rather than preserve all of them in an
active or archival doc tree; Git already retains their history. Necessary
unique contracts still need checking before consolidation, and links/tests
must be repaired when files move or disappear.

Computment also accepted prompt save/exit if an agent's provider call is
unfinished. The answer is cancelled or discarded with no partial world action.
After reopening, the agent reconsiders the unresolved decision if it still
matters. A second provider call may cost money even if the abandoned call was
already billed. Exact autosave/manual-pause behavior for in-flight work is
separate and still open.

### Provider failure never silently changes an agent's model (2026-09-26)

Computment accepted Clanker's recommendation: if an agent's chosen provider
fails or its API key runs out of credit, the game does not automatically
substitute another paid model. Other agents and the world continue. The
affected agent may perform safe routine activity but not fabricated important
choices; the player receives a visible problem report and can repair the key
or deliberately change that agent's provider/model. Jev does not impersonate
the agent's personal LLM.

### Optional AI-usage limit and settings split proposed (2026-09-26)

Computment accepted an optional AI-usage limit: a visible meter, and an
automatic world pause asking before further paid model calls once a player-set
limit is reached. This is not a cap on agent births or population. Exact units,
reset period and billing accuracy remain open. Computment proposed dividing
the in-world Pause Menu into **Game Settings** and **World Settings**. Clanker's
recommended mapping is installation-wide display/UI and provider credentials
under Game Settings, and per-world autosave, Jev configuration and AI usage
under World Settings. This menu split/mapping is preferred pending
confirmation, not yet a settled exact UI specification.

### Jev can be switched during an existing world (2026-09-26)

Computment decided Jev may be turned on or off after the world has been
created, through the in-world settings flow. Jev remains a per-world optional
helper, not a per-agent replacement model. Turning it off must not erase
memories or halt the basic world; exact handling of an in-flight Jev request
at the switch remains open.

### Human-made external mods use the same governed pipeline (2026-09-26)

Computment accepted human/community-made mods in the first complete game but
asked how they would work. The current design direction is an explicitly
imported package for a chosen world, processed by the same validation and
restricted script sandbox as agent-made creations. No imported package gets
trusted access to provider keys, arbitrary files or the network. Agent-made
creations may activate after validation without routine human approval;
external packages require the player's intentional import. Package format,
dependency/conflict UI and distribution mechanism remain open.

## World-generation interview — continent controls and camp placement (2026-09-27)

The questions and answers below preserve this round's exact scope. The
[current vision ledger](../game-design/README.md) is authoritative if later
answers revise it.

1. **Question:** If a player chooses **2 continents**, should the preview
   guarantee two substantial, ocean-separated landmasses, or treat 2 as a loose
   target that can produce land bridges and extra islands?
   **Answer:** “the latter or well chance of both right?” The count is a loose
   target; both clearly separated and connected layouts may occur. Exact
   frequencies and what qualifies as an incidental island remain open.
2. **Question:** Should continent count be adjustable on **every world size**,
   or only on Large/Huge/Mega? What should Small and Medium usually look like?
   **Answer:** Only Large, Huge, and Mega offer the count control. Small and
   Medium should always be one “continent.” Whether they may have incidental
   islands was not specified.
3. **Question:** Where should the **starter camp** go: automatically in a
   survivable spot, chosen by the player from the preview, or automatically
   placed with an option to move it?
   **Answer:** The player chooses its location, in the same spirit as placing
   the four founder agents before starting the simulation: “you have to place
   the base camp too.” Whether the placement uses the preview or the opened
   world, its order relative to founder placement, and terrain/survival
   constraints remain to be specified.

### Camp-placement follow-up (2026-09-27)

1. **Question:** Should the player choose the camp location **in the New World
   preview** or **after opening the generated world**, like founder placement?
   **Answer:** After opening the generated world.
2. **Question:** Should the game **block unsuitable sites** (too little room,
   inaccessible resources), or allow them with a warning?
   **Answer:** Allow them with a warning. The distinction between poor but
   physically placeable sites and impossible building footprints remains open.
3. **Question:** Is placing the camp **one action that lays out its buildings
   and supplies**, or should the player place its houses and other parts
   individually?
   **Answer:** “should probably be the latter. what are all the parts again?”
   Individual placement is a preference, not yet a final decision. The current
   planned kit is two houses, shared storehouse, fire/cooking area, workshop,
   connecting path, nearby fertile land, and starter food/seeds/hand
   tools/clothing; which of these the player places directly remains open.

### Camp parts confirmed; storehouse ownership questioned (2026-09-27)

Computment answered yes to the follow-up proposal: the player places the two
houses, storehouse, fire/cooking area, and workshop individually, while the game
connects a path and stocks starter supplies. This supersedes the preceding
tentative “probably” on individual placement. They asked whether the shared
storehouse is only for the settlement. That ownership/access question is not
yet decided: the accepted camp has two starter households before any formal
settlement, while the present prototype tracks starter stock by household even
though there is a common storage site. Neither the old “shared” label nor the
prototype's inventory model should silently settle the finished-game rule.

### Starter storehouse common pool and settlement access (2026-09-27)

In answer to whether the starter storehouse should hold one common pool or
separate household-owned supplies in the same building, computment chose
**one common pool**. They added: “when a settlement does exist if the
storehouse is in it then only agents who live in that settlement can use it.”
This resolves the earlier access question for a storehouse inside an existing
settlement. It does not decide formal ownership, the definition of residency,
access outside settlement borders, border changes, or unauthorized taking.

### Nonresident storehouse access is physically blocked (2026-09-27)

Asked whether outsiders should be mechanically blocked from a storehouse
inside a settlement or able to steal from it and face consequences, computment
chose **physical blocking**. They noted that implementing stealing correctly
would be complicated. This resolves unauthorized use of this storehouse; it
does not cancel the broader possibility of crime or other kinds of theft in
the finished game. Formal ownership, residency criteria, and the access rule
outside settlement borders remain open.

### Camp placement undo and proximity (2026-09-27)

1. **Question:** Before **Start World**, can the player move or undo a camp
   structure already placed?
   **Answer:** Yes, and the same applies to founder agents.
2. **Question:** Must the four founders be placed near camp, or can they start
   anywhere on the map?
   **Answer:** Near camp. Computment suggested a circular limit and asked to
   work out its size. They also require a separation limit for camp buildings
   so the components cannot be placed at opposite ends of the world. Exact
   center, geometry and distances were not chosen. Clanker then proposed an
   initial 8-tile radius for structures and 12 tiles for founders, using the
   current prototype's 6×5-tile auto-camp only as a scale reference; those
   numbers are unapproved candidates.

### Camp radius and center accepted for playtesting (2026-09-27)

Computment accepted Clanker's initial suggestion: use the fire/cooking area as
the center of circular placement limits, with other camp structures within an
8-logical-tile radius and founder agents within a 12-tile radius. These are
starting values for playtesting, not immutable balance constants. Whether a
geometrically nearby position also needs a walkable route, and exact handling
of multi-tile footprints at the boundary, remain open.

### Roads as an emergent world system; diagonal movement (2026-09-27)

Asked whether roads should include both worn trails and built roads or only
deliberately built roads, computment said they think there should be **one road
type**, probably formed by repeated walking. They want a limit so roads do not
appear everywhere and asked how that logic could work. In answer to who builds
roads, they chose the **game/world system itself**: agents and player should
have no say in road placement. They also want agents to move diagonally and
roads to run diagonally. One-type/repeated-walking details remain a preferred
direction rather than a finalized algorithm. The current prototype's route
finder uses only four cardinal neighbors, so this is not playable behavior yet.

Clanker proposed accumulating traffic from repeated journeys on neighboring
tile connections, including diagonals; forming a road only on sustained,
locally dominant connected routes; fading unused segments; and using only a
modest road preference in route-finding. This is an unaccepted proposal, not a
decision on thresholds, road lifetime, materials or visual connections.

### Playtest UI and town-layout reconsideration (2026-09-27)

While playtesting a generated world, computment reported that Main Menu
Settings wrongly offered World Settings and entering it switched to the world.
They want World Settings available only with a loaded world. They disliked the
black terrain grid lines but liked square tiles. They suggested a suitability
color/overlay throughout paused camp-component placement, starting before the
first placement, and selecting a tile for biome, terrain, object, weather,
temperature, fertility, elevation and similar information. They confirmed a
baseline of one road type; unused roads should fade through visual stages,
while additional road/building art variants wait until the whole game has a
complete texture pass. Agents may have a few visual variants.

Computment explicitly does not need development playtest saves kept compatible
as world generation changes; they expect to create new worlds to try the flow.
This does not authorize deleting an active save. They also supplied a town
generation discussion, TerraForge's `townforge.py`, and a short 2D map PDF,
asking whether a world system could lay out a town incrementally while agents
still decide to build and gather materials. This **reopens** the earlier
agent-selects-exact-site assumption; it is a question, not an accepted new
algorithm or authorization to implement one.

During the same playtest, computment reported that hovering a founder or other
map content shows a tooltip only briefly, likely interrupted by the tick, and
that the warmth/condition section quickly disappears after opening an agent
popup. These are observed client defects, not design choices; the bug register
tracks the repository repair and pending laptop verification.

### Additional playtest triage and renewed interview (2026-09-27)

Computment reported that manual saves should be overwriteable; agent-chosen
names seem too similar; the Pause Menu is ugly as three rows and should be one
row; resolution selection changes window size rather than apparent game
resolution; the Pause Menu should have one `Settings` entry with Game and World
choices inside; the purpose of `Create` is unclear and they questioned whether
it was ever in their vision; agent hitboxes bleed outside their tiles; forests
and foliage are not visible as generated objects; and mountains/peaks were not
seen. They asked for bug/issue triage and another design interview, not
immediate implementation of every item.

The current ledger treats overwrite capability and the one-row/single-Settings
menu as decided direction, with overwrite interaction and the `Create`
workbench's future still open. Code inspection finds a building-design owner
workbench behind `Create`, fixed 1280×720 viewport plus window-size-only
resolution choices, large actor buttons that can overflow zoomed-out tiles,
forest as a ground kind/color rather than tree art, and threshold-based
mountain/peak terrain. The particular playtest world's mountain distribution
and the exact reason for similar model-generated names remain unverified.

Computment then added that a compatibility warning for existing saves in
**Load World** would be nice. This is a preferred player-facing warning, not a
decision that old development saves must remain loadable. The current world
catalog has no per-world compatibility verdict; it lists name and update time.
Whether warning-only, migration, or blocking applies to incompatible saves is
unresolved.
