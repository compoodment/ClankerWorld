using System.Net;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ModelRequestWordingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalWorldRenderedRequestsUseReadableHouseholdsAndTowns(bool personal)
    {
        var handler = new SyntheticModelHandler(personal);
        using var client = new HttpClient(handler);
        IDecisionProvider provider = personal
            ? new OpenAiCompatibleDecisionProvider(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model")
            : new JevDecisionProvider(client, () => "synthetic-key", new Uri("https://model.test/v1/systemone"));
        using var seed = NormalPathWorld.CreateGenerated("model-wording", _ => provider);
        var state = seed.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select((person, index) => person with
            {
                Skills = index == 0 ? [new(SettlementSkillKind.Building, 0), new(SettlementSkillKind.Farming, 0)]
                    : index == 1 ? [new(SettlementSkillKind.Smithing, 0)] : null,
            }).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        await world.AdvanceOneTickNonBlockingAsync();
        for (var attempt = 0; attempt < 50 && handler.Bodies.Count < PrivateWorldRuntime.RequiredFounders; attempt++)
            await Task.Delay(10);
        Assert.NotEmpty(handler.Bodies);
        foreach (var body in handler.Bodies)
        {
            using var payload = JsonDocument.Parse(body);
            var text = personal
                ? payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()! +
                    payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!
                : payload.RootElement.GetRawText();
            Assert.Null(RetiredWording.Find(text));
            var userText = personal ? payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()! : null;
            using var user = userText is null ? null : JsonDocument.Parse(userText);
            var context = personal ? user!.RootElement : payload.RootElement.GetProperty("state");
            var agent = world.Society.GetInhabitant(context.GetProperty("agent_id").GetString()!);
            var self = personal ? context.GetProperty("self") : context;
            Assert.Equal(world.Society.GetHousehold(agent.HouseholdId!).Name, self.GetProperty("household").GetString());
            Assert.Equal(world.Towns.Single().Name, self.GetProperty("town").GetString());
            Assert.Equal((state.Inhabitants.Single(person => person.InhabitantId == agent.Id).Skills ?? [])
                .Select(skill => skill.Kind.ToString().ToLowerInvariant()),
                self.GetProperty("skills").EnumerateArray().Select(skill => skill.GetString()));
            foreach (var field in new[] { "world_tick", "run_epoch", "decision_generation", "inhabitant_id", "household_id" })
                Assert.False(context.TryGetProperty(field, out _), field);
            Assert.False(self.TryGetProperty("household_id", out _));
            var ids = context.GetProperty("candidates").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
            Assert.Contains("safe_idle", ids);
            if (!personal)
            {
                var choices = payload.RootElement.GetProperty("questions").GetProperty("selected_candidate").GetProperty("criteria");
                Assert.Equal(ids, choices.EnumerateObject().Select(item => item.Name));
                Assert.False(context.TryGetProperty("personality", out _));
            }
        }
    }

    private sealed class SyntheticModelHandler(bool personal) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
            var answer = "{\"selected_candidate_id\":\"safe_idle\",\"confidence\":1,\"probabilities\":{\"safe_idle\":1}}";
            var body = personal
                ? JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = answer } } } })
                : "{\"answers\":{\"selected_candidate\":{\"choice\":\"safe_idle\",\"confidence\":1,\"probabilities\":{\"safe_idle\":1}}}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
