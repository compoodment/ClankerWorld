using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class AgentIdentityChoiceTests
{
    private const string AgentId = "founder:00000000000000000000000000000001";

    [Fact]
    public async Task FirstPersonalReplyChoosesIdentityInANormalWorldAndRestoredRequestsKeepIt()
    {
        var handler = new IdentityReplyHandler();
        using var client = new HttpClient(handler);
        var model = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model");
        using var world = NormalPathWorld.CreateGenerated("identity-choice", id => id == AgentId ? model : new DeterministicDecisionProvider());
        await AdvanceUntilDecision(world);
        var physical = world.Inhabitants.Single(item => item.InhabitantId == AgentId);
        Assert.Equal("Patient and curious", physical.Personality);
        Assert.Equal("Explore the riverbanks", physical.Aspiration);
        Assert.False(physical.IdentityChoicePending);
        var profile = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(item => item.Id == AgentId);
        Assert.Contains(profile.DecisionFactors, item => item.Key == "personality" && item.Detail == physical.Personality);
        Assert.Contains(profile.DecisionFactors, item => item.Key == "aspiration" && item.Detail == physical.Aspiration);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "agent_identity_chosen" && item.Detail == AgentId);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Detail.Contains(physical.Personality, StringComparison.Ordinal));

        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(saved, id => id == AgentId ? model : new DeterministicDecisionProvider());
        Assert.Equal(physical.Personality, restored.Inhabitants.Single(item => item.InhabitantId == AgentId).Personality);
        restored.Resume();
        for (var tick = 0; tick < 310 && handler.Bodies.Count < 2; tick++)
            await restored.AdvanceOneTickNonBlockingAsync();
        Assert.True(handler.Bodies.Count >= 2);
        using var payload = JsonDocument.Parse(handler.Bodies[1]);
        using var question = JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal(physical.Personality, question.RootElement.GetProperty("self").GetProperty("personality").GetString());
        Assert.Equal(physical.Aspiration, question.RootElement.GetProperty("self").GetProperty("aspiration").GetString());
        Assert.False(question.RootElement.GetProperty("needs_personality").GetBoolean());
        Assert.False(question.RootElement.GetProperty("needs_aspiration").GetBoolean());
        await AdvanceUntilDecision(restored);
        Assert.Equal(physical.Personality, restored.Inhabitants.Single(item => item.InhabitantId == AgentId).Personality);
        Assert.Equal(physical.Aspiration, restored.Inhabitants.Single(item => item.InhabitantId == AgentId).Aspiration);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "agent_identity_chosen");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrInvalidIdentityKeepsPlaceholdersWithoutAnExtraRequest(bool invalid)
    {
        var handler = new IdentityReplyHandler(invalid ? "bad\ntext" : null, invalid ? new string('x', 257) : null);
        using var client = new HttpClient(handler);
        var model = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model");
        using var world = NormalPathWorld.CreateGenerated("identity-missing", id => id == AgentId ? model : new DeterministicDecisionProvider());
        await AdvanceUntilDecision(world);
        for (var tick = 0; tick < 20; tick++) await world.AdvanceOneTickNonBlockingAsync();
        Assert.Single(handler.Bodies, body =>
        {
            using var payload = JsonDocument.Parse(body);
            using var question = JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            return question.RootElement.GetProperty("needs_personality").GetBoolean();
        });
        world.Pause();
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        var physical = restored.Inhabitants.Single(item => item.InhabitantId == AgentId);
        Assert.Equal("undecided", physical.Personality);
        Assert.Equal("find a purpose", physical.Aspiration);
        Assert.False(physical.IdentityChoicePending);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "agent_identity_kept" && item.Detail == AgentId);
    }

    [Fact]
    public async Task PausingDiscardsLateIdentityAndSavesTheUnconsumedOpportunity()
    {
        var model = new HeldIdentityProvider();
        using var world = NormalPathWorld.CreateGenerated("identity-pause", id => id == AgentId ? model : new DeterministicDecisionProvider());
        await world.AdvanceOneTickNonBlockingAsync();
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        model.Release.TrySetResult(true);
        await model.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var restored = PrivateWorldRuntime.Restore(saved);
        Assert.True(restored.Inhabitants.Single(item => item.InhabitantId == AgentId).IdentityChoicePending);
        Assert.Equal("undecided", world.Inhabitants.Single(item => item.InhabitantId == AgentId).Personality);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind.StartsWith("agent_identity_", StringComparison.Ordinal));
    }

    private static async Task AdvanceUntilDecision(PrivateWorldRuntime world)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var step = await world.AdvanceOneTickNonBlockingAsync();
            if (step.Decisions.Any(item => item.InhabitantId == AgentId && item.Admission.Accepted)) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("The synthetic personal reply was not admitted.");
    }

    private sealed class IdentityReplyHandler(string? personality = "Patient and curious", string? aspiration = "Explore the riverbanks") : HttpMessageHandler
    {
        private readonly List<string> bodies = [];

        // Requests arrive on background tasks while the test reads, so it reads a copy.
        public IReadOnlyList<string> Bodies
        {
            get
            {
                lock (bodies) return [.. bodies];
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            bool first;
            lock (bodies)
            {
                bodies.Add(body);
                first = bodies.Count == 1;
            }
            var answer = JsonSerializer.Serialize(new
            {
                selected_candidate_id = "safe_idle",
                confidence = 1d,
                chosen_name = "Aster Vale",
                chosen_personality = first ? personality : "Overwrite attempt",
                chosen_aspiration = first ? aspiration : "Overwrite attempt"
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = answer } } } }), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class HeldIdentityProvider : IDecisionProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Release.Task;
            Returned.TrySetResult(true);
            return new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1d, new Dictionary<string, double> { ["safe_idle"] = 1d },
                ChosenPersonality: "Late identity", ChosenAspiration: "Late aspiration");
        }
    }
}
