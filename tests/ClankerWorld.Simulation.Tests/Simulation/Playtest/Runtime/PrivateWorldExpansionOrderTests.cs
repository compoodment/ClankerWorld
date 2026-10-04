using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldExpansionOrderTests
{
    private const string Household = "household:camp-alpha";
    private const string OtherHousehold = "household:camp-beta";
    private const string House = "first-town-house-a";
    private const string OtherHouse = "first-town-house-b";
    private const string Warehouse = "first-town-warehouse";
    private static readonly Lazy<byte[]> Original = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("expansion-world", _ => new ExpansionChoices());
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });
    private static readonly Lazy<byte[]> HouseBaseline = new(() => CreateBaseline(House));
    private static readonly Lazy<byte[]> WarehouseBaseline = new(() => CreateBaseline(Warehouse));

    [Theory]
    [InlineData(House, "expand my House", 4, 0)]
    [InlineData(Warehouse, "expand one my Town Warehouse", 8, 4)]
    public async Task ExpansionOrdersPayForOneNativeStageAndCreditOnlyItsCompletedJobAcrossReplay(
        string buildingId, string text, int woodCost, int stoneCost)
    {
        var state = Prepared(buildingId);
        var actor = Actor(state);
        var original = Building(state, buildingId);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "paid-expansion", text);
        var job = await StartJob(world, receipt);
        var binding = Assert.IsType<OwnerBuildingExpansionBinding>(Order(world, receipt).ExpansionBinding);
        Assert.Equal(("expand_building", "expansions", 1, false, 0),
            (Order(world, receipt).Action, Order(world, receipt).ProgressUnit, Order(world, receipt).RequestedUnits,
                Order(world, receipt).RepeatUntilCancelled, Order(world, receipt).CompletedUnits));
        Assert.Equal((original.InstanceId, original.DefinitionId, original.HouseholdId ?? original.TownId!, original.Position, 0),
            (binding.BuildingInstanceId, binding.DefinitionId, binding.OwnerId, binding.ExpectedPosition, binding.ExpectedRevision));
        Assert.Equal((job.JobId, job.TargetPosition, job.TargetFootprint),
            (binding.JobId, binding.TargetPosition, binding.TargetFootprint));
        Assert.Equal(receipt.InstructionId, job.OrderInstructionId);
        Assert.Equal(original, Building(world.ExportState(), buildingId));
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(id).State));
        var woodBefore = Quantity(world, "wood");
        var stoneBefore = Quantity(world, "stone");
        using var replay = Reload(world);
        while (world.WorldTick < job.CompletionTick - 1)
        {
            await TickTogether(world, replay);
            Assert.Equal(0, Order(world, receipt).CompletedUnits);
            Assert.Null(Order(world, receipt).LastEffectId);
            Assert.Equal(original, Building(world.ExportState(), buildingId));
        }
        await TickTogether(world, replay);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(WorldProductionJobState.Completed, Job(world, job.JobId).State);
        var expanded = Building(world.ExportState(), buildingId);
        Assert.Equal((original.InstanceId, original.DefinitionId, original.HouseholdId, original.TownId, original.PlacedTick),
            (expanded.InstanceId, expanded.DefinitionId, expanded.HouseholdId, expanded.TownId, expanded.PlacedTick));
        Assert.Equal((job.TargetPosition, job.TargetFootprint), (expanded.Position, expanded.Footprint));
        Assert.Equal(woodBefore - woodCost, Quantity(world, "wood"));
        Assert.Equal(stoneBefore - stoneCost, Quantity(world, "stone"));
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
            world.Society.Inventory.GetReservation(id).State));
        Assert.StartsWith("expand:job:", Order(world, receipt).LastEffectId, StringComparison.Ordinal);
        Assert.Single(world.ExportState().Events, item => item.Kind == "building_expanded");
        await TickTogether(world, replay);
        Assert.Single(world.WorldSimulation.BuildingExpansions!);
        Assert.Equal(1, Order(world, receipt).CompletedUnits);
    }

    [Theory]
    [InlineData("need")]
    [InlineData("owner")]
    [InlineData("reserved-material")]
    [InlineData("site")]
    [InlineData("land-permission")]
    [InlineData("land-owner")]
    public async Task ExpansionOrdersPreserveNativeNeedOwnershipMaterialAndSiteChecks(string boundary)
    {
        var state = Prepared();
        var actor = Actor(state);
        var house = Building(state, House);
        var inventory = state.Society.Society.Inventory;
        if (boundary == "need")
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id == "expansion-wood"
                ? lot with { Quantity = 4 } : lot).ToArray()
            };
        if (boundary == "reserved-material")
            inventory = InventoryFixture.Reserve(inventory, "other-work", Household, "expansion-wood", 52, "other_work", 1_000);
        state = WithInventory(state, inventory);
        if (boundary == "owner") actor = state.Society.Society.GetHousehold(OtherHousehold).MemberIds[0];
        if (boundary == "site") state = state with
        {
            RoadTiles = state.RoadTiles!.Concat(new[] { new GridPoint(house.Position.X - 1, house.Position.Y),
                new GridPoint(house.Position.X + 1, house.Position.Y), new GridPoint(house.Position.X, house.Position.Y - 1),
                new GridPoint(house.Position.X, house.Position.Y + 1) }).Distinct().OrderBy(point => point.Y).ThenBy(point => point.X).ToArray(),
        };
        if (boundary == "land-permission") state = state with
        {
            HouseholdLandUseRights = state.HouseholdLandUseRights!
                .Where(right => right.GrantSource != "expansion_test_fixture").ToArray(),
        };
        if (boundary == "land-owner") state = state with
        {
            HouseholdLandUseRights = state.HouseholdLandUseRights!.Select(right =>
                right.GrantSource == "expansion_test_fixture" ? right with { HouseholdId = OtherHousehold } : right).ToArray(),
        };
        using var world = Restore(state);
        var text = string.Create(CultureInfo.InvariantCulture, $"expand my House at ({house.Position.X}, {house.Position.Y})");
        var receipt = Submit(world, actor, "native-gate", text);
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(Order(world, receipt).ExpansionBinding?.JobId);
        Assert.Empty(world.WorldSimulation.BuildingExpansions ?? []);
        Assert.Equal(house, Building(world.ExportState(), House));
        if (boundary == "reserved-material")
        {
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("other-work").State);
            Assert.Equal(52, world.Society.Inventory.GetLot("expansion-wood").Quantity);
        }
        else Assert.Equal("blocked", Order(world, receipt).Status);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
    }

    [Fact]
    public async Task ANewOrderDoesNotAdoptAnOrdinaryExpansionOrRebindAfterItsRevisionChanges()
    {
        var state = Prepared();
        var actor = Actor(state);
        var house = Building(state, House);
        state = AtAdjacent(state, actor, house);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "separate-job", "expand my House");
        await Tick(world);
        var binding = Assert.IsType<OwnerBuildingExpansionBinding>(Order(world, receipt).ExpansionBinding);
        Assert.Null(binding.JobId);
        Assert.Equal(house.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        var ordinary = world.StartBuildingExpansion(actor, House);
        Assert.True(ordinary.Applied, ordinary.Failure);
        Assert.Null(Job(world, ordinary.JobId!).OrderInstructionId);
        using var replay = Reload(world);
        while (world.WorldTick < Job(world, ordinary.JobId!).CompletionTick) await TickTogether(world, replay);
        await TickTogether(world, replay);
        Assert.Equal(WorldProductionJobState.Completed, Job(world, ordinary.JobId!).State);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(binding, Order(world, receipt).ExpansionBinding);
        Assert.Null(Order(world, receipt).LastEffectId);
        Assert.Equal(1, Building(world.ExportState(), House).Footprint!.Revision);
        Assert.Single(world.WorldSimulation.BuildingExpansions!);
    }

    [Fact]
    public async Task ASelectedFootprintCannotBeReplacedByADifferentLegalExpansionShape()
    {
        var state = Prepared();
        var actor = Actor(state);
        var house = Building(state, House);
        using var walking = Restore(AtAdjacent(state, actor, house));
        var receipt = Submit(walking, actor, "fixed-shape", "expand my House");
        await Tick(walking);
        var binding = Assert.IsType<OwnerBuildingExpansionBinding>(Order(walking, receipt).ExpansionBinding);
        Assert.Null(binding.JobId);
        var added = Enumerable.Range(0, binding.TargetFootprint.Height).SelectMany(y =>
            Enumerable.Range(0, binding.TargetFootprint.Width).Select(x => new GridPoint(binding.TargetPosition.X + x, binding.TargetPosition.Y + y)))
            .Single(point => point != house.Position);
        state = walking.ExportState();
        state = state with { RoadTiles = state.RoadTiles!.Append(added).Distinct().OrderBy(point => point.Y).ThenBy(point => point.X).ToArray() };
        using (var native = Restore(state))
        {
            var alternative = native.StartBuildingExpansion(actor, House);
            Assert.True(alternative.Applied, alternative.Failure);
            var nativeJob = Job(native, alternative.JobId!);
            Assert.False(nativeJob.TargetPosition == binding.TargetPosition && nativeJob.TargetFootprint == binding.TargetFootprint);
        }
        using var world = Restore(state);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(binding, Order(world, receipt).ExpansionBinding);
        Assert.Empty(world.WorldSimulation.BuildingExpansions ?? []);
        Assert.Equal(house, Building(world.ExportState(), House));
    }

    [Fact]
    public async Task ARefusedTickCannotReserveMaterialsOrBindAnExpansionJob()
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "refused", "expand my House");
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Empty(world.WorldSimulation.BuildingExpansions ?? []);
        Assert.Empty(world.Society.Inventory.Reservations);
        Assert.Null(Order(world, receipt).ExpansionBinding);
        var job = await StartJob(world, receipt);
        Assert.Equal(receipt.InstructionId, job.OrderInstructionId);
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Fact]
    public async Task StrictSavesRejectInventedProgressOrMismatchedExpansionJobs()
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "saved-expansion", "expand my House");
        var job = await StartJob(world, receipt);
        var saved = world.ExportState();
        var order = Order(world, receipt);
        var binding = Assert.IsType<OwnerBuildingExpansionBinding>(order.ExpansionBinding);
        foreach (var invalid in new[]
        {
            order with { ExpansionBinding = null },
            order with { ExpansionBinding = binding with { JobId = "expansion-missing" } },
            order with { ExpansionBinding = binding with { OwnerId = OtherHousehold } },
            order with { ExpansionBinding = binding with { ExpectedRevision = binding.ExpectedRevision + 1 } },
            order with { ExpansionBinding = binding with { TargetPosition = new(binding.TargetPosition.X + 1, binding.TargetPosition.Y) } },
            order with { Status = "finished", CompletedUnits = 1, LastEffectId = "expand:job:" + new string('0', 64) },
        })
        {
            var corrupt = saved with
            {
                Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                    ? item with { Order = invalid } : item).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => Restore(corrupt));
        }
        var unlinked = saved with
        {
            WorldSimulation = saved.WorldSimulation! with
            {
                BuildingExpansions = saved.WorldSimulation.BuildingExpansions!.Select(item => item.JobId == job.JobId
                    ? item with { OrderInstructionId = null } : item).ToArray(),
            },
        };
        Assert.Throws<InvalidDataException>(() => Restore(unlinked));
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    private static PrivateWorldRuntimeState Prepared(string buildingId = House) =>
        PrivateWorldRuntimeCodec.Decode((buildingId == House ? HouseBaseline : WarehouseBaseline).Value);

    private static byte[] CreateBaseline(string buildingId)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Original.Value);
        var actor = Actor(state);
        var original = Building(state, buildingId);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == original.DefinitionId);
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [], Offers = [] };
        inventory = InventoryFixture.AddLot(inventory, "expansion-wood", "wood", original.HouseholdId ?? original.TownId!,
            buildingId == House ? 52 : 210, storageBuildingId: buildingId);
        if (buildingId == Warehouse)
            inventory = InventoryFixture.AddLot(inventory, "expansion-stone", "stone", original.TownId!, 8, storageBuildingId: buildingId);
        var occupied = state.WorldSimulation!.Buildings.Where(item => item.InstanceId != buildingId)
            .SelectMany(item => WorldContentSimulationRules.Footprint(state.WorldContent.Buildings.Single(value => value.CanonicalId == item.DefinitionId), item))
            .Concat(state.Map.Resources.Select(item => item.Position)).Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.RoadTiles!).Concat(state.Fields!.Select(item => item.Position))
            .Concat(state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position))
            .Concat(state.Towns!.Where(town => town.Id != original.TownId).SelectMany(town => town.BorderTiles)).ToHashSet();
        var connected = new HashSet<GridPoint> { state.RoadTiles![0] };
        var pending = new Queue<GridPoint>(connected);
        while (pending.TryDequeue(out var point))
            foreach (var next in state.Map.FootNeighbors(point))
                if (connected.Add(next)) pending.Enqueue(next);
        foreach (var site in state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => state.Map.FootDistance(original.Position, point) < int.MaxValue)
            .OrderBy(point => state.Map.FootDistance(original.Position, point)).ThenBy(point => point.Y).ThenBy(point => point.X))
        {
            var envelope = Enumerable.Range(-1, 4).SelectMany(y => Enumerable.Range(-1, 4)
                .Select(x => new GridPoint(site.X + x, site.Y + y)));
            if (envelope.Any(point => !state.Map.IsBuildable(point) || occupied.Contains(point))) continue;
            var entrances = state.Map.FootNeighbors(site).Where(point => WorldContentSimulationRules.IsEntrance(definition, site, point) &&
                state.Map.CanFootStep(site, point) && !occupied.Contains(point) && connected.Contains(point))
                .OrderBy(point => Math.Abs(point.X - site.X) + Math.Abs(point.Y - site.Y)).ThenBy(point => point.Y).ThenBy(point => point.X).ToArray();
            if (entrances.Length == 0) continue;
            var placed = original with { Position = site, Entrance = entrances[0] };
            var candidate = WithInventory(state, inventory) with
            {
                Inhabitants = state.Inhabitants.Select(person => person with
                {
                    Position = person.InhabitantId == actor ? site : person.Position,
                    HungerBasisPoints = 10_000,
                    Equipment = null,
                    Project = null,
                    LastDecisionContext = null,
                }).ToArray(),
                WorldSimulation = state.WorldSimulation with
                {
                    Buildings = state.WorldSimulation.Buildings.Select(item =>
                    item.InstanceId == buildingId ? placed : item).ToArray()
                },
                Towns = state.Towns!.Select(town => town.Id == placed.TownId ? town with
                {
                    BorderTiles = TownBorderRules.ExpandForBuilding(state.Map, town, site, definition.Width + 2, definition.Height + 2),
                } : town).ToArray(),
            };
            // Material/work fixtures start with the same recorded land permission as native expansion fixtures.
            candidate = ExpansionLandFixture.WithRights(candidate, placed, envelope);
            using var probe = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(candidate)));
            if (probe.StartBuildingExpansion(actor, buildingId).Applied) return PrivateWorldRuntimeCodec.Encode(candidate);
        }
        throw new InvalidOperationException("The expansion fixture needs a valid native next-stage site.");
    }

    private static PrivateWorldRuntimeState AtAdjacent(PrivateWorldRuntimeState state, string actor, PlacedBuilding building)
    {
        var point = state.Map.FootNeighbors(building.Position).OrderBy(point =>
                Math.Abs(point.X - building.Position.X) + Math.Abs(point.Y - building.Position.Y))
            .First(point => !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == point));
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Position = point, LastDecisionContext = null } : person).ToArray()
        };
    }
    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.GetHousehold(Household).MemberIds[0];
    private static PlacedBuilding Building(PrivateWorldRuntimeState state, string id) => state.WorldSimulation!.Buildings.Single(item => item.InstanceId == id);
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new ExpansionChoices());
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        return restored;
    }
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static BuildingExpansionJob Job(PrivateWorldRuntime world, string id) => world.WorldSimulation.BuildingExpansions!.Single(item => item.JobId == id);
    private static int Quantity(PrivateWorldRuntime world, string kind) => world.Society.Inventory.Lots.Where(lot => lot.ItemKind == kind).Sum(lot => lot.Quantity);
    private static async Task Tick(PrivateWorldRuntime world) => Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    private static async Task TickTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        await Tick(world);
        await Tick(replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }
    private static async Task<BuildingExpansionJob> StartJob(PrivateWorldRuntime world, OwnerInstructionReceipt receipt)
    {
        for (var step = 0; step < 8 && Order(world, receipt).ExpansionBinding?.JobId is null; step++) await Tick(world);
        return Job(world, Assert.IsType<string>(Order(world, receipt).ExpansionBinding?.JobId));
    }
    private static async Task Finish(PrivateWorldRuntime world, OwnerInstructionReceipt receipt)
    {
        for (var step = 0; step < 48 && Order(world, receipt).Status != "finished"; step++) await Tick(world);
        Assert.Equal("finished", Order(world, receipt).Status);
    }
    private sealed class ExpansionChoices : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var selected = request.Observation.OperativeOrderInstructionId is not null
                ? request.Observation.Candidates.FirstOrDefault(item => item.Id == "expand_building")?.Id ?? "safe_idle" : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
