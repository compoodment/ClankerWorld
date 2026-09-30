using System.Net;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed class InvalidHostedDecisionTests
{
    [Theory]
    [InlineData("never_offered", 1, "candidate_not_legal")]
    [InlineData("safe_idle", 1.2, "malformed_response")]
    [InlineData("safe_idle", 1, "provider_decision")]
    public async Task DeferredHostedAnswerTerminatesOneMeteredDecision(string candidate, double confidence, string outcome)
    {
        var directory = Directory.CreateTempSubdirectory("invalid-hosted-");
        try
        {
            var configuration = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            configuration.Configure(new("personal", "openai", "test-model", "test-only-key", false, "founder-scout"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            using var handler = new AnswerHandler(candidate, confidence);
            var router = new ConfigurableDecisionProvider(configuration, new ClientFactory(handler), usageStore: usage);
            using var world = new PrivateWorldRuntime("invalid-hosted-answer", id =>
                id == "founder-scout" ? router : new DeterministicDecisionProvider());
            CognitionAdmissionResult? admission = null;
            for (var tick = 0; tick < 25 && admission is null; tick++)
            {
                var result = await world.AdvanceOneTickNonBlockingAsync();
                admission = result.Decisions.FirstOrDefault(item => item.InhabitantId == "founder-scout")?.Admission;
                await Task.Delay(30);
            }
            Assert.NotNull(admission);
            Assert.True(admission.Accepted);
            Assert.Equal(outcome, admission.Outcome);
            Assert.Equal(outcome != "provider_decision", admission.FellBack);
            Assert.Equal("safe_idle", admission.Intention!.CandidateId);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(1, usage.Capture().Attempts);
            Assert.DoesNotContain(world.ExportState().Society.Cognition.Queue, item => item.InhabitantId == "founder-scout");
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class AnswerHandler(string candidate, double confidence) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var answer = JsonSerializer.Serialize(new
            {
                selected_candidate_id = candidate,
                confidence,
                probabilities = new Dictionary<string, double> { [candidate] = 1 },
            });
            var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = answer } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
