using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Built-in buildings outside the current settlement construction roster.
/// Their definitions remain available to current content and projections, but
/// planning never offers them for new projects.
/// </summary>
public static class RetiredBuildings
{
    private static readonly string StarterDigest = StarterContent.Create().PackageDigest;
    private static readonly string SettlementDigest = SettlementContent.Create().PackageDigest;

    public static bool Contains(BuildingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.PackageDigest == StarterDigest)
            return definition.LocalId is "shelter" or "storage" or "fire";
        return definition.PackageDigest == SettlementDigest && definition.LocalId == "stone-hearth";
    }
}
