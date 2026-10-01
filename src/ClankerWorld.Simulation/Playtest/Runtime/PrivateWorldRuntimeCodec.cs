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
    private const string ChunkedHeader = "clankerworld.private-world-runtime/v2";
    private static readonly JsonSerializerOptions CurrentOptions = CreateCurrentOptions();
    private static readonly JsonSerializerOptions CurrentReadOptions = CreateReadOptions(CurrentOptions);

    private static JsonSerializerOptions CreateReadOptions(JsonSerializerOptions source) => new(source)
    {
        // Required checkpoint members must not become null/default objects that
        // escape per-world compatibility checks as unexpected runtime faults.
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private static JsonSerializerOptions CreateCurrentOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
        options.Converters.Add(new PrivateWorldTerrainChunkCodec());
        return options;
    }

    public static byte[] Encode(PrivateWorldRuntimeState state)
    {
        PrivateWorldRuntime.ValidateStateForCodec(state);
        return JsonSerializer.SerializeToUtf8Bytes(
            new RuntimeDocument(ChunkedHeader, state), CurrentOptions);
    }

    public static PrivateWorldRuntimeState Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var header = JsonDocument.Parse(bytes);
            if (header.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The private-world runtime checkpoint envelope is invalid.");
            if (header.RootElement.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object &&
                state.TryGetProperty("schemaVersion", out var schemaVersion) &&
                schemaVersion.ValueKind == JsonValueKind.Number &&
                schemaVersion.TryGetInt32(out var schema))
            {
                PrivateWorldRuntime.ValidateMinimumSupportedSchemaVersion(schema);
            }
            if (!header.RootElement.TryGetProperty("format", out var format) ||
                format.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The private-world runtime checkpoint format is missing.");
            var version = format.GetString();
            if (version != ChunkedHeader)
                throw new InvalidDataException("The private-world runtime checkpoint format is unsupported.");
            var document = JsonSerializer.Deserialize<RuntimeDocument>(bytes.Span, CurrentReadOptions)
                ?? throw new InvalidDataException("The private-world runtime checkpoint is empty.");
            if (document.State is null)
                throw new InvalidDataException("The private-world runtime checkpoint is empty.");
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
