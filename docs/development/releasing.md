---
title: Releasing
type: release-policy
status: active
updated: 2026-10-10
---

# Releasing

ClankerWorld is **unreleased**. A successful build or commit is not a public
release. Keep the game version separate from the compatibility versions used
by saves, replay, content packages and network contracts.

## Game version

Public releases use [Semantic Versioning](https://semver.org/) and annotated Git
tags beginning with `v`. The first experimental line is `v0.1.0-alpha.n`;
`v1.0.0` is a deliberate stable-product promise, not simply the first build
that starts. Compatible corrections to a released line use patch versions.
Individual commits do not need version bumps because their source revisions
already identify exact builds.

The current .NET version metadata comes from
[`Directory.Build.props`](../../Directory.Build.props), not a second hand-maintained
constant. See the [build reference](build-and-test.md) for the selected toolchain.
Untagged development builds use `0.1.0-dev`; they do not claim an alpha release.
The SDK includes the checked-out source revision in the assembly's informational
version. Settings and Developer tools show that version with seven commit
characters, and the tooltip gives the full commit. The host's `/api/v1/status`
reply reports its own `build`, `version` and full `sourceRevision`, without world
or device state. A tagged release must report both its game version and source
revision.

## Matching client and host builds

For an owner-authorized private-server update, follow the
[deployment checklist](private-server-deployment.md). It covers staging,
paused backups, accounting, credentials and rollback; a private update does
not require a public release or tag.

When an action's signed format changes, ship or deploy the matching host before
distributing the client. A bundled local host and client must come from the
same verified source revision. Record both revisions for a private deployment;
a successful reconnect alone does not prove that New World or another changed
action is compatible.

Before distribution, use an approved test device and a disposable world to
preview and create with the accepted Advanced settings at the real signed HTTP
boundary. Verify an older host produces the update explanation, preserves the
pairing and cannot receive a downgraded action. The New World client now requires
the host's authenticated v2 action advertisement, including on hosts that
already verified v2 before they advertised it. See [Device pairing](device-pairing.md#client-and-host-updates).

## Save and content compatibility

The public version does **not** decide whether a saved world loads. The
existing save/replay envelopes carry their own contract, simulation and schema
versions; generator, clock, content-lock and asset versions are separate where
relevant. Change each field only when its semantics change, and cover replay
in tests. Once a release promises that older saves load, cover old-save
handling and migration too; during alpha they are not required (see
[Saves and replay](saves-and-replay.md)). A cosmetic game-version bump is not a
save migration.

Content packages have their own versions and compatibility/dependency rules.
Do not infer package compatibility from an alpha game label. Build revision and
engine-release information may be recorded for diagnostics, but must not alter
canonical world state or replay digests. The [current feature summary](../what-works.md)
describes today's supported save behavior; historical schema-specific release
notes remain in Git history rather than a growing checklist here.

[Saves and replay](saves-and-replay.md) owns backup requirements, matching
application/save rollback and the explicitly approved pre-release identifier
reset. That exception never permits deleting saves. During alpha an older save
may stop loading, but it is refused with a reason and kept.

## Release gate

Prepare a release when the owner requests it; do not tag every merged change.
Before publishing:

1. Choose the version and update runtime/package metadata. Run
   `bash scripts/collect-changes.sh` to move the entries waiting in `changes/`
   into `CHANGELOG.md`; the script requires full Git history. Rename
   `## Unreleased` to the dated release heading and add a new empty
   `## Unreleased` above it. Keep each release as one flat list without
   categories.
2. Run the applicable build, test and Godot-export gates. The owner also runs
   the [Windows release smoke check](#windows-release-smoke-check) below.
   List every remaining file in `playtest/`, except its README, in the release
   notes as "not yet checked by hand".
3. If compatibility changed, verify replay and rollback from a matching backup.
   Also verify migration and old-save handling when the release promises that
   older saves load.
4. After merging main into the release branch for the last time, run
   `bash scripts/collect-changes.sh` again. Move every newly collected bullet
   from `Unreleased` to the start of the dated release section, leaving
   `Unreleased` empty. Merge the release changes through the
   [contribution review process](../../skills/review-merge/SKILL.md).
5. The session that prepared the release fetches main and verifies the reviewed
   release PR's squash commit on GitHub's `origin/main`. Confirm its required
   checks passed and that `git ls-tree --name-only <commit> changes/` lists
   only `changes/README.md`. If it still has entries, collect them through a
   reviewed follow-up before tagging; do not tag an uncollected commit.
6. That preparing session creates and pushes the annotated tag for the verified
   release commit, then verifies that GitHub resolves it to that commit.
   Never move or delete a pushed tag. Publish a GitHub release when there is a
   distributable artifact or useful release note. For an alpha, attach the
   [portable Windows zip](build-and-test.md#portable-windows-package) and its
   `.sha256` built from the tagged commit, keeping their file names: the
   launcher finds a game release by its `v<version>` tag and those two assets,
   and refuses a package whose SHA-256 differs from the `.sha256` file.
7. The launcher has its own releases, tagged `launcher-v<launcher version>`
   with the [launcher zip](build-and-test.md#windows-launcher). Publish one only
   when the launcher changed. Players download it themselves: the launcher
   never updates itself, it only says that a newer launcher exists.

## Nightly builds

The [Nightly workflow](../../.github/workflows/nightly.yml) publishes a build of
main every night as a GitHub pre-release, so Developer mode can try main
without a release (the owner's choice, October 10). It is not a release and
needs no release gate:

- It is tagged `v<version>-nightly.<date>.<run>`, such as
  `v0.1.0-nightly.20261011.12`, and the game reports that version. It carries
  the same two assets as an alpha, so the launcher installs and checks it the
  same way, but lists it only in Developer mode.
- It runs only when main has a commit the newest nightly doesn't have and
  main's CI passed on that commit. Run it by hand from Actions to publish one
  sooner.
- It keeps the seven newest nightly builds and deletes older ones with their
  tags, the one exception to never deleting a pushed tag. Never use a nightly
  tag for anything else.

### Windows release smoke check

Before each release, the owner checks the actual Windows bundle on an approved
test device and disposable world:

- Start the game. From the portable zip, it starts its own host and pairs
  without a code; otherwise pair with the matching host.
- Create a New World.
- Load a saved World.
- Save, then quit.

Record the build and each result. A failed check gets a Bug issue and blocks
the release unless the owner explicitly accepts it. This short release check
does not claim that the remaining `playtest/` checks were performed.

Add a changelog entry in `changes/` in the same commit as player-visible
gameplay/UI, world-runtime, save-compatibility, deployment, packaging or
security changes; see [the contribution rules](../../CONTRIBUTING.md#keep-documentation-and-the-changelog-useful).
Documentation-only and test-only edits need no changelog entry unless they
alter an explicit supported promise.
