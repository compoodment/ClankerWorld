using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class CareProductionTests(ITestOutputHelper output)
{
    private const string Alpha = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private const string Clinic = "care-production-clinic";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HouseAndTailorCutOnlyTheirOwnedOnSiteClothIntoBandagesAcrossReload(bool atTailor)
    {
        using var setup = NormalPathWorld.CreateGenerated("care-bandages", _ => new Preferred([]));
        var state = setup.ExportState();
        var actor = AlphaActor(state);
        var workplace = House;
        if (atTailor)
        {
            state = Stock(state, "care-tailor-fiber", "fiber", Alpha, 2, House);
            using var funded = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
            PlaceNearHouse(funded, "care-tailor", TailorContent.TailorShop1x1().CanonicalId);
            state = funded.ExportState();
            workplace = "care-tailor";
        }
        state = Stock(state, "care-cloth", "cloth", Alpha, 1, workplace);
        state = At(state, actor, state.WorldSimulation!.Buildings.Single(building => building.InstanceId == workplace).Position);
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == (atTailor ? "tailor-bandages" : "house-bandages"));
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var started = world.StartProduction(recipe.CanonicalId, workplace, actor);
        Assert.True(started.Applied, started.Failure);
        var job = Assert.Single(world.WorldSimulation.ProductionJobs, item => item.JobId == started.JobId);
        var reservation = Assert.Single(job.InputReservationIds.Select(world.Society.Inventory.GetReservation));
        Assert.Equal(("care-cloth", Alpha, 1, InventoryReservationState.Reserved),
            (reservation.LotId, reservation.OwnerId, reservation.Quantity, reservation.State));
        Assert.Equal(8, job.CompletionTick - job.StartedTick);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new Preferred([]));
        for (var tick = 1; tick < 8; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "care-cloth");
        var bandages = world.Society.Inventory.GetLot(started.JobId + ":output:00");
        Assert.Equal((CareContent.Bandage, Alpha, workplace, 2),
            (bandages.ItemKind, bandages.OwnerId, bandages.StorageBuildingId, bandages.Quantity));
        Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(reservation.Id).State);
        Assert.Equal(WorldProductionJobState.Completed, world.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId).State);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task MedicineReservesActualJugContentsAndConsumesOnlyOneWaterWhileKeepingTheReusableJugAcrossReload()
    {
        var state = WithClinic("care-medicine");
        var actor = AlphaActor(state);
        state = MedicineInputs(state, Clinic);
        state = At(state, actor, state.WorldSimulation!.Buildings.Single(building => building.InstanceId == Clinic).Position);
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "clinic-medicine");
        var started = world.StartProduction(recipe.CanonicalId, Clinic, actor);
        Assert.True(started.Applied, started.Failure);
        var job = world.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId);
        Assert.Equal(16, job.CompletionTick - job.StartedTick);
        var reservations = job.InputReservationIds.Select(world.Society.Inventory.GetReservation).ToArray();
        Assert.Equal(3, reservations.Length);
        Assert.Contains(reservations, item => item.LotId == "care-herbs" && item.Quantity == 2 && item.OwnerId == Alpha);
        Assert.Contains(reservations, item => item.LotId == "care-water" && item.Quantity == 1 && item.OwnerId == Alpha);
        Assert.Contains(reservations, item => item.LotId == "care-fuel" && item.Quantity == 1 && item.OwnerId == Alpha);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(world.Society.Inventory,
            "reserved-jug-move", Alpha, actor, "care-jug", 1, "collect"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new Preferred([]));
        for (var tick = 1; tick < 16; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "care-herbs" or "care-fuel");
        var water = world.Society.Inventory.GetLot("care-water");
        Assert.Equal((InventoryContainerRules.FreshWater, Alpha, Clinic, "care-jug", 3),
            (water.ItemKind, water.OwnerId, water.StorageBuildingId, water.ContainerLotId, water.Quantity));
        var jug = world.Society.Inventory.GetLot("care-jug");
        Assert.Equal((InventoryContainerRules.WaterJug, Alpha, Clinic, 1),
            (jug.ItemKind, jug.OwnerId, jug.StorageBuildingId, jug.Quantity));
        var medicine = world.Society.Inventory.GetLot(started.JobId + ":output:00");
        Assert.Equal((CareContent.Medicine, Alpha, Clinic, 2),
            (medicine.ItemKind, medicine.OwnerId, medicine.StorageBuildingId, medicine.Quantity));
        Assert.All(reservations, item => Assert.Equal(InventoryReservationState.Completed,
            world.Society.Inventory.GetReservation(item.Id).State));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var moved = InventoryFixture.Transfer(world.Society.Inventory, "reuse-jug", Alpha, actor, jug.Id, 1, "collect");
        Assert.Equal(actor, moved.GetLot(jug.Id).OwnerId);
        Assert.Equal((actor, jug.Id, 3), (moved.GetLot(water.Id).OwnerId, moved.GetLot(water.Id).ContainerLotId, moved.GetLot(water.Id).Quantity));
    }

    [Theory]
    [InlineData("remote")]
    [InlineData("other-household")]
    [InlineData("reserved-water")]
    [InlineData("broken-jug")]
    public void MedicineCannotUseRemotePrivateReservedOrBrokenVesselInputs(string boundary)
    {
        var state = WithClinic("care-medicine-boundary");
        var actor = AlphaActor(state);
        state = MedicineInputs(state, boundary == "remote" ? House : Clinic);
        var position = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == Clinic).Position;
        if (boundary == "other-household")
            actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId != Alpha).Id;
        state = At(state, actor, position);
        if (boundary == "reserved-water")
            state = WithInventory(state, InventoryFixture.Reserve(state.Society.Society.Inventory,
                "care-water-other-task", Alpha, "care-water", 4, "other_work", 100));
        if (boundary == "broken-jug")
            state = WithInventory(state, InventoryFixture.WearSingleUnit(state.Society.Society.Inventory, "care-jug", 10_000));
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var result = world.StartProduction(world.WorldContent.Recipes.Single(item => item.LocalId == "clinic-medicine").CanonicalId,
            Clinic, actor);
        Assert.False(result.Applied);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(4, world.Society.Inventory.GetLot("care-water").Quantity);
        Assert.Equal(2, world.Society.Inventory.GetLot("care-herbs").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("care-fuel").Quantity);
    }

    [Theory]
    [InlineData("available")]
    [InlineData("carrying-room")]
    [InlineData("reserved-water")]
    [InlineData("storage-room")]
    public async Task ClinicSupplyMovesTheWholeFilledJugOnlyWithActualRoomAndUnreservedContentsAcrossReload(string boundary)
    {
        var state = WithHouseCookingWaterReserve(WithClinic("care-whole-jug-supply"));
        var actor = AlphaActor(state);
        state = Stock(state, "care-supply-herbs", CareContent.MedicinalHerbs, Alpha, 4, Clinic);
        state = Stock(state, "care-supply-fuel", "wood", Alpha, 2, Clinic);
        state = Stock(state, "care-supply-jug", InventoryContainerRules.WaterJug, Alpha, 1, House);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "care-supply-water",
            InventoryContainerRules.FreshWater, Alpha, 4, storageBuildingId: House,
            containerLotId: "care-supply-jug");
        var equipment = state.Inhabitants.Single(person => person.InhabitantId == actor).Equipment;
        var free = PersonalEquipmentRules.FreeCapacity(inventory, actor, equipment);
        var remainingRoom = boundary == "carrying-room" ? 4 : 5;
        Assert.True(free > remainingRoom);
        inventory = InventoryFixture.AddLot(inventory, "care-supply-carried-load", "test_cargo", actor,
            free - remainingRoom);
        Assert.Equal(remainingRoom, PersonalEquipmentRules.FreeCapacity(inventory, actor, equipment));
        if (boundary == "reserved-water")
            inventory = InventoryFixture.Reserve(inventory, "care-supply-water-reserved", Alpha,
                "care-supply-water", 1, "other_work", state.Society.Society.WorldTick + 100);
        if (boundary == "storage-room")
        {
            var clinic = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == Clinic);
            var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == clinic.DefinitionId);
            var capacity = BuildingStorageRules.Capacity(definition, clinic)!.Value;
            var stored = inventory.Lots.Where(lot => lot.StorageBuildingId == Clinic).Sum(lot => lot.Quantity);
            inventory = InventoryFixture.AddLot(inventory, "care-supply-stored-load", "test_cargo", Alpha,
                capacity - stored - 4, storageBuildingId: Clinic);
            Assert.Equal(4, capacity - inventory.Lots.Where(lot => lot.StorageBuildingId == Clinic).Sum(lot => lot.Quantity));
        }
        state = At(WithInventory(state, inventory), actor,
            state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position);
        var choices = new Preferred(["supply_workstation:fresh_water", "haul_household_stock"]);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? choices : new Preferred([]));
        for (var tick = 0; tick < 8 && (boundary != "available" ||
             world.Society.Inventory.GetLot("care-supply-jug").OwnerId != actor); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        if (boundary != "available")
        {
            Assert.DoesNotContain("supply_workstation:fresh_water", choices.Offers);
            Assert.Equal((Alpha, House, (string?)null, 1),
                (world.Society.Inventory.GetLot("care-supply-jug").OwnerId,
                    world.Society.Inventory.GetLot("care-supply-jug").StorageBuildingId,
                    world.Society.Inventory.GetLot("care-supply-jug").DeliveryBuildingId,
                    world.Society.Inventory.GetLot("care-supply-jug").Quantity));
            Assert.Equal((Alpha, House, (string?)null, "care-supply-jug", 4),
                (world.Society.Inventory.GetLot("care-supply-water").OwnerId,
                    world.Society.Inventory.GetLot("care-supply-water").StorageBuildingId,
                    world.Society.Inventory.GetLot("care-supply-water").DeliveryBuildingId,
                    world.Society.Inventory.GetLot("care-supply-water").ContainerLotId,
                    world.Society.Inventory.GetLot("care-supply-water").Quantity));
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up");
            if (boundary == "reserved-water")
                Assert.Equal(InventoryReservationState.Reserved,
                    world.Society.Inventory.GetReservation("care-supply-water-reserved").State);
            AssertHouseCookingWaterReserve(world);
            var unchanged = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var refusalReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(unchanged), _ => new Preferred([]));
            Assert.Equal(unchanged, PrivateWorldRuntimeCodec.Encode(refusalReload.ExportState()));
            return;
        }

        Assert.Contains("supply_workstation:fresh_water", choices.Offers);
        Assert.Equal((actor, (string?)null, Clinic, 1),
            (world.Society.Inventory.GetLot("care-supply-jug").OwnerId,
                world.Society.Inventory.GetLot("care-supply-jug").StorageBuildingId,
                world.Society.Inventory.GetLot("care-supply-jug").DeliveryBuildingId,
                world.Society.Inventory.GetLot("care-supply-jug").Quantity));
        Assert.Equal((actor, (string?)null, Clinic, "care-supply-jug", 4),
            (world.Society.Inventory.GetLot("care-supply-water").OwnerId,
                world.Society.Inventory.GetLot("care-supply-water").StorageBuildingId,
                world.Society.Inventory.GetLot("care-supply-water").DeliveryBuildingId,
                world.Society.Inventory.GetLot("care-supply-water").ContainerLotId,
                world.Society.Inventory.GetLot("care-supply-water").Quantity));
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor,
            world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment));
        var inTransit = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(inTransit), id => id == actor
            ? new Preferred(["supply_workstation:fresh_water", "haul_household_stock"]) : new Preferred([]));
        Assert.Equal(inTransit, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        for (var tick = 0; tick < 64 && world.Society.Inventory.GetLot("care-supply-jug").StorageBuildingId != Clinic; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal((Alpha, Clinic, (string?)null, 1),
            (world.Society.Inventory.GetLot("care-supply-jug").OwnerId,
                world.Society.Inventory.GetLot("care-supply-jug").StorageBuildingId,
                world.Society.Inventory.GetLot("care-supply-jug").DeliveryBuildingId,
                world.Society.Inventory.GetLot("care-supply-jug").Quantity));
        Assert.Equal((Alpha, Clinic, (string?)null, "care-supply-jug", 4),
            (world.Society.Inventory.GetLot("care-supply-water").OwnerId,
                world.Society.Inventory.GetLot("care-supply-water").StorageBuildingId,
                world.Society.Inventory.GetLot("care-supply-water").DeliveryBuildingId,
                world.Society.Inventory.GetLot("care-supply-water").ContainerLotId,
                world.Society.Inventory.GetLot("care-supply-water").Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up" &&
            item.Detail == $"{actor}:care-supply-jug:1:{Clinic}");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "household_stock_delivered" &&
            item.Detail.Contains("care-supply-jug", StringComparison.Ordinal));
        AssertHouseCookingWaterReserve(world);
        AssertHouseCookingWaterReserve(restored);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task OrdinarySupplyWalksFromTheRealHerbPatchAndCarriesFreshWaterHomeToTheClinicBeforeProducingMedicine()
    {
        var state = WithHouseCookingWaterReserve(WithClinic("care-normal-supply"));
        var actor = AlphaActor(state);
        state = Stock(state, "care-pottery-clay", "clay", Alpha, 2, House);
        state = Stock(state, "care-pottery-fuel", "wood", Alpha, 1, House);
        state = At(state, actor, state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position);
        using var pottery = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        var jugRecipe = pottery.WorldContent.Recipes.Single(item => item.LocalId == "water-jug");
        var started = pottery.StartProduction(jugRecipe.CanonicalId, House, actor);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < jugRecipe.DurationTicks; tick++) Assert.True((await pottery.AdvanceOneTickAsync()).Advanced);
        var jugId = started.JobId + ":output:00";
        Assert.Equal(InventoryContainerRules.WaterJug, pottery.Society.Inventory.GetLot(jugId).ItemKind);
        var jugInputs = pottery.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId)
            .InputReservationIds.Select(pottery.Society.Inventory.GetReservation).ToArray();
        Assert.Contains(jugInputs, item => item.LotId == "care-pottery-clay" && item.Quantity == 2 && item.State == InventoryReservationState.Completed);
        Assert.Contains(jugInputs, item => item.LotId == "care-pottery-fuel" && item.Quantity == 1 &&
            item.OwnerId == Alpha && item.State == InventoryReservationState.Completed);
        state = pottery.ExportState();
        var herbs = Assert.Single(state.Map.Resources, item => item.Kind == CareContent.MedicinalHerbs);
        var initialHerbs = state.WorldSystems!.Ecology.GetResource(herbs.Id).Quantity;
        Assert.True(initialHerbs > 0);
        Assert.True(state.Map.IsReachableOnFoot(state.Inhabitants.Single(person => person.InhabitantId == actor).Position, herbs.Position));
        var medicineRecipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "clinic-medicine");
        var choices = new Preferred(["collect_water_jug", "fill_water_jug:", "return_water_jug", "supply_workstation:",
            "build:recipe:" + medicineRecipe.CanonicalId, "haul_household_stock"]);
        IDecisionProvider Provider(string id) => id == actor ? choices : new Preferred([]);
        using var world = PrivateWorldRuntime.Restore(state, Provider);
        PrivateWorldRuntime? restored = null;
        var previousHerbStock = initialHerbs;
        var observedHerbDepletion = false;
        try
        {
            for (var tick = 0; tick < 600 && !world.WorldSimulation.ProductionJobs.Any(job =>
                     job.RecipeId == medicineRecipe.CanonicalId && job.State == WorldProductionJobState.Completed); tick++)
            {
                var step = await world.AdvanceOneTickAsync();
                Assert.True(step.Advanced);
                var currentHerbStock = world.ExportState().WorldSystems!.Ecology.GetResource(herbs.Id).Quantity;
                if (step.Events.Any(item => item.Kind == "material_gathered" &&
                        item.Detail.StartsWith(actor + ":" + CareContent.MedicinalHerbs + ":", StringComparison.Ordinal)))
                {
                    Assert.True(currentHerbStock < previousHerbStock,
                        $"Gathering at tick {world.WorldTick} must deplete the actual herb patch ({previousHerbStock} -> {currentHerbStock}).");
                    observedHerbDepletion = true;
                    output.WriteLine("Herb patch {0} at gathering tick {1}: {2} -> {3}",
                        herbs.Id, world.WorldTick, previousHerbStock, currentHerbStock);
                }
                previousHerbStock = currentHerbStock;
                if (tick % 60 == 59)
                {
                    var snapshot = world.ExportState();
                    var person = snapshot.Inhabitants.Single(item => item.InhabitantId == actor);
                    output.WriteLine("Tick {0}: position={1}, hunger={2}, project={3}",
                        world.WorldTick, person.Position, person.HungerBasisPoints, person.Project);
                    foreach (var lot in snapshot.Society.Society.Inventory.Lots.Where(item =>
                                 item.OwnerId == actor || item.OwnerId == Alpha &&
                                 (item.ItemKind is "medicinal_herbs" or "fresh_water" or "water_jug" or "wood" or "clay" or "medicine")))
                        output.WriteLine("  {0}: {1} x{2}, owner={3}, storage={4}, delivery={5}, container={6}",
                            lot.Id, lot.ItemKind, lot.Quantity, lot.OwnerId, lot.StorageBuildingId,
                            lot.DeliveryBuildingId, lot.ContainerLotId);
                    foreach (var decision in choices.Decisions.TakeLast(4)) output.WriteLine("  " + decision);
                }
                if (restored is null && world.WorldSimulation.ProductionJobs.Any(job =>
                        job.RecipeId == medicineRecipe.CanonicalId && job.State == WorldProductionJobState.Running))
                    restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
                        PrivateWorldRuntimeCodec.Encode(world.ExportState())), Provider);
                else if (restored is not null)
                    Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            }
            Assert.NotNull(restored);
            Assert.Contains("supply_workstation:" + CareContent.MedicinalHerbs, choices.Offers);
            Assert.Contains("supply_workstation:" + InventoryContainerRules.FreshWater, choices.Offers);
            Assert.True(observedHerbDepletion);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "material_gathered" &&
                item.Detail.StartsWith(actor + ":" + CareContent.MedicinalHerbs + ":", StringComparison.Ordinal));
            Assert.Contains(world.ExportState().Events, item => item.Kind == "water_jug_filled" &&
                item.Detail.StartsWith(actor + ":" + jugId + ":4:", StringComparison.Ordinal));
            Assert.Equal((Alpha, Clinic, 1), (world.Society.Inventory.GetLot(jugId).OwnerId,
                world.Society.Inventory.GetLot(jugId).StorageBuildingId, world.Society.Inventory.GetLot(jugId).Quantity));
            var water = Assert.Single(world.Society.Inventory.Lots, lot => lot.ContainerLotId == jugId);
            Assert.Equal((InventoryContainerRules.FreshWater, Alpha, Clinic, 3),
                (water.ItemKind, water.OwnerId, water.StorageBuildingId, water.Quantity));
            var completed = Assert.Single(world.WorldSimulation.ProductionJobs, job => job.RecipeId == medicineRecipe.CanonicalId &&
                job.State == WorldProductionJobState.Completed);
            var inputs = completed.InputReservationIds.Select(world.Society.Inventory.GetReservation).ToArray();
            Assert.Contains(inputs, input => input.LotId == water.Id && input.Quantity == 1 && input.OwnerId == Alpha &&
                input.State == InventoryReservationState.Completed);
            Assert.Contains(inputs, input => input.LotId.StartsWith("material:", StringComparison.Ordinal) &&
                input.Quantity == 2 && input.OwnerId == Alpha && input.State == InventoryReservationState.Completed);
            Assert.Equal((CareContent.Medicine, Alpha, Clinic, 2),
                (world.Society.Inventory.GetLot(completed.JobId + ":output:00").ItemKind,
                    world.Society.Inventory.GetLot(completed.JobId + ":output:00").OwnerId,
                    world.Society.Inventory.GetLot(completed.JobId + ":output:00").StorageBuildingId,
                    world.Society.Inventory.GetLot(completed.JobId + ":output:00").Quantity));
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored!.ExportState()));
            AssertHouseCookingWaterReserve(world);
            AssertHouseCookingWaterReserve(restored);
        }
        finally { restored?.Dispose(); }
    }

    private static PrivateWorldRuntimeState WithHouseCookingWaterReserve(PrivateWorldRuntimeState state)
    {
        // One water input is already claimed and two remain usable for House
        // meals. Its content claim holds this separate jug here, so the Clinic
        // cases exercise their original jug and capacity/claim boundary.
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "care-house-reserve-jug", InventoryContainerRules.WaterJug, Alpha, 1,
            storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "care-house-reserve-water",
            InventoryContainerRules.FreshWater, Alpha, 3, storageBuildingId: House,
            containerLotId: "care-house-reserve-jug");
        inventory = InventoryFixture.Reserve(inventory, "care-house-reserve-water-claim", Alpha,
            "care-house-reserve-water", 1, "house_cooking", state.Society.Society.WorldTick + 1_000);
        return WithInventory(state, inventory);
    }

    private static void AssertHouseCookingWaterReserve(PrivateWorldRuntime world)
    {
        var jug = world.Society.Inventory.GetLot("care-house-reserve-jug");
        var water = world.Society.Inventory.GetLot("care-house-reserve-water");
        Assert.Equal((InventoryContainerRules.WaterJug, Alpha, House, (string?)null, 1, 10_000),
            (jug.ItemKind, jug.OwnerId, jug.StorageBuildingId, jug.DeliveryBuildingId,
                jug.Quantity, jug.ConditionBasisPoints));
        Assert.Equal((InventoryContainerRules.FreshWater, Alpha, House, (string?)null, "care-house-reserve-jug", 3),
            (water.ItemKind, water.OwnerId, water.StorageBuildingId, water.DeliveryBuildingId,
                water.ContainerLotId, water.Quantity));
        var claim = world.Society.Inventory.GetReservation("care-house-reserve-water-claim");
        Assert.Equal((Alpha, "care-house-reserve-water", 1, InventoryReservationState.Reserved),
            (claim.OwnerId, claim.LotId, claim.Quantity, claim.State));
    }

    private static PrivateWorldRuntimeState WithClinic(string seed)
    {
        using var generated = NormalPathWorld.CreateGenerated(seed, _ => new Preferred([]));
        var state = Stock(generated.ExportState(), "care-clinic-stone", "stone", Alpha, 4, House);
        using var funded = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        PlaceNearHouse(funded, Clinic, CareContent.Clinic1x2().CanonicalId);
        var reservations = funded.Society.Inventory.Reservations.Where(item => item.Purpose.Contains(Clinic, StringComparison.Ordinal)).ToArray();
        Assert.Contains(reservations, item => item.Quantity == 4 && item.OwnerId == Alpha && item.State == InventoryReservationState.Completed);
        Assert.Equal(10, reservations.Where(item => item.LotId != "care-clinic-stone").Sum(item => item.Quantity));
        return funded.ExportState();
    }

    private static void PlaceNearHouse(PrivateWorldRuntime world, string id, string definitionId)
    {
        var house = world.WorldSimulation.Buildings.Single(building => building.InstanceId == House);
        Assert.Contains(Enumerable.Range(-8, 17).SelectMany(y => Enumerable.Range(-8, 17).Select(x =>
            new GridPoint(house.Position.X + x, house.Position.Y + y))), point => world.PlaceBuilding(id, definitionId, point, Alpha).Applied);
    }

    private static PrivateWorldRuntimeState MedicineInputs(PrivateWorldRuntimeState state, string building)
    {
        state = Stock(state, "care-herbs", CareContent.MedicinalHerbs, Alpha, 2, building);
        state = Stock(state, "care-fuel", "wood", Alpha, 1, building);
        state = Stock(state, "care-jug", InventoryContainerRules.WaterJug, Alpha, 1, building);
        return WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "care-water",
            InventoryContainerRules.FreshWater, Alpha, 4, storageBuildingId: building, containerLotId: "care-jug"));
    }

    private static string AlphaActor(PrivateWorldRuntimeState state) =>
        state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;

    private static PrivateWorldRuntimeState At(PrivateWorldRuntimeState state, string actor, GridPoint position)
    {
        var previous = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = position, HungerBasisPoints = 9_000, LastDecisionContext = null }
                : person.Position == position ? person with { Position = previous } : person).ToArray(),
        };
    }

    private static PrivateWorldRuntimeState Stock(PrivateWorldRuntimeState state, string id, string kind,
        string owner, int quantity, string storage) => WithInventory(state, InventoryFixture.AddLot(
            state.Society.Society.Inventory, id, kind, owner, quantity, storageBuildingId: storage));

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private sealed class Preferred(IReadOnlyList<string> prefixes) : IDecisionProvider
    {
        public HashSet<string> Offers { get; } = new(StringComparer.Ordinal);
        public List<string> Decisions { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates) Offers.Add(candidate.Id);
            var choice = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(candidate => candidate is not null)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            Decisions.Add($"{request.Observation.WorldTick}: chose {choice.Id}; offered " + string.Join(", ",
                request.Observation.Candidates.Where(candidate => prefixes.Any(prefix =>
                    candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).Select(candidate =>
                    candidate.Id + " -> " + candidate.DestinationId)));
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
