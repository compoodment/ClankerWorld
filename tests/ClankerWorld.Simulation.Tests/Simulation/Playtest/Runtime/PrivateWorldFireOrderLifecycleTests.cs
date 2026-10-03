using System.Diagnostics;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldFireOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UrgentColdPermitsIgnitionWhileUrgentFoodPausesItWithoutLosingTheOrder(bool hungry)
    {
        var state = Prepared();
        var actor = Actor(state);
        var house = House(state);
        if (!hungry) state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Snow);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = hungry ? 1_000 : 9_000, Survival = new(hungry ? 10_000 : 1_000) }
                : person).ToArray(),
        };
        if (hungry) state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "urgent-meal", "food", actor, 1));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "urgent-fire", "light a fire");
        await Tick(world);
        if (hungry)
        {
            Assert.Equal(("interrupted", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Empty(world.ExportState().Survival!.Fires);
            Assert.Equal(2, Wood(world, "fire-wood"));
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "urgent-meal");
        }
        else AssertPaidFire(world, receipt, actor, house, "fire-wood");
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        AssertPaidFire(world, receipt, actor, house, "fire-wood");
        Assert.Equal(1, Wood(world, "fire-wood"));
    }

    [Fact]
    public async Task PausedTravelCannotBurnWoodAndResumesTheSameHearthAcrossStrictReplay()
    {
        var state = Prepared(walking: true);
        var actor = Actor(state);
        using var initial = Restore(state);
        var receipt = Submit(initial, actor, "paused-fire", "light a fire");
        await Tick(initial);
        var binding = Order(initial, receipt).ShelterBinding;
        Assert.NotNull(binding);
        Assert.Equal(0, Order(initial, receipt).CompletedUnits);
        initial.Pause();
        using var world = Reload(initial);
        using var replay = Reload(world);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(2, Wood(world, "fire-wood"));
        world.Resume();
        replay.Resume();
        await FinishTogether(world, replay, receipt);
        Assert.Equal(binding, Order(world, receipt).ShelterBinding);
        AssertPaidFire(world, receipt, actor, House(state), "fire-wood");
    }

    [Fact]
    public async Task CancellingDuringTheWalkPreservesFuelAndNeverCreditsTheAbandonedFire()
    {
        var state = Prepared(walking: true);
        var actor = Actor(state);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "cancel-fire", "light a fire");
        await Tick(world);
        Assert.NotNull(Order(world, receipt).ShelterBinding);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.True(world.CancelOrder(new("stop-fire", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
        using var replay = Reload(world);
        for (var tick = 0; tick < 3; tick++) await TickTogether(world, replay);
        Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Null(Order(world, receipt).ShelterCompletion);
        Assert.Equal(2, Wood(world, "fire-wood"));
        Assert.Empty(world.ExportState().Survival!.Fires);
        Assert.DoesNotContain(world.Society.Inventory.Reservations, item => item.Purpose == "heating_fuel");
    }

    [Fact]
    public async Task ALateHostedReplyCannotReviveACancelledFireOrder()
    {
        var state = Prepared(walking: true);
        var actor = Actor(state);
        var provider = new FireChoices(DecisionProviderKind.LargeLanguageModel, hold: true);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new FireChoices());
        var receipt = Submit(world, actor, "held-fire", "light a fire");
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(world.CancelOrder(new("stop-held-fire", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var deadline = Stopwatch.StartNew();
            var admitted = false;
            while (!admitted && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                var step = await world.AdvanceOneTickNonBlockingAsync();
                Assert.True(step.Advanced);
                admitted = step.Decisions.Any(item => item.InhabitantId == actor && item.Admission.Accepted);
                if (!admitted) await Task.Delay(10);
            }
            Assert.True(admitted, "A post-release decision must actually be admitted before checking the cancelled order.");
            Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Null(Order(world, receipt).ShelterCompletion);
            Assert.Empty(world.ExportState().Survival!.Fires);
            Assert.Equal(2, Wood(world, "fire-wood"));
            Assert.DoesNotContain(world.Society.Inventory.Reservations, item => item.Purpose == "heating_fuel");
            using var replay = Reload(world);
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACompletedFireKeepsItsPaidHistoryAfterExpiryOrHouseRemoval(bool removeHouse)
    {
        var state = Prepared(fuel: 1);
        var house = House(state);
        using var lit = Restore(state);
        var receipt = Submit(lit, Actor(state), "historic-fire", "light a fire");
        await Tick(lit);
        AssertPaidFire(lit, receipt, Actor(state), house, "fire-wood");
        var completed = Order(lit, receipt);
        var paid = lit.Society.Inventory.GetReservation(completed.ShelterCompletion!.FuelReservationId!);
        var fire = Assert.Single(lit.ExportState().Survival!.Fires);
        if (removeHouse)
        {
            var removal = lit.RemoveBuilding(house.InstanceId, house.TownId, house.HouseholdId);
            Assert.True(removal.Applied, removal.Failure);
        }
        state = lit.ExportState();
        if (!removeHouse)
        {
            // Keep the actual ignition and payment unchanged, then test their real expiry boundary.
            var targetTick = fire.FuelUntilTick - 1;
            var systems = state.WorldSystems!;
            while (systems.WorldTick < targetTick) systems = WorldSystemsRules.AdvanceOneTick(systems);
            state = state with
            {
                WorldSystems = systems,
                Society = state.Society with { Society = SocietyFixture.AdvanceTo(state.Society.Society, targetTick).Checkpoint },
            };
        }
        using var world = Restore(state);
        using var replay = Reload(world);
        if (!removeHouse)
        {
            Assert.Equal(fire.FuelUntilTick - 1, world.WorldTick);
            Assert.Equal(fire, Assert.Single(world.ExportState().Survival!.Fires));
        }
        await TickTogether(world, replay);
        if (!removeHouse) Assert.Equal(fire.FuelUntilTick, world.WorldTick);
        Assert.Empty(world.ExportState().Survival!.Fires);
        Assert.Equal(completed, Order(world, receipt));
        Assert.Equal(paid, world.Society.Inventory.GetReservation(paid.Id));
        Assert.Single(world.ExportState().Events, item => item.Kind == "fire_fuelled");
        Assert.Single(world.ExportState().Events, item => item.Kind == "fire_extinguished");
        using var historical = Reload(world);
        Assert.Equal(completed, Order(historical, receipt));
    }

    [Fact]
    public async Task AReplacementHouseWithTheSameIdCannotAdoptTheOldFireOrder()
    {
        var state = Prepared(walking: true);
        var actor = Actor(state);
        var house = House(state);
        using var walking = Restore(state);
        var receipt = Submit(walking, actor, "old-house-fire", "light a fire");
        await Tick(walking);
        var binding = Order(walking, receipt).ShelterBinding;
        Assert.NotNull(binding);
        Assert.Equal(0, Order(walking, receipt).CompletedUnits);
        state = WithInventory(walking.ExportState(), InventoryFixture.AddLot(walking.Society.Inventory,
            "replacement-house-wood", "wood", Alpha, 20));
        using var world = Restore(state);
        Assert.True(world.RemoveBuilding(house.InstanceId, house.TownId, Alpha).Applied);
        var placement = world.PlaceBuilding(house.InstanceId, house.DefinitionId, house.Position, Alpha);
        Assert.True(placement.Applied, placement.Failure);
        var replacement = world.WorldSimulation.Buildings.Single(item => item.InstanceId == house.InstanceId);
        Assert.NotEqual(house.PlacedTick, replacement.PlacedTick);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(binding, Order(world, receipt).ShelterBinding);
        Assert.Null(Order(world, receipt).ShelterCompletion);
        Assert.Equal(2, Wood(world, "fire-wood"));
        Assert.Empty(world.ExportState().Survival!.Fires);
    }

    [Fact]
    public async Task RefusedPreparedIgnitionCannotConsumeFuelCreateFireOrCreditTheOrder()
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "refused-fire", "light a fire");
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(2, Wood(world, "fire-wood"));
        Assert.Empty(world.ExportState().Survival!.Fires);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        AssertPaidFire(world, receipt, Actor(state), House(state), "fire-wood");
    }

    [Fact]
    public async Task StrictReloadRequiresTheActualCompletedFuelReservationAndMatchingFireProof()
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "strict-fire", "light a fire");
        await Tick(world);
        var order = Order(world, receipt);
        Assert.NotNull(order.ShelterCompletion);
        var saved = world.ExportState();
        foreach (var bad in new[]
        {
            order with { ShelterCompletion = null },
            order with { ShelterCompletion = order.ShelterCompletion with { FuelReservationId = "missing-fuel-payment" } },
            order with { ShelterCompletion = order.ShelterCompletion with { WorldTick = world.WorldTick + 1 } },
            order with { ShelterBinding = order.ShelterBinding! with { OwnerId = Beta } },
        })
            Assert.Throws<InvalidDataException>(() => Restore(saved with
            {
                Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                    ? item with { Order = bad } : item).ToArray(),
            }));
        var paid = world.Society.Inventory.GetReservation(order.ShelterCompletion.FuelReservationId!);
        foreach (var invalid in new[] { paid with { Purpose = "other_work" }, paid with { State = InventoryReservationState.Released } })
            Assert.Throws<InvalidDataException>(() => Restore(WithInventory(saved, world.Society.Inventory with
            {
                Reservations = world.Society.Inventory.Reservations.Select(item => item.Id == paid.Id ? invalid : item).ToArray(),
            })));
        using var replay = Reload(world);
        Assert.Equal(order, Order(replay, receipt));
    }
}
