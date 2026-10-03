using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldCultivatedGreensOrderTests
{
    private const string Household = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private static readonly Lazy<byte[]> Generated = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("cultivated-greens-orders", _ => new ActionCoverageRecorder(chooseIdle: true));
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Theory]
    [InlineData("eat cultivated greens", 1, false)]
    [InlineData("eat two cultivated greens", 2, false)]
    [InlineData("keep eating cultivated greens until cancelled", 1, true)]
    [InlineData("please eat the cultivated greens now.", 1, false)]
    public void EatingCultivatedGreensHasAnExactSavedFoodTarget(string text, int quantity, bool repeat)
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "parse", text);
        var order = Order(world, receipt);
        Assert.Equal(("consume_food", "cultivated_greens", quantity, repeat, "food_items"),
            (order.Action, order.TargetFoodKind, order.RequestedUnits, order.RepeatUntilCancelled, order.ProgressUnit));
        Assert.Null(order.TargetResourceId);
        Assert.Null(order.TargetPosition);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EatingCultivatedGreensConsumesOnlyTheRequestedCarriedOrSharedFood(bool shared)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "greens-to-eat", "cultivated_greens",
            shared ? Household : actor, 2, storageBuildingId: shared ? House : null);
        inventory = InventoryFixture.AddLot(inventory, "berries-to-keep", "berries", actor, 1);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "eat", "eat cultivated greens");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        if (shared)
        {
            Assert.Equal(0, Order(world, receipt).CompletedUnits);
            Assert.Single(world.ExportState().Events, item => item.Kind == "household_food_collected");
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(1, world.Society.Inventory.GetLot("berries-to-keep").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("greens-to-eat").Quantity);
        Assert.Single(world.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == actor);
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal("cultivated_greens", projected.TargetFoodKind);
        world.Validate();
    }

    [Theory]
    [InlineData("wrong-kind")]
    [InlineData("reserved")]
    [InlineData("another-person")]
    [InlineData("foreign-household")]
    public async Task EatingCultivatedGreensWaitsForEligibleFood(string boundary)
    {
        var state = Prepared();
        var actor = Actor(state);
        var other = state.Society.Society.Inhabitants.First(person => person.HouseholdId != Household);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "greens-unavailable",
            boundary == "wrong-kind" ? "wild_greens" : "cultivated_greens",
            boundary == "another-person" ? other.Id : boundary == "foreign-household" ? other.HouseholdId! : actor,
            1, storageBuildingId: boundary == "foreign-household" ? "first-town-house-b" : null);
        if (boundary == "reserved") inventory = InventoryFixture.Reserve(inventory,
            "greens-reserved", actor, "greens-unavailable", 1, "other_work", 120);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "unavailable", "eat cultivated greens");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Contains("No matching food", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(1, world.Society.Inventory.GetLot("greens-unavailable").Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == actor);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(Order(world, receipt), Order(restored, receipt));
    }

    [Fact]
    public async Task EatingCultivatedGreensKeepsRemainingMealsThroughFullnessReloadAndRollback()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "greens-meals", "cultivated_greens", actor, 3);
        using var world = Restore(WithInventory(state, inventory));
        var first = Submit(world, actor, "meals", "eat two cultivated greens");
        var queued = Submit(world, actor, "queued", "eat berries", queue: true);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, Order(world, first).CompletedUnits);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 1), (Order(world, first).Status, Order(world, first).CompletedUnits));
        Assert.Contains("hungry enough", Order(world, first).BlockedReason, StringComparison.Ordinal);
        Assert.Equal("queued", Order(world, queued).Status);
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var paused = Restore(saved);
        Assert.False((await paused.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, Order(paused, first).CompletedUnits);

        // Restore the same partial task at a later meal's fullness without waiting hundreds of world steps.
        saved = saved with
        {
            Inhabitants = saved.Inhabitants.Select(person => person.InhabitantId == actor ? person with { HungerBasisPoints = 3_000 } : person).ToArray(),
        };
        using var resumed = Restore(saved);
        using var twin = Restore(saved);
        resumed.Resume();
        twin.Resume();
        var before = PrivateWorldRuntimeCodec.Encode(resumed.ExportState());
        Assert.False((await resumed.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        for (var tick = 0; tick < 65 && Order(resumed, first).Status != "finished"; tick++)
        {
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
            Assert.True((await twin.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(resumed.ExportState()), PrivateWorldRuntimeCodec.Encode(twin.ExportState()));
        }
        Assert.Equal(("finished", 2), (Order(resumed, first).Status, Order(resumed, first).CompletedUnits));
        Assert.Equal(1, resumed.Society.Inventory.GetLot("greens-meals").Quantity);
        Assert.Equal(2, resumed.ExportState().Events.Count(item => item.Kind == "food_consumed" && item.Detail == actor));
        Assert.Equal(0, Order(resumed, queued).CompletedUnits);
        resumed.Validate();
    }

    [Fact]
    public async Task CancelledRepeatingGreensOrderStaysStoppedAfterReload()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "greens-repeat", "cultivated_greens", actor, 2);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "repeat", "keep eating cultivated greens");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, Order(world, receipt).CompletedUnits);
        Assert.True(world.CancelOrder(new("cancel-greens", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("cancelled", 1), (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits));
        Assert.Equal(1, restored.Society.Inventory.GetLot("greens-repeat").Quantity);
    }

    [Theory]
    [InlineData("gather cultivated greens")]
    [InlineData("go to cultivated greens")]
    [InlineData("eat cultivated greens at (1, 2)")]
    [InlineData("eat wild cultivated greens")]
    [InlineData("eat grain")]
    [InlineData("eat potatoes")]
    public void UnsupportedGreensFormsKeepTheCurrentTask(string text)
    {
        var state = Prepared();
        using var world = Restore(state);
        var current = Submit(world, Actor(state), "current", "eat cultivated greens");
        var rejected = Submit(world, Actor(state), "unsupported", text);
        Assert.Equal("not_understood", Order(world, rejected).Status);
        Assert.Equal("waiting", Order(world, current).Status);
    }

    [Theory]
    [InlineData("harvest_food")]
    [InlineData("seek_food")]
    public void CultivatedGreensCannotBecomeASavedWildFoodTarget(string action)
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "validation", "eat cultivated greens");
        var saved = world.ExportState();
        var corrupt = saved with
        {
            Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                ? item with { Order = item.Order! with { Action = action, ProgressUnit = action == "seek_food" ? "arrivals" : "harvests" } }
                : item).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => Restore(corrupt));
    }

    private static PrivateWorldRuntimeState Prepared()
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value);
        var actor = Actor(state);
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray(),
        });
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        return state with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? house.Position : person.Position,
                HungerBasisPoints = person.InhabitantId == actor ? 3_000 : 10_000,
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                LastDecisionContext = null,
            }).ToArray(),
        };
    }

    private static string Actor(PrivateWorldRuntimeState state) =>
        state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household).Id;
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) =>
        PrivateWorldRuntime.Restore(state, _ => new ActionCoverageRecorder(chooseIdle: true));
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text, Queue: queue));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
}
