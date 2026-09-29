using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmContentTests
{
    [Fact]
    public async Task HouseholdGrainMustReachItsFarmhouseBeforeFlourProduction()
    {
        using var seed = await PreparedWorldAsync("farmhouse-stock");
        var original = seed.ExportState();
        var alpha = original.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-alpha").Id;
        var beta = original.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-beta").Id;
        var inventory = InventoryFixture.AddLot(original.Society.Society.Inventory,
            "farmhouse-stone", "stone", "household:camp-alpha", 2);
        inventory = InventoryFixture.AddLot(inventory, "field-grain", "grain", "household:camp-alpha", 8);
        var state = original with
        {
            Society = original.Society with { Society = original.Society.Society with { Inventory = inventory } },
        };
        using var placing = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("safe_idle"));
        var farmhouse = placing.WorldContent.Buildings.Single(item => item.LocalId == "farmhouse-1x1");
        var town = Assert.Single(placing.ExportState().Towns!);
        BuildingPlacementResult? placed = null;
        foreach (var point in state.Map.Tiles.Select(tile => tile.Position)
                     .Where(point => TownBorderRules.IsWithinOrAdjacent(town, point, 1, 1)))
        {
            var attempt = placing.PlaceBuilding("farmhouse-alpha", farmhouse.CanonicalId, point,
                "household:camp-alpha");
            if (!attempt.Applied) continue;
            placed = attempt;
            break;
        }
        Assert.NotNull(placed);
        Assert.Equal("household:camp-alpha", placing.WorldSimulation.Buildings
            .Single(item => item.InstanceId == placed.InstanceId).HouseholdId);
        var mill = placing.WorldContent.Recipes.Single(item => item.LocalId == "mill-grain");
        var unstocked = placing.StartProduction(mill.CanonicalId, placed.InstanceId, alpha);
        Assert.False(unstocked.Applied);
        Assert.Contains("on-site", unstocked.Failure, StringComparison.Ordinal);

        state = placing.ExportState() with
        {
            Inhabitants = placing.ExportState().Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with { Position = state.Map.GetObject("storage").Position, HungerBasisPoints = 9_000 }
                : person).ToArray(),
        };
        using var hauling = PrivateWorldRuntime.Restore(state,
            id => new CandidateProvider(id == alpha ? "haul_farm_grain" : "safe_idle"));
        for (var tick = 0; tick < 25 && !hauling.ExportState().Events.Any(item =>
                 item.Kind == "farm_grain_picked_up" && item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await hauling.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(hauling.ExportState().Events, item => item.Kind == "farm_grain_picked_up" &&
            item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal));
        var carried = hauling.Society.Inventory.Lots.Single(lot => lot.OwnerId == alpha &&
            lot.DeliveryBuildingId == placed.InstanceId);
        Assert.Null(carried.StorageBuildingId);
        Assert.Empty(new OwnerWorldObservationStore(hauling).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == placed.InstanceId).StoredItems!);

        using var delivering = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(hauling.ExportState())),
            id => new CandidateProvider(id == alpha ? "haul_household_stock" : "safe_idle"));
        for (var tick = 0; tick < 25 && delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId != placed.InstanceId; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(placed.InstanceId, delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        Assert.Contains(new OwnerWorldObservationStore(delivering).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == placed.InstanceId).StoredItems!,
            item => item.Kind == "grain" && item.Quantity == 4);

        var outsider = delivering.StartProduction(mill.CanonicalId, placed.InstanceId, beta);
        Assert.False(outsider.Applied);
        Assert.Contains("Only a member", outsider.Failure, StringComparison.Ordinal);
        var started = delivering.StartProduction(mill.CanonicalId, placed.InstanceId, alpha);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < mill.DurationTicks; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        var flour = delivering.Society.Inventory.Lots.Single(lot => lot.OwnerId == "household:camp-alpha" &&
            lot.ItemKind == "flour" && lot.StorageBuildingId == placed.InstanceId);
        Assert.Equal(3, flour.Quantity);
        using var reloaded = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(delivering.ExportState())));
        Assert.Contains(new OwnerWorldObservationStore(reloaded).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == placed.InstanceId).StoredItems!,
            item => item.Kind == "flour" && item.Quantity == 3);

        var house = reloaded.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        var housePlaced = state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => TownBorderRules.IsWithinOrAdjacent(Assert.Single(reloaded.Towns), point, 1, 1))
            .Select(point => reloaded.PlaceBuilding("flour-home-alpha", house.CanonicalId, point,
                "household:camp-alpha"))
            .First(result => result.Applied);
        using var collecting = PrivateWorldRuntime.Restore(reloaded.ExportState(),
            id => new CandidateProvider(id == alpha ? "haul_farm_flour" : "safe_idle"));
        for (var tick = 0; tick < 40 && !collecting.ExportState().Events.Any(item =>
                 item.Kind == "farm_flour_picked_up" && item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(collecting.ExportState().Events, item => item.Kind == "farm_flour_picked_up" &&
            item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal));
        var carriedFlour = collecting.Society.Inventory.Lots.Single(lot => lot.OwnerId == alpha &&
            lot.ItemKind == "flour" && lot.DeliveryBuildingId == housePlaced.InstanceId);
        Assert.Null(carriedFlour.StorageBuildingId);
        Assert.Empty(new OwnerWorldObservationStore(collecting).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == housePlaced.InstanceId).StoredItems!);

        using var delivered = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collecting.ExportState())),
            id => new CandidateProvider(id == alpha ? "haul_household_stock" : "safe_idle"));
        for (var tick = 0; tick < 40 && delivered.Society.Inventory.GetLot(carriedFlour.Id).StorageBuildingId != housePlaced.InstanceId; tick++)
            Assert.True((await delivered.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(housePlaced.InstanceId, delivered.Society.Inventory.GetLot(carriedFlour.Id).StorageBuildingId);
        Assert.Contains(new OwnerWorldObservationStore(delivered).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == housePlaced.InstanceId).StoredItems!,
            item => item.Kind == "flour" && item.Quantity == 3);
    }

    [Fact]
    public async Task GrainFieldYieldBelongsToTheActualFarmerHousehold()
    {
        using var seed = await PreparedWorldAsync("grain-field-owner");
        var state = seed.ExportState();
        var beta = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-beta").Id;
        var field = state.Map.GetResource(SeededMapGenerator.FertileLandResourceId).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == beta
                ? person with { Position = field, HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("safe_idle"));
        var crop = world.WorldContent.Recipes.Single(item => item.LocalId == "universal-grain-field");
        var started = world.StartProduction(crop.CanonicalId, WorldBuildSiteRules.FertileLandSiteId(field), beta);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < crop.DurationTicks; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == "household:camp-beta" &&
            lot.ItemKind == "grain" && lot.Id.StartsWith(started.JobId + ":output:", StringComparison.Ordinal));
    }

    private static async Task<PrivateWorldRuntime> PreparedWorldAsync(string seed)
    {
        var world = new PrivateWorldRuntime(seed, _ => new CandidateProvider("safe_idle"),
            startPace: WorldStartPace.FounderSetup);
        var founders = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < founders.Length; index++)
            world.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founders[index]);
        world.StartWorld();
        Assert.True(world.StageStarterContent());
        for (var tick = 0; tick < 9; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.WorldContent.Buildings, item => item.LocalId == "farmhouse-1x1");
        return world;
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
