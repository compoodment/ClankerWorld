using System.Diagnostics;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// Start options the launcher passes (#1566). <c>--developer-mode</c> turns on
/// F12 Developer tools; it works the same when the game is started by hand,
/// and the editor always has it. <c>--launcher=&lt;path&gt;</c> names the launcher
/// that started the game, so the Main Menu can offer Quit to Launcher.
/// </summary>
public partial class Main
{
    private const string DeveloperModeOption = "--developer-mode";
    private const string LauncherOption = "--launcher=";
    private readonly Button quitToLauncherButton = new();
    private readonly ConfirmationDialog quitToLauncherConfirmation = new();
    private const string OtherVersion = "other_version";
    private readonly ConfirmationDialog openInVersionConfirmation = new();
    private bool openLauncherOnQuit;
    private string launcherVersionOnQuit = BuildInformation.Version;
    private bool? developerMode;
    private string? launcherPath;
    private bool launcherPathRead;

    private bool DeveloperMode => developerMode ??=
        OS.HasFeature("editor") || OS.GetCmdlineUserArgs().Contains(DeveloperModeOption, StringComparer.Ordinal);

    /// <summary>The launcher that started this game, if it still exists.</summary>
    private string? LauncherPath
    {
        get
        {
            if (launcherPathRead) return launcherPath;
            launcherPathRead = true;
            var argument = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith(LauncherOption, StringComparison.Ordinal));
            var path = argument?[LauncherOption.Length..];
            launcherPath = !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && File.Exists(path) ? path : null;
            return launcherPath;
        }
    }

    private void BuildQuitToLauncher(VBoxContainer menu)
    {
        quitToLauncherButton.Text = "Quit to Launcher";
        StyleMenuChoice(quitToLauncherButton);
        quitToLauncherButton.Visible = LauncherPath is not null;
        quitToLauncherButton.Pressed += () => PopupDialog(quitToLauncherConfirmation);
        menu.AddChild(quitToLauncherButton);
        StyleConfirmation(quitToLauncherConfirmation, "Quit to Launcher?", "Quit to Launcher");
        quitToLauncherConfirmation.DialogText = "Save and close the game, then open the launcher?";
        quitToLauncherConfirmation.Confirmed += () =>
        {
            openLauncherOnQuit = true;
            QuitGame();
        };
        AddChild(quitToLauncherConfirmation);

        StyleConfirmation(openInVersionConfirmation, "Open in another version?", "Open in Launcher");
        openInVersionConfirmation.Confirmed += () =>
        {
            openLauncherOnQuit = true;
            QuitGame();
        };
        AddChild(openInVersionConfirmation);
    }

    /// <summary>
    /// The red Load World button for a world another version saved: save and
    /// close this game, and open the launcher with that version chosen. The
    /// launcher offers to download it if it isn't installed.
    /// </summary>
    private void OpenInSavedVersion(CatalogWorld world)
    {
        if (world.GameVersion is not { Length: > 0 } version || LauncherPath is null) return;
        launcherVersionOnQuit = version;
        openInVersionConfirmation.DialogText =
            $"ClankerWorld {version} last saved {world.Name}. Save and close this game, then choose {version} in the launcher to open it?";
        PopupDialog(openInVersionConfirmation);
    }

    /// <summary>What Load World says about a world another version saved.</summary>
    private string OtherVersionStatus(CatalogWorld world) => LauncherPath is null
        ? $"ClankerWorld {world.GameVersion} saved this world. Start that version to open it. Your save is safe."
        : $"ClankerWorld {world.GameVersion} saved this world. Choose Open in {world.GameVersion} to play it there. Your save is safe.";

    /// <summary>Opens the launcher with this version chosen, as the game closes.</summary>
    private void OpenLauncherIfAsked()
    {
        if (!openLauncherOnQuit || LauncherPath is not { } path) return;
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) };
            start.ArgumentList.Add("--");
            start.ArgumentList.Add("--choose-version=" + launcherVersionOnQuit);
            Process.Start(start)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            GD.PushWarning("launcher outcome=not_started");
        }
    }
}
