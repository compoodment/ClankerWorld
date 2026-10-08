using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class StaleUsagePauseTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DelayedLimitEffectsRecheckTheCurrentAllowanceBeforePausingAndSaving(
        bool grantMore, bool holdRuntimeGate)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-stale-usage-");
        Task? completion = null;
        Task? checkpoint = null;
        using var signalled = new ManualResetEventSlim();
        using var runtimeEntered = new ManualResetEventSlim();
        using var runtimeRelease = new ManualResetEventSlim();
        using var world = NormalPathWorld.CreateGenerated("stale-call-limit-notification",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        try
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            _ = usage.Configure(new(1));
            var ticket = usage.Begin("openai", "fixture-model", "personal");
            var gate = new object();
            var effects = new ProviderUsageWorldEffects(world, file, usage, gate, NullLogger.Instance);
            usage.LimitReached += signalled.Set;
            usage.LimitReached += effects.PauseAtLimit;
            if (grantMore && !holdRuntimeGate) world.Pause();
            _ = file.Save(world);
            Assert.True(File.Exists(file.Path));
            void GrantMore()
            {
                var allowed = usage.Configure(new(null, AdditionalCalls: 10));
                Assert.Equal(1, allowed.Attempts);
                Assert.Equal(11, allowed.AttemptLimit);
                Assert.False(allowed.LimitReached);
            }
            if (holdRuntimeGate)
            {
                checkpoint = Task.Run(() => world.PersistCheckpoint(state =>
                {
                    runtimeEntered.Set();
                    Assert.True(runtimeRelease.Wait(TimeSpan.FromSeconds(10)));
                    return state;
                }));
                Assert.True(runtimeEntered.Wait(TimeSpan.FromSeconds(10)));
                completion = Task.Run(() => usage.Finish(ticket, "completed", 7, 3));
                Assert.True(signalled.Wait(TimeSpan.FromSeconds(10)));
                // The effect has acquired the host gate and is waiting for the
                // native checkpoint operation, so checking only the host gate
                // would still observe the old allowance.
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    if (!Monitor.TryEnter(gate)) return true;
                    Monitor.Exit(gate);
                    return false;
                }, TimeSpan.FromSeconds(10)));
                Assert.False(completion.IsCompleted);
                if (grantMore) GrantMore();
                runtimeRelease.Set();
                await checkpoint.WaitAsync(TimeSpan.FromSeconds(10));
            }
            else
            {
                lock (gate)
                {
                    completion = Task.Run(() => usage.Finish(ticket, "completed", 7, 3));
                    Assert.True(signalled.Wait(TimeSpan.FromSeconds(10)));
                    Assert.False(completion.IsCompleted);
                    if (grantMore)
                    {
                        GrantMore();
                        _ = file.Save(world, resumeOnSuccess: true);
                    }
                    Assert.False(world.Society.IsPaused);
                }
            }
            await completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal((1L, 1L, 0L, 7L, 3L), (usage.Capture().Attempts, usage.Capture().Completed,
                usage.Capture().Abandoned, usage.Capture().InputTokens, usage.Capture().OutputTokens));
            Assert.Equal(!grantMore, usage.Capture().LimitReached);
            Assert.Equal(!grantMore, world.Society.IsPaused);
            var saved = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path));
            Assert.Equal(!grantMore, saved.Society.Society.IsPaused);
            using var restored = PrivateWorldRuntime.Restore(saved);
            restored.Validate();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(saved), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            world.Validate();
            var reopened = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            Assert.Equal((usage.Capture().Attempts, usage.Capture().Completed, usage.Capture().AttemptLimit),
                (reopened.Capture().Attempts, reopened.Capture().Completed, reopened.Capture().AttemptLimit));
        }
        finally
        {
            runtimeRelease.Set();
            if (checkpoint is not null) await checkpoint.WaitAsync(TimeSpan.FromSeconds(10));
            if (completion is not null) await completion.WaitAsync(TimeSpan.FromSeconds(10));
            directory.Delete(recursive: true);
        }
    }
}
