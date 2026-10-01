using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class NamedCookingTests
{
    private static readonly string[] StarterMealIds = ["food:camp-alpha", "food:camp-beta"];

    [Theory]
    [InlineData("berries", "berry-porridge")]
    [InlineData("fruit", "fruit-porridge")]
    public async Task FruitImprovesPorridgeFullnessAndVarietyAfterItsPortionIsCarriedAndReloaded(string fruit, string recipeName)
    {
        var plain = await EatPorridge(null, "porridge");
        var enriched = await EatPorridge(fruit, recipeName);
        Assert.Equal(500, enriched.HungerBasisPoints - plain.HungerBasisPoints);
        Assert.Equal("fruit_porridge", enriched.Survival!.LastMealKind);
        Assert.Equal("porridge", plain.Survival!.LastMealKind);
        Assert.True(enriched.Survival.NutritionBasisPoints > plain.Survival.NutritionBasisPoints);
    }

    private static async Task<PlaytestInhabitantState> EatPorridge(string? fruit, string recipeName)
    {
        using var seed = NormalPathWorld.CreateGenerated("cooking-flow", _ => new Choices());
        var (state, actor, house) = AtHouse(seed);
        var inventory = Add(state.Society.Society.Inventory, "porridge-grain", "grain", house, 1);
        inventory = Add(inventory, "porridge-fuel", "wood", house, 1);
        inventory = AddJug(inventory, house, 1);
        if (fruit is not null) inventory = Add(inventory, "porridge-fruit", fruit, house, 1);
        using var cooking = Load(WithInventory(state, inventory));
        var recipe = cooking.WorldContent.Recipes.Single(item => item.LocalId == recipeName && item.Tags.Contains("house-cooking"));
        var started = cooking.StartProduction(recipe.CanonicalId, house.InstanceId, actor);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < 16; tick++) Assert.True((await cooking.AdvanceOneTickAsync()).Advanced);
        var food = cooking.Society.Inventory.GetLot(started.JobId + ":output:00");
        Assert.Equal("porridge", food.ItemKind);
        Assert.Equal(2, food.Quantity);
        inventory = InventoryFixture.Transfer(cooking.Society.Inventory, "porridge-portion", house.HouseholdId!, actor, food.Id, 1, "collect");
        state = WithInventory(cooking.ExportState(), inventory);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                HungerBasisPoints = 1_000,
                Survival = person.Survival! with { LastMealKind = "porridge", NutritionBasisPoints = 7_000 },
            } : person).ToArray()
        };
        using var eater = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? new Choices("consume_food") : new Choices());
        for (var tick = 0; tick < 8 && !eater.ExportState().Events.Any(item => item.Kind == "food_consumed" && item.Detail == actor); tick++)
            Assert.True((await eater.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(eater.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == actor);
        using var after = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(eater.ExportState())));
        return after.Inhabitants.Single(person => person.InhabitantId == actor);
    }

    [Fact]
    public async Task BreadReservesActualHouseIngredientsAndWaterKeepsItsJugAcrossReplay()
    {
        using var seed = NormalPathWorld.CreateGenerated("cooking-flow", _ => new Choices());
        var (state, actor, house) = AtHouse(seed);
        var inventory = state.Society.Society.Inventory;
        inventory = Add(inventory, "bread-flour", "flour", house, 2);
        inventory = Add(inventory, "bread-wood", "wood", house, 1);
        inventory = AddJug(inventory, house, 2);
        using var world = Load(WithInventory(state, inventory));
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "bread" && item.Tags.Contains("house-cooking"));
        var foreign = world.Society.Inhabitants.First(person => person.HouseholdId != house.HouseholdId).Id;
        Assert.False(world.StartProduction(recipe.CanonicalId, house.InstanceId, foreign).Applied);
        var started = world.StartProduction(recipe.CanonicalId, house.InstanceId, actor);
        Assert.True(started.Applied, started.Failure);
        var job = world.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId);
        var waterReservation = job.InputReservationIds.Select(world.Society.Inventory.GetReservation)
            .Single(item => item.LotId == "cooking-water");
        Assert.Equal(1, waterReservation.Quantity);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(world.Society.Inventory,
            "reserved-jug", house.HouseholdId!, actor, "cooking-jug", 1, "move"));
        using var replay = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 24; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(WorldProductionJobState.Completed, world.WorldSimulation.ProductionJobs.Single(item => item.JobId == job.JobId).State);
        var bread = world.Society.Inventory.GetLot(job.JobId + ":output:00");
        Assert.Equal("bread", bread.ItemKind);
        Assert.Equal(2, bread.Quantity);
        Assert.Equal(house.HouseholdId, bread.OwnerId);
        Assert.Equal(house.InstanceId, bread.StorageBuildingId);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "bread-flour");
        Assert.Equal(1, world.Society.Inventory.GetLot("cooking-water").Quantity);
        Assert.Equal("cooking-jug", world.Society.Inventory.GetLot("cooking-water").ContainerLotId);
        Assert.Equal(1, world.Society.Inventory.GetLot("cooking-jug").Quantity);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(id).State));
    }

    [Fact]
    public void MissingWaterFuelAndPlantingStockGiveSpecificBlockersWithoutReservingOtherIngredients()
    {
        using var seed = NormalPathWorld.CreateGenerated("cooking-flow", _ => new Choices());
        var (state, actor, house) = AtHouse(seed);
        var inventory = Add(state.Society.Society.Inventory, "trial-grain", "grain", house, 1);
        inventory = Add(inventory, "trial-fuel", "wood", house, 1);
        using (var dry = Load(WithInventory(state, inventory)))
        {
            var porridge = dry.WorldContent.Recipes.Single(item => item.LocalId == "porridge" && item.Tags.Contains("house-cooking"));
            var before = PrivateWorldRuntimeCodec.Encode(dry.ExportState());
            var missing = dry.StartProduction(porridge.CanonicalId, house.InstanceId, actor);
            Assert.False(missing.Applied);
            Assert.Contains("water", missing.Failure);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(dry.ExportState()));
        }
        inventory = AddJug(inventory, house, 1);
        inventory = InventoryFixture.Reserve(inventory, "use-fuel", house.HouseholdId!, "trial-fuel", 1, "test", 100);
        inventory = InventoryFixture.ConsumeReservation(inventory, "use-fuel");
        // Remove all remaining household wood so remote or personally carried fuel cannot satisfy on-site work.
        foreach (var fuel in inventory.Lots.Where(lot => lot.ItemKind == "wood" && lot.OwnerId == house.HouseholdId).ToArray())
        {
            inventory = InventoryFixture.Reserve(inventory, "spent-fuel:" + fuel.Id, house.HouseholdId!, fuel.Id, fuel.Quantity, "earlier-fire", 100);
            inventory = InventoryFixture.ConsumeReservation(inventory, "spent-fuel:" + fuel.Id);
        }
        using (var unfuelled = Load(WithInventory(state, inventory)))
        {
            var porridge = unfuelled.WorldContent.Recipes.Single(item => item.LocalId == "porridge" && item.Tags.Contains("house-cooking"));
            var rejected = unfuelled.StartProduction(porridge.CanonicalId, house.InstanceId, actor);
            Assert.False(rejected.Applied);
            Assert.Contains("wood", rejected.Failure);
        }
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.ItemKind != "potatoes" || lot.OwnerId != house.HouseholdId).ToArray() };
        inventory = Add(inventory, "last-potatoes", "potatoes", house, 2);
        inventory = Add(inventory, "replacement-fuel", "wood", house, 1);
        using var planting = Load(WithInventory(state, inventory));
        var meal = planting.WorldContent.Recipes.Single(item => item.LocalId == "house-meal");
        var reserve = planting.StartProduction(meal.CanonicalId, house.InstanceId, actor);
        Assert.False(reserve.Applied);
        Assert.Contains("replant", reserve.Failure);
        Assert.Equal(2, planting.Society.Inventory.GetLot("last-potatoes").Quantity);
    }

    [Fact]
    public async Task WorkstationSupplyWaitsForRoomAndDeliversOnlyWhatActuallyFits()
    {
        using var seed = NormalPathWorld.CreateGenerated("cooking-flow", _ => new Choices());
        var (state, actor, house) = AtHouse(seed);
        var inventory = UseStarterRations(state.Society.Society.Inventory, house.HouseholdId!);
        inventory = InventoryFixture.AddLot(inventory, "carried-greens", "wild_greens", actor, 3);
        var stored = inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        inventory = Add(inventory, "house-filler", "stone", house, 64 - stored);
        var choices = new Choices("supply_workstation:wild_greens");
        using var full = Load(WithInventory(state, inventory), id => id == actor ? choices : new Choices());
        for (var tick = 0; tick < 8; tick++) Assert.True((await full.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("supply_workstation:wild_greens", choices.Offered);
        Assert.Equal(3, full.Society.Inventory.GetLot("carried-greens").Quantity);
        state = full.ExportState();
        inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "make-room", house.HouseholdId!, "house-filler", 1, "test", 100);
        inventory = InventoryFixture.ConsumeReservation(inventory, "make-room");
        choices = new Choices("supply_workstation:wild_greens");
        using var room = Load(WithInventory(state, inventory), id => id == actor ? choices : new Choices());
        for (var tick = 0; tick < 8 && room.Society.Inventory.GetLot("carried-greens").Quantity == 3; tick++)
            Assert.True((await room.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, room.Society.Inventory.GetLot("carried-greens").Quantity);
        Assert.Equal(64, room.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
        Assert.Contains(room.Society.Inventory.Lots, lot => lot.ItemKind == "wild_greens" && lot.Quantity == 1 && lot.StorageBuildingId == house.InstanceId);
    }

    [Fact]
    public async Task RestaurantWaterMovesAsAWholeJugAndWaitsUntilAllContentsFit()
    {
        using var seed = NormalPathWorld.CreateGenerated("cooking-flow", _ => new Choices());
        var (state, actor, house) = AtHouse(seed);
        var definition = seed.WorldContent.Buildings.Single(item => item.Tags.Contains("restaurant"));
        var site = seed.Towns.Single().BorderTiles.First(point => WorldContentSimulationRules.Footprint(definition, point)
            .All(tile => state.Map.IsBuildable(tile) && !seed.RoadTiles.Contains(tile) &&
                !state.Inhabitants.Any(person => person.Position == tile) &&
                !state.Map.Resources.Any(resource => resource.Position == tile) &&
                !seed.WorldSimulation.Buildings.Any(building => WorldContentSimulationRules.Footprint(
                    seed.WorldContent.Buildings.Single(value => value.CanonicalId == building.DefinitionId), building.Position).Contains(tile))));
        var funded = InventoryFixture.AddLot(state.Society.Society.Inventory, "restaurant-building-stone", "stone", house.HouseholdId!, 4);
        using var builder = Load(WithInventory(state, funded));
        var placed = builder.PlaceBuilding("cooking-restaurant", definition.CanonicalId, site, house.HouseholdId);
        Assert.True(placed.Applied, placed.Failure);
        state = MoveActor(builder.ExportState(), actor, house.Position);
        var restaurant = builder.WorldSimulation.Buildings.Single(item => item.InstanceId == "cooking-restaurant");
        var inventory = AddJug(UseStarterRations(state.Society.Society.Inventory, house.HouseholdId!), house, 8);
        inventory = Add(inventory, "restaurant-grain", "grain", restaurant, 1);
        inventory = Add(inventory, "restaurant-fuel", "wood", restaurant, 1);
        inventory = Add(inventory, "restaurant-filler", "stone", restaurant, 246); // Eight free spaces, but a full jug weighs nine.
        var choices = new Choices("supply_workstation:water");
        using var full = Load(WithInventory(state, inventory), id => id == actor ? choices : new Choices());
        for (var tick = 0; tick < 8; tick++) Assert.True((await full.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("supply_workstation:water", choices.Offered);
        Assert.Equal(house.InstanceId, full.Society.Inventory.GetLot("cooking-jug").StorageBuildingId);
        state = full.ExportState();
        inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "jug-room", house.HouseholdId!, "restaurant-filler", 1, "test", 100);
        inventory = InventoryFixture.ConsumeReservation(inventory, "jug-room");
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { LastDecisionContext = null } : person).ToArray() };
        using var pickup = Load(WithInventory(state, inventory), id => id == actor ? new Choices("supply_workstation:water") : new Choices());
        for (var tick = 0; tick < 8 && pickup.Society.Inventory.GetLot("cooking-jug").OwnerId != actor; tick++)
            Assert.True((await pickup.AdvanceOneTickAsync()).Advanced);
        Assert.True(pickup.Society.Inventory.GetLot("cooking-jug").OwnerId == actor,
            $"actor={pickup.Inhabitants.Single(person => person.InhabitantId == actor)}; restaurant stored=" +
            pickup.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == restaurant.InstanceId).Sum(lot => lot.Quantity) +
            "; events=" + string.Join(";", pickup.ExportState().Events.TakeLast(12)));
        Assert.Equal(actor, pickup.Society.Inventory.GetLot("cooking-water").OwnerId);
        Assert.Equal(restaurant.InstanceId, pickup.Society.Inventory.GetLot("cooking-water").DeliveryBuildingId);
        Assert.Equal("cooking-jug", pickup.Society.Inventory.GetLot("cooking-water").ContainerLotId);
        state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(pickup.ExportState()));
        using var delivery = Load(MoveActor(state, actor, restaurant.Position), id => id == actor ? new Choices("haul_household_stock") : new Choices());
        for (var tick = 0; tick < 8 && delivery.Society.Inventory.GetLot("cooking-jug").OwnerId == actor; tick++)
            Assert.True((await delivery.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(restaurant.HouseholdId, delivery.Society.Inventory.GetLot("cooking-jug").OwnerId);
        Assert.Equal(restaurant.InstanceId, delivery.Society.Inventory.GetLot("cooking-water").StorageBuildingId);
        Assert.Equal(8, delivery.Society.Inventory.GetLot("cooking-water").Quantity);
        var porridge = delivery.WorldContent.Recipes.Single(item => item.LocalId == "porridge" && item.Tags.Contains("restaurant-cooking"));
        var cooked = delivery.StartProduction(porridge.CanonicalId, restaurant.InstanceId, actor);
        Assert.True(cooked.Applied, cooked.Failure);
        for (var tick = 0; tick < 16; tick++) Assert.True((await delivery.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(7, delivery.Society.Inventory.GetLot("cooking-water").Quantity);
        Assert.Equal(restaurant.InstanceId, delivery.Society.Inventory.GetLot(cooked.JobId + ":output:00").StorageBuildingId);
        delivery.Validate();
    }

    [Fact]
    public async Task GeneratedHarvestIsCarriedHomeCookedAndEatenThroughOrdinaryActions()
    {
        var choices = new Choices(normalCooking: true);
        using var initial = NormalPathWorld.CreateGenerated("probe-a", _ => choices);
        Assert.All(initial.Society.Inventory.Lots.Where(lot => lot.Id is "food:camp-alpha" or "food:camp-beta"), lot => Assert.Equal(8, lot.Quantity));
        var state = initial.ExportState();
        var world = Load(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 1_000, LastDecisionContext = null }).ToArray(),
        }, _ => new Choices("consume_food", "collect_shared_food"));
        try
        {
            // Repeated appetite makes the eight real starter meals per House
            // run out promptly; each serving is still collected and eaten.
            for (var tick = 0; tick < 160 && StarterServingsEaten(world) < 16; tick++)
            {
                var previousTick = world.WorldTick;
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                var eatenBy = world.ExportState().Events.Where(item => item.Kind == "food_consumed" && item.WorldTick > previousTick)
                    .Select(item => item.Detail).ToHashSet(StringComparer.Ordinal);
                if (eatenBy.Count == 0) continue;
                state = world.ExportState();
                world.Dispose();
                world = Load(state with
                {
                    Inhabitants = state.Inhabitants.Select(person => eatenBy.Contains(person.InhabitantId) ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
                }, _ => new Choices("consume_food", "collect_shared_food"));
            }
            Assert.Equal(16, StarterServingsEaten(world));
            state = world.ExportState();
        }
        finally { world.Dispose(); }
        using var growing = Load(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 5_000, LastDecisionContext = null }).ToArray(),
        }, _ => choices);
        world = growing;
        choices.Recipes.UnionWith(world.WorldContent.Recipes.Where(recipe => HouseCookingContent.IsMealRecipe(recipe) ||
            recipe.LocalId == "mill-grain" || recipe.Tags.Contains("pottery") || recipe.Outputs.Any(output => ToolCapabilities.ForItem(output.ResourceId) is not null))
            .Select(recipe => "build:recipe:" + recipe.CanonicalId));
        for (var tick = 0; tick < 1_000 && !HasEatenCookedMeal(world); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var events = world.ExportState().Events;
        Assert.Contains(events, item => item.Kind == "field_harvested");
        Assert.Contains(events, item => item.Kind == "field_harvest_collected");
        Assert.Contains(events, item => item.Kind == "household_stock_delivered");
        Assert.Contains(world.WorldSimulation.ProductionJobs, job => job.State == WorldProductionJobState.Completed &&
            world.WorldContent.Recipes.Any(recipe => recipe.CanonicalId == job.RecipeId && recipe.Outputs.Any(output => output.ResourceId == "simple_meal")));
        Assert.True(HasEatenCookedMeal(world));
        Assert.Contains(StarterMealIds, id =>
            world.Society.Inventory.Lots.FirstOrDefault(lot => lot.Id == id)?.Quantity is null or < 8);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.StorageBuildingId == "first-town-warehouse" && FoodItems.IsEdible(lot.ItemKind));
        using var restored = Load(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        restored.Validate();
    }

    private static long StarterServingsEaten(PrivateWorldRuntime world) => world.Society.Inventory.Reservations.Where(reservation =>
        reservation.State == InventoryReservationState.Completed && reservation.Purpose == "direct_consumption" &&
        StarterMealIds.Any(id => reservation.LotId == id || reservation.LotId.StartsWith(id + "#transfer:", StringComparison.Ordinal)))
        .Sum(reservation => (long)reservation.Quantity);

    private static bool HasEatenCookedMeal(PrivateWorldRuntime world)
    {
        var harvestIds = (world.WorldSimulation.CropBuilds ?? []).Where(job => job.State == WorldProductionJobState.Completed &&
            world.WorldContent.Recipes.Any(recipe => recipe.CanonicalId == job.RecipeId && recipe.IsCrop &&
                recipe.Outputs.Any(output => output.ResourceId is "potatoes" or "cultivated_greens")))
            .Select(job => job.JobId + ":output:00").ToHashSet(StringComparer.Ordinal);
        var cookedIds = world.WorldSimulation.ProductionJobs.Where(job => job.State == WorldProductionJobState.Completed &&
            world.WorldContent.Recipes.Any(recipe => recipe.CanonicalId == job.RecipeId &&
                recipe.Tags.Contains("house-cooking") && recipe.Outputs.Any(output => output.ResourceId == "simple_meal")) &&
            job.InputReservationIds.Select(world.Society.Inventory.GetReservation).Any(reservation =>
                harvestIds.Any(id => reservation.LotId == id || reservation.LotId.StartsWith(id + "#transfer:", StringComparison.Ordinal))))
            .Select(job => job.JobId + ":output:00").ToHashSet(StringComparer.Ordinal);
        return world.Society.Inventory.Reservations.Any(reservation => reservation.State == InventoryReservationState.Completed &&
            reservation.Purpose == "direct_consumption" && world.Society.Inhabitants.Any(person => person.Id == reservation.OwnerId) &&
            cookedIds.Any(id => reservation.LotId == id || reservation.LotId.StartsWith(id + "#transfer:", StringComparison.Ordinal)));
    }

    private static InventoryCheckpoint UseStarterRations(InventoryCheckpoint inventory, string householdId)
    {
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == householdId && lot.ItemKind == "simple_meal" && lot.Quantity > 0).ToArray())
        {
            inventory = InventoryFixture.Reserve(inventory, "prior-meals:" + lot.Id, householdId, lot.Id, lot.Quantity, "earlier-meals", 100);
            inventory = InventoryFixture.ConsumeReservation(inventory, "prior-meals:" + lot.Id);
        }
        return inventory;
    }

    private static (PrivateWorldRuntimeState State, string Actor, PlacedBuilding House) AtHouse(PrivateWorldRuntime seed)
    {
        var house = seed.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var actor = seed.Society.Inhabitants.First(person => person.HouseholdId == house.HouseholdId).Id;
        return (MoveActor(seed.ExportState(), actor, house.Position), actor, house);
    }

    private static InventoryCheckpoint Add(InventoryCheckpoint inventory, string id, string kind, PlacedBuilding building, int amount) =>
        InventoryFixture.AddLot(inventory, id, kind, building.HouseholdId!, amount, storageBuildingId: building.InstanceId);

    private static InventoryCheckpoint AddJug(InventoryCheckpoint inventory, PlacedBuilding building, int water)
    {
        inventory = InventoryFixture.AddLot(inventory, "cooking-jug", "water_jug", building.HouseholdId!, 1,
            storageBuildingId: building.InstanceId, containerCapacity: 8);
        return InventoryFixture.AddLot(inventory, "cooking-water", "water", building.HouseholdId!, water,
            storageBuildingId: building.InstanceId, containerLotId: "cooking-jug");
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntimeState MoveActor(PrivateWorldRuntimeState state, string actor, GridPoint position) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = position, HungerBasisPoints = 9_000 }
            : person.Position == position ? person with { Position = state.Inhabitants.Single(item => item.InhabitantId == actor).Position } : person).ToArray(),
    };

    private static PrivateWorldRuntime Load(PrivateWorldRuntimeState state, Func<string, IDecisionProvider>? factory = null) =>
        PrivateWorldRuntime.Restore(state, factory ?? (_ => new Choices()));

    private sealed class Choices(string first = "safe_idle", string second = "safe_idle", bool normalCooking = false) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public HashSet<string> Recipes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered.UnionWith(request.Observation.Candidates.Select(candidate => candidate.Id));
            var selected = request.Observation.Candidates.Where(candidate => candidate.Id == first || candidate.Id == second || normalCooking &&
                    (Recipes.Contains(candidate.Id) || candidate.Id.StartsWith("farm:", StringComparison.Ordinal) ||
                     candidate.Id.StartsWith("supply_workstation:", StringComparison.Ordinal) || candidate.Id.StartsWith("craft_tool:", StringComparison.Ordinal) ||
                     candidate.Id is "consume_food" or "collect_shared_food" or "seek_food" or "harvest_food" or "haul_household_stock" or
                         "haul_farm_grain" or "haul_farm_flour" or "pottery_supply" or "water_collect_jug" or "water_fill" or "water_deliver" or "repair_tool"))
                .OrderBy(candidate => candidate.DeterministicPriority).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).FirstOrDefault()
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}
