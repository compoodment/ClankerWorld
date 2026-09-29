using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class WarehouseContentTests
{
    [Fact]
    public async Task ResidentsStoreAndCollectNonFoodAtTheWarehouseAcrossReload()
    {
        using var seed = new PrivateWorldRuntime("warehouse-stock", _ => new CandidateProvider("safe_idle"),
            startPace: WorldStartPace.FounderSetup);
        var founders = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < founders.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founders[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 9; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var definition = seed.WorldContent.Buildings.Single(item => item.LocalId == "warehouse-2x2");
        Assert.Equal((2, 2), (definition.Width, definition.Height));
        var initial = seed.ExportState();
        var town = Assert.Single(initial.Towns!);
        BuildingPlacementResult? placed = null;
        foreach (var point in initial.Map.Tiles.Select(tile => tile.Position)
                     .Where(point => TownBorderRules.IsWithinOrAdjacent(town, point, 2, 2)))
        {
            var attempt = seed.PlaceBuilding("town-warehouse", definition.CanonicalId, point);
            if (!attempt.Applied) continue;
            placed = attempt;
            break;
        }
        Assert.NotNull(placed);
        var warehouse = seed.WorldSimulation.Buildings.Single(item => item.InstanceId == placed.InstanceId);
        Assert.Equal(town.Id, warehouse.TownId);
        var duplicate = seed.PlaceBuilding("second-town-warehouse", definition.CanonicalId, warehouse.Position);
        Assert.False(duplicate.Applied);
        Assert.Contains("already", duplicate.Failure, StringComparison.OrdinalIgnoreCase);

        var state = seed.ExportState();
        var alpha = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-alpha").Id;
        var beta = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-beta").Id;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with { Position = warehouse.Position, HungerBasisPoints = 9_000 } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
                        "personal-warehouse-wood", "wood", alpha, 8),
                },
            },
        };
        using var depositing = PrivateWorldRuntime.Restore(state,
            id => new CandidateProvider(id == alpha ? "store_town_resources" : "safe_idle"));
        for (var tick = 0; tick < 20 && !depositing.ExportState().Events.Any(item =>
                 item.Kind == "town_resources_stored" && item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await depositing.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(depositing.ExportState().Events, item => item.Kind == "town_resources_stored" &&
            item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal));
        Assert.Equal(4, depositing.Society.Inventory.GetLot("personal-warehouse-wood").Quantity);
        var stored = new OwnerWorldObservationStore(depositing).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == warehouse.InstanceId).StoredItems!;
        Assert.Contains(stored, item => item.Kind == "wood" && item.Quantity == 4);

        var reloaded = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(depositing.ExportState()));
        var invalidFood = reloaded with
        {
            Society = reloaded.Society with
            {
                Society = reloaded.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(reloaded.Society.Society.Inventory,
                        "invalid-warehouse-food", "food", town.Id, 1, storageBuildingId: warehouse.InstanceId),
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalidFood));
        var invalidGrain = invalidFood with
        {
            Society = invalidFood.Society with
            {
                Society = invalidFood.Society.Society with
                {
                    Inventory = invalidFood.Society.Society.Inventory with
                    {
                        Lots = invalidFood.Society.Society.Inventory.Lots.Select(lot =>
                            lot.Id == "invalid-warehouse-food" ? lot with { ItemKind = "grain" } : lot).ToArray(),
                    },
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalidGrain));

        var house = seed.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        var materialPickup = reloaded with
        {
            Inhabitants = reloaded.Inhabitants.Select(person => person.InhabitantId == beta
                ? person with
                {
                    Position = warehouse.Position,
                    HungerBasisPoints = 9_000,
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(house.CanonicalId,
                            warehouse.Position), house.DisplayName, reloaded.Society.Society.WorldTick,
                        "acquiring", LastTransitionTick: reloaded.Society.Society.WorldTick),
                } : person).ToArray(),
            Society = reloaded.Society with
            {
                Society = reloaded.Society.Society with
                {
                    Inventory = reloaded.Society.Society.Inventory with
                    {
                        Lots = reloaded.Society.Society.Inventory.Lots.Where(lot =>
                            lot.OwnerId != "household:camp-beta" || lot.ItemKind != "wood").ToArray(),
                    },
                },
            },
        };
        using (var collectingMaterials = PrivateWorldRuntime.Restore(materialPickup, _ => new CandidateProvider("safe_idle")))
        {
            for (var tick = 0; tick < 20 && !collectingMaterials.ExportState().Events.Any(item =>
                     item.Kind == "town_resource_collected" && item.Detail.StartsWith(beta + ":", StringComparison.Ordinal)); tick++)
                Assert.True((await collectingMaterials.AdvanceOneTickAsync()).Advanced);
            Assert.Contains(collectingMaterials.ExportState().Events, item => item.Kind == "town_resource_collected" &&
                item.Detail.StartsWith(beta + ":", StringComparison.Ordinal));
            Assert.Contains(collectingMaterials.Society.Inventory.Lots, lot => lot.OwnerId == beta &&
                lot.ItemKind == "wood" && lot.StorageBuildingId is null);
        }

        var inventory = InventoryFixture.AddLot(reloaded.Society.Society.Inventory,
            "warehouse-tool", "tool", town.Id, 1, storageBuildingId: warehouse.InstanceId);
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot =>
            lot.OwnerId != "household:camp-beta" || lot.ItemKind != "tool").ToArray()
        };
        var workshop = seed.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
        reloaded = reloaded with
        {
            Inhabitants = reloaded.Inhabitants.Select(person => person.InhabitantId == beta
                ? person with
                {
                    Position = warehouse.Position,
                    HungerBasisPoints = 9_000,
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(workshop.CanonicalId,
                            warehouse.Position),
                        workshop.DisplayName, reloaded.Society.Society.WorldTick, "acquiring",
                        LastTransitionTick: reloaded.Society.Society.WorldTick),
                } : person).ToArray(),
            Society = reloaded.Society with { Society = reloaded.Society.Society with { Inventory = inventory } },
        };
        using var collecting = PrivateWorldRuntime.Restore(reloaded, _ => new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 20 && !collecting.ExportState().Events.Any(item =>
                 item.Kind == "equipment_collected" && item.Detail == beta + ":tool"); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(collecting.ExportState().Events, item => item.Kind == "equipment_collected" &&
            item.Detail == beta + ":tool");
        Assert.Equal(beta, collecting.Society.Inventory.GetLot("warehouse-tool").OwnerId);
        Assert.Null(collecting.Society.Inventory.GetLot("warehouse-tool").StorageBuildingId);
    }

    private sealed class CandidateProvider(string candidateId) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == candidateId) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}
