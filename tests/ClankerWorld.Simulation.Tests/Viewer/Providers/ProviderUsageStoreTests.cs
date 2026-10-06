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

    [Theory]
    [InlineData(7, 6)]
    [InlineData(1, 1)]
    public void WarningMarkIsEightyPercentRoundedUp(long limit, long mark) =>
        Assert.Equal(mark, ProviderUsageStore.WarningMark(limit));

    [Fact]
    public async Task EightyPercentWarningIsRaisedOncePerCrossingAcrossRestartsAndLimitChanges()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-usage-warning-");
        try
        {
            var path = Path.Combine(directory.FullName, "usage.json");
            var store = new ProviderUsageStore(path);
            var warnings = new System.Collections.Concurrent.ConcurrentQueue<ProviderUsageWarning>();
            store.WarningReached += warnings.Enqueue;
            store.Finish(store.Begin("openai", "test-model", "planning"), "completed");
            Assert.Empty(warnings);

            // Concurrent reservations cross 8 of 10 exactly once, not once per call.
            _ = store.Configure(new ProviderUsageLimitAction(10));
            var tickets = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            {
                try { return store.Begin("openai", "test-model", "planning"); }
                catch (ProviderUsageLimitReachedException) { return null; }
            })));
            Assert.Equal(9, tickets.Count(ticket => ticket is not null));
            Assert.Equal(new ProviderUsageWarning(8, 10), Assert.Single(warnings));
            foreach (var ticket in tickets.OfType<string>()) store.Finish(ticket, "failed");
            Assert.True(store.Capture().LimitReached);

            // A restart, a grant at the limit and a limit below the count make no new crossing.
            var restarted = new ProviderUsageStore(path);
            restarted.WarningReached += warnings.Enqueue;
            Assert.Equal(10, restarted.Capture().Attempts);
            _ = restarted.Configure(new ProviderUsageLimitAction(null, AdditionalCalls: 2));
            restarted.Finish(restarted.Begin("openai", "test-model", "planning"), "completed");
            _ = restarted.Configure(new ProviderUsageLimitAction(5));
            Assert.Throws<ProviderUsageLimitReachedException>(() => restarted.Begin("openai", "test-model", "planning"));
            Assert.Single(warnings);

            // Raising the limit sets a new mark (16 of 20) that later calls cross once.
            _ = restarted.Configure(new ProviderUsageLimitAction(20));
            while (restarted.Capture().Attempts < 19)
                restarted.Finish(restarted.Begin("openai", "test-model", "planning"), "completed");
            Assert.Equal(new[] { new ProviderUsageWarning(8, 10), new ProviderUsageWarning(16, 20) }, warnings);
            Assert.False(restarted.Capture().LimitReached);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void ModelCallWarningIsASavedWorldEventAndNeverWaitsOnABusyRuntime()
    {
        using var world = new PrivateWorldRuntime("usage-warning-event", _ => new DeterministicDecisionProvider());
        // Persisting holds the runtime gate, as a conversation turn does when it reserves a call.
        world.PersistCheckpoint(state =>
        {
            Assert.False(world.TryRecordModelCallWarning(4, 5, TimeSpan.Zero));
            return state;
        });
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "model_call_warning");
        Assert.Throws<ArgumentOutOfRangeException>(() => world.TryRecordModelCallWarning(6, 5, TimeSpan.Zero));

        Assert.True(world.TryRecordModelCallWarning(800, 1_000, TimeSpan.Zero));
        var warning = Assert.Single(world.ExportState().Events, item => item.Kind == "model_call_warning");
        Assert.Equal("used:800:limit:1000", warning.Detail);
        Assert.Null(warning.Position);
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(warning, Assert.Single(restored.ExportState().Events, item => item.Kind == "model_call_warning"));
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
}
