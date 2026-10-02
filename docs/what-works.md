---
title: What works today
type: product-status
status: active
updated: 2026-10-02
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
| Move around and inspect the map | Available in the game | Zoom, overview, wrapped east/west movement, tile facts, building cards with stored items as icons, and separate filters for Town title, household use, disputes, building property and Town borders. The disputes filter stays empty in normal play, because land requests cannot be filed yet. A building card does not yet list recent storage changes or what a workstation can make, and work in progress does not show its materials. |
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

Player renames follow the same rule: a full name held by another living or
deceased agent is refused. The Profile explains that the name is taken and
keeps the name field open with your attempt in it until you change it, close
it or choose another agent. This makes no model request. Renaming keeps the
same person and leaves past spoken lines as they were.

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

The agent card's **Speak to them** box sends a message as a **Suggest** or an
**Order**. A new suggestion or recognized order asks for one fresh decision from
the agent's planning model containing the exact words; a queued order reaches it when that order
becomes active. Any brief reply comes in the same response, without a separate
acknowledgement request. Order steps do not cause requests every tick.
The card keeps them under **Your messages** and shows whether the model heard
them, whether an order is still open, and any short reply separately from
private thoughts.

Orders currently cover eating carried food, collecting accessible household
food to eat, going to a known food source or discovering one through ordinary
exploration, and gathering berries, fruit or wild greens from a matching source.
Examples include "eat 3 berries", "gather two berries", "gather berries from
berry-patch", "go to berries at (12, 4)" and "keep gathering food until
cancelled". Quantities count food actually eaten or gathered; travel finishes
on arrival at the food source. Eating waits until the agent is hungry enough,
and a full load blocks gathering with a reason.

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
[playtest list](../playtest/586-observer-guidance.md) and the
[food-order checklist](../playtest/587-food-orders.md).

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
| Food, warmth, illness, clothing and shelter | Available in the game | Basic diet/recovery. Agents treat 40% fullness and 60% warmth as comfortable, and survival becomes urgent below 20% fullness, or below 35% warmth while exposure continues. These are provisional values ([#140](https://github.com/compoodment/ClankerWorld/issues/140)). No energy meter or sleep. Medicine and Clinic effects are not active gameplay. |
| Gathering and carrying | Basic version | Agents gather named berries, wild greens and orchard fruit, keep a hearth burning, and collect shared tools. Field harvests remain on their tiles until carried. Agents carry raw crops and seeds to private Farmhouse/Silo storage, ready-to-eat greens and fruit to the House, flour back to the House, and wood and iron ore into the Blacksmith. |
| Farming and crafting | Basic version | Adults from a household holding a Farmhouse use wooden or iron hoes to prepare fertile land, carry grain seed, potatoes or cultivated-green seed, tend the crop and harvest it. Wooden and iron sickles make harvesting faster. Fields show each stage on the map and overview; inspection names the soil's fertility and the household. Harvests stay on the field, with planting stock reserved for another crop, until physically carried into finite private farm storage. Orchard seeds grow saplings that mature and fruit in autumn. The household holding a Blacksmith makes and repairs tools from real materials: axes fell trees, pickaxes unlock finite stone, iron and rare deposits, and hammers speed building work. Iron knives speed food preparation and suitable crafting. Tools wear during successful work; loose fallen wood can be gathered by hand if an axe breaks. Grain mills into flour at the Farmhouse. A Tailor Shop makes cloth and clothing. If a household workshop recipe cannot get its ingredients, it pauses so an adult can choose another task; its saved plan can resume when supplies return to the building. Rates remain provisional. |
| Clay, pottery and water | Basic version | A household can dig finite clay and make storage pots and water jugs at its House. A pot holds up to 8 food and slows spoilage; hungry adults, and caregivers feeding an infant, take ready-to-eat servings from it. A jug holds up to 4 fresh water, which an adult can collect from a reachable riverbank or lakeshore and return to the House. Collecting a jug requires carrying room for the jug, its contents and some water. Fresh water has no active cooking, medicine-making or animal-care use yet. |
| Building new buildings | Basic version | A household plans a House, Farmhouse, Blacksmith, Silo, Tailor Shop or optional Store it does not hold yet, one of each, once it has the materials in hand. It gathers missing materials first. Only a household holding a Farmhouse builds a Silo, within two tiles of it; farm stock reaches either building in carried loads. New shared Town buildings are not offered yet. |
| House resident places, expansion and guests | Basic version | A House gives three resident places per tile, or four when one recorded domestic family unit is at least two people and a strict majority. Travelers and infants count; dead people and invited storm guests do not. A full House can be expanded for more places when the work completes. Add Agent and unanimous household admission check the House's room before adding someone to a household that holds one. Birth still completes into the primary caregiver's current household and can make it overcrowded; the building card and agent context show the count and limit. Existing overcrowding does not yet relocate anyone. Adult household members may also expand a nearly full House for storage; adult Town residents may expand a nearly full Warehouse from 2×2 to 2×3. Expansion keeps identity, stock and cooking jobs, reserves materials and cancels safely if space or permission changes. Any adult household member may invite or revoke a named storm guest. Guests cannot use House stock or cooking. Storage limits, costs and work time are trial values. |
| Local exploration and physical maps | Basic version | Short outings record personal knowledge and can produce a map or field record to share or barter. Purposeful distant exploration is unfinished. |
| Trade, relationships, conversations and teaching | Basic version | One-for-one barter, positive trust, accepted/refused partnerships, and bounded public conversations with mutual consent for a structured trust effect. Close biological relatives cannot become partners or plan a child together: parents and children, grandparents and grandchildren, full or half siblings, and aunts or uncles with their nieces or nephews. First cousins can; shared households and caregiving do not count as kinship. Each agent has at most two conversation starts or acceptances per world day; this and the six-turn limit are provisional. Adults can ask a free, healthy agent with a saved skill for a practical lesson; the learner keeps the skill, teacher and time, and the agent card shows them. Skills currently change no access or work speed. Pricing, currency, conflict and broader group dialogue remain unfinished. |
| Household shops | Basic version | Adults can offer exact goods for goods kept at a nearby Farmhouse, Blacksmith, Tailor Shop or Store. Both traders meet there before anything changes hands. The buyer carries the purchase; payment goes into household stock at that shop. Buyers can seek better tool tiers or clothing that protects them better in the current weather. Store goods must be carried in first. The building card shows the terms and progress; cancelled offers release both lots. Buying grants no access to private stock, cooking or household membership. Market stalls, tool-making orders, Restaurants and Clinics remain unfinished in [#564](https://github.com/compoodment/ClankerWorld/issues/564). Barter rates and shelf sizes are provisional. |
| Parenthood, life stages and death | Basic version | Ordinary consent/preparation binds an explicit primary caregiver and intended home; the accepting parent chooses a named caregiver-and-home option. Birth joins the caregiver's current household even if it has changed or is full, and the other parent stays put. Infant care, child talk/play/help and age restrictions are enforced. While fewer than eight non-elders live, the continuity rule lets a partnered couple with no infant put off a child for up to two days but not refuse. Parents' selected child model is recorded at birth; children without an explicit model use safe local choices. The owner can later choose another model or leave the child unconfigured; world defaults are not inherited. |
| Towns, household property and government | Basic version | First-Town membership/borders, building ownership, household stores, shared-food council, and an adult with no household asking to join a household that holds a House, with every adult member's agreement. The accepted first-Town layout records Town title over its connected land and starter household use rights on owned building footprints; later border growth does not add title. Add Agent uses recorded rights; one pending request does not assign a household, and conflicting claims make placement ambiguous. Owners can reassign or remove buildings when stored goods, deliveries and active work allow; this leaves Town borders and land rights intact. Any agent can physically recover unreserved communal stock from an empty Town's Warehouse. Recorded multiple Towns can be saved and validated, but leaving or changing a household, founding or joining another Town, land requests, grants, consent, transfers and land-case decisions, broader law and currencies remain unfinished. |

With a usable iron pickaxe and room for a whole load, an adult can mine a finite
gold or diamond outcrop. The goods remain in their carried stock. Trial mining
pauses once the adult and household hold eight of that material; ornament
making remains unfinished.

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

While the rule is on, a partnered couple with no infant may say "not yet" to a
child, but not refuse: two world days after the rule first applies to them,
their plan goes ahead as if both had agreed. Preparation still takes time, and
the birth still needs food and shelter; a plan the rule sent ahead waits for
them instead of expiring. Such a couple may also plan another child once their
youngest has left infancy, without waiting for that child to grow up. Each
partner's own model request explains the rule and how long is left. The rule
never creates a partnership, and choosing a partner stays voluntary. The
threshold and two days are provisional; a check based on the real risk of the
world dying out comes later. This has automated checks but no Windows playtest
yet.

On death, a bounded final model choice can leave the estate to the household or
name one living recipient for the whole estate. Interrupted or invalid choices
use the household path. Per-item bequests, debts, minors and inheritance law
remain unfinished. Memories do not automatically pass to children.

Towns grow along their streets. Building sites that can face an existing Road
rank higher. Each new building's street runs on a few tiles past it, and a
building away from the Roads gets a new side street. Town borders and
building-site ranking are provisional. The first accepted layout records Town
title over its then-connected land; later border expansion does not add title.
Starter household use rights cover the footprints of the buildings assigned to
each household. Add Agent uses building/field ownership and recorded use
rights, then Town title or border: one clear household claim sets the household,
and land claimed by one Town gives Town membership. A building's current owner
comes before another household's use right on its footprint, so a reassigned
building places new agents with its new owner. A lone pending use request does
not create household membership. Disputed land and other conflicting claims are
refused for a new placement, but do not move existing residents or stop
ordinary movement and work. The server checks the preview against current
records again when the adult is placed. Walking does not change membership. An
adult with no household cannot build a House. Instead they can ask a household that holds a House with a free
resident place in their Town to take them in. Every adult member of that household must agree within
the same short window as other proposals; one refusal or no answer ends the
request, and that household is not asked again for two world days. Standing
beside a House grants nothing, and a pending request grants no access to the
household's food, stock or shelter. Once every adult agrees, the newcomer is a
member of that household. The agent's profile and its own model request say
the real blocker: no household, a House still to plan, missing materials, no
legal site or an overcrowded House. [Solo formation and departure](game-design/towns.md#household-membership),
with [personal goods and dependent care](game-design/towns.md#household-goods-and-departure),
are agreed but not implemented ([#593](https://github.com/compoodment/ClankerWorld/issues/593)).
The agreed [House resident limits and expansion](game-design/towns.md#house-resident-capacity-and-relocation)
are implemented by [#598](https://github.com/compoodment/ClankerWorld/issues/598).
Relocation for existing overcrowding remains in
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
diagonal steps and slower mountain travel. Agents wade straight across rivers
one or two tiles wide, from bank to bank: a one-tile river at half walking
speed, and a two-tile river at a provisional third of walking speed. Wider
rivers, lakes and the sea cannot be crossed on foot, and there are no boats
yet. Wading two-tile rivers has not been checked in hands-on Windows play.
Peaks are impassable; mountains and peaks cannot hold construction. Town
streets take diagonals where the land allows.

In newly generated worlds, sand forms deserts and stretches of ocean beach
only; rivers and lakes keep grass banks, and trees and plants never grow on
sand. Forests show small groves on darker forest floor, where every tile holds
a tree, among scattered trees on forest grass. A band of hills rings each
mountain area. Hills are drawn as soft foothill shading over the ground and
cost the same to walk and build on as grass. These are the first version of the
September 29 terrain direction: their numbers are provisional until computment
reviews generated maps, and they have not been checked by hand in the Windows
game yet ([#461](https://github.com/compoodment/ClankerWorld/issues/461)).
Worlds generated before this change no longer load; they are refused and their
saves are kept.

Bridges are a basic version. Where agents often wade across the same river
crossing, one or two tiles wide (six crossings by at least two agents within
two world days), a bridge appears across it. It is drawn on the map, named on
the tile card and hover readout, and walked at dry-ground speed. When a Town grows, a new side street or a street
running on past a door crosses a river up to two tiles wide on a new bridge.
Households can plan new buildings, whose streets may need a bridge; the
starting layout keeps its streets on dry land. No bridge is added where one already joins the same river
banks. Road links between Towns are unfinished, and bridges have not been
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
haze/flashes. Drifting visual edges do not mean weather fronts actually move
between regions yet.

The map uses the pixel art approved in the October 1 art review: every ground
type, with mountains, peaks and hills drawn from the world's elevation as one
landform spanning many tiles, and snow that thins into frost at its edges;
water; Roads and plank bridges, with streets running up to each bridge; trees
and natural sites; barrel, saguaro and prickly-pear cacti on about one in five
desert cactus-cover tiles; farm fields as tilled soil with each crop shown at
its stage; every current building; item icons; and the interface icons.
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

Worlds keep their own saves, model assignments and autosave settings. API keys
and pairing belong to the installation. The Windows host protects stored keys
for the current Windows user; Unix hosts use private file permissions. Damaged
or wrong-user key data is preserved. Moving installations may require re-entry.

If a world tick fails, the server holds the world paused. A failed active
checkpoint write can retry while paused, but other faults require operator
inspection. The detailed recovery screen is unfinished; preserve unsaved
in-memory progress before restarting the server.

Named saves can be overwritten after choosing one and confirming. Load World
can load an older save of the current world. Playing on from it starts a new
branch and keeps the saves of the first version of events; the save list groups
saves by branch ([#679](https://github.com/compoodment/ClankerWorld/issues/679)).
This is a first version that has not been checked by hand in the Windows game
yet, and the planned timeline graphic is not built
([#680](https://github.com/compoodment/ClankerWorld/issues/680)). Unusable list
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
