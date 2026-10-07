using System.Net;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

public sealed class OpenAiDecisionsTests
{
    private const string Reply = """
        {"model":"gpt-6-luna","answers":[
          {"name":"memory_salience_00","type":"score","score":1.8,"confidence":0.75},
          {"name":"selected_candidate","type":"choice","choice":"seek_food","confidence":0.9,
           "probabilities":[{"value":"safe_idle","probability":0.1},{"value":"seek_food","probability":0.9}]}],
         "usage":{"input_tokens":123,"output_tokens":0}}
        """;

    [Fact]
    public async Task PublishedArrayContractBindsMemoryScoresToOwnerSourcesAndAdmitsOnlyLegalChoices()
    {
        var handler = new Handler(Reply);
        using var client = new HttpClient(handler);
        var provider = new OpenAiDecisionsProvider(client, () => "decisions-test-secret");
        var runtime = new CognitionRuntime("agent-test", provider);
        var request = runtime.IssueRequest(Observation());
        var response = await provider.DecideAsync(request);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("https://api.openai.com/v1/decisions", handler.Uri!.AbsoluteUri);
        Assert.Equal("Bearer decisions-test-secret", handler.Authorization);
        var root = payload.RootElement;
        Assert.Equal("gpt-6-luna", root.GetProperty("model").GetString());
        using var input = JsonDocument.Parse(root.GetProperty("input").GetString()!);
        Assert.Equal(5_000, input.RootElement.GetProperty("hunger_basis_points").GetInt32());
        Assert.Equal(0, input.RootElement.GetProperty("memory_compaction_candidates")[0].GetProperty("source_index").GetInt32());
        Assert.DoesNotContain("private-source-id", handler.Body!);
        Assert.DoesNotContain("world_tick", handler.Body!);
        var questions = root.GetProperty("questions");
        Assert.Equal("safe_idle", questions[0].GetProperty("choices")[0].GetProperty("value").GetString());
        Assert.Contains("source_index 0", questions[1].GetProperty("instructions").GetString());
        Assert.Equal(3, questions[1].GetProperty("levels").GetArrayLength());
        var admitted = runtime.ApplyResponse(response);
        Assert.True(admitted.Accepted);
        Assert.False(admitted.FellBack);
        Assert.Equal("seek_food", admitted.Intention!.CandidateId);
        var score = Assert.Single(admitted.MemoryCompactionScores!);
        Assert.Equal("private-source-id", score.Id);
        Assert.Equal("agent-test", score.OwnerId);
        Assert.Equal(9_000, score.ImportanceBasisPoints);
        Assert.Equal(7_500, score.ConfidenceBasisPoints);
        Assert.Equal(123, response.Usage!.InputTokens);
    }

    [Fact]
    public async Task MissingKeyAndCanceledRequestSendNothing()
    {
        var handler = new Handler(Reply);
        using var client = new HttpClient(handler);
        var provider = new OpenAiDecisionsProvider(client, () => null);
        var request = new CognitionDecisionRequest("request-test", provider.ProviderEpoch, Observation());
        await Assert.ThrowsAsync<CognitionProviderUnavailableException>(async () => await provider.DecideAsync(request));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.DecideAsync(request, canceled.Token));
        Assert.Null(handler.Body);
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("duplicate")]
    [InlineData("unoffered")]
    [InlineData("bad_score")]
    public async Task UnusableAnswersCannotAdmitAnActionOrMemoryScore(string failure)
    {
        var body = failure switch
        {
            "refusal" => Reply.Replace("\"type\":\"choice\"", "\"type\":\"refusal\""),
            "duplicate" => Reply.Replace("\"name\":\"memory_salience_00\"", "\"name\":\"selected_candidate\""),
            "unoffered" => Reply.Replace("\"choice\":\"seek_food\"", "\"choice\":\"invented_action\""),
            _ => Reply.Replace("\"score\":1.8", "\"score\":3"),
        };
        using var client = new HttpClient(new Handler(body));
        var provider = new OpenAiDecisionsProvider(client, () => "test-key");
        var runtime = new CognitionRuntime("agent-test", provider);
        var request = runtime.IssueRequest(Observation());
        if (failure == "unoffered")
        {
            var result = runtime.ApplyResponse(await provider.DecideAsync(request));
            Assert.True(result.FellBack);
            Assert.Equal("safe_idle", result.Intention!.CandidateId);
            Assert.Null(result.MemoryCompactionScores);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await provider.DecideAsync(request));
            Assert.Null(runtime.Capture().CurrentIntention);
        }
    }

    private static InhabitantObservation Observation() => new("agent-test", 4, 0, 2, "sha256:test", 5_000,
        [new("safe_idle", "Wait safely.", 0), new("seek_food", "Travel to food.", 10)],
        MemoryCompactionCandidates: [new("private-source-id", "agent-test", "experience", "friend", "A remembered promise.", 1)]);

    private sealed class Handler(string reply) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            return new(HttpStatusCode.OK) { Content = new StringContent(reply) };
        }
    }
}
