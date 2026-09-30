using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class OwnerClientPresenceLeaseTests
{
    [Fact]
    public async Task DisconnectCancelsDeferredHostedCallButKeepsCommittedWorldAndQueue()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clankerworld-deferred-presence-{Guid.NewGuid():N}");
        try
        {
            var clock = new ManualTimeProvider();
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(5), clock);
            var provider = new WaitingHostedProvider();
            using var runtime = new PrivateWorldRuntime("deferred-presence", id =>
                id == "founder-scout" ? provider : new DeterministicDecisionProvider());
            using var service = new PrivateWorldRuntimeService(runtime,
                new PrivateWorldStateFile(Path.Combine(directory, "world.json")), presence);
            presence.RecordAuthenticatedReconnect("owner");
            Assert.True(await service.TryAdvanceOnceAsync());
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var committedTick = runtime.WorldTick;
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.False(await service.TryAdvanceOnceAsync());
            await provider.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(committedTick, runtime.WorldTick);
            Assert.Contains(runtime.ExportState().Society.Cognition.Queue,
                item => item.InhabitantId == "founder-scout");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class WaitingHostedProvider : IDecisionProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.Jev;
        public long ProviderEpoch => 1;
        public async ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult(true);
                throw;
            }
            throw new InvalidOperationException("The hosted request must be cancelled.");
        }
    }

    [Theory]
    [InlineData("founder-scout")]
    [InlineData("founder:00000000000000000000000000000001")]
    public async Task HostedFailureLogReportsCompleteActorAndOutcomeWithoutProviderExceptionText(string actorId)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clankerworld-hosted-log-{Guid.NewGuid():N}");
        try
        {
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(1));
            var provider = new ThrowingHostedProvider();
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var runtime = new PrivateWorldRuntime("hosted-log", id =>
                id == actorId ? provider : new DeterministicDecisionProvider(),
                startPace: actorId.Contains(':') ? WorldStartPace.FounderSetup : WorldStartPace.Legacy);
            if (actorId.Contains(':'))
            {
                runtime.PlaceFounder(actorId, new(0, 0));
                runtime.PlaceFounder("founder:00000000000000000000000000000002", new(1, 2));
                runtime.PlaceFounder("founder:00000000000000000000000000000003", new(2, 2));
                runtime.PlaceFounder("founder:00000000000000000000000000000004", new(3, 2));
                runtime.StartWorld();
            }
            using var service = new PrivateWorldRuntimeService(runtime,
                new PrivateWorldStateFile(Path.Combine(directory, "world.json")), presence, logger);
            presence.RecordAuthenticatedReconnect("owner");
            Assert.True(await service.TryAdvanceOnceAsync());
            await provider.Retried.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var attempt = 0; attempt < 50 &&
                 !logger.Messages.Any(message => message.Contains("hosted_decision", StringComparison.Ordinal) &&
                     message.Contains("provider_failure", StringComparison.Ordinal)); attempt++)
            {
                await service.TryAdvanceOnceAsync();
                await Task.Delay(10);
            }
            Assert.Contains(logger.Messages, message => message.Contains("hosted_decision", StringComparison.Ordinal) &&
                message.Contains("provider_failure:HttpRequestException", StringComparison.Ordinal));
            Assert.All(logger.Messages.Where(message => message.Contains("hosted_decision tick=", StringComparison.Ordinal)),
                message => Assert.Contains($"inhabitant={actorId} outcome=", message, StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("super-secret-api-key", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ThrowingHostedProvider : IDecisionProvider
    {
        private int calls;
        public TaskCompletionSource<bool> Retried { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.Jev;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) >= 2) Retried.TrySetResult(true);
            throw new HttpRequestException("super-secret-api-key-must-not-appear");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectOrPauseCancelsAnInFlightProviderWithoutAdvancing(bool manualPause)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clankerworld-presence-cancel-{Guid.NewGuid():N}");
        try
        {
            var clock = new ManualTimeProvider();
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(5), clock);
            var provider = new WaitingProvider();
            using var runtime = new PrivateWorldRuntime("presence-cancel", _ => provider);
            using var service = new PrivateWorldRuntimeService(runtime,
                new PrivateWorldStateFile(Path.Combine(directory, "world.json")), presence);
            presence.RecordAuthenticatedReconnect("owner");
            var tick = service.TryAdvanceOnceAsync().AsTask();
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (manualPause)
            {
                runtime.Pause();
            }
            else
            {
                clock.Advance(TimeSpan.FromSeconds(5));
            }
            var expected = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            Assert.False(await tick.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(0, runtime.WorldTick);
            Assert.Equal(expected, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(manualPause, runtime.Society.IsPaused);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class WaitingProvider : IDecisionProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The test provider must be cancelled.");
        }
    }

    [Fact]
    public void AuthenticatedReconnectLeaseExpiresAndSupportsMultipleDevices()
    {
        var clock = new ManualTimeProvider();
        var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(5), clock);

        Assert.False(presence.HasActiveClient);

        presence.RecordAuthenticatedReconnect("device-one");
        Assert.True(presence.HasActiveClient);
        Assert.Equal(1, presence.ActiveClientCount);

        clock.Advance(TimeSpan.FromSeconds(3));
        presence.RecordAuthenticatedReconnect("device-two");
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(presence.HasActiveClient);
        Assert.Equal(1, presence.ActiveClientCount);

        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.False(presence.HasActiveClient);
        Assert.Equal(0, presence.ActiveClientCount);
    }

    [Fact]
    public async Task PrivateWorldAdvancesOnlyDuringPresenceAndStillHonorsManualPause()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"clankerworld-client-presence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var clock = new ManualTimeProvider();
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(5), clock);
            using var runtime = new PrivateWorldRuntime("client-presence-seed");
            var stateFile = new PrivateWorldStateFile(Path.Combine(directory, "world.json"));
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            var service = new PrivateWorldRuntimeService(runtime, stateFile, presence, logger);

            Assert.False(await service.TryAdvanceOnceAsync());
            Assert.Equal(0, runtime.WorldTick);

            presence.RecordAuthenticatedReconnect("device-one");
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Equal(1, runtime.WorldTick);

            runtime.Pause();
            Assert.False(await service.TryAdvanceOnceAsync());
            Assert.Equal(1, runtime.WorldTick);
            Assert.Contains(logger.Messages, message =>
                message.Contains("world_tick_gate state=waiting_for_client", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message =>
                message.Contains("world_tick_gate state=advancing", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message =>
                message.Contains("world_tick_gate state=paused", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, message =>
                message.Contains("cognition_decision tick=1", StringComparison.Ordinal));

            runtime.Resume();
            for (var tick = 0; tick < 20; tick++)
            {
                Assert.True(await service.TryAdvanceOnceAsync());
            }
            Assert.Contains(logger.Messages, message => message.Contains("settlement_activity", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("Cooking fire", StringComparison.Ordinal));
            var tickBeforeDisconnect = runtime.WorldTick;
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.False(await service.TryAdvanceOnceAsync());
            Assert.Equal(tickBeforeDisconnect, runtime.WorldTick);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FailedRecoveryWriteRetainsTickPausesAndRetriesWithoutAdvancing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "recovery-secret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var blocker = Path.Combine(directory, "blocked");
        File.WriteAllText(blocker, "secret-key-must-not-be-logged");
        try
        {
            using var runtime = new PrivateWorldRuntime("recovery-hold");
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            var file = new PrivateWorldStateFile(Path.Combine(blocker, "world.json"));
            using var service = new PrivateWorldRuntimeService(runtime, file, presence, logger);
            Assert.False(await service.TryAdvanceOnceAsync());
            Assert.Equal(1, runtime.WorldTick);
            Assert.True(runtime.Society.IsPaused);
            runtime.Resume();
            Assert.False(await service.TryAdvanceOnceAsync());
            Assert.Equal(1, runtime.WorldTick);
            Assert.True(runtime.Society.IsPaused);
            Assert.Single(logger.Messages, message => message.Contains("held_for_write", StringComparison.Ordinal));
            File.Delete(blocker);
            Directory.CreateDirectory(blocker);
            Assert.False(await service.TryAdvanceOnceAsync());
            Assert.Equal(1, runtime.WorldTick);
            Assert.True(runtime.Society.IsPaused);
            Assert.True(File.Exists(file.Path));
            Assert.Contains(logger.Messages, message => message.Contains("saved_paused", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains(directory, StringComparison.Ordinal) ||
                message.Contains("secret-key", StringComparison.Ordinal));
            runtime.Resume();
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Equal(2, runtime.WorldTick);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        public void Advance(TimeSpan duration) => timestamp = checked(timestamp + duration.Ticks);
    }
}
