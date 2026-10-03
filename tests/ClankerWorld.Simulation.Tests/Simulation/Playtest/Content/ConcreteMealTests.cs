using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;
using System.Collections.Concurrent;

namespace ClankerWorld.Simulation.Tests;

public sealed class ConcreteMealTests
{
    [Theory]
    [InlineData("house-meal", "simple_meal", false)]
    [InlineData("wild-green-meal", "simple_meal", false)]
    [InlineData("cultivated-green-meal", "simple_meal", false)]
    [InlineData("porridge", "porridge", false)]
    [InlineData("berry-porridge", "berry_porridge", false)]
    [InlineData("fruit-porridge", "fruit_porridge", false)]
    [InlineData("bread", "bread", false)]
    [InlineData("vegetable-stew", "stew", false)]
    [InlineData("porridge", "porridge", true)]
    [InlineData("bread", "bread", true)]
    [InlineData("vegetable-stew", "stew", true)]
    [InlineData("restaurant-meal", "restaurant_meal", true)]
    public async Task NamedRecipesReserveExactInputsLeaveVesselsAndKeepOutputInPrivateStockAcrossReload(
        string localId, string output, bool restaurant)
    {
        var (state, actor, household, site, recipe) = Prepared(localId, restaurant);
        var inventory = state.Society.Society.Inventory;
        var originalInputs = recipe.Inputs.ToDictionary(input => input.ResourceId, input =>
            inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == input.ResourceId).Sum(lot => lot.Quantity));
        var foreign = state.Society.Society.Inhabitants.First(person => person.HouseholdId != household).Id;
        using var startedWorld = Restore(state);
        var rejectedBytes = PrivateWorldRuntimeCodec.Encode(startedWorld.ExportState());
        var rejected = startedWorld.StartProduction(recipe.CanonicalId, site.InstanceId, foreign);
        Assert.False(rejected.Applied);
        Assert.Contains("Only a member", rejected.Failure, StringComparison.Ordinal);
        Assert.Equal(rejectedBytes, PrivateWorldRuntimeCodec.Encode(startedWorld.ExportState()));
        var started = startedWorld.StartProduction(recipe.CanonicalId, site.InstanceId, actor);
        Assert.True(started.Applied, started.Failure);
        var job = Assert.Single(startedWorld.WorldSimulation.ProductionJobs);
        foreach (var input in recipe.Inputs)
            Assert.Equal(input.Amount, job.InputReservationIds.Select(startedWorld.Society.Inventory.GetReservation)
                .Where(reservation => startedWorld.Society.Inventory.GetLot(reservation.LotId).ItemKind == input.ResourceId)
                .Sum(reservation => reservation.Quantity));
        var inProgress = PrivateWorldRuntimeCodec.Encode(startedWorld.ExportState());
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(inProgress));
        Assert.Equal(inProgress, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        for (var tick = 0; tick < recipe.DurationTicks; tick++) Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(WorldProductionJobState.Completed, Assert.Single(resumed.WorldSimulation.ProductionJobs).State);
        foreach (var input in recipe.Inputs)
            Assert.Equal(originalInputs[input.ResourceId] - input.Amount,
                resumed.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == input.ResourceId)
                    .Sum(lot => lot.Quantity));
        var meal = Assert.Single(resumed.Society.Inventory.Lots, lot => lot.Id == started.JobId + ":output:00");
        Assert.Equal(output, meal.ItemKind);
        Assert.Equal(2, meal.Quantity);
        Assert.Equal(household, meal.OwnerId);
        Assert.Equal(site.InstanceId, meal.StorageBuildingId);
        Assert.Contains(new OwnerWorldObservationStore(resumed).GetSnapshot().PlacedBuildings
            .Single(building => building.InstanceId == site.InstanceId).StoredItems!,
            item => item.Kind == output && item.Quantity == 2);
        if (recipe.Inputs.Any(input => input.ResourceId == InventoryContainerRules.FreshWater))
        {
            Assert.Equal(site.InstanceId, resumed.Society.Inventory.GetLot("meal-test-jug").StorageBuildingId);
            Assert.DoesNotContain(resumed.Society.Inventory.Lots, lot => lot.ContainerLotId == "meal-test-jug");
        }
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(resumed.ExportState())));
        Assert.Equal(meal, reloaded.Society.Inventory.GetLot(meal.Id));
        reloaded.Validate();
    }

    [Theory]
    [InlineData("fresh_water", "fresh water")]
    [InlineData("wood", "wood")]
    public void MissingWaterOrFuelReportsTheActualIngredientAndChangesNothing(string missing, string readable)
    {
        var (state, actor, _, site, recipe) = Prepared("porridge", false);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != missing).ToArray(),
        };
        using var world = Restore(FarmFieldTests.WithInventory(state, inventory));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var failed = world.StartProduction(recipe.CanonicalId, site.InstanceId, actor);
        Assert.False(failed.Applied);
        Assert.Contains(readable, failed.Failure, StringComparison.Ordinal);
        Assert.Contains("on-site", failed.Failure, StringComparison.Ordinal);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task InterruptedCookingProjectsShowTheActualMissingWaterInTheObservation()
    {
        var (state, actor, _, site, recipe) = Prepared("porridge", false);
        var inventory = state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != InventoryContainerRules.FreshWater).ToArray() };
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Project = new SettlementProject("build:recipe:" + recipe.CanonicalId,
                    recipe.DisplayName, state.Society.Society.WorldTick, "working", 10)
                } : person).ToArray(),
        };
        using var world = Restore(state);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var blocker = world.Inhabitants.Single(person => person.InhabitantId == actor).Project!.Blocker;
        Assert.Contains("1 fresh water", blocker, StringComparison.Ordinal);
        var observation = new OwnerWorldObservationStore(world).GetSnapshot();
        var json = System.Text.Json.JsonSerializer.Serialize(observation);
        Assert.Contains("1 fresh water", json, StringComparison.Ordinal);
        for (var tick = 0; tick < 60; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(world.Inhabitants.Single(person => person.InhabitantId == actor).Project!.RequiresFreshChoice);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.True(restored.Inhabitants.Single(person => person.InhabitantId == actor).Project!.RequiresFreshChoice);
    }

    [Fact]
    public async Task AFullHouseCanCookWithItsReservedIngredientsAndKeepsCapacityAndAccounting()
    {
        var (state, actor, household, site, recipe) = Prepared("porridge", false);
        var inventory = state.Society.Society.Inventory;
        var initial = inventory.Lots.Where(lot => lot.StorageBuildingId == site.InstanceId).Sum(lot => lot.Quantity);
        inventory = InventoryFixture.AddLot(inventory, "meal-test-storage-filler", "test_stock", household,
            BuildingStorageRules.UnitsPerTile - initial, storageBuildingId: site.InstanceId);
        using var world = Restore(FarmFieldTests.WithInventory(state, inventory));
        var started = world.StartProduction(recipe.CanonicalId, site.InstanceId, actor);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < recipe.DurationTicks; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(BuildingStorageRules.UnitsPerTile - recipe.Inputs.Sum(input => input.Amount) + 2,
            world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == site.InstanceId).Sum(lot => lot.Quantity));
        Assert.Equal(1, world.Society.Inventory.GetLot("meal-test-jug").Quantity);
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        resumed.Validate();
    }

    [Theory]
    [InlineData("porridge", 4_000)]
    [InlineData("berry_porridge", 5_000)]
    [InlineData("fruit_porridge", 5_000)]
    [InlineData("stew", 5_000)]
    [InlineData("restaurant_meal", 6_000)]
    public async Task ConcreteMealsGiveNourishmentVarietyAndSavedMealNames(string mealKind, int nourishment)
    {
        var (state, actor, _, _, _) = Prepared("house-meal", false);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "personal-meal", mealKind, actor, 1);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    HungerBasisPoints = 1_000,
                    LastDecisionContext = null,
                    Survival = person.Survival! with { NutritionBasisPoints = 5_000, LastMealKind = "bread" }
                }
                : person).ToArray(),
        };
        using var world = Restore(state, actor, "consume_food");
        await AdvanceUntil(world, () => world.Inhabitants.Single(person => person.InhabitantId == actor)
            .Survival?.LastMealKind == mealKind, 20);
        var fed = world.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.InRange(fed.HungerBasisPoints, 1_000 + nourishment - 200, 1_000 + nourishment);
        Assert.True(fed.Survival!.NutritionBasisPoints > 5_000);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "personal-meal");
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(fed.Survival, resumed.Inhabitants.Single(person => person.InhabitantId == actor).Survival);
        Assert.Throws<InvalidDataException>(() => Restore(world.ExportState() with { SchemaVersion = 39 }));
    }

    [Fact]
    public async Task RawFarmStockKeepsItsFreshnessWhileBreadAndCookedMealsSpoilInPots()
    {
        var (state, actor, household, site, _) = Prepared("house-meal", false);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "grain-test-pot",
            InventoryContainerRules.StoragePot, household, 1, storageBuildingId: site.InstanceId);
        foreach (var kind in new[] { "grain", "flour", "potatoes", "bread", "stew" })
            inventory = InventoryFixture.AddLot(inventory, "decay-test-" + kind, kind, household, 1,
                storageBuildingId: site.InstanceId, containerLotId: "grain-test-pot");
        using var world = Restore(FarmFieldTests.WithInventory(state, inventory));
        for (var tick = 0; tick < 16; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        foreach (var kind in new[] { "grain", "flour", "potatoes" })
            Assert.Equal(10_000, world.Society.Inventory.GetLot("decay-test-" + kind).FreshnessBasisPoints);
        var bread = world.Society.Inventory.GetLot("decay-test-bread").FreshnessBasisPoints;
        var stew = world.Society.Inventory.GetLot("decay-test-stew").FreshnessBasisPoints;
        Assert.InRange(bread, 1, 9_999);
        Assert.InRange(stew, 1, bread - 1);
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(10_000, resumed.Society.Inventory.GetLot("decay-test-grain").FreshnessBasisPoints);
        Assert.Equal(10_000, resumed.Society.Inventory.GetLot("decay-test-flour").FreshnessBasisPoints);
        Assert.Equal(10_000, resumed.Society.Inventory.GetLot("decay-test-potatoes").FreshnessBasisPoints);
        Assert.Equal(bread, resumed.Society.Inventory.GetLot("decay-test-bread").FreshnessBasisPoints);
        Assert.Equal(stew, resumed.Society.Inventory.GetLot("decay-test-stew").FreshnessBasisPoints);
    }

    [Fact]
    public void GenericFoodConversionIsRetiredAndRestaurantHasTheAgreedFootprint()
    {
        var (state, actor, _, site, _) = Prepared("house-meal", false);
        using var world = Restore(state);
        var generic = world.WorldContent.Recipes.Single(recipe => recipe.LocalId == "meal");
        var rejected = world.StartProduction(generic.CanonicalId, site.InstanceId, actor);
        Assert.False(rejected.Applied);
        Assert.Contains("named ingredients", rejected.Failure, StringComparison.Ordinal);
        var restaurant = world.WorldContent.Buildings.Single(building => building.LocalId == "restaurant-1x2");
        Assert.Equal((1, 2), (restaurant.Width, restaurant.Height));
        var mill = world.WorldContent.Recipes.Single(recipe => recipe.LocalId == "mill-grain");
        Assert.Equal([new ContentQuantity("grain", 1)], mill.Inputs);
        Assert.Equal([new ContentQuantity("flour", 1)], mill.Outputs);
    }

    [Fact]
    public async Task ASharedJugFeedsCookingBeforeHouseAndRestaurantReplenishmentCanMoveItBackAndForth()
    {
        var (state, actor, household, restaurant, _) = Prepared("porridge", true);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.Id != "meal-test-jug" &&
                lot.ContainerLotId != "meal-test-jug").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "shared-jug", InventoryContainerRules.WaterJug,
            household, 1, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "shared-water", InventoryContainerRules.FreshWater,
            household, 4, containerLotId: "shared-jug", storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "shared-house-grain", "grain", household, 2, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "shared-house-wood", "wood", household, 2, storageBuildingId: house.InstanceId);
        var before = inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.FreshWater).Sum(lot => lot.Quantity);
        using var world = PrivateWorldRuntime.Restore(FarmFieldTests.WithInventory(state, inventory), id =>
            id == actor ? new DeterministicDecisionProvider() : new Chooser());
        await AdvanceUntil(world, () => world.WorldSimulation.ProductionJobs.Any(job =>
            job.State == WorldProductionJobState.Completed), 120);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.ItemKind == "porridge");
        Assert.True(world.Society.Inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.FreshWater)
            .Sum(lot => lot.Quantity) < before);
        Assert.Equal(1, world.Society.Inventory.GetLot("shared-jug").Quantity);
        world.Validate();
    }

    [Fact]
    public async Task ASingleLivingCookCanImproveTwoBreadIntoRestaurantMealsWithoutProvisioningTheDead()
    {
        var (state, actor, household, restaurant, _) = Prepared("porridge", true);
        var other = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household && person.Id != actor).Id;
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, other, SocietyDeathCause.Accident, checkpoint.WorldTick));
        state = state with
        {
            Society = society.ExportState(),
            Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != other).ToArray(),
            Towns = state.Towns!.Select(town => town with
            {
                ResidentIds = town.ResidentIds.Where(id => id != other).ToArray(),
                Governance = town.Governance is { } governance ? TownGovernanceRules.Advance(governance,
                    town.Id, state.WorldSeed, town.ResidentIds.Where(id => id != other &&
                        society.Checkpoint.Inhabitants.Any(person => person.Id == id &&
                            person.Status == SocietyInhabitantStatus.Active &&
                            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder))
                        .Order(StringComparer.Ordinal).ToArray(), society.Checkpoint.WorldTick,
                    state.WorldSystems!.Config.TicksPerDay) : null,
            }).ToArray()
        };
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != household ||
                lot.ItemKind != "food").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "upgrade-bread", "bread", household, 2,
            storageBuildingId: restaurant.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "upgrade-greens", "cultivated_greens", household, 1,
            storageBuildingId: restaurant.InstanceId);
        var recipe = state.WorldContent!.Recipes.Single(recipe => recipe.LocalId == "restaurant-meal");
        using var world = Restore(FarmFieldTests.WithInventory(state, inventory), actor, "build:recipe:" + recipe.CanonicalId);
        await AdvanceUntil(world, () => world.Society.Inventory.Lots.Any(lot => lot.ItemKind == "restaurant_meal"), 60);
        Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "restaurant_meal").Sum(lot => lot.Quantity));
        Assert.Equal(1, world.Society.Inventory.GetLot("upgrade-bread").Quantity);
        var stocked = world.ExportState();
        inventory = InventoryFixture.AddLot(stocked.Society.Society.Inventory, "post-upgrade-wood", "wood", household, 2,
            storageBuildingId: restaurant.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "post-upgrade-greens", "cultivated_greens", household, 1,
            storageBuildingId: restaurant.InstanceId);
        var observer = new ActionCoverageRecorder(chooseIdle: true);
        state = FarmFieldTests.WithInventory(stocked, inventory) with
        {
            Inhabitants = world.ExportState().Inhabitants.Select(person =>
            person.InhabitantId == actor ? person with { LastDecisionContext = null } : person).ToArray()
        };
        using var completed = PrivateWorldRuntime.Restore(state, id => id == actor ? observer : new Chooser());
        for (var tick = 0; tick < 6; tick++) Assert.True((await completed.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(observer.FamiliesOfferedTo(actor, completed.WorldContent));
        Assert.DoesNotContain(observer.FamiliesOfferedTo(actor, completed.WorldContent), family =>
            family is "recipe:porridge" or "recipe:restaurant-meal");
        Assert.Contains(completed.Society.Inhabitants, person => person.Id == other && person.Status != SocietyInhabitantStatus.Active);
    }

    [Fact]
    public async Task GeneratedHarvestTravelsThroughFarmStockAndHouseCookingBeforeItIsEaten()
    {
        var (state, actor, household, point) = await FarmFieldTests.ReadyFarmer("grain-harvest-to-porridge");
        var starterRations = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == household &&
            lot.ItemKind == "food").Sum(lot => lot.Quantity);
        Assert.True(starterRations > 0);
        // Start after the household has eaten its actual rations, leaving room
        // for the harvested meal's inputs and the jug's complete water load.
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, household);
        Assert.Equal(starterRations, state.Society.Society.Inventory.Reservations.Where(reservation =>
                reservation.Purpose == "household_meals" && reservation.State == InventoryReservationState.Completed)
            .Sum(reservation => reservation.Quantity));
        using var harvest = Restore(state);
        Assert.True(harvest.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
        for (var tick = 0; tick < 4; tick++) Assert.True((await harvest.AdvanceOneTickAsync()).Advanced);
        var grain = Assert.Single(harvest.Society.Inventory.Lots, lot => lot.ItemKind == "grain" && lot.GroundPosition is not null);
        Assert.Equal(household, grain.OwnerId);
        var field = Assert.Single(harvest.Fields);
        var reserve = harvest.Society.Inventory.GetReservation(field.ReplantingReservationId!);
        Assert.Equal(InventoryReservationState.Reserved, reserve.State);
        var farmhouse = harvest.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        using var hauling = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(harvest.ExportState())),
            actor, "haul_farm_grain", "haul_household_stock");
        await AdvanceUntil(hauling, () => hauling.Society.Inventory.Lots.Any(lot => lot.ItemKind == "grain" &&
            lot.StorageBuildingId == farmhouse.InstanceId), 100);
        var house = hauling.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        Assert.DoesNotContain(hauling.Society.Inventory.Lots, lot => lot.ItemKind == "wood" &&
            lot.StorageBuildingId == house.InstanceId);
        using var home = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(hauling.ExportState())),
            actor, "supply_workstation", "haul_household_stock", "collect_wooden_axe");
        await AdvanceUntil(home, () => home.Society.Inventory.Lots.Any(lot => lot.ItemKind == "grain" &&
            lot.StorageBuildingId == house.InstanceId), 100);
        var deliveredWood = home.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" &&
            lot.StorageBuildingId == house.InstanceId).Select(lot => lot.Id).ToArray();
        Assert.NotEmpty(deliveredWood);
        Assert.Contains(home.ExportState().Events, item =>
            item.Kind is "household_stock_delivered" or "workstation_supplied" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal) &&
            item.Detail.EndsWith(":" + house.InstanceId, StringComparison.Ordinal) &&
            deliveredWood.Any(id => item.Detail.Contains(id, StringComparison.Ordinal)));
        var waterState = home.ExportState();
        var inventory = InventoryFixture.AddLot(waterState.Society.Society.Inventory,
            "harvest-cooking-jug", InventoryContainerRules.WaterJug, household, 1, storageBuildingId: house.InstanceId);
        waterState = FarmFieldTests.WithInventory(waterState, inventory) with
        {
            Inhabitants = waterState.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 10_000, LastDecisionContext = null } : person).ToArray(),
        };
        var waterChoices = new Chooser("fill_water_jug", "return_water_jug", "collect_water_jug");
        using var water = PrivateWorldRuntime.Restore(waterState,
            id => id == actor ? waterChoices : new Chooser());
        await AdvanceUntil(water, () => water.ExportState().Events.Any(item => item.Kind == "water_jug_returned" &&
            item.Detail.Contains("harvest-cooking-jug", StringComparison.Ordinal)), 300, () =>
            "offers=" + string.Join(",", waterChoices.OfferedCandidates.Distinct()) + " | " +
            "position=" + water.Inhabitants.Single(person => person.InhabitantId == actor).Position +
            " | stock=" + System.Text.Json.JsonSerializer.Serialize(water.Society.Inventory.Lots.Where(lot =>
                lot.OwnerId == actor || lot.OwnerId == household)) + " | jug events=" +
            string.Join(",", water.ExportState().Events.Where(item => item.Kind.StartsWith("water_jug", StringComparison.Ordinal))
                .Select(item => item.Kind + ":" + item.Detail)));
        Assert.Contains(water.ExportState().Events, item => item.Kind == "water_jug_filled" &&
            item.Detail.Contains("harvest-cooking-jug", StringComparison.Ordinal));
        var porridge = water.WorldContent.Recipes.Single(recipe => recipe.LocalId == "porridge" &&
            recipe.WorkstationBuildingId == house.DefinitionId);
        using var cooking = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(water.ExportState())),
            actor, "build:recipe:" + porridge.CanonicalId, "supply_workstation", "haul_household_stock",
            "collect_wooden_axe");
        await AdvanceUntil(cooking, () => cooking.WorldSimulation.ProductionJobs.Any(job =>
            job.RecipeId == porridge.CanonicalId && job.State == WorldProductionJobState.Completed), 80);
        var serving = cooking.Society.Inventory.Lots.First(lot => lot.ItemKind == "porridge");
        Assert.Equal(house.InstanceId, serving.StorageBuildingId);
        Assert.Equal(InventoryReservationState.Reserved, cooking.Society.Inventory.GetReservation(reserve.Id).State);
        var eatState = cooking.ExportState();
        Assert.DoesNotContain(eatState.Society.Society.Inventory.Lots, lot =>
            (lot.OwnerId == household || lot.OwnerId == actor) && lot.ItemKind == "food");
        eatState = eatState with
        {
            Inhabitants = eatState.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 1_000, LastDecisionContext = null } : person).ToArray(),
        };
        using var eating = Restore(eatState, actor, "consume_food", "collect_shared_food");
        await AdvanceUntil(eating, () => eating.Inhabitants.Single(person => person.InhabitantId == actor)
            .Survival?.LastMealKind == "porridge", 60);
        Assert.Contains(eating.ExportState().Events, item => item.Kind == "household_food_collected" &&
            item.Detail.Contains(serving.Id, StringComparison.Ordinal));
        Assert.Equal(1, eating.Society.Inventory.Lots.Where(lot => lot.ItemKind == "porridge").Sum(lot => lot.Quantity));
        using var savedMeal = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(eating.ExportState())));
        Assert.Equal("porridge", savedMeal.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.LastMealKind);
        Assert.Equal(InventoryReservationState.Reserved, savedMeal.Society.Inventory.GetReservation(reserve.Id).State);
    }

    [Fact]
    public async Task AColdCookFuelsTheHouseWhileRestaurantWoodOnlyPaysForItsRecipe()
    {
        var (state, actor, household, restaurant, recipe) = Prepared("porridge", true);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "actual-hearth-fuel",
            "wood", actor, 2);
        state = SettlementWeatherTestFixture.WithWeather(FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position, Survival = new SurvivalCondition(3_000) }
                : person).ToArray(),
        }, ClankerWorld.Simulation.World.WeatherKind.Snow);
        string[] choices = ["tend_fire", "seek_warmth", "build:recipe:" + recipe.CanonicalId];
        using var world = Restore(state, actor, choices);
        await AdvanceUntil(world, () => world.WorldSimulation.ProductionJobs.Any(job =>
            job.RecipeId == recipe.CanonicalId && job.State == WorldProductionJobState.Completed), 90);
        // Keep the same policy after cooking so a still-cold cook must choose
        // where to recover rather than stopping while the recipe owns the turn.
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "fire_fuelled" && item.Detail == house.InstanceId);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "fire_fuelled" && item.Detail == restaurant.InstanceId);
        Assert.DoesNotContain(world.ExportState().Survival!.Fires, fire => fire.BuildingId == restaurant.InstanceId);
        Assert.Equal(1, world.Society.Inventory.GetLot("actual-hearth-fuel").Quantity);
        var job = Assert.Single(world.WorldSimulation.ProductionJobs, item => item.RecipeId == recipe.CanonicalId);
        var cookingWood = Assert.Single(job.InputReservationIds.Select(world.Society.Inventory.GetReservation),
            reservation => reservation.LotId == "meal-test-input-wood");
        Assert.Equal((1, InventoryReservationState.Completed), (cookingWood.Quantity, cookingWood.State));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "meal-test-input-wood");
        var meal = world.Society.Inventory.GetLot(job.JobId + ":output:00");
        Assert.Equal(("porridge", household, restaurant.InstanceId, 2),
            (meal.ItemKind, meal.OwnerId, meal.StorageBuildingId, meal.Quantity));
        Assert.Equal(restaurant.InstanceId, world.Society.Inventory.GetLot("meal-test-jug").StorageBuildingId);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => new Chooser(id == actor ? choices : []));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
            PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExactCookedFoodOrderSelectsItsRealServingWithoutEatingOtherOrClaimedStock(
        bool inPot, bool claimed)
    {
        var (state, actor, household, house, recipe) = Prepared("porridge", false);
        const string wrongFoodId = "a-order-other-food";
        const string potId = "exact-order-pot";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, wrongFoodId,
            "berries", household, 1, storageBuildingId: house.InstanceId);
        if (inPot)
        {
            inventory = InventoryFixture.AddLot(inventory, potId, InventoryContainerRules.StoragePot,
                household, 1, storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.PutIntoContainer(inventory, "order-store-berries", household,
                potId, wrongFoodId, 1);
        }
        state = SettlementWeatherTestFixture.WithWeather(FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 3_000, Survival = new SurvivalCondition(LastMealKind: "porridge") }
                : person).ToArray(),
        }, ClankerWorld.Simulation.World.WeatherKind.Clear);
        using var cooking = Restore(state);
        var job = cooking.StartProduction(recipe.CanonicalId, house.InstanceId, actor);
        Assert.True(job.Applied, job.Failure);
        await AdvanceUntil(cooking, () => cooking.WorldSimulation.ProductionJobs.Any(item =>
            item.JobId == job.JobId && item.State == WorldProductionJobState.Completed), 30);
        var cooked = cooking.Society.Inventory.GetLot(job.JobId + ":output:00");
        Assert.Equal(2, cooked.Quantity);
        state = cooking.ExportState();
        inventory = state.Society.Society.Inventory;
        if (inPot)
            inventory = InventoryFixture.PutIntoContainer(inventory, "order-store-porridge", household,
                potId, cooked.Id, 2);
        if (claimed)
            inventory = InventoryFixture.Reserve(inventory, "order-protected-meals", household,
                cooked.Id, 2, "independent-food-claim", long.MaxValue);
        using var world = Restore(FarmFieldTests.WithInventory(state, inventory));
        var order = world.SubmitInstruction(new OwnerInstructionRequest("eat-real-porridge", "owner:test",
            actor, OwnerInstructionKind.MustDo, "eat porridge"));
        var submitted = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(submitted));
        Assert.Equal(submitted, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        for (var tick = 0; tick < 8; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
                PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
        var finished = world.ExportState();
        var savedOrder = Assert.Single(finished.Instructions!, item => item.InstructionId == order.InstructionId).Order!;
        Assert.Equal("porridge", savedOrder.TargetFoodKind);
        Assert.Equal(claimed ? "blocked" : "finished", savedOrder.Status);
        Assert.Equal(claimed ? 0 : 1, savedOrder.CompletedUnits);
        Assert.Equal(claimed ? 2 : 1, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "porridge")
            .Sum(lot => lot.Quantity));
        Assert.Equal(1, world.Society.Inventory.GetLot(wrongFoodId).Quantity);
        if (inPot)
        {
            var pot = world.Society.Inventory.GetLot(potId);
            Assert.Equal((household, house.InstanceId, 1), (pot.OwnerId, pot.StorageBuildingId, pot.Quantity));
            Assert.Equal(potId, world.Society.Inventory.GetLot(wrongFoodId).ContainerLotId);
        }
        if (claimed)
            Assert.Equal(InventoryReservationState.Reserved,
                world.Society.Inventory.GetReservation("order-protected-meals").State);
        else
            Assert.Equal("porridge", world.Inhabitants.Single(person => person.InhabitantId == actor).Survival!.LastMealKind);
        world.Validate();
    }

    private static (PrivateWorldRuntimeState State, string Actor, string Household, PlacedBuilding Site,
        RecipeDefinition Recipe) Prepared(string localId, bool restaurant)
    {
        using var setup = NormalPathWorld.CreateGenerated("concrete-meal-" + localId, _ => new Chooser());
        var state = setup.ExportState();
        const string household = "household:camp-alpha";
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        var site = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        if (restaurant)
        {
            var definition = setup.WorldContent.Buildings.Single(building => building.LocalId == "restaurant-1x2");
            var buildingStock = InventoryFixture.AddLot(state.Society.Society.Inventory,
                "restaurant-build-wood", "wood", household, 8);
            buildingStock = InventoryFixture.AddLot(buildingStock, "restaurant-build-stone", "stone", household, 2);
            using var placementWorld = Restore(FarmFieldTests.WithInventory(state, buildingStock));
            var housePosition = site.Position;
            var placed = state.Map.Tiles.Select(tile => tile.Position)
                .Where(point => Math.Abs(point.X - housePosition.X) <= 8 &&
                    Math.Abs(point.Y - housePosition.Y) <= 8 && state.Map.IsReachableFromCampOnFoot(point))
                .OrderBy(point => state.Map.FootDistance(housePosition, point))
                .Select(point => placementWorld.PlaceBuilding("meal-test-restaurant", definition.CanonicalId, point, household))
                .First(result => result.Applied);
            site = placementWorld.WorldSimulation.Buildings.Single(building => building.InstanceId == placed.InstanceId);
            state = placementWorld.ExportState();
        }
        var recipe = setup.WorldContent.Recipes.Single(recipe => recipe.LocalId == localId &&
            recipe.WorkstationBuildingId == site.DefinitionId);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId != site.InstanceId).ToArray(),
        };
        foreach (var input in recipe.Inputs)
        {
            if (input.ResourceId == InventoryContainerRules.FreshWater)
                inventory = InventoryFixture.AddLot(inventory, "meal-test-jug", InventoryContainerRules.WaterJug,
                    household, 1, storageBuildingId: site.InstanceId);
            inventory = InventoryFixture.AddLot(inventory, "meal-test-input-" + input.ResourceId,
                input.ResourceId, household, input.Amount, storageBuildingId: site.InstanceId,
                containerLotId: input.ResourceId == InventoryContainerRules.FreshWater ? "meal-test-jug" : null);
        }
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = site.Position,
                    HungerBasisPoints = 9_000,
                    LastDecisionContext = null,
                    Survival = new SurvivalCondition()
                }
                : person).ToArray(),
        };
        return (state, actor, household, site, recipe);
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string? actor = null,
        params string[] choices)
    {
        // A new scripted phase must get a fresh observation rather than replay
        // the preceding phase's saved idle choice for an unchanged world.
        if (actor is not null)
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { LastDecisionContext = null } : person).ToArray()
            };
        return PrivateWorldRuntime.Restore(state, id => new Chooser(id == actor ? choices : []));
    }

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> done, int limit,
        Func<string>? failureContext = null)
    {
        for (var tick = 0; tick < limit && !done(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done(), string.Join(" | ", world.ExportState().Events.TakeLast(12).Select(item => item.Kind + ":" + item.Detail)) +
            " | " + failureContext?.Invoke());
    }

    private sealed class Chooser(params string[] choices) : IDecisionProvider
    {
        public ConcurrentBag<string> OfferedCandidates { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates) OfferedCandidates.Add(candidate.Id);
            var selected = choices.Select(choice => request.Observation.Candidates.FirstOrDefault(candidate =>
                    candidate.Id == choice || candidate.Id.StartsWith(choice + ":", StringComparison.Ordinal)))
                .FirstOrDefault(candidate => candidate is not null) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}
