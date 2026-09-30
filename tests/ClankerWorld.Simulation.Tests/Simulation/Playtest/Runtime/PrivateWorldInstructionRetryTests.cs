using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Theory]
    [InlineData("retry-key")]
    [InlineData(" retry-key ")]
    public void PrivateInstructionRetriesNormalizeKeysWithoutChangingStateOrSequence(string key)
    {
        using var world = new PrivateWorldRuntime("instruction-key-retry");
        var request = new OwnerInstructionRequest(key, "owner:test", "founder-mira",
            OwnerInstructionKind.Suggestive, "wait safely");
        var receipt = world.SubmitInstruction(request);
        Assert.Equal("retry-key", receipt.IdempotencyKey);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());

        Assert.Equal(receipt, world.SubmitInstruction(request));
        Assert.Equal(receipt, world.SubmitInstruction(request with { IdempotencyKey = "retry-key" }));
        foreach (var conflicting in new[]
        {
            request with { IssuerId = "owner:other" },
            request with { TargetInhabitantId = "missing-agent" },
            request with { Kind = OwnerInstructionKind.MustDo },
            request with { Text = "gather food" },
        })
            Assert.Throws<InvalidOperationException>(() => world.SubmitInstruction(conflicting));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        var next = world.SubmitInstruction(request with { IdempotencyKey = "next-key" });
        Assert.Equal("private-instruction-0000000002", next.InstructionId);
        world.Validate();
    }

    [Fact]
    public void PrivateInstructionReceiptSurvivesRecipientDeathAndCheckpointReload()
    {
        using var initial = new PrivateWorldRuntime("instruction-death-retry");
        var request = new OwnerInstructionRequest("death-retry", "owner:test", "founder-mira",
            OwnerInstructionKind.Suggestive, "wait safely");
        var receipt = initial.SubmitInstruction(request);
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(
            InstructionRecipientFixture.AfterDeath(initial.ExportState(), request.TargetInhabitantId)));
        using var world = PrivateWorldRuntime.Restore(saved);
        world.Validate();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());

        Assert.Equal(receipt, world.SubmitInstruction(request));
        Assert.Throws<InvalidOperationException>(() => world.SubmitInstruction(request with { Text = "gather food" }));
        Assert.Throws<ArgumentException>(() => world.SubmitInstruction(request with { IdempotencyKey = "new-dead-request" }));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Single(world.ExportState().Instructions!);
        Assert.Single(world.ExportState().Events, item => item.Kind == "instruction_queued");

        var next = world.SubmitInstruction(request with { IdempotencyKey = "new-alive-request", TargetInhabitantId = "founder-rowan" });
        Assert.Equal("private-instruction-0000000002", next.InstructionId);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(receipt, restored.SubmitInstruction(request));
        restored.Validate();
    }
}
