using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Fact]
    public async Task PreparedTickKeepsNoOpCancellationReceiptAddedBeforeCommit()
    {
        var provider = new HeldTickProvider();
        using var world = new PrivateWorldRuntime("order-cancel-receipt-commit",
            id => id == OrderedAgent ? provider : new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var closed = world.SubmitInstruction(new OwnerInstructionRequest(
            "already-closed-order", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "build a house"));
        Assert.Equal("not_understood", Assert.Single(world.ExportState().Instructions!, item =>
            item.InstructionId == closed.InstructionId).Order!.Status);

        var advance = world.AdvanceOneTickAsync().AsTask();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var tickBeforeCancellation = world.WorldTick;

        var cancellation = new OwnerOrderCancelRequest(
            "closed-order-receipt", "owner:test", world.Society.WorldId, OrderedAgent, closed.InstructionId);
        var receipt = world.CancelOrder(cancellation);
        Assert.False(receipt.Changed);
        Assert.Equal("not_understood", receipt.Status);

        provider.Release.TrySetResult(true);
        try
        {
            var rejected = await advance.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(rejected.Advanced);
            Assert.Equal("tick_superseded_by_owner_change", rejected.Outcome);
            Assert.Equal(tickBeforeCancellation, world.WorldTick);
        }
        finally
        {
            provider.Release.TrySetResult(true);
        }

        var checkpoint = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Contains(checkpoint.OrderCancellations!, item =>
            item.IdempotencyKey == cancellation.IdempotencyKey && item.Receipt == receipt);
        using var restored = PrivateWorldRuntime.Restore(checkpoint);
        Assert.Equal(receipt, restored.CancelOrder(cancellation));
        restored.Resume();
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        restored.Validate();
    }

    private sealed class HeldTickProvider : IDecisionProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 1;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            var selected = request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new CognitionDecisionResponse(
                request.RequestId,
                request.Observation.InhabitantId,
                Kind,
                ProviderEpoch,
                request.Observation.RunEpoch,
                request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest,
                selected.Id,
                1d,
                request.Observation.Candidates.ToDictionary(
                    candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d,
                    StringComparer.Ordinal));
        }
    }
}
