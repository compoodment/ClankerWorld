---
title: Saves and replay
type: persistence-reference
status: active
updated: 2026-10-03
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
migration is provided. Admission approval remains a saved decision for #602 to
consume separately, with no stock or household-access effect.

Private-world schema 49 stores recognized food-order targets, progress, retry
state, cancellation receipts and their exact actor/world identity alongside the
original owner instructions. Loading validates these records together so an
unrelated action or a stale order cannot advance a replacement task. Alpha saves
must use the current checkpoint schema; older saves are refused without
migration and remain unchanged.

Private-world schema 53 adds material-gathering orders with a distinct
`TargetMaterialKind`, exact optional source or position, and progress measured
in harvest batches or material items. Saved progress and the last physical
harvest receipt are validated together; mixed food/material targets and invalid
material kinds are refused. Queueing, cancellation, discovery and partial
quantities retain their state across reload. Older alpha saves are refused and
preserved unchanged; no migration is added.

Private-world schema 54 adds `store_material` orders using the same bounded
material target. The destination is the current household House, so source,
food and coordinate target fields are refused. Progress counts storage loads
or exact item quantities and requires a committed personal-relocation receipt
with a fixed-length identity, even when the inventory lot identifier is long.
Replay preserves partial storage, queued work and cancellation without moving
goods again. Older alpha saves are refused and preserved without migration.
This number is provisional until merge and must remain above the material-order
base schema after integration.

Private-world schema 55 adds `collect_material` orders for the same material
catalogue. Their source is selected through ordinary personal-goods collection
rules; schema 59 adds an optional coordinate constraint below. Food and resource
identity targets remain invalid. Progress
counts collected loads or exact item quantities, and a bounded committed-move
receipt prevents replay from duplicating pickup. Unavailable goods and full
carrying space preserve the remaining task. Older alpha saves are refused and
preserved without migration. This number is provisional and must remain above
the storage-order base schema after integration.

Private-world schema 56 adds `repair_equipment` orders with a bounded
`TargetEquipmentKind` and progress counted in finished repairs. The equipment
work record has an optional `OrderInstructionId`, which must refer to that
actor's active repair task and match the actual lot kind. Its saved work counter
and material reservations retain their ordinary validation. Completion credits
a bounded receipt only after the real repair consumes its inputs. Cancelled or
replaced orders cannot retain live repair reservations. Replay covers partial
work, queues, cancellation and exact material costs. Older alpha saves are
refused and preserved without migration; this version is provisional above the
collection-order base until integration.

Private-world schema 57 adds field orders and their optional `TargetCropKind`;
schema 60 adds an optional exact tile below.
`FarmFieldWork.OrderInstructionId` binds work to its actor's active field order.
Restoration validates that link, action, crop, work time and ordinary seed/tool
state; a cancelled, queued, unrelated or missing instruction cannot retain bound
work. Only finished work earns a bounded receipt and one completed field. Replay
covers partial planting, queues, released seeds, real tool wear and household
harvest ownership. This version is provisional above the repair-order base and
must be reconciled above its integrated base before merge. Older alpha saves
are refused and preserved unchanged without migration.

Private-world schema 58 adds `repair_tool` orders using the existing
`TargetEquipmentKind` field. Validation restricts the action to supported tool
kinds and repair counts. Only a completed inventory repair earns a bounded
`repair:tool:` receipt. Replay covers partly completed quantities, preparation,
queues and cancellation without charging the materials twice. This version is
provisional above the field-order base and must be reconciled above its
integrated base before merge. Older alpha saves are refused and preserved
unchanged without migration.

Private-world schema 59 allows an optional `TargetPosition` on collection orders.
Coordinates keep the existing bounded integer validation; an off-map target is
a valid instruction that waits with a reason. The exact tile survives queued
work, travel, partial pickup, cancellation and reload. Runtime selection and
execution both recheck the lot's current position along with ordinary personal
collection permissions, so moved or depleted goods cannot redirect the order.
This version is provisional above the tool-repair-order base and must be
reconciled above its integrated base before merge. Older alpha saves are refused
and preserved unchanged without migration.

Private-world schema 60 allows the existing bounded `TargetPosition` on field
orders. A saved running field job must be at that tile as well as matching the
order's actor, action and crop. Queueing, partial work, exact progress and seed
reservations survive pause and reload; a rejected tick cannot leave work or
progress behind. An unavailable target waits without selecting another field.
This version is provisional above the collection-source base and must be
reconciled above its integrated base before merge. Older alpha saves are refused
and preserved unchanged without migration.

Private-world schema 61 adds `collect_food` orders using the existing
`TargetFoodKind` and optional `TargetPosition` fields. Food targets are generic
or one of berries, fruit, wild greens and cultivated greens. They use
`food_items` for exact quantities or `collection_loads` for default pickups,
with the same bounded `collect:personal:` receipts as material collection.
Validation refuses mixed material, equipment, crop or resource targets, wrong
progress units and unearned receipts. Queues, interruptions, cancellation and
partial pickups retain their state across replay and rollback. This version
is provisional above the field-location base until integration. Older alpha
checkpoints are refused and preserved without migration.

Private-world schema 62 adds `collect_equipment` using the existing exact
`TargetEquipmentKind` and optional source tile. Only the five supported garment
and carrying-aid kinds or 13 tool kinds are accepted. These tasks use
`equipment_items` for explicit quantities or `collection_loads` for default
pickups, with bounded personal-pickup receipts. Validation refuses mixed target
categories, unsupported equipment, wrong units and unearned progress. Physical
condition, source, queued work, remaining quantity and cancellation survive
reload and rollback. This version is provisional above the food-collection
base until integration; older alpha checkpoints are refused and preserved
without migration.

Private-world schema 63 permits `cultivated_greens` as an exact
`consume_food` target, in addition to its existing personal-collection use.
The same eating action, quantities, receipts and lifecycle are retained;
saved travel and wild-harvest actions cannot use that food kind. Fullness
waits, partial consumption, queues and cancellation preserve the target across
reload and rollback. This version is provisional above the equipment-collection
base until integration; older alpha checkpoints are refused and preserved
without migration.

Private-world schema 64 adds `store_equipment` with the existing exact
`TargetEquipmentKind`. Equipment storage permits the five garment/carry-aid
kinds and 13 tool kinds, with `equipment_items` for explicit quantities or
`storage_loads` for default tasks. It uses bounded personal-storage receipts.
Validation refuses mixed targets, locations, unsupported kinds, wrong units
and unearned progress. Partial storage, queued work, cancellation and equipment
condition survive reload and rollback. This version is provisional above the
cultivated-greens-order base until integration; older alpha checkpoints are
refused and preserved without migration.

Private-world schema 65 adds `produce_item` orders with an exact recipe and
output kind, an optional requested site, and the selected building, project
start and production-job identities. A production job's optional
`OrderInstructionId` records which order started it; matching a recipe and
start time alone cannot adopt an unrelated job. Progress counts finished output
items or whole batches. Loading validates recipe yields, cross-record ownership and
job references, pause state and completion receipts together. Queued work,
partial progress, cancellation and survival pauses retain their state through
reload and replay. This version is provisional above the equipment-storage
base until integration; older alpha checkpoints are refused and preserved
without migration.

Private-world schema 66 adds exact personal-goods and borrowed-return orders.
`TargetItemKind` is separate from gathering and repair subjects. Storage and
return tasks save the selected House identity, household owner and position;
returns also save their source lot. The destination binding is complete or absent,
and incompatible actions, subjects, quantities and receipts are refused.
Finite progress, queued work and cancellation survive reload without switching
House or changing ownership. This version is provisional above the production-order
base until integration; older alpha checkpoints are refused and preserved
without migration.

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

Schema 50 saves an unresolved dependent-guardian search, including its current
relative, household or Town stage, start tick and offered adults. Each stage
keeps the earlier groups, and the offers are brought up to date at the end of
every tick, so a save always matches the households it was made with. Acceptance is
an explicit adult action that changes the saved current primary caregiver.
Household membership changes in that same action only when a completed House
in the child's Town has room; otherwise it stays unchanged. The original birth
record and Town membership stay unchanged. Loading validates the stage, times,
and adult references. Replaying
from a pending request reaches the same acceptance opportunities and preserves
the single guardian-needed event. Older alpha saves without this state are
refused; no migration is added.

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

Private-world schema 47 records household departures, their original household, care group and once-only food allocation, plus optional physical inventory custody separate from ownership. Personal House storage retains the personal owner even after membership ends. Reload preserves collection rights, borrowed carried goods, the care group and unfinished housing task without awarding another allowance. Paused private jobs retain the original owners, exact input reservations and pause time. Their reserved workstation or expansion footprint stays occupied; a remaining authorized member can resume at the physical site with the same materials and remaining duration. Unavailable materials cancel the preserved job and release its remaining commitments. Invalid custody, departure records and unavailable carriers are refused. Older alpha checkpoints are refused and preserved; no migration is added.

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

Private-world schema 52 adds an optional exact ornament-lot selection to the
existing personal equipment record, without introducing combat equipment or a
second inventory. Current-format checks reject a foreign, reserved, stored or otherwise
ineligible selected unit and preserve intermediate refining, diamond setting,
gifts and barter. Removing or giving the ornament clears the selection while
keeping the actual item; death and estate handling retain the property without
an active selection on an archived profile. Older alpha saves are refused and
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

Before a potentially committed create/select/rewind request, the client clears
its held observation timeline. If the receipt is lost, reconnect starts from a
fresh baseline while retaining normal regression and terrain-identity checks
within that timeline. Continue does not resume an uncertain world switch.

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

The alpha accepts only the current private-world checkpoint schema, currently
`PrivateWorldRuntime.StateSchemaVersion` 63. The minimum supported schema is
the same value, so older alpha checkpoints are refused with a reason and left
unchanged; no private-world migration runs. The current schema also includes
bounded model-attempt status and last accepted model choice per agent, plus
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
guardian-search records with their offered adults, medical permission and
consumed-dose progress, and selected personal ornaments.
Land records are checked against the saved map, Towns, households and one
another before load. These fields retain their current validation and roundtrip
behavior.

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
for medical permission and consumed-dose progress and schema 52 for selected
ornaments record when those fields or behaviors were
introduced; they do not allow an earlier checkpoint schema past the
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

Other compatibility fields remain separate for simulation, envelopes, content,
assets, generator and network contracts. Change the field whose semantics
changed; a cosmetic game-version bump is not a migration.

The saved clock/lifecycle values govern old worlds. Restore validates matching
society/world-system calendar values rather than silently assigning the newest
playtest pace.

## Pending model work and estates

The cognition queue saves unresolved decision points. External HTTP tasks are
not save authority. Pause or shutdown cancels them; a restored still-relevant
decision may be retried and incur another attempt.

The post-death will path is intentionally different: it freezes personally owned
lot IDs/kinds/quantities in estate escrow. A cancellable final choice runs outside
the death tick. Validate the living recipient and still-escrowed frozen lots.
Death cancels only open barter offers through the ordinary cancellation
transition, releasing both parties' reservations. Completed trades and unrelated
surviving reservations remain.
A persisted pending will is not reissued on restore; interrupted work resolves
to the household default on the next active tick. Failure/deadline does likewise.
Estate settlement waits for the pending will and commits once. Per-lot bequests,
debts, minors and inheritance-law policy remain open.

A quantity-one physical map or field record retains its lot ID when inherited.
Ownership and location change; its creator, discovery facts and artifact link
remain. Ordinary divisible stock follows the usual split rules. Inheritance
does not broadcast the artifact's knowledge to everyone.

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

Episode version and bounds are validated, including topology against the saved
map. Episodes arrived with private-save schema 26; episode-bearing world systems use schema 2.
World-systems schema 1 remains readable, with the absent field omitted when null.
Older binaries reject the newer schema instead of silently dropping episodes.
The new code reads old saves; keep backups before testing.
This prototype changes future weather/events, not past recorded history.

Advanced generation saves optional forest, mountain and river presets. Missing
fields mean Normal. New-world water defaults do not alter saved water values.
Non-default maps require a build that understands their options and validates
their generated identity. Balanced Small/Medium worlds save the visibility
algorithm version and, when trial targets apply, the selected candidate attempt.
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
