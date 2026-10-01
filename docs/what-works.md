---
title: What works today
type: product-status
status: active
updated: 2026-10-01
---

# What works today

This describes the **current repository build**, not every feature in the
[game design](game-design/README.md). The private alpha uses a Windows 11 x64
Godot client connected to a private world server. Repository changes may still
need deployment or testing on the owner's laptop. Check the tested build on an
issue before treating a source fix as a verified game fix.

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
| Move around and inspect the map | Available in the game | Zoom, overview, wrapped east/west movement, tile facts, building cards with stored items as icons, and household/Town filters. General land claims are not recorded. A building card does not yet list recent storage changes or what a workstation can make, and work in progress does not show its materials. |
| Pause, inspect agents, view family trees and read events | Available in the game | Deceased profiles retain recorded thoughts and memories; old deaths without an archive cannot be reconstructed. |
| Display and interface settings | Available in the game | Themes, window size, weather switches and date/time formats. The interface grows with the screen in whole steps (100%, 200% at 1080p and 1440p, 300% at 4K) with no setting, and the game always draws at the screen's own resolution. Windows visual and keyboard acceptance is still being checked. |

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
preview shows every candidate's coverage and creates the exact selected map.
If a selected map misses a target, Create World stays unavailable until the
player accepts the displayed result or changes the seed. Uniform Dry and
polar-only settings do not use those trial bands; each band applies only while
its own forest or mountain control is Normal. This is connected to the normal
owner/server path; the current measurements are
automated fixture evidence, not a Windows visual playtest or a final balance
claim.

World time and hosted calls stop after the last connected client's short grace
period. Returning makes no offline progress; a manually paused world stays
paused. New worlds use six-minute days and a 40-day year as the current playtest
pace, subject to model/server load.

## Agents and their models

Each starting agent has a provider, model and key assignment. Stored keys can
be shared or selected independently. An agent's Profile lets you change these
choices and rename the person. For OpenAI and Ollama Cloud, the model is picked
from the game's own short list, newest at the top, or typed by name. The host checks
the chosen key with the provider and greys out listed models it can't use. That
check and the listed model names have been tested against recorded sample
replies, not yet against live provider accounts. Jev can be switched on or off
for a paused world.
When a personal model names a new agent, it gets a stable first-letter hint to
encourage varied names. If the chosen full name matches another agent's name
after Unicode normalization, case folding and whitespace cleanup, the game
asks once more without showing the other name. Deceased agents still count.
If the second answer is unavailable or also taken, the person keeps the
placeholder name until the player changes it.

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

Personal models choose from legal actions. Each request gives the agent's
name, life stage, personality, aspiration, household, hunger, and warmth and
illness where known. It also gives their latest private thought, a few relevant
memories and some places they know. Newly placed adults can choose their own
personality and aspiration in their first personal-model reply. The choice is
saved and shown on their profile. A missing or invalid choice keeps "undecided"
and "find a purpose" without an extra call; later replies cannot overwrite it.
Children's initial identity and later life changes are still unfinished. The household
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

The agent card's **Speak to them** box sends a message as a **Suggest** or an
**Order**. This is a basic version. The game understands only orders to gather
food, eat or go toward food. The agent's model does not see the words. An order
the game can't act on closes at once, and the Event Log says the agent didn't
understand it. An order that can't be carried out yet waits without extra model
requests. The card does not yet show which orders are still waiting.

Thoughts, memories, beliefs and explored map facts belong to the individual
agent. The player can inspect mistakes and where a belief came from. Jev can
rank existing memories during a normal call. Agents can also start a nearby
public conversation: each speaker uses their own assigned planning model,
accepted speech is saved with the people who could hear it, and only those
listeners receive a hearsay memory. A conversation can have up to six public
turns and one wrap-up; both people must accept the same wrap-up before its
structured effect applies. These conversation and daily-call limits are still
provisional and need a Windows playtest. Private thoughts are never shared as
conversation history. Other automatic experience capture and generated memory
summaries remain unfinished.

## Life, work and society

| Feature | Status | Current limits |
| --- | --- | --- |
| Food, warmth, illness, clothing and shelter | Available in the game | Basic diet/recovery. Agents treat 40% fullness and 60% warmth as comfortable, and survival becomes urgent below 20% fullness, or below 35% warmth while exposure continues. These are provisional values ([#140](https://github.com/compoodment/ClankerWorld/issues/140)). No energy meter or sleep. Cloth bandages and Clinic medicine use actual supplies and apply gradual recovery. Another adult chooses a named caregiver; revoking care stops the remaining effect. Values are provisional and hands-on playtesting is pending. |
| Gathering and carrying | Basic version | Agents gather and eat food, keep a hearth burning, and collect accessible tools. They carry grain to the Farmhouse, flour back to the House, and wood, stone and iron ore into the Blacksmith. Loose fallen wood can be collected by hand. Stone, ore, gold and diamond extraction require progressively better pickaxes; deposits run out. Houses craft rope and carrying baskets; Tailor Shops make larger sacks. One basket, sack or leather satchel can be equipped at a time. The current trial allows 8 cargo units without an aid, 16 with a basket and 24 with a sack or satchel; the selected garment and aid have separate slots, while vessel contents and other equipment count as cargo. Carrying limits preserve existing oversized loads and refuse incoming goods until there is room. |
| Pottery and fresh water | Basic version | Adults gather clay and fuel, make reusable jugs and pots in their House, and carry fresh water from rivers or lakes. Each vessel holds eight items; a pot halves spoilage for its actual contents. Contents and capacity appear in inspection. These values are provisional; cooking and care uses of water are tracked in [#561](https://github.com/compoodment/ClankerWorld/issues/561) and [#565](https://github.com/compoodment/ClankerWorld/issues/565). Automated generated-world flow and intermediate save checks passed; a hands-on playtest remains pending. |
| Farming and crafting | Basic version | Work follows the buildings a household holds, not a role. The household holding the Farmhouse tills neighbouring field tiles with a hoe, plants grain, potatoes or greens, tends and harvests them, and carries real harvest lots to its Silo or Farmhouse. One grain mills into one flour. Planting seed is reserved before sale, and orchards grow from distinct orchard seeds and fruit in autumn; the household holding the Blacksmith makes wooden, stone and iron axes and pickaxes plus hoes, hammers, sickles and knives, and refines 2 iron ore with 1 wood into 1 iron from its on-site stock. Tools wear, and an adult carries a worn tool to their own Blacksmith for repair with its materials. Broken tools need replacement. Hammers help building and repair work; knives speed food and clothing preparation. The agent and building cards report tool condition. Costs and speeds are provisional. Hoes and sickles affect preparing, tending and harvesting individual plots; field inspection shows stages, workers and actual waiting crops. The household holding a Tailor Shop weaves plant fiber into cloth and sews cloth into clothing, padded coats, rain cloaks and sacks, carrying inputs in from its own stock or gathering them. Clothing protects an agent when worn, and garments and sacks are repaired at the Tailor Shop with reserved cloth over eight work ticks; baskets use reserved fiber and rope at the House. Leather gear uses leather at the Tailor Shop. Interrupted repairs release unused materials. Any household can cook in its own House. If a household recipe cannot get its ingredients, its saved plan pauses so an adult can choose other work; a fresh recipe choice resumes it when materials return. A communal workstation, which no household holds, serves any agent, but a normal game does not build one yet. Flour is used for bread at a House or Restaurant, and water and named foods are carried into those buildings for cooking. |
| Building new buildings | Basic version | A household plans only buildings it needs for itself: a House, Farmhouse, Blacksmith, Silo or Tailor Shop it does not hold yet, one of each, once it has the materials in hand. It gathers missing materials first; in offline runs the first household built a Blacksmith and the second a Farmhouse within two world days. Only the household holding a Farmhouse builds a Silo, within two tiles of it, and its harvests other than ready food are stored there. Adults can also plan household Stores, Restaurants and Clinics, and contribute actual materials to Town Markets, Town Halls and shore Ports. Placement keeps reserved plots, docking water and retained physical stock clear. |
| Expand storage and invite House guests | Basic version | Adult household members may expand a nearly full House from 1×1 to 1×2 and then 2×2. Adult Town residents may expand a nearly full Warehouse from 2×2 to 2×3. Work keeps the building's identity, stock and cooking jobs, reserves materials, and cancels safely if the space or permission changes. Any adult household member may invite or revoke a named storm guest. Guests cannot use House stock or cooking. Storage limits, costs and work time are trial values. |
| Ornaments and equipment | Basic version | The Blacksmith refines gold and makes gold ornaments, optionally set with a diamond. Agents can wear them, give them to nearby agents or barter unequipped ornaments. Spears, swords, shields and basic armor can be made, equipped, replaced and repaired with on-site iron. Recipes and repair values are provisional. These are physical equipment items; combat actions, damage, protection and combat wear remain unfinished ([#568](https://github.com/compoodment/ClankerWorld/issues/568)). |
| Ports and communal boats | Basic version | Adults build Town Ports on legal shores in four rotations, preserving their land approach and six docking-water tiles. Real wood, rope and refined iron must be carried into a Port before building its Town-owned boat. One adult and their carried goods travel together between two completed Ports, including after save/reload. A blocked landing waits one day, then returns to its origin if reachable; otherwise it keeps traveler and cargo aboard. Costs, work time and speed are provisional. A generated-world runtime probe built and traveled in a real boat; hands-on Windows playtesting remains pending. |
| Household animals and horses | Mechanics available for acquired animals | Chickens provide eggs, sheep provide wool, and cows provide milk in an actual jug after feeding, watering and care. Horses have named riding permissions and actual cargo; owners can retrieve their goods after permission is revoked. Missed care pauses work without inventing deaths. Hide from a recorded natural death can become leather clothing. Acquisition, breeding and natural-death schedules remain open, so generated worlds do not begin with a herd. These values need playtesting. |
| Local exploration and physical knowledge goods | Basic version | Short outings record personal knowledge. A House makes paper from fiber and actual jug water; an adult writes a map, one-site record or book at their own House, consuming on-site paper and a cloth book cover. The item can be carried, read, copied, shared or traded. Copies keep the actual source's contents and original discoverer. The generated-world runtime probe followed exploration, paper-making, book writing, transfer, reading, copying and save/reload; a hands-on playtest remains pending. |
| Trade, relationships, conversations and teaching | Basic version | Physical barter and business catalogues use actual offered goods and exact payments, with room reserved at both destinations. Adults carry stock into their Store or household Market stall; other Town residents can buy without taking private stock. A stall remains held while goods or receipts are stocked and is released when empty. Positive trust, accepted/refused partnerships and bounded public conversations are also available. Each agent has at most two conversation starts or acceptances per world day and up to six public turns; a structured trust effect needs mutual wrap-up consent. These conversation limits are provisional. Adults can ask a free, healthy agent with a saved skill for a practical lesson. The learner keeps the skill, teacher and time; the agent card shows them. Skills currently change no access or work speed. Prices are provisional barter quantities. Agent-created currency, conflict and broader group dialogue remain unfinished. |
| Parenthood, life stages and death | Basic version | Consent/preparation, infant care, child talk/play/help and age restrictions. Parents' selected child model is recorded at birth; children without an explicit model use safe local choices. The owner can later choose another model or leave the child unconfigured; world defaults are not inherited. |
| Towns, household property and government | Basic version | First-Town membership/borders, building ownership and household stores are available. Household admission needs every adult member's agreement. A completed Town Hall holds a continuing register of willing adults. At eight adults the Town elects three supported representatives for ten days, using revisable one-day ballots, a one-day cutoff runoff and a recorded fair draw. Replacements serve the old term remainder; regular voting opens a day before term end. Representation continues down to four adults; at three all adults govern until eight again. Failed elections use adult fallback and a one-day retry. Proposal votes are explicit and final; changed members or governing form cancel unfinished requests. Hall notices and elections are saved, and communal policy preserves private household stock. Membership integration (#602), broader law, enforcement and repeal (#631, following the accepted #619 design), currencies and land disputes remain unfinished. Council rules follow #617/#618; balance values are provisional. |

Life-stage thresholds are child at day 3, adult at day 15, elder at day 45 and
death by day 60 from birth. Added adults begin at a nonzero age. These are
playtest values, not settled population balance.

On death, a bounded final model choice can leave the estate to the household or
name one living recipient for the whole estate. Interrupted or invalid choices
use the household path. Per-item bequests, debts, minors and inheritance law
remain unfinished. Memories do not automatically pass to children.

Towns grow along their streets. Building sites that can face an existing Road
rank higher. Each new building's street runs on a few tiles past it, and a
building away from the Roads gets a new side street. Town borders and
building-site ranking are provisional. Add Agent uses the recorded tile
ownership: one household-owned building footprint sets the household, and one
Town border also gives Town membership. Unclaimed land in one Town gives Town
membership without a household; land outside a Town starts an independent
household. Overlapping household footprints or Town borders are refused, and
the server checks the preview against current records again when the adult is
placed. Walking does not change membership. An adult with no household cannot
build a House. Instead they can ask a household that holds a House in their
Town to take them in. Every adult member of that household must agree within
the same short window as other proposals; one refusal or no answer ends the
request, and that household is not asked again for two world days. Standing
beside a House grants nothing, and a pending request grants no access to the
household's food, stock or shelter. Once every adult agrees, the newcomer is a
member of that household. The agent's profile and its own model request say
the real blocker: no household, a House still to plan, missing materials or no
legal site. [Solo formation and departure](game-design/towns.md#household-membership),
with [personal goods and dependent care](game-design/towns.md#household-goods-and-departure),
are agreed but not implemented ([#593](https://github.com/compoodment/ClankerWorld/issues/593)).
The agreed [House resident limits and relocation rules](game-design/towns.md#house-resident-capacity-and-relocation)
likewise await [#598](https://github.com/compoodment/ClankerWorld/issues/598) and
[#599](https://github.com/compoodment/ClankerWorld/issues/599).
Until solo formation works, an adult nobody takes in relies on clothing and
natural storm cover. This is a basic
version: it has not been checked by hand in the Windows game yet. New Shelters,
Storehouses,
Cooking fires and Stone hearths are retired; standing ones in old saves remain.
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
room for the whole yield, including orchard seeds. A hungry adult without that
room first sets down spare supplies for their household, in the House if it
has room or on the camp pile otherwise. Tools go last; maps, field records,
worn gear and reserved goods stay carried. Exploring still teaches
personal knowledge when there is no room to carry a new map or field record.
Broken or spoiled spare cargo can also be set down without losing it.
Before the first House, builders carry their construction materials to the camp
pile in loads. A helper meets the adult who requested materials and transfers only
what that adult has room to carry. Once a House exists, helpers can deliver to its
storage. Household adults
can spend cloth to repair garments or sacks at their Tailor Shop, or fiber
and rope to repair a basket at their House. Work, reservations, equipment and
overloaded cargo survive saving and reopening. These paths have automated
checks; Windows playtesting and balance tuning remain pending.
Choosing a conversation interrupts a repair and releases its unspent materials.

## Maps, weather and appearance

Small/Medium worlds have generated land, rivers, lakes, separate ground and
vegetation layers, resource sites and individual trees. Foot travel supports
diagonal steps, one-tile river crossings and slower mountain travel. Peaks are
impassable; mountains and peaks cannot hold construction. Town streets take
diagonals where the land allows.

In newly generated worlds, sand forms deserts and stretches of ocean beach
only; rivers and lakes keep grass banks, and trees and plants never grow on
sand. Forests show small groves on darker forest floor, where every tile holds
a tree, among scattered trees on forest grass. A band of hills rings each
mountain area. Hills are drawn over the ground and cost the same to walk and
build on as grass. These are the first version of the September 29 terrain
direction: their numbers are provisional until computment reviews generated
maps, and they have not been checked by hand in the Windows game yet
([#461](https://github.com/compoodment/ClankerWorld/issues/461)). Worlds
generated before this change no longer load; they are refused and their saves
are kept.

Bridges are a basic version. Where agents often wade across the same one-tile
river (six crossings by at least two agents within two world days), a bridge
appears. It is drawn on the map, named on the tile card and hover readout, and
walked at dry-ground speed. When a Town grows, a new side street or a street
running on past a door crosses a river up to two tiles wide on a new bridge.
Households can plan new buildings, whose streets may need a bridge; the
starting layout keeps its streets on dry land. No bridge is added where one already joins the same river
banks. Road links between Towns are unfinished, and bridges have not been
checked in hands-on Windows play.

Farming uses named berries, wild greens, grain, potatoes, cultivated greens and
fruit. Grain and greens return seed; potatoes are their own planting stock.
Harvested crops remain on their household's field until someone collects them,
with the current four-unit carrying load. The map and tile inspector show the
household's fields, crop state, waiting harvest and Poor, Fair, Good or Rich
fertility. Fertility depends on the world seed, rainfall, climate and ground;
sand, rock, water, mountains and snow cannot be tilled. The built-in chooser has
completed a crop cycle in a generated world, including carrying the harvest and
replaying across a save. All crop yields, work times, nourishment and spoilage
rates are provisional; checking the field overlays and pacing by hand remains
to do ([#559](https://github.com/compoodment/ClankerWorld/issues/559),
[#579](https://github.com/compoodment/ClankerWorld/issues/579)).

Households cook named meals from real ingredients at their House: simple
potato or greens meals, porridge, bread and vegetable stew. Porridge can include
berries or fruit for more nourishment and variety. The optional Restaurant
uses its own ingredients for porridge, bread, stew and a more filling meal from
bread and cultivated greens. Water arrives inside a reusable jug and cooking
leaves the jug intact. Potatoes needed to replant fields are kept aside.
Agents prepare a small household supply, including meals already being made,
and new generated Houses begin with eight portions each. Grain keeps longer
than flour, bread or cooked food. Quantities and nourishment are provisional;
hands-on checks of cooking, Restaurant use and pacing remain to do
([#561](https://github.com/compoodment/ClankerWorld/issues/561)).


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
Each picking also gives an orchard seed, which an adult can plant into a saved sapling. Orchard seeds and wood-tree seeds are separate. All tree numbers are provisional, to tune
in playtests ([#462](https://github.com/compoodment/ClankerWorld/issues/462)).
A Windows check of the tree stages at different zooms is still to do.

Weather varies by region and affects local survival and crops. Each 32Ãƒâ€”32-tile
region keeps its weather for a spell of a quarter of a day to a full day. A
storm lasts at most three-quarters of a day, and that region then gets at least
half a day without another. Rain nearby makes rain a little more likely. These
values are a prototype for playtesting ([#204](https://github.com/compoodment/ClankerWorld/issues/204)).
Recent rain gives a modest soil-moisture estimate. The map shows rain, snow, storms and optional
haze/flashes. Drifting visual edges do not mean weather fronts actually move
between regions yet.

Ground, buildings, agents and natural objects use provisional code-drawn pixel
art. The interface uses wooden frames and parchment panels, with pixel fonts:
Fusion Pixel for body text and Timber capitals for headings. The Main Menu shows
the ClankerWorld logo over an animated pixel-art valley that follows the Light
or Dark theme. None of
this is a finished production art catalogue. Mineral/clay sites exist; some
materials still lack a complete production chain.

## Saves, keys and inventions

Worlds keep their own saves, model assignments and autosave settings. API keys
and pairing belong to the installation. The Windows host protects stored keys
for the current Windows user; Unix hosts use private file permissions. Damaged
or wrong-user key data is preserved. Moving installations may require re-entry.

If a world tick fails, the server holds the world paused. A failed active
checkpoint write can retry while paused, but other faults require operator
inspection. The detailed recovery screen is unfinished; preserve unsaved
in-memory progress before restarting the server.

Named saves can be overwritten after choosing one and confirming. Unusable list
metadata is isolated so sound saves remain reachable. During the alpha, a save
from an older build may stop loading after an update; the game refuses it with a
reason and keeps the file. Mod compatibility and history retention remain design
questions. Technical rules
are in [saves and replay](development/saves-and-replay.md).

The Mod Library lists the current world's recorded packages read-only. Previously recorded
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

The save list offers confirmed deletion of one snapshot; Load World offers
separate deletion of an inactive world and all its saves. Other worlds, provider
credentials and pairing remain unchanged. Interrupted deletion stays hidden
from loading and is retried on host startup. Native Windows interaction still
needs playtesting. See [save controls](playing.md#save-and-return).
