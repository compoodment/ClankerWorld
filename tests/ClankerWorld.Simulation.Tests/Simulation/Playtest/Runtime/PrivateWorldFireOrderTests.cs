using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldFireOrderTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";

    [Fact]
    public async Task ALegalLongWoodLotIdKeepsItsFullNativePaymentIdentityAcrossReload()
    {
        var state = Prepared(fuel: 0);
        var actor = Actor(state);
        var lotId = new string('w', 500);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, lotId, "wood", actor, 1));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "long-fuel-id", "light a fire");
        await Tick(world);
        AssertPaidFire(world, receipt, actor, House(state), lotId);
        var completion = Order(world, receipt).ShelterCompletion!;
        Assert.True(completion.FuelReservationId!.Length > 512);
        Assert.Equal(0, Wood(world, lotId));
        using var replay = Reload(world);
        Assert.Equal(completion, Order(replay, receipt).ShelterCompletion);
        await TickTogether(world, replay);
    }

    [Fact]
    public async Task AnOrderedFireConsumesOneOwnedWoodOnlyAtItsPinnedHearthAcrossReplay()
    {
        var state = Prepared(walking: true);
        var actor = Actor(state);
        var house = House(state);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "paid-fire", AtHouse(house));
        await Tick(world);
        var order = Order(world, receipt);
        Assert.Equal(("tend_fire", "fires", 1, 0, false),
            (order.Action, order.ProgressUnit, order.RequestedUnits, order.CompletedUnits, order.RepeatUntilCancelled));
        var binding = order.ShelterBinding;
        Assert.NotNull(binding);
        Assert.Equal(("building", house.InstanceId, house.DefinitionId, Alpha, house.Position, house.PlacedTick),
            (binding.Kind, binding.BuildingInstanceId, binding.DefinitionId, binding.OwnerId,
                binding.BuildingPosition, binding.BuildingPlacedTick));
        Assert.Equal(house.Position, binding.Position);
        Assert.Null(order.ShelterCompletion);
        Assert.Empty(world.ExportState().Survival!.Fires);
        Assert.Equal(2, Wood(world, "fire-wood"));
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(binding, Order(world, receipt).ShelterBinding);
        AssertPaidFire(world, receipt, actor, house, "fire-wood");
        Assert.Equal(1, Wood(world, "fire-wood"));
        var completion = Order(world, receipt).ShelterCompletion;
        await TickTogether(world, replay);
        Assert.Equal(completion, Order(world, receipt).ShelterCompletion);
        Assert.Single(world.ExportState().Events, item => item.Kind == "fire_fuelled");
    }

    [Fact]
    public async Task FuelPickupBindsTheHearthBeforeAcquisitionAndDoesNotCountAsIgnition()
    {
        var state = Prepared(fuel: 0);
        var actor = Actor(state);
        var house = House(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "stored-firewood", "wood", Alpha, 1, storageBuildingId: house.InstanceId));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "collect-firewood", "light a fire");
        await Tick(world);
        Assert.Equal(house.InstanceId, Order(world, receipt).ShelterBinding?.BuildingInstanceId);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(Order(world, receipt).ShelterCompletion);
        Assert.Empty(world.ExportState().Survival!.Fires);
        var carried = world.Society.Inventory.GetLot("stored-firewood");
        Assert.Equal((actor, 1, (string?)null), (carried.OwnerId, carried.Quantity, carried.StorageBuildingId));
        Assert.True(PersonalEquipmentRules.IsCarried(carried, actor));
        Assert.DoesNotContain(world.Society.Inventory.Reservations, item => item.Purpose == "heating_fuel");
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        AssertPaidFire(world, receipt, actor, house, "stored-firewood");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnExplicitPrivateHearthDoesNotGrantFuelAccessEvenToAnInvitedStormGuest(bool invited)
    {
        var state = ShelterOrderTestFixture.WithStorm(Prepared(fuel: 0));
        var host = Actor(state);
        var guest = ShelterOrderTestFixture.Actor(state, Beta);
        var house = House(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "guest-wood", "wood", guest, 1));
        state = ShelterOrderTestFixture.At(state, guest, house.Position);
        using var world = Restore(state);
        if (invited) Assert.True(world.SetHouseGuestInvitation(host, house.InstanceId, guest, true).Applied);
        var receipt = Submit(world, guest, "private-hearth", AtHouse(house));
        await Tick(world);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Null(Order(world, receipt).ShelterCompletion);
        Assert.Empty(world.ExportState().Survival!.Fires);
        Assert.Equal(1, Wood(world, "guest-wood"));
        Assert.DoesNotContain(world.Society.Inventory.Reservations, item => item.Purpose == "heating_fuel");
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("borrowed")]
    [InlineData("promised")]
    public async Task ProtectedWoodCannotBeSpentButAnIndependentOwnedUnitCan(string protection)
    {
        var state = Prepared(fuel: 0);
        var actor = Actor(state);
        var house = House(state);
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "protected-wood", "wood", protection == "reserved" ? actor : Alpha, 1);
        if (protection == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "other-fuel-claim", actor, "protected-wood", 1, "other_work", 1_000);
        else if (protection == "borrowed")
            inventory = InventoryFixture.Relocate(inventory, "borrow-wood", "protected-wood", Alpha, 1, actor);
        else
            inventory = InventoryFixture.Transfer(inventory, "promise-wood", Alpha, actor, "protected-wood", 1,
                "delivery", destinationDeliveryBuildingId: house.InstanceId);
        state = WithInventory(state, inventory);
        var protectedLot = inventory.GetLot("protected-wood");
        using (var unavailable = Restore(state))
        {
            var pending = Submit(unavailable, actor, "protected-fuel", "light a fire");
            await Tick(unavailable);
            Assert.Equal(0, Order(unavailable, pending).CompletedUnits);
            Assert.Empty(unavailable.ExportState().Survival!.Fires);
            Assert.Equal(protectedLot with { LastProcessedTick = unavailable.WorldTick }, unavailable.Society.Inventory.GetLot("protected-wood"));
            Assert.DoesNotContain(unavailable.Society.Inventory.Reservations, item => item.Purpose == "heating_fuel");
            using var saved = Reload(unavailable);
        }
        state = WithInventory(state, InventoryFixture.AddLot(inventory, "spare-fuel", "wood", actor, 1));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "spare-fuel", "light a fire");
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        AssertPaidFire(world, receipt, actor, house, "spare-fuel");
        Assert.Equal(protectedLot with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot("protected-wood"));
        if (protection == "reserved")
            Assert.Equal(inventory.GetReservation("other-fuel-claim"), world.Society.Inventory.GetReservation("other-fuel-claim"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFullCarriedVesselCountsForPickupCapacityAndRemainsIntactWhenOwnWoodIsBurned(bool ownFuel)
    {
        var state = Prepared(fuel: 0);
        var actor = Actor(state);
        var house = House(state);
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "full-jug", InventoryContainerRules.WaterJug, actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "jug-water", InventoryContainerRules.FreshWater, actor, 4, containerLotId: "full-jug");
        inventory = InventoryFixture.Reserve(inventory, "water-claim", actor, "jug-water", 4, "other_work", 1_000);
        inventory = InventoryFixture.AddLot(inventory, "carried-stone", "stone", actor, ownFuel ? 2 : 3);
        inventory = InventoryFixture.AddLot(inventory, "capacity-wood", "wood", ownFuel ? actor : Alpha, 1,
            storageBuildingId: ownFuel ? null : house.InstanceId);
        state = WithInventory(state, inventory);
        Assert.Equal(PersonalEquipmentRules.BaseCapacity, PersonalEquipmentRules.CarriedQuantity(inventory, actor, null));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "full-load-fire", "tend the fire");
        await Tick(world);
        Assert.Equal(ownFuel ? 1 : 0, Order(world, receipt).CompletedUnits);
        if (ownFuel) AssertPaidFire(world, receipt, actor, house, "capacity-wood");
        else
        {
            Assert.Empty(world.ExportState().Survival!.Fires);
            Assert.Equal(inventory.GetLot("capacity-wood") with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot("capacity-wood"));
        }
        Assert.Equal(inventory.GetLot("full-jug") with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot("full-jug"));
        Assert.Equal(inventory.GetLot("jug-water") with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot("jug-water"));
        Assert.Equal(inventory.GetReservation("water-claim"), world.Society.Inventory.GetReservation("water-claim"));
        using var replay = Reload(world);
        await TickTogether(world, replay);
    }

    [Fact]
    public async Task AnAlreadyLitHearthWaitsForRealExpiryWithoutExtendingOrCreditingTheExistingFire()
    {
        var state = Prepared();
        var house = House(state);
        var originalFire = new CampFireState(house.InstanceId, state.Society.Society.WorldTick + 3);
        state = state with { Survival = state.Survival! with { Fires = [originalFire] } };
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "already-lit", "light a fire");
        using var replay = Reload(world);
        while (world.WorldTick < originalFire.FuelUntilTick - 1)
        {
            await TickTogether(world, replay);
            Assert.Equal(originalFire, Assert.Single(world.ExportState().Survival!.Fires));
            Assert.Equal(0, Order(world, receipt).CompletedUnits);
            Assert.Null(Order(world, receipt).ShelterCompletion);
            Assert.Equal(2, Wood(world, "fire-wood"));
        }
        await FinishTogether(world, replay, receipt);
        AssertPaidFire(world, receipt, Actor(state), house, "fire-wood");
        Assert.True(Order(world, receipt).ShelterCompletion!.WorldTick >= originalFire.FuelUntilTick);
        Assert.Single(world.ExportState().Events, item => item.Kind == "fire_extinguished");
    }

    private static PrivateWorldRuntimeState Prepared(bool walking = false, int fuel = 2)
    {
        var state = SettlementWeatherTestFixture.WithWeather(ShelterOrderTestFixture.Prepared(), WeatherKind.Clear);
        var actor = Actor(state);
        var house = House(state);
        if (walking)
        {
            var point = ShelterOrderTestFixture.Approach(state, house.Position, distance: 3);
            state = ShelterOrderTestFixture.At(state, actor, point);
        }
        return fuel == 0 ? state : WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "fire-wood", "wood", actor, fuel));
    }

    private static string Actor(PrivateWorldRuntimeState state) => ShelterOrderTestFixture.Actor(state);
    private static PlacedBuilding House(PrivateWorldRuntimeState state) => ShelterOrderTestFixture.House(state);
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        ShelterOrderTestFixture.WithInventory(state, inventory);
    private static string AtHouse(PlacedBuilding house) => string.Create(CultureInfo.InvariantCulture,
        $"light the fire in my House at ({house.Position.X}, {house.Position.Y})");
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new FireChoices());
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
    private static int Wood(PrivateWorldRuntime world, string id) => world.Society.Inventory.Lots.Where(item => item.Id == id).Sum(item => item.Quantity);
    private static async Task Tick(PrivateWorldRuntime world) => Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    private static async Task TickTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        await Tick(world);
        await Tick(replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }
    private static async Task FinishTogether(PrivateWorldRuntime world, PrivateWorldRuntime replay, OwnerInstructionReceipt receipt)
    {
        for (var tick = 0; tick < 12 && Order(world, receipt).Status != "finished"; tick++) await TickTogether(world, replay);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
    }
    private static void AssertPaidFire(PrivateWorldRuntime world, OwnerInstructionReceipt receipt, string actor,
        PlacedBuilding house, string fuelLot)
    {
        var order = Order(world, receipt);
        Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
        var completion = order.ShelterCompletion;
        Assert.NotNull(completion);
        Assert.Equal(house.Position, completion.Position);
        Assert.NotNull(completion.FuelReservationId);
        var paid = world.Society.Inventory.GetReservation(completion.FuelReservationId);
        Assert.Equal((actor, fuelLot, 1, "heating_fuel", InventoryReservationState.Completed, completion.WorldTick),
            (paid.OwnerId, paid.LotId, paid.Quantity, paid.Purpose, paid.State, paid.ExpiryTick));
        Assert.Equal(new CampFireState(house.InstanceId, completion.WorldTick + 120), Assert.Single(world.ExportState().Survival!.Fires));
        Assert.Single(world.ExportState().Events, item => item.Kind == "fire_fuelled" && item.Detail == house.InstanceId);
        Assert.NotNull(order.LastEffectId);
    }
    private sealed class FireChoices(DecisionProviderKind kind = DecisionProviderKind.Deterministic, bool hold = false) : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 0;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            if (hold && !Started.Task.IsCompleted && observation.OperativeOrderInstructionId is not null)
            {
                Started.TrySetResult(true);
                await Release.Task;
                Returned.TrySetResult(true);
            }
            var selected = observation.OperativeOrderInstructionId is not null && observation.Candidates.Any(item => item.Id == "tend_fire")
                ? "tend_fire" : "safe_idle";
            return new(request.RequestId, observation.InhabitantId, Kind, request.ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 });
        }
    }
}
