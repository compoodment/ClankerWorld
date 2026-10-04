using System.Security.Cryptography;
using System.Text.Json;

namespace ClankerWorld.Simulation.Society;

public static class SocietyWorldRuntimeCodec
{
    private const string Header = "clankerworld.society-runtime/v2";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static byte[] Encode(SocietyWorldRuntimeState state)
    {
        Validate(state);
        return JsonSerializer.SerializeToUtf8Bytes(new RuntimeDocument(Header, state), Options);
    }

    public static SocietyWorldRuntimeState Decode(ReadOnlyMemory<byte> bytes)
    {
        var document = JsonSerializer.Deserialize<RuntimeDocument>(bytes.Span, Options)
            ?? throw new InvalidDataException("The Phase 4 runtime checkpoint is empty.");
        if (document.Format != Header || document.State is null)
        {
            throw new InvalidDataException("The Phase 4 runtime checkpoint format is not supported.");
        }

        Validate(document.State);
        return document.State;
    }

    public static string Digest(SocietyWorldRuntimeState state) =>
        Convert.ToHexStringLower(SHA256.HashData(Encode(state)));

    private static void Validate(SocietyWorldRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != SocietyWorldRuntime.StateSchemaVersion)
        {
            throw new InvalidDataException("The Phase 4 runtime state schema is unsupported.");
        }

        SocietyFixture.Validate(state.Society);
        var scheduler = SocietyCognitionScheduler.Restore(state.Cognition);
        scheduler.Validate();
        var activeIds = state.Society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id)
            .OrderBy(item => item, StringComparer.Ordinal);
        if (!activeIds.SequenceEqual(scheduler.InhabitantIds.OrderBy(item => item, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("The Phase 4 runtime populations disagree.");
        }
    }

    private sealed record RuntimeDocument(string Format, SocietyWorldRuntimeState State);
}
