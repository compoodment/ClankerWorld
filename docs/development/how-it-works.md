---
title: How the game works
type: architecture
status: active
updated: 2026-10-07
---

# How the game works

The current private alpha has three components. These are implementation
boundaries; [game design](../game-design/README.md) owns the intended experience.

| Component | Owns |
| --- | --- |
| `src/ClankerWorld.Simulation` | Rules, accepted world state, content, society, persistence and replay |
| `src/ClankerWorld.Viewer` | Headless server, signed owner API, clock, provider adapters and private installation stores |
| `src/ClankerWorld.GodotClient` | Windows game view, local preferences, observations and signed requests |

The host references the simulation, never the reverse. The simulation runs and
tests without Godot, a window manager, a live model or network access after
dependencies are restored. Godot does not reference the simulation: it uses the
host's versioned HTTP contract. Legacy web assets are diagnostic tools.

## Core rules

- **The server commits world state.** Client requests, model replies, mods and
  save files are untrusted input. Deterministic rules validate movement, work,
  resources, occupancy, ownership and effects. Use seeded random paths and
  keep external I/O outside authoritative transitions.
- **A tick commits all its accepted changes together.** A tick is one step of
  world time. Rejected, cancelled or stale model work cannot leave a half-applied
  action. Pause and loss of authenticated client presence invalidate in-flight
  work. A failed or unusable reply uses only explicit `safe_idle`;
  it cannot execute a strategic candidate or complete an instruction.
- **Time and spending require presence.** The host remains reachable while
  paused. Ticks and hosted model calls require an authenticated presence lease;
  the last disconnect closes the gate after about five seconds. Reconnecting
  performs no offline catch-up and does not clear a manual pause.
- **Knowledge belongs to each agent.** The player's map and observations are
  not automatically agent knowledge. Preserve ownership, source, confidence
  and correction history. A belief can be wrong without changing world facts.
  Long descendant identities remain intact in saved discovery provenance;
  knowledge keys and model-facing discoverer references use stable hashes
  only when the original identifier exceeds their size limits.
- **Credentials are installation state.** Keep keys and device authority out
  of saves, exports, observations and telemetry. See
  [device pairing](device-pairing.md) for transport and access rules.
- **Content is bounded and validated.** Current content definitions are data-only.
  Arbitrary executable agent code is disabled. The intended restricted script
  system remains design work, not permission to run generated code today.
- **Recovery preserves evidence.** Unsupported or corrupt saves must fail
  visibly without replacing valid state or inventing replacement credentials.
  [Saves and replay](saves-and-replay.md) owns formats, migration and backup rules.

## Clock and asynchronous decisions

The current host aims for one tick per real second. New worlds save 360 ticks
per day and a 40-day year with four ten-day seasons; lifecycle thresholds are
3/15/45/60 days. Newly created playable worlds start at 06:00 on Spring 1,
Year 1. A saved 90-tick calendar offset sets that clock while elapsed world
time still begins at zero, preserving founder setup, seeded ages and elapsed
deadlines. Calendar dates and daily conversation allowances turn over at the
displayed midnight. Load can affect real-time pace. The old development calendar
is not silently reinterpreted; the observation carries the saved clock values.
Its calendar pace includes the saved season lengths and clock offset, so the game names dates
such as Autumn 2, Year 1 from the world's own calendar instead of a copy. With
no season lengths, from an older host, the game shows numeric dates.

Hosted requests are dispatched after a committed tick and resolved at a later
tick boundary. The unresolved queue entry is saved; the HTTP task is not save
authority. Admission checks the request ID, provider/run epochs and current
candidate legality. An epoch is a generation marker that makes replies from
an earlier configuration or run obsolete. Other agents continue while one waits.

The OpenAI-compatible adapter can complete one omitted empty final field of a
five-field civic candidate ID when the completed ID exactly matches a uniquely
offered choice, with no other offered ID sharing that prefix. An already exact
offered ID keeps its meaning. The adapter does
not guess a subject, a nonempty choice or multiple missing fields, and the usual
epoch and candidate-legality checks still admit the completed reply.

Owner instructions are suggestions (**Suggest** on the agent card,
`Suggestive`) or orders (**Order**, `MustDo`). The next ordinary personal
planning request for that agent can include the exact original words with an
outside-observer label. The request contains only messages for its target
agent. Guidance bypasses Jev's routine route, while adults still use their
normal planning assignment or inherited world planning provider. A child
without an explicit personal-model choice and an agent whose planning model is
set to deterministic stay local; neither receives a forced hosted call.

A submitted message is checked before it changes the world. Its idempotency key
and issuer ID must each be at most 128 characters with no control characters,
its kind must be a suggestion or an order, and its text must be at most 512
characters. A request that fails these checks is refused with a validation
error; the world, its message numbering and its save stay unchanged. These are
the same limits a save applies, so an accepted message cannot leave the world
unable to save.

`ParseInstructionOrder` reads a complete, bounded grammar for eating food,
seeking a food source, harvesting food, gathering supported raw materials,
storing personal raw materials or equipment, collecting personal raw materials,
ready-to-eat food or equipment, repairing supported personal
clothing, carrying aids and tools, household field work, and moving to an exact
tile, and supported non-food production recipes. Harvest and food-source travel orders must name a supported kind or resource; explicit resource names must match a complete
identifier and the requested kind. Unsupported
objects or operations, mixed tasks, unknown explicit targets, and invalid
quantities or leftover words are rejected as not understood rather than mapped
to a nearby candidate. A recognized order retains the player's original text and the
understood action, but the simulation still checks legal choices and requires
the requested physical effect before recording progress. Names in the prompt
do not create map knowledge. Optional observer replies are tied to the exact
message ID and stored separately from private thoughts and conversation
speech. Local deterministic decisions do not mark messages as heard.

Food-source orders can use food directly observed within normal gathering
range even when the agent's map memory is full. Food kinds and exact resource
or coordinate targets still apply; distant sites require that agent's own
knowledge. Observation does not bypass availability, reachability or the
whole-load carrying checks.

Urgent food interruptions also admit the buyer's legal ready-food shop quote,
Restaurant meal visit and continuation of an open food purchase. The same
check is used for selecting the interruption and filtering model choices.
Raw ingredients, equipment and a seller's response cannot use this food
exception; payment, carrying room, physical arrival and consent still apply.

Material orders save the requested kind separately from food targets. They use
known resource facts or observation within normal interaction range; a named
unobserved site first requires physical travel. Untargeted orders may use normal
exploration. Gathering uses the existing tool pickup, whole-load capacity,
inventory, tool-wear and ecology transitions. Only a returned physical harvest
receipt advances progress. One load is the default; explicit quantities count
actual output, including a final whole load that exceeds the requested amount.
Discovery, tool collection and movement never count as harvested goods.
Only the observed resource site joins map memory; walking there adds no facts,
so the journey cannot fill the agent's bounded ledger before arrival.

`Move to tile (12, 4)`, `Go to (12, 4)` and `Travel to (12, 4)` create a
`move_to` order with a saved `TargetPosition` and one arrival. The common
unoccupied-route finder and `MoveToward` enforce walking rules, occupancy,
travel cooldowns and illness delays. House destinations check current household
membership or a saved guest invitation. An eligible Warehouse user can share its
exact tile with other occupants, using recorded Town access including abandoned-Town
salvage. Residents can also share their Town's supplying or working project site.
These destination exceptions keep stock ownership, reservations and action
permissions intact; other occupied exact destinations stay blocked.
An unavailable destination stays blocked;
it is never substituted. The order completes only when the actor occupies the
exact target tile. Repetition, quantities and extra task words are rejected.
Arrival records one firsthand fact, for the destination tile. Submission,
waiting and the walk itself add none, so a long trip cannot fill the agent's
bounded map memory.
Children and adolescents can walk under an order; infants still cannot take
instructions. The normal queue, cancellation, stale-reply and survival rules
apply without a separate model request for each step.

The strict guardian-order form is `Become guardian for <full name or exact ID>`.
It resolves one active child with an open search and saves that child's ID as
`TargetAgentId`. Only an adult can carry out this task; renames cannot retarget it.
`CanAcceptGuardian` is checked again for each step, and acceptance goes through
the existing dependent-care transition. Completion requires the actual primary
care assignment. A search that closes first leaves the order blocked rather
than replacing its accepted guardian. Queue, cancellation, stale-response
checks and urgent survival interruptions use the common order lifecycle.

Storage orders reuse the material, equipment and goods catalogues and normal personal-storage
eligibility. `StorePersonalGoods` serves both ordinary choices and orders: it
walks to the House entrance, then uses `InventoryFixture.Relocate` to preserve
ownership, condition and provenance. Reserved goods, promised deliveries,
container contents, food and selected equipment are excluded. Only a committed
relocation receipt advances the order; its identity is hashed to a fixed length
because split inventory identifiers can grow. Walking and survival actions earn
no storage progress. Default tasks count one stored lot, while explicit quantities
limit the final relocation to the remaining amount. Repetition keeps waiting
for further matching personal goods or space until cancelled. The destination is
bound once to the current household's House identity, household owner and listed
position, before travel. An optional requested coordinate must match that House.
Each step rechecks the binding, so removal, reassignment, movement or departure
blocks the task instead of retargeting it. Named foreign buildings remain unsupported.

`store_equipment` uses the existing exact `TargetEquipmentKind` and the same
garment/carry-aid/tool subjects as collection. Complete equipment names are read
before material names, so "stone pickaxes" cannot select raw stone. Explicit
quantities count `equipment_items`; default orders count `storage_loads`.
Selection and execution both use `PersonalStorageLots`, preserving the exclusions
for worn items, reservations and borrowed or promised goods. Storage does not
equip or repair goods. Model guidance and client task text name equipment storage.

Collection orders use `PersonalGoodsAwaitingCollection` and the shared
`CollectPersonalGoods` action. The actor must own the lot, which cannot be
carried, reserved in full, promised for delivery or inside another container.
Storage must belong to the current household or one recorded in that actor's
departures. Ground lots use normal pickup range. The nearest reachable eligible
lot is chosen, with stable identity ordering for ties. An optional source tile
filters this same set by its current physical position, at both selection and
execution. Moving goods away or exhausting the tile leaves the remaining order
blocked; it never falls back to another location. Building storage uses the
building's listed position. Physical pickup preserves ownership, condition, provenance
and reserved portions, with the final quantity capped by carrying space and
the requested remainder. Only the committed relocation earns progress, using
a bounded hashed receipt. Former-household collection grants no other access.
Vessels must fit with their entire contents before selection as well as execution.

`collect_goods` and `store_goods` use an exact `TargetItemKind` from a separate
logistics catalogue. They do not broaden the gathering or repair subjects.
Explicit quantities count `goods_items`; default tasks count collection or
storage loads. A vessel relocation moves one whole container family, preserves
its ownership and credits one vessel, never its contents as extra progress.

`return_borrowed` uses the same named goods plus the existing material/equipment
subjects. It binds the carried source lot and its owning household's exact
House before travel. The shared borrowed-return helper rechecks custody,
ownership, reservations, whole-vessel space and the destination at execution.
Only a committed relocation receipt earns `goods_items` or `return_loads`.
Cancellation never undoes completed movement or changes goods ownership.
When an explicit storage or return moves the exact hoe or sickle used by that
agent's ordinary field work, it first interrupts that work through the normal
cancellation transition. The new order can then move the tool without leaving
a stale field-work reference in the same checkpoint. Spare tools and unrelated
field work are unaffected.

Food collection uses the same path with action `collect_food`. A separate
bounded subject parser accepts generic food or exactly berries, fruit, wild
greens or cultivated greens, without resolving natural-resource aliases. The
lot filter uses ordinary edible-food eligibility and any exact `TargetFoodKind`.
Default orders count one `collection_loads` pickup; explicit quantities count
`food_items` and cap the last pickup at the remaining amount. Shared food
collection, harvesting and eating never credit these orders. Model guidance,
observer activity and task labels identify the action as personal food pickup.

Equipment collection uses `collect_equipment` and the existing exact
`TargetEquipmentKind` catalogue for five garments/carrying aids and 13 tools.
The parser tries complete equipment names before material names, so "stone
pickaxe" remains a tool while "stone" remains a raw material. The same personal
pickup path preserves condition and never equips or repairs the goods.
Explicit quantities use `equipment_items` and cap the final relocation; default
orders count `collection_loads`. Shared and borrowed equipment never enters
this order's eligible personal-lot set.

Repair orders save a separate `TargetEquipmentKind` for basic clothing, padded
coats, rain cloaks, baskets or sacks. The parser refuses unsupported equipment and
explicit sites. Orders filter the normal worn-item rules by this exact kind,
collect real materials through `CollectEquipment`, then use `RepairEquipment`
and `ContinueEquipmentRepair`. Ordinary repair chooses the first feasible worn item, preferring the equipped
carry aid when its materials, carrying space and private work site are usable.
Candidate selection and execution use the same feasibility checks.
A repair work record links to the active instruction; only the returned completed
repair advances its item count, with a bounded receipt derived from the actor,
start time and lot identity. New orders release any previous repair's unspent
inputs before starting their own work. Cancellation or replacement releases
reservations immediately. Survival interruption follows ordinary repair rules:
release unused inputs and restart unfinished work when the order can resume.
Save/reload retains a running repair's work counter and exact reservations.

Tool repair orders use `repair_tool` and the same `TargetEquipmentKind` field,
restricted to the 13 supported tool kinds. The parser requires the material and
tool name. `RepairableTools` shares ordinary private Blacksmith, material,
carrying and route checks; orders additionally require personal ownership and
positive remaining condition. Ordinary repair retains its borrowed-household
behavior. Order preparation protects every matching worn personal tool from
spare-cargo storage and refuses gathering that would break a requested tool.
Preparation retains the quantity needed for one repair across usable carried
material lots. Excess units may be stowed to collect another ingredient; live
reservations, promised cargo and protected tools remain unavailable for stowing.
`RepairTool` performs physical pickup, gathering, spare-cargo storage
and walking, returning a repaired lot only after the real inventory transition.
That return alone earns one repair, with a bounded receipt derived from actor,
time and repaired lot identity. Stacked worn lots split into individual repaired
units. Preparation has no reservation or timed job to unwind: cancellation keeps
already collected goods and spent tool wear. Remaining quantities and queued
work survive reload; survival can interrupt before repair resumes.

Field orders use `till_field`, `plant_field`, `tend_field` and `harvest_field`,
with an optional `TargetCropKind` limited to the three existing crops. Explicit
quantities require the word "field" or "fields" and count finished work sites.
Selection uses household access, physical routes, usable tools and actual
planting stock without the ordinary food-demand preference. It never substitutes
a different named crop. An optional `TargetPosition` filters both new tilling
sites and existing fields; it never redirects unfinished quantities to another
tile. `ApplyFieldCandidate` performs normal walking and stock
collection; `StartFieldWorkCore` binds new work to the instruction. Only a
completed result from `ContinueFarmWork` credits a bounded receipt. Cancellation
releases unused seed reservations and removes a partly tilled field. Ordinary
work retains its existing priorities and behavior; urgent survival cancels the
current work under normal field rules and the order resumes its remaining count.
Saved work must match the actor's active instruction, action, crop and any
explicit tile.
Validation refuses links to another agent, task or equipment kind.

Delivery orders use `deliver_stock` with a separate purpose, exact item kind
and requested building kind. Bounded `haul`, `supply`, `deliver`, `donate` and
`stock` commands select existing native policies for household/farm stock,
workstation inputs, spare House food, Town Warehouse surplus and Store stock.
Their plans preserve the ordinary source, demand, reserve, capacity, access
and route checks. A requested farm-storage destination filters legal choices
before applying the default Farmhouse/Silo preference.
The Silo replenishment fallback applies those same constraints: it supplies
only grain to the household's Farmhouse, never a different requested item or
stock already stored at the destination. An unavailable requested item leaves
the order blocked without binding an unrelated shipment or earning progress.

Town Warehouse donations retain four usable personally carried units of each
resource kind across all eligible lots. Storage and collection splits do not
multiply that reserve. Reserved, borrowed and promised delivery stock cannot
satisfy it. Each transfer still uses one exact source lot, its unreserved
quantity and the destination's available room.

The order binds a native route, building identity, owner and listed position,
then the exact carried lot and requested-kind quantity. Household pickup uses
the existing ownership transfer and `DeliveryBuildingId` promise; a Town
Warehouse never becomes a household delivery destination. A matching carried
shipment can be explicitly bound before its final delivery. Continuing a bound
shipment does not repeat ingredient-demand selection, because inbound stock
already reduces that demand. Each deposit rechecks custody, access and room.

Personal building and crafting projects finish a valid carried household
delivery before acquiring missing inputs. When its destination cannot fit the
whole load, the project retains its existing blocked state and cargo instead
of retrying a capacity-impossible harvest. Ordinary project reconsideration
still applies, and delivery rechecks available room before it resumes.

Typed committed movement effects provide the actual transfer identity and
quantity. Only final delivery credits `goods_items` or `delivery_loads`, never
pickup, walking, an inventory difference or an unrelated event. A selected
load waits if its destination no longer has room for it. Whole-vessel plans
sum every matching contained resource lot, preserve the entire family and
reject a move that would exceed a finite request. Only the existing grain/flour
path may take part of a vessel's contents. Cancellation keeps real cargo and
delivery promises; it does not refund ownership or revive old progress.

Content rollback checks saved production and delivery orders against the
remaining definitions before changing the registry. A package removal that
would invalidate an active or historical order is refused without changing
the checkpoint; unrelated package removal remains available.

The production catalogue includes every shipped named-meal recipe, including
the 2×2 Restaurant's exact variant definitions. House and Restaurant prefixes
disambiguate shared output names; the larger Restaurant uses `restaurant 2x2`.
The three simple-meal recipes name their ingredient rather than treating
different inputs as interchangeable. Existing whole-batch validation and
order-bound production jobs account for their two-serving yields.

Named-meal inputs extend the existing House/Restaurant `workstation_input`
routes. Supply still checks current recipe demand, source cooking reserves,
whole vessels, access and room. Prepared-food nouns extend `collect_food`,
borrowed returns, spare House-food delivery and Store stocking. Personal
storage continues to exclude loose edible goods. Loading allows prepared
food targets only on eating or food-collection orders, retaining the separate
item targets on returns/deliveries and rejecting raw-crop food targets and
mixed fields. No order fields, receipts or checkpoint layout change.

Building orders use separate construction and expansion adapters. A
`construct_building` order selects one active household building definition,
then binds its household, exact site, project start and unique instance
identity. Its own settlement project performs normal material acquisition,
travel and work. The catalogue includes the exact shipped 1×2 Restaurant,
using its normal 8 wood and 2 stone cost and household ownership rules.
Only successful placement creates the retained construction
receipt and credits the order. The receipt preserves the actor, order,
definition, owner, site and paid-material evidence after the project is
replaced or the building is removed; an unrelated existing building is never
completion proof.

The construction adapter can replace an unpaid ordinary project rather than
waiting on the project the operative order itself paused. It creates a fresh
order-bound project, with no inherited work or placement credit. A Store-stocking
order instead keeps that ordinary project while its typed delivery proceeds;
ordinary automatic Store stocking still waits for unfinished projects. Both
paths wait for a live paid production job to finish, retaining its reservations,
goods and history. Projects bound to another order or an accepted tool-making
request keep their existing checks. The shared eligibility check adds no saved
state, and neither adapter adopts the ordinary project's payment or output as
order completion proof.

An `expand_building` order binds the original building, definition, owner,
position and revision, plus the chosen next footprint and anchor. It then
links its real expansion job through `OrderInstructionId`. Normal permission,
need, placement, cost and reservation rules remain authoritative. Only that
job's successful completion earns progress; retained completed-job history
keeps the proof if the building is removed later. Urgent survival pauses
ordered work before completion, and cancellation releases only the order's
own unfinished work and unused reservations. Neither adapter adopts an
unrelated project or job.

The `ordered-building-` identity prefix belongs to the internal construction
path. Saved building orders also reserve their bound instance identities,
including after cancellation or removal. Public placement cannot reuse them;
only an order's own construction completion can place its pending identity. This
prevents replacement buildings from satisfying old bindings or mixing new
material payments with retained completion receipts. Ordinary paid rebuilding
by the same agent chooses a fresh, deterministic instance identity when its
previous identity belongs to retained order history. It still pays the normal
cost and cannot satisfy the earlier order.

Shelter orders use `seek_shelter` for one protected arrival and `tend_fire`
for one real ignition. Their saved binding pins a natural-cover position or
the exact building, owner, anchor and placement generation. Own and invited
homes are known targets; other buildings must be locally observable. An
explicit distant coordinate is approached before its shelter facts are
inspected. Firsthand map facts record only passable tiles. Inspecting an
impassable shelter or fire target can record the legal approach tile, but
leaves the unusable order blocked without adding an invalid target fact or
interrupting saving. Natural cover uses the existing bounded local search.

Arrival requires live native coverage and permission at the actor's actual
position; it does not certify warmth recovery. Ignition uses native fuel
acquisition and consumption, then records the newly created completed
`heating_fuel` reservation for one owned wood. Each fire completion has its
own reservation. Gathering uses known or locally observable wood; an absent
usable supply leaves the task blocked. Historical completion survives weather
changes, fuel expiry or building removal; unfinished work rechecks the bound
target and access.
Food survival can interrupt these tasks, but cold does not suppress the
protective action. Children may seek shelter; tending fires remains adult
work. The parser refuses quantities, repetition, durations and recovery
promises unsupported by these single-task adapters.

Production orders use `produce_item` and retain the exact recipe, output kind,
chosen work site and owned project/job identity. The job's optional
`OrderInstructionId` proves that the instruction started it. The bounded catalogue resolves
supported product names without interpreting arbitrary recipe text or model
output. `output_items` counts actual produced units and accepts only whole
recipe-yield multiples; `production_batches` counts one task by default or an
explicit number of batches. A named site constrains the same normal access and
workstation checks. Unavailable inputs or sites block the saved task.

The order advances its bound settlement project through ordinary preparation,
travel and production, even though ordinary project continuation yields to an
active instruction. Starting or waiting for a job earns no progress. Only a
committed completion of the exact bound job credits its physical outputs, with
a production receipt preventing duplicate credit. Outputs retain ordinary
recipe ownership. Cancelling or replacing an order releases its unfinished
job's inputs without adopting or cancelling another project. Survival pauses
the bound job before it can finish and resumes its remaining duration after
the actor returns. Recipe, project, job and order references are validated
together on restore. Content removal checks retained production orders,
including cancelled history, against the remaining definitions before changing
the registry. Retired generic cooking recipes are excluded; the catalogue uses
the exact active named-meal recipes described above.

A MustDo with no recognized action is closed when it is submitted: it is added
to the completed instructions with an `instruction_not_understood` event
(`<agent ID>:<instruction ID>`), which the Event Log shows. This preserves the
active and queued recognized orders. A closed unsupported order requests no
decision and does not block later instructions. Suggestive interpretation stays
separate from the strict MustDo grammar.

Recognized MustDo instructions complete only when their requested legal action
actually progresses. Default gathering counts one harvest; explicit quantities
count goods acquired or food consumed. Food-source travel finishes on arrival within
interaction range of the requested food site; exact-tile travel requires the
tile itself. Counted travel is refused.
An unrelated action, blocked movement or unavailable food leaves the instruction
pending, including across reload. A recognized new order replaces outstanding
orders unless `Queue` is true; queued orders run in submission order. Cancel
retains an idempotent receipt, including a no-op cancellation of a closed order.
Receipt-only owner changes supersede a concurrently prepared tick too.
Suggestions do not block orders. A suggestion completes only after the
addressed personal model accepts a request containing it; local choices and
provider failures do not claim it was heard.

New suggestions and recognized active orders trigger one fresh cognition request. Its prompt
marker only prevents a new request every tick; it is not a read receipt. The
same pending message remains available on the agent's later ordinary planning
requests until a personal-model result is accepted. After the fresh request,
`NeedsCognition` applies its usual rules. For example, active agents reevaluate
every 30 ticks, and idle agents reevaluate when their legal choices change or
after 300 ticks. A blocked order therefore cannot request a paid model call on
every tick.

When a hosted decision is still running, a newly queued choice or observer
message remains pending after its older reply is accepted. The next prepared
tick refreshes that work against the resulting world before another request
starts. Ordinary planning waits while the agent is busy talking; pending work
remains available after the conversation. It also waits for a life-event
identity reply, then uses the accepted identity in its refreshed observation.
Ordinary changes to the clock or need values do
not by themselves request another paid decision. Pending work is kept only for
a choice the earlier request lacked, a change in urgent hunger or warmth, or new
observer guidance; choices that merely disappeared leave the accepted reply
valid. An agent whose reply was accepted takes no extra waiting routine in that
tick, and pending work with no call in flight is rebuilt before it is sent.
Work for an order that finished before any reply carrying it was accepted is
still sent unchanged, so the agent can acknowledge it; a pause, a load or newer
guidance rebuilds that work without it. A suggestion carried by a reply that is
set aside because its order has since finished stays open for the next fresh
request. Pending observations survive
save/load and are refreshed for the resumed world before dispatch. The usual
request, provider, conversation and legal-choice checks still reject stale
replies.

Local order steps can continue while a hosted reply is pending. An accepted
decision and a local continuation do not execute the same order twice in one
tick. If local work finishes first, a valid reply to that exact original
request can still record that the model heard the message. Its old action is
discarded before execution; cancelled or replaced tasks cannot use this path.
Urgent survival uses the normal unrestricted legal candidates before
resuming the task, and repeated eating respects the normal fullness threshold.
Gathering and collecting household food check free carrying capacity before
starting a step. A provider fallback leaves the order blocked for a bounded
normal retry; it cannot strand the task permanently on `safe_idle`.

The owner snapshot sends every open message, plus the six most recently
submitted closed orders and, separately, the six most recently submitted closed
suggestions for each agent, whether or not a personal model heard them. An
order the game could not act on, or one carried out by local rules, therefore
still appears on the agent card as closed and not heard, and heard suggestions
cannot push the latest closed orders out. **Your messages** shows up to four
messages per agent, newest first by submission but always preferring open
messages over closed ones, then lists them in the order they were sent. Newer
closed messages therefore cannot hide an order that is still waiting. The
card's order line and the **All orders** reader read the same snapshot: the
oldest open order is the current one, the other open orders follow in
submission order, and closed orders come newest first. The client keeps no
order list of its own. The save keeps every message; only the snapshot is
bounded.

Pause, quit and loss of presence cancel external work without inventing an
answer. Restore can retry a still-relevant saved decision. Synchronous fixture
methods remain for isolated tests; they are not the live scheduling path.

Tick-boundary faults hold the current in-memory state and pause the world.
Active checkpoint I/O/access failures retry only the retained save while paused;
success still requires a later explicit Resume. Other faults latch for operator
inspection. Startup corrupt-save failures remain separate. Logs use bounded
category/type/tick fields, not raw exceptions or file paths. Do not restart a
held process before preserving its unsaved state. Filesystem fault injection
does not establish arbitrary mid-tick rollback or crash durability.

### Time of day and night

Time of day is worked out from the elapsed tick and the world's saved calendar
(ticks per day, season lengths and calendar offset). Darkness itself is not
saved. `DaylightRules` follows the 24-hour clock the game shows, where a tick's
clock minute is its tick of day × 1,440 ÷ ticks per day. Night follows the
seasons ([#891](https://github.com/compoodment/ClankerWorld/issues/891),
replacing the same-all-year night of
[#641](https://github.com/compoodment/ClankerWorld/issues/641)):
`DaylightRules.NightShare` gives 30% of the day on the first day of summer,
50% on the first day of winter and 40% on the first days of spring and autumn,
with an even daily step between them across each season's own length. Night is
centred on midnight: 19:12 to 04:48 at 40%, 20:24 to 03:36 at 30% and 18:00 to
06:00 at 50%. Dusk and dawn each fade over the clock hour centred on the start
and end of night (18:42–19:42 and 04:18–05:18 at 40%), so the darker half of
each fade counts as night and night covers exactly its share of the day. At
360 ticks a day a 40% night is 144 ticks, a summer-start night 108 and a
winter-start night 180, with 15-tick fades. Each tick uses its own calendar
day's share, so a night's evening follows that day and its morning the next;
both are fully dark around midnight, so nothing jumps. The rule uses whole
numbers only, so every platform agrees on every tick. Darkness is reported in
basis points, 0 in daylight and 10,000 at full night. New playable worlds and
their founder setup begin at 06:00, after the dawn fade. A saved zero-offset
world keeps midnight at elapsed tick zero. The first day of a new world
therefore has 18 hours left; later days retain their full duration.

Night adds a provisional chill of 15 exposure points per tick at full night,
faded in and out with the darkness (`NightChillAtFullDarkness`). It is added to
the weather, climate and season exposure (`OutdoorExposure`), and clothing (35
for a basic garment when dry), shelter (45) and a lit fire (+90) offset it just
as they offset weather. The same outdoor exposure decides the choice to put on
better clothing, the warmth budget for a scouting trip and the preference for
making garments. Wear on a worn garment still follows the weather alone, so a
mild night adds no repair work. In mild clear weather a basic garment or any
shelter cancels the chill; with no protection an agent loses about a fifth of
their warmth over a night. There are no night-only limits on choices, travel,
work or conversation, and no sleep or energy. Longer winter nights mean more
hours of chill. Night does not change weather or crops yet.

The warmth action keeps an agent in place when their current protection stops
cooling. Otherwise, a reachable lit hearth takes priority over nearby natural
storm cover, so a tree along the route cannot pull the agent back from the
hearth. Natural cover remains a fallback when no lit destination is reachable;
existing access permissions, routes and fuel use still apply.
The lit destination must be a heating building the agent can use; a
storm-shelter guest invitation alone does not grant use of a household hearth.

The owner snapshot carries `darknessBasisPoints`, decided by the host from the
same rule. The Godot client's `NightLayer` draws a deep blue wash, at most 40%
opaque, over the visible map just above the ground, roads, buildings and trees,
and below map labels, agent markers, weather and panels. It eases between the
once-a-tick readings, shows a newly opened world's darkness at once, and looks
the same in both themes. The World Map panel is not darkened.

Night lights ([#890](https://github.com/compoodment/ClankerWorld/issues/890))
are drawn by `NightLightsLayer`, just above the wash and under labels and
agents. It takes each placed building's family, footprint, door side and the
roof, yard and door spots from `BuildingSprites.Plan`, and decides from the
snapshot whether it is occupied (a living agent stands within its footprint)
or working (a production job runs there); nothing new comes from the host and
nothing is saved. `NightLightShapes` turns that into rows of light in the
building's 32-unit tile space: spills from windows on the front and both
sides, never the back, and from the door; a forge pool in a Blacksmith's yard;
and a Warehouse's wall lantern, whose unlit fitting also shows by day. Pools
have ragged edges from three slow sine waves seeded per tile; fires move
faster. The layer steps that drift every eighth of a second, scales light by
the eased darkness and draws rows weakest first through a shader that
brightens the ground and pulls its hue toward the light, so the ground keeps
its texture. At overview zoom a lit building is a warm speck. The same shapes
draw the art preview's night proposal (`tools/ArtPreview/Proposed/NightLights.md`),
including designs not in the game yet and the street lanterns of
[#892](https://github.com/compoodment/ClankerWorld/issues/892).

## Model inputs, usage and memories

The per-world routine helper is Off, Jev or OpenAI Decisions. Decisions uses
`POST https://api.openai.com/v1/decisions`, sharing Jev's bounded actor context
and existing owner-private memory scoring. Its `input` is serialized context;
`questions` and `answers` are arrays keyed by unique names. Action choices are
fixed candidate IDs. Memory score questions identify `source_index`, and scores
on three ordered levels map to the same 0–10,000 importance scale as Jev.
Unknown/duplicate answer names, unusable choice types and malformed scores are
refused. The runtime still admits only legal actions and requested owner sources;
a refusal or invalid action falls back to `safe_idle`. No confidence veto is added.

Decisions uses the installation's default OpenAI key or an explicitly selected
OpenAI credential slot. The world saves only the slot ID, helper and model;
missing/deleted credentials produce the existing safe provider failure. A call
reserves one installation usage attempt before HTTP, including action and memory
questions together. Reply bodies are bounded; timeout, cancellation and sanitized
call telemetry use the existing provider boundary. Changing the helper or model
while paused advances its routing revision and cancels pending work. Late replies
cannot alter actions or memory scores. An explicitly selected helper handles
eligible adult routine choices; personal assignments stay available for planning,
guidance and identity. Children still require their own explicit personal model.
Before the first explicit helper selection, existing installation routing and
its configured Jev model remain effective. Applying the displayed default Jev
choice also activates explicit helper routing; repeating it afterward is a no-op.

The Decisions picker starts with `gpt-6-luna` and permits typed model names.
Its non-billable availability check uses OpenAI's model list with the selected
key; that list confirms model visibility, not access to the Decisions endpoint.
Jev offers `jev-1.13.0` without an availability probe.


A personal-model request selects one legal candidate, not a free-form dialogue
turn. It now includes bounded actor-owned self context: name, life stage,
personality, aspiration, household, available warmth/illness and the latest
private thought. Absent fields remain unknown. Need scales are explained;
`hunger_basis_points` measures fullness (0 starving, 10,000 full).
Self context is included in the queued-observation digest. Nearby relationships,
carried inventory and current activity are not provided. Jev's routine request
sends the same three needs as flat fields after `hunger_basis_points`:
`warmth_basis_points` and `illness_basis_points`, `null` when unknown, with
their scales explained in its instructions.

`ModelNeedWords` holds the agreed alternative from
[#646](https://github.com/compoodment/ClankerWorld/issues/646): each need as a
word followed by its whole scale, worst to best, with no exact value, such as
`"fullness": "hungry (starving, hungry, fine, full; starving is worst, full is best)"`.
Fullness is *full* from 70%, *fine* from 40%, *hungry* from 20% and *starving*
below that, matching the comfortable and urgent food references. Warmth is
*warm* from 60%, *chilly* from 35% and *freezing* below that. Illness is *well*
below 25%, *unwell* below 50%, *ill* below 75% and *very ill* from there: each
step is where illness slows work further (the owner's choice on
[#672](https://github.com/compoodment/ClankerWorld/issues/672)). In words, the
personal request sends `fullness`, `self.warmth` and `self.illness` in place of
the three `_basis_points` fields, in the same positions, and drops the numeric
scale sentences; Jev's routine request sends `fullness`, `warmth` and `illness`
the same way. Unknown warmth or illness stays `null`. Nothing else in either request changes, and
the observation, its digest, admission and the simulation keep the exact values.

Both adapters take a `needFormat` setting. `ModelNeedWords.DefaultFormat` is
`Numbers`, so play sends numbers. Words were to become the default only if they
did no worse than numbers with GLM 5.3 Flash and GPT 6 Luna; they did worse
with GLM 5.3 Flash, and on October 2 the owner chose to keep numbers.
[Need wording comparison](need-wording-comparison.md) describes the comparison
harness and its results.

Newly placed adults get one opportunity to choose personality and aspiration
in the existing first personal-model action reply. Optional `chosen_personality`
and `chosen_aspiration` strings are trimmed, limited to 256 characters and
refused if they contain control characters. An accepted personal reply consumes
the opportunity even if either field is missing or invalid; the placeholder
stays without an additional model attempt. Routine replies cannot overwrite it.
The pending opportunity is checkpointed, so pause/reload discards late replies
and preserves an unconsumed choice. It selects the personal planner rather than
Jev's routine router. Choice events contain only the agent ID; chosen text stays
in that agent's saved state and later self context, not runtime logs. Children's
initial identity remains separate work.

The runtime records five named identity opportunities: midlife (half the
configured maximum life, day 30 by default), parenthood, loss of a partner,
loss of a biological parent, and becoming an elder. Each kind is recorded
once per agent. After the initial identity choice, and outside urgent needs
or another model turn, a separate request gives only the current identity and
the named moment. It requires that agent's personal planner, never a world
default or Jev. The existing installation usage meter reserves the call.
The request has a 15-second timeout; accepted optional fields are limited to
256 characters each and contain no control characters. Invalid fields refuse
the entire change. An absent, declined, failed or interrupted reply consumes
the opportunity without retry. With no selected personal model it keeps the
identity without a call.

Life-moment replies are admitted at the live tick commit boundary, rechecking
the actor, current identity, run epoch and selected provider. Pause and presence
loss cancel outstanding requests. A saved in-flight opportunity becomes
interrupted when play resumes; death finalizes it in the deceased archive.
The moment record saves accepted fields and timing. Events contain only agent
IDs and the moment kind; runtime logs record only that an agent changed its
identity. Accepted text appears in the owner profile and later self context.
The offline bounded test reaches all five moments, observes exactly five
requests, and confirms that more ticks and a reload add none.

Request text uses the game's own words (*agent*, *Town*, *House*), not the older
*inhabitant*, *settlement* and *camp*, including plurals. Both adapters omit the
clock and run/decision counters; admission uses them on the server. Households
and Towns use their recorded names. Candidate destinations use a readable name
where one is available, while the legal option IDs remain unchanged. Jev stays
narrow: choosing a legal action and scoring existing memories, without persona.
Retrieval ranks which memories to include before sending the request. Its Jev
importance scores are not sent to the personal model: those scores select the
context, rather than adding facts about the remembered event. Belief provenance
and belief confidence remain explicit.
When an unnamed agent is asked to choose a full name, the personal-model
request includes a soft first-letter hint derived from that agent's stable ID.
The hint stays the same if the request is retried, is computed per agent, and
does not reveal anyone else's name. If a current reply supplies a valid name
whose first token is already chosen by another agent, the scheduler queues one
extra metered personal-model request. That request marks `name_retry` and says
the first name is taken, without including anyone else's name. First names use
NFC normalization, collapsed Unicode whitespace and `OrdinalIgnoreCase`;
different middle names or surnames do not avoid a collision. Deceased agents
with chosen names count too. A second duplicate, a missing or invalid name, or
an unusable retry reply leaves the placeholder for the player to rename. The
retry marker uses the existing saved cognition queue trigger list, so it
survives pause and restore. Separately, required saved `HasChosenName` records
whether the display name was chosen. `NeedsName` schedules automatic naming;
closing that opportunity preserves a placeholder's unchosen status. Neither
open nor closed placeholders reserve first names, and recent-event compaction
cannot turn one into a chosen name. The name check
is separate from action admission: a valid name from a current legal-choice,
low-confidence or rejected-action reply is kept, while malformed replies and
stale replies cannot name the agent.

Player renames reuse `InhabitantNameRules`, including NFC normalization,
collapsed Unicode whitespace and `OrdinalIgnoreCase` comparison. The runtime
checks the first names of all other inhabitants with `HasChosenName`, living
or deceased, under the same world gate that commits the rename. Keeping one's
own first name is allowed. A taken first name returns `name_taken` from
the signed owner endpoint; the client translates only that refusal into a
name-specific explanation. The Profile's open name field keeps unsubmitted
edits through ordinary refreshes, regardless of keyboard focus. Submitted and
refused attempts also stay in the field (`RefusedAgentRename`). Closing the
editor, renaming successfully, or showing another agent or world drops the
attempt; its name labels always follow the host's snapshot. An unchanged chosen
name is a no-op; deliberately choosing the exact displayed placeholder makes
it a chosen name and reserves its first token. No name check rewrites saved
dialogue or identity references, and player choices still supersede late model
naming replies.

Native births omit a chosen name and retain a placeholder with `NeedsName`.
Infants remain excluded from personal-model dispatch. Once ordinary eligibility
allows a naming request, bounded `self.allowed_child_surnames` lists only the
chosen biological parents' surnames; an empty list means none is available.
Player renames and explicit `CommitBirth` names use the same admission rule
for a child: the final name token must match one biological parent's surname.
An explicit birth name is checked before food or child records change. The
rule includes deceased biological parents and does not substitute caregivers.
An invalid surname follows the existing unusable-name outcome; only a taken
first name earns the extra paid retry. Parent renames do not retroactively
invalidate a child's name or saved state.

The response must select a legal candidate. Any finite confidence from 0 to 1
is accepted; confidence does not veto the choice or a valid chosen name.
The personal prompt asks for no probability map. Both parsers accept its
absence; a supplied valid map is retained in the existing response format,
but does not select the action. A current reply that names a candidate never offered to it, or has
invalid response values such as confidence outside 0–1, also completes with
safe idle instead of repeatedly spending calls on the same decision. A choice
that was offered but is no longer legal stays stale; request, provider and run
identity checks still reject late replies without applying fallback.
A reply for a different request cannot cancel the agent's current pending choice
or replace its last accepted intention.
No adapter can turn provider prose directly into a world mutation.
Jev has a separate, smaller routine payload; it is not a persona/dialogue adapter.

Each queued decision makes one hosted attempt. Malformed replies, timeouts and
transport failures do not trigger a repair call or an unstructured alternate
request. Every billable attempt is reserved in the durable usage store before
HTTP; an absent key or exhausted limit sends no request. Future world decisions
still follow the normal reevaluation rules. The separate, explicitly agreed
duplicate-name request in [#456](https://github.com/compoodment/ClankerWorld/issues/456)
is not a repair of an action reply.

The owner agent card reports a bounded model status: ready, waiting, canceled,
missing key, limit reached, unusable reply, timed out or unavailable. The latest
attempt and last accepted model choice are saved separately, so safe-idle
fallback does not erase the previous accepted choice. Pause/disconnect records
cancellation; refreshed views and current-format reload preserve it. Exception
messages, provider bodies and keys do not enter these fields.

The personal adapter sends JSON-object response mode and omits temperature and
output-token limits. The owner's 16-call sample in
[#457](https://github.com/compoodment/ClankerWorld/issues/457) used 44,285 output
tokens (about 2,768 per call), nearly its 43,576 input tokens. This does not
establish a safe universal output cap, so the cap stays unset; output billing
and truncation remain model-dependent. There is no smaller candidate list or
wider Jev role in this change.

Each agent retains up to eight private thoughts and exposes up to sixteen recent
non-forgotten social memories to owner inspection, including deceased profiles.
A separate belief record retains firsthand/hearsay/inference evidence and
superseded corrections. Inspection never broadcasts these records or turns
them into public events.

Ordinary conversation is a host-controlled activity, not free-form world
commands. The normal provider router requires an explicit personal planning
assignment for the current speaker and sends only that speaker's identity and
the accepted public history. A proposal and acceptance each use that agent's
saved daily allowance. The host admits at most two pending conversation calls
per world tick and six alternating public turns per conversation, followed by
one wrap-up. Responses must match the saved conversation revision, run epoch,
request and speaker; stale or malformed replies are not admitted. Public turns
carry a bounded list of agents who were actually close enough to hear. A
separate memory extraction gives those listeners hearsay claims tied to the
turn ID, with duplicate and owner checks. Private thoughts, provider payloads
and unaccepted replies never enter that history. Prose has no world effect; the
only ordinary-dialogue effect is mutual trust, offered during wrap-up and
applied only after both participants accept the same proposal.

The saved revision, status and next-speaker fields are the conversation cursor.
The checkpoint keeps the complete admitted history (up to six public turns and
one accepted wrap-up), and each later provider request receives the committed
public history so far. The wrap-up request receives the six public turns; a
pending answer is never included or saved.

Saving by itself keeps an unaccepted invitation pending, with its original
deadline and the inviter's saved daily allowance. It has no current speaker;
only acceptance assigns the first turn. When the 64-session save limit is
reached, the oldest closed session is removed before a new one starts. A
listener's private hearsay claim, its correction links and any memory-compaction
reference remain saved. Only the link from that private claim to the removed
public turn is cleared; its statement and speaker attribution stay private to
its owner, while the old turn text leaves the shared conversation history.

Pausing, disconnect, a provider failure, urgent need, separation or an
unavailable participant interrupts or closes the activity according to its
state. A save contains accepted bounded turns and daily allowances, not a live
provider task. Restore leaves accepted active conversations suspended and clears
any earlier resume choice; no model request starts until both participants choose
to resume again. A pending invitation retains its original deadline and still
requires normal acceptance and the invitee's daily allowance. Both agents receive
the same public wrap-up and proposed effect before accepting. Lessons wait while
either participant is talking. Conversation logs record only purpose, bounded
turn count, latency and usage totals, including reported usage for rejected speech.

Personal requests retrieve at most four relevant own-memory/belief excerpts and
sixteen recent own-map facts. An existing Jev routine call may score up to twelve
previously unassessed records; only linked salience/confidence values are saved.
It adds no separate paid request or generated prose. Local retrieval works with
Jev off. Automatic experience capture and narrative summarization are unfinished.

Childhood talk, play and learning record owner-private experiences whose keys
fit the same 128-character memory boundary used by recall and Jev scoring.
When the complete actor/target/time key is longer, a SHA-256 key retains the
action prefix used by the social cooldown. The saved owner and subject keep
their actual identities. Short keys and existing saved records stay unchanged;
records with oversized or malformed keys remain excluded from recall.

The three-person candidate limit counts reachable people who contribute an
available talk, play or learning action. A person whose applicable actions are
all on cooldown consumes no slot; distance and stable identity still order the
eligible targets. The 120-tick per-action cooldown and age restrictions apply
both when offering and when executing an action.

Exploration records personal knowledge; it does not create free inventory.
An actual observation of a previously known tile refreshes that agent's terrain
and resource account when it changes. Planting also records the planter's own
work. Unchanged observations do not add another fact or learned event, and
changes do not teach distant agents. The current ledger keeps one fact per
agent and tile, within its existing 128-site limit.

Written artifacts and unfinished writing keep the exact facts captured when
writing started. Earlier observations are retained only while those snapshots
need them, bounded to 81 versions per agent (eight nine-site artifacts and one
nine-site writing project). Copies preserve the held source's account even if
the reader observes a change before or during copying. The reader must already
know every source site; copying does not replace their current observations.
Cancelled writing prunes versions with no
remaining snapshot. Earlier versions validate provenance but are not offered
as the agent's current map knowledge.

Households make paper at an authorized House from physically delivered fiber
and fresh water in a reusable jug. The provisional batch uses two fiber and
one water to make two paper in sixteen work ticks, leaving the jug intact.

An adult can write a one-site field record, draw a map or bind a book from
facts they have actually learned. The provisional writing costs are one paper
and four work ticks for a record, one paper and six work ticks for a map, or
two paper, one cloth and twelve work ticks for a book. Each artifact holds at
most nine sites. Writing reserves real personal materials and creates one
distinct physical lot only when the work completes. Saved progress and
material reservations survive temporary interruptions and reload. If the
writer or copying source becomes unavailable, or the artifact limit is
reached, the unfinished work releases its unused supplies. The agent's Profile
shows the writing or copying progress.

Reading or sharing a held artifact teaches only its recorded sites to the
actual recipient, retaining the original discoverer and the source artifact.
Copying needs the source, learned facts and new writing materials; sharing
does not create another physical copy. Barter transfers the existing lot and
teaches its recipient, without granting access to unrelated knowledge or
anyone else's private stock.

Outward scouting checks occupied destinations and both diagonal corner tiles
before ranking neighboring exits. If no legal outward exit remains, the scout
uses the existing return path instead of repeatedly targeting a blocked corner.
Only completed movement adds a visited tile.

If intervening legal movement interrupts outward scouting, a new outward path
starts at the actual position without inventing missing steps. On the return
leg, recorded visited tiles are waypoints: the actor routes from its current
position around occupants without restarting the outing. Completion requires
reaching the original start, including after reload. A return that remains
blocked still ends through the existing bounded wait/abort behavior. Visited
facts remain personal knowledge.

The owner's model picker shows the game's own list for each provider,
`ProviderModelCatalog.Curated`: newest generation first and, within a generation,
larger models first. Add new models there in their place. When a key is known,
the host checks it against the provider's model-list route (OpenAI
`/v1/models`; Ollama Cloud `/api/tags`, then `/v1/models`) using
the saved key, a named key slot, or a key pasted for that check only, which is
not stored. Keys never return to the client. Listed models the key's route
doesn't include are marked unavailable; names are compared without Ollama's
`:cloud`, `-cloud` or `:latest` endings. If the key can't be checked, the whole
list stays usable and the reason is shown. A new agent starts on the provider's
default model when the key can use it. If the key can't use a new agent's
starting model, the picker selects nothing and asks the owner to choose, so no
other model, possibly a costlier one, is chosen for them. An existing or
hand-picked model the key can't use stays shown, greyed, with the same request.
Starting a model lookup resets the typed display of an automatic new-agent
choice, including after a provider switch. The new list then checks that
default's availability. Choosing or typing a model marks it as the owner's
choice, so a later list refresh preserves it.
Checks are cached per key for ten minutes, time out after eight seconds and are
not model calls, so they do not count toward the usage cap below.

**Test model** is a separate owner action in Add Agent and an agent's Model
panel. It sends one request through the same OpenAI-compatible personal
decision adapter used in play, including the required JSON response format.
It sends no temperature or output-token limit. The host durably reserves a
paid-call allowance before sending; every result after that point, including a
timeout or rejected format, counts as one attempt. The check never tries an
alternate request format, changes provider settings, or saves a pasted key.
The response contains only a bounded result such as ready, missing key, usage
limit, unsupported format, timed out, unavailable, or unusable reply. It does
not include provider error text or response bodies.

An installation-local usage file reserves every hosted attempt before HTTP work.
Concurrent requests share its optional lifetime attempt cap. Failure, retry and
abandonment keep their spent allowance; only known token counts are added.
Deterministic choices consume no attempt. Reaching the cap persists a pause;
changing allowance and resuming are separate owner actions.

The client keeps an edited limit separate from the displayed usage status
until a limit action succeeds or the registration changes. Both Settings
reads and automatic pause reads accept only the latest usage operation for
the current registration. A successful limit action invalidates earlier
reads; changing registration clears the old installation's usage status,
accounting error and limit draft.

The reservation that brings the total to 80% of the cap, rounded up
(`ProviderUsageStore.WarningMark`), raises `WarningReached` once at that
installation-wide crossing. No additional durable warning marker is saved:
totals only grow, one per reservation, and a changed cap sets a new mark that
only later reservations can cross. In private-world mode the host acquires the
world mutation gate, then appends a player-facing `model_call_warning` event
(`used:<count>:limit:<cap>`) to the world active at that time. A concurrent world
switch can change which world receives it.

A successful save keeps the warning event with that world. A failed save leaves
the event in memory for the next successful world save and logs
`event_log_unsaved`. A restart or loading an older save restores its checkpoint,
which may predate the warning; the installation counter never rewinds and does
not reissue past crossings. The count and cap stay in the usage file, and no
checkpoint schema changed. Telemetry logs `provider_usage_warning` with its
outcome.

`ProviderUsageWorldEffects` applies both the cap's pause and the warning, each
under the world mutation gate and followed by a save. Starting a conversation
call reserves before the provider's first await; canceling a provider call can
also invoke accounting callbacks synchronously. Both can happen on the thread
holding the runtime gate. That gate is not reentrant, so the runtime marks
provider startup and cancellation under it
(`IsInvokingProviderUnderGateOnThisThread`). On that thread the pause or warning
moves to another task that waits for the world. Outside the runtime gate it
runs at once, so a hosted decision's late reply cannot be admitted before the
pause. `provider_usage_limit_reached` logs `paused`, `paused_unsaved` or `failed`.

Unreadable or inconsistent accounting leaves the host reachable with paid work
blocked. Preserve the damaged file; changing the cap cannot bypass it. Writes
use unique private temporary files flushed before replacement. Failed reservation
writes do not increment the in-memory total or dispatch a model call. This does
not promise directory-fsync power-loss durability or provider-invoice parity.

## Maps, movement and terrain
Small and Medium generated maps are connected to the normal world path. The
older tiny map remains a compatibility fixture/world; generation is no longer
merely a separate primitive. Large/Huge/Mega generator outputs do not imply
playable storage, observation or performance support.

Generation saves climate zone, elevation, hydrology, surface and vegetation
cover separately beside the stable terrain summary. New World preview and
creation select saved hydrology revision 1: inland lakes retain a connected
low basin of at most 0.5% of map area, with displaced open-water area added
along ocean shores. This is provisional visual tuning, not a new player slider.
Rivers terminate at lakes or oceans; this revision does not invent lake outlets.
The [Small/Medium comparison](assets/inland-water-comparison.png) shows seed
`inland-water-0` at 45% water with wrapping (historical left, revised right).
It is a generator-layer rendering, not a Godot screenshot or native playtest.
Revision 0 (including absent revision fields) retains the historical generator;
unsupported revisions are rejected rather than substituted. Individual trees are
objects/resources. Old generated worlds recover missing layers from saved
deterministic options and retain their original resource layout.

Movement reads water and elevation; build eligibility also reads surface.
Diagonal foot steps cost 141% of cardinal entry and require both shoulder tiles
to be passable, including clear occupancy during a move. A river is waded in a
straight cardinal line from one dry bank to the opposite one, across one or two
river tiles: never along the river and never diagonally into, through or out of
the water. A river tile costs what its narrowest crossing costs: 200, half
dry-ground speed, where one tile of water separates dry banks, and
`SeededMap.TwoTileWadingFootCost` (300, a third of dry-ground speed) where it
takes two. The two-tile speed is provisional. A third water tile in the line,
or any lake or ocean tile, means there is no crossing there: wider rivers,
lakes and the sea need boats. A two-tile line through a tile that also lies on
a two-tile line across the other axis does not count either: that tile is a
corner of a river one tile thick that runs diagonally, and wading it would turn
inside the water and walk along the channel. Where a one-tile spur or a river's
head meets a two-tile line, an agent can still turn once inside the water; no
route crosses more than two water tiles, and such a turn records no bridge
evidence. A built bridge makes its river tiles walkable at
dry-ground speed, end to end along the bridge only (see
[Roads and bridges](#roads-and-bridges)). Mountains are slower to cross and
cannot be built on; peaks are impassable.

Resources are placed in bounded 16×16 cells with climate and cover biases, then
recorded in their actual 64×64 chunks. Sparse/Normal/Abundant provisionally
attempt alternating cells, one site per cell or two sites per cell. Food choices
use the actor's foot-accessible terrain component and skip sites whose current
occupied routes cannot reach harvesting range. Adjacent food needs no route
search. Candidate generation searches only when gathering or a food instruction
needs a source, and stops at the first reachable site. Caregivers use the same
selection. When fetching food for an infant, caregivers also skip household
food stores whose collection point has no currently unoccupied route. They try
other household food before gathering wild food; inaccessible stock is left
untouched. This does not change household ownership or food-policy rules.
Immutable map connectivity is cached once per map; movement rechecks
occupancy before each step. An agent gathering for its own project, and the check
that a project's inputs exist, still need a route from the original camp. Heating,
helping another agent's project and Blacksmith ore use the actor's current
reachable area (see [Material gathering](#material-gathering)). Boat transport uses physical Town assets and typed Council permission, as
described in [Ports and communal boats](#ports-and-communal-boats). Trees and planting are described in
[Trees and planting](#trees-and-planting).

Godot draws camera-visible tiles from a compact terrain index and samples it
for the overview. It does not create a Control per tile. Generated terrain uses
row-major packed bytes, with separate layer digests. Signed cache claims omit
unchanged map data only when world and digests match. Initial/changed maps and
some control receipts still send the whole map. Viewport/chunk transfer remains
unfinished.

Main-map dragging requires a held middle mouse button. Opening the pause menu
or losing application focus clears the drag. The client observes releases
before GUI controls consume them and stops on motion without the middle-button
mask, so a missed release cannot make ordinary hover pan the camera.

The regional-weather prototype saves an episode for each 32×32 region. Ordinary
episodes last one-quarter to one saved day; storms last at most three-quarters
of a day and allow no new storm for at least half a day afterward. Small fixture
calendars round durations to whole ticks. Conditions use the region-center
climate, previous condition, seed and a shared pre-transition neighbor snapshot.
No model call or camera state participates; drifting visuals are not fronts.

The October 1 default preset reduces rain, storm and snow weights by exactly
one quarter after climate and latitude adjustments. The removed share goes to
clear and cloudy weather. Integer weights use four units per old weight point,
so small weights keep the same exact reduction. Explicit custom weather
profiles retain their declared weights. Episode neighbor and persistence
bonuses use the same weight scale. Custom profile totals may reach
`int.MaxValue`; climate conversion uses wider intermediate arithmetic, and
episode weights remain wide through persistence bonuses and storm cooldown.
These adjustments cannot wrap into negative weights. Further tuning remains provisional in
[#204](https://github.com/compoodment/ClankerWorld/issues/204).
Wet neighbors add at most four rain-weight points;
a reduction to base precipitation weights offsets that bonus. The fixed-seed
comparison is recorded in [the prototype report](weather-episode-prototype.md).
Local survival and crop exposure use the active episode. Soil moisture retains
the older three-day daily-weather estimate in this first experiment; it is not
a saved moisture grid or an episode-integrated rainfall model. Existing saves
keep their active weather when loaded; the first resumed tick imports it into
an episode. See [save handling](saves-and-replay.md#regional-weather-episodes).

The generator vendors [FastNoiseLite](../../src/ClankerWorld.Simulation/ThirdParty/FastNoiseLite/README.md).
Its drainage approach draws on [Red Blob's noise guide](https://www.redblobgames.com/maps/terrain-from-noise/),
[Mapgen2](https://github.com/redblobgames/mapgen2) and
[Mapgen4](https://github.com/redblobgames/mapgen4/blob/master/map.ts). These sources
explain the approach; they do not settle open terrain design or performance.

### Terrain layers, sand, groves and hills

`GeneratedCampMapGenerator` builds a map in four separate, deterministic passes,
so each can be checked on its own
([#461](https://github.com/compoodment/ClankerWorld/issues/461)):

1. **Elevation and water** come from the geography generator.
2. **Surface.** Sand comes from two explicit rules, never from simply touching
   water: *desert* (dry climate, rainfall 42 or less) and *beach* (land below
   elevation 175, beside the ocean, where a seeded beach-noise field reaches
   0.3). Rivers and lakes keep grass banks except where a desert reaches them. *Forest floor* marks groves: forest
   tiles whose seeded grove noise reaches 0.15, keeping only the 24 strongest in
   each 64×64 chunk and none in the 6×5 starting clearing.
3. **Vegetation eligibility.** Cover follows climate and the surface beneath
   it, so a beach carries no forest or grass cover. Dry scrub and cactus cover
   on desert sand follow the dry climate; cacti additionally need hot desert
   sand, never dry scrub, ordinary beaches, water or rock. The client draws
   cactus cover as desert brush and puts a cactus sprite on about one cover
   tile in five, chosen from the tile's position, skipping Roads, bridges,
   doorsteps, fields and buildings (`WorldTerrainLayer.CactusAt`). The cacti
   are decoration only; the server does not track them.
4. **Object placement.** The starter berry patch, tree and grain seed patch must
   stand off sand. Every forest-floor tile then gets a tree. Wild sites come
   next, then rare deposits, scattered trees and orchards. A tree or plant
   picks another tile rather than stand on sand, water or mountain rock, and
   map acceptance refuses one that does. Stone outcrops and clay banks are not
   plants and may stand on sand. Eligible forest-grass tiles have a provisional
   35% tree chance; meadows have a 2% chance. Seeded ranking spreads trees
   through any chunk that reaches its budget instead of filling one edge.

Generated sites use at most 1,016 of a chunk's 1,024 resource slots; the validated
configuration ceiling is 2,048. The remaining eight slots stay
free for sites the running world adds, such as the three settlement sites
beside the first Town. Grove surfaces still use the 24-tile limit; the larger
budget supplies the denser trees on forest grass.

**Hills** are dry land below mountain height (215), at least 190 high and within
the hill reach of a connected mountain region (counting diagonal steps as one).
The reach widens with the region's size: one tile for every 7 in the square
root of its tile count, from 3 tiles up to 8, so a 1,000-tile massif has a
4-tile band and a 2,500-tile massif a 7-tile band. Regions join diagonal
neighbours and wrap east/west only on a wrapped map. Hills are a visual layer
only: nothing is saved for them, they keep their own surface, and they cost the
same to walk and build on as grass. `TerrainPlacementRules.ClassifyHills`
(used by `SeededMap.IsHillAt`) and the Godot client's `UI/Map/HillBand.cs`
(used by `WorldTerrainMap.IsHillAt`) apply the same rule to the saved elevation
and water layers; `MountainMassifTests` checks that they mark exactly the same
tiles on generated maps. The client draws mountains, peaks and hills as one
relief layer from the saved elevation (`UI/Map/ReliefRenderer.cs`): it renders
16×16-tile chunks on worker threads, caches one texture per chunk and atlas
size, and shows the per-tile mountain and hill art for a chunk until its relief
is ready. It also warms hills' overview color and shows "Landform: Hills" in
tile inspection. Hill travel cost and passability are not decided.

All of these numbers are **provisional**. They were chosen from fixed-seed
measurements, not owner-reviewed maps, and live in `TerrainPlacementRules`.
Current fixed-world tests require 25–45% forest-grass tree coverage, and every
forest-floor tile holds a tree. Across 160 default Balanced Small and Medium
worlds (wrapped and bounded), hills cover about 3–8% of dry land.

### Mountain massifs

Mountains form as **a few large massifs** instead of scattered patches
([#683](https://github.com/compoodment/ClankerWorld/issues/683), following the
owner's choice on [#628](https://github.com/compoodment/ClankerWorld/issues/628)).
`MountainMassifs` shapes them after water is classified and before rivers are
routed:

1. **Plan.** Each world picks its massif count from its size: 1–2 on Small and
   2–4 on Medium (3–6, 5–10 and 8–16 on the unplayable larger sizes). It aims
   the massifs at a seeded share of dry land: 7.5–9.5% for Normal mountain
   relief, 2.5–3.5% for Low and 14–17% for High. Each massif gets a seeded
   share of that area, but never less than twice the 150-tile minimum.
2. **Place.** Each massif has a centre, a long axis 2.4–3.6 times its width and
   a slight bend along its length. Placement tries 32 centres, each the most
   inland of three random land tiles, and keeps the one whose outline holds the
   most dry land, at least 60%. Massifs stay at least three tiles plus their
   rough edge apart, so two never merge. A massif grows to make up for water
   inside its outline. Where the land is too broken up for the planned size, a
   half-size massif is tried before the massif is dropped.
3. **Shape.** Inside its roughened outline a massif rises as a stretched dome
   from 215 at the edge. Its **crest**, a thin band along the central 60% of
   its length, holds the peaks (245 and up). A peak stays only if its own
   massif surrounds it on all eight sides, so peaks never touch water, lowland
   or the map's edge.
4. **Flatten.** Each massif keeps only its largest connected piece; a piece cut
   off by a coast or lake is lowered back to the ground around it. A massif
   below 150 tiles is flattened completely. Elevation noise outside the massifs
   that reaches 205 is squeezed into 205–214, just below mountain height, so it
   never forms a stray mountain but can still hold stone.
5. **Foothills.** Dry land around each massif rises to at least 190 out to its
   hill reach, then eases back down over two more tiles, so the hill band
   always shows and widens with the massif.

Rivers never form on mountain tiles: they rise at the foot of a massif rather
than cutting it apart. After rivers, a final pass checks that peaks never split
any land: within each piece of dry land, the ground that is not a peak must be
one walkable piece. If peaks enclose some ground, the fewest peaks needed to
reconnect it are lowered to mountain height. Temperature and climate are worked
out last, from the finished elevation, so massifs and their foothills are
cooler than the land around them.

The first Town's starting clearing always has a stone outcrop it can walk to
within 32 tiles, counting diagonal steps as one. If none formed there, one is
placed on the highest ground in reach, outside the clearing. This is in
addition to the stone site the running world adds beside the first Town. Iron,
gold and diamond outcrops still need mountain tiles, so they now gather in the
massifs.

Measured over 40 seeds for each of the four default Balanced Small/Medium
settings (50% water, wrapped and bounded), the New World selection always met
both trial targets, compared with 56 misses out of 160 before. Mountains covered
7.2–10.5% of dry land, each world had a massif count within its range, the
smallest massif had 443 tiles and peaks were 10–14% of mountain tiles. The
nearest stone was at most 33 tiles from the starting berry patch. Before this
change the same worlds had 1–65 separate mountain patches, the smallest of a
single tile, and mountains covered 1.4–19% of dry land.

Generation takes about a tenth longer. In four interleaved runs of
`scripts/measure-map-generation.sh` on a shared machine under load, the median
time to generate `probe-a` went from 191 to 211 ms, `probe-b` from 140 to
153 ms and the Dry fixture from 76 to 85 ms (averages of the per-run medians).
These are noisy measurements, not a performance promise.

The October 1 measurements used .NET 10.0.401 and Godot 4.7.2 under WSL, with
two .NET processors. Both Small maps are 256×128, use current hydrology and
the Balanced visibility rules, and were measured before and after this change:

| Seed | Resources before → after | Most resources in a chunk before → after | Forest-grass tiles with trees before → after | Ocean shore with sand before → after | Median generation ms before → after |
| --- | --- | --- | --- | --- | --- |
| `probe-a` | 391 → 1,748 | 56 → 420 | 76/3,913 (1.9%) → 1,377/3,949 (34.9%) | 195/851 (22.9%) → 73/851 (8.6%) | 119.5 → 207.8 |
| `probe-b` | 364 → 1,969 | 56 → 558 | 87/4,500 (1.9%) → 1,636/4,586 (35.7%) | 246/1,226 (20.1%) → 90/1,226 (7.3%) | 146.5 → 116.5 |

Generation used one warmup and five timed runs, reporting the median. Times
include shared-machine scheduling noise; the faster second map does not prove
a generation speedup. The separate uniform-Dry fixture had 39 cactus tiles,
all on desert sand, compared with 48 before, nine outside desert sand.

Godot measurements execute the actual terrain layer's `_Draw` and time CPU
draw-command submission. Each camera uses five warmups and 20 measured draws:

| Seed | Full overview median ms before → after | Detail median ms before → after |
| --- | --- | --- |
| `probe-a` | 3.61 → 6.26 | 6.73 → 6.07 |
| `probe-b` | 3.14 → 6.65 | 8.56 → 8.51 |

The overview covers all 32,768 tiles at size 2; detail covers 96×48 tiles at
size 16. These headless measurements exclude GPU work and frame presentation;
they do not establish Windows frame rates or a hands-on playtest. Denser
resources increased overview CPU cost in this sample.

To repeat generation measurements and write public map fixtures:

```bash
bash scripts/measure-map-generation.sh /tmp/clankerworld-map-measurements current
godot --headless --path src/ClankerWorld.GodotClient -- --measure-map-draw /tmp/clankerworld-map-measurements/current-probe-a.json
```

Use the pinned engine and build the client scripts first. The draw probe exits
with an error if the engine never calls `_Draw`. It does not start a host or
use private saves. Across a separate four-seed, 160-day episode sample, wet
time fell to 78.1% of the previous Tropical level, 70.9% Dry, 75.4% Temperate,
73.3% Cold and 75.7% Polar. Episode durations and neighboring weather explain
why time spent wet is not exactly the same as the base-weight reduction.
`TerrainPlacementTests` reports terrain coverage for shoreline, inland-river,
wrap-seam and mountain-edge cases. The script and Godot probe above report
generation and draw timings.

The [terrain comparison](assets/terrain-placement-comparison.png) shows a
90×50-tile area of seed `river-world-a` (Small, 50% water, wrapped): the
previous generator on the left, these rules on the right. Dark green is forest
grass, the darker patches with dots are groves, beige is sand, brown is dry
scrub, grey is mountain and the warm band around it is hills. Dark dots are
trees, red dots plants and grey dots stone. It is a generator-layer rendering, not a Godot
screenshot or native playtest.

## Building production inspection

The owner snapshot derives `AvailableRecipes` from active content matched to
its placed building's exact definition, including workstation-size variants.
Crop recipes and the retired generic-food and bedding transformations are
excluded from the workstation list. Each entry carries its registered name
and input/output quantities, rather than a client-maintained recipe table.

Production jobs project the same recipe facts and `HeldInputs` grouped from
the job's own active inventory reservations. Completed or released
reservations contribute nothing; building storage remains a separate view.
Godot Details refreshes these facts even when work progress stays unchanged.
Missing fields from an older host leave recipe facts unavailable rather than
fabricating them. This projection changes neither world state nor agent
knowledge and needs no save-schema change.

## Towns, building sites and death

Paused founder setup creates one First Town when the owner accepts its five-building
site. Founders become residents; Start World changes founding state to founded.
Later placement inside the saved border establishes residence; walking does
not change it. A newborn joins its primary caregiver's Town; death removes the
resident. Joining another Town later needs that Town's council (below). Owned-building placement establishes household membership.

Founders and added adults arrive at a seeded age from day 15 to day 25 in
day-lifecycle worlds. `SocietyFixture.FounderArrivalAge` orders that range once
per world seed and gives the founder in each placement position its day, so
the four founders never share a day, the same seed repeats their ages, and a
founder placed after an undo gets the undone position's age. `AddAdult` draws
an added adult's day from the world seed and the number of inhabitants already
recorded, living or dead, not from the client's agent ID. The saved birth tick
holds the result, so loading never draws again. Year-based development worlds
keep the 18-year adult age.

Each `TownRuntimeState` carries its own `TownGovernanceState`. `TownGovernanceRules`
implements all-adult and representative councils from recorded living adult
residents, independently of geometry and household affiliation. A separate
`SettlementCouncil` remains the household-food steward prototype.

Household food-policy ballots last 120 ticks and can pass through their saved
`ExpiryTick`, inclusive, with a strict majority of the remaining eligible
electorate. After that tick, resolution closes the ballot without changing the
food policy, even if a death reduces the number of approvals needed. Resolution
records its tick and adopted or rejected event, then clears the pending ballot;
save/load preserves the original deadline and votes.

The civic engine keeps final proposal votes, continuing candidate agreements,
opening voter/candidate lists, latest election ballots, cutoff runoffs, settled
seats, fair draw order, ten-day terms and retry snapshots. A failed election may
retry after one world day, or sooner when adult/candidate availability improves;
withdrawals and departures do not themselves reset that wait. Council revisions
cancel pending proposals without altering settled decisions. Admission requests
merge by subject identity; ordinary text requests merge after case/whitespace
normalization. Roster changes or independently supplied material circumstances
allow earlier proposal reconsideration.

`PrivateWorldRuntime.Governance` advances each Town on the normal tick path and
rechecks authority when applying choices. `civic|...` actions let actors visit
the public notice place, read posted notices, relay them within interaction
range, nominate another resident, register their own consent and choose proposals
or ballots. A nomination posts a notice; only the named agent's personal response
can add agreement. An adult with no Town, or a resident of another Town, may
request their own admission at a Town's notice place. An adult with no Town may
walk there from anywhere, and a resident of another Town from inside its border,
whenever a tile beside the notice place is reachable on foot. Residents may
request admission of an unaffiliated adult nearby. The optional
`civic_proposal`, `civic_ballot` and `civic_land_tiles` structured response fields are carried only
through admitted choices. Missing, stale or malformed responses cannot supply
votes. A generic private thought or another actor naming a candidate supplies
neither agreement nor approval. No polling provider calls are added, and Jev is
optional.

An adult resident may submit a land claim with up to 64 exact tile coordinates
in one connected plot adjoining the Town's recorded title. This is a bounded
provider payload, not permission to rewrite title. Because the model sees no
map grid, the claim choice names the agent's own tile and up to six unclaimed
tiles beside the Town's title, nearest first. An empty `civic_land_tiles` array
is no plot rather than a malformed reply. The server rejects water,
off-map, duplicate, disconnected and already titled tiles. The saved proposal
uses the ordinary Council majority and notice-reading rules; its identity
comes from the canonical coordinates, so reordering them does not open another
ballot. A successful vote rechecks the whole plot, records title and includes
those tiles in the Town border in the same prepared tick. An overlapping claim
that is no longer eligible is cancelled. Household rights, structures and
inventory are unchanged. Ordinary law prose cannot execute a land claim.

Household land requests use the same bounded `civic_land_tiles` field for
already titled land. The choice names the agent's tile and up to six free Town
tiles nearest first: no use right, pending request, building, expansion, road
or field.
Filing refuses tiles the household already holds and other owners' buildings,
running expansions and fields with no recorded right; another household's recorded
right is contested as a dispute.
A nonconflicting request opens an ordinary `land_use`
Council proposal. Its canonical key includes the household, exact tiles and
optional agreed end date. Filing posts a notice but supplies no acceptance.
Each current adult in the household must learn that notice and explicitly
choose `accept_land_use`; Council votes are separate. Grant settlement rechecks
the current adult roster, competing rights/requests and foreign fields. An
older pending request cannot grant another household's worked field merely
because its Council vote and household acceptance have finished. A dispute leaves the
request pending; refusal, withdrawal, an elapsed requested term and a Council
change that cancels the vote close it, and the household may ask again.
A grant preserves its Council proposal, individual consent records and the
adult roster at settlement. Closed requests remain in the save history but do
not contribute competing claims to the map or Add Agent.

Ordinary and ordered field-site choices and authoritative tilling exclude land another
household holds or has requested; unclaimed land and the farmer's own household
land keep their ordinary physical requirements. New Roads and bridge entrances
exclude every household's held or requested tiles. Routing may still join an
existing Road or reuse an existing bridge on claimed land, but it rechecks
both banks before adding a new crossing. These construction checks do not
change walking permissions or remove existing infrastructure.

`request_expansion_land` derives the extra House tiles from a currently legal
larger footprint, so agents can request them before gathering materials.
Expansion start and completion require a Town-assigned building's added tiles
to have that Town's title. A House needs household use rights on any titled
added tile, even if it has no Town assignment; shared Town buildings need title
and land no household holds or has asked for. An expansion asks the Council
once: while any of its shapes has a pending request, no other is offered. New
construction likewise avoids land other households hold or have asked for, and
Town buildings avoid all of it. Unaffiliated construction on untitled land keeps its ordinary physical
rules and creates no title. Losing permission cancels the
job and releases its materials. Existing expired rights are not silently
removed; ordinary Town advancement opens an expiry hearing and keeps the old
permission provisional until a lawful ruling.

`TownRuntimeState.LandHearings.Transfers` records voluntary transfers of exact
existing permission plots. Published right versions, terms and party snapshots
are immutable. Every current active adult or elder in every source and receiving
household needs an actual notice receipt and a separate admitted personal-model
acceptance. Filing and reading supply no acceptance. Added adults must learn and
accept too; consent belongs to the exact household, so moving households cannot
reuse it for a different party. No routine Council or mayor approval is required.
Expiry, changed source rights, disputed claims and open hearings block completion.
A decline or withdrawal closes the request without transferring permission.
Completion stores the actual final party roster and one replayable bounded
adjustment, preserving original grant terms and all outside pieces. Title,
membership, buildings, crops, inventory and private-building access stay intact.
Owner projection distinguishes current pending rosters from completed-transfer
rosters, showing names, clocks, actual awareness and accepted adult counts. All
pending transfers and eight recent closed ones are projected; the full ledger
remains saved.

`TownRuntimeState.LandHearings` saves each public case's exact plot and right
versions, affected parties, formal notice and response deadline. Equivalent
filings join the live case without resetting its clock. A different plot that
overlaps a live case is refused. Land a settled case covered needs reopening
grounds until its permission terms change; an agreed end reached after that
case's last ruling still opens its expiry review. Material right changes
or new affected adults publish a revised notice with a fresh day. Notice
publication, actual civic receipts and case-file reads are separate records;
reads identify the evidence and rehearing requests actually available to that
adult. Physical relays carry only material their source learned. Owner inspection
does not supply agent knowledge or private memories.
Notice revisions retain their published party snapshots. The shared pure
`TownLandCasePartyRules.CurrentParties` derives current adult responders and Town
representation from the same captured rights, request masks, inhabitants, civic
authority and clock used by the runtime. Death, departure and succession update
current standing without rewriting the original notice. Owner views label these
roles separately, and each ruling projects its actual closure party roster.

Hearing actions enter through admitted personal-model civic choices. Public
statements remain allegations; actual nearby observations and inspected rights,
title, law versions and earlier rulings retain their sources. The current land
mayor must inspect the file and revalidate authority, conflicts and the response
window before ruling. A permission past its agreed end must be renewed, amended
or ended; it cannot be confirmed or left as it is. A renewal renews every lapsed
permission on the plot for its own household, so several lapsed households need
no chosen winner. Ending is not offered for an unrepresented household. An amend ruling reassigns
existing permissions and the pending requests it heard; other free Town land
stays free. A conflicted holder steps aside for a willing independent
adult elected through the shared civic scheduler for this case only. A valid
acting judge survives ordinary mayor succession; missing authority leaves the
case pending. Reopening needs assessed material evidence or a demonstrated
procedural error, retains earlier rulings and publishes a fresh notice. Grounds
are judged against the latest ruling, and only while the case is settled. A
settled case with no rehearing request offers its file to adults who have not
read its ruling or whose plot's right, title or law has changed since, and offers
its parties nearby observations and a rehearing request; statements and relays
resume while a request is pending. A Council-approved Town filing that
can no longer open when its vote passes keeps the vote and posts the reason.

Bounded permission adjustments retain original grant receipts and replayable
before/after rights. A ruling records the exact covered portions of pending
requests, so resolved tiles stop contributing claims while remaining portions
stay pending. Town title, membership, buildings, crops and inventory are not
transferred by a hearing. Owner projection carries all active cases and pending
rehearing assessments plus eight recent settled cases; complete ledgers remain
in the checkpoint. Town, plot and property views use public names, sources and
world clocks. Lifecycle telemetry carries identifiers, status and counts, with
no statements, model replies or private memories.

`TownRuntimeState.Nonviolent` records non-land allegations, notice revisions,
sources, responses and civil findings separately from permission adjustments.
`TownHearingProcedure` supplies shared response, conflict and case-election
rules. The non-land mandate needs protected resident approval and personal
agreement. An extension names its existing office and retains that office's
original term; its added authority begins only at the actual handover.

Native completed actions capture applicable law wording, title and membership
at the time of conduct. Actual observations and communication chains determine
which agents can report them; the ledger itself supplies no global awareness.
Case-linked records survive event compaction. A passed `law_case` Council
proposal authorizes the exact report, without deciding its truth. Current
authority, conflicts, notice and the freshest case file are checked again when
an admitted personal-model response executes.

`TownRemedyRules` keeps offers, exact terms, each contributor's informed answer,
agreements and completion receipts. Accepted terms create no forced plan or
reservation over another person's goods. Existing physical delivery and repair
paths supply receipts only after their actual changes commit. One receipt can
credit only one term, and the saved effect cannot exceed that term's remaining
quantity. Declined, unanswered, pending, overdue and completed states remain
distinct. Findings and overdue work never create physical punishment powers.

Formal civic acts require an admitted personal-model choice. Built-in decisions,
failed replies and continued intentions supply no votes or candidate agreement.
An explicitly chosen visit can continue moving locally; ballots may be revised
on the agent's ordinary decision cadence.

Long ancestry-based agent IDs use stable SHA-256 aliases in civic model action
tokens. The runtime resolves these against current inhabitants before checking a
ballot; saved candidates and choices retain the actual IDs.

Saved notice receipts enter a bounded `CognitionSelfContext.CivicNote` excerpt
with read/relay provenance and readable names/world days. An actor receives no
unseen civic dump. Owner observations project each council, its latest eight
proposals, the current election and the latest archived election onto the normal
Godot Towns page. Failed and cancelled outcomes remain visible; the complete
authoritative proposal and election history stays in the checkpoint. Long Town
readouts scroll within the available screen height. A passed structured law
proposal records its scoped wording without creating physical or ownership
powers. Bounded civic lifecycle telemetry records Town identity and
council/vote/status counts without proposal text, notices, names or per-read
polling noise.

**Town admission** (`PrivateWorldRuntime.TownMembership`). `TownRuntimeState.ResidentIds`
is the only record of Town membership; household, House and position alone never
change it. After every saved council decision, every Town roster change and on
every tick, `SettleTownAdmissions` reads each Town's passed admission proposals
and records exactly one `TownAdmissionRecord` per proposal, rereading the Towns
after every change:

- A request the newcomer made for themselves is their consent, so it is applied
  at once.
- A request a resident made is saved as `approved`, and the newcomer is offered
  an `accept_admission` civic choice once they have learned the result notice.
  `TownAdmissionDeadlineRules` derives the acceptance deadline from the passed
  proposal's original `SettledTick` plus the world's configured `TicksPerDay`.
  Acceptance is open only before that deadline. At the deadline the approval
  lapses with `acceptance_expired`; paused time does not advance the window.
  Accepting needs the same Town they had when the council voted, and is not
  offered while their own request elsewhere is undecided.
- Applying an admission moves the newcomer and their care group: the living
  infants, children and adolescents they are primary caregiver for who share
  their current Town. The group leaves the old Town and joins the new one in the
  same step, the newcomer's other open approvals lapse as `joined_elsewhere`,
  and both councils' rosters follow their recorded adult residents.
- An approval for someone who has died, already lives there or changed Town
  lapses instead.

An adult has one undecided request of their own at a time. A refused or
withdrawn request for the same newcomer waits one unpaused world day unless the
council changed, and the choice is not offered during that wait. An adult with an
open request or approval may also ask households in that Town to take them in,
but household admission stays a separate decision and grants no Town membership.

`TownMembershipText` describes recorded membership, the rights it gives (council
seat or vote, in-person Warehouse collection) and the admission status in one
bounded line. The agent's `CognitionSelfContext.TownMembershipNote` includes only
pending, approved, lapsed, refused or cancelled admissions the agent learned from
notices. Its shared approval selector excludes expired approvals and keeps the
original notice knowledge gate. A known open approval and its acceptance choice
state the deadline and remaining world time. The owner's agent card reads public records;
its `town-membership` decision factor has an optional derived
`AcceptanceDeadlineTick`, so the client can format the actual world date and
time without depending on the bounded Council proposal display. No deadline is
saved separately or added to an agent's knowledge by publication alone. A later
ordinary request can be approved and accepted again. `town_admission`
telemetry records the Town, outcome, previous Town and counts only.

`TownGovernmentState` stores scoped law versions, protected resident processes,
mayoral consent and contests, and separate land, ordinary and non-land mandate terms.
`TownGovernmentRules` coordinates them with the existing Council engine. Law
adoption consumes passed structured Council proposals once; amendment and repeal
bind their base version, so a stale passed proposal cannot overwrite a later law.
Territorial applicability uses formal title records, including a saved site
subset, rather than the drawn Town border. Law text grants no physical powers.

Residents propose government changes at the notice place, and the choice to
seek a mayoral office appears only while an office exists, a contest is open or
a change that creates one is pending, keeping these choices out of every model
call. Government votes preserve their opening electorate and final votes. Later adults
wait; deaths and membership departures remove voters and ballots. Equivalent
requests share a process; different requests queue with fresh opening lists.
Incumbent Council revisions cannot cancel this ledger. Approved transitions
retain incumbent authority until all required successors are ready, with a
three-day deadline. Ordinary handovers require mayoral successors only for
mandates added by the target arrangement; explicit replacement requires every
targeted mandate. A retained vacancy follows its existing succession rules.
Explicit all-adult government disables automatic representation, and explicit
elected government seeks three representatives above three adults. The initial
arrangement retains the eight-adult threshold.
Each transition records the exact Council election it caused to open, including
an attempt that fails immediately. Only that election waits for the transition
or is cancelled when it lapses. An ordinary election keeps its own result and
retry policy when population changes during an unrelated handover.

Mayoral contests bind consent to exact mandates and ballots to a contest/round
opening token. A new contest starts only with an eligible willing candidate,
so an empty vacancy adds no daily history or notices. Existing contests retain
their normal failure and retry rules. Every deciding round needs a positive
vote; tied leaders repeat with fresh voters, without a random draw. Saved round
records preserve votes, ties and interruptions. Scheduled Council voting takes
priority; other Council
contests wait while mayoral voting runs. A cancelled transition cannot later
seat its dependent contest. A completed transition cancels any dependent
contest it no longer needs. Separate mandate records preserve a governing
leader when a land mandate ends, and a governing vacancy temporarily restores
all-adult authority without changing the approved succession arrangement.

The normal personal-model path supplies all proposals, consent, withdrawals,
resignations and votes. The runtime revalidates current eligibility, actual
notice knowledge and the exact round before admitting an action. Owner
observations add current mandates, the latest eight government processes,
current/latest mayoral contests and the latest sixteen laws; saved history is
not pruned. Bounded civic telemetry records transition kinds and counts without
law wording, notice text or personal model payloads.

`FirstTownLayoutPlanner` lays the first Town street first, using
`TownStreets`. A main road runs both ways from the chosen site along its most
open line, bending in 45° steps, never more than 45° from the heading it set out
on. Side streets leave it every three to five tiles, mostly at right angles. A
street takes a diagonal step only where both corner tiles are clear ground, and
keeps a one-tile gap from other streets. The five buildings then take the
nearest lots whose door opens onto a street, with a one-tile gap between
buildings. Finally every dead end is cut back to three tiles past the last door
on it, and branches with no door are removed. The same site on the same map
always gives the same layout.

The Town border is all land within about three tiles of the Town's buildings
and Roads (three straight out, rounded at the corners). It leaves out water and
is clipped at map edges. It grows around each building that joins the Town and
around that building's new Road. The saved border is authoritative: loading
checks that it lies on the map and covers the Town's origin and every assigned
building. Older worlds keep their saved rectangular border. There is no
wrapped-seam claim geometry, competing-claim graph or automatic second-Town
founding. Filters display saved Town and household-building facts, not invented
general land ownership.

A building that joins a Town later is joined to the Road network from one of the
tiles directly beside its footprint. That starting tile is saved as the
building's entrance, and the map draws the door on that side. The first Town's
planner saves the entrance of each lot it chose. A building without a Road has
no entrance and shows its door in the middle of its south side.

Towns grow along their streets. `TownLayoutService` gives a site whose door can
face an existing Road a `road_frontage` bonus. A building beside a Road needs
no new Road. Otherwise a new side street runs to the nearest Road. It stays
inside the (already grown) border where it can, may step diagonally where both
corner tiles are clear, and pays extra for each tile beside an existing Road,
so it meets streets rather than running alongside them. Then every dead end
fewer than three tiles past its nearest door carries on in its own direction
where the land allows (`town_road_extended`). The border grows around all the
new Road tiles.

Street neighbours, headings, diagonal corners and clearance use the map's
east-west wrap. A Road across the seam counts toward the distance to the last
door, and a short dead end can carry on through that seam in its original
direction. Rows do not wrap, building footprints still stay within map bounds,
and bridge headings follow their saved axis and span.

`TownLayoutService` captures one immutable layout context per decision and
normally offers at most five legal sites with reasons for footprint, route,
resources, purpose and compact growth. A model selects a site-specific candidate
or chooses another action; refusal starts no project. Accepted projects retain
their tile. If it becomes illegal, the project blocks and retries after sixty
ticks. An unchanged idle choice is reconsidered after 300 ticks, sooner if
urgent needs or legal choices change. Weights and retry values are provisional.
Material scoring searches at most five map tiles from a site, including the
east-west seam; farther resources cannot change its rank. Recipe and expansion
input checks share reachable-tool results only within one inhabitant's
read-only candidate query. Later queries and actions check current stock and
routes again; neither optimization adds saved state or a persistent cache.
Purpose scoring filters related buildings once per context and keeps its
distance, building-ID and tag tie rules. Border-growth scoring counts the same
rounded footprint margin that actual placement adds, without sorting those
tiles for every candidate.
Continuing an idle intention skips a second full candidate query after the
ordinary enqueue phase has reconsidered current choices. Orders, conversations,
care and ongoing work keep their earlier continuation guards, and the ordinary
idle action still runs its cleanup.
See [construction query measurements](construction-query-measurements.md) for
matched native timings, candidate/state equivalence and remaining limits.
Town proposal site checks also share physical placement obstacles only within
their synchronous query. Market slots stay separate from other occupied
objects, and actual placement gathers current facts again. Layout searches
rent tentative tile costs while preserving their settled tiles, costs and
stopping rules. See [placement query measurements](placement-query-measurements.md)
for the follow-up comparison.
Building plans follow what a household needs, not a role. An adult whose
household lacks a House, Farmhouse, Blacksmith, Silo, Tailor Shop, Clinic or Restaurant is offered ranked sites
for it once the household has the build costs in hand: stock the household
owns anywhere, plus what its members carry. Each kind is planned at most once
at a time and a household never holds two of a kind; a second member choosing
the same kind in the same tick is refused, and a project stops if its household
comes to hold that kind. Only a household holding a Farmhouse plans a Silo, and
its sites must lie within two tiles of that Farmhouse, counting diagonals, with
touching sites ranked first. This provisional reading of "next to" keeps a Silo
possible when Roads, resources or later buildings take the tiles beside it. While the first building it still needs lacks a material, one
adult at a time is offered to gather it from a reachable source. Buildings the
Town shares, including a new Warehouse, are never offered to a household. The
kinds are listed in `HouseholdBuildingKinds`. The optional Store uses the same
household planning and ownership rules, with 1×1 and 1×2 footprints.
Harvests remain household-owned lots on their actual field tile. An adult
carries a load of at most four raw crops or planting items to the household's Farmhouse or Silo.
Each holds a provisional 96 items, counting deliveries already on their way;
pickup and delivery both check remaining space. Source selection checks the
adult's route to each pile or vessel and the route from there to farm storage;
an earlier blocked source does not hide later reachable stock. The same checks
run again when the hauling action executes. Grain prefers the Farmhouse,
while other farm stock prefers the Silo. Selection tries the next permitted
store if the preferred one cannot accept the actual load or its route is blocked.
A filled vessel must fit with its contents; only grain and flour retain their
approved partial withdrawals. An explicit destination prevents fallback to
another store. Ready-to-eat greens and fruit go to
the household's House. Neither stock nor ownership moves
remotely.

Ordinary milling can draw needed household grain from its Silo into its
Farmhouse. Available and inbound Farmhouse grain reduce the pickup; recipe
demand, free storage, reservations, carrying room and both walking routes
still apply. Contained grain uses the existing partial pot-withdrawal rules.

Food selection tests the entire harvest load, including orchard seeds, so a
nearer oversized harvest does not hide a reachable one that fits. Making
room uses the permitted household serving or actual harvest size. Accepted
infant caregivers can collect household food for their dependent without
needing to be hungry themselves; current care authority, usable food,
reservations, capacity and real travel are checked again at execution.

House pickup counts the whole vessel family against carrying and destination
space; loose deliveries keep their four-unit limit. An unusable carried
delivery leaves ordinary hauling and can offer `recover_household_delivery`.
Candidate and action both check the current household-owned destination,
physical load, selected or repaired gear, active family reservations and a
route to camp. Recovery moves the same stock through the inventory authority,
clears its delivery pointers and records `household_delivery_recovered`.
Spoiled contents may be physically withdrawn from an owned usable pot, with
the existing family, quantity, destination and reservation guards. They
remain unusable for eating, recipes and new reservations. No agent disposal
action or player discard control is added.

**Household departure and personal custody** (`SettlementDeparture`). Ordinary decision candidates allow an adult to leave without a vote, store or collect their own goods, return borrowed household tools, explicitly accept replacement care, and found a solo household only when no suitable existing home can currently be asked. Membership exits and admissions include the complete primary-care group. The same completed House-capacity calculation checks all incoming residents; children never apply alone. Forced displacement refuses sole caregivers, including a guardian whose dependent lives in another household. Voluntary departures keep the existing care-group rules.

`InventoryLot.OwnerId` records property; optional `CarrierId` records physical custody without donation. Personal goods may remain in House storage after departure. `InventoryFixture.Relocate` preserves ownership, condition, provenance and reservations while moving an unreserved quantity. A stored personal lot is collected physically at the House's entrance, with carrying limits, under current household membership, a recorded departure's limited collection right, or a settled will's bequest to the actor. A child who moved in a caregiver's care group uses that recorded departure too once old enough to collect, including after the caregiver dies. The lookup reads living and archived departure records; it creates no child departure or additional food allowance. The bequest must match the lot's ID or provenance, kind and frozen storage location. This lets an heir retrieve inherited goods from another household without granting membership, House entry or access to shared stock. Recovery from the ground or a former household remains a routine errand. Collecting a map or field record from the current home stays available as a deliberate choice, but ranks below idle for the built-in chooser so it does not immediately retrieve knowledge goods it has just stored. Other goods retain their normal collection priority, so the built-in chooser stores only maps and field records; storing other belongings is a deliberate choice, because routine collection would fetch them straight back. Borrowed tools retain the lender's owner ID while carried and are returned physically. Shared delivery loads retain their owning household on departure. Shared buildings, stock and job records are never reassigned to the new household. A departing worker's private production and expansion jobs pause with their existing owners and reservations, except work for the worker's own goods, such as building their handcart, which nobody else may finish: it is cancelled and its reserved materials are released and stay with the worker; their previous work plan is retained on the departure record instead of resuming under a new household. A remaining member can take over paused work at its physical site, using the same still-available committed inputs and remaining work time. Private materials held by the former worker are not reassigned; these keep the task blocked. Held reservations keep their exact owner and stock, receive a new deadline only on resumption, and are released if the materials become unusable; canceled job records retain the original property owner.

Each departure allocates at most two unreserved ready-to-eat portions once. Ownership changes at allocation while the existing storage/ground location stays fixed. Saved departure records retain the allocation and collection right; retries with no current membership cannot allocate again. Caregiver IDs and ancestry stay unchanged. Dependents follow the caregiver in physical steps, and a traveling caregiver waits when a dependent falls behind. Housing, ownership, collection and care facts use normal personal-model observations and player inspection; no extra acknowledgement request is made.

Production jobs capture their owner when the original inputs are reserved. Completion uses that saved owner, including at a public workstation when the worker leaves or forms a household during the job; membership changes cannot redirect the finished goods.

**Housing requests** (`SettlementHousing`). An agent's saved `Housing` record
holds its current blocker and, for an adult, any pending request, recent refusals
or move-out notice. Each tick `MaintainHousing` resolves
requests, then recomputes the blocker and appends `housing_blocked` when it
changes. The blocker codes are `no_household`, `no_authorized_home` (the
household can plan or is building a House), `missing_materials`,
`no_legal_site` (the household has the build costs but `TownLayoutService`
ranks no site), `awaiting_answer` and `overcrowded` (the household's House
has more permanent residents than places). The state is explained on the owner's
agent card and sent to the agent's own model as a `housing` line in its self context.
An adult with no household, or with a currently valid move-out notice, is offered
`household_ask:{household}` for each other household that holds a House in the
same Town, has room for the entire moving care group, has an adult who can answer
and has not refused within the last two world days. Asking records that household's
adult members in the request, which expires after 120 ticks like other
proposals. Adults who join the household or reach adulthood while it is pending
must also answer; existing answers are retained and adults who die or leave no
longer need to answer. Each current adult is offered `household_admit:{applicant}` and
`household_refuse:{applicant}` and cannot continue a project or lesson until
they answer. An ongoing lesson waits while either participant owes a housing
answer, retaining its progress and already learned skills.
One refusal by a living member ends the request; when every living
member has agreed, `SocietyFixture.JoinHouseholdCareGroup` records the membership and
`household_joined` is appended. A refusal or an unanswered request is remembered
as a refusal for the cooldown. The request grants nothing while pending: stock,
shelter and route rules still check household membership. A resident with notice
leaves through `DepartHousehold` before joining, preserving the
[departure rules](../game-design/towns.md#household-goods-and-departure) for goods,
food allowance and care. Before that exit, live relocation eligibility and
destination capacity are checked again. A cancelled notice also cancels a
resident's pending request elsewhere; an adult who already left keeps seeking
a home.

**House resident capacity** (`HouseResidentCapacityRules`). A completed House
provides three permanent-resident places per footprint tile, or four per tile
when one explicitly recorded domestic family unit has at least two residents
and a strict majority of the House's residents. The unit is saved separately
from ancestry. A partnership changes these units only when it is accepted or
when an accepted partnership ends. Withdrawing, refusing or expiring an
unaccepted proposal leaves each person's existing unit and the resulting
House limit alone. Traveling residents and infants count, dead people and invited
storm guests do not. Joining a household is offered only when the proposed
resident fits after their arrival is counted. The server checks again after
unanimous admission, and Add Agent checks the selected household property
before placement. A birth always goes to the primary caregiver's current
household, even when that puts the House over its limit; the building card,
agent context and the newborn's saved housing status show the resulting need.
An unavailable House is recorded the same way without delaying an agreed birth.
The birth still needs food and an unoccupied, buildable tile for the newborn:
near an accessible shelter, or near the primary caregiver when there is none.
Losing a House does not bypass the food or consent checks. This status gives
dependents no adult admission or construction choices. House expansion can
start for a
storage need or when there is no resident place, but added places use only the
completed footprint. Unfinished expansion does not reserve room for another
resident.

**Overcrowding relocation** (`HouseRelocationRules`, `SettlementRelocation`).
Selection uses the completed House footprint and active permanent residents.
Volunteers come first, then existing notices and the latest eligible arrivals,
with ordinal agent IDs breaking equal arrival times. Forced selection protects
the dominant domestic family; when no family has a majority, the arrival order
does not favor a family. The majority is checked again after each selected
departure, so a family that gains it part-way is protected from then on.
Sole caregivers are ineligible for the notice timer,
including when their dependent lives elsewhere. Capacity is recalculated after
each proposed departure, and a departure that would not reduce overcrowding is
skipped. Selection stops when the remaining residents fit.

`Housing.Relocation` stores the household, original notice tick, fixed deadline
and selection reason. The initial period is one world day. Pausing and loading
do not consume or restart it. A forced replacement inherits a still-future
deadline; when notified at or after it, they receive one fresh world day from
the current world tick. Volunteers keep their original deadline. Other residents’
notices, births, age changes and unfinished expansion do not restart a notice. Reconciliation cancels obsolete notices after changes in
residents, family, care or completed capacity. Admission and departure actions
recheck that eligibility before acting on a saved or delayed choice.

At expiry, each still-eligible adult leaves through the ordinary departure
transition, in the order selection planned, rechecking remaining need before
the next exit. No location or
inventory is transferred remotely. The once-only food allowance, personal
collection rights, borrowed goods and paused work retain their existing rules.
An adult without a new home keeps a visible housing task. An overcrowded House
with no eligible adult remains blocked while its members arrange expansion or
a voluntary household split; sole caregivers are never forced out with their
children. The split is offered only when the House cannot grow: no expansion
is running, and either no larger footprint fits or its extra land is neither
the household's to use nor free Town land it can still ask the Council for. While only that land permission
is missing, the housing line names it as the next step, and the expansion
land request prefers a footprint whose extra land no other household holds or
has asked for. Built-in rules do not found a household while the adult still
has a home, so the notice period can end in a completed expansion or an
accepted request. Agent observations and owner inspection show resident
counts, notice reason and time, pending requests and expansion state.
`relocation_notice` and `relocation_cancelled` record changes without
repeating them on reload.

**Guardian placement** (`SettlementGuardianPlacement`). An adult's explicit
acceptance records primary care separately from the child's move. When a child
cannot yet join that adult's household, their physical state keeps a pending
placement tied to the exact accepted care relationship and its revision. The
guardian needs a recorded Town and a completed household House with room;
acceptance creates neither a House nor a resident place.

The guardian first reaches the child, then accompanies them to the selected
House through ordinary movement. The guardian waits for a child who falls
behind. Urgent food and warmth needs may interrupt the journey without
removing accepted care. Capacity, current care authority, Town membership and
the House's identity are checked again before placement. Only arrival together
commits the child's household and Town membership in the same world transition,
using the existing rule that dependents follow their accepted primary caregiver.
The destination guardian is already a Town resident; the move does not invent a
Council admission proposal or give an unrelated adult membership. Parenthood,
birth records and property ownership do not change.

Pending placements retry after temporary blockers clear. A change of caregiver,
death or the child reaching adulthood ends the old placement. The owner's
agent card and People section distinguish accepted care, a blocked home and
travel through existing observation notes. `guardian_placement_pending`,
`guardian_placement_completed` and `guardian_placement_cancelled` record the
placement lifecycle separately from `guardian_assigned` and `guardian_needed`.

**Continuity rule** (`SettlementContinuity`). The owner's answer on
[#654](https://github.com/compoodment/ClankerWorld/issues/654) sets provisional
numbers: the rule is on while fewer than eight active agents are not
elders, and a couple may say "not yet" for two world days
(`2 × TicksPerDay`). The checkpoint saves whether the rule was on at the last
check and, per eligible couple, the tick at which their "not yet" ends. A new
world appends `continuity_rule_on` when it is created; each tick
`MaintainContinuity`, after `MaintainParenthood`, compares the live count with
the saved flag and appends `continuity_rule_on` or `continuity_rule_off`
(detail `non_elders:{count}`) only when it changes, so reloading never repeats
one.

While the rule is on, an eligible couple is an accepted partnership that
also passes the ordinary parenthood checks (both adults, since elders cannot
have children; both with a household; not close kin) where neither partner is
a parent or caregiver of a living infant. A couple first gets a deadline when
it becomes eligible, and loses it when it stops being eligible or the rule
turns off. Until the
deadline, `parent_postpone:{owner}` replaces both `parent_decline` and
`parent_cancel`, and moves the plan to the inactive `postponed` stage; refusal
candidates are neither offered nor applied. A couple held by the rule may
propose while caring for an older child. At the deadline the server moves an
existing plan to `preparing`, or creates a new plan for the partner who last
asked (else the first by ID), and appends `continuity_plan_proceeded`.
A new plan records that parent
as its primary caregiver and their current household as its intended home.
Resuming an accepted or postponed plan preserves its original initiating
parent, request tick, selected caregiver and intended home. Birth still follows
the caregiver's current household, including after a move or when its House
is full. A plan past its deadline does not expire: it waits for the usual food,
shelter and readiness checks. The rule only reads accepted partnerships and
never proposes or accepts one.

Each partner's self context carries a `continuity` note saying the rule and
the hours left; a single agent's stays empty. Loading checks the flag and
couples: known partner IDs in order, no couples while the rule is off, and no
deadline more than two world days ahead. These events are logged as
`settlement_family`; the two transitions are also Event Log lines.

A recipe project that finds its work site busy waits with the blocker "Waiting
for a free work site". While anyone waits, no one else is offered a new recipe
for the same workstation design, so the waiting agent gets the next turn
instead of losing it each time the site frees. Field work uses its own physical
tile and cannot be started through a zero-input crop recipe.

Recipes do not depend on a role or on personality or aspiration text. An adult
resident is offered field work only when their household holds a Farmhouse,
and a workstation recipe only at a building their household holds or at a
communal one, which has no holding household. A Farmhouse or Blacksmith that no
household holds is nobody's workstation. First-Town setup gives the Farmhouse
to the first starting household and the Blacksmith to the second. The offline
check in `HouseholdBuildingUseCoverageTests` builds generated worlds the normal
way and runs two world days with the built-in rule-based chooser and no model
calls, failing if these offers disappear or cross households. The owner's direct
production request checks the same workstation ownership and age. Crop
recipes are refused; crops need tilled land, a hoe and real planting stock.

For workstation recipes, site selection checks the actor’s current walking route as well as
ownership and unused production capacity. An occupied or inaccessible workstation
does not hide another reachable one. Movement’s existing diagonal and household
access rules still apply. If no site is reachable, an existing project uses its
blocked/reconsideration path rather than travelling toward an unreachable tile.

Recipe preparation and production must use the same actor/building owner.
Direct production requests use the same age restrictions as autonomous choices:
only adults and elders may start workstation recipes or field work. A request for an
infant, child or adolescent is refused before reserving inputs or changing jobs.
Household workstation inputs must be present at the actual building; stock
elsewhere in the household is not on-site stock. Missing inputs block the
project under its existing retry rules, without granting another household's
materials or implicitly transporting remote goods.

Blacksmith input hauling checks the actor's current unoccupied pickup route
and the source-to-shop route before selecting household or permitted Town
Warehouse stock. An occupied earlier lot does not hide later reachable stock.
The same selection is repeated when hauling or planning a supply order; exact
lot/item targets, carrying limits and receiving-space checks still apply.

If an unpaid household recipe remains blocked for 60 ticks and no household
member has an actionable way to supply its missing ingredients, the runtime
pauses its saved plan and stops trying to continue it automatically. The adult
can choose other work. After ingredients return to the building, choosing the
recipe again resumes the saved plan. This does not interrupt a running
production job.

Barter choices and offer creation require both agents to be adults or elders.
Infants, children and adolescents cannot receive an offer that reserves their
belongings while they have no legal trade response. The society transaction
checks age before reserving either party's stock. Household and organization
parties keep their existing inventory rules. Previously saved offers retain
their normal withdrawal and expiry behavior; loading does not rewrite them.

Personal barter keeps one usable carried unit of each ordinary item kind in
reserve across the owner's eligible lots, rather than requiring two units in
the chosen lot. Reserved, worn, contained, delivered, stored or uncarried
units cannot supply that reserve. Artifacts and unworn ornaments retain their
single-unit exception. Each offer still reserves one actual unit from each
named lot, and settlement requires both parties' independent acceptance.

**Household shops** bind an inventory barter offer to its actual business and
holding household in `BusinessTradeState`. Offers use unreserved goods in that
building and payment already carried by the adult customer. The inventory
remains the only authority for quantities, acceptance and lot reservations;
the business binding keeps the transaction location and readable outcome.
Positive net incoming space is held for both parties, alongside production and
inbound delivery space. Both traders must reach the shop before the household
accepts and the inventory transfers anything. Payment is placed in that exact
building; the purchase becomes personal cargo. Cancelled, expired or
invalidated offers release their reservations without transporting goods.
Buyers compare usable tool tiers and garment protection in the current weather.
Their best usable tool in each family, equipped clothing and carrying aids, and
the active equipment repair target are excluded from payment.
Customers receive transaction access only. Store stocking first moves actual
surplus into carried delivery lots, then uses ordinary household hauling to
reach the Store. A remote House, field or Warehouse is never sale stock.
Store stocking also keeps each adult's best usable work tool. Food stocking
keeps two usable servings of each food kind for the adult, or two per
living member when taking household stock. Each reserve is counted once across
eligible lots with the same owner and food kind, including when an order is
bound to one lot. Unreachable, reserved, spoiled, contained or other owners'
stock cannot satisfy the reserve.
Optional shelf restocking waits behind gathering materials needed by household work.
Rates, the eight-unit shelf target and four-unit carried loads are provisional.
Blacksmiths can sell real refined iron for another household's tool work.
Meals remain tracked in #564 and its domain
issues; currency remains later work. The Clinic sells actual medicine
and bandages through the same inventory and physical business authority.

Released Town construction loads retain their Town owner and exact material
history. Resident recovery moves only available ground quantities to a reachable
Town Warehouse with actual room, leaving one carrying space for food. A split
load gets a released custody record in the original project's delivery ledger;
this does not revive its construction commitment or reservations. Urgent food or
warmth permits immediate set-down of released carried goods. A temporary path
obstruction while returning waits rather than dropping and recollecting cargo.

**Markets** use the same inventory authority with separate saved paid-building
and occupancy records. The Council-approved starter project pays for the 2×2
hall and only two 1×1 stalls on the fixed 7×4 plaza, in slots 0 and 4. When
every standing stall is borrowed and no further stall is proposed or under
construction, the next unused fixed slot may be proposed as a separate
Council-approved Town project. A standing Market's site tiles count as occupied
for other buildings, Town project sites, expansions, fields, tree planting and
household land requests. The provisional starter budget is 24 wood,
8 stone and 4 fiber with 10 work units; another stall costs 4 wood and 2 fiber
with 3 work units. General plaza growth has no implementation or agreed rule.
Physical stock receipts retain the personal or household owner. One named
active adult borrows a stall while they remain inside the hall-and-plaza area;
leaving, household change, death or removal ends borrowing and releases
unfinished offer claims without transferring leftovers.

Market live records retain recent outcomes and the exact borrowing, deposit,
open-offer and payment bindings still needed by actual stock. Receipt sequences
survive retirement; an explicit boundary allows closed source history to leave
without granting selling authority. Closed inventory offers and claims retire
with their Market records, while paid construction/removal and physical property
remain intact. The [Market save rules](saves-and-replay.md#paid-markets-and-stall-trade)
explain live bindings and archive/recovery behavior.

Loads, borrowing, deposits, collection and barter mutations need a fresh
accepted, non-fallback personal LLM choice; their candidates rank above
`safe_idle`, so built-in rules never pick them. Continued intentions walk only;
owner orders do not authorize these mutations. A member carrying their own
household's goods may return them to its House through `household_return`, and
the household hauling, farm stock and planting routines skip stock on a stall
while a member of that household borrows it. Explicit `market_collect` also
protects household stock from other housemates while borrowing is active;
the borrower may collect it, and personal owners may retrieve their own goods.
The same choices are checked again before applying a mutation or continuing
travel, using current borrowing.
Usable loose surplus (the food
reserve counts the owner's other usable stock of that kind), actual
carrying and stall room, active claims, current household rights and protected
equipment constrain the offered choices. During urgent hunger, Market candidates
include only collection of edible food the adult may legally retrieve. This
filter runs before the candidate limit; normal Market choices retain their
existing order. Collection still needs a fresh personal LLM choice and the
usual ownership, reservation, route and receiving-space checks.
The provisional one-for-one quote is
an actual `Inventory.Offers` exchange. Buyers may belong to any Town or have no
Town membership; walking into the Market and completing a purchase change
neither their household nor their Town. The named seller accepts only after
both people meet at the stall. Purchased stock
becomes the buyer's personal cargo; payment is physically set down as the
seller's household stock, including payment for personally owned goods. A
later stall borrower cannot sell an earlier borrower's stock. Its recorded
owner, or a current member of the owning household, may physically collect it.
Live inventory ownership remains authoritative after inheritance or collection.
Customer access remains limited to the named transaction. See the
[Market save rules](saves-and-replay.md#paid-markets-and-stall-trade) for the
saved layout, stock and offer checks.

Missing-input demand checks the buyer's actual production owner, keeping each
household's available materials separate. A nonterminal recipe plan marked
`RequiresFreshChoice` may choose a Market purchase while remaining paused; an
actively continuing plan keeps the adult at its work. Resuming the recipe
still requires its ordinary choice and physical ingredient-delivery rules.

`WantsFieldPlantingStock` reuses the actual field and planting-stock checks for
grain seed, cultivated-green seed and loose potatoes. The adult must belong to
a household holding a Farmhouse, have a usable hoe and have no urgent survival
need or actively continuing project. That household must need food, and the
adult must be able to reach its idle `Prepared` or `Harvested` field without
another person's planting claim. A usable personally carried planting unit,
accessible household stock or that field's reserved replanting lot satisfies
the same-kind need. The exact one-unit purchase remains personal cargo until
ordinary field work consumes it; a held unit suppresses further same-kind seed
quotes even when the seller still has stock. Other goods may still be wanted.
Demand creates no future seed buffer or access to another household's stores.

### Blacksmith tool-making requests

A tool-making request records bounded demand for an existing Blacksmith recipe.
A customer can have one active request; the world keeps up to 32 active requests
and 32 terminal records. Placing, accepting, refusing or withdrawing one requires
a fresh accepted personal-model choice. Jev, fallback, repeating intentions and
owner orders cannot make those commitments. Routine supply and already accepted
production continue through the existing household project machinery.
The four personal commitment choices rank below safe idle for the built-in
chooser, so it does not repeatedly select an action that cannot execute.
Walking to a known Blacksmith, continuing accepted work and opening a quote
for a completed tool retain their routine priorities. Opening the quote does
not accept payment or transfer goods; the existing barter consent checks apply.

The accepted worker produces with actual household-owned inputs at the named
Blacksmith and its normal output-space reservations. The finished tool remains
household property. The request references the real production job and output;
it creates no future inventory, advance payment or exclusive customer title.
An eventual quote uses the existing completed-goods business barter offer and
its acceptance, meeting, stock-room and exact transfer checks. Withdrawal or
interruption does not undo work or change the owners of materials and goods.
A request stops when the customer or the shop becomes unavailable, when no
adult is left in the selling household, or when the customer joins that
household. If the worker leaves, the request stops; a housemate may still
resume the paused job as ordinary household work.

The observation projects at most eight requests per actual building, preferring
active requests and recent transitions. The relevant customer or household
member gets a scoped note of at most 256 characters; public blockers are at most
160. Notes expose request progress, never unrelated private stock or raw model
dialogue: the worker's own errands and storage problems appear only as "still
preparing" or "waiting for a work site". Owner cards show the requester, existing recipe and current blocker;
the actual building supplies its name.

**Clinic supplies and illness care** use the normal household building,
workstation supply, ecology and recipe paths. `clankerworld-care-v1` adds a
1×2 Clinic costing 10 wood and 4 stone, bandage recipes at the House and Tailor
Shop, and a medicine recipe at the Clinic. One cloth makes two bandages in
eight base work ticks; two medicinal herbs, one fresh water and one wood make
two medicine in sixteen. These quantities and times are provisional. Herbs
come from reachable renewable patches. Ingredients must arrive at the actual
workplace; medicine reserves water from a real reusable jug and leaves the
vessel intact. Injury causes and bandage treatment remain deferred.

The [#749](https://github.com/compoodment/ClankerWorld/issues/749) fix
returns empty household pots and jugs from a workstation to the household's
House using physical pickup and the existing delivery path. It keeps inventory
ownership and reservations authoritative and checks carrying room, the walking
route and destination space. As with other household deliveries, the hauling
adult holds the vessel during the trip and delivery hands it back to the
household. A save during the trip retains the same vessel and delivery.
Automated checks cover the return path.

Medical permission is admitted only from a fresh, accepted, non-fallback
`LargeLanguageModel` choice by the adult patient. Jev, owner orders, failed
replies and continuing intentions cannot grant or revoke that authority.
The patient may permit at most 16 living named caregivers. While a permission
slot remains, the candidate list offers up to 16 eligible nearby alternatives,
independently of the remaining slot count. Granting still checks the current cap.
Self-treatment is allowed, and a dependent's effective accepted `Caregiver`
relationship supplies their existing authority. Treatment checks living adult
caregivers, permission, local patient observation and actual usable medicine.
An unrelated household's stock must be bought through ordinary barter first.
Models receive no distant patient's hidden health or location through care. A
caregiver starting an offered medicine fetch records that locally observed
patient and their last-seen position in `MedicalSupplyTrip`. The same intention
continues to accessible stock, collects one real dose, then returns to the
recorded position. Leaving interaction range does not cancel the fetch.
Treatment still requires finding the consenting patient nearby; a patient who
has moved out of sight is not tracked remotely. Permission loss or death ends
the trip. Returning to an empty last-seen location, observing that care is no
longer needed, or starting treatment also clears it. Current-format reload
preserves both outward and return travel without reserving or inventing a dose.

Starting medicine reserves and consumes one actual dose through the inventory
authority. Its completed reservation binds the patient, caregiver, owner and
start time. The provisional course lasts twenty world ticks and removes
75 illness basis points per tick. Maintenance ends an interrupted course
without refunding the dose, including death, permission withdrawal or loss of
dependent-care authority. Closing the receipt's medical purpose prevents a
spent effect from being reattached after permission is renewed. Death releases
live reservations while preserving completed consumption receipts. Permission,
active progress and closed receipts survive current-format save/reload; a
paused world advances no treatment time. See [saves and replay](saves-and-replay.md).
Automated checks cover this path; the
[Windows playtest](../../playtest/565-clinic-care.md) is still pending.

Death archives the last physical state, frozen age and the Town the agent lived
in, then removes the active actor. Existing personal inventory is frozen in
estate escrow, and `SocietyEstate.BeneficiaryIds` records the household default.
[Wills](#wills) explains the final will and how the estate is divided.

New proposals for Shelters, Storehouses, Cooking fires and Stone hearths are
retired. Existing buildings, projects and recorded proposals remain for old-world
compatibility. Approved owner building designs stay active but are not household
kinds, so agents do not plan them; they wait for shared buildings. House fires supply heat. General invention is later Workshop work.
Fire-tending selects an unlit hearth the adult can reach, using the same
household access and interaction distance as movement. Selection is repeated
when tending begins, so changed occupancy can redirect the adult to another
hearth or leave warmth-seeking available. Fuel is consumed only at the hearth.

Clothing comes from a household's Tailor Shop (`clankerworld-tailor-v1`), which
replaced the Weaving frame and its "Woven clothing" recipe outright. The shop
weaves 3 fiber into 1 cloth in 20 ticks and sews 2 cloth into 1 clothing in 24
ticks. Its 1×1 size costs 8 wood and 2 fiber to build; all of these are provisional
values. First-Town setup stores each starting agent's garment in their
household's House. A package is staged for older worlds on the first tick, but
the settlement package's digest changed when the Weaving frame was removed, so
saves made before this change are refused.

The five alternative household building sizes have separate shipped packages;
their original definitions and first-Town defaults keep their identities.
Each alternative includes companion recipes bound to its own exact building
definition. Their package identities cover the complete building, recipes and
dependency ranges, including the Smith's Ornament recipes and the Tailor's
Care recipe. First-Town setup activates them after their dependencies. Normal
runtime staging requires every declared dependency to be active at a compatible
version and never restages a package already recorded, including a rolled-back
or quarantined one.

The ordinary household planner, paid construction, full-footprint site checks,
workstation reservations and physical supply paths serve both sizes. Exact
recipe-to-building equality stays authoritative. Personal handcart planning
chooses the recipe bound to the household's actual Blacksmith, including when
checking work already running or materials the adult keeps for that cart.
Storage still follows `BuildingStorageRules`; production capacity remains one.
The current sizes and provisional costs are listed in
[What works today](../what-works.md#household-building-sizes).

Workstation recipes use only stock already at the building. A household
building without its own dedicated hauling (every kind except the House,
Farmhouse and Blacksmith, including the Tailor Shop, Clinic and Restaurant)
is kept stocked by the `supply_workstation:<item>` choice. It is offered to an
adult of the holding household while the building holds less of an input than
two batches of the
largest recipe that needs it, counting loads already on their way. The adult
delivers what they carry, picks up the household's spare stock from its House
or private farm or cooking stock (the existing delivery step then carries it
in), or gathers from a reachable source. House pottery and named cooking also
use this supply path. A vessel and its contents move together only when the
whole family fits the person and destination; loose inputs use four-unit loads.
Protected planting reserves stay unavailable. Grain already delivered to a
House or Restaurant is left there rather than hauled back into farm stock.

House cooking and Restaurants use the named recipes in the
[agreed food pipeline](../game-design/towns.md#food-and-replanting). Generic
food-to-food production is refused. Production reserves exact usable on-site
inputs and room for its net storage growth; finishing consumes the reservations
and stores two named servings, leaving water jugs intact. A runnable named meal
has priority over input replenishment, so one shared jug cannot shuttle between
House and Restaurant indefinitely before anyone cooks. The trial cooked-food
reserve is two servings per living household resident across prepared kinds.
A recipe that improves already prepared food, such as Restaurant meals from
bread, checks its own finished dish so an existing bread reserve cannot hide
that choice. Meals give 40% fullness, stew and fruit/berry porridge 50%, and
Restaurant meals 60%; decorated porridge and Restaurant meals also improve
nutrition. These values remain provisional.

Concrete meal names are retained for dietary variety. Raw grain, potatoes and
flour keep their freshness in the current trial. Bread and cooked meals spoil.
Protected odd-rate goods use a stable two-tick cadence, combined with the pot
cadence, rather than rounding their spoilage rate to zero.

### Wills

An estate with frozen lots and an agent with a personal model gets exactly one
post-death request (`PrivateWorldRuntime.Wills`). Its `CognitionWillContext`
offers the frozen lots as `item:1`… keys (at most 24, in lot-ID order) and up to
sixteen living people as `will:heir:{id}` keys, family and household first, plus
`will:town:{id}` for the archived Town when it has a Warehouse of its own. The
candidates are `will:household` and, when anyone may inherit, `will:heirs`.
The model's reply (`CognitionWillChoice`) is untrusted: the response must match
the request, epochs and digest; the runtime maps only offered keys back to
heirs and lots; and `SocietyFixture.ResolveWill` checks that every heir is a
living person other than the deceased or an offered Town, that there are one
to three distinct heirs, and that every listed lot is still frozen in this
estate. Anything else resolves to the household default.

`ResolveWill` stores the exact division as `WillBequests` (lot, heir,
quantity). "items" gives each listed lot whole to its heir. Every other lot,
and every lot under "equal", is divided equally: each heir gets the same whole
number of units, and the units left over go one at a time to the heirs in the
order the will names them, continuing from where the previous lot's leftovers
stopped. Lots are taken in lot-ID order, so each lot's parts always sum to its
frozen quantity. A storage pot, water jug or handcart counts as one unit, and its
contents always go with it to the same heir; only top-level lots are offered to the
model, with a vessel's contents described beside it.

Settlement runs once, when the escrow expires and no will is pending. A
living person heir, including a child, owns their part without automatically
carrying it. Ground lots keep their tile, stored lots retain their recorded
storage, and goods held by a living carrier remain in that carrier's custody.
Goods carried by the deceased are dropped at their last tile. A Town heir's
part goes to its Warehouse as Town stock while the Warehouse has
room and stores that kind; the runtime passes each Town's Warehouse, free room
and refused kinds (food, and handcarts, which stay on the ground) as
`SocietyTownStore`. Whatever the will cannot deliver (a share for an heir who
has since died, food, a handcart or goods beyond the room) follows the
household default: an equal split between the living household
beneficiaries, with the first in ID order taking leftovers, or communal stock
when none remain. A vessel and its contents move as one family and keep their
lot IDs: the Town takes a family only when the Warehouse accepts every kind in
it and has room for all of it, otherwise the family follows the household
default, where vessels rotate between the living beneficiaries. A quantity-one
map or field record keeps its lot ID.

Final words are optional with either outcome. `CognitionWillChoice.NormalizeFinalWords`
turns control and invisible formatting characters into spaces, collapses
spaces, and refuses text over 80 characters or containing markup characters
(`< > [ ] { }` and backticks). The HTTP provider parser drops unusable words
before admission; a directly supplied typed reply with invalid words is refused.
An admitted reply keeps its words even when its division falls back. At settlement
each living person who receives goods gets the private memory
`final-words:{estate}:{heir}` ("Name's final words were: '…'"). The owner sees
the words on the historical profile through
`ViewerFinalWill`; events, logs and telemetry carry only IDs, the split and heir
counts, never the words. See [saves and replay](saves-and-replay.md) for
pending-will restore behavior.

## Fertility and household fields

`LandFertility` derives each land tile's fertility from the world seed,
rainfall, climate, surface and nearby rivers or lakes. It is derived data,
not a saved object or a resource lot. Grass and meadow commonly support
farming; dry scrub is poor, and sand, rock, snow, mountains and water cannot
be farmed. Inspection uses Poor, Fair, Good and Rich rather than a score.

An adult or elder in a household holding a Farmhouse must stand on a free,
farmable tile with a carried wooden hoe to start tilling. Roads, resource
objects, buildings, reserved expansion tiles and existing fields are
excluded. Tilling takes eight work ticks; planting, tending and harvesting
take four each. These times, growth, yields, storage and planning targets are
provisional. Neighbouring tiles make a field without a fixed shape or size.

Each authoritative field records its household, crop, stage, work, growth
times and replanting reserve. Planting reserves and consumes one carried
grain seed, cultivated-green seed or potato. Moving away, death, lost tools
or urgent needs cancel unfinished work and release its planting input.
Field work uses the existing illness cadence. Only a successful work stroke
advances progress and wears the selected tool. A new planting input claim
lasts until actual completion or interruption, so illness does not make it
expire while the worker is still planting.
Completed harvests remain intact. Fertility and weather affect crop growth
or yield. Harvesting creates grain, potatoes or cultivated greens on the
field, and grain and greens also yield two replacement seeds. One usable
planting item is reserved before surplus can be traded. Picking it up for
the next planting releases the reserve and physically carries that item.
Player planting orders choose carried stock first, then the field's replanting
reserve, then other eligible lots in ID order. Shared stock must have an
unoccupied pickup route within its interaction range, checked during both
order selection and execution. An occupied earlier lot cannot hide another
usable seed. Autonomous crop claims keep their terrain eligibility while
temporary occupants pass and the existing physical route retries.

Planning compares population and available ready-to-eat food with a trial reserve of two
days at two meals per resident per day, then estimates fields from expected
yield. It picks free reachable soil by fertility and distance, favouring
tiles beside the household's existing fields. It can expand during shortages;
the existing household-building planner can establish another Farmhouse for a
household that lacks one and has the materials. Raw grain and potatoes cannot
satisfy this ready-food reserve directly; their prepared meals can, so raw
stock does not stop farmers planting fresh greens. Grain is milled into flour
at the Farmhouse, one grain to one flour.
Field work records the selected carried hoe or sickle lot. Wooden and iron hoes
reduce the work still needed to till and tend, while wooden and iron sickles
reduce harvest work; an iron sickle is faster than a wooden one. Each committed
work tick wears one unit of the selected tool. Interrupted or refused work does
not wear it. A tool in storage, on the ground, in delivery or inside a pot is
not directly usable; it must first be carried at the top level.
If wear breaks a selected tool before the field effect completes, the runtime
stops that work in the same committed action. The crop stays at its earlier
stage and the broken tool stays in the owner's cargo, so the checkpoint remains
valid without waiting for another tick.

Wild berries and greens replenish. Orchard fruit appears in autumn after a
planted orchard matures. Harvesting fruit also produces a distinct orchard
seed with a reserved planting unit. An adult carries that seed to legal free
land and plants a sapling; tree growth, fruiting and the reserve survive reload.
The orchard planting choice requires carrying room to collect a shared seed.
An already carried planting seed remains usable at full capacity. Ordinary
wood-tree seeds remain distinct.

Urgent food recovery first sets down ordinary spare cargo. If that cannot free
enough carrying room, it may also select the actor's own orchard propagation
seeds. Their selected planting reservations are released in the same inventory
transition that stores the seeds with the household, after reaching the House
or camp pile. Walking, an unavailable destination or a refused transfer does
not release them. Other reservations, delivery loads and borrowed goods remain
protected; crafting does not use this exception.

The Godot map draws worked soil and crop growth above the terrain, including
wrapped map edges; the overview marks field tiles. Fields join the household
property display, and inspection shows the household, stage, crop, worker and
actual ground stock. This is current household use, not a separate land-title
system.

## Roads and bridges

Roads and bridges are world-owned and permanent. Walking never adds a Road
tile. The agreed rules are in
[Towns](../game-design/towns.md#how-roads-and-bridges-appear); this section
describes how the current code applies them.

**One saved crossing for everything.** A bridge is a saved record with a stable
ID built from its position (for example `bridge-90-3-ew-2`), a design
(`plank_span_1` or `plank_span_2`), what built it (`road` or `traffic`), both
entrance tiles, its river tiles and the tick it was built. A Road bridge also
names its route, `road:<Town>:<building>`. Movement does not read terrain
alone: the runtime turns the saved bridges into passable decks on the map
(`SeededMap.BridgeDecks`). A deck is walked end to end along the bridge, never
sideways into the river or diagonally, at dry-ground cost. A Road bridge's deck
counts as Road for the Road speed factor; a traffic bridge's does not. The
observation sends the same records, so Godot draws the deck and the tile card
and hover say "Bridge" from exactly what movement uses.

**What can be bridged.** A crossing starts on buildable ground, runs straight
across one or two river tiles and lands on buildable ground. Lake, ocean,
coast, a third water tile, a mountain bank or an existing deck ends the search,
so those are never bridged. Crossings can run across the east/west seam of a
wrapping map.

**Streets that meet a river.** Town growth (above) can cross a river up to two
tiles wide on a new bridge, in two places:

- A new side street is found by `RoadRoutePlanner`, which applies the side-street
  rules above and searches at most 32,768 tiles. Besides ground steps it may
  cross an existing bridge, or a new crossing, in a straight cardinal line. A
  new crossing costs 200 per river tile, like wading a one-tile river, so an
  existing bridge is cheaper. Ties break by cost, then row, then column, then
  discovery order.
- A street running on past a door (`TownStreets.Wander`) that heads straight at
  a river may cross it. The far bank must be clear, and it counts as one step
  of the run-on. The first Town's starting layout does not use this rule and
  keeps its streets on dry land.

Each proposal is checked whole before anything is saved: every new Road tile is
clear buildable ground, each step is a legal street step or a bridge, and every
new crossing is still legal, shares no water with another and is not redundant.
Road tiles, the building's entrance and new bridges are then committed together
in the same tick, and the border grows around the new Road tiles on both banks.
If there is no legal side street, nothing changes and a `town_road_unconnected`
event records the reason (`no_entrance`, `route_unavailable` or
`redundant_crossing`). A bridge with Road at both ends counts as a link between
them when finding street ends. If that bridge is an end's only link, its
canonical entrance order supplies the outward heading for the run-on. This
keeps the street heading away from the river across the east/west world seam,
including on narrow wrapped maps. The same clearance and three-tile frontage
checks apply.

**Same connected banks.** Two crossings join the same banks only when they
cross the same river, joined through its water, and each end of one reaches an
opposite end of the other by walking along that water's shore without crossing
it. Each shore step obeys the ordinary foot-movement rules: impassable Peaks
break the connection, while walkable Mountain terrain does not. A tributary
mouth or a separate stream breaks the shore, so a bridge over a
different nearby stream never blocks another. There is no distance limit; the
comparison examines at most 4,096 river tiles and, if that runs out, does not
treat the banks as the same. It first checks the shore beside the connecting
water path. If that is incomplete because the river widens, it follows the
water between the crossing spans to include the wider shore, staying within
eight tiles of the connecting water. The spans and that reach stop the
expansion from going around distant headwaters and joining separate
tributaries; a locally proven connection needs no whole-river search.
Along one unbranched stretch of river this allows
one bridge, however long the stretch. A new crossing over the same banks as an
existing bridge is not built; a Road uses the existing bridge instead, and one
route never builds two bridges over the same banks.

**Traffic bridges.** Only a committed foot step counts. Stepping from a bank
onto an unbridged crossing of one or two river tiles starts a wade. On a
two-tile crossing, stepping onto its other water tile keeps the same wade
open; a step out of the water onto the opposite bank completes a crossing.
Route previews, blocked moves, waiting and turning back add nothing, and
walking on Roads or bridges is not wading. Both widths use the same rule and
the same saved evidence, keyed by the crossing's bridge ID (such as
`bridge-124-62-ew-2`): open wades, and completed crossings from the last two
world days, at most five per agent per crossing, which is enough to decide the
rule exactly. A crossing with a bank that is not buildable, such as a
mountain, can be waded but gives no evidence, since no bridge could land there.
At the end of each tick, a crossing with six completed crossings by at least two
agents gets a `traffic` bridge across its whole width (`plank_span_1` or
`plank_span_2`). It is not built, and its evidence is cleared, if a bank holds
a building, resource or camp object, or an existing bridge already joins the
same banks. A traffic bridge adds no Road tiles. Wading only reads terrain, so
a bridge laid across one tile of a two-tile crossing from the other direction
leaves the other tile wadeable back to its own bank, but the deck cannot be
entered from the side.

Events are `bridge_built` (`road:<bridge>:<route>` or `traffic:<bridge>`),
`traffic_bridge_not_built`, `town_road_unconnected` and, if a run-on fails its
final check, `town_road_extension_refused`. The host logs them as
`bridge` and `road_route` lines with IDs and reason codes only.

## Council-approved street lanterns

Paid street lanterns reuse the shared Town-project ledger and the ordinary
building projection. A one-tile roadside `Position` and its approved adjacent
Road `Entrance` bind the exact edge; two immutable definitions carry the style
and trial cost. Live sites use uncontested Town title and existing Road tiles.
The same actual delivery reservations and work receipts pay for completion;
direct placement cannot bypass them. Lanterns are excluded from doorway-based
Road generation and extension. Derived project tags let the owner client show
Road-side facts without guessing from names or dimensions. The saved receipt
rules and schema are in [Saves and replay](saves-and-replay.md#council-approved-town-projects).

The normal map draws those projected fittings with Claude's approved
`NightLightShapes.StreetLantern` renderer. It uses the bound Road tile and edge,
shows fittings by day and adds light only during the derived night interval.
No fuel, lit flag or visibility authority is introduced. The road remains open
and a visible fitting can be selected for its paid project details.

## Approved authored assets

Paused authoring can reference only an exact normalized `assetId` and lowercase
`sha256:<64-hex>` digest in the immutable host-approved catalog loaded at startup.
A missing catalog denies all references. Malformed, ambiguous or unknown-schema
catalogs prevent startup. Device requests cannot edit it or upload asset bytes.
This is a narrow reference rule, not the future complete art-generation pipeline.

## Building storage history

Building Details receives up to ten recent storage deltas from the existing
retained inventory-event stream. Each committed physical change records its
building, item kind and signed quantity on the existing event; event identity,
time and trade receipts stay intact. The runtime includes direct location
adjustments around kernel operations without recording an already captured
change twice. Net-zero lot splits, ownership changes, reservations, wear and
spoilage add no storage change. Projection scans the retained stream once and
never reconstructs a historical location from a current lot. The records
follow ordinary rollback and event compaction, not a second growing ledger.

## Runtime logging

Changed gameplay loops, provider adapters, persistence boundaries and lifecycle
gates need low-noise structured logs at meaningful outcomes. An operator should
be able to tell what was attempted, accepted, rejected or fell back, and why.
Use stable event names and named fields such as tick, entity/request ID,
provider role/model, legal intention, outcome, latency and bounded usage.
Log transitions and decisions, not render frames or idle polls. The player
Event Log and unread count select known player-facing events and public
milestones explicitly; unknown diagnostics do not become player text.

Never log API keys, authorization headers, signatures, credential-bearing URLs,
prompts, raw provider request/response bodies, hidden reasoning or unbounded
player/model text. Memory/belief/map telemetry uses safe IDs, counts and outcome
metadata, never private prose or map contents. Logs are derived telemetry, never
simulation authority or required save state. Observability tests must prove both
useful signal and absence of representative secrets.

Event descriptions resolve complete agent and Town IDs from the owner snapshot;
colons inside those IDs are part of the identity. Food yields and Town membership
fields are read separately. Existing entries use the current saved name, including
deceased profiles. Hosted-decision and Town telemetry likewise keep complete IDs.
These readers do not rewrite accepted event details or change save/replay formats.

The owner snapshot projects the saved continuity rule's current on/off state.
Godot uses it to keep an **Add a newcomer** offer in the Event Log while the
rule is on in a started world, even when the transition event has left bounded
history. The link opens the existing Add Agent controls and rechecks the current
snapshot when clicked; it neither places an agent nor asks for a paid model call.

Preparing parenthood notes derive the caregiver household's usable edible
reserve and required amount from the same captured society/inventory checkpoint
as the owner snapshot. Birth and these notes share the lot ownership, carrier,
edibility, usable-vessel and reservation predicates. The reserve remains two
portions per active household member plus four for birth. A parent's continuity
guidance includes the amount only when they belong to the caregiver's current
household; a partner living elsewhere gets the blocker without stock quantities.
These notes add no saved fields, events or birth authority.

## Developer edits

The F12 panel submits one signed `POST /api/v1/owner/developer-edit` command for
the selected agent. The signature binds the world ID, expected latest event ID,
agent, operation, value, amount and optional other agent. The host shares the
world-selection mutation gate, then the runtime checks that the world is paused
and the observation is current. Changes are prepared on an isolated checkpoint,
fully validated, and persisted before the live runtime accepts them. Failed
validation or persistence leaves the prior world unchanged.

Needs use whole percentages from 0 to 100. Goods use the bounded list in
`PrivateWorldRuntime.DeveloperGoods`, quantities from 1 to 100 and the existing
carrying limit. Removal consumes unreserved personal carried goods, excluding
equipped items, delivery goods, knowledge records and vessels with contents.
Skills use the four existing learned skills. Relationship edits start or end
partnerships with the existing age, availability and close-kin constraints;
parentage, guardianship and household membership retain their lifecycle rules.
Every accepted command appends a player-facing `developer_edit` event containing
the complete command, including its world and observation precondition.
See [Saves and replay](saves-and-replay.md#developer-edits) for retries and replay.

## Developer tools readouts

Developer tools (**F12** in the Godot client) read two diagnostics from the owner
observation. Both come from the committed world, are never saved and are never
read back by the simulation, so they cannot change a tick, a save or replay.

- **`PlannedRoute`** on each living agent is the route `MoveToward` planned on
  the agent's latest step: its reason code, destination and the tiles still
  ahead. The observation sends at most 256 steps; `StepCount` gives the full
  count. An agent waiting out a slow step keeps the route it was walking; one
  that arrived, was blocked or did something else that tick has none. Each
  proposed tick starts without routes and its commit replaces them. Loading a
  checkpoint, switching worlds or restarting the host clears them until the
  next tick.
- **`LastTickMilliseconds`** on the snapshot is the wall-clock time to prepare
  and advance the latest committed tick, rounded to 0.1 ms. It leaves out
  waiting for the runtime gate, hosted model calls between ticks and the
  checkpoint save. It is null until the first tick after start, load or a
  world switch, and the legacy fixture host never reports it.

The client draws only the reported route; it never plans one. Frame time is
measured in the client.

## Development and finished distribution

Development currently uses the private server. The intended first finished
release runs the same authoritative simulation on the player's Windows PC,
with local saves and keys, using a bundled companion host process
([#468](https://github.com/compoodment/ClankerWorld/issues/468)).
See [where the game runs](../game-design/world.md#where-the-game-runs).

## Material gathering

Material gathering selects available resources reachable from the acting agent,
then checks an actual unoccupied route into harvest range. Heating, help with
another agent's project and Blacksmith ore use this selector; they do not
require a path to the original map anchor. Gathering for one's own project does
not use it yet. This does not change fuel duration or harvest yields.

Shared fuel and equipment also require an unoccupied route to their collection
point. Unreachable stock stays untouched and does not prevent an agent from
using reachable supplies or gathering local fuel instead.

Tools gate and speed real material work. A wooden pickaxe extracts finite
stone, a stone pickaxe extracts iron ore, and an iron pickaxe extracts gold or
diamonds. Axes improve tree-felling output; when no usable axe is available,
agents can still gather one loose fallen-wood item by hand. Each gather action
uses one shared plan for output, tree seeds and tool wear. The runtime checks
that the whole planned load fits before it depletes ecology, then commits the
inventory output and single-unit wear together. A full load or a refused action
does not consume source stock or damage a tool.

An adult carrying a usable, unreserved iron pickaxe can choose actual gold or
diamond mining from a reachable finite outcrop. The complete eight-item trial
load must fit. The decision stops offering more once the adult and their
household together hold eight of that material. These goods remain carried
physical stock.
Ornaments extend this stock path with Blacksmith gold refining, gold
ornaments and optional diamond setting. They reuse normal physical supply,
reserved production and shop exchanges rather than adding a second inventory
or market.

Its worn-item record refers to one exact personally carried ornament. Wearing
does not exempt that unit from cargo or grant protection. Collection, removal,
gifts, sale, storage and estate handling must keep ownership, reservations and
location consistent; automatic storage or payment must protect the selected
unit. Viewer and client descriptions show the worn kind, without a new sprite.
Wear, removal and gift candidates require a fresh accepted non-fallback
LargeLanguageModel response whose exact offered target is still valid at
admission. Jev, continuing intentions and MustDo cannot select them. Gift
recipients come from locally observable people rather than a remote world scan.
Living adults in interaction range with free carrying space are filtered before
the eight-recipient limit, in stable agent-ID order. Full-handed neighbors cannot
hide another eligible adult. Gift options remain capped at sixteen across
carried ornament lots, and admission rechecks the eligible recipient and load.

`HouseToolsContent` adds two recipes at the existing House without changing
its building identity. Each crude wooden axe or pickaxe uses three wood held
at that House, with a recipe duration of 24 ticks. Ordinary household supply
brings the wood; once one batch is available, making a needed tool takes precedence over
stocking another batch. Demand is bounded by active adult household members
and counts usable family tools, including borrowed household tools and better
tiers, plus work already committed. These are provisional balance values.

Crude tools occupy tier zero: four wood or stone per extraction and 4,000
condition basis points lost per successful use, compared with six and 2,000
for the Blacksmith's wooden tools. Only tree/wood and stone entry gates accept
the new tier; ore thresholds are unchanged. Existing collection, reservations,
capacity, ecology and single-unit wear rules still apply. Completed House work
emits `house_tool_made` with the exact worker and item kind for readable Event
Log text. The generic item icon remains until crude-tool art is approved.

The Blacksmith makes wooden, stone and iron tools from actual inputs, refines
iron ore into separate refined iron, and repairs one carried worn tool at a
time. Repair consumes the recipe materials carried by that tool's owner; it
does not restore condition for free. Hammer use speeds building work, and an
iron knife speeds food or other preparation recipes. Recipe and field records
keep their exact selected tool lot through save and reload.

## Ports and communal boats

`PortContent` supplies four approved shoreline definitions. `PortNavigationRules`
checks the two land tiles, chosen approach, three water rows and six clear
one-tile docking spaces. A Port footprint fits inside the map; its water routes
may cross the east/west seam. Foot construction and supply target the land end,
not the water anchor of a north- or west-facing drawing.

Ports and boats use the same Council-approved `TownConstructionProject` ledger
as Halls and Markets. A boat plan pins its completed launch Port and exact
wood/rope/refined-iron budget. Actual delivered lots and completed consumption
receipts pay for one physical `BoatState`; direct unpaid Port placement is
refused. Costs, work and three-tick water steps remain provisional.

Only residents or a Council-adopted typed `TownBoatAccessGrant` may depart in a
Town’s boat. A grant targets one exact visitor or all visitors and is retained
on the ordinary law’s adopted version. Freeform law text supplies no authority.
Repeal ends future permission; it does not abandon a passenger already aboard.
A grant changes no membership, ownership or Warehouse access.

Native personal decisions approach the Port and create a sequenced trip request.
Queue processing chooses the oldest currently usable request, atomically binds
one idle boat and a free destination dock, and boards only that passenger with
their actual carried goods. Blocked requests retain their sequence; cancellation
or lost departure permission settles unused requests. Six moored or incoming
claims exhaust a Port. All free docks are tried for a connected route.

Transport follows saved cardinal water steps while avoiding other boats and
reservations. Underway cognition permits waiting and eating carried food;
foot work and conversations cannot move the passenger away from the boat.
A blocked arrival retains its reservation for one world day, then reserves a
usable origin for return. When neither landing works, it keeps waiting. Native
death and estate handling keep dropped cargo at the physical boat until landing.
Prepared-tick commit and current-format replay retain boat state with the other
world facts. Save validation binds assets to paid projects and journeys to exact
passengers, requests, docks and connected water routes.

The owner snapshot projects physical boats, cargo, incoming docks and all active
requests, plus recent settled requests. Godot retains one marker per boat,
observes its heading and wrap behavior, uses the approved art unchanged at close
zoom, and shows travel and dock use in tile, Port and Town inspection. Port night
lights use the approved T-head lantern. Smaller views scale the approved 32-pixel
boat until #914 supplies approved 16-pixel art.

## Physical handcarts

`InventoryContainerRules.Handcart` is a single ground-position inventory lot.
Its condition, owner and child cargo lots are authoritative inventory facts;
`HandcartHitch` saves only the exclusive cart/puller attachment. The runtime
checks the owner has physically reached the cart, verifies every load source's
position and household authorization, and moves the cart with each admitted
legal cardinal step. Ground cart contents never become carried recipe inputs,
fuel, planting seeds or equipped gear. Loading a partial quantity preserves
the untouched remainder's original location and reservations.
Shared edible food follows the Council's existing collection policy at both
choice generation and execution. Under hungry-members-first access, a healthy
loader can take only the shared surplus above the protected serving reserve.
The same allowance determines ordinary collection permission and caps a bulk
cart load. Hungry adults retain their existing access; personal food and
non-food cargo keep their ordinary loading rules.

Crafting uses the Blacksmith's handcart recipe with the adult worker's carried
wood, fittings and rope. An adult collects them from household or Town
Warehouse stock only while the whole set is carried or in that stock, and the
Blacksmith, Store and workstation supply hauls leave the carried set with them
rather than returning it to stock. Exact inputs are reserved through the
production job; the personal cart appears on the work site's ground after
completion. Repair
consumes three carried material reservations atomically. Unloading can retain
damaged goods on the ground and works after the cart breaks. Property transfer
and inheritance keep the entire cart/cargo family at its existing position.
A death, break or ownership change removes the attachment without dropping or
teleporting the goods. Owner observation derives cart inspection from those
same saved inventory lots, rather than maintaining a second cargo ledger.

Generic attach, load, pull, park, unload and give controls remain available to
models, but rank below safe idle for the built-in chooser. They do not yet bind
an autonomous delivery task, so selecting them eagerly would repeatedly undo
transfers or pull between arbitrary buildings. Crafting and repair keep their
ordinary priorities; an already attached cart still follows ordinary movement.

## Trees and planting

Each tree is one map resource with one saved growth record
(`EcologyResource`). Growth, harvest, tile inspection and map art all read that
same record: `TreeGrowthRules.StageOf` turns it into the stage the host sends
as `TreeStage`, and the client draws whatever the host sends. Every tree number
lives in `TreeGrowthRules` and is provisional ([#462](https://github.com/compoodment/ClankerWorld/issues/462)).

- **Wood trees** are `sapling`, `mature` or `stump`. Felling a mature tree gives
  wood and one `tree_seed`; the stump regrows in spring. One tree-seed item
  serves broadleaf and conifer.
- **Planting** is the typed `PlantTree` action. It checks, in order, the
  species (broadleaf or conifer only; orchard propagation is still open), that
  the planter is an adult, that the seed lot is a tree seed they own with one
  free, the ground (grass, forest floor or fertile soil; never water, sand,
  rock, snow or dry scrub), buildings, Roads and existing objects, that the
  planter stands on or next to the tile, and the chunk's resource budget. A
  refusal returns a `TreePlantingRefusal` and a one-line reason and changes
  nothing. Success consumes exactly one seed and adds a `planted-tree-{x}-{y}`
  map resource with a sapling growth record, in the same tick.
- **Agents** are offered `plant_tree` while they hold a tree seed. The built-in
  site is the nearest reachable open tile outside every Town border, so trees
  do not block building sites. The species follows the nearest wood tree.
  `replant_tree` also uses a tree seed.
  Replanting selects stumps reachable from the acting agent, including on
  disconnected islands, and skips stumps with no unoccupied route into reach.
- **Orchard trees** are `growing`, `fruiting` or `picked`. Fruit is seasonal in
  `EcologyRules`: it ripens only in the tree's recorded season (autumn for new
  worlds) and falls when that season ends. New worlds start in spring, so
  orchards start without fruit.
- **Saves.** Planted trees are part of the saved map. On load, the map must
  still match regeneration apart from the settlement's staged sites and valid
  planted trees; each planted tree must be a plantable species on legal ground,
  off Roads and buildings, with its growth record. See
  [saves and replay](saves-and-replay.md#current-formats-and-older-worlds).
- **Art.** `UI/Graphics/TreeArtManifest.cs` in the client is the one list of
  tree art: species, stage, asset ID, sprite, source, licence and review
  status. The map reads its sprites and stage names from it. Broadleaf and
  conifer mature, sapling and stump sprites and the three orchard stages are
  approved art from the October 1 review; the tree-seed item has no art yet. The
  [pixel-art style guide](art-style.md) explains how art is reviewed.
- **Logs.** The host logs `tree_planting` outcomes (planted, refused,
  replanted, seed collected) with the agent ID and a bounded detail.

## Advanced generation controls

New World defaults to 50% water with a 20–80% range. `GenerationAmount`
controls forest cover, mountain relief and river abundance independently;
Normal is zero and omitted from saved JSON. Low/High use forest rainfall
thresholds 175/125 and river catchment thresholds 288/72. Mountain relief sets
how much dry land the [massifs](#mountain-massifs) aim to cover: Low 2.5–3.5%,
Normal 7.5–9.5% and High 14–17%, for every climate mode and size. Outside the
visibility trial, Normal keeps a 150 rainfall threshold and river threshold 144.
Resource abundance retains its existing Sparse/Normal/Abundant saved values;
the UI labels them Low/Normal/High.

For Balanced Small/Medium worlds, each feature's target applies only while its
own control is Normal. The versioned Normal trial uses a 135 rainfall threshold.
Low and High remain separate controls. `GeographyCandidateSelector` tries at
most three candidates derived from the requested seed. It selects by unmet
target count, normalized distance from the 20–40% forest and 5–12% mountain
dry-land bands, then the largest connected forest region's share as a
tie-break; attempt number is the final stable tie-break. Mountains no longer
take part in the tie-break: they are already whole massifs, and preferring one
large mountain region would always pick the world with the fewest massifs.
Connected regions use diagonal neighbors, east/west wrapping when enabled, and
no north/south wrapping. Incompatible climate modes have no trial target and
use one candidate. The forest tie-break has no minimum region-size threshold.
Coverage is measured and returned for all settings.
An attempt with
no suitable starting clearing is recorded as failed and omitted from coverage
measurement and ranking; other generated-map validation errors still propagate.
Successful attempts keep their original numbers, and selection continues
through the whole bounded set. If no map remains, preview and creation refuse
without replacing the current world.

Owner world-creation signing uses payload v3 to bind all settings and, for
Create, the candidate attempt, terrain and map-layer digests, and explicit
acceptance of unmet trial targets. Preview reports the selected candidate and
coverage for generated candidates and a separate transient `FailedCandidates`
list with each unavailable attempt and its bounded `no-clearing` reason. Godot
shows these together in attempt order without inventing zero coverage for a
failed map. Older previews that omit the list remain readable. An all-failed
HTTP refusal carries a recognized generation code which the client maps to a
fixed, useful message instead of displaying arbitrary server error text.
Create reruns the bounded selector, checks those signed identities, and builds
the world from its selected map. It refuses a changed preview or an unaccepted
miss. The selected attempt is saved in
`GeographyOptions` with the visibility algorithm version and in the map
manifest; restore regenerates that attempt strictly without searching again.
The transient reports do not change signed creation or saved map authority.
Terrain version 2 adds mountain massifs and applies to every generated world,
whatever its climate mode. Any other saved version is refused rather than
replayed with different terrain rules (see
[saves and replay](saves-and-replay.md#current-formats-and-older-worlds)). Small
and Medium remain the only playable sizes; no continent-count control is exposed
for them. Existing saved water settings are
not rewritten.

## Skills and practical lessons

Agents start with no skills, including added adults and newborn children.
Finishing construction first records building; finishing a Farmhouse recipe
records farming, a Blacksmith recipe records smithing, and other production
records crafting. The record keeps the first learning time. Learning by work
has no teacher; completing an accepted lesson records the teacher's agent ID.

Lesson candidates use saved skills and the existing food/warmth readiness
rules, independently of work roles. A mentor cannot be working on an active
project, handling another social decision, or reserved for another lesson.
Both participants must be able to reach the common lesson site when a request
is offered or executed and when the teacher accepts. Mentor selection keeps
its stable identity order but skips adults without a physical route, so an
isolated teacher cannot hide another available teacher. Ordinary route checks
include occupied tiles; they do not grant travel through disconnected land.
Request, refusal, acceptance, cancellation and pause/reload retain their normal
flow. A completed lesson changes neither the agent's role nor work proficiency.
Stored skills currently affect only teaching availability, never ordinary
action access or work speed. The agent card and Event Log describe the record.
The owner observation keeps its existing JSON `role` field for the lesson's
skill name so older clients can still display it; new code calls it `Skill`.
This is separate from the saved lesson record, which now stores a skill.

## World-list requests

The private host can retain an untouched, nongenerated bootstrap checkpoint
before the player creates a world. Its founder observation reports
`requiresWorldCreation`; this is derived from the saved state, independent of
whether the response includes cached terrain. Continue and Load World route
that observation into the ordinary New World screen before entering play or
resuming time. Preview selection, explicit acceptance of missed coverage
targets and signed creation remain the only way through that screen. The
redirect neither replaces the checkpoint nor removes its catalog entry, and
founders or authored progress prevent it.

New World preview refreshes wait for both an existing preview and a pending
owner action to finish. Menu visibility, observation generation and preview
revision still fence the waiting work, so only the current options are
requested and closing or switching screens discards the old refresh.

Continue also belongs to the current Main Menu navigation. Opening Settings,
New World or Load World, returning to Main Menu, or starting another Continue
expires the earlier entry attempt. Its late refresh can update observations,
but cannot enter play or resume time behind another menu. A fresh Continue
after returning still enters normally.

The manual Save World and Load Save dialog owns one list read per opening.
Closing it, creating, overwriting or deleting a save, or starting another opening
cancels the previous read. Late success and failure replies cannot replace current rows,
selection or status, including across a world or pairing change. Clearing or
rebuilding rows recomputes Load, Overwrite and Delete availability. Creating a
new save remains available while listing is slow.

Load World keeps a visible checking state until its signed catalog request
finishes. Back cancels the client request; a late response cannot overwrite a
newer list or New World screen. Results trigger layout after population so the
first opening can display them. The host checks inactive checkpoints once per
unchanged file in each process, then reuses only that structural result. It
hashes the file bytes to notice replacements and still checks required history
and model configuration every time; selecting a world performs a fresh restore.
When a private host starts, a background task makes those checks for every
inactive world, so the first list can reuse them. It holds the world-mutation
lock only to read the catalog and each file, never while decoding or restoring,
and a world it cannot check is left for the list to report as usual.
Open captures the chosen world's ID before
pausing, so a later catalog refresh cannot change its target. Open, Create and
Delete share the owner-action gate; selecting a different row cannot re-enable
Open or Delete until the current action finishes. Cleanup checks the current
selection rather than a row retained across an await.

Client cancellation stops the host between world assessments. It does not
interrupt one restore already holding the mutation lock.

World Settings reads autosaves for its current opening, paired device and
observed world. Closing Settings, choosing Game Settings or changing worlds
invalidates the read and disables Apply. Late replies and failures cannot
replace a newer opening's controls. A reply must name the observed world
before Apply becomes available, and Apply checks that context again before
sending the displayed interval and rotation.

The host checks rotating autosaves after an advancing tick. A UTC sample older
than the schedule's anchor resets and persists that anchor before checking the
interval. It keeps the last saved world tick and creates no copy at the reset;
the next due save still requires an advancing world. Disabled schedules return
before any write, and a clock reset checks the world identity before persisting.

## Animals and horse travel

The built-in animal package adds a household animal yard and the egg, milk, wool,
leather, leather-sack and saddle recipes to their actual House, Restaurant and
Tailor stations, including larger Restaurant and Tailor variants. Generated
worlds seed small wild groups near public forage and fresh water. Each animal
has an authoritative identity and location; it never draws from private crops
or household stock while wild.

Adults tame, care, collect, supply, lead, saddle, mount and dismount through
ordinary revalidated choices. Native orders bind an exact animal name or ID.
Care spends actual unreserved grain/greens and jug water at the animal or yard;
food, planting and workstation reserves stay protected. For care away from the
actor's tile, input selection accepts only physically carried supplies. This
keeps the existing supply path collecting both feed and a water jug before
approaching the animal, including after a partial pickup. At the animal's tile,
permitted local yard stock remains usable directly. Physical supply trips
retain the owning household. One held product batch waits for local collection;
milk enters a reusable household jug. Products then use ordinary stock hauling,
recipes and trade.

When care is due, its native order candidate requires complete safe inputs, a current care
supply trip or an obtainable supply. Missing inputs use the existing blocked
order status with a reason naming feed and jug water. Candidate and blocker queries
change no state, and the existing order schedule resumes the task when its
physical supply path becomes available.

Cared adult pairs breed automatically when their yard has a place and delivered
supplies cover existing animals and the offspring. Pregnancy reserves one place;
young animals and reservations count toward eight per household or wild herd.
Missed care pauses progress. Only old age kills animals; an owned sheep, cow or
horse leaves one household hide at its actual death position. Untamed animals
leave one hide bound to their remains until a nearby adult collects it; this
does not grant access to household-owned hides. Abandonment does
not turn private animals into public salvage.

Animal gifts, sales and named outsider permissions need fresh, admitted personal
model choices. Built-in fallback and forced orders cannot give personal consent.
Receiving space and the exact payment lot are checked again on acceptance;
transfer clears old permissions and preserves position for physical leading.

A cared adult horse has one real reserved saddle and one adult rider. Movement
uses the normal legal route and occupancy rules, halves walking cost and admits
at most two legal steps per tick. A mount adds eight cargo units. Care or
permission loss ends riding; dismount puts unreserved excess cargo at the actual
position without changing its owner. Cart attachments and boat travel exclude
ridden or led animals. Godot projects and draws the authoritative animal state
with young/adult headings, mounted horses, yard art, inspection and event text.

Milk stock travels as an actual household jug to a held Store or borrowed Market
stall, with a stock receipt at a stall. Fresh seller and buyer personal choices
exchange one held portion into the buyer's real carried jug for the named
personal payment. Each pending offer has a distinct milk source lot, even
when the lot contains several available portions. Candidate generation and
native admission both enforce this existing save invariant. Closing an offer
releases its exact reservation and makes any remaining usable stock available
for another sale. Both jug owners stay the same. Expiry, refusal and changed
custody release held milk; spoiled milk can be emptied locally without removing
the vessel or its fresh contents.
