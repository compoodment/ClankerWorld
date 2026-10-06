---
title: Private-server deployment
type: development-reference
status: active
updated: 2026-09-30
---

# Private-server deployment

Use this checklist for an owner-authorized update of a private server. It
describes a manual operation; there is no staged deploy command yet. Building
or merging a PR does not authorize updating the active playtest host.

The operator records each step as passed, failed or not performed, with the
source commit, artifact hashes and bounded outcomes. Keep that record private
when it identifies an installation. Never include keys, pairing codes, save
contents, private thoughts or raw model replies.

## Prepare the update

1. Confirm the destination host, service, OS account and authorized maintenance
   window. Record the running commit and the proposed commit. Inspect the actual
   service configuration privately; the checked-in
   [systemd template](../../deploy/clankerworld-viewer.service) is a starting
   point, not evidence of what is running. Its historical fresh-install comment
   is not permission to replace existing state or pairing authority.
2. Build from a clean checkout at the proposed commit. Run the applicable
   [build and test checks](build-and-test.md#which-checks-to-run), including
   locked restore, formatting, Release tests, Godot checks and Windows export
   when distributing a matching client. Publish the server from that same
   checkout with `dotnet publish src/ClankerWorld.Viewer -c Release`. Record the
   publish directory, game version and SHA-256 hashes of the exact artifacts.
3. Check save, generator and signed-action compatibility against the current
   host. During alpha an older save may be refused, but must be preserved.
   Obtain the owner's explicit choice before a fresh-world reset. Never
   discover an incompatible save by replacing the running application first.
   See [Saves and replay](saves-and-replay.md) and
   [Matching client and host builds](releasing.md#matching-client-and-host-builds).
4. Stage the verified binaries in a separate versioned directory. Keep the
   existing binaries and service configuration available for rollback. Check
   free space for the complete private backup and staged release, and that the
   intended service account can read the release and write its state paths.
   Stop if a path is ambiguous or the rollback copy is incomplete.

## Locate and preserve installation state

Resolve absolute paths from the actual service environment and configuration,
including relative paths against its working directory. Keep state outside the
versioned binary directories. The relevant configuration keys are:

| Configuration key | What to preserve |
| --- | --- |
| `ClankerWorld:Runtime:StatePath` | Active private-world checkpoint and its adjacent `.history`, `.manual`, `.autosave.json` and `.worlds` storage |
| `ClankerWorld:Pairing:StatePath` | Durable owner-device authority and revocations |
| `ClankerWorld:Runtime:ProviderStatePath` | Installation model assignments and protected credentials |
| `ClankerWorld:Runtime:ProviderUsagePath` | Installation-lifetime paid-attempt accounting |
| `ClankerWorld:Assets:CatalogPath` | Approved asset catalog and the assets it refers to |

When `ProviderUsagePath` is unset, the current host uses `provider-usage.json`
beside the provider configuration file. An older process or different working
directory may have used another location. Identify all possible old and new
meter locations before switching; a missing file is not proof that no calls
were spent. Back up private service configuration and environment files too.
See [Checkpoints, history and backups](saves-and-replay.md#checkpoints-history-and-backups)
for the complete backup set.

1. Pause through the approved client's signed control path. Confirm the pause
   was durably acknowledged and record the world identity and tick. A client
   disconnect alone is not a durable checkpoint acknowledgement.
2. Stop the service, confirm its process has exited, and prevent an automatic
   restart during the update. Copy the whole resolved backup set while no
   writer is active. Copying a running world is not a coherent backup.
3. Verify the backup can be read by the intended recovery account, has
   restrictive permissions and contains every referenced history segment,
   catalog world and checkpoint. Check hashes privately, without printing
   file contents. Keep the untouched originals until verification succeeds.

## Move accounting and credentials safely

Record spent attempts, the configured limit and pending reservations before
changing the meter location. Preserve the entire meter, including pending
reservations; do not turn an interrupted request into unused allowance. On
startup, the current host records interrupted reservations as abandoned while
keeping their spent attempts.

- If both locations contain accounting, stop. Do not pick the smaller, newer
  or more convenient file, concatenate them, or reset either one. Reconcile
  the histories in separately reviewed work before allowing paid calls.
- If a meter cannot be read, paid calls stay blocked. Preserve the file and
  use the recovery message; deleting it would start accounting at zero.
- A binary rollback must keep the latest valid accounting. Never restore an
  older, lower meter from the world backup to make the old binary start.
- Keep credentials in installation storage, outside world saves. Check that
  protected credentials can be read by the intended OS account using the
  supported credential setup. A copied encrypted file may depend on its
  original account or OS. If it cannot be read, leave the model unavailable
  and let the owner set it up again; do not export plaintext keys.

## Switch and verify while paused

1. Point the service at the staged release and the verified state paths, then
   start it. Record the running process and exact binary hashes, and compare
   them with the staged artifacts. A responsive port alone is insufficient.
2. Check state ownership and permissions. Confirm that no fallback state or
   fresh meter appeared inside the new release directory. Check the bounded
   startup results for checkpoint, history, authority, credentials and meter
   loading. An unsupported or damaged save must remain intact.
3. Reconnect with the existing approved Windows device. Verify the signed
   handshake and required action formats, the same world identity, the saved
   paused tick and model assignments. Inspect and reload a disposable current
   checkpoint to check persistence; do not overwrite the active playtest save
   just to collect evidence.
4. Confirm time and paid attempts remain unchanged through paused observation
   refreshes. Verify missing-key, unreadable-meter and exhausted-limit cases
   using disposable synthetic state with a fake provider. Do not probe a real
   paid model merely to establish readiness.
5. Report the update and any failed checks. Only a separately explicit owner
   Resume starts time and model calls. Keep client and host revisions in the
   record; deployment, export and actual playtesting are different checks.

## Roll back or stop

If readiness fails, keep the world paused and stop the new service. Preserve
the failure evidence and all state it wrote before changing binaries again.
Restore the previous binaries and service configuration. Use the latest state
only if the previous binary can read it; otherwise use the matching paused
pre-update world/history backup with the owner's authorization. Preserve the
newer state separately. Accounting always stays at its latest valid spent
total, even when world state goes back.

If the old binary cannot safely read the latest accounting or credentials,
leave paid calls blocked and stop for repair. A rollback is not permission to
delete state, re-pair silently, reduce spent usage or start a new world.
Repeat the paused verification above before considering the rollback usable.

## Rehearse before a live update

Use disposable synthetic installation state, an isolated service and a fake
provider. Exercise failure before service stop, after backup, while moving
accounting, before readiness and after the first new-format save. Check that
each failure preserves the original files, leaves a usable recovery path and
never lowers spent attempts or starts unattended paid work. Record what the
rehearsal actually checked; this checklist is not a completed rehearsal.

A future command can automate plan, stage, apply, verify and rollback after the
local Windows package works. Its remaining contract is tracked in
[#267](https://github.com/compoodment/ClankerWorld/issues/267); no such command
is implied by this guide.
