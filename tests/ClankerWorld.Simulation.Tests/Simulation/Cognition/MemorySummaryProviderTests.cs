using System.Net;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

public sealed class MemorySummaryProviderTests
{
    [Fact]
    public async Task PersonalModelReceivesExactSummarySourceAttributionInItsActualRequest()
    {
        var handler = new Handler("""
            {"choices":[{"message":{"content":"{\"selected_candidate_id\":\"safe_idle\",\"confidence\":1}"}}]}
            """);
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "test-key", new("https://model.test/v1/chat/completions"), "test-model");
        var sources = Enumerable.Range(0, 4).Select(index => new CognitionMemorySummarySource("source-" + index, "belief", 1,
            "friend", "hearsay", 4_200, "speaker", 2, index == 0)).ToArray();
        var observation = new InhabitantObservation("agent", 8, 0, 1, "sha256:summary-wire", 10_000, [new("safe_idle", "Wait safely.", 0)])
        {
            RetrievedMemories = [new("memory-summary:wire", "agent", "agent", "Hearsay: an old private report.", 3,
                Kind: "summary", Visibility: "private", IsCorrected: true, SummarySources: sources)],
        };
        await provider.DecideAsync(new("summary-wire-request", provider.ProviderEpoch, observation));
        using var body = JsonDocument.Parse(handler.Body!);
        using var input = JsonDocument.Parse(body.RootElement.GetProperty("messages").EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "user").GetProperty("content").GetString()!);
        var sent = input.RootElement.GetProperty("retrieved_memories")[0].GetProperty("summary_sources").EnumerateArray().ToArray();
        Assert.Equal(4, sent.Length);
        for (var index = 0; index < sent.Length; index++)
        {
            Assert.Equal(sources[index].Id, sent[index].GetProperty("id").GetString());
            Assert.Equal("belief", sent[index].GetProperty("kind").GetString());
            Assert.Equal(1, sent[index].GetProperty("source_tick").GetInt64());
            Assert.Equal("friend", sent[index].GetProperty("subject_id").GetString());
            Assert.Equal("hearsay", sent[index].GetProperty("provenance").GetString());
            Assert.Equal(4_200, sent[index].GetProperty("confidence_basis_points").GetInt32());
            Assert.Equal("speaker", sent[index].GetProperty("source_agent_id").GetString());
            Assert.Equal(2, sent[index].GetProperty("source_event_id").GetInt64());
            Assert.Equal(index == 0, sent[index].GetProperty("is_corrected").GetBoolean());
        }
    }

    [Theory]
    [InlineData(false, "recent")]
    [InlineData(true, "recent")]
    [InlineData(false, "keep_records")]
    [InlineData(true, "keep_records")]
    [InlineData(false, "refusal")]
    [InlineData(true, "refusal")]
    [InlineData(false, "unknown")]
    [InlineData(true, "unknown")]
    public async Task ExistingChoiceProtocolsBindOptionalSummaryToRequestedOwnSources(bool decisions, string choice)
    {
        var summaryAnswer = choice == "refusal" ? new { type = "refusal", choice = "" } : new { type = "choice", choice };
        var reply = decisions
            ? JsonSerializer.Serialize(new
            {
                answers = new object[]
            {
                new { name = "selected_candidate", type = "choice", choice = "safe_idle", confidence = 1,
                    probabilities = new[] { new { value = "safe_idle", probability = 1 } } },
                new { name = "memory_summary", summaryAnswer.type, summaryAnswer.choice },
            }
            })
            : JsonSerializer.Serialize(new
            {
                answers = new
                {
                    selected_candidate = new { type = "choice", choice = "safe_idle", confidence = 1 },
                    memory_summary = summaryAnswer,
                }
            });
        var handler = new Handler(reply);
        using var client = new HttpClient(handler);
        IDecisionProvider provider = decisions ? new OpenAiDecisionsProvider(client, () => "test-key") : new JevDecisionProvider(client, () => "test-key");
        var sources = Enumerable.Range(0, 4).Select(index => new CognitionMemorySummarySource("source-" + index, "belief", 0,
            "friend", "hearsay", 4_200, "friend", null, false)).ToArray();
        var option = new CognitionMemorySummaryOption("agent", "recent", "Hearsay: the path may be muddy.", sources);
        var observation = new InhabitantObservation("agent", 8, 0, 1, "sha256:summary", 10_000, [new("safe_idle", "Wait safely.", 0)])
        { MemorySummaryOptions = [option] };
        var runtime = new CognitionRuntime("agent", provider);
        var request = runtime.IssueRequest(observation);
        if (choice == "unknown")
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await provider.DecideAsync(request));
            Assert.Null(runtime.Capture().CurrentIntention);
        }
        else
        {
            var response = await provider.DecideAsync(request);
            var admission = runtime.ApplyResponse(response);
            Assert.True(admission.Accepted);
            Assert.False(admission.FellBack);
            Assert.Equal(choice == "recent" ? option : null, admission.MemorySummary);
        }
        Assert.Contains("memory_summary", handler.Body!, StringComparison.Ordinal);
        Assert.Contains("keep_records", handler.Body!, StringComparison.Ordinal);
        Assert.Contains("hearsay", handler.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("other-agent-secret", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public void PersonalProviderCannotAdmitSummaryEvenWhenItCopiesARequestedChoice()
    {
        var provider = new PersonalProvider();
        var runtime = new CognitionRuntime("agent", provider);
        var observation = new InhabitantObservation("agent", 8, 0, 1, "sha256:summary", 10_000, [new("safe_idle", "Wait safely.", 0)]);
        var request = runtime.IssueRequest(observation);
        var result = runtime.ApplyResponse(new(request.RequestId, "agent", provider.Kind, provider.ProviderEpoch, 0, 1,
            observation.ObservationDigest, "safe_idle", 1, new Dictionary<string, double> { ["safe_idle"] = 1 }, MemorySummaryChoice: "recent"));
        Assert.False(result.Accepted);
        Assert.Null(result.MemorySummary);
    }

    private sealed class PersonalProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Handler(string reply) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(reply) };
        }
    }
}
