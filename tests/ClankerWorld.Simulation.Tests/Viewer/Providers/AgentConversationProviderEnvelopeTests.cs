using System.Net;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AgentConversationProviderTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"private-utterance\"")]
    [InlineData("32")]
    [InlineData("true")]
    [InlineData("{\"choices\":[{\"message\":[]}]}")]
    [InlineData("{\"choices\":[{\"message\":null}]}")]
    [InlineData("{\"choices\":[{\"message\":\"private-utterance\"}]}")]
    [InlineData("{\"choices\":[{\"message\":32}]}")]
    [InlineData("{\"choices\":[{\"message\":true}]}")]
    public async Task MalformedConversationEnvelopeIsRejectedAsProviderOutputAndNeverLogged(string envelope)
    {
        var directory = Directory.CreateTempSubdirectory("conversation-envelope-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), new(
                "deterministic", null, null, null, null, null, null));
            _ = store.Configure(Personal("agent-a", "test-model", "conversation-raw-secret"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var logger = new RecordingLogger<ConfigurableDecisionProvider>();
            var handler = new RawConversationEnvelopeHandler(envelope);
            IAgentConversationProvider provider = new ConfigurableDecisionProvider(store,
                new FixedHttpClientFactory(handler), logger, usageStore: usage);
            var request = Request("agent-a", "agent-b") with { ExpectedProviderEpoch = provider.ProviderEpoch };

            var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await provider.SpeakAsync(request));
            Assert.Equal(AgentConversationInterruption.ProviderRejected, AgentConversationFailureClassifier.Classify(error));
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(1, usage.Capture().Attempts);
            Assert.Equal(1, usage.Capture().Failed);
            Assert.Equal(0, usage.Capture().Completed);
            Assert.Contains(logger.Messages, line => line.Contains("conversation_call status=failed", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, line =>
                line.Contains("conversation-raw-secret", StringComparison.Ordinal) ||
                line.Contains("private-utterance", StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task CompletedWrapUpLogsSevenTurnsWithBoundedUsageAndNoDialogueText()
    {
        var directory = Directory.CreateTempSubdirectory("conversation-wrapup-log-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), new(
                "deterministic", null, null, null, null, null, null));
            _ = store.Configure(Personal("agent-a", "test-model", "conversation-log-secret"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var logger = new RecordingLogger<ConfigurableDecisionProvider>();
            var handler = new ConversationResponseHandler("A public wrap-up.");
            IAgentConversationProvider provider = new ConfigurableDecisionProvider(store,
                new FixedHttpClientFactory(handler), logger, usageStore: usage);
            var request = Request("agent-a", "agent-b") with
            {
                ExpectedProviderEpoch = provider.ProviderEpoch,
                Purpose = AgentConversationPurpose.WrapUp,
                PublicHistory = Enumerable.Range(1, 6).Select(number => new AgentConversationTurn(
                    $"conversation:provider-test:turn:{number}", number % 2 == 1 ? "agent-a" : "agent-b",
                    $"Public turn {number}.", number, [number % 2 == 1 ? "agent-b" : "agent-a"],
                    AgentConversationDisposition.Continue)).ToArray(),
                AllowedEffects = [AgentConversationEffect.None, AgentConversationEffect.MutualTrust],
            };

            var response = await provider.SpeakAsync(request);
            Assert.Equal("A public wrap-up.", response.Text);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(1, usage.Capture().Completed);
            Assert.Equal(7, usage.Capture().InputTokens);
            Assert.Equal(3, usage.Capture().OutputTokens);
            Assert.Contains(logger.Messages, line => line.Contains(
                "conversation_call status=completed purpose=wrap_up turn_count=7 latency_ms=", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, line =>
                line.Contains("turn_count=8", StringComparison.Ordinal) ||
                line.Contains("conversation-log-secret", StringComparison.Ordinal) ||
                line.Contains("A public wrap-up.", StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class RawConversationEnvelopeHandler(string envelope) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
            });
        }
    }
}
