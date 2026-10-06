---
title: Playing the current game
type: player-guide
status: active
updated: 2026-10-07
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

When a child loses their last caregiver, relatives are asked first, then adults
in the child's household and Town. Until someone accepts, the child shows
**Needs a guardian**. Accepting care does not mean the child has moved in: a
guardian in another Town must have a completed House with room, collect the
child and accompany them home. Inspect either agent to see whether they are
waiting for a home, meeting up or travelling. The child joins the guardian's
household and Town only when they arrive together and the House still has room.
Birth parents and family history stay unchanged.

A household can make a crude wooden axe and pickaxe at its own House from
wood stored there. Nearby fallen wood can be gathered by hand to get started.
The axe fells trees and the pickaxe extracts stone, so a household does not
need a Town or Blacksmith for those first tools. Crude tools gather less and
wear out sooner than the Blacksmith's wooden tools. Agents collect and use a
better usable tool when one is available. Inventory and plot details name the
crude tools, and the Event Log records completed work; their item pictures
still use the generic icon.

When a House is overcrowded, inspect a resident's housing details to see who
has notice to move out, why, how much time remains and whether expansion is
under way. Eligible adults get one world day; pausing stops the countdown,
and saving and loading keep the remaining time. Volunteers go first, then
the newest eligible arrivals outside the main family. With no family majority,
the same order applies without favoring a family. A forced replacement keeps a
still-future deadline, but gets a fresh world day when the old deadline is
already due. Volunteers keep the original deadline.

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
3. Create the world. It opens paused at 06:00 on Spring 1, Year 1, in daylight.
   Choose a rough site for its first Town;
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

Handcarts show their empty or loaded bed at close zoom and face the way they
last moved. A loaded cart pulled east or south-east lifts its shafts; other
directions use the parked picture. At smaller zooms, carts use a small wheeled
icon. Hover over one or inspect its tile to see its owner, parked or pulled
state, condition and actual cargo.
The owner's agent card also lists their carts. A broken cart keeps its goods;
its owner can unload here or repair it with carried wood, iron fittings and rope.
A cart holds loose goods separately from the agent's own carrying limit.

Boats show their last heading and rowing or shipped oars. Hover over one or
inspect its tile to see its Town, passenger, carried goods and travel state.
A Port’s card shows how many of its six spaces are moored or reserved for
incoming boats. **World Info → Towns** shows boats and active trip requests.
Agents may propose paid Ports and boats through the Council, then request
travel between completed Ports. Visitors need Council permission. A blocked
arrival waits one world day before returning to a usable departure Port.

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

To put carried raw materials away, use **Store wood**, **Store five clay in my
House**, or **Keep storing stone**. The agent walks to its household's House
and keeps ownership of the goods. A plain request stores one carried lot, up
to the room available; a quantity stops at that exact number. Missing personal
materials or a full House leaves the task waiting with a reason. Adults and
elders can also store unworn personal equipment and the goods listed below.
Add **in my House at (12, 4)** to require that House's listed tile. Once the
task chooses a House, it keeps that destination; losing access or moving the
House leaves it waiting instead of switching homes.

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

To pick up personal raw materials, use **Collect my wood**, **Collect three
clay**, or **Keep collecting stone**. Adults and elders walk to their own goods
in their current or former household's storage, or where they were dropped.
They keep ownership and use normal carrying space. A plain request collects
one load; a quantity stops exactly at that number. Full hands or unavailable
goods leave the task waiting. Add **from (12, 4)** to require that source tile.
You can also collect personal food or equipment without eating or equipping it.
Named meals include **Collect two bread**, **Collect berry porridge** and
**Collect restaurant meals**. These collect only the agent's own goods.

Collection and storage also recognize rope, cloth, refined iron and gold,
workshop tools, grain, flour, potatoes, named planting seeds, medicinal herbs,
bandages, medicine, storage pots, water jugs and named ornaments. For example,
use **Collect two refined iron**, **Store three grain seeds in my House**, or
**Collect one water jug at (12, 4)**. Name tree, grain, cultivated-green or
orchard seeds; bare **seeds**, **tools** and **ornaments** are not understood.
A pot or jug moves with its contents and counts as one item, while all of its
contents use carrying and storage space. These tasks cannot take household
production stock as personal property.

To return borrowed stock, use **Return two borrowed cloth**, **Return two
borrowed bread** or **Return one borrowed water jug**. The agent carries the
goods back to their owning
household's House. Its owner stays the same, and only goods actually put away
count. The task keeps its chosen House; missing space, reserved goods or a
blocked route leaves it waiting. **Queue**, **Cancel task** and save/reload
retain the same quantities and destination rules.

To work on household fields, try **Till two fields**, **Plant grain**,
**Plant two fields of potatoes**, **Tend cultivated greens**, or **Keep
harvesting fields of grain**. Adults need a household with a Farmhouse and
suitable fields, stock and tools. Counts mean completed fields, so include
**fields** when giving a number; **Harvest five potatoes** is not understood.
A named crop is never replaced with another. Harvests remain on the field
until carried. Cancel or replace the order to stop the current work and release
unused planting stock. Add **at (12, 4)** to keep the task on that field tile.

To move supplies for a household or Town, name both the goods and the destination:

- **Haul four grain to my Farmhouse** moves available household farm stock.
- **Supply two iron to my Blacksmith** supplies inputs the workstation currently needs.
- **Deliver two berries to my House** shares spare food the agent already carries.
- **Donate two wood to my Town Warehouse** gives spare carried materials to the Town.
- **Stock three cloth in my Store** fills the household's shop under its normal stock limits.

Add **at (12, 4)** to require the building's listed tile. The order keeps its
chosen destination. A plain request delivers one load; a number counts the
requested goods actually delivered. Pickup and travel earn no progress.
Demand, carrying space, building capacity and the agent's normal personal
reserves still apply, so a task can wait before its requested total is reached.
Filled vessels need space for their whole contents. A water delivery moves a
whole jug and counts the water delivered; it cannot pour out part of a jug to
meet a smaller request. Cancelling keeps goods where they actually are,
including any supplies already picked up for delivery.

To mend worn personal equipment, use **Repair my basket**, **Repair two padded
coats**, or **Keep repairing basic garments**. Adults and elders can repair basic
clothing, padded coats, rain cloaks, baskets and sacks. They collect real supplies
and use their household's House for baskets or Tailor Shop for the other items.
Only finished repairs count. Missing items, materials, carrying space or a work
site leaves the task waiting. Queue, cancellation and save/reload also work for
these orders. Tool orders also accept exact kinds, such as **Repair two iron
knives**. Weapons and named work sites are not supported repair targets.

To make goods, try **Make two sacks**, **Make rope**, or **Mill flour**. Adults
and elders use the usual work site, ingredients and tools. A plain request
means one recipe batch; an item count must fit the recipe's whole batches.
For example, **Make two house bandages** uses one batch, while **Make one
house bandage** is not understood because that recipe makes two. **Make two batches of
house bandages** makes four. **Keep making rope** repeats until cancelled.
Bandages name **house bandages** or **tailor bandages**.
Add **at (12, 4)** to use only the work site at that tile. Missing stock or an
unavailable work site leaves the task blocked with a reason. Goods retain
the normal recipe ownership and storage; ordering them does not make them
the agent's personal property. Queue, cancellation and save/reload preserve
the remaining task. Urgent survival pauses unfinished production.

Cooking orders name the recipe: **Cook potato meals**, **Cook wild green meals**
or **Cook cultivated green meals** makes simple meals at the House. Use
**Cook house porridge**, **Cook house berry porridge**, **Cook house fruit porridge**,
**Cook house bread** or **Cook house vegetable stew** for its other recipes.
Replace **house** with **restaurant** to use a 1×2 Restaurant, or **restaurant 2x2**
for its larger size. **Prepare restaurant meals** turns bread and greens into
Restaurant meals. Every cooking batch makes two servings: **Cook two house bread**
makes one batch, while **Cook two batches of house bread** makes four servings.
Odd serving counts and ambiguous commands such as **Cook porridge** are not understood.
**Supply two grain to my House** and **Supply two flour to my Restaurant** bring
ingredients under the normal demand and source-reserve rules. Fresh water travels
in a whole jug. **Deliver two bread to my House** shares spare carried bread while
keeping one serving; **Stock restaurant meals in my Store** uses the normal shop
limits. Loose personal meals are collected or shared, rather than put away with
the nonfood **Store** command.

To start household building work, use **Build a Clinic**, **Build a Silo** or
another supported household building name: House, Farmhouse, Blacksmith,
Tailor Shop, Store or Restaurant. **Build a Restaurant** starts its 1×2 size
and costs 8 wood and 2 stone. Add **at (12, 4)** to require that site. Otherwise the
agent chooses a suitable site under the normal construction rules. The task
keeps that choice through travel and work. It still needs real materials,
household access, a legal site and any prerequisite building.

Use **Expand my House** or **Expand my Town Warehouse** for the next supported
size. Expansion still needs the normal household or Town need, room and
materials; an order does not bypass those requirements. Each building order
requests one building or one expansion stage. Repeating requests and larger
counts are not understood. Progress reaches one only when the building work
actually finishes. Queue, cancellation and save/reload preserve the task;
cancelling leaves collected goods in place and releases unused expansion
materials. Urgent survival pauses the work before it resumes.

Use **Seek shelter**, **Take cover** or **Shelter in my House** to reach
permitted cover. Add **at (12, 4)** to require that location. A storm guest
still needs a current invitation, and natural cover protects only during a
storm. The task finishes when the agent reaches usable cover; this does not
mean they have recovered their warmth.

An adult can follow **Light a fire**, **Tend the fire** or **Light the fire in
my House**, also with an optional location. The agent brings eligible wood
to an accessible unlit hearth. Progress reaches one only after one real wood
is consumed and the fire lights. A fire that is already burning earns no
progress and is not extended by the order. Gather wood first if no usable
source is known. Both shelter and fire requests
are single tasks; counts, repetition, durations and **Warm up** are not
supported. Urgent food can pause either task; urgent cold does not stop the
agent from seeking protection. Queue, cancellation and save/reload preserve
the task and its selected destination.

An instruction the game cannot understand leaves the current order running.
Unsupported requests, such as "build a castle" or "gather cloth," close as not
understood and appear in the Event Log. Recognized orders follow normal access
and survival rules. [What works today](what-works.md#agents-and-their-models)
has the details.

Click a building to outline it and open a small card beside it: who owns it,
what is being made there or who is inside, and what it stores, shown as item
icons with the amount in the corner. **Details** opens the rest on the left:
who may use it, when it was built, which side its door faces, the work in
progress, a larger storage grid with each item's name, and who is inside or
lives there. Work lists what a batch uses and makes, separately from the
materials currently held for that job. **Can make** lists the recipes registered
for that exact building size and their quantities. Each still needs real
materials and an adult allowed to work there. Reading the list teaches no
agent and starts no work. Back (or **Escape**) returns to the small card.

Every day has a night, with a short dusk and dawn. Nights are longest in winter
and shortest in summer: from 19:12 to 04:48 on the clock in the top bar at the
start of spring and autumn, about 2 min 24 s of a six-minute day; from 20:24
to 03:36 at the start of summer; and from 18:00 to 06:00 at the start of
winter. The map turns a gentle dark blue but stays readable; names, agents and
panels keep their daytime colors. Buildings in use glow from their windows and
doors: a House while someone is home, a workshop while someone works there,
the Blacksmith's forge while it is busy, and a Warehouse's door lantern while
goods are fetched. Nights are colder outdoors, so an agent with no clothing,
shelter or fire loses warmth, and their warmth bar shows it. New worlds start
in the morning; loading a saved world keeps its recorded time.

Click bare ground to inspect the recorded facts there. Agents take priority
when they overlap the pointer. **Event Log** shows important events under a
heading for each day, each with an icon for its kind. An event with a known
location has a **Find** button that moves the camera there. The log grows to
fit wrapped text and day headings until it reaches the bottom of the screen,
then scrolls. It adjusts when the window changes size. Opening the log
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
that adult chooses whether to accept once they read the approval. They have
one unpaused world day from the council's approval to accept; the Profile shows
the deadline as a world date and time. An unaccepted approval expires, and
someone may ask the council again later. A newcomer who made their own request
joins as soon as it passes. Dependent children join with their caregiver. Once
admitted, a resident may collect their Town's Warehouse stock in person, but
approval never gives a House or household place. The agent's Profile shows
their Town, what it lets them do and their admission status, and the Event Log
notes approvals, admissions and approvals that no longer apply.

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
An election the current Council opened on its own continues independently,
even if someone leaves the Town while the proposed change waits for a successor.

An elected mayor may hold the land mandate, ordinary governing authority, or
the separate non-land hearing mandate. Candidates personally agree to those specific mandates.
Residents choose one candidate and may revise their ballot. A tie means another
vote, never a draw. The term lasts twenty days, and a vacancy needs a new election.
Pausing stops votes, terms and handovers. The page shows officeholders and vacant
mandates. Giving an existing mayor the non-land duty needs their own acceptance
as well as the resident vote; it keeps the end date of the named existing term.

An adult in an affected household can propose transferring an exact plot of
recorded use permission to another household. Every current adult in all giving
and receiving households must learn the published terms and personally accept;
proposing, reading and silence supply no acceptance. A new adult joining while
it is pending must accept too. This ordinary transfer needs no Council or mayor
approval. It preserves the original grant date and any agreed end date, and
moves only use permission. Town title, membership, buildings, crops, goods and
private-building access stay unchanged. Expired, disputed or under-review land
cannot use this route. Town, plot and property details show the exact terms,
names, dates and each household's acceptance progress.

An affected household can ask for a land hearing, and the Town can file through
its legitimate government. A permission with an agreed end date enters review
when that date passes; it remains provisional while the case is pending. The
Towns page shows the plot, affected households, formal response deadline,
evidence sources and who may decide the case. Publication does not mean a notice
was read: adults learn it through actual reading or a relay. Each affected adult
may answer or explicitly waive their own response; silence is neither agreement
nor evidence against them.
The page separates the parties named when the notice was published from current
adult responders and the Town's current authorized representative. Each ruling
keeps the parties who were represented when it was made.

The land mayor may confirm, renew, change or end use permission, recording the
evidence and reasons. A permission past its agreed end must be renewed, changed
or ended; renewing renews every lapsed permission on the plot for its own household. A mayor with a personal or household conflict stands aside;
residents can elect a willing, independent adult for that case only. A case waits
if nobody qualifies. A rehearing requires material new evidence or a demonstrated
procedural error. The old ruling remains recorded and current rights stay in
effect until a valid correction. These decisions leave Town title, household
membership, buildings, crops and goods with their holders; they neither evict
occupants nor grant entry to private buildings. Plot and building details show
the hearing, and the disputed-land filter marks plots still under review.

Witnesses, affected people and the legitimate Town government can report
specific conduct under a law that applied when it happened. Visitors have the
same response rights for local conduct. A report keeps its sources separate
from the eventual finding. The non-land adjudicator must hear the parties,
inspect the evidence and record reasons and uncertainty; a conflicted holder
stands aside for an independent case judge. Without lawful authority the case
waits. A child's notice and response are supported by their caregiver, without
making the household responsible for the child's conduct.

The result can be an explanation, warning or censure. Restitution, repair and
public service are voluntary offers with exact named contributions. Each adult
contributor must learn and accept their terms; declining or leaving an offer
unanswered adds no offence. The response window lasts one unpaused day and the
usual completion period is three days after acceptance. A delivery or repair
counts only when it actually happens. The Towns page distinguishes unanswered,
declined, accepted, overdue and completed work. These hearings do not impose
fines, seize property, expel people or force work.

An adult resident can propose a named Town Hall on clear Town-titled land, and
the Council votes through the same process as an ordinary proposal. In
**World Info → Towns**, its plan shows the site and entrance, exact budget and
Council result. An approved project also shows delivered materials, construction
progress and anything blocking it. Council approval supplies no building or
materials by itself.

Residents must learn the proposal from notices or a nearby relay before helping.
They carry available Town materials in real loads and work at the approved site.
If they gather new wood or stone, those goods stay personal until their own model
freshly chooses to donate them. **Speak → Suggest** may ask an adult to consider
helping or donating; an Order cannot give away their goods. After completion,
select the Hall for its project and payment details. Agents read civic notices
there, with the same nearby learning and relay rules as before. A blocked project
keeps its materials as Town property and releases unused claims rather than
building for free. If its site can no longer be used, the Town stops the
project and the Towns page says why; its leftover materials stay Town
property where they are. See [What works today](what-works.md#life-work-and-society)
for this first version's limits and provisional amounts. Its
[hands-on checklist](../playtest/767-town-projects.md) remains pending.

To begin a Market, use **Speak → Suggest** to ask an adult resident to consider a
named Market proposal. **World Info → Towns** shows its site, exact budget,
Council result, supplied materials and work. Its first project pays for the
hall and two stalls on a fixed 7×4 plaza. Once the existing stalls are borrowed,
another stall needs its own Council approval and delivered materials.

Once it is built, select the hall or a stall to see who borrows it, the goods
actually there, their owners and any offered exchange. Adults may choose to
carry surplus there and borrow a free stall. Buyers, including visitors who
belong to no Town, bring personal payment, and the named seller chooses whether
to accept when both have reached the stall. The buyer carries the purchase;
payment becomes the seller's household stock there. Leaving frees the stall;
leftover goods still belong to their
recorded owner. Suggestions can guide these choices, but an Order cannot force
a sale or give away goods. The
[Market hands-on checklist](../playtest/564-market-stalls.md) remains pending.

Residents can also propose a **stone street lamp** or **hanging street lantern**
beside an existing Road. The Council approves its design, Road edge and exact
budget before anyone builds. Trial costs are **4 stone**, or **4 wood and
1 refined iron**, with the same provisional 10 work units as other Town
projects. Residents carry and spend real Town materials; household stock is
not taken. The fitting occupies one clear roadside tile on Town-titled land
and keeps the edge that was approved. The Road itself stays open.

Completed lanterns show by day, light at dusk and go out at dawn. They need no
fuel and do not change what agents can see or do. Select the visible fitting for
its project and payment details. The [street-lantern checklist](../playtest/892-street-lanterns.md)
still needs a hands-on check.

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
If the world changes before Apply reaches the host, its settings stay unchanged.
Reopen World Settings before trying again.

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
The confirmation also opens when you manage the current world's saves straight
after starting the client. It names the snapshot; deletion is permanent and
leaves its world
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
**Reroll** chooses another seed and clears the old map while its replacement
loads. You can reroll during generation; the preview will follow your latest
seed. If the world name is missing, enter it and choose **Preview again**.
