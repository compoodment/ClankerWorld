using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldAtomicTickTests
{
    [Fact]
    public async Task CancellationDuringProviderWorkLeavesEverySubsystemUnchangedAndRetryCommitsOnce()
    {
        var provider = new ControlledProvider();
        using var runtime = new PrivateWorldRuntime("atomic-tick", _ => provider);
        var before = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
        using var cancellation = new CancellationTokenSource();
        var pending = runtime.AdvanceOneTickAsync(cancellation.Token).AsTask();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var observed = await Task.Run(runtime.ExportState).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(observed));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
        provider.Release.TrySetResult(true);
        Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, runtime.WorldTick);
        Assert.Single(runtime.ExportState().Events, item => item.Kind == "tick_advanced");
    }

    [Fact]
    public async Task PauseIsResponsiveAndInvalidatesPendingProviderWork()
    {
        var provider = new ControlledProvider();
        using var runtime = new PrivateWorldRuntime("atomic-tick", _ => provider);
        var pending = runtime.AdvanceOneTickAsync().AsTask();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Run(runtime.Pause).WaitAsync(TimeSpan.FromSeconds(3));
        var paused = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
        provider.Release.TrySetResult(true);
        Assert.False((await pending).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
        Assert.Equal(0, runtime.WorldTick);
        runtime.Resume();
        Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
    }

    [Fact]
    public async Task ConcurrentTicksCommitInOrderWithoutDuplicatingOrLosingTicks()
    {
        var provider = new ControlledProvider();
        using var runtime = new PrivateWorldRuntime("atomic-tick", _ => provider);
        var first = runtime.AdvanceOneTickAsync().AsTask();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = runtime.AdvanceOneTickAsync().AsTask();
        provider.Release.TrySetResult(true);
        Assert.Equal(1, (await first).WorldTick);
        Assert.Equal(2, (await second).WorldTick);
    }

    private sealed class ControlledProvider : IDecisionProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return await new DeterministicDecisionProvider().DecideAsync(request, cancellationToken);
        }
    }
}
