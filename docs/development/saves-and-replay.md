---
title: Saves and replay
type: persistence-reference
status: active
updated: 2026-10-06
---

# Saves and replay

This page owns save implementation and recovery requirements. The
[player guide](../playing.md#save-and-return) explains the controls;
[game design](../game-design/saves.md) owns the intended experience.
[Releasing](releasing.md) owns version selection and rollback gates.

## Separate state by ownership

World state includes its seed and generation options, clock, agents, accepted
events, Towns, content locks, model/slot assignments and autosave choices.
Installation state includes device authority, provider credentials and usage
accounting. Saves store slot IDs and model choices, never API-key bytes.

The October 1 terrain tuning changes deterministic generation for new worlds.
Loading retains the saved map and current weather episode; it does not replace
either with a freshly generated map or a new weather roll. Earlier alpha maps
may fail the existing regeneration checks and are refused and preserved; no
terrain migration is provided. New saves with default weather store a null
profile list to select the reduced built-in preset. Explicit profile lists
keep their configured weather weights.

Private-world schema 60 adds exclusive physical handcart attachments. A cart
and its cargo are existing inventory lot relationships, with the cart's ground
position retained while pulled or parked. Loading verifies one cart per puller,
one puller per cart, living ownership, condition and shared position. In-flight
crafting and repair inputs use the normal exact inventory reservations. Current
roundtrips retain loaded parked carts and mid-journey hitches; rollback retains
the previous physical position and every cargo quantity. Earlier alpha saves
are refused and preserved; no migration is added.

Private-world schema 31 records an agent's learned skills and each lesson's
skill instead of a work role. Skills retain their first learning time and
optional teacher ID, including in deceased profiles. Loading validates those
references and times, and rejects null entries in living or deceased skill
lists as damaged checkpoint data. Current lesson progress and skills survive pause,
save/load and replay. Schema 32 adds saved household requests and recent refusals
for adults without an authorized home. These states are validated and survive
save/load and replay. Older alpha lesson records need not load; no migration is
provided. Saved skills grant no ordinary action permissions or speed bonus.

Private-world schema 34 adds household field tiles and their crop/work state.
The saved inventory also records a ground position for physical harvest lots.
Field ownership, work inputs, growth times and replanting reservations are
validated together with inventory and map geometry. Current-format roundtrips
retain intermediate work, carried deliveries and planting reserves. Fertility
is derived from the seed and immutable map layers rather than saved per tile.
Older alpha saves need not load; no field or orchard migration is provided.

Schema 37 adds authoritative garment and carrying-aid lot IDs and timed repair
work, with exact material reservations. Loading checks the selected goods are
single, unreserved units physically carried by their recorded owner; repair
work also needs its private work site and exact live inputs. Broken gear and
existing overloads remain valid property. Carry capacity and weather protection
are derived from the selected lots rather than saved as a second authority.
An unpaid household recipe may also keep a paused plan that requires a fresh
choice after its inputs become unavailable; validation only accepts that flag
on a paused project without a linked job. Older schemas carrying equipment
records are refused. No migration is added.

Private-world schema 38 retains a building's definition identity in completed
or cancelled expansion history after an owner removes that building. A
building with active production or expansion work cannot be removed or
reassigned, and a household's last Farmhouse stays assigned until its field
work finishes. Town membership, building assignments and physical inventory
locations are validated together; older alpha saves need not load and no
migration is provided.

Schema 46 adds the low-population continuity rule: whether it was on at the
last check, so its Event Log transitions are not repeated after loading, and
each eligible couple's deadline for saying "not yet" to a child. A parenthood
plan may also be in the new `postponed` stage. Loading refuses a checkpoint
without this state, couples while the rule is off, unknown or unordered
partner IDs, duplicate couples, and a deadline more than two world days after
the saved clock. Current-format roundtrips keep the flag and deadlines, and a
replay from a postponed plan reaches the same plan and checkpoint. Older
schemas are refused. No migration is added.

Private-world schema 48 adds independent saved Town governance. It records the
current council and fallback cause, term/retry schedules, personal full-term or
remainder-term candidacy agreements, proposal identity/windows/final votes,
current election and settled history, ballot revisions, runoff eligibility,
recorded fair draw order, posted notices and actor-owned read/relay receipts.
A founded Town missing its governance, duplicate votes, unsupported winners,
invalid civic references or inconsistent proposal thresholds are refused as
bad checkpoint data. Founder setup may have no civic state until Start World.

Current-format roundtrips preserve windows and accepted choices, including
paused proposals, independent two-Town decisions and election ballots. Prepared
ticks rejected before commit leave no civic change. Delayed replies revalidate
the exact current contest/proposal and actor authority; cancelled votes cannot
revive after owner membership changes. Draws use a named world-local PCG stream
with unbiased selection and save the actual order, so loading does not reroll
an accepted outcome. Older alpha saves are visibly refused and preserved; no
migration is provided. A passed admission changes Town membership at most once
(schema 54); it never grants stock or household access.

Private-world schema 49 stores recognized food-order targets, progress, retry
state, cancellation receipts and their exact actor/world identity alongside the
original owner instructions. Loading validates these records together so an
unrelated action or a stale order cannot advance a replacement task. Alpha saves
must use the current checkpoint schema; older saves are refused without
migration and remain unchanged.

Food consumption progress keeps a fixed-length SHA-256 receipt derived from
the actual consumption's world time, actor and complete lot identity. Valid
inventory splits can lengthen that lot identity without lengthening the saved
order receipt. A completed order retains its progress through reload and
cannot consume or credit the same completed task again.

The last accepted model choice keeps the complete offered candidate ID, just
like the current intention. Inventory-backed actions may exceed 512 characters;
this historical ID has no separate length limit and is never truncated or
replaced with a different action. Loading still refuses blank IDs, control
characters, invalid statuses and inconsistent acceptance times. A later model
failure keeps the last accepted choice independently of its safe fallback.

Private-world schema 65 adds material-gathering orders with a distinct
`TargetMaterialKind`, exact optional source or position, and progress measured
in harvest batches or material items. Saved progress and the last physical
harvest receipt are validated together; mixed food/material targets and invalid
material kinds are refused. Queueing, cancellation, discovery and partial
quantities retain their state across reload. Older alpha saves are refused and
preserved unchanged; no migration is added.

Private-world schema 66 adds `store_material` orders using the same bounded
material target. The destination is the current household House, so source,
food, guardian and coordinate target fields are refused. Progress counts storage
loads or exact item quantities and requires a committed personal-relocation receipt
with a fixed-length identity, even when the inventory lot identifier is long.
Replay preserves partial storage, queued work and cancellation without moving
goods again. Older alpha saves are refused and preserved without migration.

Private-world schema 67 adds `collect_material` orders for the same material
catalogue. Their source is selected through ordinary personal-goods collection
rules; schema 74 adds an optional coordinate constraint below. Food, guardian
and resource identity targets remain invalid. Progress
counts collected loads or exact item quantities, and a bounded committed-move
receipt prevents replay from duplicating pickup. Unavailable goods and full
carrying space preserve the remaining task. Older alpha saves are refused and
preserved without migration.

Private-world schema 69 adds `repair_equipment` orders with a bounded
`TargetEquipmentKind` and progress counted in finished repairs. The equipment
work record has an optional `OrderInstructionId`, which must refer to that
actor's active repair task and match the actual lot kind. Its saved work counter
and material reservations retain their ordinary validation. Completion credits
a bounded receipt only after the real repair consumes its inputs. Cancelled or
replaced orders cannot retain live repair reservations. Replay covers partial
work, queues, cancellation and exact material costs. Older alpha saves are
refused and preserved without migration.

Private-world schema 86 introduces crude House tool behavior. The two new
item kinds keep ownership, physical custody, condition and reservations in
ordinary inventory lots; their recipes use existing saved projects and
production jobs. Current saves resume supply, paid work and tool use without
duplicating output or wear. The separate House-tools content package keeps
the existing House identity. Older alpha checkpoints are refused and left
unchanged; there is no migration or inferred tool history.

Schema 53 saves wills with several heirs. An estate keeps its household
default beneficiaries and adds, for an accepted will, the named heirs in order,
the split, the exact quantity of each frozen lot each heir receives, and any
final words. Frozen lots retain their original building storage when present,
so escrow and communal inheritance cannot claim an unrelated House as storage.
The deceased archive records the Town the agent lived in. Loading
checks that only an accepted will has heirs and a division, that the division
covers every frozen lot exactly with no other lots, that a held vessel's
contents go to the vessel's heir, that person heirs are known agents and Town
heirs existing Towns, and that final words are already normalized. Final words
become private memories only at settlement, so a current-format save taken
between the will and settlement replays the same transfers and memories. Older
schemas carrying these records are refused; no migration is added.

Schema 58 adds exact land-claim coordinates to Council proposals. A passed
claim and its title record must agree on Town, tiles and settlement time;
loading refuses a claim title without approval or a passed claim without its
title. Pending, refused and cancelled claims hold no title. Proposal identity
uses ordered coordinates, and title identity is derived from the proposal ID.
Prepared-tick rollback removes votes, titles and border changes together;
continuing a current-format checkpoint preserves the vote window and cannot
apply an accepted claim twice. Older alpha schemas are refused and preserved;
no migration is provided.

Schema 59 adds household land request status, separate adult consent, the
Council proposal link and the adult roster at grant settlement. A grant's
original rights receipt must retain matching plot coverage, grant time and agreed end date;
loading rejects missing approval or consent evidence and orphaned Council
land-use proposals. Closed requests remain history without competing claims.
Prepared-tick rollback and current-format reload preserve approval progress and
commit the final grant once. Older alpha schemas are refused without migration.

Private-world schema 72 adds field orders and their optional `TargetCropKind`;
schema 77 adds an optional exact tile below.
`FarmFieldWork.OrderInstructionId` binds work to its actor's active field order.
Restoration validates that link, action, crop, work time and ordinary seed/tool
state; a cancelled, queued, unrelated or missing instruction cannot retain bound
work. Only finished work earns a bounded receipt and one completed field. Replay
covers partial planting, queues, released seeds, real tool wear and household
harvest ownership. This version follows the integrated guardian-placement schema 71. Older alpha saves
are refused and preserved unchanged without migration.

Private-world schema 73 adds `repair_tool` orders using the existing
`TargetEquipmentKind` field. Validation restricts the action to supported tool
kinds and repair counts. Only a completed inventory repair earns a bounded
`repair:tool:` receipt. Replay covers partly completed quantities, preparation,
queues and cancellation without charging the materials twice. This version
follows integrated field-order schema 72. Older alpha saves are refused and preserved
unchanged without migration.

Private-world schema 74 allows an optional `TargetPosition` on collection orders.
Coordinates keep the existing bounded integer validation; an off-map target is
a valid instruction that waits with a reason. The exact tile survives queued
work, travel, partial pickup, cancellation and reload. Runtime selection and
execution both recheck the lot's current position along with ordinary personal
collection permissions, so moved or depleted goods cannot redirect the order.
This version follows integrated tool-repair schema 73. Older alpha saves are
refused and preserved unchanged without migration.

Schema 76 saves each Town's land-hearing ledger: plot and right-version notice
revisions, affected parties, public evidence and provenance, explicit responses,
actual file reads, case-only candidate consent and elections, adjudicator terms,
rulings and rehearing assessments. Notice publication does not become a receipt,
and a read records the evidence and rehearing requests actually seen. Original
grant rights remain available for the Council receipt checks; versioned bounded
adjustments reproduce current permissions without destroying that receipt.
Request-resolution pointers identify the ruling and exact tiles decided, leaving
only unresolved portions as competing claims. Loading validates the case's notice,
source, read, authority and adjustment links rather than inventing missing evidence.
Notice party snapshots remain historical; current response standing is derived
from the captured world, while each ruling retains its actual closure parties.

The same ledger saves voluntary transfer requests with immutable exact plots,
right versions, published party rosters and terms. Actual notice receipts remain
separate from individual accept or decline responses and their contemporaneous
household rosters. A completed transfer retains its final adult rosters and
exact permission-adjustment receipt. Loading validates that completion against
every required adult's informed acceptance and the unchanged original grant terms; incomplete,
declined, withdrawn or invalidated transfers cannot carry a completed adjustment.
Pending consent requirements are derived from living adults, so a new adult must
personally accept before completion. These records preserve original Council
grant receipts without transferring title or physical property.

These records share prepared-tick rollback with permissions, requests, civic
receipts and events. Current-format reload and continuation retain pending
windows, rulings and transfers without applying a decision twice. Reopening
preserves the earlier ruling and current rights until a new correction is committed. Older
alpha schemas are refused and preserved without migration.



Private-world schema 77 allows the existing bounded `TargetPosition` on field
orders. A saved running field job must be at that tile as well as matching the
order's actor, action and crop. Queueing, partial work, exact progress and seed
reservations survive pause and reload; a rejected tick cannot leave work or
progress behind. An unavailable target waits without selecting another field.
Older alpha saves are refused and preserved unchanged without migration.

Private-world schema 80 adds `collect_food` orders using the existing
`TargetFoodKind` and optional `TargetPosition` fields. Food targets are generic
or one of berries, fruit, wild greens, cultivated greens and the seven named
prepared foods. The named-food catalogue extends the same fields and receipts;
raw grain and potatoes remain invalid food targets. They use
`food_items` for exact quantities or `collection_loads` for default pickups,
with the same bounded `collect:personal:` receipts as material collection.
Validation refuses mixed material, equipment, crop or resource targets, wrong
progress units and unearned receipts. Queues, interruptions, cancellation and
partial pickups retain their state across replay and rollback. This version
follows admission schema 78 and field-location schema 77. Older alpha
checkpoints are refused and preserved without migration.

Private-world schema 82 adds `collect_equipment` using the existing exact
`TargetEquipmentKind` and optional source tile. Only the five supported garment
and carrying-aid kinds or 13 tool kinds are accepted. These tasks use
`equipment_items` for explicit quantities or `collection_loads` for default
pickups, with bounded personal-pickup receipts. Validation refuses mixed target
categories, unsupported equipment, wrong units and unearned progress. Physical
condition, source, queued work, remaining quantity and cancellation survive
reload and rollback. This version follows Council-election schema 81, food-collection schema 80
and lantern schema 79. Older alpha checkpoints are refused and preserved
without migration.

Private-world schema 83 adds `store_equipment` with the existing exact
`TargetEquipmentKind`. Equipment storage permits the five garment/carry-aid
kinds and 13 tool kinds, with `equipment_items` for explicit quantities or
`storage_loads` for default tasks. It uses bounded personal-storage receipts.
Validation refuses mixed targets, locations, unsupported kinds, wrong units
and unearned progress. Partial storage, queued work, cancellation and equipment
condition survive reload and rollback. This version follows equipment-collection
schema 82, Council-election schema 81, food schema 80 and lantern schema 79.
Older alpha checkpoints are refused and preserved without migration.

Private-world schema 85 adds `produce_item` orders with an exact recipe and
output kind, an optional requested site, and the selected building, project
start and production-job identities. A production job's optional
`OrderInstructionId` records which order started it; matching a recipe and
start time alone cannot adopt an unrelated job. Progress counts finished output
items or whole batches. Loading validates recipe yields, cross-record ownership and
job references, pause state and completion receipts together. Queued work,
partial progress, cancellation and survival pauses retain their state through
reload and replay. This version follows marriage schema 84 and equipment-storage schema 83; older alpha checkpoints are refused and preserved
without migration.

Private-world schema 87 adds exact personal-goods and borrowed-return orders.
`TargetItemKind` is separate from gathering and repair subjects. Storage and
return tasks save the selected House identity, household owner and position;
returns also save their source lot. The destination binding is complete or absent,
and incompatible actions, subjects, quantities and receipts are refused.
Finite progress, queued work and cancellation survive reload without switching
House or changing ownership. This version follows crude House tools schema 86 and production-order schema 85; older alpha checkpoints are refused and preserved
without migration.

Private-world schema 88 adds `deliver_stock` with exact purpose, item and
building kinds. Optional `DeliveryRoute`, `DeliveryLotId` and `DeliveryQuantity`
retain the selected native route and active shipment. The destination reuses
the complete building/owner/position binding; its owner may be the receiving
household or Town according to the route. The active lot and quantity are
paired, and final-delivery receipts use a distinct bounded identity. Loading
validates the action's subject, route and binding together. Queued tasks,
partially completed quantities, carried shipments and cancellation survive
reload without crediting earlier pickup or redirecting goods. This version follows
custody-order schema 87; older alpha
checkpoints are refused and preserved without migration.

Private-world schema 89 adds construction and expansion order bindings.
Construction keeps its exact definition, household, site, project start and
unique building identity, with retained completion evidence for the actual
paid placement. Expansion keeps the original building identity, owner,
position and revision, the selected target anchor and footprint, and its
order-owned expansion job. Validation checks these links rather than
inferring completion from a building that happens to exist. Completed work
survives later building removal; unfinished work, cancellation and survival
pauses remain replayable. This version follows delivery-order schema 88. Older alpha checkpoints are
refused and preserved without migration.

Private-world schema 90 adds shelter target bindings and completion records.
Building targets pin identity, definition, owner, anchor and placement time;
natural cover pins its actual destination. Shelter completion records the
arrival time and position. Fire completion also references the unique actual
completed one-wood fuel reservation. Validation checks action-specific shapes
and payment evidence without requiring historical cover, permission or a fire
to remain available forever. Unfinished targets are revalidated when work
continues. This version follows building-order schema 89; older alpha checkpoints are
refused and preserved without migration.

Schema 91 adds the required non-land case ledger, protected non-land mandate
scope and retained-term extension receipts. It saves exact allegations,
historical conduct and law context, actual observation and communication
sources, notice and response history, independent adjudicator authority,
findings, voluntary offers and each contributor's consent. Completion effects
refer to native physical receipts rather than mutable stock or the event log.
Loading rejects missing, duplicate, future or inconsistent evidence and
authority links. Historical records remain meaningful after law changes,
departure, death, consumption or removal of the physical target. Prepared-tick
rollback includes these ledgers alongside the actual goods or work. Earlier
alpha checkpoints are refused and preserved; no inferred case history or
migration is added.

Schema 33 records a child's personal-model role at birth, provider endpoint, model
ID, installation-local key-slot ID for a hosted model, and selection reason.
Matching parent assignments are disclosed as agreement; when they differ, the
parent who began the family plan is the tie-break. The provider store preserves that
choice if its key is unavailable after moving a save or deleting a key, so the
child idles on built-in choices until setup is restored rather than using a
different paid model. A child without an explicit parental model remains
unconfigured, and worlds without a saved birth choice do not acquire one by
inference. An owner's later personal-model choice, including an explicit
no-personal-model setting, is saved separately from the historical birth
choice. That setting survives ticks and reloads; an explicitly unconfigured
child uses built-in choices instead of the world default. Infants make no
personal-model calls; normal presence, budget and admission checks still apply
after infancy. Living and deceased profiles validate the descriptor against its
recorded birth and schema. While provider storage is being recovered, the Model
panel keeps showing the selected provider and model with a setup message.
Birth bindings and owner role overrides retain the complete generated
inhabitant ID. A valid long-seed newborn can bind both roles, including when
reloading a birth saved before provider storage recovered; no identity is
truncated or replaced. Repeated recovery preserves later owner overrides and
the separate historical birth descriptor.

Schema 50 saves an unresolved dependent-guardian search, including its current
relative, household or Town stage, start tick and offered adults. Each stage
keeps the earlier groups, and the offers are brought up to date at the end of
every tick, so a save always matches the households it was made with. Acceptance is
an explicit adult action that changes the saved current primary caregiver.
The original search format placed a child only in a completed House with room
in the same Town. Schema 71 adds the pending physical placement described below.
Loading validates the search stage, times and adult references. Replaying
from a pending request reaches the same acceptance opportunities and preserves
the single guardian-needed event. Older alpha saves without this state are
refused; no migration is added.

Schema 71 saves a dependent's pending guardian placement separately from the
accepted care relationship. It records the caregiver, exact relationship and
revision, start time, collecting or escorting stage, selected household and
Town, and the House's instance, definition, placement time and anchor. A
blocker may preserve accepted care while a home or route is unavailable. These
records grant no membership, reserve no House place and never substitute a
teleport for movement.

Loading checks the care authority, references, stage and saved destination as
one placement. A House that disappeared or changed ownership can leave a
pending destination; the runtime refreshes it rather than discarding accepted
care. The normal runtime rechecks availability and capacity on retry and
arrival. Completion changes the child's household and Town together and
clears the pending record; cancellation also clears it without changing birth
history. Save/load retains intermediate travel and blockers, and replay must
reach the same membership, position and lifecycle events. Earlier alpha saves
are refused and preserved; no migration is added.

The private catalog archives each world's checkpoint. It saves the active world
before a paused switch and keeps world IDs, names, seed and settings separate.
Creation/selection require signed owner requests and leave the selection paused.
An interrupted catalog update is recovered against the active checkpoint before
the server starts hosted services or accepts requests.
A corrupt leftover checkpoint encountered during deletion keeps cleanup pending
without preventing the healthy active world from starting. Unverified files and
the deletion intent are preserved; later recovery can finish after the file is repaired.
The paired authority identity belongs to the installation, not the selected
simulation world.

Load World checks each inactive checkpoint against the current runtime before
marking it compatible. The host keeps only a process-local structural verdict
and history head for each catalog identity, keyed by a SHA-256 hash of the
checkpoint bytes. File replacement, catalog-identity changes and a host restart
force a new structural check. Every list still verifies the referenced history
chain and current model credentials;
selection reads and restores the checkpoint again before changing worlds.
Checking a changed checkpoint can therefore still be slow, especially on a
large generated map, while repeated unchanged lists avoid rebuilding worlds.
After a host start, a background task makes the structural check for each
inactive checkpoint, so the first list can reuse it; it reads checkpoints only
and changes no save.

Manual load shares the world-mutation lock with world selection. Its pause/world
checks, checkpoint restore, routing/autosave restore and rollback finish before
selection can archive the active world. This serializes concurrent operations; it
does not add a cross-file crash journal.

Founder placement restores the prior in-memory world and provider configuration
if its checkpoint commit fails. World selection and founder/add-agent setup
share a transaction lock while restoring checkpoint and model routing. This
handles ordinary operation failures; process termination between separate
files and a second failure during rollback still require operator recovery.
It does not make every owner endpoint a multi-file transaction.

Start World persists its completed setup while time is still paused, then resumes.
A failed checkpoint write restores the paused pre-start setup so a fresh signed
retry can succeed after storage recovers.

Windows provider configuration uses current-user DPAPI. Validated legacy JSON
migrates atomically to the protected envelope; damaged or wrong-user data is
not overwritten with empty state. Unix uses private permissions. Forgetting a
key removes it from the current store, not backups or the provider account.
Protected Windows files are not portable key exports. This is protection at
rest, not protection against software already running as the same user.

Private-world schema 55 adds each founded Town's laws and government record.
Law versions bind adoption, amendment and repeal to passed Council proposals,
with original scope and effective dates. Resident changes save original and
remaining electorates, final votes, queues, approval and handover deadlines.
Mayoral records save consent for specific mandates, every completed/interrupted
round, repeated top ties, retries, winners and separate land/ordinary terms.

Loading rejects unsupported arrangements, invented majorities, changed opening
rosters, malformed ballots, unsupported winners, overlapping mandates and terms
without a completed election. The approved arrangement must follow a completed
resident handover. Current-format saves preserve pending windows, accepted
choices and distinct mandates. Paused and rejected ticks do not advance or
partly apply civic work; replay does not reroll ties or duplicate authority.
Older alpha checkpoints are refused and preserved; no migration is added.

Private-world schema 81 records the exact initial Council election forced by a
protected government change, or an explicit null when it forced none. The field
is required in the current format. Loading rejects missing or cross-Town
elections, duplicate ownership, unrelated renewal/replacement elections and
links inconsistent with approval or settlement. A runoff keeps the same
identity while its current round opening moves forward. Current-format replay
preserves forced attempts and ordinary elections independently; old alpha
schemas are refused and preserved without migration or inferred ownership.

## Commit and restore rules

Canonical state is the accepted state used by the simulation. A replay checks
that recorded events reproduce its expected results and digests.

- Stage tick changes before accepting them; rejected work leaves no partial state.
- Version and atomically replace saves after committed changes.
- Validate externally loaded state and map identity before accepting it.
- Reject unsupported, mismatched or corrupt data visibly; preserve the original
  file and valid current world instead of silently substituting content or keys.
- Include replay coverage when state, events, schemas, generation or replay
  semantics change, so a save in the current format still loads and replays.
- During alpha, older saves do not have to keep loading, and no migration or
  old-save handling is written only to keep one working. The rule above still
  applies: a save that cannot load is refused with a reason and kept. Finished
  releases promise forward migration later, as described in
  [Saves](../game-design/saves.md). The cutoff and removal audit are recorded in
  [issue #487](https://github.com/compoodment/ClankerWorld/issues/487).
- Keep build revision, release labels and telemetry out of canonical digests.
- Never infer compatibility merely from the public game version or file age.

The private-world checkpoint stores the conversation cursor (revision, status,
next speaker and consent/interruption state) together with the full admitted
history: up to six accepted public turns and one accepted wrap-up, including
the actual listener IDs for each. Each later speaker receives the committed
public history so far, not an unaccepted reply; the wrap-up request receives
the six public turns. Each participant's current-day allowance is saved too.
A live provider request is never saved. On restore, an accepted unfinished
conversation becomes suspended and cannot spend again until both participants
make fresh resume choices, including when a saved suspension contained one
person's earlier choice. A pending invitation keeps its original deadline and
requires normal acceptance, which counts against the invitee's daily allowance.
Conversation records use private-world schema 35, following schema 34's fields
and ground harvest lots. No migration for older alpha saves is added solely to preserve
compatibility.

Private-world schema 84 adds required marriage records and conversation kinds.
Each marriage retains its accepted partnership snapshot and the ordinary
conversation's separate mutual marriage consent. Its surname session admits
at most four alternating choices from the original surnames that fit both
accepted names within the existing 48-character limit. Later player renames
cannot invalidate those choices while the session is unfinished; loading
refuses such a checkpoint too. Marriage eligibility and saved acceptance names
also check the limit after Unicode normalization, so an accepted receipt cannot
have an empty surname choice list. Invalid replies, failed calls, pause and
cancellation admit no turn. A completed receipt records the result, completion
time and whether the seeded draw was used; validation rechecks the draw against the seed and consent identity.
Receipts survive ordinary conversation compaction, including a pending session
closed because a participant became unavailable. Active unfinished sessions
still require both partners' fresh resume choices after loading. Later player
surname changes retain the original result and record the latest player
change separately; validation requires both spouse names to match the current
surname. Tick rollback keeps both names, consent and surname history unchanged
and can admit the completed reply later without issuing the call again.
Marriage schema 84 follows equipment-storage schema 83. Older alpha files
are refused and preserved without migration.

Routine history compaction validates the compacted checkpoint without applying
load transitions. It preserves the live conversation cursor, consent and pending
turn admission identity. Resume with compaction likewise keeps the conversation's
existing pause state; only an actual load suspends it as restored and clears
previous resume choices. A failed checkpoint write leaves the live state unchanged.

Private-world schema 40 saves each inhabitant's explicit domestic family unit
and primary caregiver, plus the caregiver, intended home and actual birth home
for an agreed parenthood plan. Birth records retain the caregiver and actual
household. House resident counts and limits are derived from active household
members, recorded family units and the completed building footprint; they are
not separately mutable counters. Current-format roundtrips preserve pending
family decisions and births without inferring a family group from ancestry or
household membership. Older alpha checkpoints need not load; no migration is
provided.

Private-world schema 41 records each owner's exact pending message, target,
submission identity, whether a personal model observed it, and its optional
short reply. The one-fresh-decision prompt tick is scheduling state, not a read
receipt. Accepted replies stay attached to the original message ID; pause,
reload or a stale result cannot transfer them to a newer message. Current
schema saves require the instruction and completion records and validate their
target, ordering and tick bounds. Earlier alpha instruction records need not
load; no message migration is provided.

Private-world schema 45 records each physical shop exchange beside its inventory
offer: the shop, holding household, customer, transaction position and time,
item kinds, seller who completed it and any cancellation reason. Inventory
offers retain the exact quantities and lot reservations. Pending exchanges and
Store delivery lots survive save/reload without granting customer access to
private stock. Earlier alpha saves need not load; no shop-state migration is
provided.

Private-world schema 47 records household departures, their original household, care group and once-only food allocation, plus optional physical inventory custody separate from ownership. Personal House storage retains the personal owner even after membership ends. Reload preserves collection rights, borrowed carried goods, the care group and unfinished housing task without awarding another allowance. Paused private jobs retain the original owners, exact input reservations and pause time; a leaver's own cart build is cancelled instead, releasing its carried materials. Their reserved workstation or expansion footprint stays occupied; a remaining authorized member can resume at the physical site with the same materials and remaining duration. Unavailable materials cancel the preserved job and release its remaining commitments. Invalid custody, departure records and unavailable carriers are refused. Older alpha checkpoints are refused and preserved; no migration is added.

Private-world schema 51 lets each living inhabitant record named adult medical
permissions and an active medicine course. The course binds its patient and
caregiver to the actual completed inventory reservation for the consumed dose,
including the supply lot, owner and start time. Strict validation checks
permission, identity and progress; it refuses a mismatched or already closed
receipt. Completed or interrupted courses close that receipt's medical purpose
without releasing or refunding goods, so renewed permission cannot restore a
spent effect. Death cleanup preserves completed receipts while releasing live
claims. Archived physical profiles retain permission history but cannot retain
active treatment. No migration is provided; older alpha saves are refused and
preserved.

Private-world schema 93 adds a caregiver's optional `MedicalSupplyTrip`, naming
an accepted patient, their last-seen map position and the start tick. It keeps
an already started medicine fetch and return trip through reload without
exposing the patient's current distant location or health. Validation requires
a living adult caregiver, an accepted living patient, a map position and a
non-future start time; deceased profiles cannot retain trips. Permission loss,
death, successful treatment or finding the remembered place empty clears the
trip. Supplies remain ordinary inventory, and treatment still consumes one
actual usable dose beside the patient. Earlier alpha saves, including schema
92, are refused and preserved without migration.

Private-world schema 52 adds an optional exact ornament-lot selection to the
existing personal equipment record, without introducing combat equipment or a
second inventory. Current-format checks reject a foreign, reserved, stored or otherwise
ineligible selected unit and preserve intermediate refining, diamond setting,
gifts and barter. Removing or giving the ornament clears the selection while
keeping the actual item; death and estate handling retain the property without
an active selection on an archived profile. Older alpha saves are refused and
preserved; no migration is added.

Private-world schema 54 introduced optional admission records to each Town,
one per passed admission proposal of that Town. The Town resident lists stay
the only record of membership; an admission record says what one approval did to them:

- `approved`: a resident asked for the newcomer, and the approval waits for the
  newcomer to accept before its deadline. It keeps the newcomer's Town at the
  time of the vote, and acceptance is refused if that has changed.
- `admitted`: membership changed. It keeps the Town the newcomer left, if any,
  and the sorted IDs of the newcomer and the dependent children who moved.
- `lapsed`: the approval could not be applied, with the reason `unavailable`,
  `already_resident`, `affiliation_changed`, `joined_elsewhere` or, in schema 78,
  `acceptance_expired`.

Schema 78 gives a sponsored approval one unpaused world day for acceptance. Its
deadline is derived from the saved passed proposal's original `SettledTick` plus
the configured `TicksPerDay`; no separate deadline field is stored. The approval
expires at that tick, and its admission record keeps the lapse reason and actual
settlement tick. Pause, save and reload preserve the remaining world time. An
adult's own passed request still admits them immediately. The owner projection's
optional `AcceptanceDeadlineTick` is derived display data, not checkpoint state.
Earlier alpha saves are refused and preserved without migration.

A passed admission is settled as soon as the council decision or roster change
that passed it is saved, so every passed admission has exactly one record and
reload and replay never apply an approval twice. Loading, and the runtime's own
state check, refuse a passed admission without a record, a record without a
matching passed admission proposal, a repeated proposal, an unknown status or
lapse reason, an unsorted or incomplete moving group, a decision time before the
vote settled or after the saved world time, and an approval still waiting on
someone who is already a resident there. Older alpha saves are refused and
preserved; no migration is added.

Checkpoint decoding enforces declared non-null members and required constructor
fields before runtime validation. A missing society, cognition or inventory
object is invalid data, not an unexpected null-reference fault. No saved list
holds empty entries, so decoding refuses a null entry in any list in the
checkpoint before the typed records are read; the serializer's non-null checks
cover members, not list entries. A saved world size other than Small or Medium
is refused too, since only those sizes can be created. Compatibility
assessment marks that inactive world incompatible while retaining healthy list
entries. Selecting it fails before replacing the active world; the damaged file
stays available for recovery. Optional fields retain their declared defaults.

Signed pause, resume and rename retries persist the requested state before reporting
success, including when the in-memory value already matches after a failed
write. Storage failure remains an error. A paused world's Resume stages the
running checkpoint under the runtime gate, saves it, and only then commits the
running state in memory. A failed write leaves the original pause, epoch and
events untouched; a fresh signed retry can recover without relying on a later
tick. Resume still requires a started world and valid usage allowance. Other
recovery paths do not resume time implicitly.

Before a potentially committed create/select/rewind request, the client invalidates
older observation requests and asks for a fresh baseline. It keeps the last
confirmed view when the host advertises observer timelines; legacy hosts clear
the held observation. If the receipt is lost, reconnect starts from a fresh
baseline while retaining normal regression and terrain-identity checks within
that timeline. Continue does not resume an uncertain world switch.
Other connected devices detect that change through transient observer metadata
and fetch a fresh baseline too, including for same-world rewinds. The runtime
captures that metadata with the committed state; it is never written into a
checkpoint and does not change saved world identity or replay. See
[observer recovery](device-pairing.md#recovering-after-another-device-loads-a-world)
for ordering, cache and retained-request boundaries.

The internally captured proposed tick can reuse its committed map. External
loads still validate and regenerate it; this shortcut must not weaken input
validation. Missing/corrupt referenced history must fail closed. See Issues for
current defects rather than treating these requirements as proof of every load path.

World and manual-checkpoint selection preflight the actual referenced history
before replacing the active save. Save-list metadata alone is not enough to
establish that the checkpoint can load.

## Current formats and older worlds

Generated geography now saves a hydrology revision for new worlds. A missing
or zero revision keeps the previous lake/river algorithm, including historical
resource placement and map identity. Revision 1 is selected by both normal
New World preview and Create World. Existing saves are not regenerated with it;
there is no in-place lake shrink or river rewrite. Older builds need not accept
new-revision worlds.

The terrain rules for sand, groves and hills
([#461](https://github.com/compoodment/ClankerWorld/issues/461)) change the
surfaces, vegetation and resource layout generated from a seed, for every
hydrology revision. There is no generator switch and no migration: a generated
world saved before them no longer matches regeneration and is refused with "The
private-world map does not match deterministic regeneration." Its file is kept.
The save format and schema number do not change. As with any unloadable active
world, the host will not start until that save is moved aside. Hills are drawn
from the saved elevation and water layers, so nothing extra is saved for them.

Mountain massifs ([#683](https://github.com/compoodment/ClankerWorld/issues/683))
change elevation, rivers, climate and resources for every generated world, so
the terrain version saved in `GeographyOptions` (`balancedVisibilityVersion`)
moves from 1 to 2 for all climate modes. Generation and loading accept only the
current version. A world saved with version 1 is refused when its checkpoint is
read, with "This world's map was made by an older terrain generator, before
mountains formed massifs. This build cannot rebuild that map, so the world is
not loaded; its save is kept." The file is left unchanged, the world list marks
it as unable to load, and there is no migration. The save format and schema
number do not change; the hill band still comes from the saved layers.

Private checkpoint v2 stores verified 64×64 terrain-byte chunks. The old v1
per-tile format is refused, and generated maps must match the current generator
and package checks; there is no historical generator or package fallback.
Damaged chunks are rejected without replacing the save. Restore also removes
Road tiles inside validated saved building footprints, leaving other Roads and
state intact. The repair applies once and may expose an already broken Road
connection; it does not reroute Roads or create bridges. Back up older saves
before an upgrade.

Bridges are saved in the same checkpoint as the Road tiles they join, so a
Road never ends at a river without its bridge. Loading checks every bridge
against the map (a legal one- or two-tile river span between buildable banks,
matching its ID and design), refuses overlapping decks, a bridge landing on a
building, resource or camp object, and a Road bridge whose two entrances are
not both Road tiles. It does not need the Town or building that caused the
bridge. Traffic evidence must be recent, within its per-agent bound, for real
unbridged crossings of one or two river tiles, and any open wade must match
where that agent stands, in either water tile of a two-tile crossing. A save
that fails these checks is refused with a reason and kept. Two-tile wading and
its evidence use the existing fields and crossing IDs, so the schema number
does not change. An older build refuses, and keeps, a save that places an
agent, a map memory, an exploration path or traffic evidence in a two-tile
river.

Scouting waypoints record already walked steps, rather than permission to
repeat those steps now. Loading checks each ordered edge against the current
bridge map or against the same terrain with only bridges built strictly before
the outing's `LastOutingTick`. A bridge built on the outing's start tick is
excluded from that older graph because it may have appeared later in the same
tick. Bridges that predate the outing remain, so an impossible sideways step
across an existing deck is not excused by removing it.

For a deceased agent's archived outing, the first graph includes only bridges
built at or before `DeathTick`; a bridge built on a later tick cannot make a
fabricated old step legal. Record bounds, coordinates, times and discovery uniqueness remain
checked. Saving and loading keep the recorded path, visits and discoveries
without clearing them to hide a topology change. Actual scouting and return
movement always use today's bridge map, with its legal axes, detours and
blocked-return behavior. These checks use existing timestamps and add no saved
fields, schema change or migration.

Private-world schema 64 adds a move-out notice to an adult's `Housing` record:
the current household, original notice tick, fixed deadline and selection
reason. A pending request to another household is permitted while that notice
is held. Loading validates the adult, membership, reason and time bounds;
malformed notices and unsupported earlier schemas are refused and preserved.
Live care, family and capacity changes may make a notice obsolete, so the
runtime rechecks them before admission or displacement instead of treating a
stale notice as authority to move someone.

Current-format roundtrips retain notice deadlines, volunteer replacements,
housing requests and the ordinary departure's collection rights and once-only
food allowance. Replacing the selected adult keeps the original notice period;
pause/load, births and unfinished expansion do not restart it. Replay must
produce the same cancellation or departure without duplicating events or goods.
Only completed footprints add resident places. Sole caregivers are protected
from timed displacement even when their dependent lives in another household.
No older-save migration or backfill is added.

Private-world schema 70, society-runtime schema 2 and the standalone
`clankerworld.society/v2` and `clankerworld.society-runtime/v2` envelopes require
`HasChosenName` for every inhabitant. This separates a chosen identity
from a temporary label after automatic naming has ended. Loading rejects a
missing marker, chosen names still awaiting naming, or duplicate normalized
chosen first names across living and deceased inhabitants. Open and closed
placeholders may share first names. Choosing an identical placeholder text is
a real rename; closing automatic naming preserves its unchosen marker.

Current-format save/reload and history compaction preserve these distinctions
and the existing one-retry queue. Child surname checks happen when a chosen
name is admitted, rather than being reconstructed from mutable parent names
on load. Earlier society envelopes and private schemas are refused and their
files preserved; there is no name inference, migration or silent renaming.

The alpha accepts only the current private-world checkpoint schema, currently
`PrivateWorldRuntime.StateSchemaVersion` 94. The minimum supported schema is
the same value, so older alpha checkpoints are refused with a reason and left
unchanged; no private-world migration runs. The current schema also includes
a bounded model-attempt status and exact last accepted model choice per agent, plus
building footprint revisions, reserved expansion jobs, House guest invitations,
learned skills and skill-based lessons, birth-model choices, household fields
with ground harvest lots, bounded conversations with daily allowances, personal
equipment with timed repairs and exact reservations, reusable container lots
with their contents, locations, owners and reservations, explicit domestic
family and caregiver/birth-home records, owner messages with whether a personal
model heard them and any short reply, tool-lot links for saved field and
recipe work, per-agent life-moment identity opportunities with their outcomes,
connected Town-title plots, household use rights and pending use requests,
physical shop exchanges beside their exact inventory offers,
the continuity rule's state with each eligible couple's deadline, food-order
targets, progress, retry state and cancellation receipts, staged
guardian-search records with their offered adults and pending physical
guardian placements, medical permission and
consumed-dose progress, selected personal ornaments, and wills with up to
three named heirs, exact divisions and final words, Town admission records,
Town laws and government, concrete last-meal names for nourishment and
dietary variety, bounded tool-making requests linked to ordinary production
and barter, exact land-claim coordinates on Council proposals, household
land requests with their Council proposal and each adult's consent, physical
knowledge writing with exact material reservations and completed-artifact
receipts, exact-tile movement orders with their destination and arrival
receipt, overcrowding move-out notices, material-gathering targets and receipts,
typed Council-approved Town-project plans, shared construction progress and
exact physical delivery receipts, and paid Market layouts, stall borrowing,
stock receipts and physical barter records.
Land records are checked against the saved map, Towns, households and one
another before load. Building reassignment moves only existing footprint use rights;
connected remainder plots keep their holder and original grant terms. Split
records get deterministic unique IDs, and whole-plot moves retain their IDs.
The building and rights change under the same world lock; rejected or stale
requests change neither. No save-format change or migration is needed.

The food repairs use the existing field, inventory, reservation and delivery
records; they add no migration or new save fields. New planting claims remain
active until the work consumes the seed or an interruption releases it. An
existing current-format finite planting claim may still expire: field
maintenance then cancels the unfinished planting and retains the unconsumed
seed. Reload preserves physical food and vessel locations, exact active
claims and in-flight delivery pointers. Recovery clears those pointers only
when the real stock is set down; a discarded prepared step does not move it.

The table records earlier schema changes. Its older-save behavior is historical;
the current loader accepts only the current schema and does not run those
migrations or backfills.
Feature thresholds, such as schema 33 for a birth-model descriptor, schema 34
for fields and ground lots, schema 35 for conversations, schema 36 for terrain
and weather generation, schema 37 for personal equipment, schema 38 for building
assignments, schema 39 for reusable containers, schema 40 for domestic family
and caregiver records, schema 41 for owner-message delivery, schema 42 for
selected tools on saved field and recipe work, schema 43 for life-moment
identity, schema 44 for Town land records, schema 45 for physical shop
exchanges, schema 46 for continuity, schema 47 for household departures and
physical custody, schema 48 for Town councils, schema 49 for food-order
progress and cancellations, schema 50 for guardian searches, schema 51
for medical permission and consumed-dose progress, schema 52 for selected
ornaments, schema 53 for wills with several heirs and final words, schema 54
for Town admission records, schema 55 for Town laws and government, schema
56 for named last meals, schema 57 for tool-making requests, schema 58 for
Council land claims, schema 59 for household land grants, schema 60 for
handcart attachments, schema 61 for guardian-order targets, schema 62 for
physical knowledge writing, schema 63 for exact-tile movement orders, schema 64
for overcrowding move-out notices, schemas 65 to 67 for material gathering,
storage and collection orders, schema 68 for shared Town-project construction,
schema 69 for equipment-repair orders, schema 70 for explicit chosen names and
unique first names, schema 71 for physical guardian placements, schema 75
for paid Markets and physical stall trade, schema 76 for land hearings and
consensual permission transfers, schema 78 for sponsored admission approval
expiry, schema 81 for elections forced by government changes, and schema 84
for marriage consent and surname sessions record when those fields or behaviors
were introduced; they do not allow an earlier checkpoint schema past the
current alpha cutoff.

| Compatibility change | Meaning |
| --- | --- |
| Schema 18 | Removes persisted energy/sleep state. Legacy bedding can remain inert compatibility data; recipes cannot restart sleep gameplay. |
| Schema 19 and checkpoint v2 | Compact verified terrain chunks rather than per-tile checkpoint JSON. |
| Schema 20 | Agent-owned beliefs. |
| Schema 21 | First-Town identity, founding state, membership, assigned buildings and borders. Older setup worlds reconstruct only known founding facts. |
| Schema 22 | Optional agent-owned Jev memory-ranking indexes. Older saves do not invent indexes. |
| Schema 23 | Personal map facts and physical map/record artifacts. Earlier compatible saves start with empty personal knowledge. |
| Schema 24 | World-owned Roads. |
| Schema 25 | Optional selected first-Town origin; older Towns keep their camp-derived border. |
| Schema 26 | Optional regional weather episodes (world-systems schema 2). An older save imports its current weather on its first resumed tick. |
| Schema 27 | Optional building entrances and trees planted on new tiles. An entrance must lie directly beside a footprint edge. Planted trees are saved as `planted-tree-{x}-{y}` map resources with their growth record and must be legal plantings (see [Trees and planting](how-it-works.md#trees-and-planting)). Invalid state is refused and the file is kept. Older builds refuse schema 27 saves. |
| Schema 28 | Saved bridges and bounded bridge-traffic evidence, plus an optional pending first personality/aspiration choice for newly placed adults. An older save has no bridges; an older schema that carries bridges or pending identity choices is refused. Accepted personal replies consume the identity opportunity; missing or invalid fields keep the placeholders. The marker, selected text and ID-only choice event survive current-format save/reload. |
| Schema 29 | Optional bounded model-attempt status and a separate last accepted model choice per agent. Current-format reload preserves failed/canceled attempts without replacing the last choice. Old builds may refuse these alpha checkpoints; no migration is added. |
| Schema 30 | Building footprint revisions, reserved expansion jobs and saved House guest invitations. Expanded geometry is used by validation, Town assignment, construction and observation; building IDs and stock locations stay the same. Earlier builds refuse these checkpoints instead of losing expansion or invitation records. |
| Schema 31 | Learned skills and skill-based lessons, including learning time and optional teacher in living and deceased profiles. Earlier formats cannot hold these records; older builds refuse these checkpoints instead of discarding skills. Model-attempt and building-expansion records remain distinct. |
| Schema 32 | Optional per-adult housing state: a pending request to live in another household's House (the household asked, its recorded adult members, including adults who join or come of age while pending, their answers and the 120-tick expiry), recent refusals and the current housing blocker. Loading checks that the applicant has no household, that members and answers name known people, and that refusals name known households. An older schema that carries housing state is refused. |
| Schema 33 | A child's immutable birth-model descriptor in living and deceased profiles: personal role, provider endpoint, model, installation-local key-slot ID and parental selection reason. Owner changes to each decision role remain separate. API-key bytes stay in protected installation storage. An older schema carrying a birth descriptor is refused. |
| Schema 34 | Household field ownership, crop stages, interrupted work and protected replanting stock, plus physical ground positions for harvest lots. Older schemas carrying fields or ground lots are refused. Fertility remains derived from the world seed and map layers. |
| Schema 35 | Bounded resumable agent conversations and daily participation budgets. Accepted public turns and session facts are saved; pending model replies and private prose are not. Older builds refuse these checkpoints instead of discarding conversations. |
| Schema 36 | New-world patchy beaches, denser forests, desert-only cacti and the reduced default wet-weather preset. Earlier alpha checkpoints are refused and preserved rather than changing their saved map. |
| Schema 37 | Personal garment and carrying-aid selection, timed repair work and exact material reservations; a paused household recipe may require a fresh choice after its materials become unavailable. Selected units must be physically carried and owned by that person. Existing overloads and broken goods are preserved; capacity and protection remain derived. Earlier schemas cannot carry equipment records. |
| Schema 38 | Town membership and assigned-building references are validated together with physical inventory locations. Terminal expansion history retains its original building definition after removal; active work and the last Farmhouse's field work block removal or reassignment. Earlier checkpoints are refused. |
| Schema 39 | Reusable storage pots and water jugs, their physical contents, shared owner and location, capacities and exact reservations. A vessel and its contents move together. Earlier alpha checkpoints are refused and preserved. |
| Schema 40 | Explicit domestic family-unit IDs and dependent caregiver IDs, plus the primary caregiver and intended/actual household for a parenthood plan and birth record. House resident limits remain derived from these records and completed building footprints. Older builds refuse the checkpoint rather than infer family identities. |
| Schema 41 | Owner messages keep their exact words and target, when a personal model heard them, an optional short reply and the one-fresh-decision prompt tick. Instruction and completion records are required and validated: identifiers, kind, target, ordering, tick bounds and reply. Earlier alpha checkpoints are refused and preserved; no message migration is added. |
| Schema 42 | A selected carried tool for unfinished field work or a knife-assisted recipe. Work and its exact tool lot survive reload; each field action and recipe completion wears its selected tool when that action commits. Earlier alpha checkpoints cannot contain these links. |
| Schema 86 | Crude House tools use ordinary saved inventory, projects and production jobs, with their own content package and tool tier. Supply, paid work, ownership, condition and extraction continue across reload without duplicate output or wear. Older alpha saves are refused and preserved without migration. |
| Schema 43 | Bounded per-agent life-moment opportunities, their single-attempt outcomes and accepted personality/aspiration changes. In-flight requests are interrupted after restore; deceased archives retain finalized outcomes. Earlier schemas cannot carry life-moment records; older alpha checkpoints are refused and preserved. |
| Schema 44 | Connected Town-title plots from the accepted first-Town layout, starter household use rights on assigned building footprints, and pending land-use requests. Later border growth does not create title. Invalid or incomplete land records are refused; earlier alpha checkpoints are not migrated. |
| Schema 45 | Physical shop exchange records bind exact inventory barter offers to the shop, selling household, customer, position and proposal time, with the completing seller or cancellation reason. Reservations, purchase carrying, on-site payment and Store delivery lots retain their physical inventory locations. Earlier alpha checkpoints are refused and preserved; no shop-state migration is added. |
| Schema 46 | The continuity rule's saved on/off state and each eligible couple's "not yet" deadline, plus the `postponed` parenthood stage. A missing or inconsistent rule state is refused. Earlier schemas cannot carry it. |
| Schema 47 | Household departure records, once-only physical food allowances, care groups and personal collection rights; optional carrier IDs keep custody separate from property. Production jobs capture their owner at start. Paused private work preserves its original inputs and pause time for a remaining member to resume. Older alpha checkpoints are refused and preserved. |
| Schema 48 | Independent Town councils, personal candidacy agreements, proposal windows and final votes, current and archived elections, recorded runoff draws, notices and actor-owned read/relay receipts. Founded Towns require valid governance. Earlier alpha checkpoints are refused and preserved; no civic state is inferred or migrated. |
| Schema 49 | Recognized food orders keep their target, requested and completed units, retry state and status, plus cancellation receipts tied to the exact world, actor and order. Progress records the physical effect that earned it, so an unrelated action or a stale order cannot advance a replacement task. Earlier alpha checkpoints are refused and preserved; no order migration is added. |
| Schema 50 | Staged dependent-guardian searches with their current stage, timing and offered adults, so consent remains ordered and replayable. Older builds refuse the checkpoint rather than infer or discard a search. |
| Schema 51 | Named medical permissions and active consumed-dose progress bind to actual completed inventory receipts. Terminal treatment closes its receipt without refund or resurrection; death retains completed consumption history and archived profiles cannot carry active treatment. Earlier alpha saves are refused and preserved without migration. |
| Schema 52 | An optional exact personally owned, carried ornament lot in the canonical personal equipment record. Wearing supplies no protection or carrying bonus; removal, gifts and death retain actual property while clearing the selection when required. Earlier alpha saves are refused and preserved without migration. |
| Schema 53 | Wills with one to three named heirs (people or a Town), an equal or item-by-item split, the exact quantity of each frozen lot per heir, and final words; the deceased archive keeps the agent's Town. Divisions must cover every frozen lot exactly. Earlier schemas cannot carry these records. |
| Schema 54 | Town admission records tie each passed admission proposal to one outcome: approved and waiting for the newcomer, admitted with the care group that moved, or lapsed with its reason. An approval applies at most once. Earlier alpha saves are refused and preserved without migration. |
| Schema 55 | Scoped law versions, protected resident government votes and handovers, mayoral consent and rounds, and separate land and governing mandate terms. Earlier alpha schemas are refused and preserved. |
| Schema 56 | An agent's last meal keeps its concrete name, such as porridge, bread, stew or a Restaurant meal, for nourishment and dietary variety. Running House and Restaurant cooking keeps its exact inputs, reusable water jugs and outputs through reload. Earlier alpha checkpoints are refused and preserved; no migration is added. |
| Schema 57 | Bounded tool-making requests bind their customer, selling household, actual Blacksmith, accepted worker and ordinary production/offer history. Completed work requires exact full input receipts; purchase status must agree with the real inventory offer. Earlier alpha checkpoints are refused and preserved without migration. |
| Schema 58 | Council proposals may carry the exact connected plot of a land claim; a passed claim and its Town title must agree on Town, tiles and settlement time. Earlier alpha checkpoints are refused and preserved without migration. |
| Schema 59 | Household land requests keep their status, Council proposal, each adult's separate consent and the adult roster at settlement; a grant and its rights must agree on plot, grant time and end date. Earlier alpha checkpoints are refused and preserved without migration. |
| Schema 60 | Exclusive physical handcart attachments: one cart per puller and one puller per cart, for a living owner, with cart and puller on the same tile. Cargo stays in ordinary inventory lots inside the cart. Earlier alpha saves are refused and preserved without migration. |
| Schema 61 | Guardian orders retain the exact target agent separately from food/resource targets. Loading refuses missing or unknown targets, mixed task fields, repetition, and inconsistent completion receipts. The order survives a rename, pause, cancellation and replay. Older alpha saves are refused and preserved without migration. |
| Schema 62 | Physical knowledge-writing projects retain their author, frozen learned facts, source artifact, work and exact paper/cloth reservations. Completed maps, records and books retain the consumed-material receipts and unique physical lot. Invalid provenance, duplicated inputs and malformed work are refused. Earlier alpha saves are refused and preserved without migration. |
| Schema 63 | Exact-tile movement orders retain their destination, progress and arrival receipt. Loading refuses missing destinations, mixed food/resource fields, repetition and inconsistent completion. Queues and interrupted trips replay across saves. Older alpha saves are refused and preserved without migration. |
| Schema 64 | Household move-out notices retain their original notice period, fixed deadline and selection reason alongside pending housing requests. Reload and replacement do not restart notice or duplicate departure goods. Runtime admission and displacement recheck current need and caregiver protection. Earlier alpha saves are refused and preserved without migration. |
| Schema 65 | Material-gathering orders retain the material kind, exact optional source or position, batch/item progress and physical harvest receipt. Loading rejects mixed food/guardian/material fields and inconsistent progress. Queue, cancellation and partial work replay without duplicate harvests; older alpha saves are refused and preserved without migration. |
| Schema 66 | Personal-material storage orders retain their bounded material target, partial progress and committed relocation receipt. Queued work, cancellation and replay cannot move goods twice. Older alpha saves are refused and preserved without migration. |
| Schema 68 | Typed Council project plans and one shared Town construction record per passed proposal, with exact physical load/reservation/release history, work and paid building identity. The first consumer is the Town Hall. Earlier alpha checkpoints are refused and preserved without migration. |
| Schema 69 | Equipment-repair orders retain the equipment kind and progress in finished repairs, and an in-progress repair names the order it belongs to. Loading refuses mixed target fields, unearned progress and a repair bound to another order or item kind. Queue, cancellation and partial work replay without duplicate material costs; older alpha saves are refused and preserved without migration. |
| Schema 70 and society/runtime v2 | Required chosen-name markers distinguish temporary labels from chosen identities, even after automatic naming ends. Chosen first names are unique across living and deceased inhabitants. Current-format reload and compaction retain the marker and pending retry; older alpha formats are refused and preserved without inference or migration. |
| Schema 71 | Pending guardian placements retain exact accepted care authority, collecting or escorting progress, a selected House and current blocker. Household and Town membership change together only on valid arrival. Earlier alpha checkpoints are refused and preserved without migration. |
| Schema 75 | Required Market lists bind paid halls and stalls to their completed Town projects, with named borrowing, physical stock receipts and exact inventory barter history. Earlier alpha checkpoints are refused and preserved without migration. |
| Schema 79 | Paid stone and hanging street lanterns reuse the Town project ledger. The exact immutable definition, one-tile roadside site and adjacent Road tile bind their style and edge; altered budgets, geometry, receipts or orphan fixtures are refused. Earlier alpha saves are refused and preserved without migration. |
| Schema 76 | Land hearings and voluntary permission transfers retain notice, evidence, personal consent, authority and closure history. Original grants and bounded adjustments must reproduce current permissions. Earlier alpha saves are refused and preserved without migration. |
| Schema 78 | A sponsored admission approval expires one unpaused world day after its passed proposal settled, with `acceptance_expired` recorded when no acceptance occurred. The deadline uses saved world time and the original council decision, so pause and reload do not restart it. Earlier alpha saves are refused and preserved without migration. |
| Schema 81 | A protected government change records the exact initial Council election it forced, including failed attempts. Current-format saves require the explicit nullable link and validate its Town, ownership and lifecycle. Ordinary elections remain independent; earlier alpha saves are refused and preserved without migration. |
| Schema 84 | Required marriage consent receipts and dedicated surname conversations preserve the original surnames, admitted turns and seeded result, including across history compaction. Later player surname changes update both spouses together without rewriting the original decision. |
| Schema 91 | Non-land mandate consent, conduct-time law context, public hearings and voluntary remedy agreements retain their sources and real physical completion receipts. Earlier alpha saves are refused and preserved without migration. |

### Tool-making requests

Saved tool requests keep their requester, selling household, actual Blacksmith,
existing recipe, status, worker and real production/offer links. Active and
terminal lists remain bounded; selected production plans retain the exact
request identity. Loading checks these links against the canonical production
and barter records. Every linked job must retain its exact input reservations,
matching the whole recipe and selling household; completed work requires
completed consumption receipts. Ready work must bind a completed job, and an
offer must name that job's actual output and the same customer and shop.

Offered, fulfilled and terminal purchase history must agree with the actual
open, settled or cancelled inventory offer. A late withdrawal cannot turn an
already settled purchase into a withdrawn request. Retiring bounded terminal
history also clears its job link, so a job cannot retain an orphaned request.
Withdrawal creates no refund or ownership transfer: materials, unfinished work
and an unbought tool remain household property. A request is neither an
inventory lot nor payment authority. Reload must preserve in-progress work and
offer status without double production, duplicate payment or new ownership.
Earlier alpha checkpoints are refused and left unchanged. Current-format
integrity and byte-exact roundtrip remain required.

Other compatibility fields remain separate for simulation, envelopes, content,
assets, generator and network contracts. Change the field whose semantics
changed; a cosmetic game-version bump is not a migration.

The saved clock/lifecycle values govern old worlds. Restore validates matching
society/world-system calendar values rather than silently assigning the newest
playtest pace.

Newly created playable worlds save `CalendarOffsetTicks = 90` with 360 ticks
per day, placing elapsed tick zero at 06:00 on Spring 1, Year 1. The offset
must be nonnegative and less than one day. World-systems schema 3 carries a
nonzero offset, so older readers reject it instead of silently displaying
midnight. The outer private-world schema is unchanged. Zero-offset worlds
continue using world-systems schema 2; an absent offset means zero and is
omitted when writing, preserving their existing clock and canonical bytes.
Restore uses the saved value, not the new-world default. Ages, setup guards,
action deadlines and elapsed durations keep using the original world tick;
calendar dates, daylight and daily allowances use its offset calendar.

Night ([#673](https://github.com/compoodment/ClankerWorld/issues/673)) adds no
saved darkness field: time of day is derived from the saved tick, ticks per day
and calendar offset. A current-schema
save made before night existed loads unchanged; nights, and their chill on
outdoor warmth, apply from its next tick. Its recorded history is not
re-simulated. A world saved during dawn reloads at the same darkness and
advances to the same bytes as the live world, which `SettlementSurvivalTests`
checks. Seasonal night length
([#891](https://github.com/compoodment/ClankerWorld/issues/891)) is derived the
same way, from the saved tick and the saved season lengths, so it adds no saved
field either: an existing save loads, and from its next tick its nights follow
the season it is in.

## Council-approved Town projects

Schema 68 adds a required, non-null `Projects` list to each Town and an optional
typed `Project` payload to a civic proposal. An empty list records that no
construction has been approved. The first supported payload is
`TownHallContent.Hall3x4()`: its exact definition identity, normalized name,
site, south doorway and 24 wood / 12 stone budget are bound together. The
material amounts and ten-unit work target are provisional gameplay values.
No additional Council or vote authority is saved: a shared job references the
original passed proposal in that Town's full canonical governance ledger.

The records retain the approval tick, stage, work done, last transition, blocker,
completed building ID and every actual load's contributor, source and resulting
lot IDs, quantity, pickup/delivery times, exact reservation and release history.
Each material receipt keeps the identity derived from its exact delivery. The
normal owner removal command records a completed Hall's removal time while
retaining the consumed receipts; a missing Hall without that record is refused.
The supplied query counts usable Town-owned stock reserved on the approved
ground site, or that project's completed consumption receipts. Carried promises,
expired or released claims and cumulative deliveries whose goods are no longer
there do not stand in for paid materials. Private harvesting and an accepted
personal donation remain separate transitions.

Validation for these records must reject a missing or duplicate approval
binding, unsupported or altered payload, invalid times/stages/work, duplicate
load identities, incorrect custody or quantities, mismatched material claims,
unpaid completion and a completed building that does not match the approved
Town, definition, footprint and doorway. Legal site checks use actual Town title
and existing household rights and pending requests, independently of the
visible border. A blocked live site releases unused material claims; the goods
retain their actual location and Town owner rather than being recreated or
returned by a counter. Only a pending household land request keeps a project
blocked; any other site failure saves it as `cancelled` with its reason and
released claims, and a cancelled project no longer protects its site. A
completed Hall remains bound to its original paid receipt history.

Current-format restore and replay must keep partial multi-load supply, consumed
receipts, shared work and civic knowledge without duplicating approval, stock,
donations or the Hall. Discarding a prepared tick must leave no transfer,
reservation, progress, building or event. Late replies must recheck the current
project, actor authority and physical goods. Approval in the owner observation
comes from the complete proposal ledger even after it leaves the eight recent
results. `TownProjectRuntimeTests` and `TownProjectSaveValidationTests` cover
generated-world gathering and donation, genuine Warehouse loads, intermediate
restore/replay, discarded prepared ticks, stale votes, retained removal and
coherently altered receipt/source references. Earlier
alpha schemas, including 53, are refused visibly and preserved without migration.

## Ports and communal boats

Private-world schema 92 adds required, non-null `BoatTransport` state. Empty
boat and request lists record that no asset or trip exists. Each boat binds a
Town, one completed paid boat project, its current water position and either a
real mooring or one saved journey. Every completed boat project must retain its
physical asset, completed asset ID and exact consumed wood, rope and refined-iron
receipts. A missing boat or invented removal record is refused.

Journeys retain one passenger, origin and destination Port IDs, reserved dock,
cardinal water path, current index, start and next movement ticks, blocked-arrival
start time and return direction. Requests retain sequence, traveler, owning Town,
Ports, status, boat and settlement time. Validation rejects mismatched payment,
owner, passenger or request, duplicate physical or incoming dock claims, invalid
water steps and an active passenger separated from the boat. Waiting requests
hold no boat reservation; terminal history remains saved.

Council boat permission is an explicit typed field on its proposal draft and
adopted law version. The exact known visitor or standing grant, canonical rule,
original vote and adoption/repeal history must agree. Matching freeform text
cannot supply the field or permission. Generic amendments cannot turn a grant
into a different authorization. Deceased profiles retain their actual boat ID;
aboard estate cargo retains unique ground roots and follows the boat until landing.

Capture, prepared-tick commit, codec restore and owner projections retain these
records together. Native checks build both Ports and the boat through personal
choices, real votes, supply and work, then exercise travel, mid-voyage replay,
queues, cancellation, permissions, blocked arrival and damaged current-format
records. Earlier alpha checkpoints are refused visibly and preserved without
migration.

## Paid Markets and stall trade

Schema 75 adds a required, non-null `Markets` list to each Town. Each Market
binds its hall and fixed 7×4 plaza to the completed starter project, which pays
for exactly two stalls in slots 0 and 4. Each additional stall requires its own
completed project with the exact definition, slot, site and material budget.
Loading rejects unpaid or duplicate slots, altered plans, unmatched buildings
and inconsistent removal history. Approved, partly supplied and completed
Market construction retains the shared Town project's physical load,
reservation, consumption and work records.

Named borrowing records bind actual living adults to a paid stall while they
remain inside the hall-and-plaza area. Stock receipts keep the personal or
household owner recorded at deposit, physical lot, quantity and occupancy
identity; live inventory remains authoritative after inheritance or collection.
Another borrower gains no right to sell those goods. Barter records bind the
exact inventory offer, named seller and buyer, lots, reservations and outcome.
Completed or cancelled trades retain their history without requiring spent
goods to remain live. Loading checks owners, locations and the actual physical
occupancy instead of reconstructing authority from a stall's current borrower;
retained active claims of an open offer must bind its exact parties, lots, quantities,
purpose, expiry and exclusive reservation state. Missing or released claims remain
loadable and are cancelled before trade continues. Goods on a stall tile are not capped at load,
because a death or a removed stall can leave more there than a borrower may
deposit. Two standing Markets in one Town cannot share ground. Loading also
keeps a standing plaza clear of unrelated buildings, fields, claims, expansions,
resources, camp objects and bridge ends; only its paid stalls and aisle Roads fit.

Leaving, household change, death or removal ends borrowing and cancels open
offers. Removed Market buildings keep their paid history and stock receipts,
so recorded owners can still collect physical leftovers. Current-format
restore must preserve intermediate construction, borrowing, stock and open
trades without duplicating goods or approval. `MarketConstructionRuntimeTests`
covers a generated paid Market and additional stall, personal and household
stocking and barter, fresh consent, paused-plan ingredient buying, retrieval,
intermediate restores and malformed state. A buyer added outside all Town land
retains its independent household and no Town membership; open and settled
trades reload strictly, and its live and restored continuations remain
byte-identical after each continuing step.

Planting-stock demand reuses existing fields, inventory and reservations and
adds no saved field. The seed scenario creates its prepared field through
actual tilling, buys one grain seed, retains the supplier's remaining unit,
suppresses another same-kind purchase while the bought unit is held, then
consumes that exact unit through ordinary planting. Intermediate states reload
strictly with the same physical lots and claims. Stock, food consumption,
equipment and phase wakes are controlled test arrangements; title, field
placement and actor positions are retained from actual work and movement.
Earlier alpha checkpoints are refused and preserved
without migration.

Schema 79 adds two supported street-lantern definitions to that same paid
ledger. Their `Site` is a clear one-tile roadside footprint and `Entrance` is
the approved cardinally adjacent Road tile. The immutable definition selects
the style; the difference between those two positions binds its Road edge.
These positions and the exact trial budget are part of the approval scope,
not inferred later from a nearby Road. Completion keeps the bound entrance
and does not generate or extend streets. Live placement and current-format
validation require uncontested Town title and an existing Road, with the same
real delivery, reservation, work and removal receipts as the Hall. No fuel,
lit flag or separate rendering authority is saved: day/night appearance is
derived from the saved world clock. The owner projection supplies definition
tags as derived display data. Current roundtrip and replay retain the edge,
paid materials and partial custody. Earlier alpha saves are refused and
preserved without migration.

## Pending model work and estates

The cognition queue saves unresolved decision points. External HTTP tasks are
not save authority. Pause or shutdown cancels them; a restored still-relevant
decision may be retried and incur another attempt.

The post-death will path is intentionally different: it freezes personally owned
lot IDs/kinds/quantities and their original building storage in estate escrow.
A cancellable final choice runs outside
the death tick. Validate the living recipient and still-escrowed frozen lots.
Death cancels only open barter offers through the ordinary cancellation
transition, releasing both parties' reservations. Completed trades and unrelated
surviving reservations remain.
A persisted pending will is not reissued on restore; interrupted work resolves
to the household default on the next active tick. Failure/deadline does likewise.
Estate settlement waits for the pending will and commits once. An accepted will
may divide lots between up to three heirs, including children or the deceased
person's Town. Positive personal recipients alone receive its final words.
Inheritance changes ownership while retaining ground, House storage or a living
carrier's custody; goods carried by the deceased are dropped at their last tile.
Town shares use the Town's current Warehouse while it can accept them.
Debts and Town-law conflicts remain separate work. Inheritance does not decide guardianship.

A quantity-one physical map, field record or book retains its lot ID when inherited.
Ownership and location change; its creator, discovery facts and artifact link
remain. Ordinary divisible stock follows the usual split rules. Inheritance
does not broadcast the artifact's knowledge to everyone.

Writing projects save their exact learned contents and input reservations;
reloading does not invent supplies or finish the work. Copies preserve the
original discoverers and the source artifact while naming the actual writer.
Each completed artifact has its own quantity-one inventory lot and consumed
input receipts, so a retry cannot reuse another artifact's payment. Paper
production uses the ordinary household recipe and reusable-vessel state.

Inventory lot splits leave all actively reserved stock in the original lot,
including production and barter commitments. Only the unreserved remainder can
move to the new lot; rejected splits leave state and event history unchanged.

## Checkpoints, history and backups

The active recovery checkpoint is encoded and fsync-written after every advanced
one-second tick. Older history is compacted into digest-addressed segments.
Hot event lists are bounded, but long-term segment retention and larger-world
write cost are not yet measured.

Named manual checkpoints use a private `.manual` directory and reference the
same history archive. Overwriting a selected checkpoint retains a recovery copy;
these copies have no settled retention policy. Rotating autosaves are a separate
mechanism and must not delete another world's checkpoints, or another branch's.
Updating autosave configuration trims only that configured world, including
rotation off, and counts each branch's autosaves separately.
The signed action names the world whose settings were opened. Its identity
check, configuration write and rotation share the world-mutation gate with
selection and loading. A request delayed across a world switch is refused
without changing either world's settings or checkpoint copies.

Load and overwrite validate required manual-save metadata before creating a
recovery backup or changing the active world. Malformed JSON, missing save
records or invalid required fields return a controlled conflict and preserve
the original files. The save list skips these same invalid entries.

Manual overwrite first writes an immutable checkpoint generation, then
atomically publishes its metadata pointer with the matching model assignments
and autosave settings. Failure before publication leaves the previous selected
pair intact. Legacy saves without a generation pointer remain readable.
Unpublished generations are retained; no cleanup policy is implied. Older
binaries do not understand this pointer and must not load newly overwritten
saves. Use a matching pre-upgrade backup for rollback.

The world catalog keeps a small terrain thumbnail for each world, packed like
the world's own terrain (`terrain-kind-v1`) and at most 96 pixels wide, so Load
World can show it without reading the world's checkpoint. Worlds catalogued
before thumbnails existed get one the first time they are listed, from the
checkpoint that listing already reads. It is a convenience copy, not world
state: an older host ignores it, and a missing or damaged thumbnail shows a globe
while the rest of the world list stays available.

A full recovery backup must keep together:

- The active save and the referenced `.history` archive.
- Named and rotating saves in `.manual`, with each world's branch record.
- The adjacent `.autosave.json` schedule and saved-tick metadata.
- The world catalog and its archived worlds.
- Pairing authority, protected provider configuration and usage accounting.

Keep this material private. A world save by itself is not an installation backup.
Trim a rotating snapshot only after a newer usable copy exists. Damaged metadata
entries are isolated from listing so sound saves remain reachable; preserve and
report the damaged file rather than silently removing it.

For an incompatible rollback, restore the matching older application and
pre-upgrade save/history. An older host is not expected to read a newer schema.
The September 2026 internal-identifier reset was an explicitly approved
pre-release fresh-save/new-pairing exception. It never permits deleting saves.
During alpha an older save may stop loading, but it is refused with a reason and
kept. Finished releases follow the migration promise in [Saves](../game-design/saves.md).

### Save branches

Loading an older save and playing on starts a new branch instead of mixing two
histories in one list ([design](../game-design/saves.md#agreed)). Each save's
metadata records its branch (an opaque ID, a number for display and the save it
started from), its position within that branch and the save it continued from,
with that save's creation time.
A per-world record in `.manual` (`timeline-` plus a hash of the world ID) says
which branch and save the running world continues from. Loading a save moves it
to that save; every new save moves it to the new save. These are save metadata
only: checkpoints, events and replay are unchanged.

The owner's client reads the record for the timeline's **You are here** marker
through the signed `save-timeline` control action
(`POST /api/v1/owner/saves/timeline`). It answers with the save the running
world continues from, that save's branch and time, whether the next save starts
a new branch, and the branch number the next save will use even after deletions.
The answer comes from the same checks saving uses, so the two cannot
disagree, and reading it changes nothing. A host from before this action
answers 404, and the client then leaves the marker out.

A new save continues the recorded branch unless something else on that branch
has a higher position. Positions advance even while paused and survive deletion
of intermediate saves, so equal world ticks do not mix separate histories.
Otherwise the save starts a branch numbered
one higher than any the world has used. Overwriting a slot moves it into the
running world's branch; its recovery copy keeps the old branch and position.
The fork decision uses the loaded save before rebinding it to that recovery copy.

Before loading, the host saves the world being left as **Before loading**, so its
unsaved progress stays on its own branch. After a successful load, the record
keeps a fingerprint of the loaded world after the host's required pause, as well
as the stored checkpoint's fingerprint. It skips the copy only when the world
still matches that loaded state, the stored checkpoint remains readable and
unchanged, and model routing and autosave choices match. Browsing running
autosaves therefore does not create empty branches, while a missing or damaged
checkpoint cannot replace a recovery copy. If loading fails, the branch record
is restored along with the world.

The branch record is written before a new save's metadata is published, so an
interruption leaves it pointing at an unlisted save whose position still keeps the
next save on the same branch. A missing or damaged record never blocks saving:
the next save starts a new branch, with a warning in the log. A save whose own
branch fields are damaged stays listed, without a branch. Saves made before
branches existed have none either; Load World groups them as **Earlier saves**,
and playing on from one starts a new branch. Autosave rotation leaves saves
with damaged branch fields alone, since their histories cannot safely be
grouped; genuinely branchless earlier autosaves still rotate together.
Deleting a world removes its
branch record with its saves.

## Validation boundaries

Use focused tests for migration, replay identity, interrupted writes, referenced
history, old calendars and rejected restores as relevant to the change. Record
real Windows/server verification separately from fixture and CI results.
The full check commands are in [build and test](build-and-test.md).
Long-term save support and retention remain [design questions](../game-design/saves.md#still-to-decide).

## Regional weather episodes

The prototype adds optional `RegionalWeather` data to world systems: topology,
local climate, condition, start/end ticks and each region's earliest next storm.
Absent data stays absent on load, preserving visible daily weather. The first resumed tick imports that weather; a current
storm retains its original daily start so migration cannot extend its duration.
Ordinary imported conditions reserve a conservative half-day storm-free window.
New worlds start with episode data. Saved episodes resume without rerolling;
all transitions use the same prior neighbor snapshot.

Custom weather weights retain their saved values, including valid totals up to
`int.MaxValue`. Climate conversion and episode bonuses use wider arithmetic to
avoid overflow. Existing episodes and the save format stay unchanged; future
transitions from extreme profiles use the corrected probabilities. Current
saves round-trip and continue deterministically under those rules.

Episode version and bounds are validated, including topology against the saved
map. Episodes arrived with private-save schema 26; episode-bearing world systems use schema 2.
World-systems schema 1 remains readable, with the absent field omitted when null.
Older binaries reject the newer schema instead of silently dropping episodes.
The new code reads old saves; keep backups before testing.
This prototype changes future weather/events, not past recorded history.

Advanced generation saves optional forest, mountain and river presets. Missing
fields mean Normal. New-world water defaults do not alter saved water values.
Non-default maps require a build that understands their options and validates
their generated identity. Every generated world saves the visibility algorithm
(terrain) version, now 2, and, when trial targets apply, the selected candidate
attempt.
Restore regenerates that exact attempt, checks the saved map manifest, and does
not rerun candidate selection or silently change the saved map. The attempt
defaults to 0 for historical saves. A save whose map no longer matches
deterministic regeneration is refused for load; restore leaves the source save
file available for recovery or an explicit future migration.

## Explicit permanent deletion

Signed deletion binds the target kind, exact ID, world ID and (for snapshots)
creation timestamp. It shares the installation mutation gate with world
selection and snapshot writes. The active world cannot be deleted.

Deletion reads verify the metadata's save identity against its filename before
using it. A missing save record or mismatched identity keeps cleanup pending
instead of preventing the healthy host from starting. These checks do not
require playable model settings or a valid display name just to remove an
otherwise identifiable checkpoint.

Snapshot metadata is renamed to a deletion intent before owned generations
are removed. World removal first moves its catalog entry into pending deletion,
then removes its snapshots and archived checkpoint. Pending targets are not
loadable; startup retries cleanup. Storage failure is reported rather than
acknowledged as complete. Corrupt metadata whose ownership cannot be established
is preserved and can leave cleanup pending. Do not roll back to older binaries
while deletion intents remain: they cannot perform this recovery.

History reclamation verifies all remaining active, archived and manual checkpoint
roots and their digest-addressed chains before removing unreferenced segments.
Unpublished generations conservatively count as roots. Corrupt roots defer history
cleanup, preserving other saves. This is ordinary file deletion, not secure disk
erasure, and does not remove copies in external backups.

## Developer edits

A paused developer edit changes existing need, inventory, skill or partnership
state and appends one `developer_edit` event whose JSON detail is the complete
`PrivateWorldDeveloperEdit` command. No checkpoint fields or schema version
change. Existing save validation still applies to the entire proposed world;
older readers can load the same state representation, though they do not offer
the edit UI or describe the new event kind.

The event ID supplies deterministic lot and relationship IDs. Reapplying its
command to the same paused baseline reproduces the checkpoint; replay tests
also compare resumed ticks after a save/load roundtrip. The event carries the
world ID and expected latest event ID. An exact retry found in hot event history
returns already applied without another grant or event. After history compaction,
the old event precondition refuses that retry instead of applying it again.

The host writes the validated proposal through the checkpoint's atomic file
replacement before accepting it in memory. A failed write preserves the live
state and the prior save, and the original command can be retried. Tests cover
this rollback, stale/wrong-world refusals, field binding in signed requests,
and the generated-world path for every supported edit category.

## Animal checkpoints

Private-world schema 94 requires the complete animal-world record. It stores
exact animal identities, species, sex, birth/death ticks, physical positions,
household and yard, paid care deadlines, unfinished products and pregnancies,
reserved births, named permissions, taming work, supply trips, riders and
leaders. Products and fitted saddles reference real inventory lots and exclusive
reservations. Trade offers retain the exact animal, adults, receiving yard and
payment lot; native animal orders retain the exact bound animal ID.

Current-format replay and rollback cover arrival, paid care, collection, birth,
production jobs and attachments. Reload validates required animal fields,
physical saddle/product custody, household/herd limits and vessel contents.
Water and milk cannot share a jug, and collected milk cannot become a loose lot.
An interrupted tick or refused durable developer edit restores both animal and
inventory state. Older alpha checkpoints are refused and preserved; no animal
inference or migration runs.
