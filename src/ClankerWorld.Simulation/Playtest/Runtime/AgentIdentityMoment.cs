using ClankerWorld.Simulation.Cognition;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Playtest;

public enum AgentIdentityMomentKind
{
    Midlife,
    Parenthood,
    PartnerLoss,
    ParentLoss,
    Elder,
}

/// <summary>One saved opportunity per named moment; requested opportunities are never retried.</summary>
public sealed record AgentIdentityMoment(
    AgentIdentityMomentKind Kind,
    long TriggeredTick,
    string Outcome = "waiting",
    long? RequestedTick = null,
    long? CompletedTick = null,
    string? Personality = null,
    string? Aspiration = null)
{
    [JsonIgnore]
    public string Reason => ReasonFor(Kind);

    public static string ReasonFor(AgentIdentityMomentKind kind) => kind switch
    {
        AgentIdentityMomentKind.Midlife => "Reaching the middle of life",
        AgentIdentityMomentKind.Parenthood => "Becoming a parent",
        AgentIdentityMomentKind.PartnerLoss => "Losing a partner",
        AgentIdentityMomentKind.ParentLoss => "Losing a parent",
        AgentIdentityMomentKind.Elder => "Becoming an elder",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static void Validate(IReadOnlyList<AgentIdentityMoment>? moments, long worldTick, int schemaVersion)
    {
        if (moments is null) return;
        if (schemaVersion < PrivateWorldRuntime.LifeMomentIdentitySchemaVersion)
            throw new InvalidDataException(
                $"Saved life moments require private-world schema {PrivateWorldRuntime.LifeMomentIdentitySchemaVersion}.");
        if (moments.Count > 5 || moments.Any(item => item is null) ||
            moments.Select(item => item.Kind).Distinct().Count() != moments.Count)
            throw new InvalidDataException("The saved life moments exceed their per-life limit.");
        foreach (var item in moments)
        {
            if (!Enum.IsDefined(item.Kind) || item.TriggeredTick < 0 || item.TriggeredTick > worldTick ||
                item.Outcome is not ("waiting" or "requested" or "accepted" or "kept" or "interrupted") ||
                item.RequestedTick is { } requested && (requested < item.TriggeredTick || requested > worldTick) ||
                item.CompletedTick is { } completed && (completed < (item.RequestedTick ?? item.TriggeredTick) || completed > worldTick) ||
                (item.Outcome == "waiting" && (item.RequestedTick is not null || item.CompletedTick is not null)) ||
                (item.Outcome == "requested" && (item.RequestedTick is null || item.CompletedTick is not null)) ||
                (item.Outcome is "accepted" or "kept" or "interrupted" && item.CompletedTick is null) ||
                (item.Outcome == "accepted" && (item.RequestedTick is null || item.Personality is null && item.Aspiration is null)) ||
                (item.Outcome != "accepted" && (item.Personality is not null || item.Aspiration is not null)) ||
                (item.Personality is not null && CognitionDecisionResponse.NormalizeIdentityText(item.Personality) != item.Personality) ||
                (item.Aspiration is not null && CognitionDecisionResponse.NormalizeIdentityText(item.Aspiration) != item.Aspiration))
                throw new InvalidDataException("The saved life moment is invalid.");
        }
    }
}
