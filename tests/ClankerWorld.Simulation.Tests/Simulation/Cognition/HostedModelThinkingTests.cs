using System.Net;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

public sealed class HostedModelThinkingTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("default", null)]
    [InlineData(" Low ", "low")]
    [InlineData("medium", "medium")]
    [InlineData("HIGH", "high")]
    public void ThinkingNamesTheModelDefaultOrOneOfThreeLevels(string? value, string? expected) =>
        Assert.Equal(expected, ModelThinking.Normalize(value));

    [Theory]
    [InlineData("none")]
    [InlineData("max")]
    [InlineData("minimal")]
    public void ThinkingRefusesLevelsSomeProvidersReject(string value) =>
        Assert.Throws<ArgumentException>(() => ModelThinking.Normalize(value));

    [Fact]
    public async Task ChatCompletionsSendReasoningEffortOnlyWhenTheAgentChoseALevel()
    {
        var handler = new ChatHandler();
        using var http = new HttpClient(handler);
        var defaultProvider = new OpenAiCompatibleDecisionProvider(
            http, () => "test-key", new Uri("https://api.openai.com/v1/chat/completions"), "gpt-6-luna");
        var lowProvider = new OpenAiCompatibleDecisionProvider(
            http, () => "test-key", new Uri("https://api.openai.com/v1/chat/completions"), "gpt-6-luna", thinking: "low");

        _ = await defaultProvider.DecideAsync(Request());
        _ = await lowProvider.DecideAsync(Request());

        using var first = JsonDocument.Parse(handler.Bodies[0]);
        using var second = JsonDocument.Parse(handler.Bodies[1]);
        Assert.False(first.RootElement.TryGetProperty("reasoning_effort", out _));
        Assert.Equal("low", second.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal("json_object", second.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(2, second.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task AHostSuppliedClientGetsTheSamePromptsAndItsReplyIsValidatedTheSameWay()
    {
        var client = new FakeModelClient(
            new HostedModelReply("{\"selected_candidate_id\":\"safe_idle\",\"confidence\":0.75}", "claude-haiku-5-5", 120, 30));
        var provider = new OpenAiCompatibleDecisionProvider(client, "claude-haiku-5-5", "high", TimeSpan.FromSeconds(20));

        var response = await provider.DecideAsync(Request());

        var call = Assert.Single(client.Calls);
        Assert.Equal("claude-haiku-5-5", call.Model);
        Assert.Equal("high", call.Thinking);
        Assert.Equal(TimeSpan.FromSeconds(20), call.Timeout);
        Assert.Contains("selected_candidate_id", call.Instructions, StringComparison.Ordinal);
        using (var input = JsonDocument.Parse(call.Input))
            Assert.Equal("agent-thinking", input.RootElement.GetProperty("agent_id").GetString());
        Assert.Equal("safe_idle", response.SelectedCandidateId);
        Assert.Equal(DecisionProviderKind.LargeLanguageModel, response.Provider);
        Assert.Equal(new CognitionUsage("claude-haiku-5-5", 120, 30), response.Usage);

        var unoffered = new OpenAiCompatibleDecisionProvider(
            new FakeModelClient(new HostedModelReply("{\"confidence\":0.5}", null, 1, 1)), "claude-haiku-5-5");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await unoffered.DecideAsync(Request()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedChatAnswersKeepOnlyReportedUsage(bool reportsUsage)
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse(
            """{"model":"gpt-6-luna","choices":[{"message":{"content":"not-json"}}],"usage":{"prompt_tokens":120,"completion_tokens":30}}""")!.AsObject();
        if (!reportsUsage) body.Remove("usage");
        var handler = new ChatHandler(body.ToJsonString());
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(http, () => "test-key",
            new Uri("https://api.openai.com/v1/chat/completions"), "gpt-6-luna");
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => provider.DecideAsync(Request()).AsTask());
        Assert.True(HostedModelUnusableReply.TryGetTokens(failure, out var input, out var output));
        Assert.Equal(reportsUsage ? (120, 30) : (0, 0), (input, output));
        Assert.Single(handler.Bodies);
    }

    private static CognitionDecisionRequest Request() => new(
        "request-thinking",
        2,
        new InhabitantObservation(
            "agent-thinking",
            12,
            0,
            3,
            "sha256:thinking-test",
            5_000,
            [new CognitionCandidate("safe_idle", "Wait safely.", 100)]));

    private sealed class FakeModelClient(HostedModelReply reply) : IHostedModelClient
    {
        public List<HostedModelCall> Calls { get; } = [];

        public Task<HostedModelReply> CompleteAsync(HostedModelCall request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return Task.FromResult(reply);
        }
    }

    private sealed class ChatHandler(string? reply = null) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    reply ?? """{"model":"gpt-6-luna","choices":[{"message":{"content":"{\"selected_candidate_id\":\"safe_idle\",\"confidence\":1.0}"}}]}"""),
            };
        }
    }
}
