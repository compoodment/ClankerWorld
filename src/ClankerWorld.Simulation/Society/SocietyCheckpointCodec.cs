using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClankerWorld.Simulation.Society;

public static class SocietyCheckpointCodec
{
    private const string Header = "clankerworld.society/v2";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static byte[] Encode(SocietyCheckpoint checkpoint)
    {
        SocietyFixture.Validate(checkpoint);
        return JsonSerializer.SerializeToUtf8Bytes(
            new SocietyDocument(Header, checkpoint),
            Options);
    }

    public static SocietyCheckpoint Decode(ReadOnlyMemory<byte> bytes)
    {
        var document = JsonSerializer.Deserialize<SocietyDocument>(bytes.Span, Options)
            ?? throw new InvalidDataException("The society checkpoint is empty.");
        if (document.Format != Header || document.Checkpoint is null)
        {
            throw new InvalidDataException("The society checkpoint format is not supported.");
        }

        SocietyFixture.Validate(document.Checkpoint);
        return document.Checkpoint;
    }

    public static string StateDigest(SocietyCheckpoint checkpoint)
    {
        var stateOnly = checkpoint with { Events = [] };
        return Convert.ToHexStringLower(SHA256.HashData(Encode(stateOnly)));
    }

    public static string EventDigest(IEnumerable<SocietyEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var canonical = string.Join(
            '\n',
            events.Select(item => $"{item.EventId}|{item.WorldTick}|{item.Kind}|{item.Detail}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed record SocietyDocument(string Format, SocietyCheckpoint Checkpoint);
}
