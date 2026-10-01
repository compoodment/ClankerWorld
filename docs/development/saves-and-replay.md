---
title: Saves and replay
type: persistence-reference
status: active
updated: 2026-10-01
---

# Saves and replay

This page owns save implementation and recovery requirements. The
[player guide](../playing.md#save-and-return) explains the controls;
[game design](../game-design/saves.md) owns the intended experience.
[Releasing](releasing.md) owns version selection and rollback gates.

## Separate state by ownership

Portable vessels keep their identity while contents are consumed. Contained
lots name their vessel and share its owner and physical storage/delivery
location. A transfer or barter of the whole vessel moves those lots together;
reserved contents prevent moving the vessel. Current saves reject orphaned,
overfilled or unsuitable contents, bare water and inconsistent collection
work. In-progress fresh-water collection survives a save with its jug, shore
and last period of work. This is new alpha state, without an older-save migration.

A communal boat retains its completed Port job, consumed launch output, Town
owner, water position, dock or reserved journey, traveler and timed water route.
The decoder checks the boat against those physical records and both Ports.
Only its actual passenger may occupy water. Save/reload retains waiting and
return journeys; revoked visitor permission does not cancel a trip underway.
If the passenger dies, their actual estate roots stay at the moving boat's
position, including whole filled vessels. Inheritance preserves that physical
location until a beneficiary collects reachable cargo at the Port. Foot-only
archive validation does not move an aboard death onto land.

World state includes its seed and generation options, clock, agents, accepted
events, Towns, content locks, model/slot assignments and autosave choices.
Installation state includes device authority, provider credentials and usage
accounting. Saves store slot IDs and model choices, never API-key bytes.

Private-world schema 31 records an agent's learned skills and each lesson's
skill instead of a work role. Skills retain their first learning time and
optional teacher ID, including in deceased profiles. Loading validates those
references and times, and rejects null entries in living or deceased skill
lists as damaged checkpoint data. Current lesson progress and skills survive pause,
save/load and replay. Old alpha lesson records need not load; no migration is
provided. Saved skills grant no ordinary action permissions or speed bonus.

Private-world schema 34 adds household field tiles and their crop/work state.
The saved inventory also records a ground position for physical harvest lots.
Field ownership, work inputs, growth times and replanting reservations are
validated together with inventory and map geometry. Current-format roundtrips
retain intermediate work, carried deliveries and planting reserves. Fertility
is derived from the seed and immutable map layers rather than saved per tile.
Older alpha saves need not load; no field or orchard migration is provided.

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

Business state saves concrete listings, accepted offers, both stock
reservations, vessel-content commitments, receiving-space promises, Market
plots and household stalls, and tool-making requests. Restore refuses missing
or mismatched lots, owners, locations, contents and overbooked receiving space.
Stall stock and barter receipts remain household-owned until physically
withdrawn; only an empty stall is released. Rejected stock placed on adjacent
ground keeps its identity and contents and cannot be used as carried stock.

Canonical state is the accepted state used by the simulation. A replay checks
that recorded events reproduce its expected results and digests.

An agent's equipment record points to individual personally carried inventory
lots. A garment and a basket or sack each occupy one saved slot. The decoder
refuses another owner's stock, a stored or in-transit item, or an item in the
wrong slot. Broken gear keeps its identity and cargo; its carrying or weather
benefit stops. A load already over its capacity can be delivered or consumed,
but cannot receive more goods until room is available. A carrying aid cannot
be removed or replaced with a smaller one while that would overload the agent.

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
  [Saves](../game-design/saves.md). Old-save code already in the repository
  stays until it is removed; [issue #487](https://github.com/compoodment/ClankerWorld/issues/487)
  audits it.
- Keep build revision, release labels and telemetry out of canonical digests.
- Never infer compatibility merely from the public game version or file age.

Checkpoint decoding enforces declared non-null members and required constructor
fields before runtime validation. A missing society, cognition or inventory
object is invalid data, not an unexpected null-reference fault. Compatibility
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

Private checkpoint v2 stores verified 64×64 terrain-byte chunks. v1 per-tile JSON
remains readable and migrates atomically on load. Historical generators and
known package digests validate older generated maps without replacing their
resource layout. Damaged chunks are rejected without replacing the save. Restore also removes
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
unbridged one-tile crossings, and any open wade must match where that agent
stands. A save that fails these checks is refused with a reason and kept.

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
| Schema 35 | Containers, equipment, carts, gradual medical care, Port boats and journeys, business stock promises, livestock, physical knowledge goods and Town Hall councils. Current-format replay preserves ownership, consent, elections and underway travel together with fields, birth-model choices, housing, saved skills and expansion records. Actual new records require this format; their untouched empty legacy shells do not. |

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

A quantity-one physical map, field record or book retains its lot ID when inherited.
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
mechanism and must not delete another world's checkpoints. Updating autosave
configuration trims only that configured world, including rotation off.

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
- Named and rotating saves in `.manual`.
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
fields mean Normal and preserve the historical default generator. New-world
water defaults do not alter saved water values. Non-default maps require a
build that understands their options and validates their generated identity.

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

## Material and tool content

Town councils save their own membership, food policy, social laws, pending
rule ballot, election electorate and named votes. Representative terms last
one year under the saved world calendar, independently of biological life
pace. Save validation requires real adult Town residents and the physical
shared Hall for ballots and representative terms. A tie retains the existing
rule. Food policy never changes formal inventory ownership. The optional Town
council collection leaves legacy Townless council checkpoints readable; new
built-in Hall content still follows the alpha content compatibility policy.

The expanded Blacksmith recipes change its package digest. Fallen wood also
changes deterministic generated map identity. Worlds from earlier content may
be refused under the alpha policy; their files are preserved. This change adds
no private checkpoint schema field: tool wear uses each inventory lot's existing
condition, while finite deposits use saved ecology quantity. Current-format
checks exercise extraction, tool wear and repair, and a knife-assisted recipe
with its reserved inputs through save, load and continued simulation.

Ornament and physical combat-gear packages add recipes at the same Blacksmith.
The optional equipment record also saves individual weapon, shield, armor and
ornament lot references. References must remain personally owned, carried,
unreserved single items of the right kind. Current saves preserve refinement
and crafting reservations, equipping, gifts and consented ornament barter
across reload. Giving a worn ornament clears the donor's reference before a
checkpoint is published. No combat injury state is added. Older alpha worlds
may be refused when content identity differs; their files remain preserved.

## Physical paper and knowledge goods

Current knowledge items retain their writing House and completed input
reservations. A map or record consumes one paper; a book consumes two paper
and one cloth. The decoder refuses missing, reused or unconsumed supply proof,
or contents the author never learned. Fully consumed input lots can disappear;
their completed reservations remain the proof. Paper production consumes
contained water while preserving its jug.

Copies retain the exact earlier source item, its sites and original discoverers.
Reading or trading teaches only the receiving agent. Storage and inheritance
preserve the item identity without broadcasting its contents. Existing alpha
saves containing older free maps or records are refused before advancement,
and the files are kept; no paper-writing history is invented for them.

## Household animals and cargo

Current animal checkpoints retain each animal's identity, household, acquisition
reference, physical position, three care clocks, waiting product, rider and
named permissions. Missed care pauses production and riding. Natural death is
an explicit authority transition; the husbandry loop does not invent a death
schedule. Breeding and acquisition policy are separate from this saved state.
Horse cargo is actual inventory with an animal reference and ground position;
whole vessels and their contents keep that location and their original owner.
Decode rejects missing animals, displaced cargo, overloaded horses, invalid
riders and mixed milk/water jugs. Milk is consumed or cooked from its jug; moving
the liquid itself requires a whole-vessel operation.
