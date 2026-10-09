using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class SwimmingExplorationReturnTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ProjectResourcePurposeRequiresALoadedReturnButKeepsFootSourcesUsable(bool existingPurpose, bool footReturn)
    {
        using var generated = NormalPathWorld.CreateGenerated("cart-cargo-orders", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = SettlementWeatherTestFixture.WithWeather(generated.ExportState(), WeatherKind.Clear);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == FoodCapacityTestFixture.House);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == house.HouseholdId).Id;
        var source = state.Map.Resources.OrderBy(item => state.Map.FootDistance(item.Position, house.Position))
            .FirstOrDefault(item => item.Kind == "construction" && state.Map.IsPassable(item.Position) &&
                state.Resources.Single(resource => resource.ResourceId == item.Id).State == ResourceState.Available &&
                state.WorldSystems!.Ecology.Resources.Single(resource => resource.Id == item.Id).Quantity > 0 &&
                state.Map.IsReachableOnFoot(item.Position, house.Position) == footReturn &&
                SwimmingRules.IsReachable(state.Map, item.Position, house.Position) &&
                state.Map.FootNeighbors(item.Position).Any(point => state.Map.IsPassable(point) &&
                    state.Inhabitants.All(person => person.Position != point)) &&
                state.Inhabitants.All(person => person.Position != item.Position));
        Assert.NotNull(source);
        Assert.Contains(state.Map.FootNeighbors(source.Position), point => state.Map.IsPassable(point) &&
            state.Inhabitants.All(person => person.Position != point));
        var definition = state.WorldContent!.Buildings.First(item => item.BuildCosts.Any(cost => cost.ResourceId == "wood") &&
            item.Tags.Any(tag => tag is "tailor" or "clinic" or "restaurant" or "store") &&
            !state.WorldSimulation.Buildings.Any(placed => placed.HouseholdId == house.HouseholdId && placed.DefinitionId == item.CanonicalId));
        var site = state.Map.FootNeighbors(house.Position).First(point => state.Map.IsBuildable(point) &&
            state.WorldSimulation.Buildings.All(building => building.Position != point));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != house.HouseholdId || lot.ItemKind != "wood")).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "swim-search-axe", HouseToolsContent.CrudeWoodenAxe, actor, 1);
        foreach (var input in definition.BuildCosts.Where(input => input.ResourceId != "wood"))
            inventory = InventoryFixture.AddLot(inventory, "swim-search-paid-" + input.ResourceId,
                input.ResourceId, house.HouseholdId!, input.Amount);
        var quantity = state.WorldSystems!.Ecology.Resources.Single(resource => resource.Id == source.Id).Quantity;
        var harvest = Assert.IsType<ToolGatheringPlan>(ToolProgressionRules.PlanGather("wood", source, inventory, actor, quantity));
        Assert.True(1 + harvest.Quantity + harvest.TreeSeedQuantity > SwimmingRules.MaximumCarriedUnits);
        Assert.True(harvest.Quantity + harvest.TreeSeedQuantity <= PersonalEquipmentRules.Capacity(inventory, actor, null) - 1);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Knowledge = state.Knowledge! with { Facts = state.Knowledge.Facts.Where(fact => fact.OwnerId != actor).ToArray() },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = source.Position,
                HungerBasisPoints = 10_000,
                Survival = new SurvivalCondition(10_000),
                Equipment = null,
                LastDecisionContext = null,
                TravelCooldownTicks = 0,
                Project = new(TownConstructionCandidateIds.Building(definition.CanonicalId, site), definition.DisplayName,
                    state.Society.Society.WorldTick, "paused", LastTransitionTick: state.Society.Society.WorldTick, RequiresFreshChoice: true),
                Exploration = existingPurpose ? new([source.Position], [source.Position], state.Society.Society.WorldTick, false,
                    Goal: new("resource", "wood")) : null,
            } : person).ToArray(),
        };
        var provider = new SearchProvider();
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        if (!existingPurpose)
            Assert.Equal(!footReturn, provider.Offered.Contains("explore_for:resource:wood", StringComparer.Ordinal));
        var person = world.Inhabitants.Single(item => item.InhabitantId == actor);
        Assert.Equal(footReturn ? null : new SettlementExplorationGoal("resource", "wood"), person.Exploration?.Goal);
        Assert.Equal(0, person.Project!.WorkDone);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "wood");
        Assert.Equal(quantity, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        world.Validate();
        loaded.Validate();
    }

    private sealed class SearchProvider : IDecisionProvider
    {
        public IReadOnlyList<string> Offered { get; private set; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered = request.Observation.Candidates.Select(item => item.Id).ToArray();
            var choice = request.Observation.Candidates.FirstOrDefault(item => item.Id == "explore_for:resource:wood") ??
                request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
