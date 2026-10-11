using System.Diagnostics;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyLaunchOptionsAsync()
    {
        if (!NoMenuOverWorld() || developerPanel.Visible || localHost is not null)
            throw new InvalidOperationException("Launch option checks need the unbundled world with no menu open.");
        var previousMode = developerMode;
        var previousLauncher = launcherPath;
        var previousPathRead = launcherPathRead;
        var directory = Directory.CreateTempSubdirectory("launcher handoff ");
        try
        {
            developerMode = false;
            renderedControlsTheme = null;
            FillControlsGroups();
            _Input(new InputEventKey { Keycode = Key.F12, Pressed = true });
            if (developerPanel.Visible)
                throw new InvalidOperationException("F12 must do nothing outside Developer mode.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.F1, Pressed = true });
            var keys = controlsColumns.FindChildren("*", nameof(Label), recursive: true, owned: false)
                .OfType<Label>().Select(label => label.Text).ToArray();
            if (!controlsPanel.Visible || keys.Contains("F12"))
                throw new InvalidOperationException("The real F1 list must omit F12 outside Developer mode.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });

            isQuittingGame = true;
            quitToLauncherConfirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            if (!isQuittingGame || launcherStartFailure.Visible)
                throw new InvalidOperationException("A pending Quit to Launcher confirmation cannot replace an active quit.");
            isQuittingGame = false;
            launcherPathRead = true;
            launcherPath = Path.Combine(directory.FullName, "removed launcher");
            await QuitGameAsync(openLauncher: true);
            if (!launcherStartFailure.Visible || isQuittingGame)
                throw new InvalidOperationException("A missing launcher must report failure and keep the game open for retry.");
            launcherStartFailure.Hide();

            if (OperatingSystem.IsLinux())
            {
                launcherPath = Path.Combine(directory.FullName, "launcher with spaces.sh");
                var arguments = Path.Combine(directory.FullName, "arguments.txt");
                File.WriteAllText(launcherPath, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$(dirname \"$0\")/arguments.txt\"\n");
                File.SetUnixFileMode(launcherPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                if (!TryOpenLauncher()) throw new InvalidOperationException("The launcher hand-off process must start.");
                var deadline = Stopwatch.StartNew();
                while ((!File.Exists(arguments) || File.ReadAllLines(arguments).Length < 2) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!File.Exists(arguments) || !File.ReadAllLines(arguments).SequenceEqual(new[] { "--", "--choose-version=" + BuildInformation.Version }))
                    throw new InvalidOperationException("Launcher hand-off must retain argument boundaries and the actual game version.");
            }
            GD.Print("Game launch option checks passed: Developer mode off, F1 list, busy quit, missing launcher and version hand-off.");
        }
        finally
        {
            isQuittingGame = false;
            launcherPath = previousLauncher;
            launcherPathRead = previousPathRead;
            launcherStartFailure.Hide();
            controlsPanel.Hide();
            developerMode = previousMode;
            renderedControlsTheme = null;
            FillControlsGroups();
            directory.Delete(recursive: true);
        }
    }
}
