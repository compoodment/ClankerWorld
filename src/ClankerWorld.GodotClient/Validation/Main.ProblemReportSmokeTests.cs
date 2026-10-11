using System.IO.Compression;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Settings offers Report a problem, and the report it writes holds the
    /// summary and either the game's log or a note that it was not found.
    /// </summary>
    private void VerifyProblemReport()
    {
        if (!gameSettingsContent.FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>()
                .Any(button => button.Text == "Report a problem"))
            throw new InvalidOperationException("Game settings must offer Report a problem.");
        var directory = ProjectSettings.GlobalizePath("user://smoke-problem-reports");
        try
        {
            var path = WriteProblemReport(directory);
            using var archive = ZipFile.OpenRead(path);
            using var reader = new StreamReader(archive.GetEntry("summary.txt")!.Open());
            var summary = reader.ReadToEnd();
            var hasGameLog = archive.GetEntry("logs/game.log") is not null;
            if (!summary.Contains("Game version: " + BuildInformation.Version, StringComparison.Ordinal) ||
                !summary.Contains("World server: ", StringComparison.Ordinal) ||
                (!hasGameLog && !summary.Contains("- game.log: ", StringComparison.Ordinal)))
                throw new InvalidOperationException("A problem report must hold its summary and the game's log or a note about it.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
