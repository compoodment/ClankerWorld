using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ModelAttemptOutcomeTests
{
    private const string TargetId = "founder:00000000000000000000000000000001";
    private static readonly JsonSerializerOptions ClientJson = new() { PropertyNameCaseInsensitive = true };

    [Theory]
    [InlineData("missing", "missing_key", 0)]
    [InlineData("limit", "usage_limit", 0)]
    [InlineData("malformed", "unusable_reply", 1)]
    [InlineData("illegal", "unusable_reply", 1)]
    [InlineData("network", "model_unavailable", 1)]
    [InlineData("timeout", "timed_out", 1)]
    [InlineData("unsupported", "model_unavailable", 1)]
    public async Task FailedNormalWorldAttemptHasOneChargeAndSafePersistedStatus(string mode, string expectedStatus, int attempts)
    {
        using var fixture = new Fixture(mode);
        // Keep other residents' choices stable so this measures one attempt,
        // without a new invitation or household change prompting another.
        using var world = NormalPathWorld.CreateGenerated("model-outcomes", id =>
            id == TargetId ? fixture.Provider : new ActionCoverageRecorder(chooseIdle: true));
        var decision = await CompleteNext(world);
        world.Pause();
        Assert.True(decision.Admission.FellBack);
        Assert.Equal("safe_idle", decision.Admission.Intention?.CandidateId);
        Assert.Equal(attempts, fixture.Handler.Calls);
        Assert.Equal(attempts, fixture.Usage.Capture().Attempts - fixture.InitialAttempts);
        Assert.Equal(expectedStatus, Status(world));
        var failureRows = ClientFailures(world);
        var failure = Assert.Single(failureRows);
        Assert.Equal($"{TargetId}:{expectedStatus}", failure.Detail);
        var clientSnapshot = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldSnapshot>(
            JsonSerializer.Serialize(new OwnerWorldObservationStore(world).GetSnapshot()),
            ClientJson)!;
        var description = ClankerWorld.GodotClient.UI.WorldEventText.Describe(failure, clientSnapshot);
        Assert.Contains("could not get a model reply:", description, StringComparison.Ordinal);
        Assert.DoesNotContain(TargetId, description, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-private-status", description, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-private-status", JsonSerializer.Serialize(world.ExportState()), StringComparison.Ordinal);
        Assert.Null(world.Inhabitants.Single(item => item.InhabitantId == TargetId).LastModelAttempt!.LastAcceptedCandidateId);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => fixture.Provider);
        Assert.Equal(expectedStatus, Status(restored));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(failureRows, ClientFailures(restored));
        if (mode == "unsupported")
            Assert.Equal("unsupported_request", restored.Inhabitants.Single(item => item.InhabitantId == TargetId).LastModelAttempt!.SetupBlocker);
    }

    [Fact]
    public async Task LowConfidenceReplyWithoutProbabilityMapKeepsNameAndLastChoiceAfterLaterFailure()
    {
        using var fixture = new Fixture("accepted");
        using var world = NormalPathWorld.CreateGenerated("model-outcomes", id =>
            id == TargetId ? fixture.Provider : new ActionCoverageRecorder(chooseIdle: true));
        var accepted = await CompleteNext(world);
        Assert.False(accepted.Admission.FellBack);
        Assert.Equal(0.1, accepted.Admission.Intention!.Confidence);
        Assert.NotEqual("safe_idle", accepted.Admission.Intention.CandidateId);
        Assert.Equal("Aster Vale", world.Society.GetInhabitant(TargetId).Name);
        Assert.Equal("ready", Status(world));
        Assert.DoesNotContain("probabilities", fixture.Handler.SystemPrompt!, StringComparison.Ordinal);
        // No unmeasured output cap or temperature is imposed on models with
        // different output needs. The setup check uses this same adapter.
        Assert.False(fixture.Handler.SentOutputLimit);
        fixture.Handler.Mode = "malformed";
        var failed = await CompleteNext(world);
        world.Pause();
        Assert.True(failed.Admission.FellBack);
        Assert.Equal("unusable_reply", Status(world));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => fixture.Provider);
        Assert.Equal(accepted.Admission.Intention.CandidateId,
            restored.Inhabitants.Single(item => item.InhabitantId == TargetId).LastModelAttempt!.LastAcceptedCandidateId);
        Assert.Equal("safe_idle", restored.ExportState().Society.Cognition.Runtimes.Single(item => item.InhabitantId == TargetId).CurrentIntention!.CandidateId);
        Assert.Contains(new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants.Single(item => item.Id == TargetId).DecisionFactors,
            item => item.Key == "last-model-choice" && item.Detail == accepted.Admission.Intention.CandidateId);
    }

    [Fact]
    public async Task PauseCancelsWaitingStatusAndLateReplyCannotReplaceItAfterReload()
    {
        using var fixture = new Fixture("held");
        using var world = NormalPathWorld.CreateGenerated("model-outcomes", _ => fixture.Provider);
        await world.AdvanceOneTickNonBlockingAsync();
        await fixture.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("waiting", Status(world));
        Assert.Equal("waiting", Status(world)); // independent refreshed projection
        Assert.Empty(ClientFailures(world));
        world.Pause();
        Assert.Equal("canceled", Status(world));
        Assert.Empty(ClientFailures(world));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        fixture.Handler.Release.TrySetResult(true);
        await fixture.Handler.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("canceled", Status(world));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => fixture.Provider);
        Assert.Equal("canceled", Status(restored));
        Assert.True(restored.Society.GetInhabitant(TargetId).NeedsName);
        Assert.Null(restored.Inhabitants.Single(item => item.InhabitantId == TargetId).LastModelAttempt!.LastAcceptedCandidateId);
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.Equal(1, fixture.Usage.Capture().Attempts);
    }

    private static ClankerWorld.GodotClient.UI.OwnerWorldEvent[] ClientFailures(PrivateWorldRuntime world)
    {
        var events = new OwnerWorldObservationStore(world).GetEventsAfter(0).Events.Select(item =>
            new ClankerWorld.GodotClient.UI.OwnerWorldEvent(item.EventId, item.WorldTick, item.Kind, item.Detail));
        return ClankerWorld.GodotClient.UI.GameUiText.PlayerEvents(events)
            .Where(item => item.Kind == "model_attempt_status" && item.Detail.StartsWith(TargetId + ":", StringComparison.Ordinal)).ToArray();
    }

    private static string Status(PrivateWorldRuntime world) =>
        new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(item => item.Id == TargetId)
            .DecisionFactors.Single(item => item.Key == "model-status").Detail;

    private static async Task<ClankerWorld.Simulation.Society.SocietyCognitionDispatchResult> CompleteNext(PrivateWorldRuntime world)
    {
        for (var tick = 0; tick < 400; tick++)
        {
            var step = await world.AdvanceOneTickNonBlockingAsync();
            var decision = step.Decisions.FirstOrDefault(item => item.InhabitantId == TargetId);
            if (decision is not null) return decision;
            await Task.Delay(5);
        }
        throw new TimeoutException("The controlled model attempt was not completed.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("clanker-model-outcomes-");
        public Fixture(string mode)
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new("deterministic", null, null, null, null, null, null));
            if (mode != "missing")
                store.Configure(new("personal", "openai", "synthetic-model", "synthetic-key", false, TargetId));
            Usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            if (mode == "limit")
            {
                Usage.Configure(new(1));
                Usage.Finish(Usage.Begin("openai", "previous-call", "planning"), "completed");
            }
            InitialAttempts = Usage.Capture().Attempts;
            Handler = new ReplyHandler(mode);
            Provider = mode == "missing"
                ? new JevDecisionProvider(new HttpClient(Handler), () => null)
                : new ConfigurableDecisionProvider(store, new ClientFactory(Handler), usageStore: Usage);
        }
        public ProviderUsageStore Usage { get; }
        public ReplyHandler Handler { get; }
        public IDecisionProvider Provider { get; }
        public long InitialAttempts { get; }
        public void Dispose() => directory.Delete(recursive: true);
    }

    private sealed class ClientFactory(ReplyHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ReplyHandler(string mode) : HttpMessageHandler
    {
        public string Mode { get; set; } = mode;
        public int Calls { get; private set; }
        public string? SystemPrompt { get; private set; }
        public bool SentOutputLimit { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            SystemPrompt = payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
            SentOutputLimit = payload.RootElement.TryGetProperty("max_tokens", out _) || payload.RootElement.TryGetProperty("max_completion_tokens", out _);
            using var question = JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var selected = question.RootElement.GetProperty("candidates").EnumerateArray()
                .First(item => item.GetProperty("id").GetString() != "safe_idle").GetProperty("id").GetString();
            if (Mode == "network") throw new HttpRequestException("private failure sk-private-status");
            if (Mode == "timeout") throw new TaskCanceledException("private failure sk-private-status");
            if (Mode == "unsupported") return new(HttpStatusCode.BadRequest);
            if (Mode == "held")
            {
                Started.TrySetResult(true);
                await Release.Task; // ignores cancellation to prove late reply refusal
            }
            var answer = Mode == "malformed" ? "{broken private sk-private-status" : JsonSerializer.Serialize(new
            {
                selected_candidate_id = Mode == "illegal" ? "never-offered" : selected,
                confidence = 0.1,
                chosen_name = "Aster Vale",
            });
            Returned.TrySetResult(true);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = answer } } } }), Encoding.UTF8, "application/json"),
            };
        }
    }
}
