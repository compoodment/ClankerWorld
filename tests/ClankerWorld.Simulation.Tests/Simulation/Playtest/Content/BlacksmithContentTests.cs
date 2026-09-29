using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class BlacksmithContentTests
{
    [Fact]
    public async Task HouseholdMustDeliverWoodBeforeMakingAndCollectingWoodenAxe()
    {
        using var seed = new PrivateWorldRuntime("blacksmith-stock", _ => new CandidateProvider("safe_idle"),
            startPace: WorldStartPace.FounderSetup);
        var founders = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < founders.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founders[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 10; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var state = seed.ExportState();
        var alpha = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-alpha").Id;
        var beta = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-beta").Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "smith-stone", "stone", "household:camp-alpha", 4);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        using var placing = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("safe_idle"));
        var blacksmith = placing.WorldContent.Buildings.Single(item => item.LocalId == "blacksmith-1x2");
        var town = Assert.Single(state.Towns!);
        BuildingPlacementResult? placed = null;
        foreach (var point in state.Map.Tiles.Select(tile => tile.Position)
                     .Where(point => TownBorderRules.IsWithinOrAdjacent(town, point, 1, 2)))
        {
            var attempt = placing.PlaceBuilding("blacksmith-alpha", blacksmith.CanonicalId, point,
                "household:camp-alpha");
            if (!attempt.Applied) continue;
            placed = attempt;
            break;
        }
        Assert.NotNull(placed);
        Assert.Equal("household:camp-alpha", placing.WorldSimulation.Buildings
            .Single(item => item.InstanceId == placed.InstanceId).HouseholdId);
        var axeRecipe = placing.WorldContent.Recipes.Single(item => item.LocalId == "wooden-axe");
        var unstocked = placing.StartProduction(axeRecipe.CanonicalId, placed.InstanceId, alpha);
        Assert.False(unstocked.Applied);
        Assert.Contains("on-site", unstocked.Failure, StringComparison.Ordinal);

        state = placing.ExportState() with
        {
            Inhabitants = placing.ExportState().Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with { Position = state.Map.GetObject("storage").Position, HungerBasisPoints = 9_000 }
                : person).ToArray(),
        };
        using var hauling = PrivateWorldRuntime.Restore(state,
            id => new CandidateProvider(id == alpha ? "haul_smith_input" : "safe_idle"));
        for (var tick = 0; tick < 25 && !hauling.ExportState().Events.Any(item =>
                 item.Kind == "smith_input_picked_up" && item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await hauling.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(hauling.ExportState().Events, item => item.Kind == "smith_input_picked_up" &&
            item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal));
        var carried = hauling.Society.Inventory.Lots.Single(lot => lot.OwnerId == alpha &&
            lot.DeliveryBuildingId == placed.InstanceId);
        Assert.Equal("wood", carried.ItemKind);
        Assert.Empty(new OwnerWorldObservationStore(hauling).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == placed.InstanceId).StoredItems!);

        using var delivering = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(hauling.ExportState())),
            id => new CandidateProvider(id == alpha ? "haul_household_stock" : "safe_idle"));
        for (var tick = 0; tick < 25 && delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId != placed.InstanceId; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(placed.InstanceId, delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        var outsider = delivering.StartProduction(axeRecipe.CanonicalId, placed.InstanceId, beta);
        Assert.False(outsider.Applied);
        Assert.Contains("Only a member", outsider.Failure, StringComparison.Ordinal);
        var started = delivering.StartProduction(axeRecipe.CanonicalId, placed.InstanceId, alpha);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < axeRecipe.DurationTicks; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(new OwnerWorldObservationStore(delivering).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == placed.InstanceId).StoredItems!,
            item => item.Kind == "wooden_axe" && item.Quantity == 1);

        var collectingState = delivering.ExportState();
        collectingState = collectingState with
        {
            Inhabitants = collectingState.Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with { LastDecisionContext = null } : person).ToArray(),
        };
        using var collecting = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collectingState)),
            id => new CandidateProvider(id == alpha ? "collect_wooden_axe" : "safe_idle"));
        for (var tick = 0; tick < 20 && !collecting.Society.Inventory.Lots.Any(lot =>
                 lot.OwnerId == alpha && lot.ItemKind == "wooden_axe"); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(collecting.Society.Inventory.Lots, lot => lot.OwnerId == alpha &&
            lot.ItemKind == "wooden_axe" && lot.StorageBuildingId is null);

        var refiningRecipe = collecting.WorldContent.Recipes.Single(item => item.LocalId == "refine-iron");
        var noOre = collecting.StartProduction(refiningRecipe.CanonicalId, placed.InstanceId, alpha);
        Assert.False(noOre.Applied);
        Assert.Contains("on-site", noOre.Failure, StringComparison.Ordinal);
        var oreState = collecting.ExportState();
        oreState = oreState with
        {
            Society = oreState.Society with
            {
                Society = oreState.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(oreState.Society.Society.Inventory,
                        "smith-test-ore", "iron_ore", alpha, 2),
                },
            },
        };
        using var deliveringOre = PrivateWorldRuntime.Restore(oreState,
            id => new CandidateProvider(id == alpha ? "deliver_smith_ore" : "safe_idle"));
        for (var tick = 0; tick < 20 && deliveringOre.Society.Inventory.Lots.All(lot =>
                 lot.ItemKind != "iron_ore" || lot.StorageBuildingId != placed.InstanceId); tick++)
            Assert.True((await deliveringOre.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(deliveringOre.Society.Inventory.Lots, lot => lot.OwnerId == "household:camp-alpha" &&
            lot.ItemKind == "iron_ore" && lot.StorageBuildingId == placed.InstanceId && lot.Quantity == 2);
        using var refining = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(deliveringOre.ExportState())),
            _ => new CandidateProvider("safe_idle"));
        var refiningStart = refining.StartProduction(refiningRecipe.CanonicalId, placed.InstanceId, alpha);
        Assert.True(refiningStart.Applied, refiningStart.Failure);
        for (var tick = 0; tick < refiningRecipe.DurationTicks; tick++)
            Assert.True((await refining.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(new OwnerWorldObservationStore(refining).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == placed.InstanceId).StoredItems!,
            item => item.Kind == "iron" && item.Quantity == 1);
    }

    [Theory]
    [InlineData("wooden_axe", "wood", "house-1x1")]
    [InlineData("wooden_pickaxe", "stone", "farmhouse-1x1")]
    public async Task MatchingWoodenToolImprovesMaterialGathering(
        string tool, string material, string buildingLocalId)
    {
        using var seed = new PrivateWorldRuntime("gather-with-" + tool, _ => new CandidateProvider("safe_idle"));
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 10; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);
        var state = seed.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var householdId = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var source = state.Map.Resources.First(resource =>
            (resource.Kind == material || material == "wood" && resource.Kind == "construction") &&
            state.Map.IsReachableFromCampOnFoot(resource.Position));
        var building = seed.WorldContent.Buildings.Single(item => item.LocalId == buildingLocalId);
        var inventory = state.Society.Society.Inventory;
        if (material == "wood")
            inventory = inventory with
            {
                Lots = inventory.Lots.Where(lot => lot.OwnerId != householdId || lot.ItemKind != "wood").ToArray(),
            };
        inventory = InventoryFixture.AddLot(inventory, "gathering-tool", tool, actor, 1);
        var oldPosition = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = source.Position,
                    HungerBasisPoints = 9_000,
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(
                            building.CanonicalId, source.Position), building.DisplayName,
                        state.Society.Society.WorldTick, "acquiring",
                        LastTransitionTick: state.Society.Society.WorldTick),
                }
                : person.Position == source.Position ? person with { Position = oldPosition } : person).ToArray(),
        };
        using var gathering = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("safe_idle"));
        Assert.True((await gathering.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(gathering.ExportState().Events, item => item.Kind == "material_gathered" &&
            item.Detail == $"{actor}:{material}:6");
        Assert.Contains(gathering.Society.Inventory.Lots, lot => lot.OwnerId == actor &&
            lot.ItemKind == material && lot.Quantity == 6);
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
