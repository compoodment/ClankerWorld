---
title: Playing the current game
type: player-guide
status: active
updated: 2026-09-30
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

## Create your first Town

1. Choose **New World**. Select a Small or Medium map, a seed and the available
   climate, water, wrapping and resource options.
2. Wait for the preview. Changing an option updates it; **Create World** becomes
   available when the preview for the current choices succeeds.
3. Create the world. It opens paused. Choose a rough site for its first Town;
   the game places two Houses, a Warehouse, Farmhouse, Blacksmith and Roads.
   You can choose another site before placing the founders.
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

## Look around

Use the mouse wheel to zoom and movement keys to pan. **Map** opens the overview;
click it or drag its view rectangle to move the camera. **Filters** controls the
saved Town border and household-owned building overlays.

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

Click bare ground to inspect the recorded facts there. Agents take priority
when they overlap the pointer. **World Info** includes Towns and their residents;
**Event Log** shows important events. Clicking an event with a known location
moves the camera there. Opening the log marks its new entries read.

**Add Agent** configures and places another adult after starting. Placement
shows the Town and household-owned buildings. Starting membership follows the
place you choose; it does not make the adult biologically related to anyone.

## Pause, settings and controls

The pause button pauses or resumes time. **Menu** opens the Pause Menu and keeps
the world paused. Opening ordinary inspection panels leaves time running.

Game Settings controls the display, interface scale, theme, weather effects and
date/time format. World Settings contains that world's autosaves, Jev and the
current model-usage controls. Main Menu Settings exposes Game Settings only.

Press **F1** or **?** for the in-game controls list. With no menu or text field
active, **Space** or **P** pauses, **N** selects the next agent, **Shift+N** the
previous one, **C** centers the selected agent and **H** returns to the opening
Town view. **Escape** closes the newest panel or mode, then opens the Pause Menu.

The optional model-call limit counts hosted call attempts across this
installation, including failed, retried or abandoned attempts. It is not a
currency budget or provider invoice. Reaching it pauses time. Add allowance or
remove the cap in World Settings, then resume separately.

## Save and return

**Save World** creates a named save for the current world. To overwrite one,
select that particular save and confirm; typing the same name does not overwrite
it. World Settings offers rotating autosaves as well as automatic recovery.
**Load World** shows existing worlds and warns about saves it cannot load.
Open and Delete are unavailable while another world action finishes.

**Quit to Menu** and Main Menu **Quit Game** ask for confirmation. Closing the
last client stops time and model work after about five seconds. Returning makes
no offline progress or change to a manual pause. An unfinished model call can
be cancelled and retried later; the provider may charge for both attempts.

To remove one snapshot, select it in Load Save and choose **Delete selected save**.
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
Some fixes still need a Windows playtest; include failures as well as successes.

## Advanced New World settings

The simple preset uses 50% water, Normal forest/mountains/rivers/resources,
Balanced climates, east/west wrapping and latitude cooling. Expand **Advanced**
to change water from 20% to 80%, relative Low/Normal/High settings, climates,
wrapping or latitude cooling. **Reset generation settings** restores the preset
and Small size without changing the name or seed. The preview updates after
changes; Create World waits for a matching preview. Small and Medium are
available; larger worlds remain unavailable pending their support checks.
