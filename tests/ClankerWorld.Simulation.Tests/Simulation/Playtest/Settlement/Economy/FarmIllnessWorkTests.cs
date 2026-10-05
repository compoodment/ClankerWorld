using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmIllnessWorkTests
{
    [Theory]
    [InlineData(FarmWorkKind.Till)]
    [InlineData(FarmWorkKind.Plant)]
    [InlineData(FarmWorkKind.Harvest)]
    public async Task SevereIllnessSlowsActualFieldStrokesAndWearWithoutLosingWorkAcrossReload(FarmWorkKind kind)
    {
        var setup = await PreparedWork(kind);
        using var healthy = FarmFieldTests.Restore(WithIllness(setup.State, setup.Actor, 0));
        using var ill = FarmFieldTests.Restore(WithIllness(setup.State, setup.Actor, 9_000));
        Start(healthy, setup, kind);
        Start(ill, setup, kind);
        var toolId = kind == FarmWorkKind.Harvest ? "illness-sickle" : "carried-hoe";
        var originalCondition = ill.Society.Inventory.GetLot(toolId).ConditionBasisPoints;

        for (var tick = 0; tick < 4; tick++)
        {
            var before = ill.ExportState();
            var previousWork = Assert.Single(before.Fields!).Work!;
            var previousCondition = ill.Society.Inventory.GetLot(toolId).ConditionBasisPoints;
            Assert.True((await healthy.AdvanceOneTickAsync()).Advanced);
            Assert.True((await ill.AdvanceOneTickAsync()).Advanced);
            var person = ill.Inhabitants.Single(person => person.InhabitantId == setup.Actor);
            Assert.InRange(person.Survival!.IllnessBasisPoints, 7_500, 10_000);
            if (!SettlementIllnessRules.AllowsWork(setup.Actor, ill.WorldTick, person.Survival.IllnessBasisPoints))
            {
                Assert.Equal(previousWork, Assert.Single(ill.Fields).Work);
                Assert.Equal(previousCondition, ill.Society.Inventory.GetLot(toolId).ConditionBasisPoints);
            }
        }

        Assert.Null(Assert.Single(healthy.Fields).Work);
        var delayed = Assert.Single(ill.Fields);
        Assert.NotNull(delayed.Work);
        Assert.True(delayed.Work.RemainingTicks > 0);
        if (kind == FarmWorkKind.Plant)
        {
            Assert.Equal(originalCondition, ill.Society.Inventory.GetLot(toolId).ConditionBasisPoints);
            Assert.Equal(2, ill.Society.Inventory.GetLot("illness-planting").Quantity);
            Assert.Equal(InventoryReservationState.Reserved,
                ill.Society.Inventory.GetReservation(delayed.Work.SeedReservationId!).State);
        }
        else
        {
            Assert.True(ill.Society.Inventory.GetLot(toolId).ConditionBasisPoints >
                healthy.Society.Inventory.GetLot(toolId).ConditionBasisPoints);
        }

        var bytes = PrivateWorldRuntimeCodec.Encode(ill.ExportState());
        using var reloaded = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        for (var tick = 4; tick < 16; tick++)
        {
            Assert.True((await ill.AdvanceOneTickAsync()).Advanced);
            Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(ill.ExportState()),
                PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
        Assert.Null(Assert.Single(reloaded.Fields).Work);
        Assert.Contains(reloaded.ExportState().Events, item => item.Kind == CompletionEvent(kind) &&
            item.Detail.StartsWith(setup.Actor + ":", StringComparison.Ordinal));
        Assert.Equal(healthy.Society.Inventory.GetLot(toolId).ConditionBasisPoints,
            reloaded.Society.Inventory.GetLot(toolId).ConditionBasisPoints);
        if (kind == FarmWorkKind.Plant)
        {
            Assert.Equal(1, reloaded.Society.Inventory.GetLot("illness-planting").Quantity);
            var receipt = Assert.Single(reloaded.Society.Inventory.Reservations,
                item => item.Purpose == "field_planting" && item.LotId == "illness-planting");
            Assert.Equal((setup.Actor, 1, InventoryReservationState.Completed),
                (receipt.OwnerId, receipt.Quantity, receipt.State));
            Assert.Single(reloaded.Society.Inventory.Events,
                item => item.Kind == "reservation_consumed" && item.Detail == receipt.Id);
        }
        if (kind == FarmWorkKind.Harvest)
        {
            var field = Assert.Single(reloaded.Fields);
            Assert.Equal((FarmFieldStage.Harvested, 1), (field.Stage, field.Cycle));
            Assert.Contains(reloaded.Society.Inventory.Lots, lot => lot.ItemKind == FarmFieldRules.Grain &&
                lot.OwnerId == setup.Household && lot.GroundPosition == new InventoryGroundPosition(setup.Point.X, setup.Point.Y));
            var planting = reloaded.Society.Inventory.GetReservation(field.ReplantingReservationId!);
            Assert.Equal((setup.Household, 1, InventoryReservationState.Reserved),
                (planting.OwnerId, planting.Quantity, planting.State));
        }
    }

    [Fact]
    public async Task DelayedPlantingKeepsItsActualSeedPastTheHealthyDeadlineAndConsumesItOnlyOnCompletion()
    {
        var setup = await PreparedWork(FarmWorkKind.Plant);
        using var working = FarmFieldTests.Restore(WithIllness(setup.State, setup.Actor, 9_000));
        Start(working, setup, FarmWorkKind.Plant);
        var reservationId = Assert.Single(working.Fields).Work!.SeedReservationId!;
        await Advance(working, 6);
        var work = Assert.Single(working.Fields).Work;
        Assert.NotNull(work);
        Assert.Equal(FarmWorkKind.Plant, work.Kind);
        Assert.InRange(work.RemainingTicks, 1, 3);
        Assert.Equal(2, working.Society.Inventory.GetLot("illness-planting").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, working.Society.Inventory.GetReservation(reservationId).State);

        using var reloaded = Reload(working);
        await Advance(reloaded, 10);
        Assert.Null(Assert.Single(reloaded.Fields).Work);
        Assert.Equal(1, reloaded.Society.Inventory.GetLot("illness-planting").Quantity);
        Assert.Equal((setup.Actor, "illness-planting", 1, InventoryReservationState.Completed),
            (reloaded.Society.Inventory.GetReservation(reservationId).OwnerId,
                reloaded.Society.Inventory.GetReservation(reservationId).LotId,
                reloaded.Society.Inventory.GetReservation(reservationId).Quantity,
                reloaded.Society.Inventory.GetReservation(reservationId).State));
        Assert.Single(reloaded.Society.Inventory.Events,
            item => item.Kind == "reservation_consumed" && item.Detail == reservationId);
        Assert.Single(reloaded.ExportState().Events, item => item.Kind == "field_planted" &&
            item.Detail.StartsWith(setup.Actor + ":", StringComparison.Ordinal));
        using var restored = Reload(reloaded);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()),
            PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private sealed record Setup(PrivateWorldRuntimeState State, string Actor, string Household, GridPoint Point);

    private static async Task<Setup> PreparedWork(FarmWorkKind kind)
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("illness-field-" + kind);
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "illness-planting", FarmFieldRules.GrainSeed, actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "illness-sickle", "wooden_sickle", actor, 1);
        using var world = FarmFieldTests.Restore(FarmFieldTests.WithInventory(state, inventory));
        if (kind != FarmWorkKind.Till)
        {
            Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
            await Advance(world, 4);
        }
        if (kind is FarmWorkKind.Tend or FarmWorkKind.Harvest)
        {
            Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Plant,
                FarmFieldRules.Grain, "illness-planting").Accepted);
            await Advance(world, 5);
        }
        if (kind == FarmWorkKind.Harvest)
        {
            Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Tend).Accepted);
            await Advance(world, 3);
            var remaining = Assert.Single(world.Fields).ReadyTick - world.WorldTick;
            await Advance(world, checked((int)Math.Max(0, remaining)));
            Assert.Equal(FarmFieldStage.Ready, Assert.Single(world.Fields).Stage);
        }
        return new(world.ExportState(), actor, household, point);
    }

    private static PrivateWorldRuntimeState WithIllness(PrivateWorldRuntimeState state, string actor, int illness) => state with
    {
        Survival = state.Survival ?? new SettlementSurvivalState(0, []),
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
        {
            Survival = (person.Survival ?? new SurvivalCondition()) with { WarmthBasisPoints = 10_000, IllnessBasisPoints = illness },
        } : person).ToArray(),
    };

    private static void Start(PrivateWorldRuntime world, Setup setup, FarmWorkKind kind) => Assert.True(
        world.StartFieldWork(setup.Actor, setup.Point, kind,
            kind == FarmWorkKind.Plant ? FarmFieldRules.Grain : null,
            kind == FarmWorkKind.Plant ? "illness-planting" : null).Accepted);

    private static string CompletionEvent(FarmWorkKind kind) => kind switch
    {
        FarmWorkKind.Till => "field_prepared",
        FarmWorkKind.Plant => "field_planted",
        FarmWorkKind.Tend => "field_tended",
        _ => "field_harvested",
    };

    private static async Task Advance(PrivateWorldRuntime world, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Validate();
    }

    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world) =>
        FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
}
