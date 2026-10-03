using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmhouseSiloReplenishmentTests
{
    private const string UnrelatedHouseCargoClaimPrefix = "test-replenishment-house-cargo:";

    [Fact]
    public async Task StoredSiloGrainIsActuallyCarriedToAnEmptyFarmhouseForMillingAcrossReload()
    {
        var setup = await Prepare("silo-replenishment", growField: false, isolateStoredGrain: true);
        var inventory = InventoryFixture.AddLot(setup.State.Society.Society.Inventory,
            "silo-grain", FarmFieldRules.Grain, setup.Household, 8, storageBuildingId: setup.Silo);
        using var world = Restore(FarmFieldTests.WithInventory(setup.State, inventory), setup.Actor,
            "haul_farm_grain", "haul_household_stock");
        var mill = world.WorldContent.Recipes.Single(recipe => recipe.LocalId == "mill-grain");
        Assert.False(world.StartProduction(mill.CanonicalId, setup.Farmhouse, setup.Actor).Applied);
        AssertUnrelatedHouseCargoClaims(world, setup);
        var initialRoutes = DescribeFarmStockRoutes(world, setup);
        await Until(world, () => CarriedGrain(world, setup) is not null, 96, "Silo grain pickup",
            () => "Initial farm stock/routes:\n" + initialRoutes + "\nFinal farm stock/routes:\n" + DescribeFarmStockRoutes(world, setup));
        var carried = CarriedGrain(world, setup)!;
        Assert.Equal(2, carried.Quantity);
        Assert.True(IsFrom(carried, "silo-grain"));
        Assert.Null(carried.StorageBuildingId);
        Assert.Equal(6, world.Society.Inventory.GetLot("silo-grain").Quantity);
        Assert.Equal(setup.Silo, world.Society.Inventory.GetLot("silo-grain").StorageBuildingId);
        using var delivery = Reload(world, setup.Actor, "haul_household_stock");
        await Until(delivery, () => delivery.Society.Inventory.GetLot(carried.Id).StorageBuildingId == setup.Farmhouse,
            96, "actual Farmhouse delivery");
        Assert.Equal(setup.Household, delivery.Society.Inventory.GetLot(carried.Id).OwnerId);
        Assert.Equal(8, delivery.Society.Inventory.Lots.Where(lot => IsFrom(lot, "silo-grain")).Sum(lot => lot.Quantity));
        var started = delivery.StartProduction(mill.CanonicalId, setup.Farmhouse, setup.Actor);
        Assert.True(started.Applied, started.Failure);
        await Advance(delivery, mill.DurationTicks);
        var job = delivery.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId);
        Assert.Equal(WorldProductionJobState.Completed, job.State);
        var receipt = Assert.Single(job.InputReservationIds.Select(delivery.Society.Inventory.GetReservation));
        Assert.Equal((setup.Household, carried.Id, 1, InventoryReservationState.Completed),
            (receipt.OwnerId, receipt.LotId, receipt.Quantity, receipt.State));
        Assert.Equal(1, delivery.Society.Inventory.GetLot(job.JobId + ":output:00").Quantity);
        AssertUnrelatedHouseCargoClaims(delivery, setup);
        using var restored = Reload(delivery, setup.Actor, "safe_idle");
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(delivery.ExportState()),
            PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task ActualFieldOverflowReplenishesTheFarmhouseOnlyAfterItsRealFlourDeliveryFreesRoom()
    {
        var setup = await Prepare("field-full-stores", growField: true);
        var inventory = setup.State.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "onsite-grain", FarmFieldRules.Grain,
            setup.Household, 1, storageBuildingId: setup.Farmhouse);
        inventory = InventoryFixture.AddLot(inventory, "full-farm-seeds", FarmFieldRules.GrainSeed,
            setup.Household, 95, storageBuildingId: setup.Farmhouse);
        using var crop = FarmFieldTests.Restore(FarmFieldTests.WithInventory(setup.State, inventory));
        await Advance(crop, checked((int)Math.Max(0, Assert.Single(crop.Fields).ReadyTick - crop.WorldTick)));
        Assert.Equal(FarmFieldStage.Ready, Assert.Single(crop.Fields).Stage);
        Assert.True(crop.StartFieldWork(setup.Actor, setup.Point, FarmWorkKind.Harvest).Accepted);
        await Advance(crop, 4);
        var field = Assert.Single(crop.Fields);
        var grainRoot = $"{FarmFieldRules.FieldId(setup.Point)}:harvest:{field.Cycle}:crop";
        var batchQuantity = crop.Society.Inventory.GetLot(grainRoot).Quantity;
        Assert.True(batchQuantity > 0);
        Assert.Equal(new InventoryGroundPosition(setup.Point.X, setup.Point.Y),
            crop.Society.Inventory.GetLot(grainRoot).GroundPosition);

        using var overflow = Reload(crop, setup.Actor, "haul_farm_grain", "haul_household_stock");
        await Until(overflow, () => overflow.Society.Inventory.Lots.Where(lot => IsFrom(lot, grainRoot) &&
                lot.StorageBuildingId == setup.Silo).Sum(lot => lot.Quantity) == batchQuantity &&
            !overflow.Society.Inventory.Lots.Any(lot => lot.OwnerId == setup.Actor && lot.DeliveryBuildingId == setup.Silo),
            120, "real harvest overflow delivery to Silo");
        Assert.Equal(96, Stored(overflow, setup.Farmhouse));
        Assert.Equal(batchQuantity, overflow.Society.Inventory.Lots.Where(lot => IsFrom(lot, grainRoot)).Sum(lot => lot.Quantity));
        var planting = overflow.Society.Inventory.GetReservation(field.ReplantingReservationId!);
        Assert.Equal((1, InventoryReservationState.Reserved), (planting.Quantity, planting.State));
        Assert.Equal(new InventoryGroundPosition(setup.Point.X, setup.Point.Y),
            overflow.Society.Inventory.GetLot(planting.LotId).GroundPosition);

        var mill = overflow.WorldContent.Recipes.Single(recipe => recipe.LocalId == "mill-grain");
        using var firstMill = Reload(overflow, setup.Actor, "build:recipe:" + mill.CanonicalId);
        await Until(firstMill, () => firstMill.WorldSimulation.ProductionJobs.Any(job =>
                job.RecipeId == mill.CanonicalId && job.State == WorldProductionJobState.Completed),
            96, "first actual Mill job");
        var firstJob = firstMill.WorldSimulation.ProductionJobs.Single(job => job.RecipeId == mill.CanonicalId);
        var firstReceipt = Assert.Single(firstJob.InputReservationIds.Select(firstMill.Society.Inventory.GetReservation));
        Assert.Equal(("onsite-grain", 1, InventoryReservationState.Completed),
            (firstReceipt.LotId, firstReceipt.Quantity, firstReceipt.State));
        Assert.Equal(96, Stored(firstMill, setup.Farmhouse));
        Assert.Equal(batchQuantity, firstMill.Society.Inventory.Lots.Where(lot => IsFrom(lot, grainRoot)).Sum(lot => lot.Quantity));

        var flourId = firstJob.JobId + ":output:00";
        AssertFlourDeliveryPremises(firstMill, setup);
        using var flour = Reload(firstMill, setup.Actor, "haul_farm_flour", "haul_household_stock");
        await Until(flour, () => flour.Society.Inventory.Lots.Any(lot => IsFrom(lot, flourId) &&
                lot.StorageBuildingId == setup.House), 96, "first flour carried home");
        Assert.Equal(95, Stored(flour, setup.Farmhouse));

        using var replenishing = Reload(flour, setup.Actor, "haul_farm_grain", "haul_household_stock");
        var beforePosition = replenishing.Inhabitants.Single(person => person.InhabitantId == setup.Actor).Position;
        await Until(replenishing, () => CarriedGrain(replenishing, setup) is { } carried && IsFrom(carried, grainRoot),
            96, "stored harvest picked up from the Silo");
        var incoming = CarriedGrain(replenishing, setup)!;
        Assert.Equal(1, incoming.Quantity);
        Assert.Null(incoming.GroundPosition);
        Assert.Equal(95, Stored(replenishing, setup.Farmhouse));
        Assert.Equal(1, replenishing.Society.Inventory.Lots.Where(lot => lot.DeliveryBuildingId == setup.Farmhouse).Sum(lot => lot.Quantity));
        Assert.Equal(batchQuantity - 1, replenishing.Society.Inventory.Lots.Where(lot => IsFrom(lot, grainRoot) &&
            lot.StorageBuildingId == setup.Silo).Sum(lot => lot.Quantity));
        Assert.NotEqual(beforePosition, replenishing.Inhabitants.Single(person => person.InhabitantId == setup.Actor).Position);

        var bytes = PrivateWorldRuntimeCodec.Encode(replenishing.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), setup.Actor, "haul_farm_grain", "haul_household_stock");
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 96 && replenishing.Society.Inventory.GetLot(incoming.Id).StorageBuildingId != setup.Farmhouse; tick++)
        {
            Assert.True((await replenishing.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(replenishing.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(setup.Farmhouse, replenishing.Society.Inventory.GetLot(incoming.Id).StorageBuildingId);
        Assert.Equal(setup.Household, replenishing.Society.Inventory.GetLot(incoming.Id).OwnerId);
        Assert.Equal(96, Stored(replenishing, setup.Farmhouse));

        using var secondMill = Reload(replenishing, setup.Actor, "build:recipe:" + mill.CanonicalId);
        await Until(secondMill, () => secondMill.WorldSimulation.ProductionJobs.Count(job =>
                job.RecipeId == mill.CanonicalId && job.State == WorldProductionJobState.Completed) == 2,
            96, "field-descended grain milled into flour");
        var secondJob = secondMill.WorldSimulation.ProductionJobs.Single(job => job.RecipeId == mill.CanonicalId &&
            job.JobId != firstJob.JobId);
        var secondReceipt = Assert.Single(secondJob.InputReservationIds.Select(secondMill.Society.Inventory.GetReservation));
        Assert.Equal((setup.Household, incoming.Id, 1, InventoryReservationState.Completed),
            (secondReceipt.OwnerId, secondReceipt.LotId, secondReceipt.Quantity, secondReceipt.State));
        var output = secondMill.Society.Inventory.GetLot(secondJob.JobId + ":output:00");
        Assert.Equal((setup.Household, "flour", 1, setup.Farmhouse),
            (output.OwnerId, output.ItemKind, output.Quantity, output.StorageBuildingId));
        Assert.Equal(batchQuantity - 1, secondMill.Society.Inventory.Lots.Where(lot => IsFrom(lot, grainRoot)).Sum(lot => lot.Quantity));
        Assert.Equal(95, secondMill.Society.Inventory.GetLot("full-farm-seeds").Quantity);
        AssertUnrelatedHouseCargoClaims(secondMill, setup);
        using var saved = Reload(secondMill, setup.Actor, "safe_idle");
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(secondMill.ExportState()),
            PrivateWorldRuntimeCodec.Encode(saved.ExportState()));
    }

    [Theory]
    [InlineData("full")]
    [InlineData("incoming")]
    [InlineData("onsite")]
    [InlineData("satisfied")]
    [InlineData("reserved")]
    public async Task SiloReplenishmentRespectsActualRoomExistingSupplyOutputDemandAndReservations(string boundary)
    {
        var setup = await Prepare("silo-replenishment-boundary", growField: false);
        var inventory = InventoryFixture.AddLot(setup.State.Society.Society.Inventory, "protected-silo-grain",
            FarmFieldRules.Grain, setup.Household, 8, storageBuildingId: setup.Silo);
        if (boundary == "full") inventory = InventoryFixture.AddLot(inventory, "full-farm",
            FarmFieldRules.GrainSeed, setup.Household, 96, storageBuildingId: setup.Farmhouse);
        if (boundary == "onsite") inventory = InventoryFixture.AddLot(inventory, "already-supplied",
            FarmFieldRules.Grain, setup.Household, 2, storageBuildingId: setup.Farmhouse);
        if (boundary == "incoming")
        {
            var carrier = setup.State.Society.Society.Inhabitants.First(person => person.HouseholdId == setup.Household &&
                person.Id != setup.Actor).Id;
            inventory = InventoryFixture.AddLot(inventory, "incoming-grain-root", FarmFieldRules.Grain,
                setup.Household, 2, storageBuildingId: setup.Silo);
            inventory = InventoryFixture.Transfer(inventory, "already-promised", setup.Household, carrier,
                "incoming-grain-root", 2, "farm_grain_picked_up", destinationDeliveryBuildingId: setup.Farmhouse);
        }
        if (boundary == "satisfied") inventory = InventoryFixture.AddLot(inventory, "enough-flour", "flour",
            setup.Household, setup.State.Inhabitants.Count, storageBuildingId: setup.House);
        if (boundary == "reserved") inventory = InventoryFixture.Reserve(inventory, "silo-reservation",
            setup.Household, "protected-silo-grain", 8, "saved-grain-claim", long.MaxValue);
        var provider = new Choices("haul_farm_grain", "haul_household_stock");
        using var world = PrivateWorldRuntime.Restore(FarmFieldTests.WithInventory(setup.State, inventory),
            id => id == setup.Actor ? provider : new Choices("safe_idle"));
        await Advance(world, 40);
        Assert.NotEmpty(provider.Offered);
        // Existing loose/ground farm stock can still have a legal haul. This
        // boundary protects only the actual Silo batch that would replenish.
        Assert.Equal((setup.Household, setup.Silo, 8),
            (world.Society.Inventory.GetLot("protected-silo-grain").OwnerId,
                world.Society.Inventory.GetLot("protected-silo-grain").StorageBuildingId,
                world.Society.Inventory.GetLot("protected-silo-grain").Quantity));
        Assert.DoesNotContain(world.Society.Inventory.Lots,
            lot => lot.Id != "protected-silo-grain" && IsFrom(lot, "protected-silo-grain"));
        if (boundary == "reserved")
            Assert.Equal((setup.Household, "protected-silo-grain", 8, InventoryReservationState.Reserved),
                (world.Society.Inventory.GetReservation("silo-reservation").OwnerId,
                    world.Society.Inventory.GetReservation("silo-reservation").LotId,
                    world.Society.Inventory.GetReservation("silo-reservation").Quantity,
                    world.Society.Inventory.GetReservation("silo-reservation").State));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "farm_grain_picked_up" &&
            item.Detail.Contains(":protected-silo-grain:", StringComparison.Ordinal));
        using var saved = Reload(world, setup.Actor, "haul_farm_grain", "haul_household_stock");
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(saved.ExportState()));
    }

    [Fact]
    public async Task StoredSiloPotReplenishesOnlyTheNeededGrainWithoutMovingOrCloningTheVessel()
    {
        // Keep unrelated camp deliveries claimed, as in the loose-grain case,
        // so this directed check reaches the stored pot within its tick bound.
        var setup = await Prepare("silo-pot-replenishment", growField: false, isolateStoredGrain: true);
        var inventory = InventoryFixture.AddLot(setup.State.Society.Society.Inventory,
            "silo-pot", InventoryContainerRules.StoragePot, setup.Household, 1, storageBuildingId: setup.Silo);
        inventory = InventoryFixture.AddLot(inventory, "silo-pot-grain", FarmFieldRules.Grain,
            setup.Household, 6, storageBuildingId: setup.Silo, containerLotId: "silo-pot");
        using var world = Restore(FarmFieldTests.WithInventory(setup.State, inventory), setup.Actor,
            "haul_farm_grain", "haul_household_stock");
        AssertUnrelatedHouseCargoClaims(world, setup);
        await Until(world, () => CarriedGrain(world, setup) is not null, 96, "actual partial pot withdrawal");
        var carried = CarriedGrain(world, setup)!;
        Assert.Equal(2, carried.Quantity);
        Assert.Null(carried.ContainerLotId);
        Assert.Equal("silo-pot-grain", carried.ProvenanceLotId);
        Assert.Equal((setup.Household, setup.Silo, 1),
            (world.Society.Inventory.GetLot("silo-pot").OwnerId, world.Society.Inventory.GetLot("silo-pot").StorageBuildingId,
                world.Society.Inventory.GetLot("silo-pot").Quantity));
        Assert.Equal(("silo-pot", 4),
            (world.Society.Inventory.GetLot("silo-pot-grain").ContainerLotId,
                world.Society.Inventory.GetLot("silo-pot-grain").Quantity));
        using var delivery = Reload(world, setup.Actor, "haul_household_stock");
        await Until(delivery, () => delivery.Society.Inventory.GetLot(carried.Id).StorageBuildingId == setup.Farmhouse,
            96, "partial pot grain delivery");
        Assert.Equal(6, delivery.Society.Inventory.Lots.Where(lot => IsFrom(lot, "silo-pot-grain")).Sum(lot => lot.Quantity));
        Assert.Equal(setup.Silo, delivery.Society.Inventory.GetLot("silo-pot").StorageBuildingId);
        AssertUnrelatedHouseCargoClaims(delivery, setup);
    }

    private sealed record Setup(PrivateWorldRuntimeState State, string Actor, string Household,
        GridPoint Point, string Farmhouse, string Silo, string House);

    private static async Task<Setup> Prepare(string seed, bool growField, bool isolateStoredGrain = false)
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer(seed);
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var silo = state.WorldContent!.Buildings.Single(definition => definition.Tags.Contains("silo"));
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in silo.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "silo-build:" + cost.ResourceId,
                cost.ResourceId, household, cost.Amount, storageBuildingId: house.InstanceId);
        if (growField) inventory = InventoryFixture.AddLot(inventory, "actual-field-seed",
            FarmFieldRules.GrainSeed, actor, 2);
        using var placing = FarmFieldTests.Restore(FarmFieldTests.WithInventory(state, inventory));
        if (growField)
        {
            Assert.True(placing.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
            await Advance(placing, 4);
            Assert.True(placing.StartFieldWork(actor, point, FarmWorkKind.Plant,
                FarmFieldRules.Grain, "actual-field-seed").Accepted);
            await Advance(placing, 5);
            Assert.True(placing.StartFieldWork(actor, point, FarmWorkKind.Tend).Accepted);
            await Advance(placing, 3);
        }
        var farmhouse = placing.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var candidates = Enumerable.Range(-3, 7).SelectMany(dy => Enumerable.Range(-3, 7)
            .Select(dx => new GridPoint(farmhouse.Position.X + dx, farmhouse.Position.Y + dy)))
            .Where(candidate => candidate.X >= 0 && candidate.Y >= 0 && candidate.X < state.Map.Width && candidate.Y < state.Map.Height)
            .OrderBy(candidate => state.Map.FootDistance(farmhouse.Position, candidate));
        var ownedBeforeBuilding = silo.BuildCosts.ToDictionary(cost => cost.ResourceId,
            cost => placing.Society.Inventory.Lots.Where(lot => lot.OwnerId == household &&
                lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity), StringComparer.Ordinal);
        BuildingPlacementResult? paid = null;
        foreach (var candidate in candidates)
        {
            var attempt = placing.PlaceBuilding("test-replenishment-silo", silo.CanonicalId, candidate, household);
            if (!attempt.Applied) continue;
            paid = attempt;
            break;
        }
        Assert.NotNull(paid);
        foreach (var cost in silo.BuildCosts)
            Assert.Equal(ownedBeforeBuilding[cost.ResourceId] - cost.Amount,
                placing.Society.Inventory.Lots.Where(lot => lot.OwnerId == household &&
                    lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity));
        state = placing.ExportState();
        inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId != farmhouse.InstanceId).ToArray(),
        };
        if (growField || isolateStoredGrain)
        {
            // Silo construction has actually spent its costs. Keep unrelated
            // camp supplies claimed for this directed delivery proof. The raw
            // Silo case also preserves its starter planting stock here: new
            // seed/produce hauls and a continuing House haul are separate work.
            foreach (var stock in inventory.Lots.Where(lot => lot.OwnerId == household &&
                         lot.CarrierId is null &&
                         lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
                         lot.ContainerLotId is null && (isolateStoredGrain || !FarmFieldRules.IsFarmStock(lot.ItemKind)) &&
                         !InventoryContainerRules.IsContainer(lot.ItemKind)).ToArray())
            {
                var quantity = PersonalEquipmentRules.AvailableQuantity(inventory, stock);
                if (quantity > 0)
                    inventory = InventoryFixture.Reserve(inventory, UnrelatedHouseCargoClaimPrefix + stock.Id,
                        household, stock.Id, quantity, "unrelated-house-supply-claim", long.MaxValue);
            }
        }
        state = FarmFieldTests.WithInventory(state, inventory);
        if (!growField) state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = paid.Position, LastDecisionContext = null, TravelCooldownTicks = 0 } : person).ToArray(),
        };
        using var verified = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        return new(verified.ExportState(), actor, household, point, farmhouse.InstanceId, paid.InstanceId, house.InstanceId);
    }

    private static int Stored(PrivateWorldRuntime world, string building) =>
        world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == building).Sum(lot => lot.Quantity);

    private static void AssertFlourDeliveryPremises(PrivateWorldRuntime world, Setup setup)
    {
        var state = world.ExportState();
        var person = state.Inhabitants.Single(item => item.InhabitantId == setup.Actor);
        var inventory = world.Society.Inventory;
        var house = world.WorldSimulation.Buildings.Single(item => item.InstanceId == setup.House);
        var farmhouse = world.WorldSimulation.Buildings.Single(item => item.InstanceId == setup.Farmhouse);
        var definition = world.WorldContent.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var capacity = BuildingStorageRules.Capacity(definition, house)!.Value;
        var stored = Stored(world, setup.House);
        var inbound = inventory.Lots.Where(lot => lot.DeliveryBuildingId == setup.House).Sum(lot => lot.Quantity);
        var freeCarry = PersonalEquipmentRules.FreeCapacity(inventory, setup.Actor, person.Equipment);
        var hasRoute = HasUnoccupiedRoute(state, setup.Actor, farmhouse.Position, house.Position);
        var detail = $"Flour delivery premise at tick {world.WorldTick}: carry-free={freeCarry}, " +
            $"House={house.Position}, capacity={capacity}, stored={stored}, inbound={inbound}, " +
            $"Farmhouse={farmhouse.Position}, actor={person.Position}, route={hasRoute}.\n" +
            "Personal/House stock: " + string.Join("; ", inventory.Lots.Where(lot =>
                lot.OwnerId == setup.Actor || lot.StorageBuildingId == setup.House || lot.DeliveryBuildingId == setup.House)
                .Select(lot => $"{lot.Id}:{lot.ItemKind}:{lot.Quantity}:owner={lot.OwnerId}:store={lot.StorageBuildingId}:delivery={lot.DeliveryBuildingId}:ground={lot.GroundPosition}:pot={lot.ContainerLotId}"));
        // These directed phases do not negotiate trades or start House jobs,
        // so no other promise can consume the measured carry/storage room.
        Assert.Empty(world.BusinessTrades);
        Assert.DoesNotContain(world.WorldSimulation.ProductionJobs, job =>
            job.BuildingInstanceId == setup.House && job.State == WorldProductionJobState.Running);
        Assert.True(freeCarry >= 1, detail);
        Assert.True(capacity - stored - inbound >= 1, detail);
        Assert.Equal(farmhouse.Position, person.Position);
        Assert.True(hasRoute, detail);
        AssertUnrelatedHouseCargoClaims(world, setup);
    }

    private static void AssertUnrelatedHouseCargoClaims(PrivateWorldRuntime world, Setup setup)
    {
        var initial = setup.State.Society.Society.Inventory;
        var claims = initial.Reservations.Where(item => item.Id.StartsWith(UnrelatedHouseCargoClaimPrefix,
            StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(claims);
        foreach (var claim in claims)
        {
            Assert.Equal(claim, world.Society.Inventory.GetReservation(claim.Id));
            var before = initial.GetLot(claim.LotId);
            var after = world.Society.Inventory.GetLot(claim.LotId);
            Assert.Equal((before.OwnerId, before.ItemKind, before.Quantity, before.StorageBuildingId,
                    before.DeliveryBuildingId, before.ContainerLotId, before.GroundPosition),
                (after.OwnerId, after.ItemKind, after.Quantity, after.StorageBuildingId,
                    after.DeliveryBuildingId, after.ContainerLotId, after.GroundPosition));
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id != before.Id && IsFrom(lot, before.Id));
        }
    }

    private static bool HasUnoccupiedRoute(PrivateWorldRuntimeState state, string actor, GridPoint source,
        GridPoint destination, int interactionRange = 0)
    {
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != actor)
            .Select(person => person.Position).ToHashSet();
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId;
        // Exact work tiles at buildings held by this household are shared.
        if (interactionRange == 0 && state.WorldSimulation!.Buildings.Any(building =>
                building.Position == destination && building.HouseholdId == household && household is not null))
            occupied.Remove(destination);
        var seen = new HashSet<GridPoint> { source };
        var pending = new Queue<GridPoint>();
        pending.Enqueue(source);
        while (pending.TryDequeue(out var point))
        {
            if (state.Map.FootDistance(point, destination) <= interactionRange) return true;
            foreach (var next in state.Map.FootNeighbors(point))
            {
                if (occupied.Contains(next) || state.Map.IsDiagonalFootStep(point, next) &&
                    (occupied.Contains(new GridPoint(next.X, point.Y)) || occupied.Contains(new GridPoint(point.X, next.Y))))
                    continue;
                if (seen.Add(next)) pending.Enqueue(next);
            }
        }
        return false;
    }

    private static string DescribeFarmStockRoutes(PrivateWorldRuntime world, Setup setup)
    {
        var state = world.ExportState();
        var inventory = world.Society.Inventory;
        var person = state.Inhabitants.Single(item => item.InhabitantId == setup.Actor);
        var social = state.Society.Society.GetInhabitant(setup.Actor);
        var household = state.Society.Society.GetHousehold(setup.Household);
        var farmhouse = world.WorldSimulation.Buildings.Single(item => item.InstanceId == setup.Farmhouse);
        var silo = world.WorldSimulation.Buildings.Single(item => item.InstanceId == setup.Silo);
        var camp = state.Map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
            world.WorldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == "first-town-warehouse")?.Position ??
            state.Towns?.FirstOrDefault(item => item.OriginSite is not null)?.OriginSite ??
            state.Map.Resources.First(item => item.Id == "berry-patch").Position;
        int Room(string building) => Math.Max(0, FarmFieldRules.FarmStorageCapacity - inventory.Lots
            .Where(lot => lot.StorageBuildingId == building || lot.DeliveryBuildingId == building).Sum(lot => lot.Quantity));
        var freeCarry = PersonalEquipmentRules.FreeCapacity(inventory, setup.Actor, person.Equipment);
        var roots = inventory.Lots.Where(lot => lot.OwnerId == setup.Household && lot.ContainerLotId is null &&
                lot.StorageBuildingId is null && lot.DeliveryBuildingId is null && FarmFieldRules.IsFarmStock(lot.ItemKind))
            .OrderBy(lot => lot.GroundPosition is not null ? 0 : 1).ThenBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        var firstFitting = roots.FirstOrDefault(lot => PersonalEquipmentRules.AvailableQuantity(inventory, lot) > 0 &&
            freeCarry > 0 && (Room(setup.Farmhouse) > 0 || Room(setup.Silo) > 0));
        var loose = roots.Select(lot =>
        {
            var source = lot.GroundPosition is { } ground ? new GridPoint(ground.X, ground.Y) : camp;
            var range = lot.GroundPosition is null ? 1 : 0;
            var destination = lot.ItemKind == FarmFieldRules.Grain
                ? Room(setup.Farmhouse) > 0 ? farmhouse : silo
                : Room(setup.Silo) > 0 ? silo : farmhouse;
            return $"{lot.Id}:{lot.ItemKind}:qty={lot.Quantity}:available={PersonalEquipmentRules.AvailableQuantity(inventory, lot)}:" +
                $"source={source}:range={range}:in-range={state.Map.FootDistance(person.Position, source) <= range}:" +
                $"pickup-route={HasUnoccupiedRoute(state, setup.Actor, person.Position, source, range)}:" +
                $"destination={destination.InstanceId}@{destination.Position}:delivery-route={HasUnoccupiedRoute(state, setup.Actor, source, destination.Position)}";
        });
        var titles = (state.TownLandTitles ?? []).Where(title => title.Tiles.Contains(silo.Position) ||
            title.Tiles.Contains(farmhouse.Position)).Select(title => title.Id + ":" + title.TownId);
        var rights = (state.HouseholdLandUseRights ?? []).Where(right => right.HouseholdId == setup.Household &&
                (right.Tiles.Contains(silo.Position) || right.Tiles.Contains(farmhouse.Position)))
            .Select(right => $"{right.Id}:{right.GrantSource}:end={right.AgreedEndTick}");
        return $"tick={world.WorldTick}, actor={setup.Actor}@{person.Position}, age={social.AgeBand}, " +
            $"HH={social.HouseholdId}/{household.Name}, free-carry={freeCarry}, camp={camp}, " +
            $"FH={farmhouse.Position}/owner={farmhouse.HouseholdId}/capacity={FarmFieldRules.FarmStorageCapacity}/room={Room(setup.Farmhouse)}, " +
            $"Silo={silo.Position}/owner={silo.HouseholdId}/capacity={FarmFieldRules.FarmStorageCapacity}/room={Room(setup.Silo)}, trades={world.BusinessTrades.Count}, " +
            $"Silo-pickup-route={HasUnoccupiedRoute(state, setup.Actor, person.Position, silo.Position)}, " +
            $"Silo-FH-route={HasUnoccupiedRoute(state, setup.Actor, silo.Position, farmhouse.Position)}, " +
            $"first-fitting-loose-root={firstFitting?.Id}, titles={string.Join(",", titles)}, rights={string.Join(",", rights)}.\n" +
            "Loose farm roots: " + string.Join("; ", loose) + "\nStored farm stock: " +
            string.Join("; ", inventory.Lots.Where(lot => lot.StorageBuildingId == setup.Farmhouse || lot.StorageBuildingId == setup.Silo)
                .Select(lot => $"{lot.Id}:{lot.ItemKind}:qty={lot.Quantity}:available={PersonalEquipmentRules.AvailableQuantity(inventory, lot)}:store={lot.StorageBuildingId}:pot={lot.ContainerLotId}")) +
            "\nFH recipes: " + string.Join("; ", world.WorldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == farmhouse.DefinitionId)
                .Select(recipe => recipe.CanonicalId + ":" + string.Join(",", recipe.Inputs.Select(input => input.ResourceId + "=" + input.Amount)))) +
            "\nPersonal stock: " + string.Join("; ", inventory.Lots.Where(lot => lot.OwnerId == setup.Actor)
                .Select(lot => $"{lot.Id}:{lot.ItemKind}:{lot.Quantity}:store={lot.StorageBuildingId}:delivery={lot.DeliveryBuildingId}:ground={lot.GroundPosition}"));
    }

    private static InventoryLot? CarriedGrain(PrivateWorldRuntime world, Setup setup) =>
        world.Society.Inventory.Lots.SingleOrDefault(lot => lot.OwnerId == setup.Actor && lot.ItemKind == FarmFieldRules.Grain &&
            lot.DeliveryBuildingId == setup.Farmhouse && lot.ContainerLotId is null);

    private static bool IsFrom(InventoryLot lot, string root) => lot.Id == root ||
        lot.Id.StartsWith(root + "#transfer:", StringComparison.Ordinal) || lot.ProvenanceLotId == root;

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, params string[] choices) =>
        PrivateWorldRuntime.Restore(state, id => new Choices(id == actor ? choices : ["safe_idle"]));

    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world, string actor, params string[] choices) =>
        Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), actor, choices);

    private static async Task Until(PrivateWorldRuntime world, Func<bool> done, int ticks, string phase,
        Func<string>? diagnostics = null)
    {
        for (var tick = 0; tick < ticks && !done(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done(), $"{phase} did not finish within {ticks} actual ticks (world tick {world.WorldTick}).\n" +
            "People: " + string.Join("; ", world.Inhabitants.Select(person =>
                $"{person.InhabitantId}@{person.Position}, hunger={person.HungerBasisPoints}, project={person.Project}, context={person.LastDecisionContext}")) +
            "\nGrain/flour/delivery stock: " + string.Join("; ", world.Society.Inventory.Lots.Where(lot =>
                lot.ItemKind is FarmFieldRules.Grain or "flour" || lot.DeliveryBuildingId is not null).Select(lot =>
                $"{lot.Id}:{lot.ItemKind}:{lot.Quantity}:owner={lot.OwnerId}:store={lot.StorageBuildingId}:delivery={lot.DeliveryBuildingId}:ground={lot.GroundPosition}:pot={lot.ContainerLotId}")) +
            "\nRecent events: " + string.Join("; ", world.ExportState().Events.TakeLast(16).Select(item =>
                $"{item.WorldTick}:{item.Kind}:{item.Detail}")) +
            (diagnostics is null ? string.Empty : "\n" + diagnostics()));
        world.Validate();
    }

    private static async Task Advance(PrivateWorldRuntime world, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Validate();
    }

    private sealed class Choices(params string[] ids) : IDecisionProvider
    {
        public List<string> Offered { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates.Select(candidate => candidate.Id));
            var selected = ids.Select(id => request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == id))
                .FirstOrDefault(candidate => candidate is not null) ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}
