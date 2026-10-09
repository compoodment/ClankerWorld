using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed class AnthropicProviderTests
{
    private const string Answer = """{"selected_candidate_id":"safe_idle","confidence":0.8}""";

    [Fact]
    public async Task ClientSendsTheGamePromptToMessagesWithACachedSystemPromptAndNoEffortByDefault()
    {
        var handler = new AnthropicHandler(MessageReply("```json\n" + Answer + "\n```", thinkingFirst: true));
        var client = new AnthropicModelClient(new HttpClient(handler), "sk-ant-test-key");

        var reply = await client.CompleteAsync(Call(thinking: null), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://api.anthropic.com/v1/messages"), request.Uri);
        Assert.Equal("sk-ant-test-key", request.ApiKey);
        Assert.Null(request.Authorization);
        Assert.False(string.IsNullOrWhiteSpace(request.AnthropicVersion));
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("claude-haiku-5-5", root.GetProperty("model").GetString());
        Assert.Equal(AnthropicModelClient.MaximumOutputTokens, root.GetProperty("max_tokens").GetInt32());
        var system = Assert.Single(root.GetProperty("system").EnumerateArray());
        Assert.Equal("Return JSON only.", system.GetProperty("text").GetString());
        Assert.Equal("ephemeral", system.GetProperty("cache_control").GetProperty("type").GetString());
        var message = Assert.Single(root.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("{\"agent\":\"a\"}", message.GetProperty("content").GetString());
        Assert.False(root.TryGetProperty("output_config", out _));
        Assert.False(root.TryGetProperty("temperature", out _));

        // The thinking block is dropped and the fence is removed for the game's JSON readers.
        Assert.Equal(Answer, reply.Text);
        Assert.Equal("claude-haiku-5-5", reply.Model);
        Assert.Equal(100 + 7 + 900, reply.InputTokens);
        Assert.Equal(42, reply.OutputTokens);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public async Task ThinkingLevelBecomesClaudesEffort(string thinking)
    {
        var handler = new AnthropicHandler(MessageReply(Answer));
        var client = new AnthropicModelClient(new HttpClient(handler), "sk-ant-test-key");

        _ = await client.CompleteAsync(Call(thinking), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(thinking, body.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    public async Task ARefusedOrCutOffReplyIsUnusable(string stopReason)
    {
        var client = new AnthropicModelClient(
            new HttpClient(new AnthropicHandler(MessageReply(Answer, stopReason: stopReason))), "sk-ant-test-key");

        await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteAsync(Call(null), CancellationToken.None));
    }

    [Fact]
    public async Task AReplyWithOnlyThinkingIsUnusable()
    {
        var client = new AnthropicModelClient(new HttpClient(new AnthropicHandler(
            """{"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"thinking","thinking":"","signature":"sig"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":5,"output_tokens":9}}""")),
            "sk-ant-test-key");

        await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteAsync(Call(null), CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnErrorStatusIsReportedOnceWithItsCodeAndNeverRetried(HttpStatusCode status)
    {
        var handler = new AnthropicHandler(
            """{"type":"error","error":{"type":"invalid_request_error","message":"no"}}""", status);
        var client = new AnthropicModelClient(new HttpClient(handler), "sk-ant-test-key");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(Call(null), CancellationToken.None));

        Assert.Equal(status, failure.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AnAnthropicAgentDecidesThroughClaudeWithItsOwnKeyThinkingAndMeteredUsage()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-anthropic-route-");
        try
        {
            var store = Store(directory);
            var slot = Guid.NewGuid().ToString("N");
            _ = store.Configure(new OwnerProviderConfigurationAction(
                "personal", "anthropic", null, "sk-ant-agent-key", false, "inhabitant-test", slot, "Claude key", Thinking: "high"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var handler = new AnthropicHandler(MessageReply(Answer));
            var logger = new RecordingLogger<ConfigurableDecisionProvider>();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler), logger, usageStore: usage);

            Assert.Equal(DecisionProviderKind.LargeLanguageModel, router.KindFor(Observation(strategic: true)));
            var response = await router.DecideAsync(new CognitionDecisionRequest("request-anthropic", router.ProviderEpoch, Observation(strategic: true)));

            Assert.Equal("safe_idle", response.SelectedCandidateId);
            Assert.Equal(DecisionProviderKind.LargeLanguageModel, response.Provider);
            Assert.Equal("anthropic", response.Usage?.ProviderId);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("api.anthropic.com", request.Uri.Host);
            Assert.Equal("sk-ant-agent-key", request.ApiKey);
            using (var body = JsonDocument.Parse(request.Body))
            {
                Assert.Equal("claude-haiku-5-5", body.RootElement.GetProperty("model").GetString());
                Assert.Equal("high", body.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
            }
            var row = Assert.Single(usage.Capture().Rows);
            Assert.Equal(("anthropic", "claude-haiku-5-5", "planning"), (row.Provider, row.Model, row.Role));
            Assert.Equal(1_007, row.InputTokens);
            Assert.DoesNotContain(logger.Messages, message => message.Contains("sk-ant-agent-key", StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task OpenAiAndOllamaAgentsSendReasoningEffortOnlyWhenTheyHaveAThinkingLevel()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-thinking-route-");
        try
        {
            var store = Store(directory);
            _ = store.Configure(new OwnerProviderConfigurationAction(
                "personal", "openai", "gpt-6-luna", "openai-agent-key", false, "agent-openai",
                Guid.NewGuid().ToString("N"), "OpenAI key", Thinking: "low"));
            _ = store.Configure(new OwnerProviderConfigurationAction(
                "personal", "ollama-cloud", "glm-5.3:cloud", "ollama-agent-key", false, "agent-ollama",
                Guid.NewGuid().ToString("N"), "Ollama key"));
            var handler = new ChatHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler));

            _ = await router.DecideAsync(new CognitionDecisionRequest("request-openai", router.ProviderEpoch, Observation(true, "agent-openai")));
            _ = await router.DecideAsync(new CognitionDecisionRequest("request-ollama", router.ProviderEpoch, Observation(true, "agent-ollama")));

            using var openAi = JsonDocument.Parse(handler.Bodies[0]);
            using var ollama = JsonDocument.Parse(handler.Bodies[1]);
            Assert.Equal("low", openAi.RootElement.GetProperty("reasoning_effort").GetString());
            Assert.False(ollama.RootElement.TryGetProperty("reasoning_effort", out _));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task AnAnthropicAgentSpeaksInConversationsThroughClaude()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-anthropic-conversation-");
        try
        {
            var store = Store(directory);
            _ = store.Configure(new OwnerProviderConfigurationAction(
                "personal", "anthropic", "claude-sonnet-5-5", "sk-ant-speaker-key", false, "agent-a",
                Guid.NewGuid().ToString("N"), "Claude key", Thinking: "medium"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var handler = new AnthropicHandler(MessageReply(
                """{"utterance":"Good to see you by the river.","disposition":"continue","effect":"none"}"""));
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler), usageStore: usage);
            IAgentConversationProvider conversations = router;

            Assert.True(router.CanSpeakAs("agent-a"));
            var turn = await conversations.SpeakAsync(ConversationRequest() with { ExpectedProviderEpoch = conversations.ProviderEpoch });

            Assert.Equal("Good to see you by the river.", turn.Text);
            Assert.Equal(AgentConversationDisposition.Continue, turn.Disposition);
            var request = Assert.Single(handler.Requests);
            Assert.Equal("sk-ant-speaker-key", request.ApiKey);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal("claude-sonnet-5-5", body.RootElement.GetProperty("model").GetString());
            Assert.Equal("medium", body.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
            Assert.Contains("Speak as one agent", body.RootElement.GetProperty("system")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.Equal("conversation", Assert.Single(usage.Capture().Rows).Role);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void ThinkingIsSavedWithTheAgentsModelAndOnlyForHostedAgents()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-thinking-store-");
        try
        {
            var store = Store(directory);
            var status = store.Configure(new OwnerProviderConfigurationAction(
                "personal", "anthropic", "claude-opus-5-5", "sk-ant-saved-key", false, "agent-a",
                Guid.NewGuid().ToString("N"), "Claude key", Thinking: "High"));
            Assert.All(status.Assignments!.Where(item => item.InhabitantId == "agent-a"),
                item => Assert.Equal(("anthropic", "claude-opus-5-5", "high"), (item.Provider, item.Model, item.Thinking)));

            var reloaded = new ProviderConfigurationStore(store.Path, EmptySeed()).CaptureStatus();
            Assert.Equal(2, reloaded.Assignments!.Count(item => item.InhabitantId == "agent-a" && item.Thinking == "high"));
            Assert.Contains(reloaded.Providers, option => option.Provider == "anthropic" && option.Model == "claude-haiku-5-5");

            Assert.Throws<ArgumentException>(() => store.Configure(new OwnerProviderConfigurationAction(
                "personal", "deterministic", null, null, false, "agent-b", Thinking: "low")));
            Assert.Throws<ArgumentException>(() => store.Configure(new OwnerProviderConfigurationAction(
                "personal", "inherit", null, null, false, "agent-b", Thinking: "low")));
            Assert.Throws<ArgumentException>(() => store.Configure(new OwnerProviderConfigurationAction(
                "planning", "openai", "gpt-6-luna", "openai-world-key", false, Thinking: "low")));
            Assert.Throws<ArgumentException>(() => store.Configure(new OwnerProviderConfigurationAction(
                "personal", "anthropic", null, "sk-ant-saved-key", false, "agent-b",
                Guid.NewGuid().ToString("N"), "Other key", Thinking: "max")));

            // Leaving the model default writes no thinking field at all.
            _ = store.Configure(new OwnerProviderConfigurationAction(
                "personal", "anthropic", null, "sk-ant-saved-key", false, "agent-c", Guid.NewGuid().ToString("N"), "Third key"));
            Assert.DoesNotContain("\"Thinking\":null", ProviderCredentialFile.Decode(File.ReadAllText(store.Path)), StringComparison.Ordinal);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void AProviderFileFromBeforeAnthropicLoadsWithAnEmptyAnthropicRecord()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-anthropic-upgrade-");
        try
        {
            var path = Path.Combine(directory.FullName, "providers.json");
            File.WriteAllText(path, ProviderCredentialFile.Encode(
                """{"SchemaVersion":3,"Revision":4,"RoutineProvider":"deterministic","PlanningProvider":"openai","Jev":{"Model":"jev-1.13.0","ApiKey":null},"OpenAi":{"Model":"gpt-6-luna","ApiKey":"openai-saved-key"},"OllamaCloud":{"Model":"glm-5.3-flash:cloud","ApiKey":null},"Assignments":[{"InhabitantId":"agent-a","Role":"planning","Provider":"openai","Model":"gpt-6-sol","CredentialSlotId":null,"SelectionReason":null}],"CredentialSlots":[]}"""));

            var status = new ProviderConfigurationStore(path, EmptySeed()).CaptureStatus();

            Assert.Equal("openai", status.PlanningProvider);
            var anthropic = Assert.Single(status.Providers, option => option.Provider == "anthropic");
            Assert.False(anthropic.HasCredential);
            Assert.Null(Assert.Single(status.Assignments!).Thinking);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task TheModelListChecksAnAnthropicKeyWithItsOwnHeader()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-anthropic-models-");
        try
        {
            var store = Store(directory);
            var handler = new AnthropicHandler(
                """{"data":[{"type":"model","id":"claude-haiku-5-5","display_name":"Claude Haiku 5.5","created_at":"2026-09-01T00:00:00Z"},{"type":"model","id":"claude-sonnet-5-5","display_name":"Claude Sonnet 5.5","created_at":"2026-09-01T00:00:00Z"}],"has_more":false,"first_id":"claude-haiku-5-5","last_id":"claude-sonnet-5-5"}""");
            var catalog = new ProviderModelCatalog(store, new FixedHttpClientFactory(handler));

            var list = await catalog.ListAsync(new OwnerProviderModelListAction("anthropic", ApiKey: "sk-ant-pasted-key"), CancellationToken.None);

            Assert.Null(list.Error);
            Assert.Equal("claude-haiku-5-5", list.DefaultModel);
            Assert.Equal(["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"], list.Models.Select(model => model.Model));
            Assert.Equal([false, true, true], list.Models.Select(model => model.Available));
            var request = Assert.Single(handler.Requests);
            Assert.Equal("/v1/models", request.Uri.AbsolutePath);
            Assert.Equal("sk-ant-pasted-key", request.ApiKey);

            var refused = new ProviderModelCatalog(store, new FixedHttpClientFactory(new AnthropicHandler(
                """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", HttpStatusCode.Unauthorized)));
            var refusedList = await refused.ListAsync(new OwnerProviderModelListAction("anthropic", ApiKey: "sk-ant-wrong-key"), CancellationToken.None);
            Assert.Equal("Anthropic refused this key.", refusedList.Error);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task TestModelChecksAClaudeModelAtTheChosenThinkingLevel()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-anthropic-setup-check-");
        try
        {
            var store = Store(directory);
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var handler = new AnthropicHandler(MessageReply(Answer));
            var checks = new ProviderSetupCheckService(store, usage, new FixedHttpClientFactory(handler));

            var result = await checks.CheckAsync(
                new OwnerProviderSetupCheckAction("anthropic", "claude-haiku-5-5", ApiKey: "sk-ant-pasted-key", Thinking: "low"),
                CancellationToken.None);

            Assert.True(result.IsReady, result.Message);
            using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
            Assert.Equal("low", body.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
            Assert.Equal("setup", Assert.Single(usage.Capture().Rows).Role);

            var rejecting = new ProviderSetupCheckService(store, usage, new FixedHttpClientFactory(new AnthropicHandler(
                """{"type":"error","error":{"type":"invalid_request_error","message":"effort is not supported"}}""", HttpStatusCode.BadRequest)));
            var rejected = await rejecting.CheckAsync(
                new OwnerProviderSetupCheckAction("anthropic", "claude-haiku-4-5", ApiKey: "sk-ant-pasted-key", Thinking: "low"),
                CancellationToken.None);
            Assert.False(rejected.IsReady);
            Assert.Contains("Thinking to Model default", rejected.Message, StringComparison.Ordinal);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void SignedPayloadsAddAThinkingLineOnlyWhenALevelIsChosen()
    {
        var plain = new OwnerProviderConfigurationAction("personal", "anthropic", "claude-haiku-5-5", null, false, "agent-a");
        var withThinking = plain with { Thinking = "low" };

        Assert.EndsWith("inhabitant=" + Base64Url("agent-a"), OwnerHttpBinding.ProviderConfigurationPayload(plain), StringComparison.Ordinal);
        Assert.EndsWith("\nthinking=" + Base64Url("low"), OwnerHttpBinding.ProviderConfigurationPayload(withThinking), StringComparison.Ordinal);
        Assert.EndsWith("\nthinking=" + Base64Url("high"), OwnerHttpBinding.ProviderSetupCheckPayload(
            new OwnerProviderSetupCheckAction("claude", "claude-haiku-5-5", Thinking: "high")), StringComparison.Ordinal);
        Assert.Contains("provider=" + Base64Url("anthropic"), OwnerHttpBinding.ProviderSetupCheckPayload(
            new OwnerProviderSetupCheckAction("claude", "claude-haiku-5-5")), StringComparison.Ordinal);
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HostedModelCall Call(string? thinking) =>
        new("claude-haiku-5-5", "Return JSON only.", "{\"agent\":\"a\"}", thinking, TimeSpan.FromSeconds(30));

    private static string MessageReply(string text, bool thinkingFirst = false, string stopReason = "end_turn") =>
        JsonSerializer.Serialize(new
        {
            id = "msg_test",
            type = "message",
            role = "assistant",
            model = "claude-haiku-5-5",
            content = thinkingFirst
                ? new object[] { new { type = "thinking", thinking = "", signature = "sig" }, new { type = "text", text } }
                : [new { type = "text", text }],
            stop_reason = stopReason,
            stop_sequence = (string?)null,
            usage = new { input_tokens = 100, output_tokens = 42, cache_creation_input_tokens = 7, cache_read_input_tokens = 900 },
        });

    private static ProviderConfigurationStore Store(DirectoryInfo directory) =>
        new(Path.Combine(directory.FullName, "providers.json"), EmptySeed());

    private static ProviderConfigurationSeed EmptySeed() => new("deterministic", null, null, null, null, null, null);

    private static InhabitantObservation Observation(bool strategic, string inhabitantId = "inhabitant-test")
    {
        CognitionCandidate[] candidates = strategic
            ? [new CognitionCandidate("build:building:shelter", "Build a shelter.", 20), new CognitionCandidate("safe_idle", "Wait safely.", 100)]
            : [new CognitionCandidate("safe_idle", "Wait safely.", 100)];
        return new InhabitantObservation(inhabitantId, 12, 0, 3, "sha256:anthropic-test", 5_000, candidates);
    }

    private static AgentConversationTurnRequest ConversationRequest() => new(
        Guid.NewGuid().ToString("N"), "conversation:anthropic-test", 2, 4, 7,
        AgentConversationPurpose.PublicTurn, "agent-a", "Aster", "agent-b", "Rowan",
        "Calm and curious", "Build a mill",
        [new AgentConversationTurn("conversation:anthropic-test:turn:1", "agent-b",
            "Hello there", 6, ["agent-a"], AgentConversationDisposition.Continue)],
        [AgentConversationEffect.None]);

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ApiKey, string? AnthropicVersion, string? Authorization, string Body);

    /// <summary>Answers every request with one recorded Anthropic reply and keeps what was sent.</summary>
    private sealed class AnthropicHandler(string reply, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("x-api-key", out var keys) ? keys.Single() : null,
                request.Headers.TryGetValues("anthropic-version", out var versions) ? versions.Single() : null,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ChatHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"model":"hosted-test","choices":[{"message":{"content":"{\"selected_candidate_id\":\"safe_idle\",\"confidence\":1.0}"}}]}"""),
            };
        }
    }
}
