using System.Net;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Tests;

public sealed class CognitionTests
{
    [Fact]
    public async Task DeterministicCognitionChoosesAndExecutesARealMovementIntention()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");

        var result = await runtime.AdvanceOneActionAsync();
        var snapshot = runtime.Capture().Snapshot;

        Assert.True(result.Advanced);
        Assert.False(result.PausedForProviderOutage);
        Assert.False(result.Cognition?.FellBack);
        Assert.Equal("seek_food", result.CandidateId);
        Assert.Equal("seek_food", snapshot.Cognition?.CurrentIntention?.CandidateId);
        Assert.NotEqual(new GridPoint(0, 0), snapshot.World.Actor.Position);
        Assert.Contains(snapshot.Cognition!.Events, worldEvent => worldEvent.Kind == "cognition_requested");
        Assert.Contains(snapshot.Cognition.Events, worldEvent => worldEvent.Kind == "cognition_decision_applied");
        Assert.Equal("moved", result.MovementEvents.Single().Kind);
    }

    [Fact]
    public async Task CognitionAndWorldStateSurviveAValidatedRestart()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha");
        _ = await runtime.AdvanceOneActionAsync();
        var state = JsonSerializer.Deserialize<OwnerWorldRuntimeState>(
            JsonSerializer.Serialize(runtime.ExportState())) ?? throw new InvalidDataException();

        var restored = OwnerWorldRuntime.Restore(state, "camp-alpha");

        Assert.Equal(
            runtime.Capture().Snapshot.Cognition?.CurrentIntention,
            restored.Capture().Snapshot.Cognition?.CurrentIntention);
        Assert.Equal(
            runtime.Capture().Snapshot.World.Actor,
            restored.Capture().Snapshot.World.Actor);
        Assert.Equal(
            runtime.Capture().Snapshot.World.Events,
            restored.Capture().Snapshot.World.Events);
        Assert.Equal(
            runtime.Capture().Snapshot.World.Map.ManifestDigest,
            restored.Capture().Snapshot.World.Map.ManifestDigest);
        Assert.True((await restored.AdvanceOneActionAsync()).Advanced);
        Assert.Equal(2, restored.Capture().Snapshot.World.Identity.WorldTick);
    }

    [Fact]
    public async Task RepeatedHostedProviderFailureKeepsTheWorldRunningSafely()
    {
        var runtime = new OwnerWorldRuntime("camp-alpha", decisionProvider: new ThrowingProvider());

        var first = await runtime.AdvanceOneActionAsync();
        var second = await runtime.AdvanceOneActionAsync();
        var third = await runtime.AdvanceOneActionAsync();

        Assert.True(first.Advanced);
        Assert.True(first.Cognition?.FellBack);
        Assert.True(second.Advanced);
        Assert.True(third.Advanced);
        Assert.Equal("safe_idle", first.CandidateId);
        Assert.False(third.PausedForProviderOutage);
        Assert.False(runtime.Capture().Snapshot.IsPaused);
        Assert.Equal(3, runtime.Capture().Snapshot.World.Identity.WorldTick);
        Assert.True((await runtime.AdvanceOneActionAsync()).Advanced);
    }

    [Fact]
    public async Task JevAdapterSendsOnlyTheCompactChoiceContractAndRecordsUsage()
    {
        var handler = new RecordingHandler(JsonResponse());
        using var client = new HttpClient(handler);
        var provider = new JevDecisionProvider(
            client,
            () => "test-secret",
            new Uri("https://typesafe.test/v1/systemone"));
        var observation = new InhabitantObservation(
            "actor-scout",
            4,
            0,
            2,
            "sha256:observation-4",
            2_000,
            [
                new CognitionCandidate("safe_idle", "Continue safely.", 0),
                new CognitionCandidate("seek_food", "Travel to food.", 10, "berry-patch"),
            ], RetrievedMemories: [new CognitionMemoryExcerpt("m1", "actor-scout", "friend",
                "Private campfire promise.", 3)]);
        var request = new CognitionDecisionRequest("cognition-test", 1, observation);

        var response = await provider.DecideAsync(request);
        using var body = JsonDocument.Parse(handler.Body ?? throw new InvalidDataException());

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("Bearer test-secret", handler.Authorization);
        Assert.DoesNotContain("energy_basis_points", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Private campfire promise", handler.Body, StringComparison.Ordinal);
        Assert.Equal("jev-1.13.0", body.RootElement.GetProperty("model").GetString());
        var question = body.RootElement.GetProperty("questions").GetProperty("selected_candidate");
        Assert.Equal("choice", question.GetProperty("type").GetString());
        Assert.Null(RetiredWording.Find(question.GetProperty("instructions").GetString()!));
        var jevState = body.RootElement.GetProperty("state");
        Assert.Equal("actor-scout", jevState.GetProperty("agent_id").GetString());
        Assert.False(jevState.TryGetProperty("inhabitant_id", out _));
        Assert.Equal("seek_food", response.SelectedCandidateId);
        Assert.Equal(0.84, response.Confidence);
        Assert.Equal("jev-1.13.0", response.Usage?.ModelId);
        Assert.Equal(123, response.Usage?.InputTokens);
        Assert.Equal(7, response.Usage?.OutputTokens);
    }

    [Fact]
    public async Task JevCompactionScoresAreStructuredBoundedAndKeepSourceIdsHostLocal()
    {
        var handler = new RecordingHandler(
            """
            {
              "model": "jev-1.13.0",
              "answers": {
                "selected_candidate": {
                  "type": "choice", "choice": "safe_idle",
                  "probabilities": { "safe_idle": 1 }, "confidence": 1
                },
                "memory_salience_00": {
                  "type": "score", "score": 1.8, "confidence": 0.75,
                  "legend": { "0": "minor", "1": "useful", "2": "important" },
                  "probabilities": { "0": 0.05, "1": 0.1, "2": 0.85 }
                }
              },
              "usage": { "input_tokens": 221, "output_tokens": 14 }
            }
            """);
        using var client = new HttpClient(handler);
        var provider = new JevDecisionProvider(client, () => "test-secret",
            new Uri("https://typesafe.test/v1/systemone"));
        var observation = new InhabitantObservation(
            "actor-scout", 4, 0, 1, "sha256:memory-score", 2_000,
            [new CognitionCandidate("safe_idle", "Continue safely.")],
            MemoryCompactionCandidates: [new CognitionMemoryCompactionCandidate(
                "belief:hidden-store", "actor-scout", "belief", "actor-mira",
                "Mira said a hidden store is beneath the old oak.", 3,
                Provenance: "hearsay", ConfidenceBasisPoints: 4_200,
                SourceAgentId: "actor-mira", SourceEventId: 12)]);
        var response = await provider.DecideAsync(new CognitionDecisionRequest("cognition-memory", 1, observation));
        using var body = JsonDocument.Parse(handler.Body ?? throw new InvalidDataException());

        var question = body.RootElement.GetProperty("questions").GetProperty("memory_salience_00");
        Assert.Equal("score", question.GetProperty("type").GetString());
        Assert.Equal(3, question.GetProperty("criteria").GetArrayLength());
        var memory = body.RootElement.GetProperty("state").GetProperty("memory_compaction_candidates")[0];
        Assert.Equal("belief", memory.GetProperty("kind").GetString());
        Assert.Equal("hearsay", memory.GetProperty("provenance").GetString());
        Assert.Equal(4_200, memory.GetProperty("confidence_basis_points").GetInt32());
        Assert.Equal("Mira said a hidden store is beneath the old oak.", memory.GetProperty("summary").GetString());
        Assert.DoesNotContain("belief:hidden-store", handler.Body, StringComparison.Ordinal);
        var score = Assert.Single(response.MemoryCompactionScores!);
        Assert.Equal("belief:hidden-store", score.Id);
        Assert.Equal("actor-scout", score.OwnerId);
        Assert.Equal(9_000, score.ImportanceBasisPoints);
        Assert.Equal(7_500, score.ConfidenceBasisPoints);
        Assert.Equal(221, response.Usage?.InputTokens);
    }

    [Fact]
    public async Task OpenAiCompatibleAdapterSupportsStructuredChoiceAndUsage()
    {
        var handler = new RecordingHandler(OpenAiCompatibleJsonResponse());
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(
            client,
            () => "openai-test-secret",
            new Uri("https://model.test/v1/chat/completions"),
            "test-model");
        var observation = new InhabitantObservation(
            "actor-scout",
            9,
            1,
            3,
            "sha256:observation-9",
            3_000,
            [
                new CognitionCandidate("safe_idle", "Continue safely.", 0),
                new CognitionCandidate("seek_food", "Travel to food.", 10, "berry-patch"),
            ], NeedsName: true, RetrievedMemories: [new CognitionMemoryExcerpt(
                "m1", "actor-scout", "friend", "Mira told me about the private campfire promise.", 8,
                Kind: "belief", Provenance: "hearsay", ConfidenceBasisPoints: 4_200,
                SourceAgentId: "friend", SourceEventId: 9, IsCorrected: true,
                ImportanceBasisPoints: 7_500, ImportanceConfidenceBasisPoints: 8_200)],
            Self: new CognitionSelfContext("actor-scout", "Aster Vale", "Adult", "Curious", "Explore",
                "household:one", 4_000, 1_000, "I remember the path."));
        var request = new CognitionDecisionRequest("cognition-openai-test", 2, observation);

        var response = await provider.DecideAsync(request);
        using var body = JsonDocument.Parse(handler.Body ?? throw new InvalidDataException());

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("Bearer openai-test-secret", handler.Authorization);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("seek_food", response.SelectedCandidateId);
        Assert.Equal(0.91, response.Confidence);
        Assert.Equal("I should find food before dark.", response.PrivateThought);
        Assert.Equal("Aster Vale", response.ChosenName);
        var systemPrompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Contains("private_thought", systemPrompt);
        Assert.Contains("full name", systemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("given name", systemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("family/surname", systemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("middle name is optional", systemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not authoritative current facts", systemPrompt, StringComparison.OrdinalIgnoreCase);
        using var question = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.False(question.RootElement.TryGetProperty("energy_basis_points", out _));
        Assert.Equal("actor-scout", question.RootElement.GetProperty("agent_id").GetString());
        foreach (var internalField in new[] { "inhabitant_id", "world_tick", "run_epoch", "decision_generation" })
        {
            Assert.False(question.RootElement.TryGetProperty(internalField, out _), internalField);
        }

        Assert.Null(RetiredWording.Find(systemPrompt!));
        Assert.Null(RetiredWording.Find(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!));
        Assert.True(question.RootElement.GetProperty("needs_name").GetBoolean());
        var self = question.RootElement.GetProperty("self");
        Assert.Equal("Aster Vale", self.GetProperty("name").GetString());
        Assert.Equal(4_000, self.GetProperty("warmth_basis_points").GetInt32());
        Assert.Equal("I remember the path.", self.GetProperty("recent_thought").GetString());
        Assert.Contains("0 is starving", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("Null condition fields mean unknown", systemPrompt, StringComparison.Ordinal);
        var memory = Assert.Single(question.RootElement.GetProperty("retrieved_memories").EnumerateArray());
        Assert.Equal("Mira told me about the private campfire promise.", memory.GetProperty("summary").GetString());
        Assert.Equal("friend", memory.GetProperty("subject_id").GetString());
        Assert.Equal("belief", memory.GetProperty("kind").GetString());
        Assert.Equal("hearsay", memory.GetProperty("provenance").GetString());
        Assert.Equal(4_200, memory.GetProperty("confidence_basis_points").GetInt32());
        Assert.Equal(9, memory.GetProperty("source_event_id").GetInt64());
        Assert.True(memory.GetProperty("is_corrected").GetBoolean());
        Assert.Equal(7_500, memory.GetProperty("jev_importance_basis_points").GetInt32());
        Assert.Equal("test-model", response.Usage?.ModelId);
        Assert.Equal(44, response.Usage?.InputTokens);
        Assert.Equal(9, response.Usage?.OutputTokens);
    }

    [Fact]
    public async Task NamingHintVariesBetweenAgentsButStaysStableForRetries()
    {
        var handler = new RecordingHandler(OpenAiCompatibleJsonResponse());
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(
            client, () => "synthetic-test-key", new Uri("https://model.test/v1/chat/completions"), "test-model");

        static CognitionDecisionRequest Request(string actor, bool needsName, string requestId) =>
            new(requestId, 2, new InhabitantObservation(actor, 9, 1, 3, "sha256:test-observation", 9_000,
                [new CognitionCandidate("safe_idle", "Continue safely.")], NeedsName: needsName));

        async Task<string> SystemPrompt(CognitionDecisionRequest request)
        {
            _ = await provider.DecideAsync(request);
            using var payload = JsonDocument.Parse(handler.Body ?? throw new InvalidDataException());
            return payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        }

        var first = await SystemPrompt(Request("actor-alpha", true, "first"));
        var retry = await SystemPrompt(Request("actor-alpha", true, "retry"));
        var other = await SystemPrompt(Request("actor-gamma", true, "other"));
        var named = await SystemPrompt(Request("actor-alpha", false, "named"));

        Assert.Equal(first, retry);
        Assert.NotEqual(first, other);
        Assert.Contains("given name starting with", first, StringComparison.Ordinal);
        Assert.DoesNotContain("given name starting with", named, StringComparison.Ordinal);
    }

    [Fact]
    public void CognitionSelfContextRejectsAnotherOwnerAndOversizedThoughts()
    {
        var observation = new InhabitantObservation("actor", 1, 0, 1, "digest", 5_000,
            [new CognitionCandidate("safe_idle", "Wait")],
            Self: new CognitionSelfContext("other", "Aster", "Adult", "Curious", "Explore", null, null, null, null));
        Assert.Throws<ArgumentException>(observation.Validate);
        observation = observation with { Self = observation.Self! with { OwnerId = "actor", RecentThought = new string('x', 161) } };
        Assert.Throws<ArgumentException>(observation.Validate);
        (observation with { Self = observation.Self with { RecentThought = null } }).Validate();
    }

    [Fact]
    public void CognitionObservationRejectsAnotherActorsMemory()
    {
        var observation = new InhabitantObservation(
            "actor-scout", 9, 1, 3, "sha256:observation-9", 3_000,
            [new CognitionCandidate("safe_idle", "Continue safely.")],
            RetrievedMemories: [new CognitionMemoryExcerpt(
                "secret", "actor-mira", "actor-scout", "Mira's own secret.", 8)]);
        Assert.Throws<ArgumentException>(observation.Validate);
    }

    [Fact]
    public void CognitionAdmissionRejectsJevCompactionScoresForAnotherOwner()
    {
        var provider = new ThrowingProvider();
        var runtime = new CognitionRuntime("actor-scout", provider, minimumConfidence: 0);
        var observation = new InhabitantObservation(
            "actor-scout", 9, 0, 1, "sha256:owner-check", 2_000,
            [new CognitionCandidate("safe_idle", "Continue safely.")],
            MemoryCompactionCandidates: [new CognitionMemoryCompactionCandidate(
                "own-memory", "actor-scout", "experience", "friend", "A private promise.", 8)]);
        var request = runtime.IssueRequest(observation);
        var response = new CognitionDecisionResponse(
            request.RequestId, "actor-scout", DecisionProviderKind.Jev, provider.ProviderEpoch,
            observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
            "safe_idle", 1, new Dictionary<string, double> { ["safe_idle"] = 1 },
            MemoryCompactionScores: [new CognitionMemoryCompactionScore(
                "own-memory", "actor-mira", "experience", 8, 9_000, 9_000)]);

        var result = runtime.ApplyResponse(response);

        Assert.False(result.Accepted);
        Assert.Equal("memory_compaction_source", result.Outcome);
        Assert.Null(result.MemoryCompactionScores);
    }

    private static string JsonResponse() =>
        """
        {
          "model": "jev-1.13.0",
          "answers": {
            "selected_candidate": {
              "type": "choice",
              "choice": "seek_food",
              "probabilities": { "safe_idle": 0.16, "seek_food": 0.84 },
              "confidence": 0.84
            }
          },
          "usage": { "input_tokens": 123, "output_tokens": 7 }
        }
        """;

    private static string OpenAiCompatibleJsonResponse() =>
        """
        {
          "model": "test-model",
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "{\"selected_candidate_id\":\"seek_food\",\"confidence\":0.91,\"probabilities\":{\"safe_idle\":0.09,\"seek_food\":0.91},\"private_thought\":\"I should find food before dark.\",\"chosen_name\":\"Aster Vale\"}"
              }
            }
          ],
          "usage": { "prompt_tokens": 44, "completion_tokens": 9 }
        }
        """;

    [Fact]
    public void PrivateThoughtsAreBriefSingleLineTextNotRawReasoning()
    {
        Assert.Null(CognitionDecisionResponse.NormalizePrivateThought("   "));
        Assert.Null(CognitionDecisionResponse.NormalizePrivateThought("line one\nline two"));
        Assert.Null(CognitionDecisionResponse.NormalizePrivateThought(new string('a', 161)));
        Assert.Equal("I need warmth.", CognitionDecisionResponse.NormalizePrivateThought(" I need warmth. "));
    }

    private sealed class ThrowingProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Jev;

        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("provider unavailable");
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public string? Authorization { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            };
        }
    }
}
