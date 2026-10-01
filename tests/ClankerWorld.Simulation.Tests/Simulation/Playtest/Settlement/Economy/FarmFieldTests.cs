using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmFieldTests
{
    [Fact]
    public async Task OrdinaryChooserExpandsAndReplantsGeneratedFieldsThroughARealFoodShortageAndReload()
    {
        var (state, _, household, _) = PreparedFarmer("field-normal-cycle");
        // The household has eaten its starter rations. Keep all real planting
        // stock and let ordinary choices arrange every field operation.
        state = FeedHouseholdFromAvailableStock(state, household);
        var initialSeeds = state.Society.Society.Inventory.Lots
            .Where(lot => lot.ItemKind == "cultivated_green_seed").Sum(lot => lot.Quantity);
        using var first = PrivateWorldRuntime.Restore(state, _ => new DeterministicDecisionProvider());
        for (var tick = 0; tick < 1_800 && !first.Fields.Any(field => field.HouseholdId == household && field.Cycle > 0); tick++)
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(first.Fields, field => field.HouseholdId == household && field.Cycle > 0);
        Assert.Contains(first.ExportState().Events, item => item.Kind == "field_prepared");
        Assert.Contains(first.ExportState().Events, item => item.Kind == "field_tended");
        // Eating the available produce creates a real inventory shortage. The
        // separately reserved planting stock must remain available for recovery.
        state = FeedHouseholdFromAvailableStock(first.ExportState(), household);
        var second = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new DeterministicDecisionProvider());
        try
        {
        for (var tick = 0; tick < 1_800 && !second.Fields.Any(field => field.HouseholdId == household && field.Cycle >= 2); tick++)
        {
            var step = await second.AdvanceOneTickAsync();
            Assert.True(step.Advanced);
            if (!step.Events.Any(item => item.Kind == "field_harvested") ||
                second.Fields.Any(field => field.HouseholdId == household && field.Cycle >= 2)) continue;
            // Other fields may finish while this one is being replanted.
            // Keep the demand scenario going by accounting for that produce
            // too; leave every reserved planting unit intact.
            state = FeedHouseholdFromAvailableStock(second.ExportState(), household);
            second.Dispose();
            second = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
                _ => new DeterministicDecisionProvider());
        }
        Assert.True(second.Fields.Any(field => field.HouseholdId == household && field.Cycle >= 2), string.Join("\n",
            second.Fields.Select(field => $"{field.Position} {field.Crop} {field.Stage} cycle {field.Cycle}")
                .Concat(second.ExportState().Events.TakeLast(30).Select(item => $"{item.Kind}: {item.Detail}"))));
        var repeated = second.Fields.First(field => field.HouseholdId == household && field.Cycle >= 2);
        Assert.Equal(FarmFieldStage.Harvested, repeated.Stage);
        Assert.NotNull(repeated.ReplantingReservationId);
        Assert.Contains(second.Society.Inventory.Reservations, reservation => reservation.Id == repeated.ReplantingReservationId &&
            reservation.Quantity == 1 && reservation.State == InventoryReservationState.Reserved);
        var events = second.ExportState().Events;
        var plantedGreens = events.Count(item => item.Kind == "field_planted" && item.Detail.EndsWith(":cultivated_greens", StringComparison.Ordinal));
        var harvestedGreens = events.Count(item => item.Kind == "field_harvested" && item.Detail.EndsWith(":cultivated_greens", StringComparison.Ordinal));
        Assert.True(plantedGreens >= 2);
        Assert.Equal(initialSeeds + harvestedGreens * 2 - plantedGreens,
            second.Society.Inventory.Lots.Where(lot => lot.ItemKind == "cultivated_green_seed").Sum(lot => lot.Quantity));
        Assert.Contains(second.Fields, field => field.HouseholdId == household && field.Position != repeated.Position);
        var snapshot = new OwnerWorldObservationStore(second).GetSnapshot();
        Assert.Contains(snapshot.Fields!, field => field.HouseholdId == household);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(second.ExportState())));
        Assert.Equal(second.Fields, restored.Fields);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(second.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        finally { second.Dispose(); }
    }

    internal static PrivateWorldRuntimeState FeedHouseholdFromAvailableStock(PrivateWorldRuntimeState state, string household)
    {
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == household &&
            lot.ItemKind is "food" or "berries" or "wild_greens" or "cultivated_greens" or "fruit" or "grain" or "potatoes").ToArray())
        {
            var available = lot.FreshnessBasisPoints == 0 || lot.ConditionBasisPoints == 0 ? 0 : lot.Quantity - inventory.Reservations
                .Where(reservation => reservation.LotId == lot.Id && reservation.State is
                    InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed)
                .Sum(reservation => reservation.Quantity);
            if (available == 0) continue;
            var reservationId = $"test-household-meal:{inventory.WorldTick}:{lot.Id}";
            inventory = InventoryFixture.ConsumeReservation(InventoryFixture.Reserve(inventory, reservationId, household,
                lot.Id, available, "household_meals", checked(inventory.WorldTick + 1)), reservationId);
        }
        return WithInventory(state, inventory);
    }

    [Fact]
    public async Task ChangingCropsKeepsThePreviousReserveUntilPlantingCompletesThenReleasesItAcrossReload()
    {
        var (state, actor, household, point) = await ReadyFarmer("field-crop-change");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "old-planting-stock", "grain_seed", household, 2,
            groundPosition: new(point.X, point.Y));
        inventory = InventoryFixture.Reserve(inventory, "old-replanting", household, "old-planting-stock", 1,
            "field_replanting", long.MaxValue);
        inventory = InventoryFixture.AddLot(inventory, "new-crop-seed", "cultivated_green_seed", actor, 2);
        state = WithInventory(state, inventory) with
        {
            Fields = [state.Fields!.Single() with { Stage = FarmFieldStage.Harvested, Cycle = 1,
                ReplantingReservationId = "old-replanting" }],
        };
        using var starting = Restore(state);
        Assert.True(starting.StartFieldWork(actor, point, FarmWorkKind.Plant, "cultivated_greens", "new-crop-seed").Accepted);
        await Advance(starting, 2);
        Assert.Equal(InventoryReservationState.Reserved, starting.Society.Inventory.GetReservation("old-replanting").State);
        using var completing = Reload(starting);
        await Advance(completing, 2);
        Assert.Equal("cultivated_greens", Assert.Single(completing.Fields).Crop);
        Assert.Null(Assert.Single(completing.Fields).ReplantingReservationId);
        Assert.Equal(2, completing.Society.Inventory.GetLot("old-planting-stock").Quantity);
        Assert.Equal(1, completing.Society.Inventory.GetLot("new-crop-seed").Quantity);
        Assert.DoesNotContain(completing.Society.Inventory.Reservations, reservation => reservation.Id == "old-replanting" &&
            reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed);
        Assert.Contains(completing.ExportState().Events, item => item.Kind == "field_planted" &&
            item.Detail.EndsWith(":cultivated_greens", StringComparison.Ordinal));
        using var restored = Reload(completing);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(completing.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(SocietyAgeBand.Infant, false)]
    [InlineData(SocietyAgeBand.Child, false)]
    [InlineData(SocietyAgeBand.Adult, true)]
    [InlineData(SocietyAgeBand.Elder, true)]
    public async Task FieldWorkRespectsAgeBeforeAndAfterReload(SocietyAgeBand age, bool allowed)
    {
        var (state, actor, _, point) = PreparedFarmer("field-age");
        var society = state.Society.Society;
        var years = age switch
        {
            SocietyAgeBand.Infant => 0,
            SocietyAgeBand.Child => society.Config.DayLifecycle?.ChildStartDay ?? society.Config.InfantYears,
            SocietyAgeBand.Adult => society.Config.DayLifecycle?.AdultStartDay ?? society.Config.AdultYears,
            _ => society.Config.DayLifecycle?.ElderStartDay ?? society.Config.ElderYears,
        };
        var birth = society.LifeTickAt(society.WorldTick) - years * society.Config.TicksPerLifecycleAge;
        state = state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        AgeBand = age, BirthTick = birth, BirthLifeTick = society.LifeClock is null ? null : birth,
                        LastLifecycleYearChecked = years, CurrentRole = SocietyWorkRole.Unassigned,
                    } : person).ToArray(),
                },
            },
        };
        using var world = Restore(state);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(allowed, world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        if (!allowed)
        {
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            using var reloaded = Reload(world);
            Assert.False(reloaded.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
        else
        {
            await Advance(world, 3);
            using var reloaded = Reload(world);
            await Advance(reloaded, 5);
            Assert.Equal(FarmFieldStage.Prepared, Assert.Single(reloaded.Fields).Stage);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DeathCancelsUnfinishedFieldWorkBeforeItCompletesAndKeepsCompletedHarvest(bool harvest, bool completed)
    {
        var (state, actor, household, point) = await ReadyFarmer("field-death");
        if (!harvest) state = state with { Fields = [new(point, household, FarmFieldStage.Prepared)] };
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "death-planting", "grain_seed", actor, 2));
        using var working = Restore(state);
        Assert.True(working.StartFieldWork(actor, point, harvest ? FarmWorkKind.Harvest : FarmWorkKind.Plant,
            harvest ? null : "grain", harvest ? null : "death-planting").Accepted);
        await Advance(working, completed ? 4 : 3);
        var saved = working.ExportState();
        var outputs = saved.Society.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal))
            .Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.GroundPosition)).ToArray();
        var plantingTotal = saved.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain_seed").Sum(lot => lot.Quantity);
        saved = DyingWorker(saved, actor);
        using var dying = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        await Advance(dying, 1);
        Assert.Equal(SocietyInhabitantStatus.Dead, dying.Society.GetInhabitant(actor).Status);
        var field = Assert.Single(dying.Fields);
        Assert.Null(field.Work);
        Assert.Equal(completed ? FarmFieldStage.Harvested : harvest ? FarmFieldStage.Ready : FarmFieldStage.Prepared, field.Stage);
        Assert.Equal(completed ? 1 : 0, field.Cycle);
        Assert.Equal(outputs, dying.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal))
            .Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.GroundPosition)).ToArray());
        Assert.Equal(plantingTotal, dying.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain_seed").Sum(lot => lot.Quantity));
        if (!harvest)
            Assert.DoesNotContain(dying.Society.Inventory.Reservations, reservation => reservation.LotId == "death-planting" &&
                reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed);
        using var reloaded = Reload(dying);
        await Advance(reloaded, 2);
        Assert.Equal(field, Assert.Single(reloaded.Fields));
    }

    private static PrivateWorldRuntimeState DyingWorker(PrivateWorldRuntimeState state, string actor)
    {
        var society = state.Society.Society;
        var nextLifeTick = society.Config.TicksPerLifecycleAge - 1;
        var delta = nextLifeTick - society.LifeTickAt(society.WorldTick);
        var years = society.Config.DayLifecycle!.MaximumDay - 1;
        return state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    LifeClock = new SocietyLifeClock(society.LifeClock?.Rate ?? 1, society.WorldTick, nextLifeTick),
                    Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        BirthLifeTick = nextLifeTick + 1 - (years + 1) * society.Config.TicksPerLifecycleAge,
                        AgeBand = SocietyAgeBand.Elder, LastLifecycleYearChecked = years,
                    } : person with { BirthLifeTick = (person.BirthLifeTick ?? person.BirthTick) + delta }).ToArray(),
                },
            },
        };
    }

    internal static async Task<(PrivateWorldRuntimeState State, string Actor, string Household, GridPoint Point)> ReadyFarmer(string seed)
    {
        var (state, actor, household, point) = PreparedFarmer(seed);
        using var world = Restore(state);
        await Advance(world, 1);
        return (world.ExportState() with { Fields = [new(point, household, FarmFieldStage.Ready, "grain", ReadyTick: 1, Tended: true)] },
            actor, household, point);
    }

    [Theory]
    [InlineData("grain", "grain_seed")]
    [InlineData("potatoes", "potatoes")]
    [InlineData("cultivated_greens", "cultivated_green_seed")]
    public async Task PhysicalCropCycleSurvivesReloadAndLeavesOwnedHarvestAndPlantingReserveOnTheTile(string crop, string seedKind)
    {
        var (state, actor, household, point) = PreparedFarmer("field-cycle-" + crop);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "carried-planting", seedKind, actor, 2);
        state = WithInventory(state, inventory);
        using var tilling = Restore(state);
        Assert.True(tilling.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await Advance(tilling, 3);
        Assert.Equal(FarmFieldStage.Preparing, Assert.Single(tilling.Fields).Stage);
        using var prepared = Reload(tilling);
        await Advance(prepared, 5);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(prepared.Fields).Stage);
        Assert.True(prepared.StartFieldWork(actor, point, FarmWorkKind.Plant, crop, "carried-planting").Accepted);
        await Advance(prepared, 2);
        using var planted = Reload(prepared);
        await Advance(planted, 2);
        Assert.Equal(FarmFieldStage.Planted, Assert.Single(planted.Fields).Stage);
        Assert.Equal(1, planted.Society.Inventory.GetLot("carried-planting").Quantity);
        using var growing = Reload(planted);
        await Advance(growing, 1);
        Assert.Equal(FarmFieldStage.Growing, Assert.Single(growing.Fields).Stage);
        Assert.True(growing.StartFieldWork(actor, point, FarmWorkKind.Tend).Accepted);
        await Advance(growing, 4);
        var ticksToReady = Assert.Single(growing.Fields).ReadyTick - growing.WorldTick;
        await Advance(growing, checked((int)Math.Max(1, ticksToReady)));
        Assert.Equal(FarmFieldStage.Ready, Assert.Single(growing.Fields).Stage);
        using var harvesting = Reload(growing);
        Assert.True(harvesting.StartFieldWork(actor, point, FarmWorkKind.Harvest).Accepted);
        await Advance(harvesting, 2);
        using var harvested = Reload(harvesting);
        await Advance(harvested, 2);
        var field = Assert.Single(harvested.Fields);
        Assert.Equal(FarmFieldStage.Harvested, field.Stage);
        Assert.Equal(1, field.Cycle);
        var lots = harvested.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal)).ToArray();
        Assert.All(lots, lot =>
        {
            Assert.Equal(household, lot.OwnerId);
            Assert.Equal(new InventoryGroundPosition(point.X, point.Y), lot.GroundPosition);
            Assert.Null(lot.StorageBuildingId);
            Assert.Null(lot.DeliveryBuildingId);
        });
        Assert.InRange(Assert.Single(lots, lot => lot.ItemKind == crop).Quantity, 2, 11);
        var reserve = Assert.Single(harvested.Society.Inventory.Reservations, item => item.Id == field.ReplantingReservationId);
        Assert.Equal(InventoryReservationState.Reserved, reserve.State);
        Assert.Equal(1, reserve.Quantity);
        Assert.Equal(seedKind, harvested.Society.Inventory.GetLot(reserve.LotId).ItemKind);
        var visible = new OwnerWorldObservationStore(harvested).GetSnapshot();
        Assert.Equal("harvested", Assert.Single(visible.Fields).Stage);
        Assert.All(visible.GroundStocks.Where(stock => stock.Position.X == point.X && stock.Position.Y == point.Y),
            stock => Assert.Equal(household, stock.OwnerId));
        using var final = Reload(harvested);
        await Advance(final, 2);
        Assert.Equal(lots.Select(lot => (lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity, lot.GroundPosition, lot.StorageBuildingId)),
            final.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("field-", StringComparison.Ordinal))
                .Select(lot => (lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity, lot.GroundPosition, lot.StorageBuildingId)));
        Assert.Equal(field, Assert.Single(final.Fields));
    }

    [Fact]
    public async Task RefusedOrInterruptedWorkCannotConsumeRemoteOrForeignSeedsOrDuplicateAField()
    {
        var (state, actor, household, point) = PreparedFarmer("field-refusals");
        var foreign = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "remote-seed", "grain_seed", household, 2);
        inventory = InventoryFixture.AddLot(inventory, "foreign-seed", "grain_seed", foreign, 2);
        inventory = InventoryFixture.AddLot(inventory, "own-seed", "grain_seed", actor, 2);
        using var world = Restore(WithInventory(state, inventory));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.StartFieldWork(actor, new(point.X + 1, point.Y), FarmWorkKind.Till).Accepted);
        Assert.False(world.StartFieldWork(foreign, point, FarmWorkKind.Till).Accepted);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        await Advance(world, 8);
        before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", "remote-seed").Accepted);
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", "foreign-seed").Accepted);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", "own-seed").Accepted);
        await Advance(world, 2);
        state = world.ExportState() with
        {
            Inhabitants = world.ExportState().Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = AwayFromField(state) } : person).ToArray(),
        };
        using var interrupted = Restore(state);
        await Advance(interrupted, 1);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(interrupted.Fields).Stage);
        Assert.Null(Assert.Single(interrupted.Fields).Work);
        Assert.Equal(2, interrupted.Society.Inventory.GetLot("own-seed").Quantity);
        Assert.Contains(interrupted.Society.Inventory.Reservations, item =>
            item.Purpose == "field_planting" && item.State == InventoryReservationState.Released);
        using var reloaded = Reload(interrupted);
        await Advance(reloaded, 2);
        Assert.Equal(2, reloaded.Society.Inventory.GetLot("own-seed").Quantity);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(reloaded.Fields).Stage);
    }

    private static GridPoint AwayFromField(PrivateWorldRuntimeState state) => state.Map.Tiles
        .First(tile => state.Map.IsBuildable(tile.Position) && !state.Inhabitants.Any(person => person.Position == tile.Position)).Position;

    [Fact]
    public async Task HarvestIsPickedUpWhereItLiesAndStorageCapacityIncludesCarriedDeliveriesAcrossReload()
    {
        var (state, actor, household, point) = PreparedFarmer("field-hauling-capacity");
        const string farmhouse = "first-town-farmhouse";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "nearly-full-farm", "grain", household, 94,
            storageBuildingId: farmhouse);
        inventory = InventoryFixture.AddLot(inventory, "ground-grain", "grain", household, 9,
            groundPosition: new(point.X, point.Y));
        using var collecting = PrivateWorldRuntime.Restore(WithInventory(state, inventory),
            id => new ChooseProvider(id == actor ? "haul_farm_grain" : "safe_idle"));
        for (var tick = 0; tick < 12 && !collecting.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        var carried = Assert.Single(collecting.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.DeliveryBuildingId == farmhouse);
        Assert.Equal(2, carried.Quantity);
        Assert.Null(carried.GroundPosition);
        Assert.Null(carried.StorageBuildingId);
        Assert.Equal(7, collecting.Society.Inventory.GetLot("ground-grain").Quantity);
        Assert.Equal(new InventoryGroundPosition(point.X, point.Y), collecting.Society.Inventory.GetLot("ground-grain").GroundPosition);
        Assert.Equal(94, new OwnerWorldObservationStore(collecting).GetSnapshot().PlacedBuildings.Single(item => item.InstanceId == farmhouse)
            .StoredItems!.Single(item => item.Kind == "grain").Quantity);
        using var delivering = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collecting.ExportState())),
            id => new ChooseProvider(id == actor ? "haul_household_stock" : "safe_idle"));
        for (var tick = 0; tick < 40 && delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId != farmhouse; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(farmhouse, delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        Assert.Equal(96, delivering.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == farmhouse).Sum(lot => lot.Quantity));
        Assert.Equal(103, delivering.Society.Inventory.Lots.Where(lot => lot.Id is "nearly-full-farm" or "ground-grain" || lot.ProvenanceLotId == "ground-grain")
            .Sum(lot => lot.Quantity));
        delivering.Validate();
    }

    internal static (PrivateWorldRuntimeState State, string Actor, string Household, GridPoint Point) PreparedFarmer(string seed)
    {
        var state = GeographyGeneratorTests.StartedGeneratedWorld(new(seed, WorldSizePreset.Small));
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var household = farmhouse.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        using var world = Restore(state);
        var occupied = world.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            world.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(world.RoadTiles)
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var fertility = new LandFertility(state.Map, seed);
        var point = state.Map.Tiles.Select(tile => tile.Position).Where(point => fertility.CanFarm(point) &&
            !occupied.Contains(point) && !state.Inhabitants.Any(person => person.Position == point))
            .OrderBy(point => state.Map.FootDistance(farmhouse.Position, point)).First();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "carried-hoe", "wooden_hoe", actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = point, HungerBasisPoints = 10_000 } : person).ToArray(),
        };
        return (state, actor, household, point);
    }

    internal static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    {
        Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
    };
    internal static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
    internal static IDecisionProvider HaulProvider() => new ChooseProvider("haul_farm_grain", "haul_household_stock");
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world) => Restore(PrivateWorldRuntimeCodec.Decode(
        PrivateWorldRuntimeCodec.Encode(world.ExportState())));
    private static async Task Advance(PrivateWorldRuntime world, int count)
    {
        for (var tick = 0; tick < count; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Validate();
    }
    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")] },
            }, cancellationToken);
    }
    private sealed class ChooseProvider(params string[] ids) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [ids.Select(id => request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == id)).FirstOrDefault(candidate => candidate is not null)
                    ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")] },
            }, cancellationToken);
    }
}
