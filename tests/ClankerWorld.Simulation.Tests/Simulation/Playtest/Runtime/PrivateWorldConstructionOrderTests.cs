using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConstructionOrderTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";
    private const string House = "first-town-house-a";
    private static readonly Lazy<Task<ConstructionSetup>> Baseline = new(CreateBaselineAsync);

    [Fact]
    public async Task AClinicIsPaidAndCreditedOnlyAfterRealTravelAndWorkAndItsReceiptSurvivesRemoval()
    {
        var setup = await Baseline.Value;
        var state = Decode(setup);
        using var world = Restore(state);
        var receipt = Submit(world, setup.Actor, "paid-clinic", BuildClinic(setup.Site));
        await Tick(world);
        var order = Order(world, receipt);
        var project = Person(world, setup.Actor).Project!;
        Assert.Equal(("construct_building", "buildings", 1, 0, "clinic"),
            (order.Action, order.ProgressUnit, order.RequestedUnits, order.CompletedUnits, order.TargetBuildingKind));
        Assert.Equal((Alpha, setup.Site, project.StartedTick),
            (order.ConstructionOwnerId, order.ConstructionPosition, order.ConstructionStartedTick));
        Assert.Equal(receipt.InstructionId, project.OrderInstructionId);
        Assert.Equal(setup.CandidateId, project.CandidateId);
        Assert.NotNull(order.ConstructionInstanceId);
        Assert.Null(order.LastEffectId);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, building => building.InstanceId == order.ConstructionInstanceId);
        Assert.NotEqual(setup.Site, Person(world, setup.Actor).Position);
        AssertInputs(world, 10, 4);
        using (var travelling = Reload(world))
        {
            for (var tick = 0; tick < 40 && Person(world, setup.Actor).Project!.WorkDone < 3; tick++)
                await TickTogether(world, travelling);
        }
        Assert.InRange(Person(world, setup.Actor).Project!.WorkDone, 3, 9);
        Assert.Equal("working", Person(world, setup.Actor).Project!.Stage);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        AssertInputs(world, 10, 4);
        using var working = Reload(world);
        await FinishTogether(world, working, receipt);
        var completed = Order(world, receipt);
        var building = Assert.Single(world.WorldSimulation.Buildings, item => item.InstanceId == completed.ConstructionInstanceId);
        Assert.Equal((setup.DefinitionId, setup.Site, Alpha), (building.DefinitionId, building.Position, building.HouseholdId));
        Assert.Equal(("finished", 1), (completed.Status, completed.CompletedUnits));
        Assert.NotNull(completed.LastEffectId);
        Assert.Equal("completed", Person(world, setup.Actor).Project!.Stage);
        AssertInputs(world, 0, 0);
        var paid = world.Society.Inventory.Reservations.Where(item => item.LotId is "construction-wood" or "construction-stone").ToArray();
        Assert.Equal(14, paid.Sum(item => item.Quantity));
        Assert.All(paid, item =>
        {
            Assert.Equal(Alpha, item.OwnerId);
            Assert.Equal(InventoryReservationState.Completed, item.State);
        });
        Assert.Contains(paid, item => item.LotId == "construction-wood" && item.Quantity == 10);
        Assert.Contains(paid, item => item.LotId == "construction-stone" && item.Quantity == 4);
        var proof = Assert.Single(world.WorldSimulation.ConstructionReceipts!);
        Assert.Equal((receipt.InstructionId, setup.Actor, building.InstanceId, setup.DefinitionId, Alpha, Alpha, setup.Site),
            (proof.InstructionId, proof.ActorId, proof.BuildingInstanceId, proof.DefinitionId, proof.OwnerId, proof.MaterialOwnerId, proof.Position));
        Assert.Equal(building.PlacedTick, proof.CompletedTick);
        Assert.Equal(order.ConstructionStartedTick, proof.StartedTick);
        Assert.Equal(paid.Select(item => item.Id).Order(StringComparer.Ordinal), proof.InputReservationIds.Order(StringComparer.Ordinal));
        Assert.Single(world.ExportState().Events, item => item.Kind == "build_completed");
        AssertUnrelatedPaidPlacementPreservesReceipt(world, proof);
        var removed = world.RemoveBuilding(building.InstanceId, building.TownId, Alpha);
        Assert.True(removed.Applied, removed.Failure);
        using var removedReplay = Reload(world);
        await TickTogether(world, removedReplay);
        Assert.Equal(("finished", 1, completed.LastEffectId),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).LastEffectId));
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.InstanceId == building.InstanceId);
        Assert.Equal(JsonSerializer.Serialize(proof), JsonSerializer.Serialize(Assert.Single(world.WorldSimulation.ConstructionReceipts!)));
        AssertRemovedOrderedBuildingIdCannotBeReused(world, proof);
        var saved = world.ExportState();
        foreach (var receipts in new IReadOnlyList<WorldConstructionReceipt>[]
        {
            [],
            [proof with { InputReservationIds = [] }],
            [proof with { ActorId = state.Society.Society.Inhabitants.First(item => item.Id != setup.Actor).Id }],
        })
            Assert.Throws<InvalidDataException>(() => Restore(saved with
            {
                WorldSimulation = saved.WorldSimulation! with { ConstructionReceipts = receipts },
            }));
        AssertInputs(world, 0, 0);
    }

    [Fact]
    public async Task AFirstHouseCanBePaidFromTheBuildersActualCarriedWoodWhileItsHouseholdOwnsTheBuilding()
    {
        var setup = await Baseline.Value;
        var state = Decode(setup);
        var formerHouse = Home(state);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != Alpha && lot.OwnerId != setup.Actor).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "carried-house-wood", "wood", setup.Actor, 8);
        using var world = Restore(WithInventory(state, inventory));
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, setup.Actor, Person(world, setup.Actor).Equipment));
        var removed = world.RemoveBuilding(formerHouse.InstanceId, formerHouse.TownId, Alpha);
        Assert.True(removed.Applied, removed.Failure);
        var text = string.Create(CultureInfo.InvariantCulture,
            $"build one House at ({formerHouse.Position.X}, {formerHouse.Position.Y})");
        var receipt = Submit(world, setup.Actor, "carried-house", text);
        await ReachWork(world, setup.Actor);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal((setup.Actor, 8), (world.Society.Inventory.GetLot("carried-house-wood").OwnerId,
            world.Society.Inventory.GetLot("carried-house-wood").Quantity));
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        var proof = Assert.Single(world.WorldSimulation.ConstructionReceipts!);
        Assert.Equal((Alpha, setup.Actor, formerHouse.Position, HouseContent.House1x1().CanonicalId),
            (proof.OwnerId, proof.MaterialOwnerId, proof.Position, proof.DefinitionId));
        var building = Assert.Single(world.WorldSimulation.Buildings, item => item.InstanceId == proof.BuildingInstanceId);
        Assert.Equal(Alpha, building.HouseholdId);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "carried-house-wood");
        var payment = Assert.Single(world.Society.Inventory.Reservations, item => item.LotId == "carried-house-wood");
        Assert.Equal((setup.Actor, 8, InventoryReservationState.Completed),
            (payment.OwnerId, payment.Quantity, payment.State));
        Assert.Equal(payment.Id, Assert.Single(proof.InputReservationIds));
        AssertInputs(world, 0, 0);
        using var completed = Reload(world);
        Assert.Equal(proof.MaterialOwnerId, Assert.Single(completed.WorldSimulation.ConstructionReceipts!).MaterialOwnerId);
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("outside")]
    public async Task AnExplicitIllegalSiteNeverFallsBackToTheAvailableRankedClinicSite(string boundary)
    {
        var setup = await Baseline.Value;
        var state = Decode(setup);
        var requested = boundary == "occupied" ? Home(state).Position : new GridPoint(0, -1);
        using var world = Restore(state);
        var receipt = Submit(world, setup.Actor, "illegal-clinic", BuildClinic(requested));
        await Tick(world);
        Assert.Equal(("blocked", 0, requested),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).TargetPosition));
        Assert.NotEmpty(Order(world, receipt).BlockedReason!);
        Assert.Null(Order(world, receipt).LastEffectId);
        using var replay = Reload(world);
        for (var tick = 0; tick < 3; tick++) await TickTogether(world, replay);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.DefinitionId == setup.DefinitionId);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        AssertInputs(world, 10, 4);
        Assert.DoesNotContain(world.Society.Inventory.Reservations,
            item => item.LotId is "construction-wood" or "construction-stone");
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("foreign")]
    public async Task ConstructionCannotPayWithReservedOrAnotherHouseholdsMaterials(string boundary)
    {
        var setup = await Baseline.Value;
        var state = Decode(setup);
        var inventory = state.Society.Society.Inventory;
        if (boundary == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "construction-held", Alpha, "construction-wood", 10,
                "other_work", state.Society.Society.WorldTick + 100);
        else
            inventory = InventoryFixture.Transfer(inventory, "foreign-construction-stock", Alpha, Beta,
                "construction-wood", 10, "other_household_stock", destinationStorageBuildingId: "first-town-house-b");
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, setup.Actor, "protected-clinic", BuildClinic(setup.Site));
        for (var tick = 0; tick < 3; tick++) await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(Order(world, receipt).LastEffectId);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.DefinitionId == setup.DefinitionId);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        Assert.Equal((boundary == "reserved" ? Alpha : Beta, 10),
            (world.Society.Inventory.GetLot("construction-wood").OwnerId, world.Society.Inventory.GetLot("construction-wood").Quantity));
        Assert.Equal(4, world.Society.Inventory.GetLot("construction-stone").Quantity);
        Assert.DoesNotContain(world.Society.Inventory.Reservations,
            item => item.LotId == "construction-wood" && item.State == InventoryReservationState.Completed);
        if (boundary == "reserved") Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation("construction-held").State);
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Theory]
    [InlineData("house", Alpha)]
    [InlineData("silo", Beta)]
    public async Task ExistingBuildingsAndTheFarmhousePrerequisiteRemainRealConstructionGates(string kind, string household)
    {
        var setup = await Baseline.Value;
        var state = Decode(setup);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        using var world = Restore(state);
        var receipt = Submit(world, actor, "building-gate", "build one " + kind);
        await Tick(world);
        Assert.Equal(("construct_building", "blocked", 0),
            (Order(world, receipt).Action, Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.NotEmpty(Order(world, receipt).BlockedReason!);
        Assert.Null(Order(world, receipt).ConstructionInstanceId);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        Assert.Equal(state.WorldSimulation!.Buildings.Count, world.WorldSimulation.Buildings.Count);
        AssertInputs(world, 10, 4);
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Fact]
    public async Task AnExistingOrdinaryProjectCannotBeAdoptedAsProofOfTheNewOrder()
    {
        var setup = await Baseline.Value;
        var provider = new ConstructionChoices(nativeCandidate: setup.CandidateId);
        var state = Decode(setup);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == setup.Actor
                ? person with { LastDecisionContext = null } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, id => id == setup.Actor ? provider : new ConstructionChoices());
        await Tick(world);
        var ordinary = Person(world, setup.Actor).Project!;
        Assert.NotNull(ordinary);
        Assert.Equal(setup.CandidateId, ordinary.CandidateId);
        Assert.Null(ordinary.OrderInstructionId);
        var receipt = Submit(world, setup.Actor, "separate-clinic", BuildClinic(setup.Site));
        await Tick(world);
        Assert.Equal(("doing", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.NotNull(Order(world, receipt).ConstructionInstanceId);
        Assert.Null(Order(world, receipt).LastEffectId);
        var remaining = Person(world, setup.Actor).Project!;
        Assert.Equal(ordinary.CandidateId, remaining.CandidateId);
        Assert.True(remaining.StartedTick > ordinary.StartedTick);
        Assert.Equal(0, remaining.WorkDone);
        Assert.Equal(receipt.InstructionId, remaining.OrderInstructionId);
        Assert.NotEqual("cancelled", remaining.Stage);
        AssertInputs(world, 10, 4);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
    }

    private static void AssertRemovedOrderedBuildingIdCannotBeReused(PrivateWorldRuntime removed, WorldConstructionReceipt proof)
    {
        var state = removed.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "replacement-clinic-wood", "wood", Alpha, 10, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "replacement-clinic-stone", "stone", Alpha, 4, storageBuildingId: House);
        state = WithInventory(state, inventory);
        using (var control = Restore(state))
        {
            var placement = control.PlaceBuilding("replacement-clinic-control", proof.DefinitionId, proof.Position, proof.OwnerId);
            Assert.True(placement.Applied, placement.Failure);
            AssertInputs(control, 0, 0);
            using var validReplacement = Reload(control);
        }
        using var attempt = Restore(state);
        var before = PrivateWorldRuntimeCodec.Encode(attempt.ExportState());
        var reused = attempt.PlaceBuilding(proof.BuildingInstanceId, proof.DefinitionId, proof.Position, proof.OwnerId);
        Assert.False(reused.Applied);
        Assert.NotEmpty(reused.Failure!);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(attempt.ExportState()));
        AssertInputs(attempt, 10, 4);
        using var unchanged = Reload(attempt);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(unchanged.ExportState()));
    }

    private static void AssertUnrelatedPaidPlacementPreservesReceipt(PrivateWorldRuntime completed, WorldConstructionReceipt proof)
    {
        var state = completed.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "later-silo-wood", "wood", Alpha, 6, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "later-silo-stone", "stone", Alpha, 2, storageBuildingId: House);
        using var ordinary = Restore(WithInventory(state, inventory));
        var farm = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var sites = Enumerable.Range(-2, 5).SelectMany(y => Enumerable.Range(-2, 5).Select(x =>
            new GridPoint(farm.Position.X + x, farm.Position.Y + y)));
        Assert.Contains(sites, site => ordinary.PlaceBuilding("later-paid-silo", SiloContent.Silo1x1().CanonicalId, site, Alpha).Applied);
        Assert.DoesNotContain(ordinary.Society.Inventory.Lots, item => item.Id is "later-silo-wood" or "later-silo-stone");
        Assert.Equal(8, ordinary.Society.Inventory.Reservations.Where(item => item.LotId is "later-silo-wood" or "later-silo-stone")
            .Sum(item => item.State == InventoryReservationState.Completed ? item.Quantity : 0));
        using var replay = Reload(ordinary);
        Assert.Equal(JsonSerializer.Serialize(proof), JsonSerializer.Serialize(Assert.Single(replay.WorldSimulation.ConstructionReceipts!)));
    }

    private static async Task<ConstructionSetup> CreateBaselineAsync()
    {
        using var generated = NormalPathWorld.CreateGenerated("personal-storage-orders", _ => new ConstructionChoices());
        var state = generated.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var home = Home(state);
        var previous = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != Alpha && lot.OwnerId != actor).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "construction-wood", "wood", Alpha, 10, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "construction-stone", "stone", Alpha, 4, storageBuildingId: House);
        state = WithInventory(state, inventory) with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? home.Position : person.Position == home.Position ? previous : person.Position,
                HungerBasisPoints = 10_000,
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                LastDecisionContext = null,
            }).ToArray(),
        };
        var definition = CareContent.Clinic1x2().CanonicalId;
        var capture = new ConstructionChoices();
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? capture : new ConstructionChoices());
        await Tick(world);
        var candidate = capture.Observations.SelectMany(item => item.Candidates)
            .Select(item => TownConstructionCandidateIds.TryParse(item.Id, out var selected) ? (item.Id, Selected: selected) : default)
            .Where(item => item.Selected.IsBuilding && item.Selected.DefinitionId == definition && item.Selected.SitePosition is not null)
            .OrderByDescending(item => state.Map.FootDistance(home.Position, item.Selected.SitePosition!.Value)).First();
        var site = candidate.Selected.SitePosition!.Value;
        Assert.True(state.Map.FootDistance(home.Position, site) >= 2);
        Assert.Null(Person(world, actor).Project);
        return new(PrivateWorldRuntimeCodec.Encode(world.ExportState()), actor, definition, site, candidate.Id);
    }
    private static PrivateWorldRuntimeState Decode(ConstructionSetup setup) => PrivateWorldRuntimeCodec.Decode(setup.Bytes);
    private static PlacedBuilding Home(PrivateWorldRuntimeState state) => state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new ConstructionChoices());
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        return replay;
    }
    private static string BuildClinic(GridPoint site) => string.Create(CultureInfo.InvariantCulture, $"build one Clinic at ({site.X}, {site.Y})");
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text, Queue: queue));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static PlaytestInhabitantState Person(PrivateWorldRuntime world, string actor) => world.Inhabitants.Single(item => item.InhabitantId == actor);
    private static void AssertInputs(PrivateWorldRuntime world, int wood, int stone)
    {
        Assert.Equal(wood, world.Society.Inventory.Lots.Where(item => item.OwnerId == Alpha && item.ItemKind == "wood").Sum(item => item.Quantity));
        Assert.Equal(stone, world.Society.Inventory.Lots.Where(item => item.OwnerId == Alpha && item.ItemKind == "stone").Sum(item => item.Quantity));
    }
    private static async Task Tick(PrivateWorldRuntime world) => Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    private static async Task TickTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        await Tick(world);
        await Tick(replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }
    private static async Task FinishTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay, OwnerInstructionReceipt receipt)
    {
        for (var tick = 0; tick < 60 && Order(world, receipt).Status != "finished"; tick++) await TickTogether(world, replay);
        Assert.Equal("finished", Order(world, receipt).Status);
    }
    private sealed record ConstructionSetup(byte[] Bytes, string Actor, string DefinitionId, GridPoint Site, string CandidateId);
    private sealed class ConstructionChoices(string? nativeCandidate = null) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ConcurrentQueue<InhabitantObservation> Observations { get; } = new();
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            Observations.Enqueue(observation);
            var preferred = observation.OperativeOrderInstructionId is not null ? "construct_building" : nativeCandidate;
            var selected = preferred is not null && observation.Candidates.Any(candidate => candidate.Id == preferred) ? preferred : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind,
                request.ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected, 1, new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
