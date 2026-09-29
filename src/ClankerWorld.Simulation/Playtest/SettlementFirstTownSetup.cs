using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    /// <summary>Accept or replace the starter footprint before any founder is placed.</summary>
    public FirstTownLayout AcceptFirstTownLayout(GridPoint roughSite)
    {
        gate.Wait();
        try
        {
            if (geographyOptions is null || founderSetup is not { Started: false, FounderIds.Count: 0 } ||
                !society.Checkpoint.IsPaused || WorldTick != 0 ||
                worldSimulation.ProductionJobs.Count != 0 || (worldSimulation.CropBuilds?.Count ?? 0) != 0)
                throw new InvalidOperationException("Choose the first Town layout during paused setup before placing founders.");
            var existing = worldSimulation.Buildings;
            if (existing.Any(building => !building.InstanceId.StartsWith("first-town-", StringComparison.Ordinal)) ||
                existing.Count is not (0 or 5))
                throw new InvalidOperationException("Other building work prevents replacing the initial layout.");
            var plan = FirstTownLayoutPlanner.Plan(map, roughSite)
                ?? throw new ArgumentException("No connected five-building layout fits near this rough site.", nameof(roughSite));
            var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
            if (plan.Buildings.Any(building => !definitions.ContainsKey(building.DefinitionId)))
                throw new InvalidOperationException("The initial building definitions are not active.");

            var placed = plan.Buildings.Select(building => new PlacedBuilding(
                "first-town-" + building.Role, building.DefinitionId, building.Position, 0,
                TownBorderRules.FirstTownId,
                building.Role switch
                {
                    "house-a" => HouseholdId,
                    "house-b" => SecondHouseholdId,
                    _ => null,
                })).OrderBy(building => building.InstanceId, StringComparer.Ordinal).ToArray();
            var town = TownBorderRules.CreateFirstTown(map, originSite: roughSite);
            foreach (var building in placed)
            {
                var definition = definitions[building.DefinitionId];
                town = town with
                {
                    AssignedBuildingIds = town.AssignedBuildingIds.Append(building.InstanceId)
                        .Order(StringComparer.Ordinal).ToArray(),
                    BorderTiles = TownBorderRules.ExpandForBuilding(map, town, building.Position,
                        definition.Width, definition.Height),
                };
            }
            var starterInventory = PrepareFirstTownStock(society.Checkpoint.Inventory);
            var firstTownWasUnplaced = towns.Count == 0;
            worldSimulation = WorldContentSimulationState.Empty with { Buildings = placed };
            towns = [town];
            roadTiles = plan.RoadTiles.ToHashSet();
            ApplyInventoryTransition(_ => starterInventory);
            checkpointSchemaVersion = StateSchemaVersion;
            if (firstTownWasUnplaced) AppendEvent("town_founding_started", TownBorderRules.FirstTownId);
            AppendEvent(existing.Count == 0 ? "first_town_layout_accepted" : "first_town_layout_redone",
                $"{roughSite.X},{roughSite.Y}:buildings:{placed.Length}:roads:{roadTiles.Count}");
            return plan;
        }
        finally { gate.Release(); }
    }

    private static InventoryCheckpoint PrepareFirstTownStock(InventoryCheckpoint source)
    {
        const string firstHouse = "first-town-house-a";
        const string secondHouse = "first-town-house-b";
        const string warehouse = "first-town-warehouse";
        var foodLocations = new Dictionary<string, (string OwnerId, string BuildingId)>(StringComparer.Ordinal)
        {
            [FoodLotId] = (HouseholdId, firstHouse),
            ["food:camp-beta"] = (SecondHouseholdId, secondHouse),
        };
        foreach (var (lotId, location) in foodLocations)
        {
            if (!source.Lots.Any(lot => lot.Id == lotId && lot.ItemKind == "food" &&
                    lot.OwnerId == location.OwnerId && lot.Quantity > 0 &&
                    lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0))
                throw new InvalidOperationException("The starting households need usable food before a Town site can be accepted.");
        }

        var stock = source with
        {
            Lots = source.Lots.Select(lot => foodLocations.TryGetValue(lot.Id, out var location)
                ? lot with { StorageBuildingId = location.BuildingId }
                : lot).ToArray(),
        };
        if (!stock.Lots.Any(lot => lot.Id == "first-town-wooden-axe"))
            stock = InventoryFixture.AddLot(stock, "first-town-wooden-axe", "wooden_axe",
                TownBorderRules.FirstTownId, 1, storageBuildingId: warehouse);
        if (!stock.Lots.Any(lot => lot.Id == "first-town-wooden-pickaxe"))
            stock = InventoryFixture.AddLot(stock, "first-town-wooden-pickaxe", "wooden_pickaxe",
                TownBorderRules.FirstTownId, 1, storageBuildingId: warehouse);
        return stock;
    }
}
