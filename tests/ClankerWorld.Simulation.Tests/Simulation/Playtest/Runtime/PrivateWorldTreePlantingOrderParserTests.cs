using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldTreePlantingOrderParserTests
{
    [Theory]
    [InlineData("plant trees", "plant_tree", 1, false)]
    [InlineData("please plant two broadleaf trees!", "plant_broadleaf", 2, false)]
    [InlineData("plant a conifer tree at (12, 4)", "plant_conifer", 1, false)]
    [InlineData("plant an orchard at tile 12,4", "plant_orchard", 1, false)]
    [InlineData("plant three orchard trees", "plant_orchard", 3, false)]
    [InlineData("keep planting trees", "plant_tree", 1, true)]
    [InlineData("plant 1000 trees until cancelled", "plant_tree", 1000, true)]
    public void WholeTreeRequestsBecomeBoundedPersistentOrders(string text, string action, int quantity, bool repeat)
    {
        using var world = new PrivateWorldRuntime("tree-order-parser");
        var actor = world.Inhabitants[0].InhabitantId;
        var receipt = world.SubmitInstruction(new("parse-tree", "owner:test", actor, OwnerInstructionKind.MustDo, text));
        var instruction = Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == receipt.InstructionId);
        Assert.Equal(text, instruction.Text);
        Assert.Equal((action, quantity, repeat, "trees", 0), (instruction.Order!.Action, instruction.Order.RequestedUnits,
            instruction.Order.RepeatUntilCancelled, instruction.Order.ProgressUnit, instruction.Order.CompletedUnits));
        Assert.Equal(text.Contains("12", StringComparison.Ordinal), instruction.Order.TargetPosition is not null);
        world.Validate();
    }

    [Theory]
    [InlineData("plant oak trees")]
    [InlineData("plant 1001 trees")]
    [InlineData("plant zero trees")]
    [InlineData("plant conifer trees at 12,")]
    [InlineData("plant trees at 12,4 and then eat")]
    [InlineData("don't plant trees")]
    [InlineData("plant trees inside the Town")]
    public void PartialOrUnsupportedTreeRequestsLeaveAnExistingOrderIntact(string text)
    {
        using var world = new PrivateWorldRuntime("tree-order-parser");
        var actor = world.Inhabitants[0].InhabitantId;
        var current = world.SubmitInstruction(new("existing-tree", "owner:test", actor, OwnerInstructionKind.MustDo, "plant trees"));
        var receipt = world.SubmitInstruction(new("bad-tree", "owner:test", actor, OwnerInstructionKind.MustDo, text));
        var state = world.ExportState();
        Assert.Equal("waiting", Assert.Single(state.Instructions!, item => item.InstructionId == current.InstructionId).Order!.Status);
        var unsupported = Assert.Single(state.Instructions!, item => item.InstructionId == receipt.InstructionId);
        Assert.Equal(text, unsupported.Text);
        Assert.Equal("not_understood", unsupported.Order!.Status);
        world.Validate();
    }
}
