using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ClankerWorld;

/// <summary>A log file to copy into a problem report, under <c>logs/EntryName</c>.</summary>
public sealed record ProblemReportLog(string EntryName, string SourcePath);

/// <summary>
/// Builds the zip a player sends with a bug report: a short summary and the
/// latest logs, with anything that looks like a key or secret blanked out.
/// It reads only the files it is given, so saves, keys and settings never
/// reach it. The game and the launcher share this code.
/// </summary>
public static partial class ProblemReport
{
    /// <summary>Only the end of a longer log is kept; that is where a problem shows.</summary>
    public const int MaxBytesPerLog = 4 * 1024 * 1024;

    /// <summary>Older reports beyond this many are deleted when a new one is written.</summary>
    public const int KeptReports = 5;

    public const string FilePrefix = "ClankerWorld-report-";
    public const string Removed = "[removed]";
    private const string EarlierLinesLeftOut = "[earlier lines left out]";

    /// <summary>
    /// Writes a new report into <paramref name="reportDirectory"/> and returns
    /// its path. A log that is missing or unreadable is listed in the summary
    /// instead. Every exact value in <paramref name="knownSecrets"/> is blanked
    /// as well as anything that looks like a key.
    /// </summary>
    public static string Write(
        string reportDirectory,
        IReadOnlyList<KeyValuePair<string, string>> summary,
        IReadOnlyList<ProblemReportLog> logs,
        IReadOnlyCollection<string> knownSecrets,
        DateTimeOffset now)
    {
        Directory.CreateDirectory(reportDirectory);
        var name = FilePrefix + now.UtcDateTime.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(reportDirectory, name + ".zip");
        for (var copy = 2; File.Exists(path); copy++)
            path = Path.Combine(reportDirectory, $"{name}-{copy}.zip");

        var secrets = knownSecrets.Where(secret => secret.Length >= 8).ToArray();
        var notes = new List<string>();
        var partial = path + ".partial";
        try
        {
            using (var archive = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                foreach (var log in logs)
                {
                    var lines = TryReadTail(log.SourcePath, out var note);
                    if (lines is null)
                    {
                        notes.Add($"{log.EntryName}: {note}");
                        continue;
                    }
                    WriteEntry(archive, "logs/" + log.EntryName,
                        string.Join('\n', lines.Select(line => Redact(line, secrets))) + "\n");
                }
                WriteEntry(archive, "summary.txt", SummaryText(summary, now, notes, secrets));
            }
            File.Move(partial, path);
        }
        finally
        {
            File.Delete(partial);
        }
        DeleteOlderReports(reportDirectory, path);
        return path;
    }

    /// <summary>
    /// Blanks anything in one log line that looks like a credential: values
    /// after names such as <c>api_key</c> or <c>Authorization</c>, bearer
    /// tokens, passwords in addresses, common provider key formats and long
    /// random-looking strings. Plain hex IDs and hashes are kept, because they
    /// are what a bug report needs to follow a request.
    /// </summary>
    public static string Redact(string line, IReadOnlyCollection<string>? knownSecrets = null)
    {
        foreach (var secret in knownSecrets ?? [])
            line = line.Replace(secret, Removed, StringComparison.Ordinal);
        line = AuthorizationScheme().Replace(line, match => match.Groups["scheme"].Value + " " + Removed);
        line = NamedValue().Replace(line, match => match.Groups["prefix"].Value + Removed);
        line = AddressPassword().Replace(line, match => match.Groups["scheme"].Value + Removed + "@");
        line = ProviderKey().Replace(line, Removed);
        return LongToken().Replace(line, match => LooksRandom(match.Value) ? Removed : match.Value);
    }

    private static bool LooksRandom(string token) =>
        token.Any(char.IsAsciiLetterUpper) && token.Any(char.IsAsciiLetterLower) && token.Any(char.IsAsciiDigit);

    private static string SummaryText(IReadOnlyList<KeyValuePair<string, string>> summary, DateTimeOffset now,
        List<string> notes, IReadOnlyCollection<string> secrets)
    {
        var text = new StringBuilder();
        text.Append("ClankerWorld problem report\n");
        text.Append("Created: ").Append(now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var (label, value) in summary)
            text.Append(label).Append(": ").Append(Redact(OneLine(value), secrets)).Append('\n');
        if (notes.Count > 0)
        {
            text.Append("\nLogs not included:\n");
            foreach (var note in notes) text.Append("- ").Append(note).Append('\n');
        }
        text.Append("\nThis report never includes keys, pairing codes, device credentials, saves, prompts or model replies.\n");
        return text.ToString();
    }

    private static string OneLine(string value) =>
        string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static List<string>? TryReadTail(string path, out string note)
    {
        note = string.Empty;
        try
        {
            // The game and its host keep writing their logs while a report is made.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var skipped = stream.Length > MaxBytesPerLog;
            if (skipped) stream.Seek(-MaxBytesPerLog, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new List<string>();
            // A tail starts mid-line, so its first partial line is dropped.
            if (skipped)
            {
                reader.ReadLine();
                lines.Add(EarlierLinesLeftOut);
            }
            while (reader.ReadLine() is { } line) lines.Add(line);
            return lines;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            note = "not found";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            note = "could not be read (" + exception.GetType().Name + ")";
        }
        return null;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void DeleteOlderReports(string reportDirectory, string newest)
    {
        var older = Directory.GetFiles(reportDirectory, FilePrefix + "*.zip")
            .Where(path => !string.Equals(path, newest, StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(KeptReports - 1);
        foreach (var path in older)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A report the player has open stays; the next report tries again.
            }
        }
    }

    [GeneratedRegex(
        """(?<prefix>(?:api[-_ ]?key|apikey|x-api-key|authorization|secret|token|password|passwd|signature|pairing[-_ ]?code|companion[-_ ]?secret|private[-_ ]?key)"?\s*[:=]\s*"?)(?!\[removed\])[^\s",;&'}]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedValue();

    [GeneratedRegex("""(?<scheme>\b(?:Bearer|Basic))\s+(?!\[removed\])[A-Za-z0-9._~+/=-]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationScheme();

    [GeneratedRegex("""(?<scheme>\b[A-Za-z][A-Za-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@""", RegexOptions.CultureInvariant)]
    private static partial Regex AddressPassword();

    [GeneratedRegex("""\b(?:sk-[A-Za-z0-9_-]{16,}|AIza[0-9A-Za-z_-]{30,}|gh[pousr]_[A-Za-z0-9]{20,}|gsk_[A-Za-z0-9]{20,}|xox[abprs]-[A-Za-z0-9-]{10,})""",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProviderKey();

    [GeneratedRegex("""[A-Za-z0-9+/_=-]{32,}""", RegexOptions.CultureInvariant)]
    private static partial Regex LongToken();
}
