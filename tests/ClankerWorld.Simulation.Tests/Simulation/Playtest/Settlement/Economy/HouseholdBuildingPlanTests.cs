using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

/// <summary>A household plans only buildings it needs for itself, once it has the materials in hand.</summary>
public sealed class HouseholdBuildingPlanTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";
    [Fact]
    public async Task PrivateHouseholdPlansCannotSpendTownOwnedStone()
    {
        var idle = new ActionCoverageRecorder(chooseIdle: true);
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => idle);
        var state = setup.ExportState();
        Assert.DoesNotContain(state.Society.Society.Inventory.Lots, lot => lot.ItemKind == "stone" && lot.Quantity > 0);
        var town = Assert.Single(setup.Towns);
        var publicStock = InventoryFixture.AddLot(state.Society.Society.Inventory, "town-stone", "stone", town.Id, 4);
        var withoutHouseholdStone = WithInventory(state, publicStock);

        using (var withoutStone = PrivateWorldRuntime.Restore(withoutHouseholdStone, _ => idle))
        {
            for (var tick = 0; tick < 40; tick++)
                Assert.True((await withoutStone.AdvanceOneTickAsync()).Advanced);
            var families = idle.FamiliesOffered(withoutStone.WorldContent);
            var privatePlans = withoutStone.WorldContent.Buildings.Where(definition => HouseholdBuildingKinds.KindOf(definition) is not null)
                .Select(definition => "building:" + definition.LocalId).ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain(families, privatePlans.Contains);
            // Shared Town projects have their own acquisition rules; they do not
            // grant a household the Town's actual stone for its private Smith.
            foreach (var entry in idle.OfferedByAgent)
                foreach (var candidate in entry.Value.Keys.Where(id => TownConstructionCandidateIds.TryParse(id, out var selection) && selection.IsBuilding))
                {
                    Assert.True(TownConstructionCandidateIds.TryParse(candidate, out var selection));
                    var definition = withoutStone.WorldContent.Buildings.Single(item => item.CanonicalId == selection.DefinitionId);
                    Assert.Contains("communal", definition.Tags);
                    var resident = withoutStone.Society.GetInhabitant(entry.Key);
                    Assert.Contains(entry.Key, town.ResidentIds);
                    Assert.Contains(resident.AgeBand, new[] { SocietyAgeBand.Adult, SocietyAgeBand.Elder });
                }
            Assert.Equal(town.Id, withoutStone.Society.Inventory.GetLot("town-stone").OwnerId);
            Assert.Equal(4, withoutStone.Society.Inventory.GetLot("town-stone").Quantity);
            Assert.Contains(FamiliesForHousehold(withoutStone, idle, Alpha), family => family == "gather_building_material");
        }
    }

    [Fact]
    public async Task OnlyTheFarmhouseHouseholdPlansASiloAndOnlyBesideItsFarmhouse()
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = setup.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "alpha-stone", "stone", Alpha, 2,
            storageBuildingId: "first-town-house-a");
        inventory = InventoryFixture.AddLot(inventory, "beta-stone", "stone", Beta, 2, storageBuildingId: "first-town-house-b");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(state, _ => recorder);
        for (var tick = 0; tick < 40; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var silo = world.WorldContent.Buildings.Single(item => item.LocalId == "silo-1x1");
        var farmhouse = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var siloSites = recorder.OfferedByAgent
            .Where(entry => world.Society.GetInhabitant(entry.Key).HouseholdId == Alpha)
            .SelectMany(entry => entry.Value.Keys)
            .Where(id => TownConstructionCandidateIds.TryParse(id, out var selection) &&
                selection.IsBuilding && selection.DefinitionId == silo.CanonicalId)
            .Select(id => TownConstructionCandidateIds.TryParse(id, out var selection) ? selection.SitePosition!.Value : default)
            .ToArray();
        Assert.NotEmpty(siloSites);
        // Near means within two tiles, counting diagonals; this Farmhouse has
        // free corners, so a touching site is offered too.
        int Distance(GridPoint site) => Math.Max(Math.Abs(site.X - farmhouse.Position.X), Math.Abs(site.Y - farmhouse.Position.Y));
        Assert.All(siloSites, site => Assert.InRange(Distance(site), 1, TownLayoutContext.NeighborReach));
        Assert.Contains(siloSites, site => Distance(site) == 1);
        Assert.DoesNotContain("building:silo-1x1", FamiliesForHousehold(world, recorder, Beta));
    }

    [Fact]
    public async Task HouseholdHarvestMustBeCarriedFromItsFieldIntoItsSilo()
    {
        var harvest = await PrepareSiloHarvest();
        using var world = harvest.World;
        var grainId = harvest.JobId + ":crop";
        var grainQuantity = world.Society.Inventory.GetLot(grainId).Quantity;
        var quantities = BatchLots(world, harvest.JobId).GroupBy(lot => lot.ItemKind)
            .ToDictionary(group => group.Key, group => group.Sum(lot => lot.Quantity), StringComparer.Ordinal);
        using var carrying = LoadCarrying(world.ExportState(), harvest.Farmer);
        for (var tick = 0; tick < 16 && !BatchLots(carrying, harvest.JobId).Any(lot =>
            lot.ItemKind == "grain" && lot.OwnerId == harvest.Farmer && lot.DeliveryBuildingId == "alpha-silo"); tick++)
            Assert.True((await carrying.AdvanceOneTickAsync()).Advanced);
        var load = Assert.Single(BatchLots(carrying, harvest.JobId), lot => lot.ItemKind == "grain" &&
            lot.OwnerId == harvest.Farmer && lot.DeliveryBuildingId == "alpha-silo");
        Assert.Equal(harvest.Field, carrying.Inhabitants.Single(person => person.InhabitantId == harvest.Farmer).Position);
        Assert.Null(load.GroundPosition);
        Assert.Null(load.StorageBuildingId);
        Assert.InRange(load.Quantity, 1, 4);
        Assert.Equal(grainQuantity, BatchLots(carrying, harvest.JobId).Where(lot => lot.ItemKind == "grain").Sum(lot => lot.Quantity));
        using var delivery = await CarryHarvestBatchesIntoSilo(carrying.ExportState(),
            harvest.Farmer, harvest.JobId, harvest.Field, 120);
        var collected = delivery.World;
        var replay = delivery.Replay;
        var walked = delivery.Walked;
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(collected.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.True(walked.Count > 1);
        Assert.Contains(collected.WorldSimulation.Buildings.Single(building => building.InstanceId == "alpha-silo").Position, walked);
        Assert.All(BatchLots(collected, harvest.JobId).Where(lot => Available(collected, lot) > 0), lot =>
        {
            Assert.Equal(Alpha, lot.OwnerId);
            Assert.Equal("alpha-silo", lot.StorageBuildingId);
            Assert.Null(lot.GroundPosition);
            Assert.Null(lot.DeliveryBuildingId);
        });
        var field = collected.Fields.Single(field => field.Position == harvest.Field);
        var replant = collected.Society.Inventory.GetReservation(field.ReplantingReservationId!);
        Assert.Equal(InventoryReservationState.Reserved, replant.State);
        Assert.Equal(1, replant.Quantity);
        var reservedSeed = collected.Society.Inventory.GetLot(replant.LotId);
        Assert.Equal("grain_seed", reservedSeed.ItemKind);
        Assert.Equal(1, reservedSeed.Quantity);
        Assert.Equal(Alpha, reservedSeed.OwnerId);
        Assert.Equal(new InventoryGroundPosition(harvest.Field.X, harvest.Field.Y), reservedSeed.GroundPosition);
        Assert.Null(reservedSeed.StorageBuildingId);
        foreach (var quantity in quantities)
            Assert.Equal(quantity.Value, BatchLots(collected, harvest.JobId).Where(lot => lot.ItemKind == quantity.Key).Sum(lot => lot.Quantity));
        collected.Validate();

        var farmhouse = collected.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        using var hauling = LoadChoices(collected.ExportState(), harvest.Farmer, "haul_farm_grain", "haul_household_stock");
        for (var tick = 0; tick < 96 && !BatchLots(hauling, harvest.JobId).Any(lot => lot.ItemKind == "grain" &&
            lot.StorageBuildingId == farmhouse.InstanceId); tick++)
            Assert.True((await hauling.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(BatchLots(hauling, harvest.JobId), lot => lot.ItemKind == "grain" && lot.OwnerId == Alpha &&
            lot.StorageBuildingId == farmhouse.InstanceId && lot.GroundPosition is null);
        Assert.Equal(grainQuantity, BatchLots(hauling, harvest.JobId).Where(lot => lot.ItemKind == "grain").Sum(lot => lot.Quantity));
        var mill = hauling.WorldContent.Recipes.Single(recipe => recipe.LocalId == "mill-grain");
        using var milling = LoadChoices(hauling.ExportState(), harvest.Farmer, "build:recipe:" + mill.CanonicalId);
        for (var tick = 0; tick < 96 && !milling.WorldSimulation.ProductionJobs.Any(job =>
            job.RecipeId == mill.CanonicalId && job.State == WorldProductionJobState.Completed); tick++)
            Assert.True((await milling.AdvanceOneTickAsync()).Advanced);
        var completed = Assert.Single(milling.WorldSimulation.ProductionJobs, job => job.RecipeId == mill.CanonicalId && job.State == WorldProductionJobState.Completed);
        Assert.Equal(farmhouse.InstanceId, completed.BuildingInstanceId);
        Assert.All(completed.InputReservationIds, id =>
        {
            var reservation = milling.Society.Inventory.GetReservation(id);
            Assert.StartsWith(grainId, reservation.LotId, StringComparison.Ordinal);
            Assert.Equal(InventoryReservationState.Completed, reservation.State);
        });
        var flour = milling.Society.Inventory.GetLot(completed.JobId + ":output:00");
        Assert.Equal("flour", flour.ItemKind);
        Assert.Equal(1, flour.Quantity);
        Assert.Equal(farmhouse.InstanceId, flour.StorageBuildingId);
        Assert.Equal(grainQuantity - 1, BatchLots(milling, harvest.JobId).Where(lot => lot.ItemKind == "grain").Sum(lot => lot.Quantity));
        milling.Validate();
    }

    private sealed record SiloDelivery(PrivateWorldRuntime World, PrivateWorldRuntime Replay,
        HashSet<GridPoint> Walked) : IDisposable
    {
        public void Dispose()
        {
            World.Dispose();
            Replay.Dispose();
        }
    }

    private static async Task<SiloDelivery> CarryHarvestBatchesIntoSilo(PrivateWorldRuntimeState state,
        string farmer, string jobId, GridPoint field, int ticks)
    {
        var world = LoadCarrying(state, farmer);
        var replay = LoadCarrying(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), farmer);
        var walked = new HashSet<GridPoint> { field };
        static int Delivered(PrivateWorldRuntime current, string harvestJob) => BatchLots(current, harvestJob)
            .Where(lot => lot.StorageBuildingId == "alpha-silo").Sum(lot => lot.Quantity);
        static bool Remaining(PrivateWorldRuntime current, string harvestJob) => BatchLots(current, harvestJob)
            .Any(lot => lot.StorageBuildingId != "alpha-silo" && Available(current, lot) > 0);
        try
        {
            var delivered = Delivered(world, jobId);
            for (var tick = 0; tick < ticks && Remaining(world, jobId); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                walked.Add(world.Inhabitants.Single(person => person.InhabitantId == farmer).Position);
                var nowDelivered = Delivered(world, jobId);
                Assert.Equal(nowDelivered, Delivered(replay, jobId));
                if (nowDelivered > delivered && Remaining(world, jobId))
                {
                    // A real batch delivery completes this directed carrying phase.
                    // Generic hauling can otherwise continue with unrelated supplies.
                    // Compare both branches before separately restoring each checkpoint.
                    var worldBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
                    var replayBytes = PrivateWorldRuntimeCodec.Encode(replay.ExportState());
                    Assert.Equal(worldBytes, replayBytes);
                    var saved = PrivateWorldRuntimeCodec.Decode(worldBytes);
                    var replaySaved = PrivateWorldRuntimeCodec.Decode(replayBytes);
                    world.Dispose();
                    replay.Dispose();
                    world = LoadCarrying(saved, farmer, freshChoice: true);
                    replay = LoadCarrying(replaySaved, farmer, freshChoice: true);
                }
                delivered = nowDelivered;
            }
            return new SiloDelivery(world, replay, walked);
        }
        catch
        {
            world.Dispose();
            replay.Dispose();
            throw;
        }
    }

    [Fact]
    public async Task FullSiloKeepsGrainOnItsFieldAndIncomingLoadsReserveTheFreedRoomAcrossReplay()
    {
        var harvest = await PrepareSiloHarvest();
        using var world = harvest.World;
        var silo = world.WorldSimulation.Buildings.Single(building => building.InstanceId == "alpha-silo");
        var capacity = BuildingStorageRules.Capacity(world.WorldContent.Buildings.Single(definition => definition.CanonicalId == silo.DefinitionId), silo)!.Value;
        var grainId = harvest.JobId + ":crop";
        var quantity = world.Society.Inventory.GetLot(grainId).Quantity;
        var inventory = InventoryFixture.AddLot(world.Society.Inventory, "full-silo-stock", "stone", Alpha,
            checked((int)capacity), storageBuildingId: silo.InstanceId);
        using var full = LoadChoices(WithInventory(world.ExportState(), inventory), harvest.Farmer, "farm:collect");
        for (var tick = 0; tick < 4; tick++) Assert.True((await full.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(quantity, full.Society.Inventory.GetLot(grainId).Quantity);
        Assert.Equal(new InventoryGroundPosition(harvest.Field.X, harvest.Field.Y), full.Society.Inventory.GetLot(grainId).GroundPosition);
        Assert.DoesNotContain(full.Society.Inventory.Lots, lot => lot.DeliveryBuildingId == silo.InstanceId);
        Assert.DoesNotContain(full.ExportState().Events, item => item.Kind == "field_harvest_collected");

        inventory = InventoryFixture.Reserve(full.Society.Inventory, "silo-used-stock", Alpha, "full-silo-stock", 3, "earlier-work", full.WorldTick + 100);
        inventory = InventoryFixture.ConsumeReservation(inventory, "silo-used-stock");
        using var pickup = LoadChoices(WithInventory(full.ExportState(), inventory), harvest.Farmer, "farm:collect");
        for (var tick = 0; tick < 8 && !pickup.Society.Inventory.Lots.Any(lot => lot.OwnerId == harvest.Farmer && lot.DeliveryBuildingId == silo.InstanceId); tick++)
            Assert.True((await pickup.AdvanceOneTickAsync()).Advanced);
        var carried = Assert.Single(pickup.Society.Inventory.Lots, lot => lot.OwnerId == harvest.Farmer && lot.DeliveryBuildingId == silo.InstanceId);
        Assert.Equal("grain", carried.ItemKind);
        Assert.Equal(3, carried.Quantity);
        Assert.Equal(grainId, carried.ProvenanceLotId);
        Assert.Null(carried.GroundPosition);
        Assert.Equal(quantity - 3, pickup.Society.Inventory.GetLot(grainId).Quantity);
        var collectedEvents = pickup.ExportState().Events.Count(item => item.Kind == "field_harvest_collected");
        var secondFarmer = pickup.Society.Inhabitants.First(person => person.HouseholdId == Alpha && person.Id != harvest.Farmer).Id;
        using var promised = LoadChoices(pickup.ExportState(), secondFarmer, "farm:collect");
        for (var tick = 0; tick < 4; tick++) Assert.True((await promised.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(quantity - 3, promised.Society.Inventory.GetLot(grainId).Quantity);
        Assert.Equal(3, promised.Society.Inventory.GetLot(carried.Id).Quantity);
        Assert.Equal(harvest.Farmer, promised.Society.Inventory.GetLot(carried.Id).OwnerId);
        Assert.DoesNotContain(promised.Society.Inventory.Lots, lot => lot.OwnerId == secondFarmer && lot.DeliveryBuildingId == silo.InstanceId);
        Assert.Equal(collectedEvents, promised.ExportState().Events.Count(item => item.Kind == "field_harvest_collected"));

        using var delivery = LoadCarrying(promised.ExportState(), harvest.Farmer, freshChoice: true);
        using var replay = LoadCarrying(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(delivery.ExportState())), harvest.Farmer);
        for (var tick = 0; tick < 40 && delivery.Society.Inventory.GetLot(carried.Id).OwnerId == harvest.Farmer; tick++)
        {
            Assert.True((await delivery.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(delivery.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(Alpha, delivery.Society.Inventory.GetLot(carried.Id).OwnerId);
        Assert.Equal(silo.InstanceId, delivery.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        Assert.Equal(capacity, delivery.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == silo.InstanceId).Sum(lot => (long)lot.Quantity));
        Assert.Equal(quantity - 3, delivery.Society.Inventory.GetLot(grainId).Quantity);
        Assert.Equal(new InventoryGroundPosition(harvest.Field.X, harvest.Field.Y), delivery.Society.Inventory.GetLot(grainId).GroundPosition);
        Assert.Equal(quantity, BatchLots(delivery, harvest.JobId).Where(lot => lot.ItemKind == "grain").Sum(lot => lot.Quantity));
        delivery.Validate();
    }

    private sealed record SiloHarvest(PrivateWorldRuntime World, string Farmer, GridPoint Field, string JobId);

    [Fact]
    public async Task GrainWaitingForSiloRoomDoesNotHideAnotherFieldsHouseBoundGreens()
    {
        var harvest = await PrepareSiloHarvest();
        using var world = harvest.World;
        var state = world.ExportState();
        var silo = world.WorldSimulation.Buildings.Single(building => building.InstanceId == "alpha-silo");
        var capacity = BuildingStorageRules.Capacity(world.WorldContent.Buildings.Single(definition => definition.CanonicalId == silo.DefinitionId), silo)!.Value;
        var inventory = InventoryFixture.AddLot(world.Society.Inventory, "full-silo-stock", "stone", Alpha,
            checked((int)capacity), storageBuildingId: silo.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "fixture-second-crop:seed", FarmFieldRules.GreensSeed, harvest.Farmer, 1);
        var occupied = world.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            world.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var point = world.Towns.Single().BorderTiles.OrderBy(tile => state.Map.FootDistance(tile, harvest.Field))
            .First(tile => new LandFertility(state.Map, state.WorldSeed).CanFarm(tile) && tile != harvest.Field && !occupied.Contains(tile) &&
                !world.RoadTiles.Contains(tile) && !state.Map.Resources.Any(resource => resource.Position == tile) &&
                !state.Inhabitants.Any(person => person.Position == tile));
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == harvest.Farmer ? person with { Position = point } : person).ToArray(),
            Fields = state.Fields!.Append(new FarmFieldState(point, Alpha, FarmFieldStage.Prepared))
                .OrderBy(field => field.Position.Y).ThenBy(field => field.Position.X).ToArray(),
        };
        using var growing = PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(chooseIdle: true));
        await FarmTestFields.PlantAndGrow(growing, harvest.Farmer, point, FarmFieldRules.Greens, "fixture-second-crop:seed");
        await FarmTestFields.Harvest(growing, harvest.Farmer, point);
        var greensPrefix = FarmFieldRules.FieldId(point) + ":harvest:1";
        var greensId = greensPrefix + ":crop";
        var grainId = harvest.JobId + ":crop";
        var grainQuantity = growing.Society.Inventory.GetLot(grainId).Quantity;
        var greensQuantity = growing.Society.Inventory.GetLot(greensId).Quantity;
        using var collecting = LoadCarrying(growing.ExportState(), harvest.Farmer, freshChoice: true);
        for (var tick = 0; tick < 8 && !collecting.Society.Inventory.Lots.Any(lot => lot.OwnerId == harvest.Farmer && lot.DeliveryBuildingId is not null); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        var load = Assert.Single(collecting.Society.Inventory.Lots, lot => lot.OwnerId == harvest.Farmer && lot.DeliveryBuildingId is not null);
        Assert.Equal("cultivated_greens", load.ItemKind);
        Assert.StartsWith(greensId, load.Id, StringComparison.Ordinal);
        Assert.Equal("first-town-house-a", load.DeliveryBuildingId);
        for (var tick = 0; tick < 64 && collecting.Society.Inventory.GetLot(load.Id).OwnerId == harvest.Farmer; tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(Alpha, collecting.Society.Inventory.GetLot(load.Id).OwnerId);
        Assert.Equal("first-town-house-a", collecting.Society.Inventory.GetLot(load.Id).StorageBuildingId);
        Assert.Equal(greensQuantity, BatchLots(collecting, greensPrefix).Where(lot => lot.ItemKind == "cultivated_greens").Sum(lot => lot.Quantity));
        Assert.Equal(grainQuantity, collecting.Society.Inventory.GetLot(grainId).Quantity);
        Assert.Equal(new InventoryGroundPosition(harvest.Field.X, harvest.Field.Y), collecting.Society.Inventory.GetLot(grainId).GroundPosition);
        Assert.Null(collecting.Society.Inventory.GetLot(grainId).StorageBuildingId);
        Assert.Equal(capacity, collecting.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == silo.InstanceId).Sum(lot => (long)lot.Quantity));
        collecting.Validate();
    }

    private static async Task<SiloHarvest> PrepareSiloHarvest()
    {
        using var generated = NormalPathWorld.CreateGenerated("probe-a", _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = generated.ExportState();
        var stone = InventoryFixture.AddLot(initial.Society.Society.Inventory, "alpha-stone", "stone", Alpha, 2,
            storageBuildingId: "first-town-house-a");
        using var setup = PrivateWorldRuntime.Restore(initial with
        {
            Society = initial.Society with { Society = initial.Society.Society with { Inventory = stone } },
        }, _ => new ActionCoverageRecorder(chooseIdle: true));
        var silo = setup.WorldContent.Buildings.Single(item => item.LocalId == "silo-1x1");
        var farmhouse = setup.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var placed = Enumerable.Range(-2, 5).SelectMany(dy => Enumerable.Range(-2, 5)
                .Select(dx => new GridPoint(farmhouse.Position.X + dx, farmhouse.Position.Y + dy)))
            .Any(point => setup.PlaceBuilding("alpha-silo", silo.CanonicalId, point, Alpha).Applied);
        Assert.True(placed);

        var state = setup.ExportState();
        var farmer = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var occupied = setup.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(setup.WorldContent.Buildings.Single(definition =>
                definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
        var field = setup.Towns.Single().BorderTiles.OrderBy(point => state.Map.FootDistance(point, farmhouse.Position)).First(point => new LandFertility(state.Map, state.WorldSeed).CanFarm(point) &&
            !occupied.Contains(point) && !setup.RoadTiles.Contains(point) && !state.Map.Resources.Any(resource => resource.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == farmer
                ? person with { Position = field, HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        state = FarmTestFields.Prepare(state, farmer, field);
        var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new ActionCoverageRecorder(chooseIdle: true));
        await FarmTestFields.PlantAndGrow(world, farmer, field);
        await FarmTestFields.Harvest(world, farmer, field);
        var prefix = FarmFieldRules.FieldId(field) + ":harvest:1";
        var harvest = world.Society.Inventory.Lots.Where(lot =>
            lot.Id.StartsWith(prefix + ":", StringComparison.Ordinal)).ToArray();
        Assert.Contains(harvest, lot => lot.ItemKind == "grain");
        Assert.All(harvest, lot =>
        {
            Assert.Equal(Alpha, lot.OwnerId);
            Assert.Null(lot.StorageBuildingId);
            Assert.Equal(new InventoryGroundPosition(field.X, field.Y), lot.GroundPosition);
        });
        world.Validate();
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        return new(world, farmer, field, prefix);
    }

    private static int Available(PrivateWorldRuntime world, InventoryLot lot) => lot.Quantity - world.Society.Inventory.Reservations
        .Where(reservation => reservation.LotId == lot.Id && reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed)
        .Sum(reservation => reservation.Quantity);

    private static IEnumerable<InventoryLot> BatchLots(PrivateWorldRuntime world, string jobId) => world.Society.Inventory.Lots.Where(lot =>
        lot.Quantity > 0 && (lot.Id.StartsWith(jobId + ":", StringComparison.Ordinal) ||
            lot.ProvenanceLotId?.StartsWith(jobId + ":", StringComparison.Ordinal) == true));

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntime LoadCarrying(PrivateWorldRuntimeState state, string farmer, bool freshChoice = false)
    {
        return PrivateWorldRuntime.Restore(freshChoice ? FreshChoices(state) : state, id => new FarmCarryProvider(id == farmer));
    }

    private static PrivateWorldRuntimeState FreshChoices(PrivateWorldRuntimeState state) =>
        state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
            Society = state.Society with
            {
                Cognition = state.Society.Cognition with
                {
                    Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime with { CurrentIntention = null }).ToArray(),
                }
            }
        };

    private static PrivateWorldRuntime LoadChoices(PrivateWorldRuntimeState state, string farmer, params string[] choices) =>
        PrivateWorldRuntime.Restore(FreshChoices(state), id => new GrainProcessingProvider(id == farmer ? choices : []));

    private sealed class GrainProcessingProvider(string[] choices) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.Where(candidate => choices.Contains(candidate.Id))
                .OrderBy(candidate => candidate.DeterministicPriority).FirstOrDefault() ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }

    private static HashSet<string> FamiliesForHousehold(PrivateWorldRuntime world, ActionCoverageRecorder recorder,
        string householdId) => world.Society.Inhabitants
        .Where(person => person.HouseholdId == householdId)
        .SelectMany(person => recorder.FamiliesOfferedTo(person.Id, world.WorldContent))
        .ToHashSet(StringComparer.Ordinal);
}
