using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Building kinds a single household holds, identified by tag, in the order a
/// household plans them. The Store is listed ahead of its content so that
/// planning and ownership treat it the same way once it exists.
/// </summary>
public static class HouseholdBuildingKinds
{
    public static IReadOnlyList<string> All { get; } = ["house", "farmhouse", "blacksmith", "silo", "tailor", "store", "clinic", "restaurant", AnimalContent.YardTag];

    public static bool IsKindTag(string tag) => All.Contains(tag, StringComparer.Ordinal);

    /// <summary>A kind's place in the planning order, or the end for other tags.</summary>
    public static int PlanOrder(string? kind)
    {
        for (var index = 0; index < All.Count; index++)
            if (All[index] == kind)
                return index;
        return All.Count;
    }

    public static string? KindOf(BuildingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return All.FirstOrDefault(kind => definition.Tags.Contains(kind, StringComparer.Ordinal));
    }
}
