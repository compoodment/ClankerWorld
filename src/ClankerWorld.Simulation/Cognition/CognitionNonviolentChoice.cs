using System.Text.Json;

namespace ClankerWorld.Simulation.Cognition;

/// <summary>A personal legal submission, never proof, consent by another person or a physical effect.</summary>
public sealed record CognitionRemedyTerm(string Kind, string ContributorId, string? BeneficiaryId,
    string? ItemKind, int Quantity, string? TargetId);

public sealed record CognitionNonviolentChoice(string? Statement = null, string? Uncertainty = null,
    IReadOnlyList<string>? EvidenceIds = null, string? Grounds = null,
    IReadOnlyList<CognitionRemedyTerm>? Terms = null, long? CompletionTicks = null)
{
    public void Validate()
    {
        if (!ValidText(Statement) || !ValidText(Uncertainty) || !ValidText(Grounds) || CompletionTicks is <= 0 ||
            EvidenceIds is { } refs && (refs.Count > 16 || refs.Any(reference => !Reference(reference)) ||
                refs.Distinct(StringComparer.Ordinal).Count() != refs.Count) ||
            Terms is { } terms && (terms.Count is < 1 or > 8 || terms.Any(term => term is null ||
                term.Kind is not ("return_goods" or "repair_equipment" or "public_service_goods") ||
                !Reference(term.ContributorId) || !OptionalReference(term.BeneficiaryId) ||
                !OptionalReference(term.ItemKind) || !OptionalReference(term.TargetId) || term.Quantity is < 1 or > 10_000)))
            throw new ArgumentOutOfRangeException(nameof(CognitionNonviolentChoice), "The legal submission is malformed or too large.");
    }

    private static bool ValidText(string? value) => value is null || CognitionDecisionResponse.NormalizeIdentityText(value) == value;
    private static bool Reference(string? value) => value is { Length: > 0 and <= 256 } && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl);
    private static bool OptionalReference(string? value) => value is null || Reference(value);

    internal static CognitionNonviolentChoice? Parse(JsonElement answer)
    {
        if (!answer.TryGetProperty("civic_nonviolent", out var payload) || payload.ValueKind == JsonValueKind.Null) return null;
        if (payload.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid legal submission.");
        var allowed = new[] { "statement", "uncertainty", "evidence_ids", "grounds", "terms", "completion_ticks" };
        if (payload.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal)))
            throw new InvalidDataException("Unknown legal submission field.");
        string? Text(string name)
        {
            if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.String || CognitionDecisionResponse.NormalizeIdentityText(value.GetString()) is not { } text)
                throw new InvalidDataException("Invalid legal submission text.");
            return text;
        }
        static string? ReadReference(JsonElement value, string name, bool required = false)
        {
            if (!value.TryGetProperty(name, out var item) || item.ValueKind == JsonValueKind.Null)
            {
                if (required) throw new InvalidDataException("A remedy reference is required.");
                return null;
            }
            if (item.ValueKind != JsonValueKind.String || !Reference(item.GetString())) throw new InvalidDataException("Invalid remedy reference.");
            return item.GetString();
        }
        string[]? evidence = null;
        if (payload.TryGetProperty("evidence_ids", out var references) && references.ValueKind != JsonValueKind.Null)
        {
            if (references.ValueKind != JsonValueKind.Array || references.GetArrayLength() > 16 ||
                references.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || !Reference(item.GetString())))
                throw new InvalidDataException("Invalid legal evidence references.");
            evidence = references.EnumerateArray().Select(item => item.GetString()!).ToArray();
        }
        long? duration = null;
        if (payload.TryGetProperty("completion_ticks", out var ticks) && ticks.ValueKind != JsonValueKind.Null)
        {
            if (ticks.ValueKind != JsonValueKind.Number || !ticks.TryGetInt64(out var count)) throw new InvalidDataException("Invalid remedy duration.");
            duration = count;
        }
        List<CognitionRemedyTerm>? terms = null;
        if (payload.TryGetProperty("terms", out var rawTerms) && rawTerms.ValueKind != JsonValueKind.Null)
        {
            if (rawTerms.ValueKind != JsonValueKind.Array || rawTerms.GetArrayLength() is < 1 or > 8)
                throw new InvalidDataException("Invalid remedy terms.");
            terms = [];
            foreach (var term in rawTerms.EnumerateArray())
            {
                if (term.ValueKind != JsonValueKind.Object || !term.TryGetProperty("quantity", out var quantity) || quantity.ValueKind != JsonValueKind.Number || !quantity.TryGetInt32(out var count))
                    throw new InvalidDataException("Invalid remedy quantity.");
                var fields = new[] { "kind", "contributor_id", "beneficiary_id", "item_kind", "quantity", "target_id" };
                if (term.EnumerateObject().Any(property => !fields.Contains(property.Name, StringComparer.Ordinal)))
                    throw new InvalidDataException("Unknown remedy term field.");
                terms.Add(new(ReadReference(term, "kind", true)!, ReadReference(term, "contributor_id", true)!,
                    ReadReference(term, "beneficiary_id"), ReadReference(term, "item_kind"), count, ReadReference(term, "target_id")));
            }
        }
        var choice = new CognitionNonviolentChoice(Text("statement"), Text("uncertainty"), evidence, Text("grounds"), terms, duration);
        try { choice.Validate(); }
        catch (ArgumentException error) { throw new InvalidDataException("Malformed legal submission.", error); }
        return choice;
    }
}
