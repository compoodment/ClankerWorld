---
title: Saving and recovery
type: game-design
status: active
updated: 2026-10-08
---

# Saving and recovery

These are the intended game rules and choices. They do not describe
everything that is available in the current build. See [what works today](../what-works.md).

[Game-design guide](README.md) explains the agreement labels.

## On this page

- [Saving and recovery](#saving-and-recovery)
- [Questions linking these systems](#questions-linking-these-systems)

## Saving and recovery

### Agreed

- Every world has its own save state. **Autosave is on by default**, initially
  every **5 minutes**, with **5 rotating autosaves** by default. Settings offer
  autosave off/on, intervals of 1, 2, 5, 10, 15, or 30 minutes, and rotation
  off/3/5/10. These are initial choices subject to performance playtesting;
  they live in the current world's **World Settings**, as agreed on October 8
  ([#1276](https://github.com/compoodment/ClankerWorld/issues/1276);
  [Interface and art](interface-and-art.md#main-menu-world-view-and-controls)).
  **Agreed on October 8
  ([#1275](https://github.com/compoodment/ClankerWorld/issues/1275)):**
  autosave runs on its schedule whatever model requests are in flight; see
  [slow models](world.md#world-time-pausing-and-slow-models) for what happens
  to those requests.
- Named manual saves are unlimited. Emergency recovery is automatic in the
  background. Saving on Quit to Menu/Game is the accepted direction, while
  quit confirmation remains.
- **Crash promise, agreed on October 8
  ([#1250](https://github.com/compoodment/ClankerWorld/issues/1250)):** if the
  game or the computer crashes, the player loses at most about the last second
  of play. If the latest saved state is damaged, a recovery screen on restart
  offers the last good autosave.
- The player must be able to **intentionally overwrite an existing named
  manual save** rather than accumulating a new checkpoint every time. In Save
  World, select an existing checkpoint and confirm overwriting that specific
  checkpoint; creating a new save remains a separate action. Typing a matching
  name alone must not replace an existing save. **Agreed on October 8
  ([#1275](https://github.com/compoodment/ClankerWorld/issues/1275)):**
  overwriting keeps a visible **Before overwriting** copy of the replaced save
  until the player deletes it.
- Load World should show an existing save's assessed compatibility and warn
  before selection when it cannot load. The assessment must use actual
  save/version and required-content checks.
  **Block loading only when an actual compatibility check establishes that
  the save cannot be loaded**; a version difference or old timestamp alone is
  not evidence of incompatibility. Preserve a blocked save rather than
  deleting or overwriting it. Development playtest saves may legitimately
  become incompatible as formats change; migration is not a current alpha
  promise. For finished stable releases, support forward migration from the
  immediately preceding supported stable save format when one exists. Write
  and validate a new save while retaining the unmodified original; select
  the migrated copy only after validation succeeds. Publish the actual tested
  support window with each release, rather than promising all old formats or
  downgrades. An unknown assessment must remain distinct from a proven
  incompatibility. Missing required content or a mod needs a clear, resolvable
  explanation rather than silently discarding its state. **Agreed on October 8
  ([#1272](https://github.com/compoodment/ClankerWorld/issues/1272)):** a save
  that needs a mod that is missing or has changed does not load until the mod
  it needs is back, and the save is kept unchanged. The rest of that mod rule,
  such as each world locking the exact mod versions it uses, is in
  [Inventions and mods](inventions-and-mods.md#inventions-mods-and-technology).
- World-created inventions and active mods travel with that world's save;
  provider credentials and graphical/device settings are global. The global
  library reads the latest save for each world rather than silently merging
  their creations. **Agreed on October 8
  ([#1272](https://github.com/compoodment/ClankerWorld/issues/1272)):** for a
  world with several branches, it reads the latest save of the branch that
  world continues from.
- **Save branches, agreed on October 1
  ([#647](https://github.com/compoodment/ClankerWorld/issues/647)):** loading
  an older save and playing on starts a **new branch** of that world instead
  of rewinding the original. Every save belongs to one branch, and the saves
  made after the loaded point stay in their original branch, unchanged.
  Load World shows each world's branches, so the player can tell which saves
  belong to which version of events, and each branch has its own latest save.
  For example, loading **Before the flood** after saving **Big harvest** keeps
  **Big harvest** in the first branch; **Hungry winter**, saved after playing
  on, belongs to the new one. Load Save and Save World draw a world's
  branches as a timeline; [Interface and art](interface-and-art.md) records
  its look. **Agreed on October 8
  ([#1251](https://github.com/compoodment/ClankerWorld/issues/1251)):** a branch
  is labelled after the save it started from, such as **From Before the
  flood**; renaming branches can come later. Autosave rotation stays per
  branch, so each branch keeps its own rotating autosaves; how much disk this
  uses is handled by the disk-space rule under
  [Explicit deletion](#explicit-deletion).

**Agreed development playtest policy:** computment does not require old
playtest worlds to remain loadable as the New World flow and save format change;
they expect to create fresh worlds to test new features. No migration code is
written only to keep an alpha save loading. An alpha build opens private-world
saves only when they use its current checkpoint schema; an older schema is
refused with a reason and its file is kept unchanged. This is not permission to
delete or overwrite a save. Finished stable releases follow the migration rule
above.

### Explicit deletion

The owner may permanently remove one selected named save or autosave after a
confirmation. Whole-world deletion is a separate action covering all of that
world's snapshots. The active/only world is blocked until the owner opens or
creates another world. Manual saves and the unmodified originals kept before
migration remain until the owner explicitly deletes them. Recovery-history
cleanup is a separate, **opt-in** option, off by default: first show the exact
recoveries proposed for removal and keep the latest verified recovery for each
named save. The existing autosave rotation is separate and does not authorize
deletion of manual saves or migration originals. Age alone must not trigger
deletion.

**Agreed on October 8
([#1252](https://github.com/compoodment/ClankerWorld/issues/1252)):** the
game warns when free disk space is low before any save, and the warning never
blocks emergency recovery. The recovery-history cleanup is count-based: it
keeps a set number of copies for each save, rather than working to a size
limit. The number of copies and the free-space level that triggers the
warning are provisional, for playtesting.

### Still to decide

**Parked until ClankerWorld becomes a public, versioned alpha
([#1263](https://github.com/compoodment/ClankerWorld/issues/1263)):** which
save formats older than the immediately preceding stable one a stable release
will open. The owner is the only player for now, so this waits until then.

## Questions linking these systems

These remain open; they are not new decisions.

- **Pending model work versus continuous simulation/saves.** One slow model
   must not freeze the world, but conversations, inventions, and post-death
   wills can remain unresolved while ticks and autosaves continue. Quit and
   pause now share an accepted cancel/discard-and-reconsider rule, and
   autosave runs on schedule (agreed October 8); still define durable
   pending states, deadlines/fallbacks, and
   completion that cannot apply twice so crashes cannot duplicate or lose estate transfers
   or other important actions.
