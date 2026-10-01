using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PersonalEquipmentTests
{
    private const string Alpha = "household:camp-alpha";

    [Fact]
    public void CapacityCountsDeliveredCargoOnceAndDoesNotUseAStoredOrBrokenSack()
    {
        var inventory = InventoryFixture.CreateGenesis([
            new("sack", "sack", "person", 1, 10_000, 10_000, 0), new("coat", "padded_coat", "person", 1, 10_000, 10_000, 0),
            new("wood", "wood", "person", 8, 10_000, 10_000, 0, DeliveryBuildingId: "house"),
            new("pot", "jug", "person", 1, 10_000, 10_000, 0), new("water", "water", "person", 3, 10_000, 10_000, 0),
            new("ground-stock", "grain", "person", 30, 10_000, 10_000, 0, GroundPosition: new(2, 3)),
        ]);
        var equipment = new PersonalEquipment("coat", "sack");
        Assert.Equal(12, PersonalEquipmentRules.CarriedQuantity(inventory, "person", equipment));
        Assert.Equal(12, PersonalEquipmentRules.FreeCapacity(inventory, "person", equipment));
        var broken = InventoryFixture.WearSingleUnit(inventory, "sack", 10_000);
        Assert.Equal(8, PersonalEquipmentRules.Capacity(broken, "person", equipment));
        Assert.Equal(12, PersonalEquipmentRules.CarriedQuantity(broken, "person", equipment));
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(broken, "person", equipment));
        var stored = InventoryFixture.Transfer(inventory, "store", "person", Alpha, "sack", 1, "store", "house");
        Assert.Equal(8, PersonalEquipmentRules.Capacity(stored, "person", equipment));
        Assert.Equal(12, PersonalEquipmentRules.CarriedQuantity(stored, "person", equipment));
    }

    [Fact]
    public void GroundContainerFamiliesDoNotCountUntilTheWholeVesselIsPickedUpAndSaved()
    {
        using var seed = new PrivateWorldRuntime("ground-container-family-carry");
        var state = seed.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha &&
            person.Status == SocietyInhabitantStatus.Active).Id;
        var personState = state.Inhabitants.Single(person => person.InhabitantId == actor);
        var ground = new InventoryGroundPosition(personState.Position.X, personState.Position.Y);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray(),
            Reservations = state.Society.Society.Inventory.Reservations.Where(item => item.OwnerId != actor).ToArray(),
            Offers = state.Society.Society.Inventory.Offers.Where(item =>
                item.FirstPartyId != actor && item.SecondPartyId != actor).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "ground-actor-pot", InventoryContainerRules.StoragePot,
            actor, 1, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "ground-actor-pot-food", "berries", actor,
            InventoryContainerRules.StoragePotCapacity, containerLotId: "ground-actor-pot");
        inventory = InventoryFixture.AddLot(inventory, "ground-actor-jug", InventoryContainerRules.WaterJug,
            actor, 1, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "ground-actor-jug-water", InventoryContainerRules.FreshWater,
            actor, InventoryContainerRules.WaterJugCapacity, containerLotId: "ground-actor-jug");
        Assert.Equal(0, PersonalEquipmentRules.CarriedQuantity(inventory, actor, null));
        inventory = InventoryFixture.AddLot(inventory, "ground-household-jug", InventoryContainerRules.WaterJug,
            Alpha, 1, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "ground-household-jug-water", InventoryContainerRules.FreshWater,
            Alpha, InventoryContainerRules.WaterJugCapacity, containerLotId: "ground-household-jug");
        Assert.Equal(0, PersonalEquipmentRules.CarriedQuantity(inventory, actor, null));
        inventory = InventoryFixture.AddLot(inventory, "loose-carried-wood", "wood", actor, 2);
        Assert.Equal(2, PersonalEquipmentRules.CarriedQuantity(inventory, actor, null));
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = null } : person).ToArray(),
        };

        using var loaded = PrivateWorldRuntime.Restore(state, _ => new Choices([]));
        var groundRoundTrip = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        var groundInventory = groundRoundTrip.Society.Society.Inventory;
        Assert.Equal(ground, groundInventory.GetLot("ground-actor-pot").GroundPosition);
        Assert.Equal(ground, groundInventory.GetLot("ground-actor-jug").GroundPosition);
        Assert.Null(groundInventory.GetLot("ground-actor-pot-food").GroundPosition);
        Assert.Equal(2, PersonalEquipmentRules.CarriedQuantity(groundInventory, actor, null));
        Assert.Equal(6, PersonalEquipmentRules.FreeCapacity(groundInventory, actor, null));

        var pickedUp = InventoryFixture.Transfer(groundInventory, "pick-up-ground-jug", Alpha, actor,
            "ground-household-jug", 1, "water_jug_collected");
        using var carrying = PrivateWorldRuntime.Restore(WithInventory(groundRoundTrip, pickedUp),
            _ => new Choices([]));
        var carriedRoundTrip = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(carrying.ExportState()));
        var carriedInventory = carriedRoundTrip.Society.Society.Inventory;
        Assert.Equal(ground, carriedInventory.GetLot("ground-actor-pot").GroundPosition);
        Assert.Equal(2, carriedInventory.GetLot("loose-carried-wood").Quantity);
        Assert.Equal(actor, carriedInventory.GetLot("ground-household-jug").OwnerId);
        Assert.Null(carriedInventory.GetLot("ground-household-jug").GroundPosition);
        Assert.Equal("ground-household-jug", carriedInventory.GetLot("ground-household-jug-water").ContainerLotId);
        Assert.Equal(7, PersonalEquipmentRules.CarriedQuantity(carriedInventory, actor, null));
        Assert.Equal(1, PersonalEquipmentRules.FreeCapacity(carriedInventory, actor, null));
        carrying.Validate();
    }

    [Fact]
    public void CoatsAndCloaksProtectAgainstTheirWeatherAndWearReducesProtection()
    {
        var basic = new InventoryLot("basic", "clothing", "person", 1, 10_000, 10_000, 0);
        var coat = new InventoryLot("coat", "padded_coat", "person", 1, 10_000, 10_000, 0);
        var cloak = new InventoryLot("cloak", "rain_cloak", "person", 1, 10_000, 10_000, 0);
        Assert.True(PersonalEquipmentRules.Protection(coat, WeatherKind.Snow) > PersonalEquipmentRules.Protection(basic, WeatherKind.Snow));
        Assert.True(PersonalEquipmentRules.Protection(cloak, WeatherKind.Storm) > PersonalEquipmentRules.Protection(basic, WeatherKind.Storm));
        Assert.True(PersonalEquipmentRules.Protection(coat, WeatherKind.Snow) > PersonalEquipmentRules.Protection(coat with { ConditionBasisPoints = 2_000 }, WeatherKind.Snow));
        Assert.Equal(0, PersonalEquipmentRules.Protection(cloak with { ConditionBasisPoints = 0 }, WeatherKind.Rain));
    }

    [Fact]
    public async Task FiberBecomesRopeAndAnEquippedBasketThroughHouseWorkAcrossReload()
    {
        using var generated = NormalPathWorld.CreateGenerated("equipment-basket-loop", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == Alpha).Id;
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "basket-fiber", "fiber", Alpha, 6, storageBuildingId: house.InstanceId);
        state = WithInventory(state, inventory);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == actor
            ? item with { HungerBasisPoints = 9_000 } : item).ToArray()
        };
        var rope = state.WorldContent!.Recipes.Single(item => item.LocalId == "twist-rope");
        var basket = state.WorldContent.Recipes.Single(item => item.LocalId == "weave-basket");
        string[] choices = ["equip_carry_aid", "build:recipe:" + basket.CanonicalId, "build:recipe:" + rope.CanonicalId];
        var actorProvider = new Choices(choices);
        IDecisionProvider Provider(string id) => id == actor ? actorProvider : new Choices([]);
        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reopened = false;
            for (var tick = 0; tick < 300 && world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.CarryAidLotId is null; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reopened && world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == basket.CanonicalId && job.State == WorldProductionJobState.Running))
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reopened = true;
                }
            }
            Assert.True(reopened, "Jobs: " + string.Join(", ", world.WorldSimulation.ProductionJobs.Select(job => job.RecipeId + ":" + job.State)) +
                " Offers: " + string.Join("; ", actorProvider.Offers.TakeLast(4)));
            var person = world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor);
            var equipped = world.Society.Inventory.GetLot(Assert.IsType<string>(person.Equipment?.CarryAidLotId));
            Assert.Equal(("basket", actor, 1), (equipped.ItemKind, equipped.OwnerId, equipped.Quantity));
            Assert.Null(equipped.StorageBuildingId);
            Assert.Equal(16, PersonalEquipmentRules.Capacity(world.Society.Inventory, actor, person.Equipment));
            Assert.Equal(0, world.Society.Inventory.Lots.Where(lot => lot.Id == "basket-fiber").Sum(lot => lot.Quantity));
            Assert.Equal(2, world.WorldSimulation.ProductionJobs.Count(job => job.State == WorldProductionJobState.Completed));
            world.Validate();
        }
        finally { world.Dispose(); }
    }

    [Theory]
    [InlineData("sew-clothing", "clothing", 6, 2, WeatherKind.Snow)]
    [InlineData("sew-padded-coat", "padded_coat", 12, 4, WeatherKind.Snow)]
    [InlineData("sew-rain-cloak", "rain_cloak", 7, 2, WeatherKind.Rain)]
    [InlineData("sew-sack", "sack", 6, 2, WeatherKind.Clear)]
    public async Task HouseholdSuppliesWeavesSewsAndEquipsTailorGoodsAcrossReload(string recipeId,
        string kind, int fiber, int clothCost, WeatherKind weather)
    {
        var (state, shopId) = TailorTestWorld.Create("equipment-tailor-" + kind, fiber);
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == Alpha).Id;
        // Exhaust the starting garments so the selected garment must come
        // from the actual Tailor production chain below.
        var inventory = state.Society.Society.Inventory;
        state = WithInventory(state, inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.OwnerId == Alpha && PersonalEquipmentRules.IsGarment(lot.ItemKind)
                ? lot with { ConditionBasisPoints = 0 } : lot).ToArray(),
        });
        if (kind == "sack")
            state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
                "sack-rope", "rope", Alpha, 1, storageBuildingId: "first-town-house-a"));
        var systems = state.WorldSystems!;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 9_000 } : person).ToArray(),
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>()
                    .Select(season => new WeatherProfile(season,
                        weather == WeatherKind.Clear ? 1 : 0, 0, weather == WeatherKind.Rain ? 1 : 0, 0,
                        weather == WeatherKind.Snow ? 1 : 0)).ToArray()
                },
                Climate = systems.Climate with { Weather = weather },
            },
        };
        var target = state.WorldContent!.Recipes.Single(item => item.LocalId == recipeId);
        var weave = state.WorldContent.Recipes.Single(item => item.LocalId == "weave-cloth");
        string[] choices = ["wear_clothing", "equip_carry_aid", "build:recipe:" + target.CanonicalId,
            "haul_household_stock", "supply_workstation:rope", "build:recipe:" + weave.CanonicalId, "supply_workstation:fiber",
            "seek_warmth", "tend_fire", "consume_food", "collect_shared_food"];
        var actorChoices = new Choices(choices);
        IDecisionProvider Provider(string id) => id == actor ? actorChoices : new Choices([]);
        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reopened = false;
            string? selected = null;
            WorldProductionJob? sewn = null;
            for (var tick = 0; tick < 300; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reopened && world.WorldSimulation.ProductionJobs.Any(job =>
                        job.RecipeId == target.CanonicalId && job.State == WorldProductionJobState.Running))
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reopened = true;
                }
                sewn = world.WorldSimulation.ProductionJobs.SingleOrDefault(job =>
                    job.RecipeId == target.CanonicalId && job.State == WorldProductionJobState.Completed);
                var equipment = world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment;
                selected = kind == "sack" ? equipment?.CarryAidLotId : equipment?.ClothingLotId;
                if (sewn is not null && selected == sewn.JobId + ":output:00") break;
            }
            Assert.True(reopened, "Jobs: " + string.Join(",", world.WorldSimulation.ProductionJobs.Select(job => job.RecipeId + ":" + job.State)) +
                " Project: " + world.Inhabitants.Single(person => person.InhabitantId == actor).Project +
                " Stock: " + string.Join(",", world.Society.Inventory.Lots.Where(lot => lot.OwnerId == Alpha || lot.OwnerId == actor)
                    .Select(lot => $"{lot.ItemKind}:{lot.Quantity}@{lot.StorageBuildingId}")) +
                " Offers: " + string.Join(";", actorChoices.Offers.TakeLast(3)));
            Assert.NotNull(sewn);
            Assert.Equal(sewn.JobId + ":output:00", selected);
            var unit = world.Society.Inventory.GetLot(selected!);
            Assert.Equal((kind, actor, 1), (unit.ItemKind, unit.OwnerId, unit.Quantity));
            Assert.Null(unit.StorageBuildingId);
            Assert.Equal(clothCost, world.WorldSimulation.ProductionJobs.Count(job =>
                job.RecipeId == weave.CanonicalId && job.State == WorldProductionJobState.Completed));
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot =>
                lot.OwnerId == Alpha && lot.ItemKind == "cloth" && lot.Quantity > 0);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up");
            if (kind == "sack") Assert.Equal(0, world.Society.Inventory.Lots.Where(lot => lot.Id == "sack-rope").Sum(lot => lot.Quantity));
            Assert.All(world.WorldSimulation.ProductionJobs, job => Assert.Equal(shopId, job.BuildingInstanceId));
            using var savedWorld = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
                PrivateWorldRuntimeCodec.Encode(world.ExportState())), Provider);
            Assert.Equal(world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment,
                savedWorld.Inhabitants.Single(person => person.InhabitantId == actor).Equipment);
            savedWorld.Validate();
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public async Task TimedRepairResumesAfterCodecReloadAndSpendsOnlyItsReservedCloth()
    {
        var (state, shop) = TailorTestWorld.Create("equipment-repair", 0);
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == Alpha).Id;
        var position = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == shop).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "repair-coat", "padded_coat", actor, 1);
        inventory = InventoryFixture.WearSingleUnit(inventory, "repair-coat", 8_000);
        inventory = InventoryFixture.AddLot(inventory, "repair-cloth", "cloth", actor, 2);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == actor
                ? item with { Position = position, HungerBasisPoints = 9_000, Survival = new(), Equipment = new("repair-coat") } : item).ToArray(),
        };
        IDecisionProvider Provider(string id) => new Choices(id == actor ? ["repair_equipment"] : []);
        using var first = PrivateWorldRuntime.Restore(state, Provider);
        for (var tick = 0; tick < 30 && first.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair is null; tick++)
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        var working = first.ExportState();
        var repair = Assert.IsType<EquipmentRepairWork>(working.Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair);
        Assert.Equal(2, first.Society.Inventory.GetLot("repair-cloth").Quantity);
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(working)), Provider);
        for (var tick = 0; tick < 12 && resumed.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair is not null; tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Null(resumed.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair);
        Assert.Equal(1, resumed.Society.Inventory.GetLot("repair-cloth").Quantity);
        Assert.InRange(resumed.Society.Inventory.GetLot("repair-coat").ConditionBasisPoints, 7_800, 8_000);
        Assert.All(repair.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Completed, resumed.Society.Inventory.GetReservation(id).State));
        resumed.Validate();

        var interrupted = working with { Inhabitants = working.Inhabitants.Select(item => item.InhabitantId == actor ? item with { HungerBasisPoints = 1_000 } : item).ToArray() };
        using var cancelled = PrivateWorldRuntime.Restore(interrupted, Provider);
        Assert.True((await cancelled.AdvanceOneTickAsync()).Advanced);
        Assert.Null(cancelled.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair);
        Assert.Equal(2, cancelled.Society.Inventory.GetLot("repair-cloth").Quantity);
        Assert.All(repair.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Released, cancelled.Society.Inventory.GetReservation(id).State));
        cancelled.Validate();
    }

    [Fact]
    public async Task ReplacingABrokenBasketKeepsItsOverloadedCargoAndTheOldBasket()
    {
        using var generated = NormalPathWorld.CreateGenerated("equipment-replacement", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == Alpha).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "old-basket", "basket", actor, 1);
        inventory = InventoryFixture.WearSingleUnit(inventory, "old-basket", 10_000);
        inventory = InventoryFixture.AddLot(inventory, "overloaded-wood", "wood", actor, 12);
        inventory = InventoryFixture.AddLot(inventory, "replacement-sack", "sack", Alpha, 1, storageBuildingId: "first-town-house-a");
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == actor
            ? item with { Equipment = new(CarryAidLotId: "old-basket") } : item).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state, id => new Choices(id == actor ? ["equip_carry_aid"] : []));
        for (var tick = 0; tick < 120 && world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.CarryAidLotId != "replacement-sack"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var person = world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor);
        Assert.Equal("replacement-sack", person.Equipment?.CarryAidLotId);
        Assert.Equal((actor, 1, 0), (world.Society.Inventory.GetLot("old-basket").OwnerId, world.Society.Inventory.GetLot("old-basket").Quantity, world.Society.Inventory.GetLot("old-basket").ConditionBasisPoints));
        Assert.Equal(12, world.Society.Inventory.GetLot("overloaded-wood").Quantity);
        var reloaded = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(person.Equipment, reloaded.Inhabitants.Single(item => item.InhabitantId == actor).Equipment);
        world.Validate();
    }

    [Fact]
    public void CurrentSaveRejectsAnEquippedStackOrAnotherPersonsItem()
    {
        using var generated = NormalPathWorld.CreateGenerated("equipment-invalid", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        foreach (var (owner, quantity) in new[] { (actor, 2), (state.Inhabitants[1].InhabitantId, 1) })
        {
            var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "invalid-sack", "sack", owner, quantity);
            var invalid = WithInventory(state, inventory) with
            {
                Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == actor
                ? item with { Equipment = new(CarryAidLotId: "invalid-sack") } : item).ToArray()
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(invalid)));
        }
    }

    [Fact]
    public async Task AFullCarrierDeclinesGearAndRepairThenCanDeliverAndSwapWithoutLosingGoods()
    {
        var (state, _) = TailorTestWorld.Create("equipment-full-carrier", 0);
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == Alpha).Id;
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "full-garment", "clothing", actor, 1);
        inventory = InventoryFixture.WearSingleUnit(inventory, "full-garment", 8_000);
        inventory = InventoryFixture.AddLot(inventory, "full-cargo", "wood", Alpha, 8,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.Transfer(inventory, "full-cargo-collected", Alpha, actor, "full-cargo", 8,
            "household_stock_picked_up", destinationDeliveryBuildingId: house.InstanceId);
        foreach (var (id, kind) in new[] { ("full-coat", "padded_coat"), ("full-cloth", "cloth"), ("full-pickaxe", "wooden_pickaxe") })
            inventory = InventoryFixture.AddLot(inventory, id, kind, Alpha, 1, storageBuildingId: house.InstanceId);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position, HungerBasisPoints = 9_000, Equipment = new("full-garment") } : person).ToArray(),
        };
        state = WithWeather(state, WeatherKind.Snow);
        var blocked = new Choices(["wear_clothing", "repair_equipment", "collect_wooden_pickaxe"]);
        using var full = PrivateWorldRuntime.Restore(state, id => id == actor ? blocked : new Choices([]));
        for (var tick = 0; tick < 5; tick++) Assert.True((await full.AdvanceOneTickAsync()).Advanced);
        Assert.All(blocked.Offers, offer =>
        {
            Assert.DoesNotContain("wear_clothing", offer);
            Assert.DoesNotContain("repair_equipment", offer);
            Assert.DoesNotContain("collect_wooden_pickaxe", offer);
        });
        Assert.Equal("full-garment", full.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.ClothingLotId);
        Assert.Equal((actor, 8), (full.Society.Inventory.GetLot("full-cargo").OwnerId, full.Society.Inventory.GetLot("full-cargo").Quantity));
        // This controlled provider changes its choice after the declined
        // actions, so ask for a fresh decision without changing physical stock.
        var deliveryState = full.ExportState();
        deliveryState = deliveryState with
        {
            Inhabitants = deliveryState.Inhabitants.Select(person =>
            person.InhabitantId == actor ? person with { LastDecisionContext = null } : person).ToArray()
        };
        using var delivery = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(deliveryState)), id => new Choices(id == actor ? ["haul_household_stock"] : []));
        Assert.True((await delivery.AdvanceOneTickAsync()).Advanced);
        var delivered = delivery.Society.Inventory.GetLot("full-cargo");
        Assert.Equal((Alpha, 8, house.InstanceId), (delivered.OwnerId, delivered.Quantity, delivered.StorageBuildingId));
        using var swap = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(delivery.ExportState())), id => new Choices(id == actor ? ["wear_clothing"] : []));
        for (var tick = 0; tick < 60 && swap.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.ClothingLotId == "full-garment"; tick++)
            Assert.True((await swap.AdvanceOneTickAsync()).Advanced);
        var replacementId = Assert.IsType<string>(swap.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.ClothingLotId);
        Assert.NotEqual("full-garment", replacementId);
        var replacement = swap.Society.Inventory.GetLot(replacementId);
        Assert.True(PersonalEquipmentRules.IsGarment(replacement.ItemKind));
        Assert.Equal((actor, 1), (replacement.OwnerId, replacement.Quantity));
        Assert.Null(replacement.StorageBuildingId);
        Assert.True(replacement.ConditionBasisPoints > swap.Society.Inventory.GetLot("full-garment").ConditionBasisPoints);
        Assert.Equal((actor, 1), (swap.Society.Inventory.GetLot("full-garment").OwnerId, swap.Society.Inventory.GetLot("full-garment").Quantity));
        Assert.Equal((Alpha, 1), (swap.Society.Inventory.GetLot("full-cloth").OwnerId, swap.Society.Inventory.GetLot("full-cloth").Quantity));
        swap.Validate();
    }

    [Fact]
    public async Task AnOverloadedCarrierKeepsTheBrokenBasketWhenAReplacementWouldStillExceedCapacity()
    {
        using var generated = NormalPathWorld.CreateGenerated("equipment-too-small-replacement", _ => new Choices([]));
        var state = generated.ExportState();
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == Alpha).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "broken-basket", "basket", actor, 1);
        inventory = InventoryFixture.WearSingleUnit(inventory, "broken-basket", 10_000);
        inventory = InventoryFixture.AddLot(inventory, "heavy-cargo", "wood", actor, 20);
        inventory = InventoryFixture.AddLot(inventory, "small-replacement", "basket", Alpha, 1, storageBuildingId: "first-town-house-a");
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = new(CarryAidLotId: "broken-basket") } : person).ToArray(),
        };
        var choices = new Choices(["equip_carry_aid"]);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? choices : new Choices([]));
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.All(choices.Offers, offer => Assert.DoesNotContain("equip_carry_aid", offer));
        Assert.Equal("broken-basket", world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.CarryAidLotId);
        Assert.Equal((actor, 20), (world.Society.Inventory.GetLot("heavy-cargo").OwnerId, world.Society.Inventory.GetLot("heavy-cargo").Quantity));
        Assert.Equal((Alpha, 1), (world.Society.Inventory.GetLot("small-replacement").OwnerId, world.Society.Inventory.GetLot("small-replacement").Quantity));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new Choices([]));
        restored.Validate();
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(4, true)]
    public async Task FoodHarvestIsOfferedOnlyWhenTheWholeOutputFits(int cargo, bool fits)
    {
        using var initial = new PrivateWorldRuntime("food-capacity-boundary", _ => new Choices([]));
        initial.StageStarterContent();
        Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var source = state.Map.Resources.First(item => item.Kind == "food" && item.TreeKind is null);
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "harvest-cargo", "wood", actor, cargo);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = source.Position, HungerBasisPoints = 6_000, LastDecisionContext = null } : person).ToArray(),
        };
        var provider = new Choices(["harvest_food"]);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new Choices([]));
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        if (fits) Assert.Contains(provider.Offers, offer => offer.Split(',').Contains("harvest_food", StringComparer.Ordinal));
        else Assert.All(provider.Offers, offer => Assert.DoesNotContain("harvest_food", offer.Split(',')));
        Assert.Equal(fits ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)));
        Assert.Equal(cargo + (fits ? 4 : 0), PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
        Assert.Equal(cargo, world.Society.Inventory.GetLot("harvest-cargo").Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "carrying_full" && item.Detail == actor);
    }

    [Fact]
    public async Task AHungryCarrierSetsDownSpareSuppliesThenGathersFoodAcrossReload()
    {
        using var initial = new PrivateWorldRuntime("food-capacity-boundary", _ => new Choices([]));
        initial.StageStarterContent();
        Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.Inhabitants.Single(item => item.Id == actor).HouseholdId!;
        var source = state.Map.Resources.First(item => item.Kind == "food" && item.TreeKind is null);
        var inventory = state.Society.Society.Inventory;
        // With no household food to collect, the bush is the only food.
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.OwnerId != actor && !(lot.OwnerId == household && lot.ItemKind == "food")).ToArray(),
        };
        // Five carried units leave room for 3; a food pick needs 4.
        inventory = InventoryFixture.AddLot(inventory, "spare-wood", "wood", actor, 4);
        inventory = InventoryFixture.AddLot(inventory, "spare-tool", "tool", actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = source.Position, HungerBasisPoints = 6_000, LastDecisionContext = null } : person).ToArray(),
        };
        var householdWood = inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var provider = new Choices(["make_room_for_food", "harvest_food", "seek_food"]);
        var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new Choices([]));
        try
        {
            for (var tick = 0; tick < 60 && !world.ExportState().Events.Any(item => item.Kind == "food_harvested" &&
                     item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null), 0, 8);
                if (tick == 1)
                {
                    world.Pause();
                    var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
                        PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => id == actor ? provider : new Choices([]));
                    world.Dispose();
                    world = reloaded;
                    world.Resume();
                }
            }

            var events = world.ExportState().Events;
            Assert.Contains(provider.Offers, offer => offer.Split(',').Contains("make_room_for_food", StringComparer.Ordinal));
            Assert.Single(events, item => item.Kind == "spare_cargo_stored" && item.Detail.StartsWith(actor + ":wood:1:", StringComparison.Ordinal));
            Assert.Single(events, item => item.Kind == "food_harvested" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
            var lots = world.Society.Inventory.Lots;
            // Wood is set down before the tool, and nothing is lost on the way.
            Assert.Equal(3, lots.Where(lot => PersonalEquipmentRules.IsCarried(lot, actor) && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
            Assert.Equal(1, lots.Where(lot => PersonalEquipmentRules.IsCarried(lot, actor) && lot.ItemKind == "tool").Sum(lot => lot.Quantity));
            Assert.Equal(householdWood + 1, lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
            Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
            world.Validate();
        }
        finally
        {
            world.Dispose();
        }
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(4, true)]
    public async Task HelpingAProjectOffersGatheringOnlyWhenTheWholeLoadFits(int cargo, bool fits)
    {
        using var initial = new PrivateWorldRuntime("settlement-acquisition", _ => new Choices([]));
        initial.StageStarterContent();
        for (var tick = 0; tick < 6; tick++) Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        var state = initial.ExportState();
        var requester = state.Inhabitants[0].InhabitantId;
        var helper = state.Inhabitants[1].InhabitantId;
        var source = state.Map.Resources.First(item => item.Kind == "construction" && item.TreeKind is null);
        var house = initial.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != helper && lot.ItemKind != "wood").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "helper-unrelated-cargo", "fiber", helper, cargo);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == helper
                ? person with { Position = source.Position, HungerBasisPoints = 9_500, Equipment = null, LastDecisionContext = null }
                : person.InhabitantId == requester ? person with
                {
                    HungerBasisPoints = 9_500,
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(house.CanonicalId, site),
                        house.DisplayName, initial.WorldTick, "acquiring", LastTransitionTick: initial.WorldTick),
                } : person).ToArray(),
        };
        var provider = new Choices(["assist:wood"]);
        using var world = PrivateWorldRuntime.Restore(state, id => id == helper ? provider : new Choices([]));
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.InRange(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, helper, null), 0, 8);
            if (world.ExportState().Events.Any(item => item.Kind == "material_gathered" &&
                item.Detail.StartsWith(helper + ":wood:", StringComparison.Ordinal))) break;
        }
        if (fits) Assert.Contains(provider.Offers, offer => offer.Split(',').Contains("assist:wood", StringComparer.Ordinal));
        else Assert.All(provider.Offers, offer => Assert.DoesNotContain("assist:wood", offer.Split(',')));
        Assert.Equal(fits ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "material_gathered" &&
            item.Detail.StartsWith(helper + ":wood:4", StringComparison.Ordinal)));
        Assert.Equal(cargo, world.Society.Inventory.GetLot("helper-unrelated-cargo").Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "carrying_full" && item.Detail == helper);
        world.Validate();
    }

    [Fact]
    public async Task FirstHouseHelperStagesMaterialsAtCampEvenWhenTheBuilderCannotCarryMoreAcrossReload()
    {
        using var initial = new PrivateWorldRuntime("settlement-acquisition", _ => new Choices([]));
        initial.StageStarterContent();
        for (var tick = 0; tick < 6; tick++) Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        var state = initial.ExportState();
        var requester = state.Inhabitants[0].InhabitantId;
        var helper = state.Inhabitants[1].InhabitantId;
        var household = state.Society.Society.GetInhabitant(requester).HouseholdId!;
        var camp = state.Map.GetObject("storage").Position;
        var house = initial.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != helper &&
                lot.OwnerId != requester && lot.ItemKind != "wood").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "full-builders-stone", "stone", requester, 8);
        inventory = InventoryFixture.AddLot(inventory, "helper-house-wood", "wood", helper, 4);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == helper
                ? person with { HungerBasisPoints = 9_500, Equipment = null, LastDecisionContext = null }
                : person.InhabitantId == requester ? person with
                {
                    HungerBasisPoints = 9_500,
                    Equipment = null,
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(house.CanonicalId, site),
                        house.DisplayName, initial.WorldTick, "acquiring", LastTransitionTick: initial.WorldTick),
                } : person).ToArray(),
        };
        var provider = new Choices(["assist:wood"]);
        using var world = PrivateWorldRuntime.Restore(state, id => id == helper ? provider : new Choices([]));
        for (var tick = 0; tick < 30 && !world.ExportState().Events.Any(item => item.Kind == "project_request_fulfilled"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "project_request_fulfilled" &&
            item.Detail == helper + ":" + requester + ":wood:4");
        var staged = world.Society.Inventory.GetLot("helper-house-wood");
        Assert.Equal(household, staged.OwnerId);
        Assert.Equal(new InventoryGroundPosition(camp.X, camp.Y), staged.GroundPosition);
        Assert.Null(staged.StorageBuildingId);
        Assert.Null(staged.DeliveryBuildingId);
        Assert.Equal(4, staged.Quantity);
        Assert.Equal(8, world.Society.Inventory.GetLot("full-builders-stone").Quantity);
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, requester, null));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new Choices([]));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(staged, restored.Society.Inventory.GetLot(staged.Id));
        restored.Validate();
    }

    private static PrivateWorldRuntimeState WithWeather(PrivateWorldRuntimeState state, WeatherKind weather)
    {
        var systems = state.WorldSystems!;
        return state with
        {
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>()
                    .Select(season => new WeatherProfile(season, ClearWeight: weather == WeatherKind.Clear ? 1 : 0,
                        CloudyWeight: 0, RainWeight: weather == WeatherKind.Rain ? 1 : 0,
                        StormWeight: 0, SnowWeight: weather == WeatherKind.Snow ? 1 : 0)).ToArray()
                },
                Climate = systems.Climate with { Weather = weather },
            },
        };
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private sealed class Choices(IReadOnlyList<string> allowed) : IDecisionProvider
    {
        public List<string> Offers { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offers.Add(string.Join(",", request.Observation.Candidates.Select(item => item.Id)));
            var permitted = request.Observation.Candidates.Where(item => allowed.Any(prefix => item.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .Select(item => item with
                {
                    DeterministicPriority = allowed.Select((prefix, index) => (prefix, index))
                    .First(entry => item.Id.StartsWith(entry.prefix, StringComparison.Ordinal)).index
                }).ToArray();
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                { Candidates = permitted.Length > 0 ? permitted : [request.Observation.Candidates.Single(item => item.Id == "safe_idle")] }
            }, cancellationToken);
        }
    }
}
