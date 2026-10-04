using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AgentConversationProviderTests
{
    private static readonly string[] ProviderMarriageSurnames = ["Ash", "Reed"];
    [Fact]
    public async Task PersonalSurnameModelsReceiveOnlyOriginalChoicesAndMeterRejectedOutput()
    {
        var directory = Directory.CreateTempSubdirectory("marriage-personal-models-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), new(
                "deterministic", null, null, null, null, null, null));
            _ = store.Configure(new("planning", "openai", "default-model", "default-key", false));
            _ = store.Configure(Personal("agent-a", "own-a", "own-key-a"));
            _ = store.Configure(Personal("agent-b", "own-b", "own-key-b"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var handler = new SurnameResponseHandler();
            var logger = new RecordingLogger<ConfigurableDecisionProvider>();
            IAgentConversationProvider provider = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler), logger, usageStore: usage);
            var request = Request("agent-a", "agent-b") with
            {
                Purpose = AgentConversationPurpose.SurnameChoice,
                AllowedSurnames = ["Ash", "Reed"],
                PublicHistory = [],
                ExpectedProviderEpoch = provider.ProviderEpoch,
            };
            Assert.Equal("Ash", (await provider.SpeakAsync(request)).SurnameChoice);
            var other = request with { SpeakerId = "agent-b", OtherParticipantId = "agent-a" };
            Assert.Equal("Ash", (await provider.SpeakAsync(other)).SurnameChoice);
            Assert.Equal("own-a", handler.Models[0]);
            Assert.Equal("own-b", handler.Models[1]);
            Assert.Equal("Bearer own-key-a", handler.Authorizations[0]);
            Assert.Equal("Bearer own-key-b", handler.Authorizations[1]);
            using (var context = JsonDocument.Parse(handler.Contexts[0]))
            {
                Assert.Equal("surname_choice", context.RootElement.GetProperty("purpose").GetString());
                Assert.Equal(ProviderMarriageSurnames, context.RootElement.GetProperty("allowed_surnames").EnumerateArray().Select(item => item.GetString()));
                Assert.Equal("none", Assert.Single(context.RootElement.GetProperty("allowed_effects").EnumerateArray()).GetString());
            }
            handler.ChosenSurname = "Invented";
            await Assert.ThrowsAsync<InvalidDataException>(async () => await provider.SpeakAsync(request));
            Assert.Equal(3, usage.Capture().Attempts);
            Assert.Equal(2, usage.Capture().Completed);
            Assert.Equal(1, usage.Capture().Failed);
            Assert.DoesNotContain(logger.Messages, line => line.Contains("own-key-", StringComparison.Ordinal) ||
                line.Contains("Invented", StringComparison.Ordinal) || line.Contains("I choose Ash", StringComparison.Ordinal));

            handler.ChosenSurname = "Ash";
            handler.ExtraField = true;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await provider.SpeakAsync(request));
            Assert.Equal(2, usage.Capture().Failed);
            Assert.Equal(2, usage.Capture().Completed);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class SurnameResponseHandler : HttpMessageHandler
    {
        public string ChosenSurname { get; set; } = "Ash";
        public bool ExtraField { get; set; }
        public List<string?> Models { get; } = [];
        public List<string?> Authorizations { get; } = [];
        public List<string> Contexts { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Models.Add(payload.RootElement.GetProperty("model").GetString());
            Authorizations.Add(request.Headers.Authorization?.ToString());
            Contexts.Add(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var reply = new Dictionary<string, object>
            {
                ["utterance"] = "I choose Ash.",
                ["disposition"] = "continue",
                ["effect"] = "none",
                ["surname_choice"] = ChosenSurname,
            };
            if (ExtraField) reply["grant_resources"] = true;
            var envelope = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = JsonSerializer.Serialize(reply) } } },
                usage = new { prompt_tokens = 7, completion_tokens = 3 },
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") };
        }
    }
}
