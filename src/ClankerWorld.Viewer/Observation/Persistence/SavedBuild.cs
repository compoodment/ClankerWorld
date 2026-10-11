using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// The game build that last wrote a world's checkpoint, kept in a small file
/// beside it (<c>&lt;checkpoint&gt;.build.json</c>). It can be read without
/// loading the world, so a newer game that cannot open a save can say which
/// version can. It is diagnostic only and never part of world state or replay.
/// </summary>
public sealed record SavedBuild(string GameVersion, string SourceRevision)
{
    public static string PathFor(string checkpointPath) => checkpointPath + ".build.json";

    /// <summary>Records this build. A failure is ignored: the checkpoint itself is already saved.</summary>
    public static bool TryWrite(string checkpointPath)
        => TryRestore(checkpointPath, JsonSerializer.SerializeToUtf8Bytes(
            new SavedBuild(BuildInformation.Version, BuildInformation.SourceRevision)));

    internal static byte[]? TryReadBytes(string checkpointPath)
    {
        try { return File.ReadAllBytes(PathFor(checkpointPath)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static bool TryRestore(string checkpointPath, byte[]? bytes)
    {
        var destination = PathFor(checkpointPath);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (bytes is null)
            {
                File.Delete(destination);
                return true;
            }
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    public static SavedBuild? TryRead(string checkpointPath)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<SavedBuild>(File.ReadAllBytes(PathFor(checkpointPath)));
            return saved is { GameVersion.Length: > 0 and <= 64, SourceRevision.Length: <= 64 } ? saved : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>Unusable optional build diagnostics read as unknown without refusing a world or save.</summary>
public sealed class SavedBuildTextConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }
        var value = reader.GetString();
        return value is { Length: > 0 and <= 64 } && !value.Any(char.IsControl) ? value : null;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
