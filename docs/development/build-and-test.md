---
title: Build and test
type: development-reference
status: active
updated: 2026-10-06
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
- **tests (1)** to **tests (6)** split the Release test suite between them, so
  it runs on six machines at once.

**test-times** then compares the run's test times with main's latest green run
and lists the tests that grew most in the run summary. It warns when the tests
both runs have take 40% and two minutes longer in total; tests that slow down
without changing usually mean the simulation got slower, for players too. It
never warns about a single test: one test can take two to four times as long or
as short between runs of the same code, depending on which tests share the
runner with it, while the total varies by about a tenth. This job is not part
of `verify` and never blocks a merge on its own.

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

### Trace unexpected Windows checkpoint failures

For the unexplained overwrite investigation in [#778](https://github.com/compoodment/ClankerWorld/issues/778),
[Trace Windows checkpoints](../../.github/workflows/windows-checkpoint-trace.yml)
is a manual Windows job. Choose `target` for the unchanged 2,500-save test,
with one to twenty runs, or `full-suite` for one unfiltered Release run with
SingleHit coverage, matching the original failure's instrumentation. It first
runs the existing three intentional overwrite-refusal controls to check the
native trace, including an access-denied operation completion for each
synthetic fixture in the test process. It builds once, uses four test processors
and records all six
Simulation/Viewer/test DLL and PDB hashes before and after the unchanged tests.

On an administrator Windows machine with PowerShell 7 and the pinned .NET SDK:

```powershell
./scripts/trace-windows-checkpoints.ps1 -OutputDirectory C:/temp/checkpoint-trace-new
./scripts/trace-windows-checkpoints.ps1 -Mode full-suite -OutputDirectory C:/temp/checkpoint-trace-full-new
```

Use a new empty output directory each time. Windows' built-in `logman` records
kernel file activity; `tracerpt` decodes it. The trace is circular and limited
to 128 MB. If an unexpected escaping checkpoint exception appears in the
existing observer's evidence, the runner freezes the trace while the unchanged
tests finish. A failed run stops the iteration loop and keeps its failure
status; it does not replace that result with a successful rerun.

The workflow uploads only synthetic checkpoint events, test reports, binary
hashes and existing exception evidence as `windows-checkpoint-trace`. Raw ETL
and decoded whole-machine XML stay in the local `raw` directory, outside the
upload paths. The filtered event file keeps original native XML and resolves
file-object, file-key and IRP identities to synthetic checkpoint paths. The
runner retires file identities on close/delete and IRPs at operation completion
to limit reuse of old associations. Resolved paths are inferred correlations;
always inspect the original fields before attributing an operation.
In a passing full-suite run, earlier checkpoint activity may have fallen out
of the circular window; zero selected events then means no retained checkpoint
activity, not proof that the original failure is fixed. The
trace summary records its capture statistics; check it for lost events or a
truncated window before treating missing activity as evidence.

Compare the unexpected exception's exact path, process, time and operation
with the matching native events. Keep intentional controls separate using
`ExpectedFailureControl`. A matching path or passing target alone does not
identify the cause, and tracing can change I/O timing. Keep the original failed
capture even when a separate control passes. The script changes no persistence
code, durability checks or test assertions.

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

## Disk space

Sessions on one machine share the tools they download, and each worktree keeps
its own build output. A shared tool cache, a scratch folder for each run and a
cleanup script keep the disk from filling up, without weakening any check.

### Shared tool cache

The Godot scripts download their pinned tools through
[scripts/tool-cache.sh](../../scripts/tool-cache.sh), into
`~/.cache/clankerworld/tools` (under `$XDG_CACHE_HOME` when it is set). Set
`CLANKERWORLD_TOOL_CACHE` to move it.

- Each entry is keyed by its pinned SHA-256, so every version and platform has
  its own. A download is checked when it arrives and each time it is used, and
  a damaged copy is downloaded again.
- Each archive is unpacked once, made read-only and shared by every worktree
  and session.
- A lock lets several sessions use the cache at the same moment. A changed pin
  downloads the new version; the cleanup later removes the old one.
- Without `flock`, the cache uses an atomic directory lock. A stale or
  incomplete lock stops the run and names the directory to inspect; it never
  guesses that another session's lock can be removed.

### A scratch folder for each run

Each Godot check gets its own temporary folder for Godot's settings, caches and
the game's `user://` files, and removes it when it ends, so runs from different
worktrees or sessions share no state. Build output (`bin/`, `obj/`, `.godot/`)
stays in each worktree, so one worktree's build never affects another's, and
the Windows export still builds only from committed files.

### What to keep

- Put test reports, coverage, logs and other evidence for your work in
  `.evidence/` in your worktree, which git ignores.
- Put what is worth keeping after the work ends in `.evidence/keep/`: evidence
  of a failure, and records a pull request or issue cites that can't be
  recreated.
- Everything else can be rebuilt and goes when the work is finished: build
  output, exports, per-test coverage reports and the output of runs that
  passed.

### Cleanup

`scripts/clean-workspace.sh` lists what can go and why; `--apply` removes it.
Run it when you finish a job on your own machine.

- It removes a worktree only when its work is finished (its branch on origin
  was deleted, as GitHub does after a merge, or its commit is on main), it has
  no uncommitted, untracked or local files such as `.env` or `saves/`, it isn't
  locked or the one you run it from, and nothing in it changed for 12 hours.
  It checks branch deletion on origin directly, and keeps missing or unmounted
  worktree records, unfinished Git operations, hidden local edits and commits
  held only in a worktree's reflog.
  The branch stays, so `git worktree add <path> <branch>` brings the files
  back. `.evidence/keep/` first moves to
  `~/.local/state/clankerworld/kept-evidence`. Archives older than 30 days are
  listed but kept. Inspect them before adding `--prune-kept-evidence` to
  `--apply`; that flag explicitly selects old archives for removal.
- In other idle worktrees it removes only build and test output.
- It removes tool versions no script pins that went unused for 30 days,
  holding the same lock as cache users and checking the age again. Unknown
  names in overridden cache or evidence folders are kept, as are unknown
  files in the older Godot download cache. Interrupted partial downloads are
  cleaned after a day.
- It removes idle managed Godot scratch folders only when their saved owner
  process has ended. Measurements, failure worlds, unmarked older temporary
  folders and live runs are kept for inspection.
- `git worktree lock <path>` keeps the cleanup away from a worktree, for
  example during a long pause.

It covers only the worktrees of the clone holding this script, and refuses
to run from an unrelated repository. Run that clone's copy in each separate
clone. Retention arguments must be positive whole numbers.

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
