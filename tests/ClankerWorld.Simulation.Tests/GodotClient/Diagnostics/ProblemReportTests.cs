using System.IO.Compression;
using ClankerWorld.GodotClient.Diagnostics;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProblemReportTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 30, 0, TimeSpan.Zero);
    private readonly string root = Directory.CreateTempSubdirectory("problem-report-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void ReportHoldsSummaryAndLogsWithSecretsBlanked()
    {
        const string companionSecret = "q2Vb7nX0pL9sT4wY1zA8cD3eF6gH5jK2mN0rU7vW9xQ";
        const string requestId = "3f2a9c1be04d4e7fa6b81c2d3e4f5a6b";
        var hostLog = Write("host.log",
            $"info: decision_accepted tick=5040 request={requestId} outcome=accepted latency_ms=812",
            "warn: provider_failed model=openai/gpt-5 key=sk-proj-AbCdEf0123456789XyZ outcome=fallback",
            $"debug: companion header {companionSecret}",
            "debug: Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig",
            "debug: {\"apiKey\":\"mistral-key-without-prefix\",\"pairing_code\":\"482913\"}",
            "debug: provider url https://player:hunter22@example.test/v1",
            "debug: token Zx81Qw7Er6Ty5Ui4Op3As2Df1Gh0JkLmNb");
        var gameLog = Write("godot.log", "Godot Engine v4.7.2", "owner_action_failed type=HttpRequestException");

        var path = ProblemReport.Write(Path.Combine(root, "reports"),
            [new("Game version", "0.1.0-dev"), new("World", "Riverfold\n(day 12)")],
            [new("host.log", hostLog), new("godot.log", gameLog), new("host.previous.log", Path.Combine(root, "missing.log"))],
            [companionSecret], Now);

        Assert.Equal("ClankerWorld-report-2026-10-10-093000.zip", Path.GetFileName(path));
        using var archive = ZipFile.OpenRead(path);
        Assert.Equal(["logs/host.log", "logs/godot.log", "summary.txt"], archive.Entries.Select(entry => entry.FullName));
        var host = Read(archive, "logs/host.log");
        foreach (var secret in new[]
                 {
                     companionSecret, "sk-proj-AbCdEf0123456789XyZ", "eyJhbGciOiJIUzI1NiJ9", "mistral-key-without-prefix",
                     "482913", "hunter22", "Zx81Qw7Er6Ty5Ui4Op3As2Df1Gh0JkLmNb",
                 })
            Assert.DoesNotContain(secret, host);
        // What a bug report needs to follow a request survives.
        Assert.Contains($"tick=5040 request={requestId} outcome=accepted latency_ms=812", host);
        Assert.Contains("model=openai/gpt-5", host);
        Assert.Contains("https://[removed]@example.test/v1", host);
        Assert.Contains("owner_action_failed type=HttpRequestException", Read(archive, "logs/godot.log"));

        var summary = Read(archive, "summary.txt");
        Assert.Contains("Created: 2026-10-10 09:30:00 UTC", summary);
        Assert.Contains("World: Riverfold (day 12)", summary);
        Assert.Contains("- host.previous.log: not found", summary);
    }

    [Fact]
    public void LongLogKeepsOnlyWholeLinesFromItsEnd()
    {
        var line = new string('a', 99);
        var lines = Enumerable.Range(0, ProblemReport.MaxBytesPerLog / 100 + 500).Select(_ => line).ToList();
        lines.Add("last line before the crash");
        var log = Write("host.log", [.. lines]);

        var path = ProblemReport.Write(root, [], [new("host.log", log)], [], Now);

        using var archive = ZipFile.OpenRead(path);
        var kept = Read(archive, "logs/host.log").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("[earlier lines left out]", kept[0]);
        Assert.All(kept[1..^1], item => Assert.Equal(line, item));
        Assert.Equal("last line before the crash", kept[^1]);
        Assert.True(kept.Length < lines.Count);
    }

    [Fact]
    public void OnlyTheNewestReportsAreKept()
    {
        var reports = Path.Combine(root, "reports");
        var written = Enumerable.Range(0, ProblemReport.KeptReports + 2)
            .Select(minute =>
            {
                var path = ProblemReport.Write(reports, [], [], [], Now.AddMinutes(minute));
                File.SetLastWriteTimeUtc(path, Now.AddMinutes(minute).UtcDateTime);
                return path;
            })
            .ToList();
        File.WriteAllText(Path.Combine(reports, "notes.txt"), "the player's own file");

        Assert.Equal(written.TakeLast(ProblemReport.KeptReports).Order(),
            Directory.GetFiles(reports, "*.zip").Order());
        Assert.True(File.Exists(Path.Combine(reports, "notes.txt")));
    }

    private string Write(string name, params string[] lines)
    {
        var path = Path.Combine(root, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
