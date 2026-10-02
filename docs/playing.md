---
title: Playing the current game
type: player-guide
status: active
updated: 2026-10-01
---

# Playing the current game

This guide covers the private alpha. You need the Windows 11 x64 portable game
bundle and access to the private world server. There is no public server,
finished installer or multiplayer mode. [What works today](what-works.md)
explains the current limits.

## Connect your device

Start `ClankerWorld.exe` from the extracted bundle. Use the connection and
pairing screen to connect to the private server you have been given. Your device
must be able to reach it through its private network.

A new device needs approval. Compare the game's code with the person approving
the device on the server, then complete pairing. Do not post the code, keys or
private world data in an issue. Once paired, reconnecting uses the saved
registration; normal play does not need approval every time.

For an already paired device, **Connect** checks the saved server connection
and shows the result below the address. It does not start a new pairing.

If the server changes, forget the old registration and pair again.
[Device pairing](development/device-pairing.md) has the operator instructions.

If New World asks for matching updates, update the game and have the server
updated too. Keep your current device pairing; pairing again does not repair
that version mismatch. You can still reconnect to the existing world.

Adults may leave a household without its permission. Their own goods remain theirs even in the old House; collecting them and returning borrowed tools require travel and carrying space. A primary caregiver keeps dependent children with them unless another adult explicitly takes over care. An adult seeks an accepting existing home with room for the whole group first, then may start a household alone and build a House through the usual materials and site rules. Inspect the agent to see membership, goods awaiting collection, borrowed goods, care responsibility and the housing blocker.

## Create your first Town

1. Choose **New World**. Enter a name and seed and choose Small or Medium. Other
   map options are under **+ More options** (see
   [More New World options](#more-new-world-options)).
2. Wait for the preview. Changing an option updates it; **Create World** becomes
   available when the preview for the current choices succeeds.
3. Create the world. It opens paused. Choose a rough site for its first Town;
   greener shading and the hovered tips show nearby food, fertile ground, wood,
   stone and open space for Roads. These are suggestions only: the game checks
   whether the starter layout fits after you choose a site. It then places two
   Houses, a Warehouse, Farmhouse, Blacksmith and Roads. You can choose another
   site before placing the founders.
4. Configure and place **four starting agents**. Choose each agent's provider,
   key and model. A saved key can be reused, or you can add another for the same
   provider. Then pick the model from the game's short list for that
   provider, newest at the top. Models your key can't use are greyed out; if
   it can't use the model shown, the picker asks you to choose another.
   **Type a model name…** covers any other model. The agents start in two
   households, not two forced couples or biological families.
5. Press **Start World** when all four are ready. Placing the fourth agent does
   not start time automatically. Before starting, placements can be moved or
   undone; incomplete setup can be saved and resumed later.

Keys stay on the server installation, separately from world saves. Cloud
providers charge you for usage. Jev is an optional helper for the world; change
its on/off setting while the world is paused.

To save keys before placing any agents, open **Settings → Game → API keys**.
Choose OpenAI or Ollama Cloud, name the key, paste it and press **Save API key**.
You can save both providers' keys from Main Menu Settings once this device is
connected and paired. Saving a key does not run a model or place an agent.
Choose the saved key later in **Add Agent** or an agent's **Model** panel.

To check a selected model, press **Test model · 1 paid call** in Add Agent or
an agent's Model panel. This sends one real request and counts toward the
installation's paid-call limit, even if the provider times out or rejects it.
A key pasted for this check is not saved. The result tells you whether the
provider returned a reply the game can use; it does not show provider error
details.

## Look around

Use the mouse wheel to zoom and movement keys to pan. **Map** opens the World
Map, which marks each Town and agent; click it or drag its view rectangle to
move the camera. The label in the corner names the ground under the pointer
beside a small picture of it. Click a tile to open its card: the ground with a
picture of the tile, its height, climate, weather and soil, its Town, any land
title, household use right, pending use request or dispute, the household that
owns it, and anything on it, such as a tree, a field or goods left on the
ground.

**Filters** turns map overlays on and off: Town borders, household property,
Town land title, household land use and disputed land. Each one says what it
draws, and all start off. Town borders show as a pale dashed line. While you
place a founder or an added agent, the map shows all of them without turning
Filters on.

Click an agent to open a small card beside them: what they are doing and bars
for fullness, warmth and illness. **Profile** opens everything else on the left
of the screen: diet, belongings, work, private thoughts, relationships and the
Memories, Family and Model buttons. Click their thoughts, or **Read all**, to
read every recent thought in a larger panel beside the Profile. The pencil
beside their name renames them, and **Speak** goes straight to the message box,
where you choose **Suggest** or **Order**. The crosshair centers the camera on
them, and back (or **Escape**) returns from the Profile to the small card.
Inspection does not pause time, and reading private thoughts does not tell
other agents.

When agents talk, a chat bubble appears above each participant and nearby
listeners. Click a bubble to see who spoke and whether the conversation is
finished or interrupted. **Show history** opens the bounded public history
that this agent could hear. Reading it does not pause the world, and private
thoughts never appear in the conversation. An interrupted conversation stays
stopped until both participants choose to resume; loading a save never starts
provider calls for it on its own.

**Suggest** opens a **Suggestion** and **Order** opens a **Direct order**. Their
model does not read your words yet. A direct order works only when it asks them to gather
or harvest food, eat, or go somewhere, and "go" currently means walking toward
food. [What works today](what-works.md#agents-and-their-models) has the details.

Click a building to outline it and open a small card beside it: who owns it,
what is being made there or who is inside, and what it stores, shown as item
icons with the amount in the corner. **Details** opens the rest on the left:
who may use it, when it was built, which side its door faces, the work in
progress, a larger storage grid with each item's name, and who is inside or
lives there. Back (or **Escape**) returns to the small card.

Click bare ground to inspect the recorded facts there. Agents take priority
when they overlap the pointer. **World Info** includes Towns and their residents;
**Event Log** shows important events under a heading for each day, each with an
icon for its kind. An event with a known location has a **Find** button that
moves the camera there. Opening the log marks its new entries read; the ones
that were new keep a small dot while it stays open. While the continuity rule
is on, the top of the log offers **Add a newcomer**, which opens Add Agent.
Opening the offer adds nobody and makes no paid model call. **Agents** lists
everyone with their portrait and what they are doing, and tags anyone
**Hungry**, **Cold** or **Ill**. Click a row to find that agent, or
double-click it to open their Profile.

**Add Agent** configures and places another adult after starting. Placement
shows the household and Town that the selected tile would give the adult.
Household-owned property gives the adult that household; otherwise, a tile in
one Town gives Town membership without a household; outside a Town, the adult
starts an independent household. If household owners or Town borders overlap,
choose another tile. The preview does not reserve the land: the server checks
the current ownership and borders again when you place the adult, and asks you
to choose again if they changed. Walking later does not change membership.

## Pause, settings and controls

The pause button pauses or resumes time. **Menu** opens the Pause Menu and keeps
the world paused. Opening ordinary inspection panels leaves time running.
Settings and the Mod Library open inside the Pause Menu window; its back arrow
or **Escape** returns to the menu's buttons.

Game Settings controls the window, theme, weather effects and date/time format.
Menus, panels and text grow with your screen in whole steps, so pixel letters
stay crisp: 100% on small screens, 200% at 1080p and 1440p and 300% at 4K.
There is no setting for this. World Settings contains that world's autosaves,
Jev and the current model-usage controls. Main Menu Settings exposes Game
Settings only.
Autosave Apply stays unavailable until the current world's settings have loaded.

Press **F1** or **?** for the in-game controls list, with keys drawn as keycaps
and grouped by what they do. With no menu or text field
active, **Space** or **P** pauses, **N** selects the next agent, **Shift+N** the
previous one, **C** centers the selected agent and **H** returns to the opening
Town view. **Escape** closes the newest panel or mode, then opens the Pause Menu.

Press **F12** in a world to open **Developer tools** at the right edge of the
screen, and again to close them; **Escape** closes them once nothing newer is
open. Time keeps running. The panel shows the coordinates and facts of the last
tile under the pointer, the frame time, the tick time (how long the server took
to work out the latest step of world time) and how many agents are living.
Choose an agent in its list to select them and move the camera to them, and
turn on **Show planned path** to draw the route they are walking, as the server
planned it. The panel also holds the aging override, recovery for a request
whose reply was lost, paused world editing and paired-device management. The
aging override and world editing need the world paused first.

The optional model-call limit counts hosted call attempts across this
installation, including failed, retried or abandoned attempts. It is not a
currency budget or provider invoice. Reaching it pauses time. Add allowance or
remove the cap in World Settings, then resume separately.

## Save and return

**Save World** creates a named save for the current world. Its name starts as
the world's date, so **Save** works straight away; type another name first if
you like. To overwrite a save, choose its card, then **Overwrite**, and confirm;
typing the same name does not overwrite it. World Settings offers rotating
autosaves as well as automatic recovery. **Load World** shows each world as a
card with a small map of it, when it was last saved and its seed, and marks the
current world and any it cannot open. Double-click a card, or choose it and
**Open World**. Open
and Delete are unavailable while another world action finishes.

To go back to an earlier save, choose the current world in Load World and
**Load a save…**, then pick a save and **Load**. Your world as it was is saved
first. Playing on from an older save starts a new **branch**, so the saves from
the first version of events stay as they were. When a world has more than one
branch, each save card names its branch, such as **Branch 2**, and each branch's
newest save is marked **Latest**. Saves from before branches existed are listed
as **Earlier saves**. To load a save of another world, open that world first.

**Quit to Menu** and Main Menu **Quit Game** ask for confirmation. Closing the
last client stops time and model work after about five seconds. Returning makes
no offline progress or change to a manual pause. An unfinished model call can
be cancelled and retried later; the provider may charge for both attempts.

To remove one snapshot, choose it in **Load a save…** or Save World and choose
**Delete**.
The confirmation names the snapshot; deletion is permanent and leaves its world
and other saves alone. In Load World, **Delete World** removes that
world and all its saves. Open or create another world first if the target is
active. Canceling either confirmation changes nothing. These controls require
a paused world.

## Report what you notice

Use [GitHub Issues](https://github.com/compoodment/ClankerWorld/issues/new/choose)
for a bug or playtest report. Rough notes are welcome: what you did, what happened,
what you expected and the build you used.
[Contributing](../CONTRIBUTING.md#issues-and-design-questions) explains the report options.
Some fixes still need a Windows playtest; the [playtest list](../playtest/README.md)
says what to try. Include failures as well as successes.

## More New World options

The simple preset uses 50% water, Normal forest/mountains/rivers/resources,
Balanced climates, east/west wrapping and latitude cooling. Choose
**+ More options** to set water from 20% to 80% with a slider, pick Low, Normal
or High forest, mountains, rivers and resources, and change climates, wrapping
or latitude cooling. **Reset these options** restores the preset and Small size
without changing the name or seed. The preview updates after
changes; Create World waits for a matching preview. Small and Medium are
available; larger worlds remain unavailable pending their support checks.
