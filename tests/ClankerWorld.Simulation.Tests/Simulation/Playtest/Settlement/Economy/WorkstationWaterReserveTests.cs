using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorkstationWaterReserveTests
{
    private const string Household = "household:camp-alpha";
    private const string House = "first-town-house-a";

    [Fact]
    public async Task OnlyJugAtANeededSourceStaysThereInsteadOfShuttlingToAnotherWorkstation()
    {
        var state = WithRestaurant();
        var actor = Actor(state);
        state = WithInventory(state, AddJug(state.Society.Society.Inventory, "source-jug", House, 6));
        var choices = new WaterChoices("supply_workstation:water");
        using var world = Load(state, actor, choices);
        for (var tick = 0; tick < 16; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("supply_workstation:water", choices.Offered);
        Assert.Equal(House, world.Society.Inventory.GetLot("source-jug").StorageBuildingId);
        Assert.Equal(6, world.Society.Inventory.GetLot("source-jug:water").Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "workstation_water_picked_up");
        AssertReload(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherJugMustLeaveActualUnreservedSourceWaterBeforeAWholeJugCanMove(bool reserveRemainingWater)
    {
        var state = WithRestaurant();
        var actor = Actor(state);
        var inventory = AddJug(AddJug(state.Society.Society.Inventory, "a-moving-jug", House, 6), "z-staying-jug", House, 2);
        if (reserveRemainingWater)
            inventory = InventoryFixture.Reserve(inventory, "source-recipe-water", Household,
                "z-staying-jug:water", 2, "actual-source-input", 100);
        state = WithInventory(state, inventory);
        var choices = new WaterChoices("supply_workstation:water", "haul_household_stock");
        using var world = Load(state, actor, choices);
        for (var tick = 0; tick < 32 && (reserveRemainingWater ||
            world.Society.Inventory.GetLot("a-moving-jug").StorageBuildingId != "reserve-restaurant"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var moving = world.Society.Inventory.GetLot("a-moving-jug");
        var contents = world.Society.Inventory.GetLot("a-moving-jug:water");
        Assert.Equal(Household, moving.OwnerId);
        Assert.Equal(Household, contents.OwnerId);
        Assert.Equal("a-moving-jug", contents.ContainerLotId);
        Assert.Equal(6, contents.Quantity);
        Assert.Equal(reserveRemainingWater ? House : "reserve-restaurant", moving.StorageBuildingId);
        Assert.Equal(moving.StorageBuildingId, contents.StorageBuildingId);
        Assert.Equal(House, world.Society.Inventory.GetLot("z-staying-jug").StorageBuildingId);
        Assert.Equal(2, world.Society.Inventory.GetLot("z-staying-jug:water").Quantity);
        Assert.Equal(reserveRemainingWater ? 0 : 1, world.ExportState().Events.Count(item => item.Kind == "workstation_water_picked_up"));
        if (reserveRemainingWater)
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("source-recipe-water").State);
        AssertReload(world);
    }

    [Fact]
    public async Task ASourceWhoseOutputsAreAlreadySatisfiedCanSupplyItsLastJug()
    {
        var state = WithRestaurant();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "enough-house-meals", "simple_meal", Household, 4,
            storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "enough-paper", "paper", Household, 4, storageBuildingId: House);
        state = WithInventory(state, AddJug(inventory, "unneeded-source-jug", House, 6));
        using var world = Load(state, actor, new WaterChoices("supply_workstation:water", "haul_household_stock"));
        for (var tick = 0; tick < 32 && world.Society.Inventory.GetLot("unneeded-source-jug").StorageBuildingId != "reserve-restaurant"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("reserve-restaurant", world.Society.Inventory.GetLot("unneeded-source-jug").StorageBuildingId);
        Assert.Equal(6, world.Society.Inventory.GetLot("unneeded-source-jug:water").Quantity);
        AssertReload(world);
    }

    [Fact]
    public async Task DistinctNeededWaterAndMilkSitesMakeFillAndDeliverAnotherRealJugAcrossReload()
    {
        var state = WithRestaurant();
        state = PlacePaid(state, "reserve-clinic", state.WorldContent!.Buildings.Single(definition => definition.Tags.Contains("clinic")));
        var actor = Actor(state);
        var inventory = AddJug(AddJug(state.Society.Society.Inventory, "existing-house-jug", House, 8),
            "existing-clinic-jug", "reserve-clinic", 8);
        inventory = AddJug(inventory, "existing-house-milk-jug", House, 2, "milk");
        inventory = AddJug(inventory, "existing-restaurant-milk-jug", "reserve-restaurant", 2, "milk");
        inventory = InventoryFixture.AddLot(inventory, "new-jug-clay", "clay", Household, 2, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "new-jug-fuel", "wood", Household, 1, storageBuildingId: House);
        state = WithInventory(state, inventory);
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "water-jug");
        var choices = new WaterChoices("build:recipe:" + recipe.CanonicalId, "continue_project");
        using var world = Load(state, actor, choices);
        for (var tick = 0; tick < 40 && !world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == recipe.CanonicalId); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var job = Assert.Single(world.WorldSimulation.ProductionJobs, job => job.RecipeId == recipe.CanonicalId);
        using var replay = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), actor,
            new WaterChoices("build:recipe:" + recipe.CanonicalId, "continue_project"));
        for (var tick = 0; tick < 24; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(WorldProductionJobState.Completed, world.WorldSimulation.ProductionJobs.Single(item => item.JobId == job.JobId).State);
        var output = world.Society.Inventory.GetLot(job.JobId + ":output:00");
        Assert.Equal("water_jug", output.ItemKind);
        Assert.Equal(1, output.Quantity);
        Assert.Equal(8, output.ContainerCapacity);
        Assert.Equal(Household, output.OwnerId);
        Assert.Equal(House, output.StorageBuildingId);
        Assert.Contains(job.InputReservationIds.Select(world.Society.Inventory.GetReservation), reservation =>
            reservation.LotId == "new-jug-clay" && reservation.Quantity == 2 && reservation.State == InventoryReservationState.Completed);
        Assert.Contains(job.InputReservationIds.Select(world.Society.Inventory.GetReservation), reservation =>
            reservation.LotId == "new-jug-fuel" && reservation.Quantity == 1 && reservation.State == InventoryReservationState.Completed);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "new-jug-clay" or "new-jug-fuel");
        Assert.Equal(8, world.Society.Inventory.GetLot("existing-house-jug:water").Quantity);
        Assert.Equal(8, world.Society.Inventory.GetLot("existing-clinic-jug:water").Quantity);
        var supplied = new WaterChoices("water_collect_jug", "water_fill", "water_deliver", "haul_household_stock", "supply_workstation:water");
        // This directed fixture changes its chooser's permitted task phase once;
        // let that new phase reconsider the completed craft's saved idle decision.
        var fillingState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        fillingState = fillingState with
        {
            Inhabitants = fillingState.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { LastDecisionContext = null } : person).ToArray()
        };
        using var filling = Load(fillingState, actor, supplied);
        using var fillingReplay = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(fillingState)), actor,
            new WaterChoices("water_collect_jug", "water_fill", "water_deliver", "haul_household_stock", "supply_workstation:water"));
        for (var tick = 0; tick < 160 && !filling.Society.Inventory.Lots.Any(lot => lot.ItemKind == "water" &&
            lot.StorageBuildingId == "reserve-restaurant"); tick++)
        {
            Assert.True((await filling.AdvanceOneTickAsync()).Advanced);
            Assert.True((await fillingReplay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(filling.ExportState()), PrivateWorldRuntimeCodec.Encode(fillingReplay.ExportState()));
        Assert.Contains(filling.ExportState().Events, item => item.Kind == "water_collected" && item.Detail.Contains(output.Id, StringComparison.Ordinal));
        Assert.Contains(filling.ExportState().Events, item => item.Kind == "water_delivered" && item.Detail.Contains(output.Id, StringComparison.Ordinal));
        Assert.Contains(filling.Society.Inventory.Lots, lot => lot.ContainerLotId == output.Id && lot.ItemKind == "water" && lot.Quantity == 8);
        Assert.Equal("reserve-restaurant", filling.Society.Inventory.GetLot("existing-house-jug").StorageBuildingId);
        Assert.Equal(8, filling.Society.Inventory.GetLot("existing-house-jug:water").Quantity);
        Assert.Contains(filling.Society.Inventory.Lots, lot => lot.ItemKind == "water" && lot.StorageBuildingId == House && lot.Quantity == 8);
        AssertReload(world);
        AssertReload(filling);
    }

    [Fact]
    public async Task OutputSatisfiedSitesDoNotIncreaseTheExistingTwoJugTarget()
    {
        var state = WithRestaurant();
        state = PlacePaid(state, "reserve-clinic", state.WorldContent!.Buildings.Single(definition => definition.Tags.Contains("clinic")));
        var actor = Actor(state);
        var inventory = AddJug(AddJug(state.Society.Society.Inventory, "enough-jug-a", House, 8), "enough-jug-b", House, 8);
        foreach (var (id, kind, amount) in new[] { ("enough-meals", "simple_meal", 6), ("enough-paper", "paper", 4),
            ("enough-medicine", "medicine", 4), ("idle-jug-clay", "clay", 2) })
            inventory = InventoryFixture.AddLot(inventory, id, kind, Household, amount, storageBuildingId: House);
        state = WithInventory(state, inventory);
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "water-jug");
        var choices = new WaterChoices("build:recipe:" + recipe.CanonicalId);
        using var world = Load(state, actor, choices);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("build:recipe:" + recipe.CanonicalId, choices.Offered);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        Assert.Equal(2, world.Society.Inventory.GetLot("idle-jug-clay").Quantity);
        AssertReload(world);
    }

    private static PrivateWorldRuntimeState WithRestaurant()
    {
        using var seed = NormalPathWorld.CreateGenerated("cooking-flow", _ => new WaterChoices());
        var state = seed.ExportState();
        var inventory = state.Society.Society.Inventory;
        var food = inventory.GetLot("food:camp-alpha");
        inventory = InventoryFixture.Reserve(inventory, "earlier-household-meals", Household, food.Id, food.Quantity, "earlier-meals", 100);
        inventory = InventoryFixture.ConsumeReservation(inventory, "earlier-household-meals");
        state = WithInventory(state, inventory);
        state = PlacePaid(state, "reserve-restaurant", state.WorldContent!.Buildings.Single(definition => definition.Tags.Contains("restaurant")));
        var actor = Actor(state);
        var point = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
        var previous = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? point : person.Position == point ? previous : person.Position,
            }).ToArray(),
        };
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "workstation-water-basket", "basket", actor, 1);
        using var equipped = Load(WithInventory(state, inventory), actor, new WaterChoices());
        var result = equipped.EquipItem(actor, "workstation-water-basket");
        Assert.True(result.Applied, result.Failure);
        var equipment = equipped.Inhabitants.Single(person => person.InhabitantId == actor).Equipment;
        Assert.Equal("workstation-water-basket", equipment!.CarryAidLotId);
        Assert.Equal(16, PersonalEquipmentRules.Capacity(equipped.Society.Inventory, actor, equipment));
        Assert.True(PersonalEquipmentRules.FreeCapacity(equipped.Society.Inventory, actor, equipment) >= 9);
        return equipped.ExportState();
    }

    private static PrivateWorldRuntimeState PlacePaid(PrivateWorldRuntimeState state, string id, BuildingDefinition definition)
    {
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, id + ":cost:" + cost.ResourceId, cost.ResourceId, Household, cost.Amount,
                storageBuildingId: House);
        using var builder = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new WaterChoices());
        Assert.Contains(state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(builder.WorldSimulation.Buildings.Single(site =>
                site.InstanceId == House).Position, tile.Position)), tile => builder.PlaceBuilding(id, definition.CanonicalId, tile.Position, Household).Applied);
        Assert.Contains(builder.WorldSimulation.Buildings, site => site.InstanceId == id);
        return builder.ExportState();
    }

    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household).Id;

    private static InventoryCheckpoint AddJug(InventoryCheckpoint inventory, string id, string building, int water, string liquid = "water")
    {
        inventory = InventoryFixture.AddLot(inventory, id, "water_jug", Household, 1, storageBuildingId: building, containerCapacity: 8);
        return InventoryFixture.AddLot(inventory, id + ":water", liquid, Household, water, storageBuildingId: building, containerLotId: id);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntime Load(PrivateWorldRuntimeState state, string actor, WaterChoices choices) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? choices : new WaterChoices());

    private static void AssertReload(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new WaterChoices());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private sealed class WaterChoices(params string[] allowed) : IDecisionProvider
    {
        public List<string> Offered { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates.Select(candidate => candidate.Id));
            var choice = request.Observation.Candidates.Where(candidate => allowed.Contains(candidate.Id, StringComparer.Ordinal))
                .OrderBy(candidate => candidate.DeterministicPriority).FirstOrDefault() ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [choice] },
            }, cancellationToken);
        }
    }
}
