using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

internal sealed record BuildingOrderDefinition(BuildingDefinition Definition, string BuildingKind);

/// <summary>Exact shipped starting footprints supported by native building orders.</summary>
internal static class PrivateWorldBuildingOrderCatalog
{
    private static readonly BuildingOrderDefinition[] Entries =
    [
        new(HouseContent.House1x1(), "house"),
        new(AnimalContent.Yard(), "animal-yard"),
        new(FarmContent.Farmhouse1x1(), "farmhouse"),
        new(BlacksmithContent.Blacksmith1x2(), "blacksmith"),
        new(TailorContent.TailorShop1x1(), "tailor"),
        new(SiloContent.Silo1x1(), "silo"),
        new(CareContent.Clinic1x2(), "clinic"),
        new(RestaurantContent.Restaurant1x2(), "restaurant"),
        new(ContentDefinitionPayloadCodec.ApplyPackage(new([], []), BusinessContent.Create()).Buildings
            .Single(building => building.LocalId == "store-1x1"), "store"),
        new(WarehouseContent.Warehouse2x2(), "warehouse"),
    ];

    internal static BuildingOrderDefinition[] Available(DeclarativeWorldContentState content) =>
        content.Buildings.Select(Describe).OfType<BuildingOrderDefinition>().ToArray();

    internal static BuildingOrderDefinition? Find(DeclarativeWorldContentState content, string? definitionId) =>
        definitionId is null ? null : Describe(content.Buildings.FirstOrDefault(item => item.CanonicalId == definitionId));

    internal static bool Supports(string action, string? kind) => action switch
    {
        "construct_building" => kind is "house" or "farmhouse" or "blacksmith" or "tailor" or "silo" or "clinic" or "store" or "restaurant" or "animal-yard",
        "expand_building" => kind is "house" or "warehouse" or "animal-yard",
        _ => false,
    };

    private static BuildingOrderDefinition? Describe(BuildingDefinition? definition)
    {
        if (definition is null) return null;
        var entry = Entries.FirstOrDefault(item => item.Definition.CanonicalId == definition.CanonicalId &&
            item.Definition.PayloadDigest == definition.PayloadDigest);
        return entry is null ? null : new(definition, entry.BuildingKind);
    }
}
