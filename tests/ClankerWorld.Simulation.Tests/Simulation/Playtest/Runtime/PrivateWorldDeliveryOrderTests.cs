using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldDeliveryOrderTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";
    private const string House = "first-town-house-a";
    private const string Farmhouse = "first-town-farmhouse";
    private const string Smith = "first-town-blacksmith";
    private const string Clinic = "delivery-clinic";
    private const string Silo = "delivery-silo";
    private static readonly string[] ClinicJugFamily = ["clinic-jug", "clinic-water", "clinic-water-second"];
    private static readonly Lazy<byte[]> Baseline = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("personal-storage-orders", _ => new DeliveryChoices());
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });
    private static readonly Lazy<byte[]> ClinicBaseline = new(() => WithBuilding(Clinic, CareContent.Clinic1x2().CanonicalId));
    private static readonly Lazy<byte[]> SiloBaseline = new(() => WithBuilding(Silo, SiloContent.Silo1x1().CanonicalId));

    [Fact]
    public async Task HousePickupEarnsNothingUntilTheExactUnreservedQuantityReachesItsBoundHouseAcrossReplay()
    {
        var state = Prepared();
        var actor = Actor(state);
        var source = SourceNear(state, Home(state, House).Position);
        state = At(state, actor, source);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "house-cloth", "cloth", Alpha, 3,
            conditionBasisPoints: 6_000, groundPosition: new(source.X, source.Y));
        inventory = InventoryFixture.Reserve(inventory, "cloth-hold", Alpha, "house-cloth", 1, "other_work", 100);
        inventory = InventoryFixture.AddLot(inventory, "foreign-cloth", "cloth", Beta, 2, groundPosition: new(source.X, source.Y));
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "house-delivery", "haul two cloth to my House");
        await Tick(world);
        var order = Order(world, receipt);
        Assert.Equal(("deliver_stock", "goods_items", 0), (order.Action, order.ProgressUnit, order.CompletedUnits));
        Assert.Null(order.LastEffectId);
        Assert.Equal((House, Alpha, Home(state, House).Position, "house_stock", 2),
            (order.TargetStorageBuildingId, order.TargetStorageOwnerId, order.TargetStoragePosition, order.DeliveryRoute, order.DeliveryQuantity));
        var shipment = world.Society.Inventory.GetLot(order.DeliveryLotId!);
        Assert.Equal((actor, House, 2), (shipment.OwnerId, shipment.DeliveryBuildingId, shipment.Quantity));
        Assert.Equal(0, Stored(world, House, "cloth"));
        Assert.Single(world.ExportState().Events, item => item.Kind == "owner_stock_picked_up");
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Null(Order(world, receipt).DeliveryLotId);
        Assert.Null(Order(world, receipt).DeliveryQuantity);
        Assert.StartsWith("delivery:stock:", Order(world, receipt).LastEffectId!);
        var delivered = world.Society.Inventory.GetLot(shipment.Id);
        Assert.Equal((Alpha, House, 2, 6_000), (delivered.OwnerId, delivered.StorageBuildingId, delivered.Quantity, delivered.ConditionBasisPoints));
        Assert.Null(delivered.DeliveryBuildingId);
        Assert.Equal(1, world.Society.Inventory.GetLot("house-cloth").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("cloth-hold").State);
        Assert.Equal((Beta, 2), (world.Society.Inventory.GetLot("foreign-cloth").OwnerId, world.Society.Inventory.GetLot("foreign-cloth").Quantity));
        Assert.Single(world.ExportState().Events, item => item.Kind == "owner_stock_delivered");
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldInstructionOrder>(
            JsonSerializer.Serialize(projected, json), json)!;
        Assert.Equal(("cloth", "house", "goods_items", 2),
            (client.TargetItemKind, client.TargetBuildingKind, client.ProgressUnit, client.CompletedUnits));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FarmLoadsRespectAnExplicitDestinationAndTakeOnlyTheRequestedGrainFromAnOversizedPot(bool silo)
    {
        var state = Prepared(silo ? SiloBaseline.Value : null);
        var actor = Actor(state);
        var destination = Home(state, silo ? Silo : Farmhouse);
        var source = SourceNear(state, destination.Position);
        state = At(state, actor, source);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "grain-pot", "storage_pot", Alpha, 1, groundPosition: new(source.X, source.Y));
        inventory = InventoryFixture.AddLot(inventory, "grain-content", "grain", Alpha, 8, containerLotId: "grain-pot");
        using var world = Restore(WithInventory(state, inventory));
        Assert.Equal(0, Stored(world, Farmhouse, "grain"));
        var receipt = Submit(world, actor, "farm-delivery", "haul two grain to my " + (silo ? "Silo" : "Farmhouse"));
        await Tick(world);
        Assert.Equal((0, destination.InstanceId, 2),
            (Order(world, receipt).CompletedUnits, Order(world, receipt).TargetStorageBuildingId, Order(world, receipt).DeliveryQuantity));
        Assert.Equal((Alpha, 6), (world.Society.Inventory.GetLot("grain-content").OwnerId, world.Society.Inventory.GetLot("grain-content").Quantity));
        Assert.Equal((source.X, source.Y), (world.Society.Inventory.GetLot("grain-pot").GroundPosition!.Value.X, world.Society.Inventory.GetLot("grain-pot").GroundPosition!.Value.Y));
        Assert.Equal(Alpha, world.Society.Inventory.GetLot("grain-pot").OwnerId);
        var shipment = world.Society.Inventory.GetLot(Order(world, receipt).DeliveryLotId!);
        Assert.Null(shipment.ContainerLotId);
        Assert.Equal(2, shipment.Quantity);
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(2, Stored(world, destination.InstanceId, "grain"));
        Assert.Equal(2, Order(world, receipt).CompletedUnits);
        Assert.Equal(8, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain" &&
            (lot.OwnerId == Alpha || lot.OwnerId == actor)).Sum(lot => lot.Quantity));
        if (silo) Assert.Equal(0, Stored(world, Farmhouse, "grain"));
    }

    [Theory]
    [InlineData(63, 0, false, Farmhouse)]
    [InlineData(64, 0, false, Farmhouse)]
    [InlineData(63, 0, true, Farmhouse)]
    [InlineData(63, 95, false, null)]
    [InlineData(61, 0, false, Silo)]
    [InlineData(95, 0, false, Farmhouse)]
    public async Task RoutineFarmHaulFindsStorageForTheWholePot(int siloStock, int farmhouseStock, bool ordered, string? expectedDestination)
    {
        var state = Prepared(SiloBaseline.Value);
        var actor = Actor(state);
        var source = SourceNear(state, Home(state, Farmhouse).Position);
        state = At(state, actor, source);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "routine-silo-stock", "potato_seed", Alpha,
            siloStock, storageBuildingId: Silo);
        if (farmhouseStock > 0)
            inventory = InventoryFixture.AddLot(inventory, "routine-farmhouse-stock", "potato_seed", Alpha,
                farmhouseStock, storageBuildingId: Farmhouse);
        inventory = InventoryFixture.AddLot(inventory, "routine-pot", "storage_pot", Alpha, 1, groundPosition: new(source.X, source.Y));
        inventory = InventoryFixture.AddLot(inventory, "routine-potatoes", "potatoes", Alpha, 2, containerLotId: "routine-pot");
        var choices = new DeliveryChoices(farmHaulActor: actor);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => choices);
        OwnerInstructionReceipt? receipt = ordered ? Submit(world, actor, "routine-pot-order", "haul two potatoes to my Farmhouse") : null;
        await Tick(world);
        var canFit = expectedDestination is not null;
        var observation = Assert.Single(choices.Requests, request => request.InhabitantId == actor);
        if (!ordered)
        {
            Assert.Equal(canFit, observation.Candidates.Any(candidate => candidate.Id == "haul_farm_grain"));
            if (canFit) Assert.Equal(expectedDestination, world.Society.Inventory.GetLot("routine-pot").DeliveryBuildingId);
        }
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new DeliveryChoices(farmHaulActor: actor));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 24 && canFit && world.Society.Inventory.GetLot("routine-pot").StorageBuildingId != expectedDestination; tick++)
            await TickTogether(world, replay);
        if (!canFit) await TickTogether(world, replay);
        var pot = world.Society.Inventory.GetLot("routine-pot");
        var potatoes = world.Society.Inventory.GetLot("routine-potatoes");
        Assert.Equal((Alpha, 1, expectedDestination), (pot.OwnerId, pot.Quantity, pot.StorageBuildingId));
        Assert.Equal((Alpha, 2, "routine-pot"), (potatoes.OwnerId, potatoes.Quantity, potatoes.ContainerLotId));
        Assert.Null(pot.DeliveryBuildingId);
        if (!canFit) Assert.Equal(new InventoryGroundPosition(source.X, source.Y), pot.GroundPosition);
        if (receipt is not null) Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(siloStock, world.Society.Inventory.GetLot("routine-silo-stock").Quantity);
        world.Validate();
    }

    [Fact]
    public async Task FlourTravelsFromTheFarmhouseToTheHouseWithoutMovingOtherFarmStock()
    {
        var state = Prepared();
        var actor = Actor(state);
        state = At(state, actor, Home(state, Farmhouse).Position);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "farm-flour", "flour", Alpha, 3, storageBuildingId: Farmhouse);
        inventory = InventoryFixture.AddLot(inventory, "farm-grain", "grain", Alpha, 2, storageBuildingId: Farmhouse);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "flour-delivery", "haul two flour to my House");
        await Tick(world);
        Assert.Equal(("farm_flour", 0, 2),
            (Order(world, receipt).DeliveryRoute, Order(world, receipt).CompletedUnits, Order(world, receipt).DeliveryQuantity));
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(2, Stored(world, House, "flour"));
        Assert.Equal(1, Stored(world, Farmhouse, "flour"));
        Assert.Equal(2, Stored(world, Farmhouse, "grain"));
    }

    [Theory]
    [InlineData("available")]
    [InlineData("finite-two")]
    [InlineData("carry-room")]
    [InlineData("storage-room")]
    [InlineData("reserved-content")]
    public async Task ClinicWaterCountsTheActualFourWaterUnitsAndKeepsItsWholeJugAndReservations(string boundary)
    {
        var state = Prepared(ClinicBaseline.Value);
        var actor = Actor(state);
        state = At(state, actor, Home(state, House).Position);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "clinic-herbs", "medicinal_herbs", Alpha, 4, storageBuildingId: Clinic);
        inventory = InventoryFixture.AddLot(inventory, "clinic-fuel", "wood", Alpha, 2, storageBuildingId: Clinic);
        if (boundary == "available")
        {
            // The House keeps two cooking batches; the Clinic shipment is genuine surplus.
            inventory = InventoryFixture.AddLot(inventory, "house-reserve-jug", "water_jug", Alpha, 1, storageBuildingId: House);
            inventory = InventoryFixture.AddLot(inventory, "house-reserve-water", "fresh_water", Alpha, 2,
                storageBuildingId: House, containerLotId: "house-reserve-jug");
        }
        inventory = InventoryFixture.AddLot(inventory, "clinic-jug", "water_jug", Alpha, 1, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "clinic-water", "fresh_water", Alpha,
            boundary == "available" ? 2 : 4, storageBuildingId: House, containerLotId: "clinic-jug");
        if (boundary == "available") inventory = InventoryFixture.AddLot(inventory, "clinic-water-second", "fresh_water", Alpha,
            2, storageBuildingId: House, containerLotId: "clinic-jug");
        if (boundary == "carry-room") inventory = InventoryFixture.AddLot(inventory, "ballast", "stone", actor, 4);
        if (boundary == "storage-room")
        {
            var clinic = Home(state, Clinic);
            var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == clinic.DefinitionId);
            var stored = inventory.Lots.Where(lot => lot.StorageBuildingId == Clinic).Sum(lot => lot.Quantity);
            var capacity = BuildingStorageRules.Capacity(definition, clinic)!.Value;
            inventory = InventoryFixture.AddLot(inventory, "clinic-ballast", "stone", Alpha,
                capacity - stored - 4, storageBuildingId: Clinic);
            Assert.Equal(4, capacity - inventory.Lots.Where(lot => lot.StorageBuildingId == Clinic).Sum(lot => lot.Quantity));
        }
        if (boundary == "reserved-content") inventory = InventoryFixture.Reserve(inventory, "water-held", Alpha,
            "clinic-water", 1, "other_work", 100);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "water-delivery", "supply " + (boundary == "finite-two" ? "two" : "four") + " fresh water to my Clinic");
        await Tick(world);
        if (boundary != "available")
        {
            Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.NotEmpty(Order(world, receipt).BlockedReason!);
            Assert.Equal((Alpha, House, 4), (world.Society.Inventory.GetLot("clinic-water").OwnerId,
                world.Society.Inventory.GetLot("clinic-water").StorageBuildingId, world.Society.Inventory.GetLot("clinic-water").Quantity));
            Assert.Equal((Alpha, House), (world.Society.Inventory.GetLot("clinic-jug").OwnerId, world.Society.Inventory.GetLot("clinic-jug").StorageBuildingId));
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "owner_stock_picked_up");
            if (boundary == "reserved-content") Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("water-held").State);
            using var refused = Reload(world);
            await TickTogether(world, refused);
            return;
        }
        Assert.Equal((Alpha, House, 2), (world.Society.Inventory.GetLot("house-reserve-water").OwnerId,
            world.Society.Inventory.GetLot("house-reserve-water").StorageBuildingId,
            world.Society.Inventory.GetLot("house-reserve-water").Quantity));
        Assert.Equal((0, "clinic-jug", 4),
            (Order(world, receipt).CompletedUnits, Order(world, receipt).DeliveryLotId, Order(world, receipt).DeliveryQuantity));
        Assert.Equal(5, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
        Assert.All(ClinicJugFamily, id =>
        {
            Assert.Equal(actor, world.Society.Inventory.GetLot(id).OwnerId);
            Assert.Equal(Clinic, world.Society.Inventory.GetLot(id).DeliveryBuildingId);
        });
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(("finished", 4), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal((Alpha, House, 2), (world.Society.Inventory.GetLot("house-reserve-water").OwnerId,
            world.Society.Inventory.GetLot("house-reserve-water").StorageBuildingId,
            world.Society.Inventory.GetLot("house-reserve-water").Quantity));
        Assert.All(ClinicJugFamily, id =>
        {
            Assert.Equal(Alpha, world.Society.Inventory.GetLot(id).OwnerId);
            Assert.Equal(Clinic, world.Society.Inventory.GetLot(id).StorageBuildingId);
            Assert.Null(world.Society.Inventory.GetLot(id).DeliveryBuildingId);
        });
        Assert.Equal(("clinic-jug", 2), (world.Society.Inventory.GetLot("clinic-water").ContainerLotId, world.Society.Inventory.GetLot("clinic-water").Quantity));
        Assert.Equal(("clinic-jug", 2), (world.Society.Inventory.GetLot("clinic-water-second").ContainerLotId, world.Society.Inventory.GetLot("clinic-water-second").Quantity));
        Assert.Empty(world.WorldSimulation.ProductionJobs);
    }

    [Theory]
    [InlineData("wood", false)]
    [InlineData("iron_ore", true)]
    public async Task SmithSuppliesUseTheRealHouseholdInputOrPersonalOreAndNeverCreditWalking(string kind, bool personal)
    {
        var state = Prepared(household: Beta);
        var actor = Actor(state, Beta);
        var source = SourceNear(state, Home(state, Smith).Position);
        state = At(state, actor, source);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "smith-input", kind,
            personal ? actor : Beta, 3, groundPosition: personal ? null : new(source.X, source.Y));
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "smith-delivery", "supply two " + kind.Replace('_', ' ') + " to my Blacksmith");
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(0, Stored(world, Smith, kind));
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(2, Stored(world, Smith, kind));
        Assert.Equal(1, world.Society.Inventory.GetLot("smith-input").Quantity);
        Assert.Equal(personal ? actor : Beta, world.Society.Inventory.GetLot("smith-input").OwnerId);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
    }

    [Theory]
    [InlineData("berries")]
    [InlineData("bread")]
    [InlineData("restaurant_meal")]
    public async Task DeliveringPersonalFoodLeavesOneMealAndDoesNotTakeAnotherOwnersFood(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "spare-berries", kind, actor, 3);
        inventory = InventoryFixture.AddLot(inventory, "foreign-berries", kind, Beta, 5, groundPosition: new(Home(state, House).Position.X, Home(state, House).Position.Y));
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "food-delivery", "deliver three " + kind.Replace('_', ' ') + " to my House");
        await Tick(world);
        Assert.Equal(2, Order(world, receipt).CompletedUnits);
        Assert.Equal(2, Stored(world, House, kind));
        Assert.Equal((actor, 1), (world.Society.Inventory.GetLot("spare-berries").OwnerId, world.Society.Inventory.GetLot("spare-berries").Quantity));
        await Tick(world);
        Assert.Equal(("blocked", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal((Beta, 5), (world.Society.Inventory.GetLot("foreign-berries").OwnerId, world.Society.Inventory.GetLot("foreign-berries").Quantity));
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    private static byte[] WithBuilding(string id, string definition)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Baseline.Value);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "delivery-building-wood", "wood", Alpha, 32, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "delivery-building-stone", "stone", Alpha, 8, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var home = Home(state, id == Silo ? Farmhouse : House);
        var positions = Enumerable.Range(-8, 17).SelectMany(y => Enumerable.Range(-8, 17).Select(x =>
            new GridPoint(home.Position.X + x, home.Position.Y + y))).OrderBy(point => state.Map.FootDistance(point, home.Position));
        Assert.Contains(positions, point => world.PlaceBuilding(id, definition, point, Alpha).Applied);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }
    private static PrivateWorldRuntimeState Prepared(byte[]? baseline = null, string household = Alpha)
    {
        var state = PrivateWorldRuntimeCodec.Decode(baseline ?? Baseline.Value);
        var actor = Actor(state, household);
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor && lot.OwnerId != household).ToArray(),
        });
        state = state with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                LastDecisionContext = null,
            }).ToArray(),
        };
        return At(state, actor, Home(state, household == Alpha ? House : "first-town-house-b").Position);
    }
    private static PrivateWorldRuntimeState At(PrivateWorldRuntimeState state, string actor, GridPoint position)
    {
        var previous = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Position = position, LastDecisionContext = null }
            : person.Position == position ? person with { Position = previous } : person).ToArray()
        };
    }
    private static GridPoint SourceNear(PrivateWorldRuntimeState state, GridPoint destination) => state.Map.FootNeighbors(destination)
        .Where(point => state.Map.IsPassable(point) && state.Inhabitants.All(person => person.Position != point) &&
            state.Map.Resources.All(resource => resource.Position != point) && state.Map.CampObjects.All(item => item.Position != point))
        .OrderBy(point => point.Y).ThenBy(point => point.X).First();
    private static PlacedBuilding Home(PrivateWorldRuntimeState state, string id) => state.WorldSimulation!.Buildings.Single(building => building.InstanceId == id);
    private static string Actor(PrivateWorldRuntimeState state, string household = Alpha) => state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new DeliveryChoices());
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        return replay;
    }
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text, Queue: queue));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static int Stored(PrivateWorldRuntime world, string building, string kind) => world.Society.Inventory.Lots
        .Where(lot => lot.ItemKind == kind && lot.StorageBuildingId == building).Sum(lot => lot.Quantity);
    private static async Task Tick(PrivateWorldRuntime world) => Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    private static async Task TickTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        await Tick(world);
        await Tick(replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }
    private static async Task FinishTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay, OwnerInstructionReceipt receipt)
    {
        for (var tick = 0; tick < 48 && Order(world, receipt).Status != "finished"; tick++) await TickTogether(world, replay);
        Assert.Equal("finished", Order(world, receipt).Status);
    }
    private sealed class DeliveryChoices(DecisionProviderKind kind = DecisionProviderKind.Deterministic, bool hold = false,
        string? farmHaulActor = null) : IDecisionProvider
    {
        private static readonly string[] FarmHaulChoices = ["haul_farm_grain", "haul_household_stock", "safe_idle"];
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 0;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            Requests.Enqueue(observation);
            if (hold && !Started.Task.IsCompleted && observation.OperativeOrderInstructionId is not null)
            {
                Started.TrySetResult(true);
                await Release.Task;
                Returned.TrySetResult(true);
            }
            var selected = observation.OperativeOrderInstructionId is not null && observation.Candidates.Any(candidate => candidate.Id == "deliver_stock")
                ? "deliver_stock" : observation.InhabitantId == farmHaulActor
                ? FarmHaulChoices.First(id => observation.Candidates.Any(candidate => candidate.Id == id &&
                    (id != "haul_household_stock" || candidate.DestinationId is Farmhouse or Silo)))
                : "safe_idle";
            return new(request.RequestId, observation.InhabitantId, Kind, request.ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 });
        }
    }
}
