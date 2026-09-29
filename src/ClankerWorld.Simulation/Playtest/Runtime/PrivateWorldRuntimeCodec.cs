using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public static class PrivateWorldRuntimeCodec
{
    private const string LegacyHeader = "clankerworld.private-world-runtime/v1";
    private const string ChunkedHeader = "clankerworld.private-world-runtime/v2";
    private static readonly JsonSerializerOptions LegacyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
    private static readonly JsonSerializerOptions ChunkedOptions = CreateChunkedOptions();

    private static JsonSerializerOptions CreateChunkedOptions()
    {
        var options = new JsonSerializerOptions(LegacyOptions);
        options.Converters.Add(new PrivateWorldTerrainChunkCodec());
        return options;
    }

    public static byte[] Encode(PrivateWorldRuntimeState state)
    {
        PrivateWorldRuntime.ValidateStateForCodec(state);
        var chunked = state.SchemaVersion >= 19;
        return JsonSerializer.SerializeToUtf8Bytes(
            new RuntimeDocument(chunked ? ChunkedHeader : LegacyHeader, state),
            chunked ? ChunkedOptions : LegacyOptions);
    }

    public static PrivateWorldRuntimeState Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var header = JsonDocument.Parse(bytes);
            if (!header.RootElement.TryGetProperty("format", out var format) ||
                format.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The private-world runtime checkpoint format is missing.");
            var version = format.GetString();
            if (version is not (LegacyHeader or ChunkedHeader))
                throw new InvalidDataException("The private-world runtime checkpoint format is unsupported.");
            var document = JsonSerializer.Deserialize<RuntimeDocument>(bytes.Span,
                version == ChunkedHeader ? ChunkedOptions : LegacyOptions)
                ?? throw new InvalidDataException("The private-world runtime checkpoint is empty.");
            if (document.State is null ||
                (version == ChunkedHeader && document.State.SchemaVersion < 19) ||
                (version == LegacyHeader && document.State.SchemaVersion >= 19))
                throw new InvalidDataException("The private-world runtime checkpoint schema and terrain format disagree.");
            PrivateWorldRuntime.ValidateStateForCodec(document.State);
            return document.State;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The private-world runtime checkpoint JSON is damaged.", exception);
        }
    }

    private sealed record RuntimeDocument(string Format, PrivateWorldRuntimeState State);
}
