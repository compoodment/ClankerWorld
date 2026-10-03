---
title: Playing the current game
type: player-guide
status: active
updated: 2026-10-03
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

When a House is overcrowded, inspect a resident's housing details to see who
has notice to move out, why, how much time remains and whether expansion is
under way. Eligible adults get one world day; pausing stops the countdown,
and saving and loading keep the remaining time. Volunteers go first, then
the newest eligible arrivals outside the main family. With no family majority,
the same order applies without favoring a family.

An adult with notice may ask another household with room while still living
in the old House. Every adult in the new household must agree. If no home is
ready when notice ends, the adult leaves the old household and keeps seeking
a home. Only a finished expansion adds places; it can cancel an unnecessary
notice. Children and their sole caregivers are never forced out, including
together as a group. Their House stays overcrowded until expansion or a
voluntary move with care preserved provides enough room.

To rename an agent, open their **Profile**. The first name must be unused by
any other living or deceased agent in the world; changing the surname alone
does not free a taken first name. You can keep the selected agent's own first
name. A child's chosen surname must come from a biological parent. Temporary
names do not reserve first names, and renaming leaves past conversations as
spoken. Changing a married agent's surname updates both spouses together,
keeping the other spouse's first and middle names. A taken first name leaves
both names unchanged.

## Create your first Town

On a fresh server, **Continue** opens **New World** so you can choose the map
before placing your first Town. Existing worlds with founders or other work
still open normally.

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
   site before placing the founders, including after removing an empty starter
   Farmhouse or Blacksmith. **Redo Town site** replaces the starter layout and
   its Roads together.
4. Configure and place **four starting agents**. Choose each agent's provider,
   key and model. A saved key can be reused, or you can add another for the same
   provider. Then pick the model from the game's short list for that
   provider, newest at the top. Models your key can't use are greyed out; if
   it can't use the model shown, the picker asks you to choose another.
   **Type a model name...** covers any other model. The agents start in two
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
**Open model settings** in that panel opens the agent's model in World
Settings, where you can also delete a saved key.

To check a selected model, press **Test model · 1 paid call** in Add Agent or
an agent's Model panel. This sends one real request and counts toward the
model-call limit, even if the provider times out or rejects it.
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
of the screen: diet, belongings, work, their newest private thought, the people
in their life and the Memories, Family and Model buttons. The Model line names
the provider whose model made their latest choice, such as OpenAI; when Jev or
the built-in rules made it, the Profile says so instead. Click their thought,
or **Read all**, to read every recent thought in a larger panel beside the
Profile. The pencil beside their name renames them, and **Speak** goes straight
to the message box, where you choose **Suggest** or **Order**. The crosshair
centers the camera on them, and back (or **Escape**) returns from the Profile
to the small card. Inspection does not pause time, and reading private thoughts
does not tell other agents.

**Memories** shows what the agent remembers, believes and has mapped as cards,
newest first, with a tab for each kind. A belief shows how sure they are and
whether they saw it or heard it from someone; a map record shows each place
with a picture of its ground. **Family** opens their family tree beside the
Profile, with a portrait for each person and a heart between partners. Click
someone to open their Profile.

Handcarts appear as small wheeled carts on the map. Hover over one or inspect
its tile to see its owner, parked or pulled state, condition and actual cargo.
The owner's agent card also lists their carts. A broken cart keeps its goods;
its owner can unload here or repair it with carried wood, iron fittings and rope.
A cart holds loose goods separately from the agent's own carrying limit.

When agents talk, a chat bubble appears above each participant and nearby
listeners. Click a bubble to see who spoke and whether the conversation is
finished or interrupted. **Show history** opens the bounded public history
that this agent could hear. Reading it does not pause the world, and private
thoughts never appear in the conversation. An interrupted conversation stays
stopped until both participants choose to resume; loading a save never starts
provider calls for it on its own.

Partnered adults can propose marriage during a face-to-face conversation.
Both people decide with their own selected models. If they agree, a separate
**Shared surname** conversation follows, using only their two existing
surnames. It may continue while the partners are apart. They have at most four
alternating turns; if they still disagree, the game discloses a draw for the
surname. A failed call, pause or cancellation keeps the completed turns and
leaves marriage incomplete until the surname is settled. The agent's Profile
shows their spouse and whether that choice is still pending. Marriage is not
required to have a child.

**Suggest** shares an idea with the agent's own planning model. **Order** gives
it a task. A new suggestion or recognized order asks for one fresh planning
decision; any short reply comes in that same response. Following an order does
not add a model request for every step or tick.

Food orders can eat carried food, collect accessible household food to eat,
travel to a food source, or gather berries, fruit or wild greens. Use simple
phrases such as "eat 3 berries", "gather berries from berry-patch" or "go to
berries at (12, 4)". "Keep gathering food" continues until cancelled. Eating
waits until the agent is hungry enough, and gathering needs enough carrying
space. A recognized new order replaces the current one unless you turn on
**Queue**. Use **Cancel task** to stop a waiting or active order.

Material orders can gather wood, stone, plant fiber, clay, iron ore, gold ore
or diamond. For example, use **Gather five wood**, **Keep gathering clay**, or
**Gather stone at (12, 4)**. One ordinary load is the default; a quantity counts
the goods actually gathered, and the last whole load may exceed it. Agents use
their normal tools and carrying space. A named site is checked through travel
and observation, and an empty or unavailable site leaves the order waiting
with a reason. These orders are for adults and elders.

Use **Move to tile (12, 4)** to send an agent to an exact tile. **Go to (12, 4)**
and **Travel to (12, 4)** work too. The agent walks there normally and finishes
only on that tile. An occupied or unreachable destination leaves the order
blocked with a reason. These are single trips; queue another order for the
return journey. A destination in another household's House requires an
invitation. Urgent food or warmth needs can interrupt the trip, then it resumes.

The small card and the Profile show the order the agent is on now: whether it
is waiting, being done, interrupted or blocked, how far along it is, why it is
held up, and how many orders are queued after it. With no open order, they
show how the latest one ended. An order shows as finished only once the work is
done. **All orders**, beside **Your messages** in the Profile, lists every
order the world keeps for them: the current one, the queue in the order it will
be done, and their six latest closed orders. Each shows your words, and any
reply from the agent is set apart from the game's status.

When a child needs a guardian, an adult can follow **Become guardian for Lina**,
using the child's full name. The adult still has to be eligible in the current
guardian search. The order finishes when they accept primary care; moving the
child into their House still requires room and a shared Town. This order can be
queued or cancelled like a food task. It cannot replace an existing guardian.

An instruction the game cannot understand leaves the current order running.
Unsupported requests, such as "build a house" or "gather cloth," close as not
understood and appear in the Event Log. Recognized orders follow normal access
and survival rules. [What works today](what-works.md#agents-and-their-models)
has the details.

Click a building to outline it and open a small card beside it: who owns it,
what is being made there or who is inside, and what it stores, shown as item
icons with the amount in the corner. **Details** opens the rest on the left:
who may use it, when it was built, which side its door faces, the work in
progress, a larger storage grid with each item's name, and who is inside or
lives there. Back (or **Escape**) returns to the small card.

Every day has a night, from 19:12 to 04:48 on the clock in the top bar, with a
short dusk and dawn. In a six-minute day that is about 2 min 24 s of night. The
map turns a gentle dark blue but stays readable; names, agents and panels keep
their daytime colors. Nights are colder outdoors, so an agent with no clothing,
shelter or fire loses warmth, and their warmth bar shows it. A new world starts
at midnight.

Click bare ground to inspect the recorded facts there. Agents take priority
when they overlap the pointer. **Event Log** shows important events under a
heading for each day, each with an icon for its kind. An event with a known
location has a **Find** button that moves the camera there. Opening the log
marks its new entries read; the ones that were new keep a small dot while it
stays open. While the continuity rule
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

**World Info** has two pages. **Towns** lists each Town with its residents'
portraits, its council, its current and latest settled election, eight recent
proposal results and a **Show** button that moves the map there. Below the list
are each household's stores, the projects under way and how far they have got,
the household council and recent social activity; the page scrolls once it is
long. **World** shows today's date, the season and weather where you are
looking and the year length, then counts of agents, Towns, households,
buildings, road tiles, bridges, resource sites and the map size.

Town residents make civic choices through their normal personal-model turns.
Adults can agree to stand for election, visit their Town's public notice place
near its founding site, read notices, relay what they learned to someone nearby,
submit a proposal and vote. A traveler can miss notices; being a resident does
not automatically teach them every proposal or candidate. You inspect the
process in World Info rather than voting for the agents.

An ordinary proposal stays open for one unpaused world day unless its result
is settled earlier. A four-person council needs three yes votes, and an elected
three-seat council needs two. Silence supplies no approval. Election ballots
may change until closing, while cast proposal votes are final. Pausing stops
these windows and council terms.

An agent belongs to one Town or none. Traveling, visiting or losing a home
does not change it. An adult with no Town, such as one you add outside the
border, can walk to a Town's notice place and ask its council for admission. A
resident can also ask the council to admit an adult nearby who has no Town, and
that adult chooses whether to accept once they read the approval. Dependent
children join with their caregiver. Once admitted, a resident may collect their
Town's Warehouse stock in person, but approval never gives a House or household
place. The agent's Profile shows their Town, what it lets them do and any
admission in progress, and the Event Log notes approvals, admissions and
approvals that no longer apply.

Residents can also propose laws as a **subject: rule**, applying to the Town's
claimed land, a nearby recorded site, or residents wherever they travel. Council
votes adopt, amend or repeal them. The Towns page shows their scope and effective
dates. These are social rules: they do not physically prevent violations or
change who owns a building or its goods.

A resident at the Town's notice place can propose changing government without
the incumbent's permission.
The one-day resident vote needs a majority of its opening adult electorate;
death or departure removes a voter, while travel does not. Approval begins a
handover of at most three days. The Towns page distinguishes an approved proposal
from a completed handover and explains a failed transition.

An elected mayor may hold the land mandate, ordinary governing authority, or
both separately. Candidates personally agree to those specific mandates.
Residents choose one candidate and may revise their ballot. A tie means another
vote, never a draw. The term lasts twenty days, and a vacancy needs a new election.
Pausing stops votes, terms and handovers. The page shows officeholders and vacant
mandates; land hearings and enforcement have not been implemented yet.

## Pause, settings and controls

The pause button pauses or resumes time. **Menu** opens the Pause Menu and keeps
the world paused. Opening ordinary inspection panels leaves time running.
Settings and the Mod Library open inside the Pause Menu window; its back arrow
or **Escape** returns to the menu's buttons.

Game Settings controls the window, theme, weather effects, date/time format
and the model-call limit.
Dates name the season and its day, such as **Autumn 2, Year 1 · 14:20**; a new
world's year has four ten-day seasons: Spring, Summer, Autumn and Winter. The
top bar then shows the weather beside the date without naming the season again.
**Date display** can show numbers instead (DD-MM-YYYY, MM-DD-YYYY or
YYYY-MM-DD), and the top bar then names the season beside the weather.
**Time display** offers a 24-hour or 12-hour clock. Both apply at once.
Menus, panels and text grow with your screen in whole steps, so pixel letters
stay crisp: 100% on small screens, 200% at 1080p and 1440p and 300% at 4K.
There is no setting for this. World Settings contains that world's autosaves,
Jev and agent model settings. Main Menu Settings exposes Game Settings only.
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

To set up a test, pause and use **Edit selected agent**. Choose a need and its
percentage, goods and a quantity, a skill, or another agent for a partnership,
then press **Apply developer edit**. The server saves accepted changes and
marks each one **Developer edit** in the Event Log. Goods go into the selected
agent's carried load; removal protects equipped items, filled vessels, knowledge
records and reserved or borrowed goods. Starting a partnership requires two
living, unpartnered adults who are not close relatives. A refused edit leaves the world
unchanged; refresh the world view before trying again if the world changed.

**Settings → Game → Model calls** shows how many model calls this installation
has made and sets an optional limit. One count and limit cover all your worlds.
Every call attempt counts, including failed, retried or abandoned ones; tokens
reported by providers are shown for information only. The count never resets
by itself, and loading an older save does not lower it. It is not a currency
budget or provider invoice. When the count reaches 80% of the limit, the Event
Log warns you once. Reaching the limit pauses time: raise or remove the limit,
or choose **Allow 100 more calls**, then resume separately.

## Save and return

**Save World** creates a named save for the current world. Its name starts as
the world's date, so **Save** works straight away; type another name first if
you like. The card under the timeline says whether the new save continues a
branch or starts a new one. To overwrite a save, choose it, then **Overwrite**,
and confirm; typing the same name does not overwrite it. Autosaves can't be
overwritten. World Settings offers rotating
autosaves as well as automatic recovery. **Load World** shows each world as a
card with a small map of it, when it was last saved and its seed, and marks the
current world and any it cannot open. Double-click a card, or choose it and
**Open World**. Open
and Delete are unavailable while another world action finishes.

To go back to an earlier save, choose the current world in Load World and
**Load a save...**, then pick a save and **Load**, or double-click it. Your world
as it was is saved first. Playing on from an older save starts a new **branch**,
so the saves from the first version of events stay as they were. To load a save
of another world, open that world first.

Load Save and Save World open on a **timeline** of the world's saves: a line for
each branch across the world's days, with the seasons along the top. A new
branch bends down from the save it grew from. Each branch's newest save has a
small banner, older saves are open circles and autosaves are small diamonds;
the key above the timeline shows which is which. **You are here** marks the
world as it is now, at the end of its branch, or on a dotted **New branch** row
when your next save will start one. Click a save to see it described
underneath; the arrow keys also move between saves. **List** shows the saves as
cards instead, grouped by branch, with tags such as **Branch 2** and **Latest**.
Saves from before branches existed are shown as **Earlier saves**.

**Quit to Menu** and Main Menu **Quit Game** ask for confirmation. Closing the
last client stops time and model work after about five seconds. Returning makes
no offline progress or change to a manual pause. An unfinished model call can
be cancelled and retried later; the provider may charge for both attempts.

To remove one snapshot, choose it in **Load a save...** or Save World and choose
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
