using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldFieldOrderTests
{
    [Theory]
    [InlineData("till")]
    [InlineData("plant")]
    [InlineData("tend")]
    [InlineData("harvest")]
    public async Task FieldLocationKeepsWorkOnTheRequestedTileAcrossTravelAndReplay(string action)
    {
        var state = Prepared();
        var target = OtherPoint(state);
        FarmFieldState At(GridPoint point) => action switch
        {
            "plant" => new(point, Household, FarmFieldStage.Prepared),
            "tend" => new(point, Household, FarmFieldStage.Growing, "grain", ReadyTick: 10_000),
            _ => ReadyField(point, "grain"),
        };
        if (action != "till") state = WithFields(state, At(Point), At(target));
        if (action == "plant") state = WithSeeds(state, "grain", 1);
        using var world = Restore(state);
        var text = (action == "till" ? "till a field" : action + " grain") + $" at ({target.X}, {target.Y})";
        var receipt = Submit(world, "exact-field", text);
        Assert.Equal("waiting", Order(world, receipt).Status);
        for (var tick = 0; tick < 8 && !world.Fields.Any(field => field.Work?.OrderInstructionId == receipt.InstructionId); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var working = Assert.Single(world.Fields, field => field.Work is not null);
        Assert.Equal(target, working.Position);
        Assert.Equal(receipt.InstructionId, working.Work!.OrderInstructionId);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 20 && Order(world, receipt).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(("finished", 1, target),
            (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits, Order(restored, receipt).TargetPosition));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var completed = restored.Fields.Single(field => field.Position == target);
        if (action == "till") Assert.Equal(FarmFieldStage.Prepared, Assert.Single(restored.Fields).Stage);
        else
        {
            var untouched = restored.Fields.Single(field => field.Position == Point);
            Assert.Equal(At(Point).Stage, untouched.Stage);
            Assert.Null(untouched.Work);
            Assert.Equal(At(Point).Tended, untouched.Tended);
        }
        if (action == "plant")
        {
            Assert.Equal(FarmFieldStage.Planted, completed.Stage);
            Assert.Equal(0, SeedQuantity(restored, "grain"));
        }
        if (action is "till" or "tend")
            Assert.True(restored.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints < 10_000);
        if (action == "tend") Assert.True(completed.Tended);
        if (action == "harvest")
        {
            var crop = Assert.Single(restored.Society.Inventory.Lots, lot => lot.Id == FarmFieldRules.FieldId(target) + ":harvest:1:crop");
            Assert.Equal((Household, new InventoryGroundPosition(target.X, target.Y)), (crop.OwnerId, crop.GroundPosition));
            Assert.Equal(InventoryReservationState.Reserved, restored.Society.Inventory.GetReservation(completed.ReplantingReservationId!).State);
        }
        restored.Validate();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("unripe")]
    [InlineData("wrong-crop")]
    [InlineData("outside-map")]
    public async Task FieldLocationDoesNotSubstituteAnotherHarvestableField(string boundary)
    {
        var state = Prepared();
        var target = OtherPoint(state);
        var requested = ReadyField(target, boundary == "wrong-crop" ? "potatoes" : "grain");
        if (boundary == "foreign") requested = requested with
        { HouseholdId = state.Society.Society.Households.First(home => home.Id != Household).Id };
        if (boundary == "unripe") requested = requested with { Stage = FarmFieldStage.Growing, ReadyTick = 10_000 };
        state = boundary is "missing" or "outside-map" ? WithFields(state, ReadyField(Point, "grain"))
            : WithFields(state, ReadyField(Point, "grain"), requested);
        if (boundary == "outside-map") target = new(-1, target.Y);
        using var world = Restore(state);
        var receipt = Submit(world, "unavailable-field", $"harvest grain at tile {target.X},{target.Y}");
        Assert.Equal("waiting", Order(world, receipt).Status);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Contains(boundary == "outside-map" ? "outside" : "requested tile", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.All(world.Fields, field => Assert.Null(field.Work));
        Assert.Equal(FarmFieldStage.Ready, world.Fields.Single(field => field.Position == Point).Stage);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(target, Order(restored, receipt).TargetPosition);
    }

    [Fact]
    public async Task FieldLocationKeepsAnUnfinishedQuantityOnItsOwnTile()
    {
        var state = Prepared();
        var other = OtherPoint(state);
        state = WithFields(state,
            new(Point, Household, FarmFieldStage.Growing, "grain", ReadyTick: 10_000),
            new(other, Household, FarmFieldStage.Growing, "grain", ReadyTick: 10_000));
        using var world = Restore(state);
        var receipt = Submit(world, "two-at-one-site", $"tend two fields of grain at ({Point.X}, {Point.Y})");
        for (var tick = 0; tick < 14 && Order(world, receipt).Status != "blocked"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.True(world.Fields.Single(field => field.Position == Point).Tended);
        Assert.False(world.Fields.Single(field => field.Position == other).Tended);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal((2, 1, Point), (Order(restored, receipt).RequestedUnits, Order(restored, receipt).CompletedUnits, Order(restored, receipt).TargetPosition));
        Assert.True(restored.CancelOrder(new("stop-exact-field", "owner:test", restored.Society.WorldId, Actor, receipt.InstructionId)).Changed);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "field_tended");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FieldLocationCannotTillABuildingOrLandFarFromTheFarmhouse(bool distant)
    {
        var state = Prepared();
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var fertility = new LandFertility(state.Map, state.WorldSeed);
        var target = distant ? state.Map.Tiles.Select(tile => tile.Position).First(point =>
            fertility.CanFarm(point) && state.Map.FootDistance(point, farmhouse.Position) > 20) : farmhouse.Position;
        using var world = Restore(state);
        var receipt = Submit(world, "unusable-till-site", $"till a field at ({target.X}, {target.Y})");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Contains("requested tile", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Empty(world.Fields);
        Assert.Equal(10_000, world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
    }

    [Fact]
    public async Task FieldLocationBindsSavedWorkAndSurvivesPauseQueueAndRejectedTicks()
    {
        var state = WithFields(WithSeeds(Prepared(), "grain", 1), new FarmFieldState(Point, Household, FarmFieldStage.Prepared));
        using var world = Restore(state);
        var plant = Submit(world, "plant-here", $"plant grain at ({Point.X}, {Point.Y})");
        var tend = Submit(world, "tend-here", $"tend grain at ({Point.X}, {Point.Y})", queue: true);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var stateWithWork = world.ExportState();
        var wrong = OtherPoint(state);
        Assert.Throws<InvalidDataException>(() => Restore(stateWithWork with
        {
            Instructions = stateWithWork.Instructions!.Select(item => item.InstructionId == plant.InstructionId
                ? item with { Order = item.Order! with { TargetPosition = wrong } } : item).ToArray(),
        }));
        world.Pause();
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(Point, Order(restored, tend).TargetPosition);
        restored.Resume();
        await Finish(restored, tend, 20);
        Assert.Equal("finished", Order(restored, plant).Status);
        Assert.Equal("finished", Order(restored, tend).Status);
        Assert.Equal(0, SeedQuantity(restored, "grain"));
        Assert.Single(restored.ExportState().Events, item => item.Kind == "field_planted");
        Assert.Single(restored.ExportState().Events, item => item.Kind == "field_tended");
        restored.Validate();
    }
}
