using System.Net;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

public sealed class MemorySummaryProviderTests
{
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
            ? JsonSerializer.Serialize(new { answers = new object[]
            {
                new { name = "selected_candidate", type = "choice", choice = "safe_idle", confidence = 1,
                    probabilities = new[] { new { value = "safe_idle", probability = 1 } } },
                new { name = "memory_summary", summaryAnswer.type, summaryAnswer.choice },
            } })
            : JsonSerializer.Serialize(new { answers = new
            {
                selected_candidate = new { type = "choice", choice = "safe_idle", confidence = 1 },
                memory_summary = summaryAnswer,
            } });
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
        Assert.True(result.FellBack);
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
