---
title: Saving and recovery
type: game-design
status: active
updated: 2026-09-29
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
  their proposed home is the current world's **World Settings**.
- Named manual saves are unlimited. Emergency recovery is automatic in the
  background. Saving on Quit to Menu/Game is the accepted direction, while
  quit confirmation remains.
- The player must be able to **intentionally overwrite an existing named
  manual save** rather than accumulating a new checkpoint every time. In Save
  World, select an existing checkpoint and confirm overwriting that specific
  checkpoint; creating a new save remains a separate action. Typing a matching
  name alone must not replace an existing save. Recovery/backup behavior after
  overwrite remains open.
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
  explanation rather than silently discarding its state.
- World-created inventions and active mods travel with that world's save;
  provider credentials and graphical/device settings are global. The global
  library reads the latest save for each world rather than silently merging
  their creations.

### Explicit deletion

The owner may permanently remove one selected named save or autosave after a
confirmation. Whole-world deletion is a separate action covering all of that
world's snapshots. The active/only world is blocked until the owner opens or
creates another world. Manual saves and the unmodified originals kept before
migration remain until the owner explicitly deletes them. Recovery-history
cleanup is a separate, **opt-in** option, off by default: first show the exact
recoveries proposed for removal and keep the latest verified recovery for each
named save. The existing autosave rotation is separate and does not authorize
deletion of manual saves or migration originals. A specific cleanup budget
still needs measurement and a choice; age alone must not trigger deletion.

### Still to decide

Whether manual saves are checkpoints or divergent branches; restore/rewind
behavior; disk-space warnings and the opt-in recovery-history budget; exactly
when autosave occurs relative to model/conversation work; crash-recovery
guarantees; and compatibility outside the preceding supported stable format
or across changed mod requirements.

**Agreed development playtest policy:** computment does not require old
playtest worlds to remain loadable as the New World flow and save format change;
they expect to create fresh worlds to test new features. This is not permission
to delete or overwrite an active save and does not settle finished-release
compatibility policy.

## Questions linking these systems

These remain open; they are not new decisions.

- **Pending model work versus continuous simulation/saves.** One slow model
   must not freeze the world, but conversations, inventions, and post-death
   wills can remain unresolved while ticks and autosaves continue. Quit now
   has an accepted cancel/discard-and-reconsider rule; still define durable
   pending states, autosave/manual-pause behavior, deadlines/fallbacks, and
   completion that cannot apply twice so crashes cannot duplicate or lose estate transfers
   or other important actions.
