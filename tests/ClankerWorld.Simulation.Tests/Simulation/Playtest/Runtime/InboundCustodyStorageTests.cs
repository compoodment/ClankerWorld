using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class InboundCustodyStorageTests
{
    [Theory]
    [InlineData("personal", 60, false)]
    [InlineData("borrowed", 60, false)]
    [InlineData("none", 60, false)]
    [InlineData("personal", 56, false)]
    [InlineData("borrowed", 56, false)]
    [InlineData("personal", 57, true)]
    [InlineData("borrowed", 57, true)]
    [InlineData("personal", 56, true)]
    [InlineData("borrowed", 56, true)]
    public async Task CustodyOrdersLeaveRoomForAnActuallyCollectedJugDelivery(string mode, int initialStock, bool vessel)
    {
        using var setup = NormalPathWorld.CreateGenerated("inbound-personal-storage", _ => new CustodyChoice("safe_idle"));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var household = house.HouseholdId!;
        var adults = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == household).Take(2).Select(person => person.Id).ToArray();
        var hauler = adults[0];
        var actor = adults[1];
        var occupied = state.Inhabitants.Where(person => !adults.Contains(person.InhabitantId)).Select(person => person.Position).ToHashSet();
        var neighbors = state.Map.FootNeighbors(house.Position).Where(position => state.Map.IsPassable(position) &&
            !occupied.Contains(position) && state.Map.Resources.All(resource => resource.Position != position) &&
            state.Map.CampObjects.All(item => item.Position != position)).Take(2).ToArray();
        Assert.Equal(2, neighbors.Length);
        var inventory = state.Society.Society.Inventory;
        var stored = inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        Assert.InRange(stored, 0, initialStock - 1);
        inventory = InventoryFixture.AddLot(inventory, "inbound-room-filler", "stone", household, initialStock - stored,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "000-inbound-jug", "water_jug", household, 1,
            groundPosition: new(neighbors[0].X, neighbors[0].Y));
        inventory = InventoryFixture.AddLot(inventory, "inbound-water", "fresh_water", household, 3,
            containerLotId: "000-inbound-jug");
        var woodOwner = mode == "borrowed" ? household : actor;
        var quantity = vessel ? 1 : 4;
        inventory = InventoryFixture.AddLot(inventory, "custody-wood", vessel ? "water_jug" : "wood", woodOwner, quantity);
        if (vessel)
            inventory = InventoryFixture.AddLot(inventory, "custody-water", "fresh_water", woodOwner, 3, containerLotId: "custody-wood");
        if (mode == "borrowed")
            inventory = InventoryFixture.Relocate(inventory, "borrow-custody-wood", "custody-wood", household, quantity, carrierId: actor);
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear) with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == hauler ? neighbors[0] : person.InhabitantId == actor ? neighbors[1] : person.Position,
                HungerBasisPoints = 10_000,
                Survival = person.Survival is { } survival ? survival with
                { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000, IllnessBasisPoints = 0 } : null,
                LastDecisionContext = null,
                Project = null,
                TravelCooldownTicks = 0,
            }).ToArray(),
        };
        var hauling = new CustodyChoice("haul_household_stock");
        var ordering = new CustodyChoice(mode == "borrowed" ? "return_borrowed" : vessel ? "store_goods" : "store_material");
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == hauler ? hauling : id == actor ? ordering : new CustodyChoice("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 8 && world.Society.Inventory.GetLot("000-inbound-jug").DeliveryBuildingId is null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var jug = world.Society.Inventory.GetLot("000-inbound-jug");
        Assert.Equal((hauler, (string?)null, house.InstanceId), (jug.OwnerId, jug.CarrierId, jug.DeliveryBuildingId));
        Assert.Equal(initialStock, world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
        var beforeRoomQuery = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(64 - initialStock - 4, world.DestinationRoom(house.InstanceId));
        Assert.Equal(64 - initialStock, world.DestinationRoom(house.InstanceId, jug));
        Assert.Equal(beforeRoomQuery, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        hauling.Preferred = "safe_idle";
        if (mode != "none") world.SubmitInstruction(new("custody-capacity", "owner:test", actor, OwnerInstructionKind.MustDo,
            mode == "borrowed" ? vessel ? "return one borrowed water jug" : "return four borrowed wood" :
                vessel ? "store one water jug in my House" : "store four wood in my House"));
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var replayHauling = new CustodyChoice("safe_idle");
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == hauler ? replayHauling : id == actor ? new CustodyChoice(ordering.Preferred) : new CustodyChoice("safe_idle"));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 4; tick++) await TickTogether(world, replay);
        if (mode != "none" && initialStock != 56)
        {
            var order = world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "custody-capacity").Order!;
            Assert.Equal(("blocked", 0), (order.Status, order.CompletedUnits));
            Assert.Null(world.Society.Inventory.GetLot("custody-wood").StorageBuildingId);
        }
        hauling.Preferred = replayHauling.Preferred = "haul_household_stock";
        for (var tick = 0; tick < 12; tick++)
        {
            await TickTogether(world, replay);
            if (world.Society.Inventory.GetLot("000-inbound-jug").StorageBuildingId == house.InstanceId)
                hauling.Preferred = replayHauling.Preferred = "safe_idle";
        }
        jug = world.Society.Inventory.GetLot("000-inbound-jug");
        Assert.Equal((household, house.InstanceId, (string?)null, (string?)null),
            (jug.OwnerId, jug.StorageBuildingId, jug.CarrierId, jug.DeliveryBuildingId));
        var water = world.Society.Inventory.GetLot("inbound-water");
        Assert.Equal((household, house.InstanceId, 3, "000-inbound-jug"),
            (water.OwnerId, water.StorageBuildingId, water.Quantity, water.ContainerLotId));
        var wood = world.Society.Inventory.GetLot("custody-wood");
        Assert.Equal(woodOwner, wood.OwnerId);
        Assert.Equal(quantity, wood.Quantity);
        Assert.Equal(initialStock == 56 && mode != "none" ? house.InstanceId : null, wood.StorageBuildingId);
        if (vessel)
        {
            var contents = world.Society.Inventory.GetLot("custody-water");
            Assert.Equal((woodOwner, 3, "custody-wood", wood.StorageBuildingId, wood.CarrierId),
                (contents.OwnerId, contents.Quantity, contents.ContainerLotId, contents.StorageBuildingId, contents.CarrierId));
        }
        if (mode != "none" && initialStock == 56)
        {
            var order = world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "custody-capacity").Order!;
            Assert.Equal(("finished", quantity), (order.Status, order.CompletedUnits));
        }
        // Ordinary work may use capacity left over after the delivery. It
        // must not exceed the House limit or displace either checked family.
        Assert.InRange(world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity),
            initialStock + 4 + (initialStock == 56 && mode != "none" ? 4 : 0), 64);
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == hauler ? new CustodyChoice(hauling.Preferred) : id == actor ? new CustodyChoice(ordering.Preferred) : new CustodyChoice("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        await TickTogether(world, reload);
        world.Validate();
        reload.Validate();
    }

    private static async Task TickTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    private sealed class CustodyChoice(string preferred) : IDecisionProvider
    {
        public string Preferred { get; set; } = preferred;
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var chosen = observation.Candidates.FirstOrDefault(item => item.Id == Preferred) ??
                observation.Candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind,
                ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                chosen.Id, 1, observation.Candidates.ToDictionary(item => item.Id, item => item.Id == chosen.Id ? 1d : 0d)));
        }
    }
}
