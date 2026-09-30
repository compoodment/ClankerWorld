---
title: Build and test
type: development-reference
status: active
updated: 2026-09-29
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
