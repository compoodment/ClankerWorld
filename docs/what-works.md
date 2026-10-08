---
title: What works today
type: product-status
status: active
updated: 2026-10-07
---

# What works today

This describes the **current repository build**, not every feature in the
[game design](game-design/README.md). The private alpha uses a Windows 11 x64
Godot client connected to a private world server. Repository changes may still
need deployment or testing on the owner's laptop. Check the tested build on an
issue before treating a source fix as a verified game fix.

**Settings → Game** and **Developer tools (F12)** show the client build, for
example `Build 0.1.0-dev+abc1234`. Include that line in a bug or playtest report.
Hover over it for the full source commit. The host reports its own version and
full commit at `/api/v1/status`; client and host builds can differ.

## Status labels

- **Available in the game:** connected to the normal private-world and Godot path.
- **Basic version:** connected, but the experience or rules are still limited.
- **Built but not connected to normal play:** code and isolated tests exist,
  but players do not yet encounter the complete feature.
- **Not built yet:** an intended feature still needs implementation.

Availability and verification are separate. A feature can be connected in the
repository build while its Windows playtest is pending. A passing isolated
test alone does not make it available in the game.

## World and controls

| Feature | Status | Current limits |
| --- | --- | --- |
| Create, select, save and load worlds | Available in the game | Small/Medium maps; larger playable worlds and a local Windows host are unfinished. Default Balanced Small/Medium previews try up to three candidates for the Normal forest and mountain targets. |
| Wooded forests, patchy beaches and reduced wet weather | Available in newly created worlds | Forest grass has many trees, forest-floor tiles always have trees, and cacti stay on desert sand. Default rain, storm and snow weights are one quarter lower across climates. Density and Windows performance still need owner playtesting. |
| Choose the first Town and place four founders before starting | Available in the game | On-map, guidance-only hints for nearby food, fertile ground, wood, stone and open space for Roads; exact factor tuning remains provisional. Player-chosen supplies are unfinished. |
| Move around and inspect the map | Available in the game | Zoom, overview, wrapped east/west movement, tile facts, building cards with stored items as icons, and separate filters for Town title, household use, disputes, building property and Town borders. The disputes filter stays empty in normal play, because land requests cannot be filed yet. Building Details lists the workstation’s registered recipes with input/output quantities, and running work shows its recipe and the materials held for it. Details also lists recent recorded storage additions and removals. |
| Approved art for playable content | Available in the game | Clinics and Restaurants have their approved exteriors. Medicinal herb patches and picked or depleted natural sites use their matching drawings on the map and tile cards. Potatoes, cultivated green seeds, medicinal herbs, diamond ornaments and simple meals use their approved item icons. Refined gold, gold ore, plain gold ornaments, iron fittings, saddles, cooked eggs, milk, berry and fruit porridge, rich meals and leather sacks have their own icons from the October 7 art review; crude wooden axes and pickaxes use the wooden tool icons. [Windows visual checks](../playtest/792-approved-playable-art.md) are still wanted. |
| Pause, inspect agents, view family trees and read events | Available in the game | The Agents list keeps its browsing position through information refreshes; selecting another agent still brings that card into view. Deceased profiles retain recorded thoughts and memories, and show any final will and final words; old deaths without an archive cannot be reconstructed. |
| Developer tools | Available in the game | **F12** opens them in a world without pausing it: the tile's coordinates and facts, frame time, how long the server takes per step of world time, the agent count, jumping to an agent and drawing their planned path, plus the aging override, lost-reply recovery, paused world editing and paired-device management. While paused, **Edit selected agent** sets fullness, warmth, illness or nutrition, gives/removes carried goods, adds/removes skills, and starts/ends partnerships; each accepted change is saved and marked **Developer edit** in the Event Log. There are no time tools such as stepping one tick. The Windows playtest is pending. |
| Display and interface settings | Available in the game | Themes, window size, weather switches, a 24-hour or 12-hour clock, and dates by season (the default, such as Autumn 2, Year 1) or as DD-MM-YYYY, MM-DD-YYYY or YYYY-MM-DD. A server too old to report season lengths shows numeric dates. The interface grows with the screen in whole steps (100%, 200% at 1080p and 1440p, 300% at 4K) with no setting, and the game always draws at the screen's own resolution. Windows visual and keyboard acceptance is still being checked. |

Building Details shows up to ten recent recorded storage changes, newest first,
with the exact additions or removals and when they happened. Moving a filled
vessel includes its contents. These records survive saving and reconnecting;
older records follow the existing bounded history archive. An empty list says
no recent changes were recorded, and inspection teaches agents nothing.

Building quick cards and Details show a storage-space bar and exact occupied
space against the building's recorded limit. The count includes all physically
stored goods, including other owners' goods and materials held for work; the
household's item grid is a separate view. An older host without a recorded
limit keeps its item grid and has no guessed capacity bar.

On a fresh server, Continue opens New World instead of entering the retired
test camp. The normal map preview and acceptance steps still apply, and saved
worlds with founders or other progress keep opening normally.

New worlds open paused with no old camp. Choosing the first Town lays a winding
main road with side streets, then places two Houses, a Warehouse, Farmhouse and
Blacksmith along them, each with its door facing its packed-dirt Road and a short
doorstep path joining it. Streets run on a few tiles past the last building,
and the Town border keeps about three tiles of spare land around buildings
and Roads. The Houses hold
household food; the Warehouse supplies a wooden axe and pickaxe. Four unrelated
founders form two starting households: the first holds the Farmhouse and the
second holds the Blacksmith. The player presses Start World explicitly.

Default Balanced Small/Medium previews measure forests and mountains against
dry land and try at most three deterministic maps for the Normal settings. The
preview describes the selected map in plain words, such as "Plenty of forest
and some mountain ranges". Its tooltip gives the measured shares of every
playable map tried and lists, in the order tried, attempts that have no room
for a first Town. Create World makes the selected map exactly. If no attempted
map has room, it asks the player to try another seed or change the settings.
If the selected map has less or more forest or mountains than a balanced
world, the preview says so, and Create World stays unavailable until the
player ticks **Keep this map anyway** or changes the seed. Uniform Dry and
polar-only settings do not use those trial bands; each band applies only while
its own forest or mountain control is Normal. This is connected to the normal
owner/server path; the current measurements are
automated fixture evidence, not a Windows visual playtest or a final balance
claim.

World time and hosted calls stop after the last connected client's short grace
period. Returning makes no offline progress; a manually paused world stays
paused. New worlds use six-minute days and a 40-day year as the current playtest
pace, subject to model/server load.

Every world day has a night, longer in winter and shorter in summer: 40% of the
day at the start of spring and autumn (19:12 to 04:48 on the clock, about
2 min 24 s of a six-minute day), 30% at the start of summer (20:24 to 03:36)
and 50% at the start of winter (18:00 to 06:00), changing a little each day in
between. Dusk and dawn each fade over an hour (15 seconds at that pace). A new world starts at
06:00 on Spring 1, Year 1, after dawn, so founder setup happens in full
daylight. Loading an existing world keeps its saved clock. At night the map darkens with a
gentle blue wash at every zoom, under map names, agents and weather, and it is
colder outdoors (see [Life, work and society](#life-work-and-society)).
Buildings in use glow: light falls on the ground from the windows on a
building's front and sides and from its open door, never from its roof. A House
is lit only while someone is inside; a Farmhouse, Store, Tailor Shop, Workshop
or other building while someone is inside or a job runs there; and the
Blacksmith's forge glows in its yard while it works. A Warehouse shows only a
lantern by its loading doors, lit while someone fetches or stores goods, and a
Silo stays dark. Each pool of light has a ragged edge that drifts slightly, and
the forge flickers. Zoomed out, a lit building is a warm speck. Night lights have
not been checked by hand in the Windows game yet. Night
adds no rules of its own: agents need no sleep or energy, and nothing limits
their choices or travel at night; they only react to the cold. The night chill
is a provisional amount for playtesting, and night has not been checked by hand
in the Windows game yet. Night effects on weather and night length that changes
with the seasons are not built.

## Agents and their models

Each starting agent has a provider, model and key assignment. Stored keys can
be shared or selected independently. An agent's Profile lets you change these
choices and rename the person. For OpenAI and Ollama Cloud, the model is picked
from the game's own short list, newest at the top, or typed by name. The host checks
the chosen key with the provider and greys out listed models it can't use. That
check and the listed model names have been tested against recorded sample
replies, not yet against live provider accounts. A paused world's routine
helper can be Off, Jev or OpenAI Decisions, with its own model picker. Decisions
uses a saved OpenAI key and handles the same routine choices and owner-private
memory scoring as Jev. Switching cancels pending work and preserves memories.
The Decisions integration is checked against the published API contract and
sample replies; the live-account Windows check is still [pending](../playtest/magical-heisenberg-mwvofv-routine-helper.md).
When a personal model names a new agent, it gets a stable first-letter hint to
encourage varied names. Each chosen first name is unique across the world,
including deceased agents. Different surnames, capitalization or spacing do
not make a taken first name available. The game asks once more after a
collision, without showing anyone else's name. If the second answer is
unavailable or also taken, the person keeps the placeholder until the player
changes it. Temporary names never reserve a first name, even after automatic
naming ends.

Player renames follow the same rule. An agent may keep their own first name
while changing the rest of their name. The Profile explains that the name is taken and
keeps the name field open with your attempt in it until you change it, close
it or choose another agent. This makes no model request. Renaming keeps the
same person and leaves past spoken lines as they were.

A newborn starts with a temporary name. A chosen child's name must use one
biological parent's surname and an unused first name. The player can name the
child in Profile; automatic naming waits until the child is old enough for
ordinary personal-model calls. Later parent renames do not change a child's
existing name.

A child's personal model is recorded at birth from the parents' explicit
personal assignments. When those differ, the parent who began the family plan
is the disclosed tie-break. Infants make no personal-model calls. After
infancy, the child uses the saved model only when its original key is available
and normal call checks allow it; if the key is missing, built-in choices keep
the child safe and the game does not substitute another paid model. The child
card shows the selected model or that it needs setup. Children whose parents
have no explicit model remain unconfigured; world defaults are not inherited.
The owner can later choose a different personal model or explicitly clear it;
that setting is saved, and a cleared child continues with built-in choices.
This setup supports long valid world seeds and recovers saved newborns whose
provider setup was interrupted.

Personal models choose from legal actions. Each request gives the agent's
name, life stage, personality, aspiration, household, hunger, and warmth and
illness where known. It also gives their latest private thought, a few relevant
memories and some places they know. Jev's routine choices also see fullness,
warmth and illness. Needs are sent as exact numbers. Describing them in plain
words on a stated scale, such as "hungry (starving, hungry, fine, full;
starving is worst, full is best)", is built for personal models and Jev but
switched off: compared with real models, it did worse than numbers with one of
the two models tried ([#672](https://github.com/compoodment/ClankerWorld/issues/672)).
Newly placed adults can choose their own
personality and aspiration in their first personal-model reply. The choice is
saved and shown on their profile. A missing or invalid choice keeps "undecided"
and "find a purpose" without an extra call; routine replies cannot overwrite it.
At the middle of life, becoming a parent or elder, or losing a partner or
parent, an agent gets one separate chance to reconsider its personality and
aspiration using its selected personal model. Each kind of moment is offered
once, for up to five extra model requests in a life. The agent may keep its
current choices. Missing or invalid replies, a pause, disconnection or a reload
while waiting keep the current identity and do not automatically retry the
opportunity. Accepted changes and their reasons appear on the profile and
survive saving. These moments never change names or choose a physical action.
Children's initial identity is still unfinished. The household
and Town are sent by their recorded names. Models are not told about nearby
people, relationships, what the agent carries or what it is doing now
([#255](https://github.com/compoodment/ClankerWorld/issues/255)). Better pacing
or thought quality from these changes has not been proven in live play.

One slow model can wait while other agents and the world continue. A legal
choice is accepted even when the model reports low confidence. A failed or
unusable reply uses the safe fallback and finishes that attempt without another
paid repair request. The agent card shows whether the model is ready, waiting,
canceled, missing a key, out of allowed calls, unable to give a usable reply,
timed out or unavailable. It shows the last accepted model choice separately.
Those facts survive a refresh and save/reload. The paired Windows/model-wait
check remains in [the playtest list](../playtest/453-model-choices-and-checks.md).

**Settings → Game → Model calls**, also reachable from the Main Menu, shows the
installation's model-call count and its optional limit. One count and limit
cover every world and every call attempt, including failed, retried and
abandoned ones. The count never resets by itself, and loading an older save
does not lower it. Provider-reported tokens appear only as information. When
a call brings the count to 80% of the limit, the host adds one Event Log warning.
It belongs to the world active when the host records it; switching worlds while
it waits can change which world receives it. A changed limit sets a new 80%
mark that only later calls can cross. Reaching the limit pauses
the world until the limit is raised or more calls are allowed and the world is
resumed. After that, time keeps running under the new allowance, even if an
earlier limit notice was delayed. Per-world limits and cost estimates are not offered. The Windows
check is in [the playtest list](../playtest/670-model-call-limit-game-settings.md).

The agent card's **Speak to them** box sends a message as a **Suggest** or an
**Order**. A new suggestion or recognized order asks for one fresh decision from
the agent's planning model containing the exact words; a queued order reaches it when that order
becomes active. Any brief reply comes in the same response, without a separate
acknowledgement request. Order steps do not cause requests every tick.
The Profile keeps the four newest under **Your messages** and shows whether the
model heard them, whether an order is still open, and any short reply
separately from private thoughts.

The quick card and the Profile name the agent's current order with its status
as the host reports it: queued, waiting, doing, interrupted or blocked, with
progress, the host's reason when it is held up and how many orders wait after
it. With no open order they show how the latest order ended: finished,
cancelled (saying when a newer order replaced it) or not understood. Finished
appears only after the host records the work as done, never when a message is
accepted or a model hears it. **All orders** in the Profile opens a reader with
every open order, in the order they will be done, and the agent's six latest
closed orders, which closed suggestions no longer push out. The game draws
both from the host's snapshot and keeps no order list of its own, so after a
reconnect or reload they show what the host holds.

Urgently hungry adults may pause an order to buy ready food at a shop or
Restaurant, including continuing an open food purchase. They need their own
payment and enough carrying room, and keep the outstanding order. Equipment
purchases, raw ingredient shopping and selling goods do not gain this exception.

Orders currently cover eating carried food, collecting accessible household
food to eat, going to a known food source or discovering one through ordinary
exploration, and gathering berries, fruit or wild greens from a matching source.
Examples include "eat 3 berries", "gather two berries", "gather berries from
berry-patch", "go to berries at (12, 4)" and "keep gathering food until
cancelled". Quantities count food actually eaten or gathered; travel finishes
on arrival at the food source. Eating waits until the agent is hungry enough,
and a full load blocks gathering with a reason.

Orders also gather wood, stone, plant fiber, clay, iron ore, gold ore and diamond
through the ordinary material-gathering rules. Adults and elders can take one
load, a requested quantity, or repeat until cancelled. Quantities count real
goods; the last whole load may exceed the requested number. A usable tool of
the required tier and enough carrying space are still needed, including room
for seeds from a felled tree. Tools wear and sources deplete normally. An agent
may collect an accessible shared tool before gathering.

Use "gather clay", "gather five wood", "keep gathering stone", or an exact
resource identifier after "from". "Gather iron ore at (12, 4)" checks that tile
without choosing a substitute. Unspecified sites use the agent's known or
nearby observed resources, with ordinary exploration when none is reachable.
A remembered source across disconnected land does not stop an untargeted order
from exploring locally; the agent keeps that memory.
Unknown explicit sites require travel and observation before gathering. Queue,
cancellation, urgent survival interruptions and progress survive save/reload.

Adults and elders can also store their own carried raw materials in their
household's House: "store wood", "store five clay in my House", or "keep storing
stone". Storage preserves personal ownership. A default task puts away one
available lot, limited by House space; an explicit quantity counts goods actually
stored and stops exactly at the requested number. Borrowed, reserved and
promised delivery goods are unavailable to these orders. Walking earns no
progress. Missing goods, a missing House, full storage or a blocked route leaves
the task waiting with a reason. Queueing, cancellation and interrupted progress
survive reload.

Equipment storage accepts the same five clothing/carrying-aid kinds and 13
tool kinds as equipment collection below: "store my basket", "store two iron
knives in my House", or "keep storing padded coats". The agent puts away its
own carried, unworn goods while keeping their ownership and condition. Worn
clothing and carrying aids stay with their wearer. Exact quantities, House
space, travel, queues and cancellation follow the material-storage rules above.
All personal-storage orders accept an optional House tile, such as "in my House
at (12, 4)". They keep the first chosen House and its household owner. If it
moves, disappears or changes owner, or the agent leaves that household, the
task waits without changing destinations.

Orders also support exact-tile movement: "Move to tile (12, 4)", "Go to (12, 4)"
or "Travel to (12, 4)". They use ordinary walking routes and travel delays,
finish only on the requested tile, and wait when it is occupied or unreachable.
Entering another household's House still requires an invitation.
Each order is one trip; repeated or counted tile trips are not understood.
The target survives queueing, urgent survival interruptions and save/reload.
Giving coordinates does not create firsthand map knowledge; the agent learns
the tile only by reaching it.

An adult can also follow "Become guardian for Lina", using the full name of a
child with an open guardian search. The name is resolved once to that child,
so a later rename does not redirect the task. Acceptance uses the search's
current eligibility rules and finishes only after primary care is assigned.
House capacity and same-Town rules still govern relocation. This is one
acceptance, not a repeated task; other non-food task domains remain in
[#587](https://github.com/compoodment/ClankerWorld/issues/587).

Personal collection orders cover the same seven raw materials: "collect my
wood", "collect three clay", or "keep collecting stone". Add "from (12, 4)"
or "at tile 12,4" to collect only from that tile, including storage at a
building's listed coordinates. If the requested goods are unavailable there,
the order waits instead of collecting somewhere else. Adults and elders
walk to their own available goods in current or former household storage,
belongings left to them by a settled will in another household's House, or goods
on the ground, and physically pick them up. Household or other people's goods,
reserved portions and promised deliveries remain unavailable. A default task
collects one load within carrying space; quantities stop exactly at the requested
amount. Travel earns no progress, full hands leave the task waiting, and
queueing, cancellation and partial progress survive reload. Collection grants
no renewed household membership or private-stock access.

Food collection uses the same personal-goods rules: "collect food", "collect
three berries", "collect cultivated greens from (12, 4)", or "keep collecting
fruit". A named kind selects berries, fruit, wild greens, cultivated greens or
one of the seven prepared foods; plain "food" accepts any ready-to-eat food. These tasks collect existing
stored or dropped goods. They do not harvest a food source or eat the pickup.
Raw grain and potatoes use the goods catalogue below. Quantities,
source tiles, carrying limits, queues, interruptions and reload work as above.

Equipment collection covers the five clothing and carrying-aid kinds and the
13 tool kinds described below: "collect my basket", "collect two iron knives"
or "keep collecting padded coats". Add a source tile in the same way as other
collections. The agent picks up its own goods without equipping or repairing
them; their condition stays unchanged. Each unit occupies ordinary carrying
space, and explicit quantities count individual items even when they share a
stored lot. Shared or borrowed equipment remains unavailable to these orders.

Personal-goods collection and storage also cover rope, cloth, refined iron and
gold, workshop tools, grain, flour, potatoes, named planting seeds, medicinal
herbs, bandages, medicine, storage pots, water jugs and gold or diamond ornaments.
Examples are "collect two refined iron", "store three grain seeds in my House
at (12, 4)" and "collect one water jug". Bare seeds, tools and ornaments remain
ambiguous. Pots and jugs move whole, with their contents and ownership intact;
their contents use space but do not add to the number of vessels moved.
Selected equipment and reservations remain protected. Household-owned outputs
are not available to personal collection or storage.

Borrowed-return orders such as "return two borrowed cloth" and "return one
borrowed water jug" put carried household goods back in their owner's House.
This includes a collected milk jug even when a Store or market stall could
stock it instead. The whole jug and its milk return together; reservations and
committed deliveries remain protected.
They preserve ownership and pin the selected House and source lot while work
is pending. A default task returns one available load; an explicit quantity
caps the last move. Only a physical return earns progress. Unavailable goods,
House space or walking routes leave a visible blocker. Queues, interruptions,
cancellation and reload retain the work already done.
An order to put away a tool currently used for ordinary field work interrupts
that work before moving the tool, keeping the saved work state consistent.

Delivery orders use the existing household and Town supply rules. The commands
distinguish their purpose: "haul four grain to my Farmhouse", "supply two iron
to my Blacksmith", "deliver two berries to my House", "donate two wood to my
Town Warehouse", or "stock three cloth in my Store". Named destinations may
include a listed tile with "at (12, 4)". The selected building and owner stay
fixed through travel, partial progress and reload.

Pickup and walking earn no delivery progress. An explicit count measures the
requested goods at the final deposit; a plain request completes one load.
Household supplies retain their real carried delivery assignment between
pickup and deposit. A matching existing shipment can be selected before its
delivery, but its earlier pickup does not count. If space runs out during the
trip, the load waits until it fits. Cancellation leaves actual goods and
completed transfers in place, and never credits a later ordinary delivery to
the cancelled task.

Workstation supplies remain limited by current recipes and ingredient demand.
Farm hauling retains its existing stock-source and storage rules, with the
requested Farmhouse or Silo taking precedence over the ordinary preference.
House food delivery shares only spare carried food. Town donations accept the
normal wood, stone, fiber and tree-seed surplus while keeping the agent's
reserve of four usable units of each kind across their carried lots. Storing
and collecting goods does not create extra reserves. Store stocking keeps its
usable shelf target and food reserves. It keeps one best usable tool in each family;
extra units in the same carried lot can be stocked. Spoiled or broken goods
still take physical storage space but do not satisfy the restocking target.
Its food reserves apply across usable lots of the same owner and kind, so
collecting food in smaller loads does not create extra reserves. Missing demand
or surplus leaves a visible blocker even if the order
asks for more.

A Store-stocking order can pause an ordinary unfinished project, deliver its
goods, then let that project continue with its previous work. A production job
already holding reserved inputs finishes before the agent leaves to stock the
Store. Its goods and payment history remain separate from the delivery count.

Vessels and their contents keep their physical identity and ownership rules.
For a water supply, the count measures water in the delivered jug, not the jug
as an extra item. The whole family must fit and stay unreserved; water cannot
be split to satisfy a smaller count. Grain and flour can use the existing
partial-vessel pickup. No other decanting or arbitrary private-stock access is
introduced by these orders.

Production orders use the existing recipes for goods such as rope, baskets,
cloth, clothing, sacks, tools, flour, pottery and care supplies. Examples are
"make two sacks", "mill flour" and "make two batches of house bandages". They use
ordinary recipe ingredients, household access, work sites, preparation and
production time. Goods keep the recipe's usual ownership and location.
The default is one batch. Explicit item quantities must be an exact number
of whole batches; the game refuses a count that would require making extra
items. Only completed production advances the displayed item or batch count.
Bandages distinguish "house bandages" from "tailor bandages". These names
select exact recipes; an ambiguous plain "bandages" request is not understood.

An optional "at (12, 4)" pins the work site to that tile. The task keeps its
chosen recipe and work site while it waits or repeats; it does not redirect
to another building when access or materials disappear. Queueing, replacement,
cancellation and save/reload retain the remaining count. Cancellation releases
unused reserved ingredients; already finished goods remain. Urgent survival
pauses the current production job and its remaining work before the order
resumes.

Named cooking orders cover all shipped House and Restaurant recipes. Simple
House meals specify potatoes, wild greens or cultivated greens. Other recipes
name the work site: "cook house bread", "cook restaurant berry porridge" or
"prepare restaurant meals". The larger Restaurant uses "restaurant 2x2" in
the recipe name. Every batch makes two servings, so "cook four house bread"
requires two paid batches; odd serving counts are not understood. Plain
"cook porridge" is ambiguous and does not replace the current order.

House and Restaurant ingredient supplies use their native demand, cooking
reserves, carrying and whole-jug rules. Named prepared foods can be collected
as personal property, returned when borrowed, delivered as spare carried food
to the House, or stocked in a Store. These actions preserve the existing
ownership and food reserves. The nonfood personal-storage action still excludes
loose meals; named-food consumption and wild-food gathering keep their separate
commands.

Building orders cover one House, Farmhouse, Blacksmith, Tailor Shop, Silo,
Clinic, Store or Restaurant at its starting size (including the 1×1 Store
and 1×2 Restaurant) through the
ordinary household construction path. "Build a
Clinic at (12, 4)" requires that exact site; without coordinates the agent
chooses a legal site and keeps it. An animal yard requires a completed House;
its order waits without spending materials until that House exists.
Normal one-per-kind limits, Silo prerequisites, material ownership, carrying, travel and construction work
still apply. An existing building or somebody else's project never counts as
the ordered completion.

Construction uses Town Warehouse supplies only when the builder can reach
them. A blocked stocked Warehouse does not stop the builder from gathering
reachable materials elsewhere; its stock stays in place.

A construction order can replace the agent's unpaid ordinary building or
crafting plan. It starts fresh construction work and pays the normal costs;
the earlier plan's work does not count toward the order. A production job
already holding reserved inputs finishes first. Other agents' projects and
work bound to another order or an accepted tool-making request keep their
existing checks.

"Expand my House" and "expand my Town Warehouse" request one next supported
size under the normal access, need, space and cost checks. The task pins the
original building and chosen expansion, then follows its real reserved
materials and work. A changed building or lost access blocks or cancels that
work rather than applying it somewhere else. Both forms accept an explicit
count of one; larger counts and repetition remain unsupported.

Only successful placement or a completed expansion advances progress.
Completed work remains recorded even if the building is later removed.
Cancellation releases unused expansion reservations and ends only the task's
own unfinished work; collected goods stay where they are. Expansion cannot
take borrowed material from another living agent's carried load, including
after that agent leaves the household. Physically returned stock becomes
available again. Urgent survival pauses the work. Queues, cancellation, partial work and completion survive
save/reload without repeating a paid build or adopting an unrelated project.

Repair orders cover personally owned, carried basic clothing, padded coats,
rain cloaks, baskets and sacks that are worn enough for the ordinary repair
rules. For example, "repair my basket", "repair two padded coats", or "keep
repairing basic garments". Adults and elders collect available materials and
work at their household's House for baskets or Tailor Shop for the other items.
Eight steps finish one repair and consume its reserved materials. Walking,
collecting supplies and starting a job earn no repair progress. Cancel or
replace an order to release unused materials immediately. Urgent survival also
releases them; the order resumes with its remaining repairs when the need passes.
Saving during a repair preserves its work and reservations. A repeating task
waits for another matching worn item after the current ones are repaired. Ordinary
repair also considers the next worn item when the preferred one lacks materials,
carrying space or a reachable private work site. When both can proceed, the
equipped carry aid still comes first.

Tool repair orders name a supported material and tool, such as "repair my
wooden axe", "repair two stone pickaxes", or "keep repairing iron knives".
Adults and elders prepare real supplies, walk to their household's Blacksmith
and repair their own carried tools. Only finished repairs count; pickup,
gathering and travel earn no progress. Worn wooden, stone and iron axes and
pickaxes, wooden and iron hoes and sickles, wooden and stone hammers, and iron
knives are supported. Fully broken tools need replacement in both ordinary
repair and owner orders. Borrowed, reserved,
stored and promised tools are unavailable to these personal orders. The agent
may put spare cargo in household storage to make room for supplies, preserving
all matching worn personal tools and any tools needed to gather materials.
Excess repair materials can also be stowed, while the amount needed for the
repair stays carried. Reserved and promised cargo remain protected.
Preparation waits for supplies or another gathering tool if gathering would
break a requested tool. Cancellation
keeps already collected goods and spent gathering wear. Quantities, queues,
survival interruptions and preparation survive save/reload.

Field orders cover tilling, planting, tending and harvesting for adults and
elders whose household has a Farmhouse. Use "till two fields", "plant grain",
"plant two fields of potatoes", "tend cultivated greens", or "keep harvesting
fields of grain". With no quantity, one field is the task. Quantities must name
fields: "harvest five potatoes" is not understood rather than being treated as
five fields. Grain, potatoes and cultivated greens are supported. An omitted
crop for tending or harvesting allows any suitable household crop; a named crop
must match. Add "at (12, 4)" or "at tile 12,4" to work only at that location.
An unavailable or unsuitable tile leaves the order waiting, even if another
field could take the work. Quantities and repetition keep the exact location
when there is no further matching work to do there.

Agents use real walking routes and planting stock. Planting orders skip a seed
lot with an occupied pickup route when another usable seed is accessible.
Tilling and tending require
a usable carried hoe; a carried sickle speeds harvesting under the ordinary
rules. Orders can request work even when the household already has enough food.
Only completed field work counts, with normal tool wear and seed consumption.
Harvests stay on the field as household property, with planting stock reserved
for another cycle. Missing tools, stock, access, suitable fields or routes leave
the task blocked with a reason. Cancellation or replacement releases unused
planting stock and removes unfinished tilling; spent tool wear remains. Urgent
survival interrupts work before its remaining task resumes. Queues, partial
work, stock reservations and progress survive save/reload.

Shelter orders accept "seek shelter", "take cover" and "shelter in my House",
with optional exact coordinates. They finish on actual arrival in permitted
cover, including valid natural cover during a storm. Reaching shelter does
not claim that warmth has recovered. Household access and storm-guest
invitations remain authoritative. An agent keeps its selected building or
natural cover; lost permission or a changed target leaves a visible blocker.
Own and invited homes are known destinations. Other shelters must be locally
observable; a supplied distant coordinate is visited and checked on arrival.
Water and other impassable sites leave the order blocked while the world
continues running and saving.

Adults can follow "light a fire", "tend the fire" or "light the fire in my
House", with optional coordinates. These use normal fuel collection,
ownership, carrying and access rules. Only a real ignition after spending
one eligible owned wood counts. Borrowed, reserved and delivery-promised wood
cannot be spent. An already-lit hearth waits until another ignition is
possible; it is not refuelled or credited as the order's work. Children may
seek shelter but cannot tend fires. Missing usable known wood leaves the
fire task blocked until a supply becomes available.

Both actions perform one task. Counts, repetition, durations and "warm up"
remain unsupported. Urgent food can interrupt either action, while urgent
cold still allows protective work. Queues, cancellation, travel and completion
survive save/reload. Finished progress remains recorded after weather changes,
fire expiry or removal of the building.

Weapon-repair orders remain outside the current catalogue.

A recognized new order replaces the active and queued orders unless **Queue**
is selected. **Cancel task** stops a waiting or active order. An instruction
the game cannot understand preserves the current task and its queue. Explicit
source names must match exactly; the game does not quietly choose another
source or food. Unsupported, mixed, negated or incomplete requests close as not
understood and appear in the Event Log. The parser also refuses counted travel
such as "go get 2 berries" instead of guessing a task. A recognized task that
cannot be done yet stays pending, with model retries on the usual schedule.
Urgent survival can interrupt it before it resumes; waiting for a model reply
does not stop the agent from following its task or handling an urgent need.
Deterministic local choices do not claim the model heard a message, and
suggestions do not block recognized orders. Hands-on Windows paired-client
checks remain pending in the
[playtest list](../playtest/586-observer-guidance.md), the
[food-order checklist](../playtest/587-food-orders.md) and the
[order list checklist](../playtest/589-order-cards.md).

Thoughts, memories, beliefs and explored map facts belong to the individual
agent. The player can inspect mistakes and where a belief came from. Jev can
rank existing memories during a normal call. Agents can also start a nearby
public conversation: each speaker uses their own assigned planning model,
accepted speech is saved with the people who could hear it, and only those
listeners receive a hearsay memory, including descendants with long generated
identities. A conversation can have up to six public
turns and one wrap-up; both people must accept the same wrap-up before its
structured effect applies. These conversation and daily-call limits are still
provisional and need a Windows playtest. Private thoughts are never shared as
conversation history. Other automatic experience capture and generated memory
summaries remain unfinished.

Partnered adults can propose marriage through that conversation's wrap-up.
Each partner separately accepts or declines with their selected personal model.
Mutual marriage consent starts a dedicated surname conversation, which can
continue remotely and has at most four alternating valid turns. Only the
partners' original surnames are allowed; agreement ends it early, otherwise a
disclosed seeded draw resolves it after four turns. Failed calls, pauses and
cancellations consume no surname turn. Consent, surname progress and the final
result survive saves and ordinary conversation-history compaction. Profiles
show the spouse and shared surname, and later player surname changes update
both spouses together while keeping the original conversation unchanged. A
taken first name leaves both names unchanged. Divorce, remarriage and widowhood
rules are not implemented here.

## Animals

**Basic version, connected to normal play; Windows playtesting is pending.**
New generated worlds contain small untamed groups of chickens, sheep, cows and
horses in suitable habitat. Households receive no free animals. Adults with a
House can build a 2×2 animal yard for eight wood and two rope, then expand it
for the same materials. Four/eight places and the household limit of eight
include young animals and reserved births. Yards hold sixteen supply units.

Untamed animals feed from reachable wild greens. If people or other animals
block a patch's approach, they can use another reachable patch nearby.

Taming uses two feed and one jug water. Daily care uses one feed/water for a
chicken and two each for sheep, cows and horses. Feed is grain or greens. Adults
fetch and carry real supplies, preserving food, planting and production
reserves. Supplying a yard carries only the chosen feed load; any remaining
grain keeps its owner and location. Water jugs travel with their contents.
Yard stock is picked up before approaching an animal on another
tile; care at the stock tile can use it directly. Care uses grain before
ready-to-eat greens when available, and may combine grain with spare greens
across lots. An incomplete safe feed or water load spends nothing. Care orders
explain missing safe feed and jug water, while valid supply fetching stays
active. They resume when physical supplies become available. Cared adult hens
supply eggs daily, cows two milk daily,
and sheep two wool every three days. One batch waits for local collection. Milk needs a
household jug that contains milk or is empty; water and milk never mix.
Adults haul milk jugs to their Store or borrowed Market stall. An agreed sale
pours one portion into the buyer's jug and takes the named personal payment;
both vessels retain their owners. Only one pending sale may use each milk lot;
closing the offer frees any remaining usable milk for another sale. Spoiled
milk can be poured away for jug reuse.

Replacing or cancelling an animal task releases its unfinished supply trip.
The adult keeps already collected goods under their existing ownership and
can fetch supplies for a new animal task without finishing the cancelled one.

Egg and milk meals use House/Restaurant recipes. Tailors turn wool into padded
coats, hides into leather, and leather into sacks or saddles. A leather sack
provides 32 carried cargo units. Cared adult pairs breed automatically with
space and delivered supplies; missed care pauses work and riding. Old age is
the only animal death rule, leaving one owned hide from sheep, cows or horses.

A saddled, cared adult horse carries one adult at twice walking speed with eight
extra cargo units. Named outsider riding and care permissions are separate.
Riders dismount before collecting another animal's product or saddling another
horse; receiving space
is checked again on foot, and excess cargo stays at the dismount position.
Gifts and sales need both adults' fresh personal choices, exact payment and
receiving space. Animals keep their owners after household abandonment.
Map inspection shows species, age, care, products, household and permissions.
Animals use the approved art in all eight directions at both close and mid
zoom: chicks, lambs, calves and foals have their own drawings, a horse shows
its saddle or a bare back, and a household sheep looks shorn for the first half
of each wool cycle. The animal yard is a split-rail pen on trampled earth with
an open gate, a water trough and hay.
See [animal controls](playing.md#animals) and the [approved decision](https://github.com/compoodment/ClankerWorld/pull/1027).
All balance values are provisional. Hunting, slaughter, meat, predators,
neglect deaths, horse-drawn carts and animal boat transport remain excluded.

## Life, work and society

| Feature | Status | Current limits |
| --- | --- | --- |
| Food, warmth, illness, clothing and shelter | Available in the game | Basic diet/recovery. Agents treat 40% fullness and 60% warmth as comfortable, and survival becomes urgent below 20% fullness, or below 35% warmth while exposure continues. These are provisional values ([#140](https://github.com/compoodment/ClankerWorld/issues/140)). Nights are colder outdoors by a provisional amount; clothing, shelter and a lit fire help at night as they do by day ([#673](https://github.com/compoodment/ClankerWorld/issues/673)). No energy meter or sleep. Medicine supports gradual illness recovery, described below. |
| Gathering and carrying | Basic version | Agents gather named berries, wild greens and orchard fruit, keep a hearth burning, and collect shared tools. Field harvests remain on their tiles until carried. Agents carry raw crops and seeds to private Farmhouse/Silo storage, trying another permitted farm store when the preferred one cannot fit the load or be reached. Filled pots and their contents stay together unless the existing grain or flour withdrawal rules allow a partial load. They carry ready-to-eat greens and fruit to the House, flour back to the House, and wood and iron ore into the Blacksmith. An adult with a building or crafting project completes a valid household delivery before gathering more project materials. If the destination has no room for the load, the project shows the storage blocker and keeps its cargo and plan; it can resume when room is available. A well-fed child can help by giving one spare serving to the household store when the House has room. Space promised to other deliveries also counts; if the House fills before arrival, the child keeps the serving. |
| Farming and crafting | Basic version | Adults from a household holding a Farmhouse use wooden or iron hoes to prepare fertile land, carry grain seed, potatoes or cultivated-green seed, tend the crop and harvest it. Illness slows field work and tool wear. Planting supplies stay held until the work finishes or is interrupted. A farmer with a usable wooden or iron hoe keeps the chosen crop when someone briefly occupies the field. Wooden and iron sickles make harvesting faster. Fields show each stage on the map and overview; inspection names the soil's fertility and the household. Harvests stay on the field, with planting stock reserved for another crop, until physically carried into finite private farm storage. Orchard seeds grow saplings that mature and fruit in autumn. A household makes crude wooden axes and pickaxes at its own House from on-site wood, including fallen wood gathered by hand. They unlock tree felling and stone without a Town or Blacksmith, gather less and wear out sooner than smith-made wooden tools, and keep distinct inventory and Event Log names, shown with the wooden axe and pickaxe icons. The household holding a Blacksmith makes and repairs better tools from real materials: axes fell trees, pickaxes unlock finite stone, iron and rare deposits, and hammers speed building work. Iron knives speed food preparation and suitable crafting. Tools wear during successful work; loose fallen wood can be gathered by hand if an axe breaks. Adults carry needed grain back from their Silo to the Farmhouse, where it mills into flour. Houses cook potatoes or greens into simple meals, grain into porridge, flour into bread, and potatoes with cultivated greens into stew. Porridge, bread and stew need carried fresh water and wood. Berries or fruit improve porridge; Restaurants also turn bread and greens into better meals. Cooking uses private on-site ingredients and stores its output in that House or Restaurant. A Tailor Shop makes cloth and clothing. If a household workshop recipe cannot get its ingredients, it pauses so an adult can choose another task; its saved plan can resume when supplies return to the building. Rates remain provisional. |
| Handcarts | Basic version | Adults in the Blacksmith household can craft a personal cart from carried wood, iron fittings and rope. Its ground output does not hold a Blacksmith storage space while work is running or paused. The visible cart carries up to 32 loose goods separately from the agent's load. Its owner reaches and attaches it, loads physically nearby authorized goods, pulls legal cardinal routes, parks, unloads, repairs or gives the cart and its cargo to a nearby adult. Loading shared food follows the existing food reserve, including the permitted quantity; personal food and non-food cargo remain loadable. These cart controls are available to personal models; built-in choices leave them alone without a delivery task. An attached cart follows ordinary movement. Roads reduce movement waits and wear; broken carts keep their cargo. Map, tile and agent inspection show ownership, position, load and condition. |
| Clay, pottery and water | Basic version | A household can dig finite clay and make storage pots and water jugs at its House. A pot holds up to 8 food and slows spoilage; hungry household children, adolescents and adults, and caregivers feeding an infant, take ready-to-eat servings from it. A jug holds up to 4 fresh water, which an adult can collect from a reachable riverbank or lakeshore and return to the House. Collecting a jug requires carrying room for the jug, its contents and some water. The Clinic uses delivered fresh water to make medicine, leaving the jug reusable. The related [empty-vessel return fix](https://github.com/compoodment/ClankerWorld/issues/749) carries empty household pots and jugs from workstations back to the House; automated checks cover its return and reuse. Porridge, bread and stew also consume fresh water while leaving the jug reusable. Empty jugs at Houses or Restaurants can be collected and refilled. Animal care also spends actual feed and jug water; see [Animals](#animals). |
| Building new buildings | Basic version | A household plans a House, Farmhouse, Blacksmith, Silo, Tailor Shop, Clinic, Restaurant or optional Store it does not hold yet, one of each, once it has the materials in hand. It gathers missing materials first. Only a household holding a Farmhouse builds a Silo, within two tiles of it; farm stock reaches either building in carried loads. Adult residents can supply and build a Town Hall, Market, street lantern, Port or communal boat after the Council approves its exact site and budget, as described below. |
| House resident places, expansion and guests | Basic version | A House gives three resident places per tile, or four when one recorded domestic family unit is at least two people and a strict majority. Travelers and infants count; dead people and invited storm guests do not. A full House can be expanded for more places when the work completes. Add Agent and unanimous household admission check the House's room before adding someone to a household that holds one. Birth still completes into the primary caregiver's current household and can make it overcrowded; the building card and agent context show the count and limit. Existing overcrowding gives eligible adults one unpaused world day to move, with volunteers first and sole caregivers protected; notices, requests and expansion progress appear in agent inspection. Adult household members may also expand a nearly full House for storage; adult Town residents may expand a nearly full Warehouse from 2×2 to 2×3. Expansion keeps identity, stock and cooking jobs, reserves materials and cancels safely if space or permission changes. Any adult household member may invite or revoke a named storm guest. Guests cannot use House stock or cooking. Storage limits, costs and work time are trial values. |
| Local exploration and physical knowledge goods | Basic version | Short outings record personal knowledge. Actual revisits refresh changed terrain and resources, and planting teaches the planter; distant agents keep their own accounts. Existing written items and unfinished writing retain their original contents. A House makes paper from real fiber and jug-carried water; adults use paper to write field records and maps, or paper and cloth to bind books. Reading, sharing and trading teach only their actual contents to the recipient. Adults can exchange records, maps or books of the same kind when both contain discoveries the recipient has not learned. Copies cost fresh materials. Purposeful distant exploration is unfinished. |
| Trade, relationships, conversations and teaching | Basic version | One-for-one barter, where both people must meet and have room for what they receive. Spare carried goods can come from separate stacks while keeping one usable unit of the same kind; individual artifacts and unworn ornaments keep their existing exception. Positive trust, accepted/refused partnerships, and bounded public conversations with mutual consent for a structured trust effect. Close biological relatives cannot become partners or plan a child together: parents and children, grandparents and grandchildren, full or half siblings, and aunts or uncles with their nieces or nephews. First cousins can; shared households and caregiving do not count as kinship. Each agent has at most two conversation starts or acceptances per world day; this and the six-turn limit are provisional. Adults can ask a free, healthy agent with a saved skill for a practical lesson when both can reach its teaching site. An isolated teacher does not hide another available teacher. The learner keeps the skill, teacher and time, and the agent card shows them. Skills currently change no access or work speed. Pricing, currency, conflict and broader group dialogue remain unfinished. |
| Household shops | Basic version | Adults can offer exact goods for goods kept at a nearby Farmhouse, Blacksmith, Tailor Shop, Clinic, Restaurant or Store. Both traders meet there before anything changes hands. The buyer carries the purchase; payment goes into household stock at that shop. Buyers can seek better tool tiers, clothing that protects them better in the current weather or medicine for an observed illness. Store goods must be carried in first. The building card shows the terms and progress; cancelled offers release both lots. Buying grants no access to private stock, cooking, treatment or household membership. A customer may ask a Blacksmith household for a tool before it is stocked. The household may accept or refuse; accepted work uses its own real materials, and a finished tool is purchased through the usual physical barter. No payment, price promise or future ownership is created by the request. Request status and any missing-input or storage blocker appear on the Blacksmith and relevant agent cards; [its Windows checklist](../playtest/564-tool-making.md) is pending. Market stall trading is described below. Restaurant adults buy missing ingredients and customers buy meals after walking to a shop in their own Town. Its [Windows checklist](../playtest/561-concrete-meals.md) is pending. Barter rates and shelf sizes are provisional. |
| Clinic supplies and illness care | Basic version | Reachable renewable herb patches supply a household-held 1×1 or 1×2 Clinic. It makes medicine from herbs, wood and water in a reusable jug; a House or Tailor Shop cuts cloth into bandages. One real medicine dose reduces illness gradually. Adults choose up to 16 named caregivers through a fresh accepted personal-model decision and may revoke permission. The last free permission still offers nearby alternatives. Self-care and a dependent's accepted caregiver use their existing authority. Jev, failed replies, repeated intentions and owner orders cannot grant adult permission. Interrupted treatment stops without refunding the spent dose. A caregiver who starts fetching medicine remembers the patient and where they last saw them, brings back one dose and treats only a consenting patient they can find nearby. Saving during either leg keeps the trip; withdrawal of permission stops it. Saving keeps permission and progress; pausing stops recovery time. Injury causes and bandage treatment remain deferred. Automated checks cover this path; [the Windows playtest](../playtest/565-clinic-care.md) remains pending. |
| Parenthood, guardians, life stages and death | Basic version | Ordinary consent/preparation binds an explicit primary caregiver and intended home; the accepting parent chooses a named caregiver-and-home option. Birth joins the caregiver's current household even if it has changed, is full, or has lost its House; a missing home is recorded as a housing need, and the other parent stays put. If the current primary caregiver dies or ends care, living relatives, household adults and then Town residents are asked in order, and each wider group keeps the earlier ones. A willing adult must accept; until then the child's card and Event Log say “Needs a guardian,” nearby adults may still feed them, and the player can suggest an adult who is being asked in a message. An adult can also be ordered to accept a named child through the same active search; Queue, Cancel task and normal eligibility checks still apply. A completed House with room in the same known Town permits household placement at acceptance. Otherwise care stays accepted while a pending move waits for a suitable home: the guardian collects the child and accompanies them to their House, including in another Town. Household and Town membership change together on arrival, after care authority and room are checked again; birth records stay unchanged. The agent card shows waiting, travel and blockers, and pending moves survive save/load. Infant care, child talk/play/help and age restrictions are enforced. Children can choose another reachable person when nearer contacts are on cooldown for all their social actions; up to three available contacts are offered. Children can recall newly recorded talk, play and learning experiences after save/load, including those with generated identities. Only adults can have children; elders cannot, and a plan ends if either partner becomes an elder before the birth. While fewer than eight non-elders live, the continuity rule lets a partnered couple with no infant put off a child for up to two days but not refuse. Parents' selected child model is recorded at birth; children without an explicit model use safe local choices. The owner can later choose another model or leave the child unconfigured; world defaults are not inherited. |
| Towns and household property | Basic version | First-Town membership/borders, building ownership, household stores, household food steward, and an adult with no household asking to join a household that holds a House, with every adult member's agreement. The accepted first-Town layout records Town title over its connected land and starter household use rights on owned building footprints; later border growth alone does not add title. Councils can approve explicit claims to adjoining unclaimed land. Add Agent uses recorded rights; one pending request does not assign a household, and conflicting claims make placement ambiguous. Owners can reassign or remove buildings when stored goods, deliveries and active work allow; Town borders and title stay unchanged, while a household building takes its existing footprint use right to the new household. Empty Towns appear as abandoned and keep their identity, property, borders and laws. Agents may salvage real unreserved communal stock on the ground or at the Warehouse; one adult physically in the Town may deliberately resettle it with their Town care group. Revival restores ordinary communal access and admission. Adults may leave without a vote, retrieve their personal goods and form a household alone after seeking an accepting existing home. An adult with no Town can join one through its council (below). Recorded multiple Towns can be saved and validated. Existing use permissions can transfer with every current adult's acceptance in all giving and receiving households; public land hearings decide disputes and expiry with sourced rulings and grounded rehearings. Non-land hearings record sourced civil findings and voluntary remedies; founding another Town and currencies remain unfinished. Nonconflicting household land grants require Council approval and every current adult household member's explicit acceptance. House expansion needs recorded use rights on its extra tiles. |

Restaurant trading uses the normal private-world path; its
[Windows playtest checklist](../playtest/561-concrete-meals.md) is pending.
Restaurant adults visit ingredient shops in their Town, and adult residents
visit its Restaurants for meals. Exact goods and terms are checked after
arrival; a visit reveals no private stock remotely and transfers nothing.
An adult from the household holding a Restaurant buys ingredients it actually
lacks, such as flour milled at another household's Farmhouse. Usable supplies
already there or actually deliverable reduce the shortage; private House
reserves, goods in an unsuitable shop and vessels too heavy to carry do not
become free Restaurant supplies. The adult uses only their own carried payment
and preserves needed Restaurant ingredients. Purchased inputs must reach the
Restaurant before cooking. Customers can buy bread, stew and porridge,
including the berry and fruit versions, and carry their own purchase away.
Two cooked servings for one wood is a provisional offer; other payment terms
depend on the recipe. Trading requires both sides to meet, sufficient carrying
and shop space, and unclaimed goods. It grants no private cooking access.

With a usable iron pickaxe and room for a whole load, an adult can mine a finite
gold or diamond outcrop. The goods remain in their carried stock. Trial mining
pauses once the adult and household hold eight of that material. The Blacksmith
refines carried gold ore and crafts gold ornaments, optionally set with a
diamond. Agents may wear an ornament, remove it, give it to a named nearby
adult who has carrying space or sell it through physical barter. Full-handed
neighbors do not hide another nearby adult who can receive the gift. Ornaments
give no warmth, carrying
or combat bonus and add no art. These paths have automated checks;
[the Windows playtest](../playtest/567-ornaments.md) remains pending.

Life-stage thresholds are child at day 3, adult at day 15, elder at day 45 and
death by day 60 from birth. Nobody dies of old age before day 45; from then
the daily chance starts at 1% and rises a quarter of a point a day. Each
founder and each adult added later arrives at an age from day 15 to day 25,
chosen from the world seed. The four founders always get different ages, and
the same seed gives the same founder ages in placement order. The agent card
shows the age, such as **Adult · 19 days**. These are playtest values, not
settled population balance.

A world cannot die out only because every couple keeps declining children.
While fewer than eight agents who are not elders are alive (infants, children
and adults all count), the **continuity rule** is on. A new world starts with
it on, and the Event Log says when it turns on or off. Once eight non-elders
are alive it turns off and ordinary refusal returns.

While the rule is on in a started world, the Event Log keeps a warning that
the world is at risk and offers **Add a newcomer**. This opens the usual
Add Agent controls, where you choose the key, model and placement. Opening the
offer adds nobody and makes no paid model call. The offer disappears once the
rule turns off; nobody arrives on their own.

While the rule is on, a partnered adult couple with no infant may say "not yet"
to a child, but not refuse: two world days after the rule first applies to them,
their plan goes ahead as if both had agreed. Preparation still takes time, and
the birth still needs food; a plan the rule sent ahead waits for food instead
of expiring. A missing House creates a housing need without stopping the
birth. Such a couple may also plan another child once their
youngest has left infancy, without waiting for that child to grow up. Each
partner's own model request explains the rule and how long is left. The rule
never creates a partnership, and choosing a partner stays voluntary. The
threshold and two days are provisional; a check based on the real risk of the
world dying out comes later. This has automated checks but no Windows playtest
yet.

Both parents' cards show a preparing plan's food shortage: the
caregiver household's available ready-to-eat portions and how many more it
needs: two for each living household member, plus four for the birth. Grain
and flour need cooking. Reserved, spoiled, privately owned food, food carried
by someone other than the caregiver, and food in a broken pot do not count.
A parent in another household gets the resource blocker in their model
guidance without the caregiver household's private quantities. Ordinary food
acquisition and cooking priorities still apply.

When an agent with a personal model dies owning something, their model is
asked once for a final will. It can leave everything to the household or name
up to three heirs: living people of any age, including children, or the Town
the agent lived in when that Town has a Warehouse. The will either shares every
item equally or gives each item to one heir, and anything it leaves out is
shared equally. The estate is divided when its seven-day hold ends. Heirs,
children included, own what they inherit as their own property. Inheritance
does not put goods into the heir's carried load: ground goods stay on their
tile, stored goods retain their storage, and goods held by a living carrier
stay with that carrier. Goods the deceased carried are dropped at their last
tile. A pot, jug or handcart always goes to one heir together with what it
holds. A Town keeps its share in its Warehouse while there is room. All food,
including eggs, milk and their meals, follows the household path. So do
handcarts, goods that do not fit, and shares for heirs who have died since, plus
interrupted, unknown or invalid choices, so no goods are created or lost.

The will may also leave short final words. When the estate is divided, each
person who inherits keeps them as a private memory, such as "Rowan Hale's final
words were: 'Keep the orchard going.'" Nobody else learns them. The dead
agent's profile shows the will, what it leaves each heir and the final words.
Debts, conflicts with Town law and other guardianship cases remain unfinished.
Memories do not automatically pass to children.

A Town becomes abandoned only when no recorded living residents remain. Children,
travelers and adults without a home still count. An adult physically inside an
abandoned Town can choose resettlement with their personal model, leaving their
previous Town if needed. Dependent children in their primary care who shared
that Town follow membership; bodies, household membership, care, private property
and laws stay intact. Its council restarts from the returning living adults.
Later visitors need that government's admission. Salvage alone grants no
membership, and public pickup stops on revival while already collected goods
stay with their carrier. The Towns panel, map labels and Event Log show the
change; [its Windows playtest](../playtest/410-abandoned-towns.md) is pending.
An abandoned Town's buildings look neglected on the map: faded and mossy,
with weeds and boarded doors. Once it has stood empty for a full season they
look falling apart, with holes in the roofs, fallen planks and saplings; a
revived Town looks lived in again. Its street lanterns and its Port's pier
lantern stay dark at night; the street lanterns also fade, gathering moss and
weeds, and its bridges fade and lose boards, then whole planks once the Town
falls apart.
Founding another Town remains unfinished.

Each founded Town has its own council. Its recorded living adult residents
include travelers and adults without a home; visitors gain no vote. All adults
govern initially. At eight adults the Town opens elections for three willing
representatives, with ten-day terms, vacancy contests and one cutoff runoff
followed by a saved draw when needed. A Town with an elected council keeps
representation at seven adults and returns to all adults at three or fewer.

Agents personally register willingness, visit the public notice place near the
Town's founding site, read actual notices and relay learned information nearby.
They submit ordinary social-law or admission proposals and cast votes in their
usual personal-model turns. No extra paid calls poll for civic votes. Ordinary
proposals need a strict council majority, with two yes votes required while
representative seats are vacant. Proposal votes are final; election ballots
may change before the one-day window closes. Council changes cancel unfinished
proposals. The scrolling Towns page shows current councillors, candidates,
election totals, the latest completed, failed or cancelled election, and eight
recent pending, passed, rejected or cancelled proposals.

Adult residents can propose a connected plot of adjoining unclaimed land in
their personal-model turns. The Towns page shows its exact coordinates and
votes. A Council majority records the new Town title and extends the border
to cover it; the Town title filter then shows that plot. The server rechecks
the plot at approval, so competing claims cannot overwrite one another.
Household use rights, buildings and goods keep their holders.

Adults can request household use of a connected plot of Town-titled land. An
ordinary Council majority and explicit acceptance from every current adult in
the beneficiary household are both required. Filing and Council votes do not
count as household acceptance. A new adult joining before the grant must also
accept. A refusal, withdrawal or elapsed requested end date closes the request;
conflicting claims remain pending without changing anyone's rights. Plot
details show Council votes and household acceptance counts.

Existing use permissions can transfer between households for an exact connected
plot. Every current adult in every giving and receiving household must actually
learn the published terms and personally accept. A proposal, a read or silence
supplies no consent, and a new adult joining before completion must accept too.
This ordinary route needs no mayor or Council approval and preserves the original
grant date and any agreed end date. It changes no Town title, household membership,
building, crop, goods or private-building access. A decline or the proposer's
withdrawal closes it without moving permission. Expiry, disputed claims, an open
hearing or changed source permission stop it. Town, plot and property details
show exact terms, named adults and acceptance counts; Towns retains all pending
transfers and eight recent closed ones, with full history in the save.

A House that needs more room can request the exact extra tiles for an expansion.
Construction waits for recorded permission, and rechecks it before completion.
Town-owned expansions need Town title on their extra tiles. These checks do not
transfer buildings or goods, and an expired existing right remains recorded
until a lawful ruling changes it. An agreed expiry opens a review and leaves
the permission provisional while that review is pending.

Councils can adopt, amend and repeal laws with a named subject and scope:
formally claimed land (including visitors), a recorded site within that land,
or an explicit duty of residents wherever they travel. Earlier wordings remain
saved, and laws apply only from adoption. Ordinary law text grants no physical
authority. The supported boat-access vote can permit one visitor or all visitors
to use a Town’s communal boats; it grants no goods, ownership or membership.

Any adult resident can initiate a protected government-change vote at the
Town's notice place. It needs
more than half the remaining opening electorate to approve within one day;
cast votes are final. Equivalent proposals share the window and different
proposals queue. Approval gives at most three days to seat a valid successor
for newly added offices or an explicit replacement, while lawful incumbents
continue. An unrelated office may remain vacant during the change.
Supported arrangements include all adults,
elected representatives and one elected governing leader. Ordinary laws cannot
remove the protected resident vote or invent new powers.
An unrelated government handover does not delay or cancel the Council's own
election when an adult leaves during voting, or stop its retry after failure.

Residents can create an elected land mayor without a population gate. Each
mandate lasts twenty days, with renewal voting one day before expiry. Mayor
candidacy needs separate personal agreement. Each resident chooses one willing
candidate; tied leaders face further votes, never a draw. Scheduled Council
elections interrupt mayoral voting and discard its unfinished ballots. Death,
resignation, departure and expiry create vacancies. Land and ordinary governing
mandates stay separate, even when one person holds both. A new mayoral election
waits for a willing eligible candidate, without posting failed elections each
day while nobody agrees to stand. A governing vacancy
restores all-adult decisions until lawful succession; land cases wait.

The Towns page shows the approved arrangement, offices, recent government
votes, current and last mayoral contests, and the latest sixteen laws with
scope, version and effective dates. Complete histories stay in the save.

Affected households and the legitimate Town government can file public land
hearings. Each affected adult gets a formal response period; actual reading or
relaying supplies awareness, and only their own answer or explicit waiver can
close their opportunity early. The file distinguishes allegations, observations
and inspected records, with sources and the deciding mayor's reasons. A conflicted
land mayor stands aside for a willing independent adult elected for that case
only. Without valid authority, the case and existing rights remain pending.

Parties named at notice publication remain separate from current adult responders
and the authorized Town representative. Death, household departure or loss of authority
cannot leave someone falsely displayed as a required current responder; rulings
retain their actual parties at the time of decision.

Rulings can confirm, renew, change or end use permissions while preserving Town
title, households, buildings, crops and goods. A permission past its agreed end
must be renewed, changed or ended, and free Town land is given out only where a
pending request for it was heard. Settled cases reopen only on
material new evidence or a demonstrated procedural error, preserving earlier
rulings and current rights until a correction. A ruling resolves only the
covered part of a competing request; remaining tiles stay pending. Towns shows
active cases, pending rehearing assessments and eight recent settled cases;
plot and property details show matching cases. Complete history stays saved.
Non-land cases have a separate protected mandate. Reports retain the conduct,
applicable law version and actual evidence sources. Response windows,
independent judges and reasoned findings lead to explanations, warnings or
censure, with voluntary offers for named goods, repair or public service.
Every contributor gives their own informed acceptance; physical completion
receipts distinguish accepted work from completed work. Visitors retain local
response rights and children receive caregiver support. Counteroffers during
renegotiation still replace the original unfinished agreement once everyone
accepts; only the newly agreed remaining contributions can then be performed.
Goods returns can use a later carried lot with enough goods when an earlier
one is too small; a named source lot and reservations still limit the payment.
Public-service goods
keep four usable personal units across carried lots, including stock split by
storage and collection. No fine, seizure, expulsion or forced work is available.
Windows civic pacing and
visual checks remain pending; see the unchecked
[#633 playtest](../playtest/633-land-hearings.md) and
[#635 playtest](../playtest/635-nonviolent-town-hearings.md).

An agent belongs to one Town or none, and travel never changes that. Losing a
home keeps Town membership, the council vote and in-person Warehouse
collection. An adult with no Town, such as one added outside the border, asks a
Town's council for admission at its notice place; an adult inside a Town they
don't belong to can walk there to read it. A passed request made by the
newcomer admits them straight away. A resident may also ask the council to
admit an adult nearby who has no Town; once it passes, the newcomer must accept
before anything changes. Approval lasts one unpaused world day from the council's
decision, and the Profile shows its acceptance deadline. An unaccepted approval
expires with a reason; someone may ask again later through the ordinary council
rules. Their dependent children join with them. Each agent has
one request of their own open at a time, and a refused request waits a day
before it is offered again. An admitted resident may collect their Town's
Warehouse stock in person, but approval never gives a House or household place;
an adult waiting on a Town's approval may ask its households to take them in.
The agent's Profile and model context show their Town and its rights.
The Profile shows public admission status and its deadline; the
agent's model only learns admission status from notices they read or were told.
The approval, lapse and admission events name what happened. Moving from one
Town to another, with the old Town left in the same step, works in saves that
hold several Towns, but normal play has only the first Town until founding
another Town is finished.

The first Council-approved Town project is a Town Hall
([#767](https://github.com/compoodment/ClankerWorld/issues/767)). Its
plan binds a name, a 3×4 site, its south entrance and a provisional budget of
24 wood and 12 stone. Approval follows the Town's ordinary proposal vote and
creates no goods. The full footprint needs existing, uncontested Town title,
without a household use right or pending land request. A Town border alone is
not enough; a Council land claim can add title first.

The implemented path lets adult residents who learned the proposal carry
Town-owned materials from the Warehouse or recover released Town loads.
Eligible Warehouse users can share its collection tile, and residents can share
the work tile of their Town's active project. One resident standing there does
not stop the others from collecting, returning, supplying, donating or building.
Gathering more materials gives the gatherer personal goods; donating those
requires a fresh, explicit choice by their personal model at the approved site.
Built-in choices and owner orders cannot donate private goods, and household
stock is not taken. Delivered usable stock is reserved for this exact project.
Only the full paid budget enables construction, with a provisional target of
10 work units; a usable hammer helps and illness can slow work. A blocked site
or missing delivered stock releases unused claims, while the real goods stay
where they are and retain Town ownership. A pending household land request on
the site only pauses the project. A site that can no longer be used, for
example because the request was granted or the site was taken while the vote
was open, cancels the project instead of holding its land. Its leftover loads
stay Town property. Adult residents can collect released ground loads and return
them to a reachable Town Warehouse with room, without waiting for another
project. Recovery leaves carrying space for food and pauses for urgent food or
warmth; an urgent carrier sets down released goods before meeting those needs.
Temporarily blocked paths wait instead of repeatedly dropping and collecting a
load. Other jobs' reservations and private goods are left alone; a later Town
project can also use the released materials.
A pending Hall proposal's site is not offered for another Hall proposal or as
free land for a household request. The Towns page shows the plan,
Council result, supplied materials, work and blocker, even after the vote leaves
the eight recent results. Completion makes a Town-owned Hall with the approved
bell-tower drawing and south-facing Road entrance. It becomes the Town's civic
notice place; it adds no private storage or new government powers.
Automated checks cover gathering, carried deliveries, fresh donations, paid
construction, saves and civic notices. The
[Windows checklist](../playtest/767-town-projects.md) is unchecked. This Hall
supplies the shared construction rules used by Markets, Ports and communal boats.

A Council can approve a Port in any of four shoreline directions. Its two-tile
land end needs Town title and a clear approach; three rows extend into water,
with six clear docking spaces beside the pier. The provisional budget is
16 wood and 4 stone, with 10 work units. A separate approved boat project at a
completed Port spends 8 wood, 2 rope and 2 refined iron, with 16 work units.
Both use actual Town stock or fresh voluntary donations and the shared paid
construction ledger. Approval alone creates no boat or building.

An adult requests a trip at the departure Port’s land approach. A boat carries
one passenger and their carried goods between completed, usable Ports along
connected water, including the world’s east/west seam. Boats remain Town
property. Residents may use their Town’s boats; visitors need a Council-adopted
individual or standing permission. Permission grants no membership or stock
access. Ordinary law text does not authorize travel.

The oldest currently usable request boards first. A blocked request keeps its
place without holding a boat or blocking another usable traveler. Departure
reserves the actual boat and one destination space; incoming boats count toward
six spaces. Waiting travelers may cancel. A blocked arrival waits one unpaused
world day before returning to a usable origin, keeping passenger and goods
aboard. If neither landing is usable, it continues waiting. Ordinary survival
still applies aboard; passengers may eat carried food or drink fresh, available
milk from a usable carried jug. Drinking spends one portion and keeps the jug.

Boat positions, passengers, cargo and active requests appear on the map and
Towns page. Port inspection shows moored boats and incoming reservations.
The approved drawings cover four Port rotations and eight boat headings with
rowing and shipped oars. The pier lantern lights at night. Medium zoom scales
the approved drawing while [#914](https://github.com/compoodment/ClankerWorld/issues/914)
tracks missing 16-pixel art. Automated checks cover paid construction, travel,
save/load, permissions, queues and recovery; the
[Windows checklist](../playtest/411-ports-boats.md) awaits hands-on play.

A Council can also approve a Market: a Town-owned 2×2 hall beside a fixed 7×4
packed-earth plaza. Its first paid project includes two 1×1 stalls, one in each
opposite-facing row. The provisional budget is 24 wood, 8 stone and 4 fiber,
with 10 work units. When every standing stall is borrowed, the Town may approve
one more in the next unused fixed slot, and the next only once that one is
built. Up to six more stalls can fill the plaza; each needs separate Council
approval, 4 wood, 2 fiber and 3 work units. The same Town-title, real supply,
voluntary donation and shared construction rules apply. The built hall and
plaza stay clear of other buildings, fields, trees and household land requests.
General plaza growth remains open.

An adult can borrow an empty stall while they stay at the Market and physically
bring their own or their household's surplus there. Stock keeps its recorded
owner. Buyers may visit from any Town or have no Town membership. They offer
real personal goods, and both people must meet at the stall before its named
seller freshly chooses to accept.
The provisional exchange is one unit for one unit. The buyer carries the
purchase; payment becomes the seller's household stock at the stall, including
when the purchase was the seller's personal property. Buying gives no access
to private stores or household membership. Leaving frees the stall and cancels
unfinished exchanges. Its next borrower cannot sell the previous seller's
leftovers; the recorded owner can return to collect them, including spoiled
stock that still takes up stall space. An urgently hungry
adult can retrieve their personally owned food; unrelated Market work still
waits. Household goods a
member carries to or from the Market can be brought back into the household's
House. Housemates leave household stock on a stall while one of them borrows
it, and haul it home once the borrowing ends. Explicit Market collection follows
the same boundary. The borrower may collect household stock, and adults may
retrieve their own personal goods. An Order or built-in choice
cannot give away stock or consent to a Market exchange; built-in rules prefer
ordinary work over any Market choice.

An adult whose recipe plan has paused for missing ingredients can choose to
buy the missing material. The purchase remains personal cargo; it grants no
access to the seller's stores and does not resume the paused work by itself.
A household holding a Farmhouse may also buy a missing planting unit when it
needs food and an adult with a usable hoe can reach an idle prepared or
harvested field. Usable carried stock of that kind, the household's available
planting stock and the field's reserved replanting stock satisfy that need;
holding the bought seed stops another purchase of the same kind until it is
needed again. Agents still choose to return and plant it through ordinary field
work. Walking into a Market or buying there changes no household or Town
membership.

Hall and stall cards show built stalls, borrowers, goods and owners, and exact
exchange progress. Only paid stalls appear on the map, and the Event Log reports
Market and Town project events. Automated checks cover
paid construction, another paid stall, real barter, ingredient and seed buying,
actual planting, visitors and current-format saves. The nine checks in the
[Windows checklist](../playtest/564-market-stalls.md) are pending.

Street lanterns use the same Council-approved supply and construction path.
An adult can propose a stone street lamp or hanging street lantern at a clear
Town-titled tile beside an existing titled Road, with the exact Road edge
bound into its plan. Trial budgets are 4 stone for a lamp and 4 wood plus
1 refined iron for a hanging lantern, and the shared target is 10 work units.
Both amounts and work are provisional. Completion consumes the delivered
budget once and keeps the chosen edge; it does not lay or extend Roads.
The Towns page and selected fitting show Council approval, materials and work.
Daytime fittings use the approved art; at dusk they light automatically and
go out at dawn, without fuel or visibility effects. In an abandoned Town they
stay dark until it is resettled, and the selected fitting says why. The map never places
them itself. [Windows checks](../playtest/892-street-lanterns.md) remain pending.

Towns grow along their streets. Building sites that can face an existing Road
rank higher. Each new building's street runs on a few tiles past it, and a
building away from the Roads gets a new side street. New Road tiles and bridge
entrances avoid household use rights and pending requests; existing Roads and
bridges remain usable. Town borders and
building-site ranking are provisional. The first accepted layout records Town
title over its then-connected land; later border expansion does not add title.
Starter household use rights cover the footprints of the buildings assigned to
each household. New fields cannot occupy land another household holds or has
requested. Ordinary household land requests and grants cannot take over another
household's field; challenges to recorded use rights remain disputes.
Add Agent closes when the host acknowledges placement. A later model-settings
refresh failure keeps the placement confirmation and asks the player to reopen
Game Settings; another map click does not add another agent.
Add Agent uses building/field ownership and recorded use
rights, then Town title or border: one clear household claim sets the household,
and land claimed by one Town gives Town membership. A building's current owner
comes before another household's use right on its footprint. Reassigning a
household building also moves its existing footprint use right to the new
household; surrounding rights and Town title stay unchanged. Disputed, expired
or differently owned rights block the reassignment, and a right cannot be left
without a household holder. A lone pending use request does
not create household membership. Disputed land and other conflicting claims are
refused for a new placement, but do not move existing residents or stop
ordinary movement and work. The server checks the preview against current
records again when the adult is placed. Walking does not change membership. An
adult with no household first asks a household that holds a House with a free
resident place in their Town to take them in. Every adult member of that household must agree within
the same short window as other proposals; one refusal or no answer ends the
request, and that household is not asked again for two world days. Standing
beside a House grants nothing, and a pending request grants no access to the
household's food, stock or shelter. Once every adult agrees, the newcomer is a
member of that household. The agent's profile and its own model request say
the real blocker: no household, a House still to plan, missing materials, no
legal site or an overcrowded House. Adults may leave without a vote and keep personal ownership of goods stored
at the old House. Collection and returning borrowed work tools require travel
and carrying space. A departure allocates up to two available, unreserved
ready-to-eat portions once; collection and reload do not repeat that allowance.
Dependent children keep their primary caregiver and move as a care group unless
another adult explicitly accepts primary care. After growing up, they can collect
their own goods left at the former House, even if the caregiver has died; they
need not rejoin the household. Existing homes must accept the
whole group and have enough completed places. If no suitable home accepts them,
one adult can start a household and pursue a House through the usual materials,
legal-site and work rules. No House or materials are supplied for free. Leaving
preserves Town membership and an empty household's property. See the
[departure rules](game-design/towns.md#household-goods-and-departure).
The agreed [House resident limits and expansion](game-design/towns.md#house-resident-capacity-and-relocation)
are implemented by [#598](https://github.com/compoodment/ClankerWorld/issues/598).

An overcrowded House gives eligible adults one unpaused world day to move out.
Volunteers go first, followed by the most recent arrivals outside its dominant
family; without a family majority, no family gets priority. The agent's housing
details show the reason, remaining time, requests to other households and
expansion progress. Save/load keeps the remaining time. A forced replacement
inherits a deadline that is still in the future; when notified at or after it,
the replacement gets one fresh world day. Volunteers keep the original deadline,
and other residents’ notices do not restart. Only a completed expansion adds places, and changed
residents, family or care arrangements cancel notices that are no longer needed.
Children and their sole caregivers never receive forced notices. If nobody
can safely be required to leave, the House stays visibly overcrowded while
adults arrange expansion or a voluntary split with care preserved.

During notice an adult can ask another household with room, still requiring
every adult member's agreement, or start their own household when no suitable
home can be asked. If notice expires with no home ready, the eligible adult
leaves membership and keeps a visible housing need; goods and people still move
physically under the departure rules. The
[Windows relocation checks](../playtest/599-overcrowding-relocation.md) remain
pending.

Until a household formed alone builds its House, its adult relies on clothing
and natural storm cover. This is a basic
version: it has not been checked by hand in the Windows game yet. New Shelters,
Storehouses,
Cooking fires and Stone hearths are retired; standing ones in old saves remain.
Cold adults use hearths they can reach. A blocked hearth does not keep them
from tending another reachable hearth or seeking natural storm cover; wood
is used only after they arrive.
Natural storm cover can reduce exposure without stopping cooling. An agent
who is still cooling can walk to a reachable lit House; protection that
already stops cooling lets them stay where they are.
A guest's shelter-only invitation does not make another household's lit hearth
a heating option.
The Weaving frame and its woven clothing are gone. Each starting agent's
garment waits in their household's House, and new clothing comes only from a
household's Tailor Shop.

Agents can now equip a padded coat for cold weather, a rain cloak for wet
weather, or a basic garment. The House makes rope and baskets; the Tailor Shop
makes cloth, garments and sacks from physically delivered private inputs.
The agent card shows the worn garment, carrying aid, their condition, cargo
amount and carrying limit, and an active repair. Garments do not change map
sprites.

The trial carrying limits are 8 cargo units, 16 with an equipped basket, or 24
with an equipped sack. Other carried goods and delivery loads count toward
that limit; stored and ground goods do not. Wear can break an aid and reduce
its capacity, but existing cargo and replaced gear remain owned and intact.
Full carriers decline pickups that would add excess cargo. Food harvests need
room for the whole yield, including orchard seeds. A hungry adult can make room
for one permitted household serving when spare supplies can be set down. An
accepted caregiver can also make room to feed a hungry infant, even when
already well fed. The spare supplies go to the House if it has room or to the
camp pile otherwise, with tools set down after other ordinary supplies. Maps,
field records, books, worn gear, gear being repaired and reserved goods stay
carried. If urgent hunger leaves no other way to make enough room, an adult may
set down some of their own reserved orchard seeds too. Only the seeds actually
stored lose their planting reserve; the seeds remain household property for
later planting. Exploring still teaches
personal knowledge when there is no paper or room for a written copy. Scouts
refresh changed terrain and resources when they reach a known tile, both on the
outward trip and while returning. Existing written items keep their contents.
Broken or spoiled spare cargo can also be set down without losing it.
A filled jug moves home as one load when the adult has room for the jug and
all its contents and the House has space. Loose household deliveries keep
their four-unit batches. If a delivery spoils while carried, an adult can
walk it back to camp and set down the same goods for the household. This
frees carrying space and clears the old delivery promise. Other stock, work
or deliveries can still prevent removing or reassigning its destination.

Spoiled food cannot be eaten. Households do not yet clear spoiled food from
pots automatically, and there is no discard control. Raw grain, potatoes and
flour keep their freshness and need separate cooking work before they become
meals. Bread and other cooked food spoil; storage pots slow that loss.

Before the first House, builders carry their construction materials to the camp
pile in loads. A helper meets the adult who requested materials and transfers only
what that adult has room to carry. Once a House exists, helpers can deliver to its
storage. Household adults
can spend cloth to repair garments or sacks at their Tailor Shop, or fiber
and rope to repair a basket at their House. Work, reservations, equipment and
overloaded cargo survive saving and reopening. These paths have automated
checks; Windows playtesting and balance tuning remain pending.
Choosing a conversation interrupts a repair and releases its unspent materials.

While a household's or Town's building goes up, the map shows it on its site at
one of three stages from the October 7 art review: cleared, staked-out ground
with materials until a third of the work is done, then the footing, frame and
half a floor, then the walls with the roof half on. A Port's piles go in first.
A street lantern rises on its Road edge from a dug hole to its post and then
its unlit fitting. Hovering or inspecting a site names the building, says
whether it is waiting for materials or being built, and how much of the work is
done. Port materials wait on the actual bank, ladders follow the doorway, and
mid-zoom stages retain the approved pixels. Lantern sites remain visible when
zoomed out; their fittings stay unlit until construction finishes.
The [Windows checklist](../playtest/1090-construction-sites.md) is
pending.

### Household building sizes

Households can choose these additional sizes when planning a building they do
not already hold. The whole site must fit, and adults must gather, carry and
spend the materials and finish the work. The first Town still starts with its
1×1 Farmhouse and 1×2 Blacksmith.

| Additional size | Provisional building cost | Stock capacity |
| --- | --- | --- |
| Farmhouse 1×2 | 16 wood, 4 stone | 96 |
| Blacksmith 2×2 | 24 wood, 8 stone | 256 |
| Tailor Shop 2×2 | 32 wood, 8 fiber | 256 |
| Clinic 1×1 | 5 wood, 2 stone | 64 |
| Restaurant 2×2 | 16 wood, 4 stone | 256 |

Each size keeps its building's existing recipes and household access rules,
including Blacksmith fittings, personal handcarts and ornaments, Tailor
bandages, Clinic medicine and Restaurant meals. Each has one production place;
a larger building grants no extra worker, speed or healing effect. A household
still holds at most one building of each kind. These are choices for new
construction; changing an existing building's size remains limited to Houses
and Warehouses. The [Windows checklist](../playtest/829-building-sizes.md)
covers the new sizes and their ordinary work.

## Maps, weather and appearance

Small/Medium worlds have generated land, rivers, lakes, separate ground and
vegetation layers, resource sites and individual trees. Foot travel supports
diagonal steps and slower mountain travel. Agents wade straight across rivers
one or two tiles wide, from bank to bank: a one-tile river at half walking
speed, and a two-tile river at a provisional third of walking speed. Wider
rivers, lakes and the sea cannot be crossed on foot. Communal boats travel
between completed Ports over connected water. Wading two-tile rivers has not been checked in hands-on Windows play.
Peaks are impassable; mountains and peaks cannot hold construction. Town
streets take diagonals where the land allows.

In newly generated worlds, sand forms deserts and stretches of ocean beach
only; rivers and lakes keep grass banks, and trees and plants never grow on
sand. Forests show small groves on darker forest floor, where every tile holds
a tree, among scattered trees on forest grass. These are the first version of
the September 29 terrain direction: their numbers are provisional until
computment reviews generated maps, and they have not been checked by hand in
the Windows game yet
([#461](https://github.com/compoodment/ClankerWorld/issues/461)).

Mountains form a few large massifs instead of many small patches: one or two
on a Small world and two to four on a Medium world. Each is a long range with a
crest of impassable peaks inside a rim of mountain that agents can walk around,
and small leftover patches are flattened. A band of hills rings each massif and
is wider around larger ones. Hills are drawn as soft foothill shading over the
ground and cost the same to walk and build on as grass. Peaks never cut off any
land, rivers start at the foot of a massif, and the starting clearing always
has stone within 32 tiles on foot. Iron, gold and diamonds are found in the massifs. The counts, sizes and
hill widths are provisional until computment reviews generated maps
([#683](https://github.com/compoodment/ClankerWorld/issues/683)). Worlds
generated before this change no longer load; they are refused and their saves
are kept.

Bridges are a basic version. Where agents often wade across the same river
crossing, one or two tiles wide (six crossings by at least two agents within
two world days), a bridge appears across it. It is drawn on the map, named on
the tile card and hover readout, and walked at dry-ground speed. When a Town grows, a new side street or a street
running on past a door crosses a river up to two tiles wide on a new bridge.
Households can plan new buildings, whose streets may need a bridge; the
starting layout keeps its streets on dry land. No bridge is added where one already joins the same river
banks and agents can walk along both banks to reach it. Peaks that block either
bank can leave room for another needed crossing. A wider stretch between narrow
crossings does not cause an extra bridge when both banks still connect to the
existing one. Road links between Towns are unfinished, and bridges have not been
checked in hands-on Windows play.

Resources can deplete or regrow. Each tile holds at most one tree. Wood trees
have sapling, mature and stump stages. Felling one for wood also gives the agent
a tree seed. An adult carrying a tree seed may replant a stump, or plant a new
broadleaf or conifer on grass or forest ground outside the Town, and the Event
Log says so. Trees are never planted on sand, water, rock, snow, buildings or
Roads or bridge entrances; a refused planting keeps the seed. A sapling takes 3 days to grow. Trees
do not spread on their own. This is a basic version: agents have seeds only
after felling a tree, and you cannot yet tell an agent where to plant.

Orchard fruit trees bear fruit only in autumn. They are growing (leaves only)
the rest of the year and drop any fruit left when autumn ends. A picked tree
fruits again after 3 days while autumn lasts, and each picking gives 4 fruit.
Orchard trees cannot be planted yet. All tree numbers are provisional, to tune
in playtests ([#462](https://github.com/compoodment/ClankerWorld/issues/462)).
A Windows check of the tree stages at different zooms is still to do.

Weather varies by region and affects local survival and crops. Each 32×32-tile
region keeps its weather for a spell of a quarter of a day to a full day. A
storm lasts at most three-quarters of a day, and that region then gets at least
half a day without another. Rain nearby makes rain a little more likely. These
values are a prototype for playtesting ([#204](https://github.com/compoodment/ClankerWorld/issues/204)).
Recent rain gives a modest soil-moisture estimate. The map shows rain, snow, storms and optional
haze/flashes, and darkens gently at night. Drifting visual edges do not mean weather fronts actually move
between regions yet.

The map uses the pixel art approved in the October 1 art review: every ground
type, with mountains, peaks and hills drawn from the world's elevation as one
landform spanning many tiles, and snow that thins into frost at its edges;
water; Roads and plank bridges, with streets running up to each bridge; trees
and natural sites; barrel, saguaro and prickly-pear cacti on about one in five
desert cactus-cover tiles; farm fields as tilled soil with each crop shown at
its stage; every current building; item icons; and the interface icons.
At close zoom, physical handcarts use the approved empty and loaded pictures
in eight directions, including the two loaded pulled poses. Smaller zooms
keep the existing cart icon while the
[separate 16px artwork](https://github.com/compoodment/ClankerWorld/issues/914)
is pending.
Agents face the way they last moved and show walking steps, and carrying,
working, talking or hurt poses chosen from what the game already knows about
them. A mountain range near the camera can show the earlier per-tile art for
a moment while its relief is drawn. Picked, harvested and per-site depleted
states are drawn but need the game to track them. The interface uses wooden
frames and parchment panels, with pixel fonts:
Fusion Pixel for body text and Timber capitals for headings. The Main Menu shows
the ClankerWorld logo over an animated pixel-art valley with snowy mountains
that follows the Light or Dark theme. Mineral/clay sites exist; some
materials still lack a complete production chain.

## Saves, keys and inventions

Worlds keep their own saves, model assignments and autosave settings. API keys,
pairing and the model-call count and limit belong to the installation. The
Windows host protects stored keys for the current Windows user; Unix hosts use
private file permissions. Damaged or wrong-user key data is preserved. Moving
installations may require re-entry.

The rotating autosave schedule recovers within its configured interval after
the host's clock moves backwards, including across a restart.
Rotation keeps the newest copies on each known branch despite earlier future
dates, while named manual saves remain separate.

If a world tick fails, the server holds the world paused. A failed active
checkpoint write can retry while paused, but other faults require operator
inspection. The detailed recovery screen is unfinished; preserve unsaved
in-memory progress before restarting the server.

Named saves can be overwritten after choosing one and confirming. Load World
can load an older save of the current world. Playing on from it starts a new
branch and keeps the saves of the first version of events
([#679](https://github.com/compoodment/ClankerWorld/issues/679)). Load Save and
Save World draw a world's branches as a timeline over its seasons, mark where
the running world continues with **You are here**, and keep the save list,
grouped by branch, behind a **Timeline / List** switch
([#680](https://github.com/compoodment/ClankerWorld/issues/680)). A host from
before the timeline can't say where the world continues, so the marker is left
out. Neither has been checked by hand in the Windows game yet. Unusable list
metadata is isolated so sound saves remain reachable. During the alpha, a save
from an older build may stop loading after an update; the game refuses it with a
reason and keeps the file. Mod compatibility and history retention remain design
questions. Technical rules
are in [saves and replay](development/saves-and-replay.md).

The Mod Library lists the current world's recorded packages read-only, one card each with
its name, who proposed it, its version and whether it is in use. Previously recorded
data-only building proposals and host approval rules remain, but agents no
longer create the retired shelter, storehouse or hearth proposals, and they do
not build approved designs, because households plan only their own buildings. General invention,
player review controls, personal libraries and mod import/export are unfinished.
Asset-validation and rights tools are built but not connected to a complete
creator experience. Arbitrary generated scripts are disabled; no sandbox has
been chosen. Multiplayer and public worlds are outside the current plan.

## Work and testing still to do

[Issues](https://github.com/compoodment/ClankerWorld/issues) holds bugs, work,
experiments and open decisions. [The playtest list](../playtest/README.md)
records hands-on checks still needed for merged changes. Code, tests and exports are evidence
for the build; an export alone is not a Windows playtest or proof of the running
server. This documentation pass did not inspect that server.

New World includes Low/Normal/High terrain controls under More options and a
resettable 50%-water preset for Small and Medium. All settings feed the matching
preview and saved generation options. See [More New World options](playing.md#more-new-world-options);
Windows interaction and real preview latency still need playtesting.

## Permanent deletion

The save list offers confirmed deletion of one snapshot, including from the
title screen before entering the current world. Load World offers
separate deletion of an inactive world and all its saves. Other worlds, provider
credentials and pairing remain unchanged. Interrupted deletion stays hidden
from loading and is retried on host startup. Native Windows interaction still
needs playtesting. See [save controls](playing.md#save-and-return).
