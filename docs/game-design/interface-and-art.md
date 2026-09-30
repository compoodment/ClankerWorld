---
title: The interface, art and audio
type: game-design
status: active
updated: 2026-09-30
---

# The interface, art and audio

These are the intended game rules and choices. They do not describe
everything that is available in the current build. See [what works today](../what-works.md).

[Game-design guide](README.md) explains the agreement labels.

## On this page

- [Main Menu, world view, and controls](#main-menu-world-view-and-controls)
- [Pixel art and generated images](#pixel-art-and-generated-images)
- [Audio and dialogue presentation](#audio-and-dialogue-presentation)

## Main Menu, world view, and controls

### Agreed

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
- **Agreed after reviewing the tile-inspection prototype:** selecting ground
  should open an inspection panel with meaningful, facts recorded by the game.
  Keep terrain kind separate from generated climate, elevation, water layout,
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
  How far the zoom-out limit should go, and when it is tuned, is under
  [Leaning toward](#leaning-toward) below.
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
- **Agreed after the September 29 style review:** the Event Log button shows
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
  world information. Filters should reveal established Town land claims,
  household use areas and property, Town borders, and similar world facts.
  The UI must not invent ownership or borders that agents have not established.
- **Agreed on September 30:** World Info lists discovered capabilities from
  recorded discoveries only. When nothing has been discovered it says
  **No recorded discoveries**, rather than listing every built-in recipe as
  known. Opening World Info never gives any agent knowledge. Experimental or
  unvalidated proposals are shown separately from usable discoveries.
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
- **Agreed after the September 29 style review:** the interface uses the
  **Timber & Parchment** look chosen from three mockup directions: wooden
  frames around parchment panels, ink text, bevelled pixel buttons and a green
  main action. Game Settings offers a **Theme** of **Light**, **Dark** (dark
  wood with cream text) or **Match system**. The exact icon set and remaining
  accessibility treatment stay open.
- **Agreed after the September 29 background review:** the Main Menu
  background is a side-view pixel-art valley chosen from three mockup
  directions: mountains, patchwork fields, a river and small Towns built from
  the game's own building types. It animates gently (drifting clouds, chimney
  smoke, birds, river sparkle). The Light theme shows a clear morning and the
  Dark theme a dusk with lit windows, lanterns, stars and fireflies.
- **Agreed after the September 29 logo review:** the Main Menu shows a logo
  and no slogan. Chosen from five options, it has wood-grain letters (without
  angled 3D) either side of a friendly robot waving in front of a small
  planet; the robot and planet also serve as the app icon. A status line
  appears only when something needs attention; pairing is expected to be a
  development-only step once the game hosts worlds on the player's own PC.
- **Agreed after the September 30 font review:** text uses pixel fonts,
  chosen from six options shown on real game screens. Body text is **Fusion
  Pixel 12px**. Titles, panel headings, section labels, dialog titles and the
  Main Menu's choices use **Timber**: thin capitals drawn from the logo's
  letter shapes. Base text sizes are whole multiples of 12 px and are drawn
  without smoothing, and UI Scale uses only whole-number steps, so letters
  stay crisp. computment prefers this look over a smoother font chosen for
  easy reading.
- **Agreed after the September 30 playtest:** UI Scale magnifies the whole
  interface in whole-number steps (100%, 200%, 300%, 400%) instead of
  enlarging text alone. Panels, padding, buttons, icons, confirmations,
  drop-down lists, tooltips and names on the map grow together, while the map
  keeps its own zoom. Top-bar panels open under the button that opened them,
  except the Event Log, which sits at the right edge of the screen, and the
  mouse wheel over a panel never zooms the map behind it. The current
  default, **Automatic**, keeps the interface about 720 pixels tall: 200% at
  1080p and 1440p and 300% at 4K. computment found 100% far too small at
  1440p. A step that would leave less than 960 × 540 interface pixels is
  unavailable, and on a narrow layout the top bar shows icons only.
- **Agreed on September 30:** panels and confirmation dialogs fit what they
  hold rather than keeping a fixed size with empty space. A panel with long
  text stays on screen and scrolls that text; a long dialog message wraps.
- **Agreed on September 30:** buttons are consistent across the whole game.
  Every panel closes with one small square button showing a pixel ×. A screen
  reached from another shows a pixel back chevron in that same place, instead
  of a separate Back button. The Main Menu and Pause Menu use the same choice
  buttons, other buttons share one height, and confirming something that
  cannot be undone uses a red button. The hover readout names what is under
  the pointer without map coordinates.
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
- **Agreed on September 30:** before the player confirms, Add Agent shows which
  household and which Town the new agent will belong to. One shared rule
  decides this, checked in this order: household property first, then
  unclaimed land inside a Town, then land outside both. The preview is not a
  reservation: when the player confirms, the game checks the same map again and
  tells the player if the placement changed. If claims or Towns overlap so that
  the answer is ambiguous, the placement is refused and the game explains why.
  It is never decided by list order or distance. How claims and use rights
  work is in
  [Town land and household use rights](towns.md#town-land-and-household-use-rights).
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
  believes happened, including mistaken beliefs; it is different from the game's
  record of actual world events. Inventory, relationships, and model settings can expand
  from the same popup. A **Family Tree** action opens a larger interactive
  graphical view; selecting a person in the tree opens that person's agent
  info popup, including for deceased relatives. The tree distinguishes
  parent-child ancestry and partnerships; household membership is displayed
  separately, never as proof of biological family. Unrelated starter
  housemates must not be drawn as relatives.

### Leaning toward

Provisionally, the zoom-out floor stays as it is until Large worlds have been
measured, and is then tuned. Computment suggested Small/Medium zoom-out around
70% of map-fit scale and a shared performance cap for Large/Huge/Mega; that
exact percentage and visible-tile budget remain preferred starting points for
tuning, not fixed finished-game numbers.

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

In the **Add Agent** placement view, show exclusive household use areas and
Town borders so computment can see the new agent's initial affiliation.
Within the agreed placement order above, placement on a tile assigned
exclusively to one household **forces the new agent into that household**
(and its enclosing Town, if any); this player setup action does not seek
household consent. Placement elsewhere within a
Town joins the Town but no household; placement on unclaimed land
outside both starts an independent agent. Location establishes **starting
social membership**, not biological ancestry or permanent membership based on
where the agent later walks. A player-added adult could be a new unrelated
family line even when placed inside an existing household. Other
invalid-placement rules and later voluntary household changes remain open.
This forced Add Agent membership is distinct from inviting a nonmember to
visit a House. The agreed first-Town household setup below is separate from
this later Add Agent placement rule.

### Still to decide

Exact top-bar layout on small screens; the final zoom-out/visible-tile cap,
which waits for measurements of Large worlds; which overview and filter layers
ship first; display of disputed or overlapping claims; the precise event
categories, filter UI, event retention, and handling of events without a single map location; custom
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

## Pixel art and generated images

### Agreed

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

### Suggestion

Use editable `.aseprite` or layered PNG sources where useful; export PNG
spritesheets/atlases plus metadata for asset ID, footprint, anchor, layer,
collision, frames/timing, style/biome, creator, rights, and version. Build a small
visual reference scene before locking a palette or mass-producing textures.
For autonomous inventions, first try approved component assembly/recoloring;
optionally request new AI imagery under player-controlled cost limits. Normalize
it to the pixel grid, check format, source and performance rules, preview it,
and use a legible fallback sprite if generation fails. Aesthetically odd art
should not automatically erase a mechanically valid invention.

Use a small set of supported visual families for built-in content, then add a
new family when an invention truly needs a new silhouette. Reusing an image
need not mean two creations behave identically. The exact reuse/generation
method remains Clanker's proposal, not an accepted implementation rule.

### Still to decide

Exact palette and style guide; animation standards;
AI-generation provider and spending controls; how much visual cleanup can be
automated; quality criteria; asset size budgets; and the final art/content
metadata contract. Also open: the minimum distinct designs each category
needs, and how an agent invention technically expands the supported visual
vocabulary. The first complete game does **not** require extra cosmetic
variants just for variety. Existing code has partial PNG validation, **not**
the full finished-game autonomous art pipeline.

## Audio and dialogue presentation

### Agreed for the first complete game

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
