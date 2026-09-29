using ClankerWorld.Viewer.Control;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProviderUsageStoreTests
{
    [Theory]
    [InlineData("{broken private sk-secret-meter")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":1,\"Rows\":[null],\"Pending\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Rows\":[],\"Pending\":[null]}")]
    public void UnreadableAccountingBlocksCallsAndLimitChangesWithoutOverwritingEvidence(string damaged)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-damaged-meter-");
        try
        {
            var path = Path.Combine(directory.FullName, "usage.json");
            File.WriteAllText(path, damaged);
            var store = new ProviderUsageStore(path);
            Assert.True(store.Capture().LimitReached);
            Assert.Equal(ProviderUsageStore.RecoveryMessage, store.Capture().AccountingError);
            Assert.Throws<InvalidOperationException>(() => store.Begin("openai", "test", "planning"));
            Assert.Throws<InvalidOperationException>(() => store.Configure(new ProviderUsageLimitAction(null)));
            Assert.Equal(damaged, File.ReadAllText(path));
            Assert.DoesNotContain("sk-secret", System.Text.Json.JsonSerializer.Serialize(store.Capture()));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void InterruptedReplacementKeepsCommittedReservationsAndRecoversOnlyAfterTrustedRestore()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-interrupted-meter-");
        try
        {
            var path = Path.Combine(directory.FullName, "usage.json");
            var original = new ProviderUsageStore(path);
            original.Configure(new ProviderUsageLimitAction(2));
            original.Begin("openai", "test", "planning");
            var trusted = File.ReadAllText(path);
            var interrupted = path + ".interrupted.tmp";
            File.WriteAllText(interrupted, "{partial");
            var restarted = new ProviderUsageStore(path);
            Assert.Equal(1, restarted.Capture().Attempts);
            Assert.Equal(1, restarted.Capture().Abandoned);
            Assert.Equal(2, restarted.Capture().AttemptLimit);
            Assert.Equal("{partial", File.ReadAllText(interrupted));
            File.WriteAllText(path, "{broken");
            var blocked = new ProviderUsageStore(path);
            File.WriteAllText(path, trusted);
            Assert.Throws<InvalidOperationException>(() => blocked.Begin("openai", "test", "planning"));
            var recovered = new ProviderUsageStore(path);
            Assert.Null(recovered.Capture().AccountingError);
            Assert.Equal(1, recovered.Capture().Attempts);
            Assert.Equal(1, recovered.Capture().Abandoned);
            recovered.Begin("openai", "test", "planning");
            Assert.True(recovered.Capture().LimitReached);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task LimitReservesConcurrentAttemptsAndPersistsAbandonedCalls()
    {
        var directory = Path.Combine(Path.GetTempPath(), "clanker-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "usage.json");
            var store = new ProviderUsageStore(path);
            var pauses = 0;
            store.LimitReached += () => Interlocked.Increment(ref pauses);
            _ = store.Configure(new ProviderUsageLimitAction(3));
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            {
                try { return store.Begin("openai", "gpt-5-mini", "planning"); }
                catch (ProviderUsageLimitReachedException) { return null; }
            })));
            var permitted = outcomes.Where(ticket => ticket is not null).Cast<string>().ToArray();
            Assert.Equal(3, permitted.Length);
            Assert.Equal(3, store.Capture().Attempts);
            Assert.True(store.Capture().LimitReached);
            Assert.Equal(1, pauses);
            store.Finish(permitted[0], "completed", 12, 4);
            store.Finish(permitted[1], "failed");
            // The third attempt is still pending at shutdown. Startup must
            // retain its spent allowance and mark its unknown result abandoned.
            var reopened = new ProviderUsageStore(path);
            var status = reopened.Capture();
            Assert.Equal(3, status.Attempts);
            Assert.Equal(1, status.Completed);
            Assert.Equal(1, status.Failed);
            Assert.Equal(1, status.Abandoned);
            Assert.Equal(12, status.InputTokens);
            Assert.Equal(4, status.OutputTokens);
            Assert.Equal("openai", Assert.Single(status.Rows).Provider);
            Assert.Throws<ProviderUsageLimitReachedException>(() => reopened.Begin("openai", "gpt-5-mini", "planning"));
            _ = reopened.Configure(new ProviderUsageLimitAction(null, AdditionalCalls: 2));
            Assert.Equal(5, reopened.Capture().AttemptLimit);
            var retry = reopened.Begin("openai", "gpt-5-mini", "planning");
            reopened.Finish(retry, "completed", 3, 1);
            Assert.Equal(4, reopened.Capture().Attempts);
            Assert.False(reopened.Capture().LimitReached);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void UsagePayloadMatchesGodotAndNeverContainsProviderSecrets()
    {
        var server = new ProviderUsageLimitAction(30, 0);
        var client = new ClankerWorld.GodotClient.UI.OwnerUsageLimitAction(30, 0);
        Assert.Equal(OwnerHttpBinding.UsageLimitPayload(server),
            ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.UsageLimit(client));
        Assert.Equal(OwnerHttpBinding.UsageStatusPayload(),
            ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.UsageStatus());
        Assert.DoesNotContain("api-key", OwnerHttpBinding.UsageLimitPayload(server));
    }

    [Fact]
    public void MeterRedactsCredentialShapedModelLabels()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-usage-redaction-");
        try
        {
            var path = Path.Combine(directory.FullName, "usage.json");
            var store = new ProviderUsageStore(path);
            var ticket = store.Begin("openai", "sk-secret-test-value", "planning");
            store.Finish(ticket, "abandoned");
            Assert.Equal("other", Assert.Single(store.Capture().Rows).Model);
            Assert.DoesNotContain("sk-secret-test-value", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void FailedReservationDoesNotChargeOrPersistAnAttemptBeforeAnyProviderCall()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-usage-write-failure-");
        try
        {
            var blockedParent = Path.Combine(directory.FullName, "blocked-parent");
            File.WriteAllText(blockedParent, "not a directory");
            var path = Path.Combine(blockedParent, "usage.json");
            var store = new ProviderUsageStore(path);
            Assert.ThrowsAny<IOException>(() => store.Begin("ollama-cloud", "test-model", "planning"));
            Assert.Equal(0, store.Capture().Attempts);

            File.Delete(blockedParent);
            Directory.CreateDirectory(blockedParent);
            var ticket = store.Begin("ollama-cloud", "test-model", "planning");
            store.Finish(ticket, "completed");
            Assert.Equal(1, store.Capture().Attempts);
            Assert.Equal(1, new ProviderUsageStore(path).Capture().Completed);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task LimitTriggeredInsideHostedCompletionPausesBeforeDecisionAdmission()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-usage-tick-");
        try
        {
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            _ = usage.Configure(new ProviderUsageLimitAction(1));
            var provider = new HeldUsageProvider(usage);
            using var world = new PrivateWorldRuntime("usage-pause-boundary", id =>
                id == "founder-scout" ? provider : new DeterministicDecisionProvider());
            usage.LimitReached += world.Pause;
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var concurrentTick = world.AdvanceOneTickNonBlockingAsync().AsTask();
            provider.Release.TrySetResult(true);
            var raced = await concurrentTick.WaitAsync(TimeSpan.FromSeconds(10));
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(world.Society.IsPaused);
            Assert.DoesNotContain(raced.Decisions, item => item.InhabitantId == "founder-scout");
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "hosted_decision_completed");
            Assert.Contains(world.ExportState().Society.Cognition.Queue,
                item => item.InhabitantId == "founder-scout");
            var stopped = await world.AdvanceOneTickNonBlockingAsync();
            Assert.False(stopped.Advanced);
            Assert.Equal(1, usage.Capture().Attempts);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class HeldUsageProvider(ProviderUsageStore usage) : IDecisionProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var ticket = usage.Begin("openai", "test-model", "planning");
            Started.TrySetResult(true);
            await Release.Task; // Intentionally ignore cancellation, like a late provider reply.
            usage.Finish(ticket, "completed", 8, 2); // Synchronously fires pause before returning a decision.
            var selected = request.Observation.Candidates.First(item => item.Id == "safe_idle");
            Returned.TrySetResult(true);
            return new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                selected.Id, 1d, request.Observation.Candidates.ToDictionary(item => item.Id,
                    item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal));
        }
    }
}
