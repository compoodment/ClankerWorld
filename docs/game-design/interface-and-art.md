---
title: The interface, art and audio
type: game-design
status: active
updated: 2026-10-02
---

# The interface, art and audio

These are the intended game rules and choices. They do not describe
everything that is available in the current build. See [what works today](../what-works.md).

[Game-design guide](README.md) explains the agreement labels.

## On this page

- [Main Menu, world view, and controls](#main-menu-world-view-and-controls)
- [Player guidance and orders](#player-guidance-and-orders)
- [Pixel art and generated images](#pixel-art-and-generated-images)
- [Audio and dialogue presentation](#audio-and-dialogue-presentation)

## Main Menu, world view, and controls

### Agreed

- Launch into the **Main Menu** with **New World**, **Load World** when worlds
  exist, **Settings**, **Mod Library**, **Quit Game**, and a separate **Continue** action for the
  most recently played world.
- **New World** offers world size and climate choices, advanced generation
  controls, preview/reroll and wrapping. **Agreed on October 1
  ([#648](https://github.com/compoodment/ClankerWorld/issues/648)):** it offers
  no survival grace period or extra starting supplies for now; a gentler start
  may be added once the default game is finished. The ordinary game has **one
  supported first-Town setup mode**. The nothing-start challenge mode was
  removed from the plan for now. Generating the world creates the map and opens
  it paused; four starting agents must be configured before starting time. The
  first-Town siting and generated building layout flow are in
  [Agents and social life](agents-and-families.md#agreed-starter-town-and-remaining-choices).
  API keys are not a prerequisite for reaching the world view.
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
- **Agreed on October 1
  ([#640](https://github.com/compoodment/ClankerWorld/issues/640)):** the date
  uses season names by default, such as **Autumn 2, Year 1 · 14:20**, because
  each 10-day month is one season: Spring, Summer, Autumn and Winter. Game
  Settings also offers numeric dates (DD-MM-YYYY, MM-DD-YYYY or YYYY-MM-DD) for
  players who prefer them, and keeps the 24-hour or 12-hour time choice. Agent
  ages show as life stage plus days, such as **Adult · 22 days**.
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
  world information. Separate map filters show Town title, household land use,
  disputed land, household property and Town borders. The UI must not invent
  ownership or borders that agents have not established.
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
- **Agreed after the October 1 playtest** (replacing the UI Scale choices
  above): **there is no UI Scale setting.** The interface grows with the
  screen in whole steps, so pixel letters always stay crisp: 100% on small
  screens, 200% at 1080p and 1440p and 300% at 4K, one step lower whenever
  the menus would have less than 960 × 540 interface pixels. computment
  tried a 150% size and found its pixel letters too uneven, and found 100%
  too small and anything above 200% unnecessary at 1440p. **Render
  Resolution is removed**: the game always draws at the screen's own
  resolution. Settings is titled simply **Settings**, with its sections in
  their own boxes.
- **Agreed on October 1:** agents inside a building are not drawn shrunk onto
  its tile. They are hidden from the map while inside, the building shows a
  small badge with how many people are in it, and its card names them.
- **Agreed on October 1:** Developer tools leave the pause menu for their own
  panel on a key, and become genuinely useful: for example tile coordinates,
  a performance readout, jumping to any agent and showing an agent's planned
  path.
- **The first Developer tools, agreed on October 1
  ([#650](https://github.com/compoodment/ClankerWorld/issues/650)):** **F12**
  opens the panel. It keeps today's tools (life-speed override, recovery for a
  lost model reply, paused world editing and paired-device management) and adds
  tile coordinates and facts, a performance readout, jumping to any agent and
  showing an agent's planned path. It also offers **direct edits**: set an
  agent's needs, give or remove goods, add or remove a skill, and start or end
  a relationship. Every direct edit is written to the Event Log as a developer
  edit, so playtest results are not mixed up with normal play. Time tools, such
  as stepping one tick, are not in the first set.
- **Agreed after the October 2 panel review:** computment accepted redesigns
  of the panels that had not been touched yet, shown as before-and-after game
  screenshots, with these changes of their own:
  - The selected-tile card is headed by the ground (Meadow, Forest) with a
    picture of the tile. **Climate** is its own labelled row, so it cannot be
    read as describing the whole world.
  - The Agents list shows a portrait row per agent with **Hungry**, **Cold**
    and **Ill** tags. The Event Log has an icon per kind of event, a heading
    per day and a **Find** button kept clear of the scrollbar. The controls
    list draws keys as keycaps with the letter centred on the key.
  - World Info's World page is a "today" card and a grid of counts with icons.
    Its Towns page shows residents' portraits, stores as item slots and
    projects with progress bars, without the note about Town borders.
  - Memories has tabs and a card per entry with a five-step sureness meter.
    The Family Tree uses small portrait boxes and opens beside the Profile.
  - The Profile's model line names the model's provider plainly instead of
    "chosen by". Model choices are labelled **Provider**, **API key** and
    **Model**, and an agent's model settings link to the main model settings
    instead of offering to delete a key. Add an agent says **Click on land to
    place them**.
  - The Mod Library lists mods as cards with a status tag. Confirmation
    dialogs centre their text and have a framed close button inside the frame.
    Status messages show a tick or a warning sign.
  - Load Save draws a world's save branches as a large timeline with the
    chosen save described underneath and the list behind a **Timeline / List**
    switch (option C of three, for #680). After two more rounds of drawings,
    computment chose this look:
    - Each branch is a thick line in its own colour, with a numbered badge and
      its save count on the left. A new branch bends down from the save it
      grew from.
    - A thin season bar runs across the top, with a tick per day, each
      season's icon and name ("Spring · year 2") and day numbers where there
      is room. A faint wash of each season's colour sits behind the lanes.
    - Older saves are open points with a dot of the branch's colour, and
      autosaves are small diamonds. Each branch's newest save is a solid point
      under a small hanging banner in the branch's colour.
    - Save names sit in small tags below the line, or above it when the space
      below is taken. The chosen save's tag turns orange, with corner marks
      around its point.
    - **You are here** is an orange camp marker at the end of a dotted line.
    - The row above the timeline holds a key to the points (newest on its
      branch, save, autosave) beside the switch, instead of a line of counts.
    - **Agreed later on October 2:** Save World shows the same timeline under
      its **New save** box, with the same switch. With no save chosen, the
      card underneath says where the new save goes: on along a branch, or
      starting a new one. Choosing a save offers **Overwrite**. Autosaves are
      drawn but can't be chosen there, because they can't be overwritten.
- **Agreed on September 30:** panels and confirmation dialogs fit what they
  hold rather than keeping a fixed size with empty space. A panel with long
  text stays on screen and scrolls that text; a long dialog message wraps.
- **Agreed after the September 30 agent card review:** computment chose the
  two-step card (option D of four mockups). Selecting an agent opens a small
  **quick card** beside them with their name, what they are doing, bars for
  fullness, warmth and illness, and **Profile** and **Speak** buttons.
  **Profile** docks a larger panel on the left of the screen: portrait, a
  pencil to rename, age, diet and the other bars, belongings, work and
  learning, private thoughts, people, **Memories**, **Family** and **Model**,
  and a message box with a **Suggest** or **Order** choice. Clicking the
  thoughts, or **Read all**, opens every recent thought in a larger reader
  beside the Profile, grouped by day; Memories opens beside it too. The
  Profile's back button returns to the quick card; a deceased agent's
  historical Profile opens directly and simply closes. This refines the
  selected-agent popup described further down this list.
- **Agreed on September 30:** buttons are consistent across the whole game.
  Every panel closes with one small square button showing a pixel ×. A screen
  reached from another shows a pixel back chevron in that same place, instead
  of a separate Back button. The Main Menu and Pause Menu use the same choice
  buttons, other buttons share one height, and confirming something that
  cannot be undone uses a red button. The hover readout names what is under
  the pointer without map coordinates.
- **Agreed after the September 30 nameplate review:** the map shows an agent's
  name only while that agent is selected or hovered, as light pixel letters
  with a dark edge and no box; the selected agent's name is gold. Names carry
  no activity symbol. Buildings show no names on the map; hovering a building
  still names it. computment chose the outlined-text look (option C) with the
  only-when-needed rule (option E) from the nameplate mockups.
- **Agreed after the September 30 building panel review:** selecting a
  building opens a small **quick card** beside it, like an agent's: its roof,
  name and owner (household or Town), one line on what is happening there (who
  is inside, or who is making what, with a progress bar and time left), what
  it stores as item icons with the count in the corner, and a **Details**
  button. **Details** docks a larger panel on the left, like an agent's
  Profile: owner, who may use it, when it was built and which side its door
  faces; storage as a larger icon grid with each item's name; work in progress
  with what it uses; and who lives there or is inside. A list of recent
  storage changes and what a workstation can make are wanted later. A
  storage-space bar waits until buildings record a storage limit. When the
  owner is paired, Details also offers signed building removal and owner-change
  actions. A confirmation explains that removal keeps the Town border and
  Roads; the host refuses a change while stock, deliveries or active work
  remain. Moving a private building between households keeps its Town title and
  assignment; moving a Warehouse changes its recorded Town assignment but not
  either border. Refusals appear in plain language. Hovering a building still
  names it.
- **Agreed on September 30:** items have their own pixel-art icons, drawn by
  hand on a 16-pixel grid in a few shades with a complete dark outline and
  shown only at whole-number sizes so they stay crisp. An item without its own
  icon yet shows a plain crate, so new items are still counted.
- **Agreed on September 30:** map Filters start off, Town borders included.
  Town borders show as a pale dashed line along the border's edge (option B of
  four looks). Add Agent placement still shows Town borders and household
  property while placing, without switching the Filters on.
- **Agreed on October 1:** stripe land with competing household claims, and
  clicking a tile lists each household's claim.
- Following the Filters above, Town title and household use rights show as
  separate overlays; the tile list also names the Town title, and Add Agent
  placement shows these records.
- Adding an adult agent opens a flow to select a provider, one of its stored
  API credentials or a newly entered one, and a model, then place the agent in
  the world. Existing credentials can be reused by multiple agents. A player
  may also add another key for the **same provider** and choose which key that
  agent uses; there is no single-key-per-provider restriction. The model is
  chosen from the game's own short list for that provider, newest at the top.
  Listed models the selected key can't use are shown greyed out, and the
  player can always type any other model name. The agent
  chooses its own name after placement; the player can rename it later. Agents
  should choose full names with a surname and may choose a middle name. Do not
  include every existing agent name in the naming prompt merely to avoid
  collisions. The world rejects an already-taken exact full name and asks the
  agent once more; if that also fails, the agent keeps a placeholder name the
  player can change. The comparison covers every agent in the world, including
  those who have died; similar but distinct names are allowed (see
  [Agents and social life](agents-and-families.md#starting-agents-families-and-life-stages)).
  Player renaming follows the same full-name check, as recorded in
  [Player guidance and orders](#player-guidance-and-orders). Cultural naming
  context remains open.
- **Agreed on September 30:** before the player confirms, Add Agent shows which
  household and which Town the new agent will belong to. One shared rule
  decides this, checked in this order: household property first, then
  unclaimed land inside a Town, then land outside both. The preview is not a
  reservation: when the player confirms, the game checks the same map again and
  tells the player if the placement changed. If claims or Towns overlap so that
  the answer is ambiguous, the placement is refused and the game explains why.
  It is never decided by list order or distance. How claims and use rights
  work, and how Add Agent currently reads them, is in
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

### Player guidance and orders

**Agreed on October 1, during the owner design session:**

- The player is an **outside observer whom agents can hear**, rather than a
  physical character in the world or an impulse agents mistake for their own
  thought. An agent can distinguish the observer's guidance from its own
  intentions.
- **Suggest** sends the player's actual words to the agent's own model at its
  next personal-model decision. The agent may accept, modify or reject the
  suggestion. A new message asks for one fresh decision; any brief reply is
  part of that response. For example, "Growing potatoes could help your
  household through winter" conveys the reasoning, not just a farming task hint.
- A recognized **Order** takes priority over the agent's ordinary plans. The
  agent obeys within the game's physical and access rules; its model cannot
  refuse the order merely because it prefers another activity. An order cannot
  create goods, grant ownership or make an impossible action happen.
- The player may deliberately tell an agent something that agent has not
  discovered, such as the location of a berry patch across a river. The agent
  learns that the observer told it something; the statement can be true or
  false and is not automatically firsthand knowledge or a verified world
  fact. It still needs to travel there and verify the claim. Merely inspecting
  the map or another agent's private thoughts teaches no agent anything.
- The practical order catalogue covers movement, shelter, eating, gathering,
  carrying, storage, farming, cooking, crafting, repairs and building work,
  as those tasks become supported by the game. A recognized task uses the
  normal task system and its physical, resource and access rules.
- Conversations, relationships, inventions/mods and combat are agreed parts
  of the intended game, with their full systems planned for later alpha work.
  Their supported actions may become **Orders** too: "Talk to Ari", "Propose
  marriage" or "Work on a bridge invention" can require the addressed agent
  to attempt the activity once the game supports it. An order does not
  guarantee another person's agreement or a successful invention. Each
  system's participation and validation rules still apply; later development
  does not make these features provisional or restrict them to Suggest.
- When an order does not specify its target, the agent chooses a suitable
  target using its own knowledge and the normal access and travel rules. It
  may also explore to find new resources rather than always returning to the
  same known plant. Exploration discovers things through actual travel and
  observation; it does not grant hidden map knowledge. An explicitly named
  target takes precedence over this free target choice.
- Urgent survival needs may **temporarily interrupt an order**. For example,
  an agent gathering wood may seek warmth during a dangerous storm, then
  resume the pending order when the emergency passes. Ordinary preferences
  cannot interrupt it on the same basis.
- An order means **one task by default**, unless it specifies a quantity or
  repetition. "Gather food" finishes after one normal gathering job; "Make
  two sacks" finishes after two sacks; "Keep gathering food" continues until
  cancelled. Emergency interruptions preserve the outstanding task, quantity
  or ongoing instruction.
- A **recognized new Order replaces the previous order by default**, including
  an ongoing or waiting order. The player can explicitly choose **Queue** to have the
  new order done afterward instead. Pending and ongoing orders can be
  cancelled. For example, "Make two sacks" replaces "Keep gathering food"
  unless the player chooses Queue.
- **Agreed on October 2:** an order the game cannot understand leaves the
  current order running. The failed new instruction is reported as not
  understood; a typo does not cancel a task already in progress.
- The agent's own model sees an order's **original words and the task the
  game understood** in its next ordinary request. The wording supplies
  context, such as why the household needs food; it does not let the model
  refuse an otherwise valid order merely because it prefers another plan.
  A new active order asks for one fresh decision. Following its steps does not
  add per-tick model requests or a separate paid acknowledgement.
- The agent card shows orders and their status: **waiting, doing,
  interrupted, blocked with a reason, finished, cancelled or not understood**.
  For example, "Waiting for cloth: needs two pieces" explains a blocked job.
  A brief natural reply may accompany the agent's ordinary model response;
  the game does not generate an extra paid reply just to acknowledge a click.
- Normal direct agent edits are **names and model/key settings**, alongside
  the existing Add Agent and Suggest/Order controls. Needs, skills, goods,
  memories, relationships and world objects change through simulation actions.
  Direct manipulation of those fields belongs in developer tools, whose
  first set is agreed above. This preserves the separately agreed
  founder setup, world settings and Mod Library controls.
- **Player renaming uses the same full-name check as model naming**, against
  every other living or deceased agent in the world. A taken full name is
  rejected; the player supplies another name, without a model retry. Similar
  but distinct full names remain allowed. Renaming keeps the same person,
  relationships and memories; old conversation text remains as originally
  spoken rather than being rewritten to use the new name.

**Existing agreed handling, from September 30:** an order the game cannot act
on is accepted and closed at once as not understood, without a model request.
It does not hold up later suggestions or orders. A recognized task that cannot
be carried out yet keeps waiting, including across saves. Blocked tasks retry
on the agent's usual decision schedule, without paid polling on every tick.

**These are intended rules.** [What works today](../what-works.md#agents-and-their-models)
describes the supported food orders and the remaining catalogue.

### Leaning toward

Provisionally, the zoom-out floor stays as it is until Large worlds have been
measured, and is then tuned. Computment suggested Small/Medium zoom-out around
70% of map-fit scale and a shared performance cap for Large/Huge/Mega; that
exact percentage and visible-tile budget remain preferred starting points for
tuning, not fixed finished-game numbers.

Game Settings apply across worlds/on this installation: UI date and time
display formats, graphics/display preferences, and stored provider credentials.
**Agreed on October 1
([#639](https://github.com/compoodment/ClankerWorld/issues/639)):** the
AI-usage meter and limit also belong in Game Settings, because one limit covers
every world on the installation. The Main Menu's Settings entry opens Game
Settings. World Settings belong to the current save: autosave on/off, interval
and rotation, and Jev's per-world configuration. Selecting either category
within Pause Menu Settings keeps the world paused. World generation choices
such as size, climate and wrapping are chosen before creation and should be
inspectable afterward, not silently mutable settings. Jev's on/off switch for
an existing world is accepted; the exact settings categories and transition
behavior for an in-flight Jev task remain open. World Settings must be absent
from Main Menu Settings while no world is loaded; opening Main Menu settings
must never enter a world.

**Agreed (confirmed 30 September):** placing an agent with Add Agent on a
household's property joins that household without its consent, as recorded in
[Towns](towns.md#buildings-land-towns-and-animals). The rest of this paragraph
stays provisional.

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
invalid-placement rules remain open. A later voluntary move by an adult who
has no household into a household's House needs every adult member's
agreement, as agreed in [Towns](towns.md#buildings-land-towns-and-animals).
This forced Add Agent membership is distinct from inviting a nonmember to
visit a House. The agreed first-Town household setup in
[Agents and social life](agents-and-families.md#agreed-starter-town-and-remaining-choices)
is separate from this later Add Agent placement rule.

### Still to decide

Exact top-bar layout on small screens; the final zoom-out/visible-tile cap,
which waits for measurements of Large worlds; which overview and filter layers
ship first; display of disputed or overlapping claims; the precise event
categories, filter UI, event retention, and handling of events without a
single map location. The player's role, direct edits,
suggestions and order rules are agreed in
[Player guidance and orders](#player-guidance-and-orders). Computment may
provide a UI drawing.

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

- **Pixel art** is the visual style, with **32×32-pixel ground tiles**. The
  art is drawn in code at startup rather than loaded from PNG files (October
  1); an exported mod may still carry approved image files. Taller agents/trees and multi-tile
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
- **Agreed after the October 1 art review:** the game keeps its current
  pixel-art character, made more consistent by the
  [pixel-art style guide](../development/art-style.md): ramps built on the
  existing colours, light from the north-west, calm ground with soft
  patches, outlined sprites and roof materials. computment reviewed every
  texture beside a proposed redraw and approved 193 of 216 pictures as drawn,
  with no rejections. Approved: the ground tiles for grass, forest, sand,
  rock, snow and tundra; water; Roads and the plank bridge, with a street
  running up to the deck; trees and natural sites, including picked and
  depleted states; crop fields and orchard stages; House, Warehouse,
  Blacksmith, Silo and Tailor Shop roofs and the Store, Market, Town Hall,
  Port and Clinic; agents facing all eight directions, with walking,
  carrying, working, talking and hurt poses and a first horse; and item
  icons, including those for water, wild greens, potatoes, porridge, stew,
  meals and diamonds. The coin icon is gold.
- **Changes asked in that review:** mountains, peaks and hills are redrawn as
  one landform spanning many tiles rather than a picture per tile, and how
  they generate is to be rethought too
  ([#628](https://github.com/compoodment/ClankerWorld/issues/628)); the Main Menu valley keeps its look
  with better mountains; tilled soil needs more than plain lines; the grain
  icon must read as grain as clearly as before; and the Farmhouse loses its
  painted cart, because handcarts will be real vehicles.
- **Agreed after the second round of the art review (October 1):** computment
  approved 211 of 222 pictures, with no rejections. Mountains, peaks and
  hills are drawn from the world's elevation as one landform spanning many
  tiles: a range reads as one mass with a snowy crest, and hills are soft
  foothill shading with no rings ("I like that mountain is a range"). Also
  approved: the Main Menu valley with its new mountains; tilled soil and the
  scrub, dry brush, desert brush and tundra snow ground; the clearer grain
  icon; the Farmhouse with grain sheaves and sacks instead of a cart; every
  building footprint, the Workshop, the Restaurant, a handcart vehicle and a
  rowing boat; chickens, sheep and cows; the remaining tree, plant, outcrop
  and crop states; 43 more item icons; and part of the interface icon set.
  Animals, handcarts and boats face all eight directions, like agents. How
  mountains generate stays open in [#628](https://github.com/compoodment/ClankerWorld/issues/628).
- **Agreed after the third round of the art review (October 1):** computment
  approved 99 pictures; the others were the icon options not chosen, the old
  snow edge shown for comparison, and the Market plaza. Approved: the Market
  as a timber hall with no stalls inside; a Port where six boats moor bow-in,
  three along each side of the pier; chickens, sheep, cows, the horse with and
  without a rider, handcarts and boats drawn in all eight directions; the
  redrawn Hammer, Map, Pencil and Play icons, the Sun and Speech icons kept
  close to today's, the Rain, Snow and Storm icons built on the new cloud, and
  the solid-headed Arrow; three desert cacti, a barrel cactus, a saguaro and a
  prickly pear, standing on some of the desert's cactus cover; and a soft snow
  edge that thins into clumps and frost, shaded like a low drift, instead of
  ending in a flat white band. The plaza was "a bit too big for the eight
  stalls"; in the fourth round computment kept a shorter plaza but wanted
  the open column on each side back, so eight stalls take a 7×4 plaza.
- **Night lights, agreed on October 3:** at night, buildings in use and
  fires warm the ground around them. computment's rules:
  - Roofs never glow. Light comes out of a building's windows and open door
    onto the ground beside its walls. Windows are on the **front and both
    sides, not the back**, for every building with windows, the Farmhouse
    included; a front only one tile wide has room just for the door.
  - A **House is lit only while someone is inside**; nobody keeps a lamp
    burning in an empty house. Work buildings light while someone is inside
    or a job runs there, and the Blacksmith's forge glows in its yard while
    it works. A **Warehouse has no windows**: only a lantern by its loading
    doors, lit while someone fetches or stores goods. A Silo and Market
    stalls stay dark. A Port's lantern on the end of its pier burns every
    night.
  - Light must **not look circular** and must plainly **come from
    something**: a window, a door, a fire or a lantern fitting that is drawn,
    never a bare bright dot. Each pool has a ragged edge that **moves
    slightly**; fires flicker faster. Light is stepped on the art's pixel grid,
    warms the ground without hiding its texture, and fades in and out with
    dusk and dawn.
  - Every design gets lights, including those approved on October 1 that the
    game does not have yet (Market, Town Hall, Port, Clinic and Restaurant);
    each gets its lights when its feature lands. Night lights are only for
    looks: night still sets no visibility rule.
- **Street lanterns, agreed on October 3:** agents build street lanterns
  beside Roads; they need **no fuel** and light themselves at dusk. Two
  designs: **B, a round stone lamp with an open flame**, and **C, a lantern
  hung from an arm over the Road** ([Towns](towns.md#shared-town-projects)).
- Art is drawn in code, not stored as image files. computment prefers this.
  Approved art for content that is not built yet waits in
  `tools/ArtPreview/Proposed/` until its feature lands.
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

How mountains, peaks and hills generate ([#628](https://github.com/compoodment/ClankerWorld/issues/628)); animation timing;
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
