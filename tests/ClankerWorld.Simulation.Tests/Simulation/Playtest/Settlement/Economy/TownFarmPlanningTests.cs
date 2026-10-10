using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownFarmPlanningTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 8)]
    [InlineData(2, 0)]
    [InlineData(2, 8)]
    public async Task NativeFarmerPreparesItsShareOfTheTownShortageAndStopsAtTheFieldLimit(int farms, int food)
    {
        var (state, actor, household, point) = Fixture();
        var otherHousehold = state.Society.Society.Households.Single(item => item.Id != household).Id;
        if (farms == 2) state = AddFarmhouse(state, otherHousehold);
        if (food > 0) state = FarmFieldTests.WithInventory(state,
            InventoryFixture.AddLot(state.Society.Society.Inventory, "town-farm-reserve", "food", household, food));
        Assert.Equal(4, Assert.Single(state.Towns!).ResidentIds.Count);
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var yield = FarmFieldRules.HarvestQuantity(FarmFieldRules.Grain, new LandFertility(state.Map, state.WorldSeed).At(farmhouse.Position));
        var wanted = (int)Math.Ceiling((16d - food) / farms / yield);
        Assert.True(wanted > 0);
        var fieldPoints = FreeFieldPoints(state, point).Take(wanted - 1).ToArray();
        state = state with { Fields = fieldPoints.Select(position => new FarmFieldState(position, household, FarmFieldStage.Prepared)).ToArray() };
        var choice = new FarmChoice(actor, "farm:Till:");
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => choice);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new FarmChoice(actor, "farm:Till:"));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 100; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            if (world.Fields.Count == wanted && world.Fields.All(field => field.Stage == FarmFieldStage.Prepared)) break;
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.True(choice.Offered);
        Assert.Equal(wanted, world.Fields.Count);
        Assert.All(world.Fields, field => Assert.Equal(FarmFieldStage.Prepared, field.Stage));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "field_prepared");
        var stoppedChoice = new FarmChoice(actor, "farm:Till:");
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => stoppedChoice);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        await Advance(reload, 8);
        Assert.False(stoppedChoice.Offered);
        Assert.Equal(wanted, reload.Fields.Count);
        if (food > 0) Assert.Equal(food, reload.Society.Inventory.GetLot("town-farm-reserve").Quantity);
        reload.Validate();
    }

    [Theory]
    [InlineData("farmer", 8, true)]
    [InlineData("neighbor", 16, false)]
    [InlineData("resident", 16, false)]
    [InlineData("foreign-town", 16, true)]
    public async Task NativePlantingUsesOnlyItsTownReserveIncludingOtherHouseholdsAndCarriedFood(string owner, int food, bool shouldPlant)
    {
        var (state, actor, household, point) = Fixture();
        var neighbor = state.Society.Society.Households.Single(item => item.Id != household).Id;
        var neighborIds = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == neighbor).Select(person => person.Id).ToArray();
        if (owner == "foreign-town")
        {
            var first = Assert.Single(state.Towns!);
            var remaining = first.ResidentIds.Except(neighborIds).ToArray();
            var site = state.Map.Tiles.Select(tile => tile.Position).First(position => state.Map.IsBuildable(position) && !first.BorderTiles.Contains(position));
            state = state with
            {
                Towns = [first with { ResidentIds = remaining, Governance = TownGovernanceState.Create(remaining) },
                    new("town:farm-neighbor", "Neighbor Town", "founded", 0, neighborIds, [], [site], site,
                        TownGovernanceState.Create(neighborIds), TownGovernmentState.Create())],
            };
        }
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "town-planting-seed", "grain_seed", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "town-planting-food", "food",
            owner == "farmer" ? household : owner == "resident" ? neighborIds[0] : neighbor, food);
        state = FarmFieldTests.WithInventory(state, inventory) with { Fields = [new(point, household, FarmFieldStage.Prepared)] };
        var choice = new FarmChoice(actor, $"farm:Plant:{point.X}:{point.Y}:grain");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => choice);
        await Advance(world, 8);
        Assert.Equal(shouldPlant, choice.Offered);
        if (!shouldPlant) Assert.False(choice.OfferedTill);
        Assert.Equal(shouldPlant ? FarmFieldStage.Growing : FarmFieldStage.Prepared, Assert.Single(world.Fields).Stage);
        Assert.Equal(shouldPlant ? 0 : 1, world.Society.Inventory.Lots.Where(lot => lot.Id == "town-planting-seed").Sum(lot => lot.Quantity));
        Assert.Equal(food, world.Society.Inventory.GetLot("town-planting-food").Quantity);
        Assert.Equal(shouldPlant ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "field_planted"));
        world.Validate();
    }

    private static (PrivateWorldRuntimeState State, string Actor, string Household, GridPoint Point) Fixture()
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("town-farm-demand-native");
        foreach (var owner in state.Society.Society.Households.Select(item => item.Id))
            state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, owner);
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear) with
        {
            Survival = state.Survival ?? new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                LastDecisionContext = null,
                HungerBasisPoints = 10_000,
                Survival = (person.Survival ?? new SurvivalCondition()) with
                { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000, IllnessBasisPoints = 0 },
            }).ToArray(),
        };
        return (state, actor, household, point);
    }

    private static PrivateWorldRuntimeState AddFarmhouse(PrivateWorldRuntimeState state, string household)
    {
        var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "farmhouse-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "town-second-farm:" + cost.ResourceId, cost.ResourceId, household, cost.Amount);
        using var world = FarmFieldTests.Restore(FarmFieldTests.WithInventory(state, inventory));
        var town = Assert.Single(world.Towns);
        var placed = state.Map.Tiles.Select(tile => tile.Position).Where(position => TownBorderRules.IsWithinOrAdjacent(town, position, 1, 1))
            .Select(position => world.PlaceBuilding("town-second-farm", definition.CanonicalId, position, household)).First(result => result.Applied);
        Assert.NotNull(placed.InstanceId);
        return world.ExportState();
    }

    private static GridPoint[] FreeFieldPoints(PrivateWorldRuntimeState state, GridPoint first)
    {
        using var world = FarmFieldTests.Restore(state);
        var occupied = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(world.RoadTiles)
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var fertility = new LandFertility(state.Map, state.WorldSeed);
        return state.Map.Tiles.Select(tile => tile.Position).Where(position => fertility.CanFarm(position) && !occupied.Contains(position))
            .OrderBy(position => state.Map.FootDistance(first, position)).ToArray();
    }

    private static async Task Advance(PrivateWorldRuntime world, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    }

    private sealed class FarmChoice(string actor, string prefix) : IDecisionProvider
    {
        public bool Offered { get; private set; }
        public bool OfferedTill { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            if (observation.InhabitantId == actor && observation.Candidates.Any(item => item.Id.StartsWith("farm:Till:", StringComparison.Ordinal)))
                OfferedTill = true;
            var chosen = observation.InhabitantId == actor ? observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal)) : null;
            if (chosen is not null) Offered = true;
            chosen ??= observation.Candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind,
                ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                chosen.Id, 1, observation.Candidates.ToDictionary(item => item.Id, item => item.Id == chosen.Id ? 1d : 0d)));
        }
    }
}
