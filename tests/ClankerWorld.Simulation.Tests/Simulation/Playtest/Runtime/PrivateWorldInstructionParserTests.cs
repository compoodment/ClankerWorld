using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldInstructionParserTests
{
    private const string TargetInhabitant = "founder-ilya";

    [Theory]
    [InlineData("gather fruit not berries")]
    [InlineData("harvest fruit from berry-patch")]
    [InlineData("gather berries from berry-patch-unknown")]
    [InlineData("go find food")]
    [InlineData("go share food")]
    [InlineData("go keep food")]
    [InlineData("keep food for winter")]
    [InlineData("go get 2 berries")]
    [InlineData("go get2berries")]
    [InlineData("eat 3 times")]
    [InlineData("eat 1,000,000 berries")]
    [InlineData("gather berries at tile12")]
    [InlineData("gather berries at 12,")]
    [InlineData("gather berries at 12,4 and eat food")]
    [InlineData("stop harvesting berries")]
    [InlineData("avoid eating berries")]
    [InlineData("are you hungry?")]
    public void UnrecognizedOrPartiallyUnderstoodDirectOrdersCloseWithoutChangingTheirWords(string text)
    {
        using var world = new PrivateWorldRuntime("strict-order-parse");

        var receipt = world.SubmitInstruction(new OwnerInstructionRequest(
            "strict-unknown", "owner:test", TargetInhabitant, OwnerInstructionKind.MustDo, text));

        var state = world.ExportState();
        var instruction = Assert.Single(state.Instructions!, item => item.InstructionId == receipt.InstructionId);
        Assert.Equal(text, instruction.Text);
        Assert.Equal("strict-unknown", instruction.IdempotencyKey);
        Assert.Equal("unknown", instruction.Order!.Action);
        Assert.Equal("not_understood", instruction.Order.Status);
        Assert.Contains(receipt.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.Contains(state.Events, item => item.Kind == "instruction_not_understood" &&
            item.Detail == $"{TargetInhabitant}:{receipt.InstructionId}");
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrecognizedMustDoLeavesCurrentAndQueuedOrdersUnchanged(bool queueUnknown)
    {
        using var world = new PrivateWorldRuntime($"unknown-does-not-replace-{queueUnknown}");
        var current = world.SubmitInstruction(new OwnerInstructionRequest(
            "recognized-current", "owner:test", TargetInhabitant, OwnerInstructionKind.MustDo, "gather berries"));
        var queued = world.SubmitInstruction(new OwnerInstructionRequest(
            "recognized-queued", "owner:test", TargetInhabitant, OwnerInstructionKind.MustDo,
            "eat berries", Queue: true));

        var unknown = world.SubmitInstruction(new OwnerInstructionRequest(
            $"unknown-{queueUnknown}", "owner:test", TargetInhabitant, OwnerInstructionKind.MustDo,
            "gather fruit not berries", Queue: queueUnknown));

        var state = world.ExportState();
        var savedCurrent = Assert.Single(state.Instructions!, item => item.InstructionId == current.InstructionId);
        var savedQueued = Assert.Single(state.Instructions!, item => item.InstructionId == queued.InstructionId);
        var savedUnknown = Assert.Single(state.Instructions!, item => item.InstructionId == unknown.InstructionId);
        Assert.Equal("waiting", savedCurrent.Order!.Status);
        Assert.Equal("queued", savedQueued.Order!.Status);
        Assert.DoesNotContain(current.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.DoesNotContain(queued.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.Equal("gather fruit not berries", savedUnknown.Text);
        Assert.Equal($"unknown-{queueUnknown}", savedUnknown.IdempotencyKey);
        Assert.Equal("not_understood", savedUnknown.Order!.Status);
        Assert.Contains(unknown.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.DoesNotContain(state.Events, item => item.Kind == "instruction_order_cancelled" &&
            (item.Detail.Contains(current.InstructionId, StringComparison.Ordinal) ||
             item.Detail.Contains(queued.InstructionId, StringComparison.Ordinal)));

        var encoded = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();
    }

    [Theory]
    [InlineData("gather berries at 12 4", 12, 4)]
    [InlineData("gather berries at (12,4)", 12, 4)]
    [InlineData("gather berries at10,12", 10, 12)]
    public void CoordinateFormsAreConsumedAndSaved(string text, int x, int y)
    {
        using var world = new PrivateWorldRuntime("strict-order-coordinates");

        var receipt = world.SubmitInstruction(new OwnerInstructionRequest(
            "coordinate-order", "owner:test", TargetInhabitant, OwnerInstructionKind.MustDo, text));

        var state = world.ExportState();
        var order = Assert.Single(state.Instructions!, item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal("harvest_food", order.Action);
        Assert.Equal(new GridPoint(x, y), order.TargetPosition);
        world.Validate();
    }

    [Fact]
    public void ThousandsQuantityIsParsedWithoutChangingTheRequestedAmount()
    {
        using var world = new PrivateWorldRuntime("strict-order-thousands");

        var receipt = world.SubmitInstruction(new OwnerInstructionRequest(
            "thousand-food", "owner:test", TargetInhabitant, OwnerInstructionKind.MustDo, "eat 1,000 berries"));

        var order = Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal("consume_food", order.Action);
        Assert.Equal(1000, order.RequestedUnits);
        Assert.True(order.QuantityIsExplicit);
        Assert.Equal("food_items", order.ProgressUnit);
    }

    [Theory]
    [InlineData("Eat!", "consume_food")]
    [InlineData("gathering berries", "harvest_food")]
    [InlineData("Please eat the food now.", "consume_food")]
    [InlineData("go to the berry patch", "seek_food")]
    [InlineData("keep gathering food until cancelled", "harvest_food")]
    [InlineData("keep eating", "consume_food")]
    [InlineData("keep eating food", "consume_food")]
    public void ExplicitlySupportedDirectFormsRemainRecognized(string text, string action)
    {
        using var world = new PrivateWorldRuntime("strict-supported-order-forms");

        var receipt = world.SubmitInstruction(new OwnerInstructionRequest(
            "supported-form", "owner:test", TargetInhabitant, OwnerInstructionKind.MustDo, text));

        var order = Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(action, order.Action);
        Assert.Equal("waiting", order.Status);
        Assert.Equal(text.StartsWith("keep ", StringComparison.Ordinal),
            order.RepeatUntilCancelled);
    }

}
