---
title: Build and test
type: development-reference
status: active
updated: 2026-10-03
---

# Build and test

This page is for contributors building the code. Players use the Windows
bundle; they do not need the Godot editor or a development SDK.
See [Playing](../playing.md).

## Toolchain

The server and simulation use C# 14 on .NET 10. The exact SDK baseline is
`10.0.401`, selected by [global.json](../../global.json); patch updates within
that feature band are permitted.

The Godot client uses the pinned Godot 4.7.2 .NET SDK and targets `net8.0`,
the desktop script target supported by that engine. This target belongs only
to the client; the server projects remain on .NET 10. The simulation must stay
runnable without Godot, a window manager, a model service or a network
connection once dependencies are restored. See [How it works](how-it-works.md).

The first export target is Windows 11 x64. The current export is an unsigned
portable bundle. It does not establish an installer, signing provider or public
release. A Windows smoke test and paired reconnect were recorded on 2026-09-21;
that historical check does not verify every later build.

xUnit is the test framework. External packages have committed NuGet lock files.
Use `--locked-mode` so an unexpected dependency change fails visibly.
Shared compiler, analyzer and version settings live in
[Directory.Build.props](../../Directory.Build.props). Use its version fields
rather than adding a second version constant.

The client bundles one third-party font, Fusion Pixel 12px, in
`src/ClankerWorld.GodotClient/UI/Theme/Fonts/`, under the SIL Open Font
License. Keep `fusion-pixel-OFL.txt` beside it; the export preset's include
filter copies that licence into the game data. Do not edit the font file: the
licence reserves the name Fusion Pixel for unmodified copies. The heading
lettering, Timber, is drawn in code in `UI/Theme/TimberFont.cs`.

## Which checks to run

| Change | Check |
| --- | --- |
| Docs or wording only | Check links, headings and accuracy. Run existing documentation checks when files, metadata or links change. No new tests or full runtime suite are required. |
| C# behavior | Format the changed code and run relevant tests; include the full Release suite for changes crossing simulation, host or save boundaries. |
| Godot scripts, scenes or UI | Run the Godot client check and relevant automated tests; inspect the affected controls in the game. |
| Windows export or device storage | Run the Windows export check and relevant native Windows checks. Test the actual bundle on Windows when claiming player usability. |
| Saves, events or compatibility | Include the replay and rollback checks described in [Saves and replay](saves-and-replay.md). During alpha, older saves need not keep loading, so no old-save or migration checks are needed; a save that cannot load must still be refused visibly and preserved. |
| Workflow automation (scripts, workflows, labels and templates in `.github/`) | Run `node --test .github/scripts/*.test.js`, and add or update tests when a script's behavior changes. For `.github/labels.json`, read the pull request's Labels check output to see what it would create, rename or delete; after merging, check that the Labels run on main passed. |
| Release | Follow the applicable [release gate](releasing.md#release-gate), including real player-path checks. |

The CI configuration in [.github/workflows/ci.yml](../../.github/workflows/ci.yml)
is the source for current automated gates. A PR needs green CI before merge;
local checks should fit the change. List checks you could not run and why.

### How CI runs

The Protect main ruleset requires three checks: `verify`,
`windows-documentation` and `windows-provider-storage`. `verify` passes only
when every part of the Verify workflow passes:

- **scope** decides which of the other jobs the change needs.
- **checks** runs the workflow-script tests and the label list, the Godot
  client check, `dotnet format` and the Windows export.
- **tests (1)** to **tests (4)** split the Release test suite between them, so
  it runs on four machines at once.

A pull request that changes only documentation (Markdown files and anything
under `docs/`) runs the workflow-script checks and the documentation tests on
Linux and Windows, and skips the rest, including `windows-provider-storage`.

A pull request that changes only documentation and Godot client files also
runs the Godot client check, `dotnet format` and the Windows export, and skips
the test jobs and `windows-provider-storage`. The test project compiles only
the client files its `<Compile Include>` lines name, so no other client file
can change a test result. A change to one of those files, to anything under
`tests/`, or to anything outside the client folder runs everything.

Pushes to main always run everything. A newer push to a pull request cancels
its older run.

The test jobs split the tests by how long each took in main's latest green run,
so new slow tests spread out on their own and nobody needs to rebalance them by
hand. Each test job uploads its durations as a `test-timings-<job>` artifact;
**scope** downloads main's latest set and
[.github/scripts/ci-plan.js](../../.github/scripts/ci-plan.js) plans the split:

- xUnit runs four test classes at once on a runner's four cores, and a class's
  tests one after another. A class too long for one job is split by method.
- A test runs in the first job whose filter names it, and the last job runs
  every test no filter names, so a test that is new since main's run, or
  renamed, still runs exactly once.
- If main's timings can't be read, the split falls back to the fixed one in
  `PinnedShards`.

No run can finish before its slowest single test. Each test job lists its ten
slowest tests in the run summary and warns about any test over five minutes.
Aim for under a minute per test: start a test close to the moment it checks,
in the smallest world that shows the behavior.

To time tests locally, or list what a job would run under the fixed split:

```bash
dotnet test tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj --configuration Release --logger "trx;LogFileName=timings.trx"
dotnet test tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj --configuration Release --no-build --list-tests --filter "$(node .github/scripts/ci-plan.js filter 1)"
```

The first command records each test's duration in
`tests/ClankerWorld.Simulation.Tests/TestResults/timings.trx`; the second lists
the tests job 1 would run without timings.

The native Windows storage job also runs the repeated checkpoint-compaction
test and checks that a refused overwrite preserves the previous checkpoint.
It uploads the test report as `windows-storage-evidence`. If the repeated-save
test encounters an I/O failure, that artifact also contains copies of its
disposable world files and first-failure diagnostics, including any temporary
checkpoint captured before cleanup. Successful test worlds are deleted. These
are synthetic test worlds; do not add a player's save directory to the upload
paths.
The deliberate lock and read-only controls mark their evidence with
`ExpectedFailureControl`, so their expected refusals remain distinguishable
from an unexpected stress-test failure.

To retain the same evidence for a local persistence test, set
`CLANKERWORLD_CHECKPOINT_DIAGNOSTICS` to an empty disposable directory before
running `FullyQualifiedName~PrivateWorldStateFileTests`. On I/O failure, the
tests copy evidence from their uniquely named temporary worlds there. A
passing rerun does not explain an earlier intermittent access refusal; retain
the failed report and its diagnostics when investigating one.

Hands-on checks above describe useful verification, not a blanket pre-merge
playtest gate. Routine owner playtesting may follow merge under
[Drafts and readiness](../../CONTRIBUTING.md#drafts-and-readiness). Keep pending
playtests explicit in the [playtest list](#windows-playtests); do not equate a
passing automated check with actual play.
The separate release gates still apply when preparing a release.

## Full build and test commands

From the repository root with the selected SDK:

```bash
dotnet restore --locked-mode
dotnet format --verify-no-changes --no-restore
dotnet test --configuration Release --no-restore
bash scripts/verify-godot-client.sh
bash scripts/verify-godot-windows-export.sh
```

For C# changes, run `dotnet format --no-restore` first if formatting needs
applying, then verify that no changes remain.

`verify-godot-client.sh` downloads the pinned engine archive, checks its
SHA-256, builds the scripts and starts the scene headlessly.
`verify-godot-windows-export.sh` verifies the pinned editor and templates,
creates an unsigned Windows x64 PE bundle and writes a SHA-256 manifest.
The export stages tracked files from the checkout's current commit, so commit
any changes you want in the bundle first. Uncommitted and untracked files stay
out of the export. It reads the game version from the staged project's MSBuild
metadata, then passes that commit as `SourceRevisionId` into the staging build.
It checks that the exported client assembly contains both values. The manifest
records the build line and full commit. Godot requires four numeric parts in
Windows file/product version fields, so those use the assembly file version;
the executable's product name carries the readable version and short commit.
Local builds get their revision from the SDK's Git integration. A source archive
without Git metadata must provide `SourceRevisionId` to MSBuild to identify its
origin; otherwise the game honestly reports `unknown`.
CI uploads that bundle as an artifact. These checks verify the build and
export; they do not replace playing the bundle on Windows.

The program icon, `src/ClankerWorld.GodotClient/icon.ico`, is generated from
the logo art in `UI/MenuLogo.cs` and embedded in the exported `.exe`. After
changing that art, rebuild the icon with
`godot --headless --path src/ClankerWorld.GodotClient -- --write-app-icon`.
The UI smoke test fails if the committed icon no longer matches the art.

## Focused documentation checks

```bash
dotnet restore tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj --locked-mode
dotnet test tests/ClankerWorld.Simulation.Tests/ClankerWorld.Simulation.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~DocumentationTests
```

These checks cover front matter on pages under `docs/`, local links and linked
headings in every Markdown file, and that each entry in `changes/` starts with
a `- ` bullet. They do not check that any particular page exists. The
documentation test reads each page with both LF and CRLF line endings, and CI
also runs it on a Windows checkout. That job is separate from a Windows game
playtest and from the native provider-storage checks.

## Windows playtests

The [playtest list](../../playtest/README.md) holds the merged changes that
still need trying by hand in the Windows game. Automated checks, including the
Windows CI jobs and a passing export, do not count as a playtest.

When you record a playtest result, note what you know of the client and host
commits, the Windows build, the date, the screen resolution and the interface
size it picked; ask the owner for missing details only when a check failed. Say
whether each check passed, failed or could not be run, with what you did and
saw. Leave out private keys, pairing codes, agent thoughts and raw model
replies. The agent receiving the owner's chat report updates the playtest
checklist and records these build details in any linked Bug or Implementation
issues, labelled `from:playtest`; do not create a separate report issue. Follow
the [playtest results procedure](../../playtest/README.md#keeping-the-list-current).

Do not deploy, change credentials or modify the active playtest save just to
run a check; that needs the owner's separate go-ahead. Use a disposable world
for setup, placement and damaged-save cases.
