using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Theory]
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
}
