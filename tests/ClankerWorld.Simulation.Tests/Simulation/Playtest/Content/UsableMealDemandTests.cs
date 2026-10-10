using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ConcreteMealTests
{
    private static readonly Lazy<Task<byte[]>> CookingDemandWorld = new(async () =>
    {
        using var world = NormalPathWorld.CreateGenerated("broken-ready-meal-cooking-audit", _ => new Chooser());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Theory]
    [InlineData("bread", true, false, true)]
    [InlineData("bread", false, false, false)]
    [InlineData(null, false, false, true)]
    [InlineData("bread", true, true, true)]
    [InlineData("porridge", true, false, true)]
    [InlineData("porridge", false, false, false)]
    public async Task HouseCooksWhenUsableMealsAreMissingOrAnOrderRequiresIt(
        string? storedMeal, bool broken, bool ordered, bool canCook)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await CookingDemandWorld.Value);
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        Assert.Equal(2, state.Society.Society.GetHousehold(household).MemberIds.Count);
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "bread" && item.WorkstationBuildingId == house.DefinitionId);
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [] };
        inventory = InventoryFixture.AddLot(inventory, "demand-flour", "flour", household, 2, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "demand-wood", "wood", household, 1, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "demand-jug", "water_jug", household, 1, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "demand-water", "fresh_water", household, 1,
            storageBuildingId: house.InstanceId, containerLotId: "demand-jug");
        if (storedMeal is not null)
        {
            inventory = InventoryFixture.AddLot(inventory, "demand-pot", "storage_pot", household, 1, storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "stored-meals", storedMeal, household, 4,
                storageBuildingId: house.InstanceId, containerLotId: "demand-pot");
            if (broken) inventory = InventoryFixture.WearSingleUnit(inventory, "demand-pot", 10_000);
        }
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Equipment = null,
                HungerBasisPoints = 9_500,
                LastDecisionContext = null,
                Project = null,
                Position = person.InhabitantId == actor ? house.Position : person.Position,
            }).ToArray(),
        };
        var choice = "build:recipe:" + recipe.CanonicalId;
        var chooser = new Chooser(choice, "produce_item");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? chooser : new Chooser());
        var receipt = ordered ? world.SubmitInstruction(new("demand-bread", "owner:test", actor,
            OwnerInstructionKind.MustDo, "make house bread")) : null;
        var original = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(original, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < (canCook ? 65 : 8) &&
            (!world.WorldSimulation.ProductionJobs.Any(job => job.State == WorldProductionJobState.Completed) ||
                receipt is not null && CookingOrder(world, receipt).Status != "finished"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(canCook, chooser.OfferedCandidates.Contains(choice) || chooser.OfferedCandidates.Contains("produce_item"));
        Assert.Equal(canCook ? 2 : 0, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "bread" && lot.Id != "stored-meals").Sum(lot => lot.Quantity));
        foreach (var input in new[] { "demand-flour", "demand-water", "demand-wood" })
            Assert.Equal(canCook ? 0 : inventory.GetLot(input).Quantity,
                world.Society.Inventory.Lots.Where(lot => lot.Id == input).Sum(lot => lot.Quantity));
        Assert.Equal((household, 1, house.InstanceId), (world.Society.Inventory.GetLot("demand-jug").OwnerId,
            world.Society.Inventory.GetLot("demand-jug").Quantity, world.Society.Inventory.GetLot("demand-jug").StorageBuildingId));
        if (storedMeal is not null)
        {
            var kept = world.Society.Inventory.GetLot("stored-meals");
            Assert.Equal((household, storedMeal, 4, "demand-pot", house.InstanceId),
                (kept.OwnerId, kept.ItemKind, kept.Quantity, kept.ContainerLotId, kept.StorageBuildingId));
            Assert.Equal(broken ? 0 : 10_000, world.Society.Inventory.GetLot("demand-pot").ConditionBasisPoints);
            Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation => reservation.LotId == kept.Id);
        }
        if (canCook)
        {
            var job = Assert.Single(world.WorldSimulation.ProductionJobs);
            Assert.Equal((WorldProductionJobState.Completed, household, house.InstanceId, recipe.CanonicalId),
                (job.State, job.OwnerId, job.BuildingInstanceId, job.RecipeId));
            var output = world.Society.Inventory.GetLot(job.JobId + ":output:00");
            Assert.Equal((household, house.InstanceId, "bread", 2), (output.OwnerId, output.StorageBuildingId, output.ItemKind, output.Quantity));
            Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(id).State));
        }
        else Assert.Empty(world.WorldSimulation.ProductionJobs);
        if (receipt is not null) Assert.Equal("finished", CookingOrder(world, receipt).Status);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "build_rejected");
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), id => id == actor ? new Chooser(choice, "produce_item") : new Chooser());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestaurantImprovementCountsOnlyItsUsableFinishedDish(bool broken)
    {
        var (state, actor, household, site, recipe) = Prepared("restaurant-meal", true);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "dish-pot", "storage_pot", household, 1, storageBuildingId: site.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "stored-dishes", "restaurant_meal", household, 4,
            storageBuildingId: site.InstanceId, containerLotId: "dish-pot");
        if (broken) inventory = InventoryFixture.WearSingleUnit(inventory, "dish-pot", 10_000);
        state = FarmFieldTests.WithInventory(state, inventory) with { JevEnabled = false, RoutineHelper = RoutineHelperSettings.Off };
        var choice = "build:recipe:" + recipe.CanonicalId;
        var chooser = new Chooser(choice);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? chooser : new Chooser());
        for (var tick = 0; tick < (broken ? 65 : 8) && !world.WorldSimulation.ProductionJobs.Any(job => job.State == WorldProductionJobState.Completed); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(broken, chooser.OfferedCandidates.Contains(choice));
        Assert.Equal(broken ? 2 : 0, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "restaurant_meal" && lot.Id != "stored-dishes").Sum(lot => lot.Quantity));
        Assert.Equal((household, 4, "dish-pot", site.InstanceId), (world.Society.Inventory.GetLot("stored-dishes").OwnerId,
            world.Society.Inventory.GetLot("stored-dishes").Quantity, world.Society.Inventory.GetLot("stored-dishes").ContainerLotId,
            world.Society.Inventory.GetLot("stored-dishes").StorageBuildingId));
        foreach (var input in recipe.Inputs)
            Assert.Equal(broken ? 0 : input.Amount, world.Society.Inventory.Lots.Where(lot => lot.Id == "meal-test-input-" + input.ResourceId).Sum(lot => lot.Quantity));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "build_rejected");
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), id => id == actor ? new Chooser(choice) : new Chooser());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
    }
}
