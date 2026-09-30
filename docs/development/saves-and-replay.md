---
title: Saves and replay
type: persistence-reference
status: active
updated: 2026-09-30
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

Private checkpoint v2 stores verified 64×64 terrain-byte chunks. v1 per-tile JSON
remains readable and migrates atomically on load. Historical generators and
known package digests validate older generated maps without replacing their
resource layout. Damaged chunks are rejected without replacing the save. Restore also removes
Road tiles inside validated saved building footprints, leaving other Roads and
state intact. The repair applies once and may expose an already broken Road
connection; it does not reroute Roads or create bridges. Back up older saves
before an upgrade.

| Compatibility change | Meaning |
| --- | --- |
| Schema 18 | Removes persisted energy/sleep state. Legacy bedding can remain inert compatibility data; recipes cannot restart sleep gameplay. |
| Schema 19 and checkpoint v2 | Compact verified terrain chunks rather than per-tile checkpoint JSON. |
| Schema 21 | First-Town identity, founding state, membership, assigned buildings and borders. Older setup worlds reconstruct only known founding facts. |
| Schema 22 | Optional agent-owned Jev memory-ranking indexes. Older saves do not invent indexes. |
| Schema 23 | Personal map facts and physical map/record artifacts. Earlier compatible saves start with empty personal knowledge. |
| Schema 24 | World-owned Roads. |
| Schema 25 | Optional selected first-Town origin; older Towns keep their camp-derived border. |

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
pre-release fresh-save/new-pairing exception; it is not a future permission to
discard players' saves.

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
map. Private saves now use schema 26; episode-bearing world systems use schema 2.
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
