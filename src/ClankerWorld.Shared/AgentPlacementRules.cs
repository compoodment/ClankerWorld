using System;
using System.Collections.Generic;
using System.Linq;

namespace ClankerWorld.AgentPlacement;

[Flags]
public enum AgentPlacementAmbiguity : byte
{
    None = 0,
    HouseholdProperty = 1,
    TownBorders = 2,
}

/// <summary>
/// Membership implied by the household-property and Town records at one tile.
/// The same resolver is compiled into the host and Godot client so preview and
/// confirmation use the same priority and ambiguity rules.
/// </summary>
public readonly record struct AgentPlacementResolution(
    string? HouseholdPropertyOwnerId,
    string? TownId,
    AgentPlacementAmbiguity Ambiguity)
{
    public bool IsAmbiguous => Ambiguity != AgentPlacementAmbiguity.None;

    public string? HouseholdIdFor(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (IsAmbiguous) throw new InvalidOperationException(AmbiguityExplanation());
        return HouseholdPropertyOwnerId ?? (TownId is null ? "household:" + agentId : null);
    }

    public bool Matches(string agentId, string? householdId, string? townId) =>
        !IsAmbiguous &&
        string.Equals(HouseholdIdFor(agentId), householdId, StringComparison.Ordinal) &&
        string.Equals(TownId, townId, StringComparison.Ordinal);

    public string AmbiguityExplanation()
    {
        var household = (Ambiguity & AgentPlacementAmbiguity.HouseholdProperty) != 0;
        var town = (Ambiguity & AgentPlacementAmbiguity.TownBorders) != 0;
        return (household, town) switch
        {
            (true, true) => "Household property and Town borders overlap at this tile, so the agent's membership is unclear.",
            (true, false) => "Household property overlaps at this tile, so the agent's household is unclear.",
            (false, true) => "Town borders overlap at this tile, so the agent's Town is unclear.",
            _ => string.Empty,
        };
    }
}

public static class AgentPlacementRules
{
    public static AgentPlacementResolution Resolve(
        IEnumerable<string?> householdPropertyOwnerIds,
        IEnumerable<string?> townIds)
    {
        ArgumentNullException.ThrowIfNull(householdPropertyOwnerIds);
        ArgumentNullException.ThrowIfNull(townIds);

        var householdOwners = DistinctOwners(householdPropertyOwnerIds);
        var matchingTowns = DistinctOwners(townIds);
        var ambiguity = AgentPlacementAmbiguity.None;
        if (householdOwners.Length > 1) ambiguity |= AgentPlacementAmbiguity.HouseholdProperty;
        if (matchingTowns.Length > 1) ambiguity |= AgentPlacementAmbiguity.TownBorders;

        return new AgentPlacementResolution(
            householdOwners.Length == 1 ? householdOwners[0] : null,
            matchingTowns.Length == 1 ? matchingTowns[0] : null,
            ambiguity);
    }

    private static string[] DistinctOwners(IEnumerable<string?> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .Distinct(StringComparer.Ordinal)
        .Take(2)
        .ToArray();
}
