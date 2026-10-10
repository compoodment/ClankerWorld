using System.Diagnostics;
using System.Reflection;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class CommittedTickTimingTests
{
    private static readonly Lazy<Task<byte[]>> WarmWorld = new(async () =>
    {
        using var world = NormalPathWorld.CreateGenerated("committed-tick-timing", _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < 2; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedTimingIncludesSynchronousCommitChecksAndChildSelectionPreparation(bool childPreparation)
    {
        var initial = await WarmWorld.Value;
        using var measured = Restore(initial);
        using var control = Restore(initial);
        Assert.Null(measured.ExportStateWithDiagnostics().Diagnostics.LastTickMilliseconds);
        Assert.True((await measured.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.True((await control.AdvanceOneTickNonBlockingAsync()).Advanced);
        var previous = measured.ExportStateWithDiagnostics().Diagnostics.LastTickMilliseconds!.Value;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var callbackMilliseconds = 0d;
        var calls = 0;
        void Prepare()
        {
            if (Interlocked.Increment(ref calls) != 1) return;
            var started = Stopwatch.GetTimestamp();
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            callbackMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        // A controlled hold at the real public commit boundary establishes a
        // measured component; this is a telemetry check, not a speed benchmark.
        var step = Task.Run(async () => await measured.AdvanceOneTickNonBlockingAsync(
            commitPermitted: () => { if (!childPreparation) Prepare(); return true; },
            prepareChildModelSelections: (_, _) => { if (childPreparation) Prepare(); return []; }));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(previous + 200, 250, 1_500)));
        }
        finally { release.Set(); }
        Assert.True((await step.WaitAsync(TimeSpan.FromSeconds(10))).Advanced);
        Assert.True((await control.AdvanceOneTickNonBlockingAsync()).Advanced);
        var reported = measured.ExportStateWithDiagnostics().Diagnostics.LastTickMilliseconds!.Value;
        Assert.True(reported >= callbackMilliseconds - 0.1,
            $"Committed time {reported:F1} ms omitted synchronous preparation lasting {callbackMilliseconds:F3} ms.");
        var committed = PrivateWorldRuntimeCodec.Encode(measured.ExportState());
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(control.ExportState()), committed);
        Assert.False((await measured.AdvanceOneTickNonBlockingAsync(() => false)).Advanced);
        Assert.Equal(reported, measured.ExportStateWithDiagnostics().Diagnostics.LastTickMilliseconds);
        Assert.Equal(committed, PrivateWorldRuntimeCodec.Encode(measured.ExportState()));
        measured.Pause();
        measured.LoadPausedCheckpoint(PrivateWorldRuntimeCodec.Decode(committed));
        Assert.Null(measured.ExportStateWithDiagnostics().Diagnostics.LastTickMilliseconds);
        using var restored = Restore(committed);
        Assert.Null(restored.ExportStateWithDiagnostics().Diagnostics.LastTickMilliseconds);
    }

    [Theory]
    [InlineData("tickGate")]
    [InlineData("gate")]
    [InlineData("child-reacquisition")]
    public async Task CommittedTimingExcludesInitialAndChildPreparationRuntimeGateWaits(string boundary)
    {
        var initial = await WarmWorld.Value;
        using var measured = Restore(initial);
        using var control = Restore(initial);
        // Inspect the existing lock only to create real contention; no runtime
        // hook or saved fixture field is added or changed.
        var gateName = boundary == "child-reacquisition" ? "gate" : boundary;
        var gate = (SemaphoreSlim)typeof(PrivateWorldRuntime).GetField(gateName, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(measured)!;
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long heldStarted = 0;
        if (boundary != "child-reacquisition")
        {
            await gate.WaitAsync();
            heldStarted = Stopwatch.GetTimestamp();
            acquired.TrySetResult();
        }
        var callStarted = Stopwatch.GetTimestamp();
        var step = Task.Run(async () => await measured.AdvanceOneTickNonBlockingAsync(
            commitPermitted: null,
            prepareChildModelSelections: (_, _) =>
            {
                if (boundary == "child-reacquisition")
                {
                    gate.Wait();
                    heldStarted = Stopwatch.GetTimestamp();
                    acquired.TrySetResult();
                }
                return [];
            }));
        double heldMilliseconds;
        try
        {
            await acquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(250);
            heldMilliseconds = Stopwatch.GetElapsedTime(heldStarted).TotalMilliseconds;
        }
        finally { gate.Release(); }
        Assert.True((await step.WaitAsync(TimeSpan.FromSeconds(10))).Advanced);
        var callMilliseconds = Stopwatch.GetElapsedTime(callStarted).TotalMilliseconds;
        var reported = measured.ExportStateWithDiagnostics().Diagnostics.LastTickMilliseconds!.Value;
        Assert.True(callMilliseconds - reported >= heldMilliseconds - 10,
            $"Readout {reported:F1} ms included {boundary} contention: call={callMilliseconds:F3}, held={heldMilliseconds:F3} ms.");
        Assert.True((await control.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(control.ExportState()), PrivateWorldRuntimeCodec.Encode(measured.ExportState()));
    }

    private static PrivateWorldRuntime Restore(byte[] bytes) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ActionCoverageRecorder(chooseIdle: true));
}
