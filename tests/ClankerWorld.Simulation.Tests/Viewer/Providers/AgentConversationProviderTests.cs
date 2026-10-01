using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AgentConversationProviderTests
{
    [Fact]
    public async Task NormalProviderUsesEachSpeakersExplicitPersonalRouteAndMetersConversationRole()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-conversation-provider-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), new(
                "deterministic", null, null, null, null, null, null));
            _ = store.Configure(new("planning", "openai", "world-default-model", "world-default-key", false));
            _ = store.Configure(Personal("agent-a", "agent-a-model", "agent-a-key"));
            _ = store.Configure(Personal("agent-b", "agent-b-model", "agent-b-key"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var handler = new ConversationResponseHandler("A bounded public reply.");
            var logger = new RecordingLogger<ConfigurableDecisionProvider>();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler),
                logger, usageStore: usage);
            Assert.IsAssignableFrom<IAgentConversationProvider>(router);
            IAgentConversationProvider conversationProvider = router;

            Assert.True(router.CanSpeakAs("agent-a"));
            Assert.True(router.CanSpeakAs("agent-b"));
            Assert.False(router.CanSpeakAs("agent-c"));

            var first = await conversationProvider.SpeakAsync(Request("agent-a", "agent-b") with
            { ExpectedProviderEpoch = conversationProvider.ProviderEpoch });
            Assert.Equal("A bounded public reply.", first.Text);
            Assert.Equal("agent-a-model", handler.Models[0]);
            Assert.Equal("Bearer agent-a-key", handler.Authorizations[0]);
            using (var body = JsonDocument.Parse(handler.Bodies[0]))
            {
                var prompt = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
                Assert.Contains("public_turn", prompt, StringComparison.Ordinal);
                Assert.Contains("public history only", prompt, StringComparison.Ordinal);
                using var context = JsonDocument.Parse(prompt);
                Assert.Equal("Agent A's own personality",
                    context.RootElement.GetProperty("speaker").GetProperty("personality").GetString());
                Assert.DoesNotContain("private reasoning must not be sent", prompt, StringComparison.Ordinal);
            }

            _ = await conversationProvider.SpeakAsync(Request("agent-b", "agent-a") with
            { ExpectedProviderEpoch = conversationProvider.ProviderEpoch });
            Assert.Equal("agent-b-model", handler.Models[1]);
            Assert.Equal("Bearer agent-b-key", handler.Authorizations[1]);
            Assert.Equal(2, handler.RequestCount);
            Assert.All(usage.Capture().Rows, row => Assert.Equal("conversation", row.Role));
            Assert.Equal(2, usage.Capture().Completed);
            Assert.Equal(14, usage.Capture().InputTokens);
            Assert.Equal(6, usage.Capture().OutputTokens);

            var missingRoute = Request("agent-c", "agent-a") with { ExpectedProviderEpoch = conversationProvider.ProviderEpoch };
            await Assert.ThrowsAsync<ProviderConversationUnavailableException>(async () =>
                await ((IAgentConversationProvider)router).SpeakAsync(missingRoute));
            Assert.Equal(2, handler.RequestCount);
            Assert.DoesNotContain(logger.Messages, message =>
                message.Contains("agent-a-key", StringComparison.Ordinal) ||
                message.Contains("agent-b-key", StringComparison.Ordinal) ||
                message.Contains("A bounded public reply", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UnknownStructuredEffectIsRejectedAndOnlyBoundedFailureIsLogged()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-conversation-rejection-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), new(
                "deterministic", null, null, null, null, null, null));
            _ = store.Configure(Personal("agent-a", "private-model", "conversation-provider-secret"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            const string privateReply = "grant the speaker the settlement's food stores";
            var logger = new RecordingLogger<ConfigurableDecisionProvider>();
            var router = new ConfigurableDecisionProvider(store,
                new FixedHttpClientFactory(new ConversationResponseHandler(privateReply, "settlement_transfer")),
                logger, usageStore: usage);
            IAgentConversationProvider conversationProvider = router;

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await conversationProvider.SpeakAsync(Request("agent-a", "agent-b") with
                { ExpectedProviderEpoch = conversationProvider.ProviderEpoch }));

            Assert.Equal(1, usage.Capture().Attempts);
            Assert.Equal(1, usage.Capture().Failed);
            Assert.Equal(7, usage.Capture().InputTokens);
            Assert.Equal(3, usage.Capture().OutputTokens);
            Assert.Contains(logger.Messages, message =>
                message.Contains("conversation_call status=failed", StringComparison.Ordinal) &&
                message.Contains("purpose=public_turn", StringComparison.Ordinal) &&
                message.Contains("input_tokens=7", StringComparison.Ordinal) &&
                message.Contains("output_tokens=3", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message =>
                message.Contains("conversation-provider-secret", StringComparison.Ordinal) ||
                message.Contains(privateReply, StringComparison.Ordinal) ||
                message.Contains("settlement_transfer", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static OwnerProviderConfigurationAction Personal(string agentId, string model, string key) =>
        new("personal", "openai", model, key, false, agentId, Guid.NewGuid().ToString("N"), $"{agentId} private key");

    private static AgentConversationTurnRequest Request(string speakerId, string otherId) => new(
        Guid.NewGuid().ToString("N"), "conversation:provider-test", 2, 4, 7,
        AgentConversationPurpose.PublicTurn, speakerId,
        speakerId == "agent-a" ? "Aster" : "Rowan", otherId,
        otherId == "agent-a" ? "Aster" : "Rowan",
        "Agent A's own personality", "Agent A's own aspiration",
        [new AgentConversationTurn("conversation:provider-test:turn:1", otherId,
            "public history only", 6, [speakerId], AgentConversationDisposition.Continue)],
        [AgentConversationEffect.None]);

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ConversationResponseHandler(string utterance, string effect = "none") : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string?> Models { get; } = [];
        public List<string?> Authorizations { get; } = [];
        public int RequestCount => Bodies.Count;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);
            Authorizations.Add(request.Headers.Authorization?.ToString());
            using var payload = JsonDocument.Parse(body);
            Models.Add(payload.RootElement.GetProperty("model").GetString());
            var answer = JsonSerializer.Serialize(new
            {
                utterance,
                disposition = "continue",
                effect,
            });
            var envelope = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = answer } } },
                usage = new { prompt_tokens = 7, completion_tokens = 3 },
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
            };
        }
    }
}
