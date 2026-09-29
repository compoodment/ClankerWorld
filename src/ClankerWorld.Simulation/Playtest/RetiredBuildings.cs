using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Built-in buildings the accepted finished-game roster leaves out: House
/// covers shelter, cooking and household storage, and Warehouse covers the
/// communal stock. Their definitions stay in the shipped packages so saved
/// buildings and projects already under way keep loading and working, but
/// planning never offers a new one.
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
        return definition.LocalId == "stone-hearth" &&
            (definition.PackageDigest == SettlementDigest ||
             definition.PackageDigest == PrivateWorldRuntime.LegacySettlementPackageDigest);
    }
}
