using System.Diagnostics;
using ClankerWorld.GodotClient.Launcher;
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
    private readonly ConfirmationDialog launcherStartFailure = new();
    private const string OtherVersion = "other_version";
    private readonly ConfirmationDialog openInVersionConfirmation = new();
    private string? pendingOpenVersion;
    private string? failedLauncherVersion;
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
        quitToLauncherConfirmation.Confirmed += QuitToLauncher;
        AddChild(quitToLauncherConfirmation);

        StyleConfirmation(openInVersionConfirmation, "Open in another version?", "Open in Launcher");
        openInVersionConfirmation.Confirmed += () =>
        {
            if (pendingOpenVersion is { } version) _ = QuitGameAsync(openLauncher: true, version);
        };
        AddChild(openInVersionConfirmation);
    }

    private void OpenInSavedVersion(CatalogWorld world)
    {
        if (GameVersionName.Parse(world.GameVersion) is not { } version || LauncherPath is null) return;
        pendingOpenVersion = version.ToString();
        openInVersionConfirmation.DialogText =
            $"ClankerWorld {version} last saved {world.Name}. Save and close this game, then choose {version} in the launcher to open it?";
        PopupDialog(openInVersionConfirmation);
    }

    private string OtherVersionStatus(CatalogWorld world) => GameVersionName.Parse(world.GameVersion) is null
        ? "This save names a version the launcher can't use. Your save is safe."
        : LauncherPath is null
            ? $"ClankerWorld {world.GameVersion} saved this world. Start that version to open it. Your save is safe."
            : $"ClankerWorld {world.GameVersion} saved this world. Choose Open in {world.GameVersion} to play it there. Your save is safe.";

    /// <summary>Opens the launcher with this version chosen, as the game closes.</summary>
    private bool TryOpenLauncher(string? version = null)
    {
        failedLauncherVersion = version ?? BuildInformation.Version;
        try
        {
            if (LauncherPath is not { } path) throw new IOException("The launcher is no longer available.");
            var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) };
            start.ArgumentList.Add("--");
            start.ArgumentList.Add("--choose-version=" + failedLauncherVersion);
            using var process = Process.Start(start);
            if (process is null) throw new IOException("The launcher did not start.");
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            GD.PushWarning("launcher outcome=not_started");
            ShowLauncherStartFailure();
            return false;
        }
    }

    private void ShowLauncherStartFailure()
    {
        if (launcherStartFailure.GetParent() is null)
        {
            StyleConfirmation(launcherStartFailure, "Couldn't open the launcher", "Try Again");
            launcherStartFailure.CancelButtonText = "Keep Game Open";
            launcherStartFailure.DialogText = "The launcher couldn't start. The game will stay open so you can try again. Check that the launcher is still installed.";
            launcherStartFailure.Confirmed += () => _ = QuitGameAsync(openLauncher: true, failedLauncherVersion);
            AddChild(launcherStartFailure);
        }
        PopupDialog(launcherStartFailure);
    }
}
