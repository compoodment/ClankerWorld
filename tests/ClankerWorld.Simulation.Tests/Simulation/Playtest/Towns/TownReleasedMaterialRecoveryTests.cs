using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownReleasedMaterialRecoveryTests
{
    [Fact]
    public async Task ResidentsReturnActualGroundLoadsAfterAHallIsCancelledByGrantedHouseholdRights()
    {
        var state = await CancelledStateAsync();
        var released = state.Towns![0].Projects[0].Deliveries.Select(delivery => delivery.LotId).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(state.Society.Society.Inventory.Lots, lot => released.Contains(lot.Id) && lot.GroundPosition is not null);
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new RecoveryChoice());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new RecoveryChoice());
        for (var tick = 0; tick < 160 && StoredMaterials(world, warehouse.InstanceId) != 44; tick++)
        {
            var before = world.Inhabitants.ToDictionary(person => person.InhabitantId, person => person.Position);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            foreach (var person in world.Inhabitants)
            {
                Assert.True(before[person.InhabitantId] == person.Position || state.Map.CanFootStep(before[person.InhabitantId], person.Position));
                Assert.InRange(PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, person.InhabitantId, person.Equipment),
                    0, PersonalEquipmentRules.Capacity(world.Society.Inventory, person.InhabitantId, person.Equipment));
            }
        }
        Assert.Equal(44, TownProjectScenario.MaterialQuantity(world.Society.Inventory, TownBorderRules.FirstTownId));
        Assert.Equal(44, StoredMaterials(world, warehouse.InstanceId));
        Assert.Equal("cancelled", world.Towns[0].Projects[0].Stage);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("unrelated-wood").State);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("unrelated-stone").State);
        world.Validate();
    }

    private static async Task<PrivateWorldRuntimeState> CancelledStateAsync()
    {
        using var scenario = await TownProjectScenario.ApprovedAsync(initialTownStock: true);
        scenario.Policy.Supply = true;
        scenario.Policy.PersonalSupply = false;
        await scenario.UntilAsync(() => scenario.Project.Deliveries.Any(delivery => delivery.DeliveredTick is not null), 80);
        scenario.Policy.Supply = false;
        var requested = scenario.World.RequestHouseholdLandUse("recover:hall-site", TownProjectScenario.Author,
            TownBorderRules.FirstTownId, WorldContentSimulationRules.Footprint(TownHallContent.Hall3x4(), scenario.Project.Plan.Site).ToArray());
        Assert.True(requested.Applied, requested.Failure);
        await scenario.UntilAsync(() => scenario.Project.Stage == "blocked", 4);
        scenario.Policy.AcceptLandUse = true;
        await scenario.UntilAsync(() => scenario.Project.Stage == "cancelled", 160);
        return scenario.World.ExportState();
    }

    [Theory]
    [InlineData("last-carry-space")]
    [InlineData("urgent-food")]
    [InlineData("urgent-warmth")]
    [InlineData("claimed")]
    [InlineData("private")]
    [InlineData("full-warehouse")]
    [InlineData("already-carried-return")]
    public async Task RecoveryLeavesSurvivalSpaceAndRefusesUnavailableOrForeignGoods(string situation)
    {
        var (state, lot, actor) = await AtReleasedGroundAsync();
        var inventory = state.Society.Society.Inventory;
        if (situation == "last-carry-space")
            inventory = InventoryFixture.AddLot(inventory, "recovery:private-load", "wood", actor,
                PersonalEquipmentRules.BaseCapacity - 1);
        if (situation == "urgent-food")
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 1_000 } : person).ToArray()
            };
        if (situation == "urgent-warmth")
            state = SettlementWeatherTestFixture.WithWeather(state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { Survival = new SurvivalCondition(1_000) } : person).ToArray(),
            }, WeatherKind.Storm);
        var released = state.Towns![0].Projects[0].Deliveries.Select(delivery => delivery.LotId).ToHashSet(StringComparer.Ordinal);
        foreach (var item in inventory.Lots.Where(item => released.Contains(item.Id) && item.GroundPosition is not null &&
                     PersonalEquipmentRules.AvailableQuantity(inventory, item) > 0).ToArray())
        {
            if (situation == "claimed")
                inventory = InventoryFixture.Reserve(inventory, "recovery:other-job:" + item.Id, item.OwnerId, item.Id,
                    item.Quantity, "unrelated-job", long.MaxValue);
            if (situation == "private")
                inventory = InventoryFixture.Transfer(inventory, "recovery:private-transfer:" + item.Id, item.OwnerId, actor,
                    item.Id, item.Quantity, "fixture_transfer", destinationGroundPosition: item.GroundPosition);
        }
        if (situation == "full-warehouse")
            inventory = FillWarehouse(state, inventory, room: 0);
        if (situation == "already-carried-return")
        {
            var other = inventory.Lots.First(item => item.Id != lot.Id && released.Contains(item.Id) &&
                item.GroundPosition is not null);
            inventory = InventoryFixture.ReleaseReservation(inventory, "recovery:fixture-claim:" + other.Id, "fixture_custody");
            inventory = InventoryFixture.Relocate(inventory, "recovery:other-carrier", other.Id, other.OwnerId,
                other.Quantity, carrierId: state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId);
            inventory = FillWarehouse(state, inventory, room: 2);
        }
        state = FoodCapacityTestFixture.WithInventory(state, inventory);
        var policy = new RecoveryChoice(actor);
        using var world = PrivateWorldRuntime.Restore(state, _ => policy);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(policy.Offers);
        Assert.DoesNotContain(policy.Offers.SelectMany(ids => ids), id => id.StartsWith("town_project_recover:", StringComparison.Ordinal));
        Assert.Equal(inventory.GetLot(lot.Id) with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot(lot.Id));
        if (situation == "claimed") Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("recovery:other-job:" + lot.Id).State);
        world.Validate();
    }

    [Theory]
    [InlineData("carry-space")]
    [InlineData("warehouse-room")]
    public async Task APartialRecoveryTracksTheRealSplitAndReturnsOnlyWhatFits(string limit)
    {
        var (state, lot, actor) = await AtReleasedGroundAsync();
        Assert.True(lot.Quantity >= 2);
        var inventory = state.Society.Society.Inventory;
        if (limit == "carry-space")
            inventory = InventoryFixture.AddLot(inventory, "recovery:private-load", "wood", actor,
                PersonalEquipmentRules.BaseCapacity - 3);
        else inventory = FillWarehouse(state, inventory, room: 2);
        state = FoodCapacityTestFixture.WithInventory(state, inventory);
        var policy = new RecoveryChoice(actor);
        using var world = PrivateWorldRuntime.Restore(state, _ => policy);
        var initialStored = world.Society.Inventory.Lots.Where(item => item.StorageBuildingId == "first-town-warehouse").Sum(item => item.Quantity);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var recovered = Assert.Single(world.Towns[0].Projects[0].Deliveries, delivery => delivery.ReleaseReason == "Unused construction materials are being returned.");
        Assert.Equal(2, recovered.Quantity);
        var split = world.Society.Inventory.GetLot(recovered.LotId);
        Assert.Equal(actor, split.CarrierId);
        Assert.Equal(lot.OwnerId, split.OwnerId);
        Assert.Equal(lot.Id, split.ProvenanceLotId);
        Assert.Equal(lot.ConditionBasisPoints, split.ConditionBasisPoints);
        Assert.Equal(lot.Quantity - 2, world.Society.Inventory.GetLot(lot.Id).Quantity);
        Assert.Equal(inventory.Lots.Where(item => item.OwnerId == lot.OwnerId && item.ItemKind == lot.ItemKind).Sum(item => item.Quantity),
            world.Society.Inventory.Lots.Where(item => item.OwnerId == lot.OwnerId && item.ItemKind == lot.ItemKind).Sum(item => item.Quantity));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new RecoveryChoice(actor));
        for (var tick = 0; tick < 60 && world.Society.Inventory.GetLot(split.Id).StorageBuildingId is null; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal("first-town-warehouse", world.Society.Inventory.GetLot(split.Id).StorageBuildingId);
        Assert.Equal(initialStored + 2, world.Society.Inventory.Lots.Where(item => item.StorageBuildingId == "first-town-warehouse").Sum(item => item.Quantity));
        world.Validate();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AHungryCarrierSetsDownReleasedTownGoodsAndEatsBeforeRecoveringThem(bool reservedRemainder, bool ownerOrder)
    {
        var (state, lot, actor) = await AtReleasedGroundAsync();
        var inventory = InventoryFixture.Relocate(state.Society.Society.Inventory, "recovery:held-load", lot.Id,
            lot.OwnerId, lot.Quantity, carrierId: actor);
        if (reservedRemainder)
            inventory = InventoryFixture.Reserve(inventory, "recovery:held-claim", lot.OwnerId, lot.Id, 1,
                "unrelated-job", long.MaxValue);
        var household = state.Society.Society.Inhabitants.Single(person => person.Id == actor).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == household);
        inventory = InventoryFixture.AddLot(inventory, "recovery:meal", "berries", household, 1,
            storageBuildingId: house.InstanceId);
        state = FoodCapacityTestFixture.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
        };
        var policy = new RecoveryChoice(actor);
        using var world = PrivateWorldRuntime.Restore(state, _ => policy);
        if (ownerOrder)
        {
            var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
            var receipt = world.SubmitInstruction(new("recovery:order", "owner:test", actor, OwnerInstructionKind.MustDo,
                $"move to {warehouse.Position.X},{warehouse.Position.Y}"));
            Assert.Contains(world.ExportState().Instructions!, instruction => instruction.InstructionId == receipt.InstructionId);
        }
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var dropped = Assert.Single(world.Society.Inventory.Lots, item =>
            item.OwnerId == lot.OwnerId && item.GroundPosition == lot.GroundPosition &&
            (item.Id == lot.Id || item.ProvenanceLotId == lot.Id));
        Assert.Equal(lot.Quantity - (reservedRemainder ? 1 : 0), dropped.Quantity);
        Assert.Null(dropped.CarrierId);
        Assert.Equal(lot.GroundPosition, dropped.GroundPosition);
        Assert.Equal(lot.OwnerId, dropped.OwnerId);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new RecoveryChoice(actor));
        for (var tick = 0; tick < 30 && world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints <= 1_000; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.True(world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints > 1_000);
        if (reservedRemainder)
        {
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("recovery:held-claim").State);
            Assert.Equal(actor, world.Society.Inventory.GetLot(lot.Id).CarrierId);
            Assert.Contains(world.Towns[0].Projects[0].Deliveries, delivery => delivery.LotId == dropped.Id && delivery.ReleasedTick is not null);
        }
        Assert.DoesNotContain(policy.Offers.SelectMany(ids => ids), id => id.StartsWith("town_project_recover:", StringComparison.Ordinal));
        world.Validate();
    }

    [Fact]
    public async Task ATemporaryPedestrianBarrierDoesNotMakeAReleasedCarrierDropAndPickUpTheSameLoad()
    {
        var (state, lot, actor) = await AtReleasedGroundAsync();
        var warehouse = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-warehouse");
        var blockers = state.Inhabitants.Where(person => person.InhabitantId != actor).ToArray();
        var origin = state.Map.Tiles.Select(tile => tile.Position).First(point =>
        {
            var exits = state.Map.FootNeighbors(point).ToArray();
            return state.Map.IsReachableOnFoot(point, warehouse.Position) && point != warehouse.Position &&
                exits.Length > 0 && exits.Length <= blockers.Length && !exits.Contains(warehouse.Position);
        });
        var positions = state.Map.FootNeighbors(origin).Select((point, index) => (blockers[index].InhabitantId, point))
            .ToDictionary(item => item.InhabitantId, item => item.point);
        state = FoodCapacityTestFixture.WithInventory(state, InventoryFixture.Relocate(state.Society.Society.Inventory,
            "recovery:barrier-load", lot.Id, lot.OwnerId, lot.Quantity, carrierId: actor)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = origin }
                : positions.TryGetValue(person.InhabitantId, out var blocked) ? person with { Position = blocked } : person).ToArray(),
        };
        var policy = new RecoveryChoice(actor);
        using var world = PrivateWorldRuntime.Restore(state, _ => policy);
        var before = world.Society.Inventory.GetLot(lot.Id);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(before with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot(lot.Id));
        Assert.Equal(origin, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Contains(policy.Offers.SelectMany(ids => ids), id => id.StartsWith("town_project_return:", StringComparison.Ordinal));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "town_project_material_returned" && item.Detail.Contains(lot.Id, StringComparison.Ordinal));
        world.Validate();
    }

    private static async Task<(PrivateWorldRuntimeState State, InventoryLot Lot, string Actor)> AtReleasedGroundAsync()
    {
        var state = await CancelledStateAsync();
        var ids = state.Towns![0].Projects[0].Deliveries.Where(delivery => delivery.ReleasedTick is not null)
            .Select(delivery => delivery.LotId).ToHashSet(StringComparer.Ordinal);
        var lot = state.Society.Society.Inventory.Lots.First(item => ids.Contains(item.Id) && item.GroundPosition is not null);
        var actor = TownProjectScenario.Author;
        var inventory = state.Society.Society.Inventory;
        foreach (var carried in inventory.Lots.Where(item => PersonalEquipmentRules.IsCarried(item, actor)).ToArray())
            inventory = InventoryFixture.Relocate(inventory, "recovery:clear:" + carried.Id, carried.Id, carried.OwnerId,
                carried.Quantity, groundPosition: new(state.Inhabitants.Single(person => person.InhabitantId == actor).Position.X,
                    state.Inhabitants.Single(person => person.InhabitantId == actor).Position.Y));
        var released = state.Towns![0].Projects[0].Deliveries.Where(delivery => delivery.ReleasedTick is not null)
            .Select(delivery => delivery.LotId).ToHashSet(StringComparer.Ordinal);
        foreach (var carried in inventory.Lots.Where(item => item.OwnerId == TownBorderRules.FirstTownId &&
            item.CarrierId is not null && released.Contains(item.Id)).ToArray())
        {
            var position = state.Inhabitants.Single(person => person.InhabitantId == carried.CarrierId).Position;
            inventory = InventoryFixture.Relocate(inventory, "recovery:released-clear:" + carried.Id, carried.Id,
                carried.OwnerId, carried.Quantity, groundPosition: new(position.X, position.Y));
        }
        // Isolate this physical load for capacity and urgency checks; the other
        // released goods remain Town-owned and reserved for a separate job.
        foreach (var other in inventory.Lots.Where(item => item.Id != lot.Id && released.Contains(item.Id) &&
                     item.GroundPosition is not null && PersonalEquipmentRules.AvailableQuantity(inventory, item) > 0).ToArray())
            inventory = InventoryFixture.Reserve(inventory, "recovery:fixture-claim:" + other.Id, other.OwnerId,
                other.Id, PersonalEquipmentRules.AvailableQuantity(inventory, other), "unrelated-job", long.MaxValue);
        state = FoodCapacityTestFixture.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = new GridPoint(lot.GroundPosition!.Value.X, lot.GroundPosition.Value.Y),
                    HungerBasisPoints = 9_500,
                    Project = null,
                    Equipment = null,
                    LastDecisionContext = null
                } : person).ToArray(),
        };
        return (state, lot, actor);
    }

    private static InventoryCheckpoint FillWarehouse(PrivateWorldRuntimeState state, InventoryCheckpoint inventory, int room)
    {
        var building = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-warehouse");
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        var capacity = BuildingStorageRules.Capacity(definition, building)!.Value;
        var stored = inventory.Lots.Where(item => item.StorageBuildingId == building.InstanceId).Sum(item => item.Quantity);
        return InventoryFixture.AddLot(inventory, "recovery:warehouse-fill", "wood", TownBorderRules.FirstTownId,
            capacity - stored - room, storageBuildingId: building.InstanceId);
    }

    private static int StoredMaterials(PrivateWorldRuntime world, string warehouse) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == TownBorderRules.FirstTownId &&
            lot.StorageBuildingId == warehouse && lot.ItemKind is "wood" or "stone").Sum(lot => lot.Quantity);

    private sealed class RecoveryChoice(string? actor = null) : IDecisionProvider
    {
        internal List<string[]> Offers { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            if (actor is null || observation.InhabitantId == actor)
                Offers.Add(observation.Candidates.Select(candidate => candidate.Id).ToArray());
            var selected = actor is not null && observation.InhabitantId != actor
                ? observation.Candidates.Single(candidate => candidate.Id == "safe_idle")
                : observation.Candidates.Where(candidate => candidate.DeterministicPriority <= 5 &&
                    (candidate.Id.StartsWith("town_project_return:", StringComparison.Ordinal) ||
                        candidate.Id is "consume_food" or "collect_shared_food" or "take_food_from_pot" or "make_room_for_food" or
                        "harvest_food" or "seek_food" or "wear_clothing" or "seek_warmth"))
                .OrderBy(candidate => candidate.DeterministicPriority)
                .ThenBy(candidate => candidate.Id.StartsWith("town_project_return:", StringComparison.Ordinal) ? 0 : 1).FirstOrDefault()
                ?? observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("town_project_", StringComparison.Ordinal))
                ?? observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId,
                Kind, ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected.Id, 1, observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}
