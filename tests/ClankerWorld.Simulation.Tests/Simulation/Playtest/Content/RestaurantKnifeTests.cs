using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class RestaurantKnifeTests
{
    private static readonly string[] PorridgeInputs = ["porridge-fuel", "porridge-grain", "porridge-water"];

    [Fact]
    public async Task CarriedKnifeSpeedsRestaurantCookingAndWearsWithoutChangingInputsOrServings()
    {
        using var seed = NormalPathWorld.CreateGenerated("restaurant-knife", _ => new Idle());
        var state = seed.ExportState();
        var house = seed.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var actor = seed.Society.Inhabitants.First(person => person.HouseholdId == house.HouseholdId).Id;
        var definition = seed.WorldContent.Buildings.Single(building => building.Tags.Contains("restaurant"));
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "restaurant-funding-" + cost.ResourceId,
                cost.ResourceId, house.HouseholdId!, cost.Amount, storageBuildingId: house.InstanceId);
        using var placing = Load(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } });
        var placed = false;
        foreach (var point in seed.Towns.Single().BorderTiles.OrderBy(point => state.Map.FootDistance(house.Position, point)))
            if (placing.PlaceBuilding("knife-restaurant", definition.CanonicalId, point, house.HouseholdId).Applied)
            {
                placed = true;
                break;
            }
        Assert.True(placed, "The generated Town needs a clear site for a paid Restaurant.");
        state = placing.ExportState();
        var restaurant = placing.WorldSimulation.Buildings.Single(building => building.InstanceId == "knife-restaurant");
        inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "porridge-grain", "grain", house.HouseholdId!, 1,
            storageBuildingId: restaurant.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "porridge-fuel", "wood", house.HouseholdId!, 1,
            storageBuildingId: restaurant.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "porridge-jug", "water_jug", house.HouseholdId!, 1,
            storageBuildingId: restaurant.InstanceId, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, "porridge-water", "water", house.HouseholdId!, 1,
            storageBuildingId: restaurant.InstanceId, containerLotId: "porridge-jug");
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = restaurant.Position, HungerBasisPoints = 9_500, LastDecisionContext = null } : person).ToArray()
        };
        using var plain = Load(state);
        inventory = InventoryFixture.AddLot(inventory, "carried-knife", "knife", actor, 1);
        using var withKnife = Load(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } });
        var recipe = plain.WorldContent.Recipes.Single(item => item.LocalId == "porridge" && item.Tags.Contains("restaurant-cooking"));
        var plainStart = plain.StartProduction(recipe.CanonicalId, restaurant.InstanceId, actor);
        var knifeStart = withKnife.StartProduction(recipe.CanonicalId, restaurant.InstanceId, actor);
        Assert.True(plainStart.Applied, plainStart.Failure);
        Assert.True(knifeStart.Applied, knifeStart.Failure);
        var plainJob = Assert.Single(plain.WorldSimulation.ProductionJobs);
        var knifeJob = Assert.Single(withKnife.WorldSimulation.ProductionJobs);
        var capability = ToolCapabilities.ForItem("knife")!;
        Assert.Equal(recipe.DurationTicks, plainJob.CompletionTick - plainJob.StartedTick);
        Assert.Equal(recipe.DurationTicks / capability.WorkQuantity, knifeJob.CompletionTick - knifeJob.StartedTick);
        Assert.True(knifeJob.CompletionTick < plainJob.CompletionTick);
        Assert.Equal(10_000 - capability.WearPerUse, withKnife.Society.Inventory.GetLot("carried-knife").ConditionBasisPoints);
        for (var tick = 0; tick < recipe.DurationTicks / capability.WorkQuantity; tick++)
        {
            Assert.True((await plain.AdvanceOneTickAsync()).Advanced);
            Assert.True((await withKnife.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(WorldProductionJobState.Running, plain.WorldSimulation.ProductionJobs.Single().State);
        Assert.Equal(WorldProductionJobState.Completed, withKnife.WorldSimulation.ProductionJobs.Single().State);
        Assert.DoesNotContain(plain.Society.Inventory.Lots, lot => lot.ItemKind == "porridge");
        for (var tick = recipe.DurationTicks / capability.WorkQuantity; tick < recipe.DurationTicks; tick++)
            Assert.True((await plain.AdvanceOneTickAsync()).Advanced);
        foreach (var world in new[] { plain, withKnife })
        {
            var job = world.WorldSimulation.ProductionJobs.Single();
            Assert.Equal(WorldProductionJobState.Completed, job.State);
            var meal = world.Society.Inventory.GetLot(job.JobId + ":output:00");
            Assert.Equal(("porridge", 2, house.HouseholdId, restaurant.InstanceId),
                (meal.ItemKind, meal.Quantity, meal.OwnerId, meal.StorageBuildingId));
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "porridge-grain" or "porridge-fuel" or "porridge-water");
            var jug = world.Society.Inventory.GetLot("porridge-jug");
            Assert.Equal((1, house.HouseholdId, restaurant.InstanceId), (jug.Quantity, jug.OwnerId, jug.StorageBuildingId));
            var inputs = job.InputReservationIds.Select(world.Society.Inventory.GetReservation).ToArray();
            Assert.Equal(3, inputs.Sum(input => input.Quantity));
            Assert.Equal(PorridgeInputs, inputs.Select(input => input.LotId).Order(StringComparer.Ordinal));
            Assert.All(inputs, input => Assert.Equal(InventoryReservationState.Completed, input.State));
            using var restored = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
            restored.Validate();
        }
    }

    private static PrivateWorldRuntime Load(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new Idle());

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(item => item.Id == "safe_idle")] }
            }, cancellationToken);
    }
}
