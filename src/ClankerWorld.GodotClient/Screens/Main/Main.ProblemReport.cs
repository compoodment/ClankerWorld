using System.Globalization;
using ClankerWorld.GodotClient.Diagnostics;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// Report a problem: saves a zip of the game's and its host's latest logs with
/// a short summary, then shows it in the file manager so the player can send
/// it with a bug report (#1260). Keys, saves and model replies never go in.
/// </summary>
public partial class Main
{
    private const string ReportProblemTooltip =
        "Save a zip of the game's logs to send with a bug report. It never includes keys, saves or model replies.";

    private Button NewReportProblemButton()
    {
        var button = new Button { Text = "Report a problem", TooltipText = ReportProblemTooltip };
        StyleButton(button, primary: false);
        button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        button.Pressed += ReportProblem;
        return button;
    }

    private void ReportProblem()
    {
        string path;
        try
        {
            path = WriteProblemReport(ProblemReportDirectory());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"problem_report_failed type={exception.GetType().Name}");
            SetStatus("Couldn't save a problem report. Check there is free disk space, then try again.", good: false);
            return;
        }
        OS.ShellShowInFileManager(path);
        SetStatus($"Saved {Path.GetFileName(path)}. Send it with your bug report.", good: true);
    }

    private string WriteProblemReport(string directory) =>
        ProblemReport.Write(directory, ProblemReportSummary(), ProblemReportLogs(),
            localHost?.SecretsToHide() ?? [], DateTimeOffset.UtcNow);

    private string ProblemReportDirectory() =>
        localHost?.Layout.ReportDirectory ?? ProjectSettings.GlobalizePath("user://reports");

    private List<ProblemReportLog> ProblemReportLogs()
    {
        var logs = new List<ProblemReportLog>();
        var gameLog = ProjectSettings.GlobalizePath(
            ProjectSettings.GetSetting("debug/file_logging/log_path", "user://logs/godot.log").AsString());
        logs.Add(new("game.log", gameLog));
        // Godot renames the last run's log when the game starts again, so a
        // crash is in the newest older log.
        var previousGameLog = PreviousGameLog(gameLog);
        if (previousGameLog is not null) logs.Add(new("game.previous.log", previousGameLog));
        if (localHost is not null)
        {
            logs.Add(new("host.log", localHost.Layout.LogPath));
            logs.Add(new("host.previous.log", localHost.Layout.PreviousLogPath));
        }
        return logs;
    }

    private static string? PreviousGameLog(string gameLog)
    {
        var directory = Path.GetDirectoryName(gameLog);
        if (directory is null || !Directory.Exists(directory)) return null;
        var pattern = Path.GetFileNameWithoutExtension(gameLog) + "*" + Path.GetExtension(gameLog);
        return Directory.GetFiles(directory, pattern)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(gameLog), StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private List<KeyValuePair<string, string>> ProblemReportSummary()
    {
        var window = GetWindow();
        var summary = new List<KeyValuePair<string, string>>
        {
            new("Game version", BuildInformation.Version),
            new("Source commit", BuildInformation.SourceRevision),
            new("Operating system", $"{OS.GetName()} {OS.GetVersion()}"),
            new("Graphics", $"{RenderingServer.GetVideoAdapterVendor()} {RenderingServer.GetVideoAdapterName()} " +
                $"({RenderingServer.GetVideoAdapterApiVersion()})"),
            new("Screen", SizeText(DisplayServer.ScreenGetSize(window.CurrentScreen))),
            new("Window", SizeText(window.Size) +
                (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen ? " (fullscreen)" : string.Empty)),
            new("World server", localHost is null ? "another computer" : "this PC"),
        };
        var snapshot = observationSession.Current?.Baseline.Snapshot;
        if (snapshot is null)
        {
            summary.Add(new("World", "none open"));
            return summary;
        }
        var name = worldListRequest.Catalog?.Worlds.FirstOrDefault(world => world.WorldId == snapshot.WorldId)?.Name;
        summary.Add(new("World", name is null ? snapshot.WorldId : $"{name} ({snapshot.WorldId})"));
        summary.Add(new("World time", $"{clockLabel.Text} (tick {snapshot.WorldTick.ToString(CultureInfo.InvariantCulture)})"));
        return summary;
    }

    private static string SizeText(Vector2I size) =>
        string.Create(CultureInfo.InvariantCulture, $"{size.X} × {size.Y}");
}
