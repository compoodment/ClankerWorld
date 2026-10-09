using System.Text.Json;

namespace ClankerWorld.Simulation.Cognition;

/// <summary>
/// A personal hearing submission. References name offered case records; this
/// payload supplies neither authority, verified evidence nor a permission.
/// </summary>
public sealed record CognitionLandHearingChoice(
    string? Statement = null,
    string? HouseholdId = null,
    long? AgreedEndTick = null,
    IReadOnlyList<string>? EvidenceIds = null,
    IReadOnlyList<string>? LawIds = null,
    string? Grounds = null,
    string? RequestedOutcome = null,
    string? PaymentItemKind = null,
    int? PaymentQuantity = null)
{
    public const int MaximumReferences = 16;

    public void Validate()
    {
        if (Statement is not null && CognitionDecisionResponse.NormalizeIdentityText(Statement) != Statement ||
            Grounds is not null && CognitionDecisionResponse.NormalizeIdentityText(Grounds) != Grounds ||
            HouseholdId is not null && !ValidReference(HouseholdId) || AgreedEndTick is < 0 ||
            RequestedOutcome is not null and not ("confirm" or "renew" or "amend" or "end" or "reject") ||
            !ValidReferences(EvidenceIds) || !ValidReferences(LawIds) ||
            (PaymentItemKind is null) != (PaymentQuantity is null) ||
            PaymentItemKind is not null && !ValidReference(PaymentItemKind) || PaymentQuantity is <= 0)
            throw new ArgumentOutOfRangeException(nameof(CognitionLandHearingChoice), "The hearing submission is malformed or too large.");
    }

    private static bool ValidReference(string? value) => value is { Length: > 0 and <= 256 } &&
        !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl);

    private static bool ValidReferences(IReadOnlyList<string>? values) => values is null ||
        values.Count <= MaximumReferences && values.All(ValidReference) &&
        values.Distinct(StringComparer.Ordinal).Count() == values.Count;

    internal static CognitionLandHearingChoice? Parse(JsonElement answer)
    {
        if (!answer.TryGetProperty("civic_land_hearing", out var payload) || payload.ValueKind == JsonValueKind.Null)
            return null;
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The provider returned an invalid hearing submission.");

        long? endTick = null;
        if (payload.TryGetProperty("agreed_end_tick", out var end) && end.ValueKind != JsonValueKind.Null)
        {
            if (end.ValueKind != JsonValueKind.Number || !end.TryGetInt64(out var value))
                throw new InvalidDataException("The provider returned an invalid permission end date.");
            endTick = value;
        }
        int? paymentQuantity = null;
        if (payload.TryGetProperty("payment_quantity", out var quantity) && quantity.ValueKind != JsonValueKind.Null)
        {
            if (quantity.ValueKind != JsonValueKind.Number || !quantity.TryGetInt32(out var value))
                throw new InvalidDataException("The provider returned an invalid goods price quantity.");
            paymentQuantity = value;
        }
        var choice = new CognitionLandHearingChoice(
            Text(payload, "statement"), Reference(payload, "household_id"), endTick,
            References(payload, "evidence_ids"), References(payload, "law_ids"),
            Text(payload, "grounds"), Reference(payload, "requested_outcome"),
            Reference(payload, "payment_item_kind"), paymentQuantity);
        try { choice.Validate(); }
        catch (ArgumentException error)
        { throw new InvalidDataException("The provider returned a malformed hearing submission.", error); }
        return choice;
    }

    private static string? Reference(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || !ValidReference(value.GetString()))
            throw new InvalidDataException("The provider returned an invalid hearing reference.");
        return value.GetString();
    }

    private static string? Text(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String ||
            CognitionDecisionResponse.NormalizeIdentityText(value.GetString()) is not { } text)
            throw new InvalidDataException("The provider returned invalid hearing text.");
        return text;
    }

    private static string[]? References(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > MaximumReferences ||
            value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || !ValidReference(item.GetString())))
            throw new InvalidDataException("The provider returned invalid hearing references.");
        return value.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }
}
