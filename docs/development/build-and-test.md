---
title: Build and test
type: development-reference
status: active
updated: 2026-09-30
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
| Saves, events or compatibility | Include old-save handling, migration, replay and rollback checks described in [Saves and replay](saves-and-replay.md). |
| Release | Follow the applicable [release gate](releasing.md#release-gate), including real player-path checks. |

The CI configuration in [.github/workflows/ci.yml](../../.github/workflows/ci.yml)
is the source for current automated gates. A PR needs green CI before merge;
local checks should fit the change. List checks you could not run and why.

Hands-on checks above describe useful verification, not a blanket pre-merge
playtest gate. Routine owner playtesting may follow merge under
[Drafts and readiness](../../CONTRIBUTING.md#drafts-and-readiness). Keep pending
playtests explicit; do not equate a passing automated check with actual play.
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

These checks cover the required pages, front matter, local links and linked
headings. The Windows documentation CI job also checks LF and CRLF line
endings. It is separate from a Windows game playtest and from the native
provider-storage checks.

## Windows paired-world verification record

Use this protocol for [#285](https://github.com/compoodment/ClankerWorld/issues/285).
It is a checklist for collecting evidence, **not a report of completed playtests**.
Source and headless checks do not fill the Windows result column. Do not deploy,
repair accounting, alter credentials or modify the active playtest save merely
to run this checklist: obtain the separate operational authorization first.
Use a disposable world for setup, placement and damaged-save cases.

Record client commit/export digest, host commit, Windows build, date, viewport,
render resolution and UI scale. For each case record Pass/Fail/Blocked, exact
steps, observed result, and a screenshot or bounded diagnostic reference with
private keys, pairing codes, thoughts and raw provider payloads excluded. Keep
failures linked to their focused issue; a successful unrelated case cannot close
an entire multi-case report. Keep live operational evidence separate from client
presentation evidence when the deployed server predates the client.

| Case | Reproduction and pass condition |
| --- | --- |
| Hosted decisions and accounting | After the separately authorized meter migration, explicitly resume with an authenticated client. Confirm each configured founder can receive an accepted model choice and a new private thought; note waiting, limit or provider errors without claiming every idle action is a failure. Pause afterward. Reconcile the known phantom reservations separately; do not count them as paid calls or erase them as part of this UI check. |
| Fresh Town, no legacy camp | Create a disposable fresh world, inspect before site choice, accept a Town site and inspect again. No legacy camp objects or camp-derived border should appear; exactly the accepted generated layout should remain. |
| Stable Town and agent text | Pause, open Town, wait across several refreshes, switch agents and reselect the first. Contents, relationships and existing thought/memory text stay visible; an unchanged refresh must not clear them. |
| Agent card at 720p | Select and Find agents near every screen edge at 1280×720. The selected marker remains visible and Speak/Send controls are reachable by scrolling, without controls extending off-screen. |
| Status lifetime | Enter Town-site and founder-move modes and wait through refreshes. Instructions remain while the mode is active; submit a refused request and confirm its explanation remains readable without a false disconnect. |
| Initial camera | Open a new unset world and a saved Town near a wrapping seam. The initial view shows relevant dry land/Town/agents, not an arbitrary open-sea center or the wrong side of a seam. |
| Tile card and short labels | Select ground near the bottom edge; inspect at several zooms. The complete card fits, absent facts are omitted, and tiny marker labels disappear rather than render fragments. |
| Modal input | Open Pause Menu and try top-bar Start/Pause/founder actions. The modal blocks them. In Town-site mode confirm Cancel is visible; inspect pairing/back controls and Settings caption alignment. |
| Keyboard focus | Click a top-bar button, then pan with arrow keys and use Space. Arrows pan rather than cycle focus, Space toggles pause once, Tab/Enter still reach controls. Evaluate diagonal movement while held, and compare the F1 list with actual shortcuts. |
| Disconnected land | In a disposable world add an adult on land without a food route to the Town, then reconnect. Observation remains available and reports the missing route; Main Menu still accepts pause even if a later refresh fails. |
| Preview while running | With a disposable current world running, request New World preview. Preview succeeds without changing current-world identity/state; Create still performs its separate pause/switch flow. |
| Main Menu Settings pointer | From Main Menu open Settings and change an installation preference with the mouse. The overlay must not swallow input. Restore the preference after recording the result. |
| Hover and marker priority | At several zooms hover bare ground and multiple same-tile agents. Ground outline is visible, each agent is individually selectable, and nearby tiles do not activate its marker. |
| No Main Menu World Settings | With no loaded world, open Main Menu Settings. No World Settings category or hidden navigation path enters a world. |
| Terrain seams | Inspect contiguous terrain at representative zooms. No black tile-gap grid appears; this does not approve provisional textures as final art. |
| Hover and condition stability | Hold the pointer over an agent through several observations while its card is open. Tooltip and warmth/illness/diet/equipment stay visible without per-refresh flicker. |
| Slow provider | Using a separately authorized controlled delay or an unavailable test endpoint, keep the client connected while a model remains pending. Other agents/world systems continue; pause and reload must reject the old reply. Never prolong real paid calls solely to create this test. |

**Exit criterion:** each applicable row has evidence against the stated builds.
Blocked live migration or missing Windows access remains Blocked, not Pass. The
owner's review of this protocol does not approve a deployment or certify the build.
