---
title: Releasing
type: release-policy
status: active
updated: 2026-09-29
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
A tagged release must report both its game version and source revision.

## Save and content compatibility

The public version does **not** decide whether a saved world loads. The
existing save/replay envelopes carry their own contract, simulation and schema
versions; generator, clock, content-lock and asset versions are separate where
relevant. Change each field only when its semantics change, and cover old-save
handling, migration and replay in tests. A cosmetic game-version bump is not a
save migration.

Content packages have their own versions and compatibility/dependency rules.
Do not infer package compatibility from an alpha game label. Build revision and
engine-release information may be recorded for diagnostics, but must not alter
canonical world state or replay digests. The [current feature summary](../what-works.md)
describes today's supported save behavior; historical schema-specific release
notes remain in Git history rather than a growing checklist here.

[Saves and replay](saves-and-replay.md) owns backup requirements, matching
application/save rollback and the explicitly approved pre-release identifier
reset. Do not use that one exception as permission to discard later saves.

## Release gate

Prepare a release when the owner requests it; do not tag every merged change.
Before publishing:

1. Choose the version, update runtime/package metadata and move relevant
   `CHANGELOG.md` entries into a dated release section, leaving `Unreleased`.
2. Run the applicable build, test, Godot-export and Windows playtest gates.
   Check a real player path, not only isolated simulation fixtures.
3. If compatibility changed, verify migration, old-save handling, replay and
   rollback from a matching backup.
4. Merge the release changes through the [contribution review process](../../CONTRIBUTING.md#review-and-merge),
   then fetch and verify the intended commit on GitHub's `origin/main`.
5. Create and push the annotated tag, then verify that GitHub resolves it to
   the intended commit. Publish a GitHub release when there is a distributable
   artifact or useful release note.

Update `CHANGELOG.md` under `Unreleased` in the same commit as player-visible
gameplay/UI, world-runtime, save-compatibility, deployment, packaging or
security changes. Documentation-only and test-only edits need no changelog
entry unless they alter an explicit supported promise.
