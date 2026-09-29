# Changelog

All notable player-facing, world-simulation, save-compatibility, deployment,
and security changes are documented here. ClankerWorld has not published a
release yet.

## Unreleased

- Personal models receive their own saved name, life stage, personality, aspiration, household, survival condition and most recent private thought; unknown conditions stay unknown.
- A failed active recovery write holds the in-memory world paused and retries saving without advancing or resuming paid work; other tick faults halt for inspection.

### Added

- Scouting safely starts a new local path after another action moves the agent away, instead of joining nonadjacent steps and breaking saves.
- Inherited physical maps and field records keep their lot identity, preserving the knowledge artifact link and saveability without broadcasting their contents.

- Death cancels unfinished barter through the normal two-sided release, immediately freeing the survivor’s stock without cancelling completed exchanges.
- Successful pause and rename retries now persist the acknowledged state even when a failed earlier save already changed it in memory.

- Starting households are named First household and Second household instead of
  Camp Alpha/Beta. Existing default camp names display the new wording without
  changing saved membership, property or custom household names. Founder setup
  instructions are shorter and describe the two-household grouping plainly.

- Public pairing creation is limited to eight attempts per minute and pairing
  request bodies to 16 KiB. Signed owner actions remain available. Host-local
  recovery can replace one unapproved request in a full pairing queue without
  discarding an approved pairing or revoking an active device.

- Loading an older world removes only Road tiles embedded inside saved building footprints, preserving buildings, ownership, stock and all other Roads.
- The Event Log and unread badge now select explicitly supported player events, including public partnership, care, trade and Town policy milestones; new internal event kinds no longer appear automatically as humanized diagnostics.
- Changing autosave rotation trims only the selected world, including Rotation off; other worlds’ checkpoint files remain untouched.

- A damaged model-call meter no longer prevents the host from starting. Paid
  calls stay blocked, and World Settings explains how to restore accounting
  without losing spent calls. Meter writes flush before replacing the file.
- A stalled world refresh stops after four seconds instead of holding the
  client for the default network timeout. Owner actions cancel an older refresh
  so it cannot overwrite their result; Pause remains available during polling.
- Map name tags use a whole given name or initial instead of cutting full names
  after seven letters. Activity symbols distinguish exploration, warmth, trade,
  social activity and care. Resource captions use ordinary case and disappear
  at overview zoom; full names and resource facts stay in hover/inspection.
- Held movement keys now pan smoothly with elapsed frame time, with equal
  straight and diagonal speed. Error notices give a plain recovery hint instead
  of showing raw exception text, private file paths or server responses.
- On Windows, saved provider keys are protected for the current Windows user.
  Valid old key files are migrated when loaded; an unreadable protected file is
  preserved rather than reset. Forgetting a key still removes it from the
  current installation, not from independent backups or the provider account.

- A new look for every menu and panel: wooden frames around parchment
  panels, ink text and chunky pixel buttons, with green for the main action.
  Settings has a new Theme choice of Light, Dark (dark wood with cream text)
  or Match system, which follows your computer's setting. Switching applies at
  once and is remembered.
- Settings can turn off the drifting cloud haze and the soft lightning flashes
  in storms. Both are on to start with and your choice is remembered.
- Escape now backs out one step at a time: a focused text field, an open
  menu, Town-site selection or a founder move, the newest open panel, then the
  selected agent. With nothing open it opens the Pause Menu. The mouse wheel
  now zooms toward the pointer instead of the screen center. The open Settings
  category shows as a selected tab instead of looking disabled, and New World
  labels its name and seed fields.
- Keyboard shortcuts in the world: Space or P pauses and resumes, N and
  Shift+N step through living agents and bring each into view, C re-centers
  the selected agent, H returns to the Town, + and − zoom, and M, F, I, R, T
  and E open the Map, Filters, World Info, Agents, Town and Event Log. F1 or ?
  shows the full controls list, and top-bar tooltips name each key. Clicking
  a top-bar button or the map no longer leaves the arrow keys and Space stuck
  on that button.
- A small readout in the bottom corner of the world view names the ground
  under the pointer: its surface, forest or water, any building or resource
  there, Road, the Town it belongs to and its tile position. It doesn't take
  clicks away from the map.
- Plainer in-game wording: the agent card names households and relatives
  ("Member of Camp Alpha", "Parent of Mira") instead of internal IDs, leaves
  out an unassigned role or unreported condition, and reads "Wants to take it
  easy" instead of "Wants to keeping a safe routine". When a model does not
  provide a usable choice, the card says built-in rules chose the safe action;
  local action failures are no longer described as lost host connectivity. The
  non-model option is called Built-in rules instead of Deterministic. Status
  messages drop request, revision and tick jargon ("World paused", "Connection lost · showing
  the world as of …"), and placing a founder or agent names the household they
  joined.
- Fresh generated worlds no longer show a second pre-placed camp or provisional
  Town beside the player-chosen five-building site. The New World preview no
  longer marks a supposed starting camp. Older saves keep their original map
  identity and camp objects rather than being rewritten on load.

- Starter Town and later Town-building Roads now connect beside building
  entrances, never through their footprints. New branches use connected
  orthogonal ground tiles instead of painting a building anchor as Road. Future
  construction sites cannot overlap an existing Road.

- Small/Medium world zoom-out now stops before showing empty space beyond the
  map, while larger maps retain a shared 8 px overview floor. Maximum zoom-in
  now frames roughly the same world height across 720p and 1440p render sizes.

- Removed the noisy per-tile grain, striped surface edges and large circular
  cloud overlays from the provisional world view. Rain and snow marks still
  show regional weather without changing the simulation or saved terrain.

- Game Settings now opens fullscreen by default while remembering a windowed
  choice. UI Scale enlarges window widths as well as text and controls. Main
  Menu Settings uses a single compact back button and shorter setting labels;
  Load World drops redundant helper copy. The agent model panel and model-call
  limit use plainer wording; the card no longer retains an oversized height
  after a view change. Tile
  inspection omits facts that are absent or not yet measured instead of
  showing `none` or `unavailable` placeholders.

- Hosted-model usage now defaults beside the configured private provider state,
  so a root-owned application directory cannot silently stop agents from
  thinking. A usage reservation that cannot be saved no longer counts as a
  model attempt or survives as a phantom pending call.

- During paused four-founder setup, select an already placed founder and use
  Move founder to choose another passable tile. The founder keeps their
  identity, household, Town membership and model assignment; the new position
  survives reload. The last placement can also be undone, removing that founder
  and their personal-model assignment while leaving shared keys available.
  Start World closes both edit controls.
- The paused-world host can accept or redo a deterministic five-building first
  Town layout before any founders are placed. Its two Houses belong to the two
  starting households, while Farmhouse and Blacksmith claims stay unassigned;
  the chosen site, Town border, buildings and connected Roads survive reload.
  The signed Godot New World setup now offers a map-click Choose/Redo Town site
  control before Add founders. Accepting the site locates each starting
  household's existing food in its House and puts one usable wooden axe and one
  wooden pickaxe in the communal Warehouse. Redoing a site moves the food with
  the Houses without duplicating tools. Supply choices, final quantities and
  suitability guidance remain open. Save schema 25 records the selected site
  while older Town saves retain their camp-derived border.
- Newly created worlds now load the built-in House, Warehouse, Farmhouse,
  Blacksmith and cooking definitions during paused founder setup, before the
  first simulation tick. The five starter buildings are placed when the player
  chooses a rough site, not automatically when the map is created.
- Adults in a household with both a Farmhouse and a House can carry milled
  flour from its Farmhouse to its House. Flour remains with the carrier until
  arrival, then becomes inspectable private House stock, including after a
  save/reload. This does not yet add bread or other flour-based cooking.
- A provisional household-claimed 1×2 Blacksmith joins the additive first-Town
  content. Its on-site stock can receive physically carried household wood;
  adults can mine natural iron ore and carry it to the building. Household
  members can make distinct wooden axes and pickaxes or refine ore into a
  separate iron item there. Another household cannot work
  the building. Carrying the matching wooden tool increases one resource-site
  wood or stone/ore gathering action from four to six units. The first-Town
  Blacksmith footprint is now generated but initially unclaimed; tool
  durability, sales/requests, later tool tiers, footprint and cost balance
  remain unfinished.
- Household builders can claim a provisional 1×1 Farmhouse. A distinct
  universal-grain crop now yields household-owned grain; household members
  carry grain to their Farmhouse before milling it into flour. Only that
  household can process there, and the resulting flour remains inspectable
  on site after reload. Unclaimed Farmhouses cannot produce, and food crops or
  flour cannot be stored in the communal Warehouse. A field's output now goes
  to its actual farmer's household instead of the old camp-alpha fallback.
  The first-Town Farmhouse footprint is now generated but initially unclaimed;
  farm fields' full growth states, private Silo, sales and final
  recipes/footprints remain unfinished.
- Town residents can build one 2×2 Warehouse inside their Town's growing
  building area. Adults may carry spare personal wood, stone, fiber or seeds
  there as communal stock; residents of another household can collect stored
  tools, clothing, wood or seeds there for work. The building inspection shows
  the actual stored lots after delivery, and they survive reload. Food cannot
  be Warehouse stock. The old Storehouse remains loadable in existing saves
  but is no longer offered for new agent construction once Warehouse content
  activates. Expansion, full resource logistics and formal ownership remain
  unfinished.
- Town-assigned building placement now generates world-owned Road tiles along a
  bounded legal dry-land route to the camp or existing Road network. Roads are
  visible in the world and overview, named in tile inspection, survive reload,
  and give a provisional route/diagonal-travel advantage; ordinary walking does
  not paint Roads. A building with no legal dry route stays unconnected. Initial
  Town layout, river bridges, inter-Town links and final Road-speed rules remain
  unfinished.
- Members helping another person in their own household with a project now
  deliver carried materials at their shared House. The contribution becomes
  household stock there only on arrival and survives save/reload. A helper
  outside the requesting household keeps the camp handoff; invitation and
  private-House entry rules remain undecided.
- A member carrying gathered materials for their own household's project now
  delivers them at their House instead of the old camp stockpile. The materials
  become household-owned stock at that House only on arrival and remain there
  after save/reload. Projects without a member-accessible House keep the camp
  route; other gathering, cross-household helper and production deliveries
  still need physical logistics.
- Adults with spare personally carried food can bring a bounded load to their
  own House while keeping one serving. The transfer happens only after they
  reach the House, becomes household-owned stock at that location, and survives
  save/reload; a resident without a House cannot use this route.
- Add Agent placement now previews the saved Town and recorded household owner
  under the pointer and reveals their map overlays. Placing an adult on an
  owned building footprint joins that household and the enclosing Town, if
  any; even an occupied House tile can accept another member. Placement
  on unclaimed Town land joins the Town without a household or access to a
  founding household's private stock; outside Town borders, it starts a new
  independent household. Moving later does not change that affiliation.
- The top-bar Filters panel can hide the saved Town-border outline and show
  household-owned building footprints with stable household colors. It never
  tints unclaimed land as property; selecting a tile names its recorded
  household property owner or says none is recorded. Filter changes follow
  new observations without changing the underlying Town or ownership facts.
- Adult household members can carry bounded loads of older camp supplies to
  their own House. The load remains with its carrier through save/reload and
  appears in the House's inspected stock only after the carrier reaches home;
  invalid delivery destinations are rejected on reload. Residents without a
  household cannot use the first household's House through the legacy camp
  fallback. Other gathering, cross-household helper and production paths still
  need physical delivery before all household supplies live at the House.
- Meals finished at a House now record that House as their physical stock
  location. Its map inspection shows only items actually recorded there, and
  household members enter the House tile to collect those meals, cook, or take
  refuge rather than using it from an adjacent tile or the old camp store.
  Multiple household members can share the House tile without a headcount cap.
  Children carrying spare food deliver it to their household House when one
  exists. Invalid or missing storage buildings are rejected on reload; older
  household stock remains unlocated until a hauling path moves it into a House.
- Members can now cook a household meal at their own House. The job reserves
  only food and wood actually stored in that House and returns the meal there
  after save or reload; outsiders cannot start the job, and remote camp supplies
  cannot be used as if they were already at the hearth.
- A first 1×1 House design can now be built for a specific existing household.
  Its ownership survives save/load, appears on the building's map inspection,
  charges construction materials to its household, and limits storm refuge and
  lit-house warmth to that household. Legacy Shelters remain loadable in existing
  worlds while larger Houses, physical household storage and guest invitations
  are still being developed.
- Game Settings now offers a locally saved UI Scale from 100% to 200%, making
  controls and text easier to read without changing the selected map render
  resolution or terrain detail.
- Generated Small/Medium worlds now visibly distinguish grass, forest floor,
  beach sand, dry scrub, rocky upland, snow and saved fertile-land sites, with
  subtle deterministic ground variants and readable coast/surface edges. Dry
  climates can show cactus cover. Natural sites now include berry bushes, wild
  greens, fiber plants/reeds, stone and iron/gold/diamond outcrops, clay banks,
  orchard trees and woodland trees; each retains an inspectable object kind and
  resource state, and trees remain separate objects with at most one tree per
  tile. Mineral outcrops and clay banks are finite, inspectable deposits only—
  recipes and processing are not included. Fertile-soil ground marks an actual
  saved fertile-land site; it does not represent a general fertility estimate.
  The new terrain and object marks are code-drawn prototypes rather than final
  production textures/sprites. Existing generated saves without the new natural
  object details and geology sites remain loadable.
- The paused New World setup now has a persistent first Town. Founders join it
  as they are placed; its amber map border and World Info show only its saved
  founding state, residents, and assigned buildings. Adults placed within the
  border join the Town, and Town-assigned buildings can expand it. Existing
  founder-setup saves migrate without inferring unrecorded adult membership or
  building assignments.
- Building plans now offer agents up to five ranked legal sites with reasons
  for access, clear ground, Town growth, nearby materials and related purpose.
  The agent chooses the site or declines by choosing another action; an
  accepted tile stays with the project. If it becomes illegal, fresh choices
  return after 60 ticks. The ranking follows the prototype's single rectangular
  Town border and does not yet model land claims or multiple Towns.
- After an agent dies, their assigned personal model can make one bounded final
  choice to leave frozen personal belongings on the household inheritance path
  or give the whole estate to a living agent. Invalid, unavailable, timed-out or
  interrupted choices use the household default. The result survives saves and
  appears on the deceased agent card and in the Event Log.
- Agent memory inspection can distinguish owner-private beliefs from social
  memories, showing firsthand/hearsay/inference provenance, confidence, and
  retained correction history. Beliefs stay out of the public event log;
  schema-19 private saves remain loadable and migrate to schema 21.
- Optional Jev memory assistance now scores small batches of one agent's
  existing memories and beliefs during Jev's normal routine decision call.
  Personal-model decisions can retrieve up to four relevant source records with
  their provenance, confidence, and correction status; Jev-off retrieval still
  works locally. The source text is not rewritten, and compaction adds no
  separate provider request.
- Local exploration now gives each agent a bounded personal ledger of terrain
  and resource-site facts. A completed outing can produce a physical field
  record or map that the agent may share nearby or barter; the recipient learns
  only those recorded sites, with discoverer and source tracked. Personal-model
  decisions receive only that agent's bounded facts, and the Godot Memories and
  Maps view shows their knowledge and held artifacts. Schema-21 and schema-22
  saves load with an empty knowledge ledger and migrate to schema 23.

### Changed

- Agents no longer start Shelters, Storehouses, Cooking fires or Stone
  hearths. A household's House now provides shelter, cooking, warmth from its
  fire and food storage, and a Town's Warehouse holds shared supplies.
  Experienced builders also stop suggesting shelter, storehouse and hearth
  designs. Worlds that already have these buildings keep them working,
  projects for them that are already under way still finish, and designs
  suggested earlier stay in the Mod Library. An adult without a household
  cannot build a House yet, so in a new world clothing and natural cover are
  their only protection from cold.
- Weather on the map now moves. Rain falls as short drops that land with
  small splash rings, storms darken the sky with heavier slanted rain and a
  soft flash of lightning every several seconds, and snow drifts down. Rain,
  storms and snow no longer fill square blocks: their edges wander in
  irregular shapes that creep slowly and fade out softly. A very light haze of
  cloud drifts over the land now and then, more often under cloudy or rainy
  skies, without covering the view. All of it holds still while time is
  paused and carries on when you resume.
- Neighboring land surfaces now blend at close zoom instead of meeting in a
  hard tile-grid line. Grass reaches softly into sand, forest into grass, snow
  into rock and tundra, and mountains shed a rocky edge. The edge wanders and
  merges with a few bumps and half-transparent rim pixels, continues smoothly
  from tile to tile, and rounds corners, while staying in a narrow band so
  every tile still reads as a square of its own ground. Water keeps its
  shoreline edges, and the zoomed-out overview is unchanged.
- Coasts, lakes and rivers now have rounded, wandering shores instead of
  straight tile-edge strips. The land reaches a narrow way into the water, and
  the lighter shallows and the thin foam line on coasts and lakes follow that
  edge; rivers keep a quiet bank without foam and still read as one-tile
  channels.
- Where a river runs into the sea or a lake spills into a river, the lighter
  water now fans softly into the darker instead of changing color in a
  straight line.
- Open water no longer repeats the same small square: seas, lakes and rivers
  carry wave crests and the odd sparkle that run across tile edges and gather
  in some stretches, and shallow water shows faint light ripples across its
  bottom. Seas have the most swell, lakes are calmer and rivers ripple in
  short marks, all in their usual colors.
- The Town panel and Event Log are easier to scan. The Town panel puts
  shared stores, projects, the household council and social activity under
  headings, and says "No one is working on a project right now" instead of
  leaving an empty gap. The Event Log groups entries under each day with a
  dimmed time per row, and located events are highlighted as links. The
  Agents, Town, Event Log, World Info, Filters and World Map panels now have
  a close button, and the panels are titled Agents and Event Log to match the
  top bar.
- The top bar is now three small wooden panels floating over the map, with
  pixel icons on every button: Map and Filters; the pause control with the
  date, time, season and local weather; and the world actions. When time is
  stopped the pause button turns orange and reads Paused. The Agents button
  shows how many are alive and a small orange dot when someone is hungry, and
  the Event Log button shows a red count of events since you last opened it,
  with those rows dotted in the log.
  During first-Town setup, its extra controls sit in a second row so Start
  World and Menu stay on screen at 1280×720.
- The Town button is gone, since a world can hold more than one Town. World
  Info now opens on a Towns page listing every Town with its residents, when
  it was founded and a Show button that moves the map there, followed by the
  household stores, projects and council; the World page holds the rest. T
  now opens Towns.
- The Agents list now shows what each living agent is doing and flags anyone
  hungry, lists the deceased under their own heading, and fits its height to
  the people in it. Choosing someone moves the map to them, and the agent
  card's new Find button does the same.
- The menus match the new look. Main Menu, Pause Menu and New World buttons
  carry pixel icons, Quit to Menu sits apart at the bottom of the Pause Menu,
  and Game Settings groups its choices under Interface, Display, and Date and
  time, with Fullscreen as an on/off switch. New World now speaks of choosing
  your first Town's site.
- New World fits on one screen at 1280×720: the options sit in a captioned
  column beside a map preview about twice as large as before, with Back and
  Create World always in view. The preview keeps its place while it updates,
  and Preview again is there if an update fails. Load World shows each world's
  name, whether it is current and when it was saved, flags only worlds that
  can't open or haven't been checked, and opens a world on double-click.
- Zooming in now shows pixel-art ground instead of flat color squares: two
  calm 32×32 textures for each kind of grass, forest floor, sand, scrub, rock,
  soil, snow and water, small top-down mountains with snowy peaks, and
  shoreline foam or river banks where water meets land. The zoomed-out overview
  keeps its flat colors, and the old ≈ and ▲ map symbols are gone.
- Confirmation dialogs (Quit to Menu, Quit Game, Load Save and Overwrite) now
  match the game's dark panels instead of the default grey window, and their
  confirm button names the action, such as "Quit to Menu" or "Overwrite",
  instead of OK.
- Trees and natural sites are now pixel-art sprites instead of plain circles:
  leafy broadleaf and star-shaped conifer canopies, stumps with growth rings,
  saplings, fruiting and picked orchard trees, berry bushes, wild greens, fiber
  plants, reeds, stone boulders, rust-streaked iron, gold-flecked and
  crystal-studded outcrops, clay banks, seed heads, tilled soil, and distinct
  depleted and regrowing sites. Their map markers no longer draw a second
  symbol on top.
- The starting camp no longer shows plain ■ squares: its cooking fire draws as
  the stone-ringed hearth, the camp path as flagstones, and an old bedroll as
  a blanket. The camp's original food, wood, stone, fiber and seed stores draw
  as berry bushes, a log pile, boulders, fiber plants and seed heads instead of
  ●, ⬟ and ♧ symbols. Camp objects are named like buildings ("Campfire",
  "Shelter", "Path") instead of FIRE and HOME.
- Buildings are now drawn as top-down pixel-art roofs across their whole
  footprint instead of a text symbol: red-tiled houses with chimneys, slate
  warehouses with loading doors, thatched farmhouses, smithies with a glowing
  forge, hide tents for shelters, plank storehouses, workshops with a hammer
  sign, weaving frames, and stone-ringed hearths for campfires. Roads have a
  worn edge and a lighter center. Agents appear as small top-down people with a
  stable look per person and different sprites for infants, children, adults
  and elders; hovering or selecting one rings them and shows their name.

### Fixed

- A damaged named-save entry no longer hides other saves or stops autosave
  rotation. Damaged files are preserved, with a safe diagnostic for recovery.

- Hovering the Town panel no longer pops up a technical tooltip of world
  ticks, revisions, map digests and internal system counts.
- The selected-agent card no longer covers the agent it describes: when it
  cannot fit above or below them, it opens beside them. On short screens its
  profile scrolls inside the card, so Speak and Send are no longer cut off
  below the bottom edge. Rows in the Agents list are no longer clipped.
- A world where an agent ate orchard fruit now saves and reloads normally;
  that meal source was previously rejected by save validation.
- The Town panel no longer goes blank while the world is paused, and
  reselecting an agent no longer leaves their relationships, private thoughts
  or memories empty. The Event Log keeps your scroll position between updates
  and no longer repeats "Initial content activated" when a world is created.
- Action results and map-click instructions, such as choosing the Town site or
  moving a founder, stay on screen long enough to read instead of disappearing
  within a second; mode instructions remain until the mode ends. A request the
  world host refuses is reported as not accepted rather than as a lost
  connection. Messages from Main Menu Settings are no longer hidden behind the
  title backdrop.
- Opening a world now centers the camera on its first Town or living agents;
  a new, unsettled world starts over dry land instead of possibly over open sea.
- The selected-tile card no longer extends below the bottom of the screen or
  hides its last fact behind a scrollbar.
- Map markers too small for their name now show only their symbol, with full
  details in the tooltip, instead of clipped fragments such as "rehou".
- Top-bar actions such as Start World, Play and Undo last founder are dimmed
  and blocked while the Pause Menu or Settings are open. Choosing the Town
  site now changes its button to Cancel Town site until a site is chosen.
- The Connect/Pair screen keeps the title backdrop, uses the same compact back
  button as Main Menu Settings instead of a second large Back button, and
  scrolls the pairing code into view. Settings and autosave choices line up in
  one column at every UI Scale.

- Adults in a household that already has a House no longer plan a redundant
  new House. An in-progress second-House project ends with a visible reason
  before spending supplies; household invitation and new-household choices for
  unaffiliated adults remain open.
- New World map preview no longer fails with HTTP 409 when the previously
  selected world is running. Preview leaves that world untouched; creating or
  selecting a different world still pauses it first.
- Regional storms now end within three-quarters of a game day, becoming rain
  for the rest of that day. The world, inspected weather and resumed saves use
  the same transition, so severe exposure cannot persist through a full day.
- Agents caught away from buildings can now seek nearby forest or a standing
  tree during a storm. Natural cover reduces exposure while they occupy it.

- Adding an adult on disconnected land no longer breaks world reconnects while
  the client previews a path to food. An accepted pause also lets Quit to Menu
  work when the next world refresh fails. The host can again load pre-layer
  generated worlds whose original resource layout and settlement package
  predate the current generator.
- Render Resolution now defaults to the current window size or fullscreen
  display size, so a 2560×1440 screen renders sharply instead of enlarging a
  720p/1080p viewport. Fixed resolution choices remain available and include
  the detected display size; older explicit non-default choices are preserved.
- Hosts now report accepted Jev memory-index updates with the owner and bounded
  record counts, without logging the underlying memory or belief text.
- Main Menu Settings now accepts clicks while keeping the title backdrop visible;
  Back returns to the Main Menu instead of leaving the game apparently frozen.
- New agents' personal models are now asked to choose a full name with a family
  name; a middle name is optional.
- Turning Jev off keeps an agent's saved social memories and private beliefs useful in personal-model decisions. Each agent receives only a small, relevant set of its own records, with belief provenance and confidence preserved; provider failures do not add or share memories.
- Generated Small/Medium maps now keep climate, elevation, water, surface and
  vegetation cover distinct through saves, previews and owner observations.
  Ground drawing and movement use those facts separately; individual trees
  remain objects. Older generated saves recover the missing layers without
  changing their terrain manifest or resource layout.
- Agents can cross narrow river tiles on foot only between opposite dry banks.
  They no longer walk along a channel or cut diagonally through its water;
  wider rivers remain impassable without later transport or bridges.
- Children born in a world no longer inherit a billable world cognition default
  when their personal model has not been selected. Infants cannot queue personal
  decisions; after infancy they make safe local decisions until an explicit
  personal model is assigned in the agent card. Removing that assignment
  returns them to local decisions rather than another paid account.
- Children now have age-appropriate choices to talk, play, learn by observing
  an adult, and carry spare food home. These encounters persist as social
  memories and trust. The world rejects adult-only work, trade, partnerships,
  parenthood and council actions for children even if a model or owner request
  tries to select one; infants still make no personal-model decisions.
- Added selected-tile inspection. Clicking ground
  highlights the tile and shows terrain kind, separate generated climate,
  elevation, surface, hydrology and vegetation cover, objects, resources,
  buildings, regional weather and moisture. Fertility remains explicitly
  unavailable. The owner accepted this direction; future field and layout
  refinements can follow the underlying world systems.
- Signed owner reconnects now use a versioned terrain-cache claim. After an
  initial generated-map transfer, unchanged terrain bytes are omitted from
  routine observations; clients reuse only a matching world, terrain manifest
  and map-layer digest, and fetch fresh map data after a cache miss or change.
  Ordinary world events and dynamic objects remain fresh.
- Private-world saves now store generated terrain in versioned 64×64 byte
  chunks with per-chunk integrity hashes rather than one JSON object per tile.
  Existing v1 saves load and migrate atomically to v2/schema 19; damaged or
  incomplete chunks fail closed without replacing the checkpoint.
- Regional rain, snow and storm conditions now draw bounded top-down cloud and
  precipitation marks over camera-visible terrain, including wrapped world
  seams. Clear regions stay unobscured; the effects are presentation only.
- Agents no longer have an energy meter, passive energy drain, sleep action,
  bedroll recovery, or energy gates on work, social life and exploration. Food
  and severe-weather effects remain. New worlds omit bedrolls and bedding;
  older private-world saves remain loadable with hidden legacy bedroll markers
  and migrate to the current save schema when loaded.
- Illness now slows work and travel by severity without suppressing ordinary
  social choices. Food, shelter and caregiver actions support bounded recovery;
  Bandage, Medicine and Clinic have catalog-only identifiers without guessed
  recipes or treatment effects. No sleep/energy loop was added.
- New World now refreshes the exact seed/settings preview after edits without
  requiring another button press. Stale previews cannot enable Create World;
  preview failures leave the active world unchanged.
- Out-of-view event pop-ups and their Game Settings switches are gone. Births,
  deaths and other important events remain in the Event Log, where located
  entries can still move the camera to the event.
- The Pause Menu now shows Save World, Settings, Mod Library, and Quit to Menu
  in a compact vertical order, with Quit to Menu last. Game and World are
  categories inside Settings; Quit Game remains on the Main Menu. The old
  player-facing Create building workbench has been removed, while existing
  agent proposals remain recorded and visible in the read-only world package
  list.
- Wrapped worlds now pan continuously across the east/west map seam. The
  overview marks seam-spanning camera areas, and placement and hover use the
  matching map tile; non-wrapped worlds keep their bounded camera edges.
- The world view can zoom farther out to show more of a large map at once.
  The 8-pixel terrain-tile minimum is a provisional playtest setting, not a
  settled final readability/performance cap.
- Load World now checks archived worlds before selection and labels them compatible,
  incompatible or unknown. Proven-unloadable saves are preserved and cannot be
  selected; an older but still loadable save is not blocked merely for its version.
- Save World now separates Create New Save from a confirmed overwrite of one
  selected named checkpoint. Matching names never replace a save by themselves,
  and overwriting keeps the previous checkpoint as a recovery save.
- Main Menu Settings keeps the title-screen background, and selecting the
  already-active Settings page no longer closes it. Game Settings now separates
  physical Window Size from Render Resolution; both choices are remembered
  locally, with viewport scaling applied to the game and UI.
- Hovering bare ground now outlines its square tile, while an agent under the
  pointer keeps hover and selection priority. Agent markers sharing a tile
  divide that tile into bounded click targets at supported zoom levels, so
  adjacent-tile clicks no longer select them.
- Owners can now delete an unused named provider API key from the host through the
  cognition settings. A key assigned in the active world must be switched first;
  older saved assignments to a deleted key return to deterministic cognition
  instead of silently using another account's key.
- Main Menu Settings now hides and blocks World Settings until a world is
  loaded, so opening settings cannot jump into the world. Removed the dark
  gaps between square terrain tiles that appeared as black grid lines.
- Agent markers now stay under the cursor across world refreshes, so their
  hover tooltips are no longer cut off every tick. Warmth, illness, diet and
  equipment status stay in a fixed area of the selected agent card.
- Settings, model, key and paid-call tooltips and messages now use short,
  plain wording, and several developer-style messages read as ordinary
  sentences. The agent list shows Fullness instead of Hunger, because 100%
  means well fed and the old label read backwards.

### Added

- World Settings now shows installation-lifetime paid-model attempt and known
  token totals by provider/model. An optional call-attempt cap pauses the world
  at the limit; the owner must explicitly allow more paid calls or turn the cap
  off before resuming. Retries and abandoned calls count as attempts.
- Agents now route and move diagonally when both orthogonal corner tiles are
  clear. Diagonal steps have a deterministic longer cost, work across wrapped
  east/west seams, and cannot squeeze past blocked or occupied corners.
- Stable adult agents can now choose short curiosity outings. They scout only
  adjacent passable ground, remember tiles they actually visit, and return to
  their starting point. Hunger and urgent exposure take precedence;
  a cooldown prevents continuous wandering.
- Generated forests and scattered meadow sites now have individual broadleaf
  and conifer trees, with no more than one tree per tile. Cutting a tree for
  project wood leaves a visible stump that regrows in season; agents can carry
  household seeds to a depleted site to replant a sapling. Tree stages persist
  through saved worlds, and the top-down client draws only camera-visible trees.
  Existing generated saves retain their original vegetation layout and remain loadable.
- Suitable newly generated meadows now also contain individual generic orchard
  fruit trees. Agents can pick a distinct fruit item and eat or share it; the
  tree visibly changes from fruiting to picked to growing and regrows its fruit.
  Species, yield and seasonality are provisional, and older generated saves
  keep their original tree layouts.
- Agents can walk through one-tile-wide river crossings and mountain tiles at
  half the dry-ground travel speed. Wider river sections, peaks, lakes and
  oceans remain impassable, and river/mountain tiles remain non-buildable.
- Generated worlds with east/west wrapping now route agents and evaluate
  interaction range across the seam; non-wrapped worlds retain bounded paths.
  Old generated checkpoints inherit their saved wrapping choice on reload.

- Generated Small/Medium worlds now place sparse food, fiber, seed, stone and
  regrowing wood sites beyond the starter camp according to local ground.
  New World offers Sparse, Normal and Abundant resource settings that change
  actual site placement and the signed preview count, not just a label.
  The preview reports the number of sites, and the same sites persist through
  world creation and reload. Agents can seek reachable food beyond the starter
  berry patch, while inaccessible islands do not count as immediately
  gatherable; some remote sites will require later boats.

- New World now offers balanced, uniform, or dominant climate generation,
  a selected climate family for uniform/dominant worlds, and optional
  equator-to-pole cooling. The preview and created world use the same signed
  options. Generated ground distinguishes provisional sand, forest, and snow
  appearances; regional rain/snow likelihood and resulting soil moisture use
  the saved local climate. Clear-day cold exposure also respects that climate,
  so tropical winters do not behave like temperate winters. Mountain and peak
  tiles remain unbuildable.

- Recent local rain now raises a bounded soil-moisture estimate, while dry days
  lower it. Moist soil modestly improves food-crop harvests and very dry soil
  reduces them; snow and storm crop penalties still take priority. World Info
  shows the moisture estimate near the camera, and affected harvests explain
  the change in the event log.

- Generated worlds now have 32×32-tile weather regions instead of one weather
  condition across the whole map. Agents' warmth, clothing/fire choices and
  travel fatigue use weather where they stand or travel; crops use weather at
  their field. The top bar and World Info show weather at the camera location.
  The small development fixture keeps its original single local condition.

- New World now shows a signed, read-only terrain preview before creation.
  Changing the seed, size, water share, climate, resource abundance or wrapping
  marks it stale; rerolling
  the seed can regenerate it, and Create World only enables for the previewed
  options. The camp location is marked on the atlas. The preview and created
  world use the same deterministic map and do not alter the current world.

- Main Menu **New World** now creates a separately saved Small or Medium map
  from a chosen seed, water share and east/west-wrap choice, then opens its
  empty camp paused for four-founder setup. **Load World** selects an archived
  world and restores its per-agent model/key-slot assignments and autosave
  choices without copying API keys or re-pairing. The current world is saved
  before switching; named checkpoints remain in the pause menu for the selected
  world. Larger playable presets are still pending.

- Generated Small/Medium worlds now have a compact row-major terrain payload
  for owner observations instead of tens of thousands of JSON tile objects.
  The Godot client can draw that payload, inspect the minimap and place founders
  through the same single-view camera. The current live world still uses its
  existing small-map wire format.

- The deterministic geography generator can now form a valid empty base camp
  on Small and Medium maps and carry its seed, wrapping and water options
  through the private-world save. A four-founder generated world can advance
  and reload in the simulation. River/lake/ocean and mountain/peak ground are
  represented in the current physical map; mountain and peak are not buildable.
  The later New World menu uses this bridge; layered ecology is not finished.
  Larger presets remain in the compact geography generator only.
  Terrain lookups are indexed, and proposed ticks reuse their committed map
  instead of regenerating the whole geography each tick.

- The world view now draws only camera-visible terrain instead of creating a
  Godot button for every tile. The top-left overview uses a compact atlas of
  the same terrain data; it remains the existing single zoomable view, not a
  separate regional art set. A regional-size UI smoke map exercises this path.
  New World uses the compact map observation protocol added below.

- World Settings now controls per-world autosaves: on by default every five
  minutes with five rotating copies, with the agreed interval and rotation
  choices. Rotating snapshots appear in Load Save and can be restored like
  manual checkpoints; turning autosave off does not disable the host's separate
  per-change emergency recovery checkpoint.

- Pause Menu → **Save World** now creates an unlimited named checkpoint of the
  paused world. Pause Menu → **Load Save** lists those checkpoints, confirms a
  rewind, preserves the current state as a new checkpoint first, and opens the
  loaded world paused. Saves survive restart and retain per-agent model/key-slot
  choices without copying API-key secrets into world files.

- Freshly placed founders and later added adults can choose their own names in
  an accepted ordinary personal-model decision, without a separate naming
  request. If the player renames one first, a delayed model answer cannot
  overwrite that choice. Names persist with the world.

- The selected-agent card now lets the paired player rename an agent. The
  chosen name updates the visible world and family tree and survives reload;
  biological identity and relationships do not change.

- After the four-founder start, the top bar now offers **Add Agent**. Choose a
  provider, model, and saved or new API key, then click an empty passable tile.
  The adult joins the running world in a separate one-person household and
  survives save/reload. Placement requires the paired owner and rejects
  occupied or impassable tiles; the current map has no property claims yet.

- The Windows client now starts at a Main Menu with Continue, Settings,
  connection/pairing and Quit Game. Quit to Menu pauses the host world; Continue
  returns to it. New World and Load World were enabled in a subsequent batch;
  Load Save remains for checkpoints of the selected world.

- Building placement now has an explicit buildable-ground rule. The existing
  mountain tiles reject construction, including owner-authored placement;
  future peak terrain is also reserved as no-build ground.

- A fresh private world now opens with an empty two-household base camp. The
  paired player picks a provider, model and saved or new API key for each of
  four unrelated founders, then places them on empty map tiles. Progress is
  saved after every placement; time cannot resume until the player explicitly
  chooses **Start World**. Existing development worlds keep their state.

- In World Settings, an inhabitant can now use one personal provider/model for
  both daily and project decisions. Players can save multiple named API keys
  for the same provider and choose which one an agent uses. Keys remain in
  private host storage, never in the world save or owner status. Existing
  provider settings migrate without dropping their saved credentials. The
  selected agent's card also opens those model/key controls directly.

- Fresh private worlds now start paused with six-minute days, a 40-day year and four
  10-day seasons. Agents born in those worlds become children at day 3, adults
  at day 15, elders at day 45 and cannot survive past day 60 from birth.
  Agent profiles show age in days. The prior development save was archived,
  and a fresh paused world was created to playtest this pace.

- World Settings now has a saved Jev assistance switch. Disabling it while
  paused keeps memories and credentials, invalidates older decisions, and
  routes work Jev would have handled to the agent's personal planning model,
  the world planner, or a local safe fallback. Jev can be re-enabled later;
  changing the setting writes a newer save format so older hosts cannot
  silently discard the choice.

- The top bar, World Info, agent histories and event log now format dates and
  time from the saved world's calendar pace. Game Settings offers DD-MM-YYYY,
  MM-DD-YYYY and YYYY-MM-DD display without changing world time. The old aging
  multiplier has moved out of World Settings into clearly labelled prototype
  developer tools; it is not the decided custom calendar.

- Agent profiles now open a separate Memories panel showing that agent's
  saved records, including private memories and memories retained after death.
  These records no longer masquerade as public social notes. Quit Game now
  asks for confirmation before closing the client.

- Agent profiles now show a small scrollable history of recent private thoughts
  written alongside accepted personal-model decisions. These thoughts survive
  saves and remain inspectable after death; routine fallback and hidden model
  reasoning are not presented as thoughts.
- Important out-of-view births, deaths and building proposals can raise brief
  notices that jump to their location or open the event log. Game Settings
  remembers notice choices by category without hiding full log entries.

- Game Settings now remembers a 24-hour or AM/PM clock preference on this
  installation. The top bar, World Info, settlement view, and event log use
  the same display choice without changing world time.

- An interactive Family Tree opens from an agent's card. It draws accepted
  parent–child and partnership links, keeps unrelated household members
  separate, and opens living or deceased relatives' profiles when clicked.

- Slow hosted agent-model calls no longer hold the entire world tick in the
  private-host build. Other agents and world systems advance while a request
  waits; pause/disconnect discard the external call, and saved unresolved
  decisions can be retried safely after reload. An agent card marks a queued
  decision so the wait is visible.
- The Events panel can jump to the recorded map location of an
  actor-associated event; global events remain informational.

- Deceased inhabitants now leave a saved, read-only record with their last
  location, age, role, relationships and death details. They remain inspectable
  from the inhabitant list after reload without appearing as living map actors.

- The Windows world view now supports mouse-wheel zoom, WASD/arrow and
  middle-drag panning, plus a top-left Map button. Its data-drawn overview
  marks the visible area and lets players click or drag to move the camera;
  this adds no second regional texture set. The top bar shows the living-agent
  count and opens a concise World Info panel; the pause menu separates current
  Game Settings from World Settings.

- Experienced builders can now propose bounded 1×1 shelter, storehouse or
  hearth designs through planning cognition. Completed building practice gates
  the choice; proposals are rate-limited, retain durable inhabitant authorship
  and enter Menu → Create as **proposed only**, labelled with their proposer.
  They never validate, approve,
  stage, activate or consume live materials without the owner's existing review
  steps.

- Added bounded directed trust earned from completed material help, mutually
  accepted barter and finished teaching. Trust survives restart, is visible on
  inhabitant cards, prioritizes familiar barter partners and remains required
  before proposing partnership. Refusal, withdrawal and disagreement do not
  reduce trust. Save schema 12 persists scores while projecting older
  cooperation memories without rewriting paused saves.

- Added bounded building, farming and crafting practice. Inhabitants earn one
  point only when useful work completes successfully; every ten points speeds
  the hands-on preparation stage without bypassing roles, materials or crop
  growth. Practice persists in save schema 11 and appears on inhabitant cards.

- Added Menu → Create: a data-only building workbench for named shelters,
  storehouses and fuelled hearths. Isolated construction previews consume no
  live materials or paid cognition. Saved designs require separate validation,
  approval and staging; activation waits while paused. Used designs cannot be
  withdrawn destructively. Inhabitants can select and construct active designs.
- Constructed buildings now appear on the map with readable names, footprints
  and hover help instead of existing only in the server's building list.

- Package withdrawal now rejects committed building, production-history and
  settlement-project references until an explicit migration is available.
  A rejected request preserves the world; withdrawing unused content no longer
  cancels another package's work. Rollback outcome logs omit private reason text.

- Resource markers show remaining stock and capacity, with working hover help
  for finite deposits and seasonal regrowth. Markers persist across refreshes
  so updates no longer discard the hovered control.
- Unfinished production and crop jobs are cancelled when their worker dies,
  releasing remaining reserved inputs before completion can produce output.
  Finished work remains intact; operators receive a safe cancellation event.

- Added managed coppice: farmers reserve seeds and a fertile plot for a full
  world day, then harvest timber and replacement seeds. Exhausted wild wood is
  not refilled; food crops and forestry compete for the same growing space.
- Fixed crowded rest and food access: inhabitants choose reachable shelters,
  fall back to bedding or slower outdoor rest, and do not keep choosing blocked
  shared food over reachable alternatives. Outdoor rest retains exposure risk.
- Construction retains the worker's current legal site instead of chasing newly
  vacated earlier tiles. Busy mentors can respond to teaching requests before
  finishing their existing project.
- Descendant activity/condition logs preserve complete colon-bearing inhabitant
  IDs instead of truncating them or silently losing condition events.
- Descendants can now finish buildings: generated instance IDs are canonical
  stable hashes when society IDs contain separators. Existing founder-building
  IDs remain unchanged, so already-built structures are still recognized.
- Preserved sibling and direct-ancestor partnership exclusions after relatives
  die, including grandparents. Ordinary relationship commands can no longer
  revoke historical parentage or accept a fabricated parentage proposal.
- Connected replacement caregiving after a dependent loses all active carers.
  Adults volunteer; infants receive protected care without fabricated consent,
  while older dependents independently accept/refuse. Offers expire, either
  participant may withdraw, and replacement carers use the real feeding loop.
  The client shows missing care and readable caregiving decisions.
- Fixed household caregiver tracking when one of several care obligations ends;
  remaining dependents no longer lose the adult from the household projection.
- Added pause-only, signed **Life pace** settings: original calendar aging or
  opt-in generational aging. Changes preserve current ages, birth dates and the
  365-day world calendar; they affect future biological aging, not tick or model
  cadence. Faster modes bring adulthood, elderhood and mortality sooner.
- Persist biological clock anchors and newborn life dates in schema 10, show
  biological ages on inhabitant cards, and keep adult work/social choices
  unavailable to minors. Old saves remain unchanged until the owner opts in.
- Connected separate parenthood proposals and consent to delayed, atomic births
  when food, shelter and caregivers remain available. Withdrawal, separation or
  loss of a parent cancels preparation; partnership alone never creates a child.
- Added dependent infants with physical needs and actual caregiver food/warmth
  delivery. Infants make no hosted-model calls or adult work decisions; the UI
  shows age bands and family-plan status. Schema 9 preserves preparation across
  restart without changing an older paused save.
- Connected adult partnership proposals to independent acceptance/refusal and
  unilateral withdrawal. Prior cooperation opens a choice, not automatic
  consent; unanswered proposals expire and rejected pairs have a cooldown.
  Partnerships are visible in the relationship panel and do not create children.
- Added persistent practical apprenticeships: eligible adults request a builder
  or farmer role, a qualified mentor independently accepts/refuses, and joint
  lessons at camp earn the role. Hunger, exhaustion, exposure, cancellation and
  mentor death cannot silently grant completion.
- Show work roles and lesson progress on inhabitant cards, preserve training
  across pause/restart in schema 8, and record bounded lesson-stage telemetry.
- Added a persistent household council: demonstrated contributors become
  stewards, but food-allocation changes require independent majority votes.
  A scarce-food reserve protects hungry members' access; rejection retains the
  previous rule. Policies survive leadership succession and restart.
- Show the steward, active food rule and vote counts in the Settlement panel.
  Schema 7 stores council state and ballots; older paused saves remain unchanged.
- Connected bounded inhabitant barter: surplus-for-needed-item offers, separate
  planning-provider acceptance/refusal, expiry and unusable-item cancellation,
  with no transfer until both parties agree. Completed exchanges leave public
  memories; pending offers and social notes appear in the settlement UI.
- Fixed idle reuse hiding choices created earlier in the same tick by another
  inhabitant. Cached decision context now reflects the provider's observation,
  not the later world state at execution time.
- Kept clothing, fire tending and warmth-seeking decisions on the assigned
  routine provider rather than accidentally routing them to the planning model.
- Connected weather exposure to warmth and recoverable illness, with clothing
  insulation, shelter, fuelled hearths, better rest from bedding and faster
  project work with carried tools. Inhabitants collect equipment and seek heat.
- Added perishable-food decay, slower household decay with a storehouse, and
  fresh-only consumption/reservation selection. Production now observes stock
  targets instead of endlessly manufacturing equipment.
- Weather now changes crop food yields and travel fatigue. Inhabitants prefer
  varied food sources; diet quality survives transfers and affects fatigue.
- Cancelled production safely when reserved ingredients become unusable,
  releasing remaining inputs instead of repeatedly failing the world tick.
- Fixed builders repeatedly selecting occupied or unreachable sites. Urgent
  exposure still permits protective construction and clothing work, while
  interrupting unrelated projects; exposure changes invalidate idle reuse.
- Added visible warmth/illness/equipment state and bounded survival-transition
  logs. Save schema 6 retains survival conditions and fire fuel deadlines;
  older paused saves are unchanged and receive no retroactive spoilage.

- Added persistent settlement projects with material acquisition, travel, work,
  interruption/restart recovery and explicit blockers. Other inhabitants can
  fulfil material requests; successful help leaves inspectable public memories.
- Added a versioned settlement supplement with stone, fiber and seed sources,
  hearth/weaving buildings, bedding, clothing, meals and grain production.
  Existing worlds receive additive resources only after resuming; schema 5
  preserves projects and validates additions against the original seeded map.
- Show shared stores, project progress and cooperation memories in the normal
  world/inhabitant UI, with technical identifiers relegated to diagnostics.
- Added a Settlement toolbar panel and secret-safe `settlement_activity` logs
  for project transitions and fulfilled requests, without logging content text.
- Kept authoring revisions monotonic after event-history compaction.

- Bounded hot event histories with durable, hash-verified archive segments and
  explicit stale-cursor snapshot resets. Save schema 4 keeps global event IDs;
  backups must include the save's adjacent `.history` directory.
- Hardened ticks against cancelled or slow providers: observations and pause
  stay responsive, and cancelled/superseded ticks leave no partial world state.
- Cancelled in-flight provider work when the last client lease expires or the
  owner pauses, and rechecked client presence before committing a proposed tick.
- Capped hosted-provider response bodies at 256 KiB before JSON parsing.
- Corrected reservation/barter expiry, automatic release on society clock
  advancement, asset-charge conflicts/overflow, and prefix-ID ledger restore.
- Made dependency quarantine block subsequent content activation and enforce
  dependency-first activation order. Active dependents must be rolled back
  before their dependency, preventing an unrecoverable content graph.
- Included crop work in owner job projections and kept empty-population worlds
  observable after all inhabitants die.
- Eliminated idle authority-file rewrites: one-use challenges are process-local
  and fail closed across restart; paired identities and revocations stay durable.

- Added per-inhabitant routine/planning provider and model overrides, with
  explicit inheritance from world defaults and shared host-only credentials.
- Added automatic, versioned starter content activation on the first resumed
  client-present tick: shelter, storage, cooking fire, workshop, crops, meals
  and tools. Existing paused worlds remain unchanged until resumed.
- Connected household food pickup to movement, inventory ownership and eating.
- Added per-inhabitant accepted decision, fallback, model, token and latency
  telemetry to the selection card without showing prompts or keys.
- Reused unchanged idle decisions across ticks and reloads, with a bounded
  reevaluation deadline and immediate reconsideration when legal choices change.
- Compacted cognition settings and centered the game menu independently of
  inhabitant selection, with engine-level layout regression checks.
- Limited the product roadmap to single-player; multiplayer is out of scope.

- Added structured, secret-safe live observability for cognition provider
  calls, authoritative intention outcomes, usage, latency, and world lifecycle
  gates so private-world behavior can be diagnosed from host logs.
- Added a deterministic .NET 10 headless simulation with an integer world
  clock, atomic ticks, durable pause/resume epochs, bounded recovery, and a
  pinned PCG32 random stream.
- Added seeded world generation with canonical terrain and resource manifests,
  stable cardinal routing, deterministic multi-inhabitant movement, contention
  resolution, legal direct swaps, and route-cache invalidation.
- Added survival simulation for hunger, energy, harvesting, eating, sleeping,
  renewable resources, and resource regeneration.
- Added canonical snapshots and event logs, schema migrations, save/reload,
  physical replay, compatibility checks, and digest proofs for deterministic
  world recovery.
- Added lot-based inventories with quantity, freshness, spoilage, reservations,
  ownership, capacity checks, atomic transfers, and exact barter settlement.
- Added durable world commands and messages with idempotency, stale-result
  rejection, and crash-safe processing boundaries.
- Added a private hosted world runtime with reconnectable observation, a
  browser diagnostic viewer, and a Godot client for normal play.
- Added owner-device pairing, signed owner requests, anti-replay protection,
  device management, and owner-only controls for pausing, resuming, and giving
  inhabitants instructions.
- Added deterministic cognition with bounded legal choices, validated
  responses, retries, fallbacks, usage accounting, provider-outage pausing,
  and restart-safe pending decisions. Supported adapters include the local
  deterministic provider, Jev, OpenAI-compatible providers, and Ollama Cloud.
- Added player-managed hybrid cognition settings. A paired owner can assign
  Deterministic or Jev to routine survival decisions, assign Deterministic,
  OpenAI, or Ollama Cloud to planning and work, and save, replace, or forget
  each provider's API key directly in the game.
- Added independently scheduled inhabitants with persistent identities, roles,
  needs, skills, intentions, inventories, and per-inhabitant cognition
  configuration.
- Added typed, consent-aware relationships, households, caregivers, social
  interactions, and atomic person-to-person trade.
- Added family and mortality simulation including birth, aging, death,
  tombstones, estates, inheritance, and household continuity.
- Added a four-inhabitant private world that saves and restores movement,
  needs, inventories, relationships, cognition, production, and world events.
- Added ecology and weather state, factions and laws, currencies, cultural
  state, and deterministic chunk manifests to the private-world simulation.
- Added governed data-only content packages with canonical IDs, semantic
  versions, dependency locks, validation, owner approval, staging, activation,
  rollback, and quarantine.
- Added typed content definitions for materials, buildings, recipes, and
  production, with deterministic preview and validation before activation.
- Added deterministic building placement, production jobs, ingredient and
  asset reservations, workstation checks, and inventory completion.
- Added asset governance with format and quota validation, rights and source
  metadata, provenance manifests, canonical package digests, deterministic
  cache accounting, previews, and portable artifact envelopes.
- Added public inhabitant intention and relationship summaries without
  exposing private model reasoning.
- Added inhabitant-chosen `build` actions. Inhabitants can independently choose
  valid structures or recipes; every generated world supplies reachable fertile
  land, and crop builds such as carrots complete into household inventory.
- Added a portable Windows 11 x64 game build and an automated verification
  pipeline that tests the simulation, starts the Godot client, exports Windows,
  and uploads the resulting artifact.

### Changed

- Main Menu, New World, save and load, founder setup, filter and Mod Library
  text now uses short, plain sentences. Save lists and messages show the
  in-world date and time instead of tick numbers, and error and confirmation
  messages say what happened and that your save is safe.

- Completed the pre-release ClankerWorld rename across save/content identifiers,
  package and signature domains, Windows user storage/device-key names, and the
  systemd template and installation paths. Previous development saves and
  pairings are not compatible; the old installation is retained as a rollback
  backup, while provider credentials are carried into the fresh installation.

- Renamed the game, .NET projects, Godot client and Windows export to
  **ClankerWorld**.
- Agent models are now told that the hunger number they receive is how well fed
  the agent is (10000 is full, 0 is starving). It was not explained before, so
  a model could read a low number as "not very hungry" and neglect eating.
- Changed private-world lifetime so simulation ticks and hosted-provider calls
  run only while at least one authenticated game client remains connected.
  Closing or losing the last client stops the world after a five-second grace
  period; reconnecting does not clear a manual pause or simulate offline time.
- Connection and server errors now read as plain sentences ("cannot reach the
  world server", "the server cannot handle another connection request") instead of raw HTTP status text.
- Changed inhabitant cognition from one provider request per person per world
  second to bounded, persistent intentions. Inhabitants now carry out legal
  movement, rest, gathering, eating, and building work locally until the plan
  completes, becomes invalid, or reaches a scheduled reevaluation; hosted Jev
  decisions for different inhabitants are dispatched concurrently.
- Animated inhabitant movement between observed tiles and added compact
  activity markers and intention summaries, making travel and current work
  visible without exposing private reasoning.
- Changed hosted-provider configuration to a durable private-world setting.
  Provider-role and model changes take effect at the next cognition boundary
  while stale responses from an older configuration epoch are rejected.
- Changed urgent survival candidate generation to withhold strategic building
  work until hunger and exhaustion are out of the critical range.
- Replaced the inspector-style Godot shell with a world-first, responsive 16:9
  play surface. The world now fills the screen beneath a compact clock and
  weather HUD instead of sharing space with permanent developer panels.
- Moved the inhabitants roster and recent events into temporary popovers, and
  moved settings, display controls, and developer tools into the pause menu.
- Changed inhabitant inspection to a closeable card anchored near the selected
  person. No empty selection panel is shown before a person is selected.
- Replaced raw ticks with a player-readable `Day N · HH:MM` clock and filtered
  routine movement, cognition, and tick noise out of the recent-events view so
  it can focus on meaningful world events.
- Removed protocol versions, fixture IDs, provider state, authoring drafts, and
  other implementation language from ordinary play; diagnostics remain
  available under Developer tools.
- Made the integrated private world the intended playable runtime and isolated
  its save and pairing authority from the preserved one-person fixture used by
  diagnostics.
- Expanded the map from a fixture grid into layered terrain, resources,
  buildings, and selectable inhabitants with contextual inspection.
- World Info, tile details, the Event Log and the memories panel now use plain
  wording: the year length, map size and soil moisture read naturally, event
  lines no longer mention internal steps, and agent plans read like "look after
  someone who is ill" or "offer a trade". Resource tips say "Grows back" or
  "Does not grow back".

### Fixed

- An unavailable or low-confidence model now leaves its agent on the explicit
  safe-idle fallback instead of silently choosing the highest-priority legal
  action. Pending instructions are retained rather than marked completed by
  that fallback; repeated failure no longer pauses the legacy fixture world.

- Fixed the four-inhabitant world deadlocking around a single berry tile.
  Inhabitants now route around occupied tiles, interact with resources from an
  adjacent tile, prioritize critical sleep, and suppress repeated blocked-path
  noise while they yield or retry.
- Fixed survival gathering so renewable ecology produces carried food rather
  than trying to transfer a depleted household fixture lot. Regenerating
  resources are no longer treated as harvestable, and inhabitants stop
  stripping the patch while they still carry food.
- Fixed re-pairing after a world-authority change by exposing a `Pair again`
  action in Settings; players no longer need to find and delete client files to
  replace an obsolete saved registration.
- Fixed restart and replay edge cases across inventories, reservations,
  owner-control idempotency, cognition scheduling, stale provider responses,
  and durable command recovery.
- Fixed client selection and map-layer interactions so clearing a selection,
  selecting an inhabitant from either the map or roster, and reconnecting all
  produce the same observation state.
- Fixed the game viewport so it scales to the display without making the whole
  play screen scrollable or reserving a permanent right-hand sidebar.

### Security

- Stored paired-owner signing keys as non-exportable Windows CNG keys and
  required explicit pairing approval before owner capabilities are granted.
- Stored provider credentials in a separate service-account-only file with
  atomic replacement and `0600` Unix permissions. Keys are accepted only over
  signed paired-owner requests, are represented by a digest in canonical
  request bindings, and are never returned to the client or written to world
  saves, replay digests, observations, telemetry, or the Windows client.
- Kept content packages data-only and fail-closed: executable mods, invalid
  dependencies, unapproved assets, and over-quota packages cannot activate.
