using System.Net;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ModelNeedWordsTests
{
    [Theory]
    [InlineData(0, "starving")]
    [InlineData(1_999, "starving")]
    [InlineData(2_000, "hungry")]
    [InlineData(3_999, "hungry")]
    [InlineData(4_000, "fine")]
    [InlineData(6_999, "fine")]
    [InlineData(7_000, "full")]
    [InlineData(10_000, "full")]
    public void FullnessWordsFollowTheAgreedLevels(int fullness, string level)
    {
        Assert.Equal(level, ModelNeedWords.FullnessLevel(fullness));
        Assert.Equal($"{level} (starving, hungry, fine, full; starving is worst, full is best)",
            ModelNeedWords.Fullness(fullness));
    }

    [Theory]
    [InlineData(0, "freezing")]
    [InlineData(3_499, "freezing")]
    [InlineData(3_500, "chilly")]
    [InlineData(5_999, "chilly")]
    [InlineData(6_000, "warm")]
    [InlineData(10_000, "warm")]
    public void WarmthWordsFollowTheAgreedLevels(int warmth, string level)
    {
        Assert.Equal(level, ModelNeedWords.WarmthLevel(warmth));
        Assert.Equal($"{level} (freezing, chilly, warm; freezing is worst, warm is best)",
            ModelNeedWords.Warmth(warmth));
    }

    [Theory]
    [InlineData(0, "well")]
    [InlineData(2_499, "well")]
    [InlineData(2_500, "unwell")]
    [InlineData(4_999, "unwell")]
    [InlineData(5_000, "ill")]
    [InlineData(7_499, "ill")]
    [InlineData(7_500, "very ill")]
    [InlineData(10_000, "very ill")]
    public void IllnessWordsFollowTheCutPointsWhereIllnessSlowsWork(int illness, string level)
    {
        Assert.Equal(level, ModelNeedWords.IllnessLevel(illness));
        Assert.Equal($"{level} (very ill, ill, unwell, well; very ill is worst, well is best)",
            ModelNeedWords.Illness(illness));
    }

    [Fact]
    public void IllnessWordsChangeWhereTheSimulationSlowsWork()
    {
        foreach (var illness in new[] { 2_499, 2_500, 4_999, 5_000, 7_499, 7_500 })
        {
            var expected = SettlementIllnessRules.WorkRatePercent(illness) switch
            {
                100 => "well",
                75 => "unwell",
                50 => "ill",
                _ => "very ill",
            };
            Assert.Equal(expected, ModelNeedWords.IllnessLevel(illness));
        }
    }

    [Fact]
    public async Task PersonalRequestsDescribeEveryNeedInWordsWithoutExactValues()
    {
        var handler = new RecordingHandler(PersonalReply);
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-test-key",
            new Uri("https://model.test/v1/chat/completions"), "test-model", needFormat: ModelNeedFormat.Words);

        await provider.DecideAsync(new CognitionDecisionRequest("words", 2, Observation(3_917, 5_123, 2_731)));

        using var body = JsonDocument.Parse(handler.Body!);
        var system = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        var input = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        using var question = JsonDocument.Parse(input);
        var self = question.RootElement.GetProperty("self");
        Assert.Equal("hungry (starving, hungry, fine, full; starving is worst, full is best)",
            question.RootElement.GetProperty("fullness").GetString());
        Assert.Equal("chilly (freezing, chilly, warm; freezing is worst, warm is best)", self.GetProperty("warmth").GetString());
        Assert.Equal("unwell (very ill, ill, unwell, well; very ill is worst, well is best)", self.GetProperty("illness").GetString());
        Assert.Contains("whole scale from worst to best", system, StringComparison.Ordinal);
        foreach (var exact in new[] { "warmth_basis_points", "illness_basis_points", "hunger_basis_points", "3917", "5123", "2731" })
            Assert.DoesNotContain(exact, input, StringComparison.Ordinal);
        // Memory confidence keeps its own 0–10000 scale; only the need scales go.
        Assert.DoesNotContain("10000 is full", system, StringComparison.Ordinal);
        Assert.DoesNotContain("10000 warm", system, StringComparison.Ordinal);
        Assert.DoesNotContain("10000 severely ill", system, StringComparison.Ordinal);
        Assert.Null(RetiredWording.Find(system));
        Assert.Null(RetiredWording.Find(input));
        // Only how needs are shown changes; the rest of the request keeps its fields and order.
        Assert.Equal(["agent_id", "fullness", "needs_name", "name_retry", "needs_personality", "needs_aspiration",
                "self", "candidates", "retrieved_memories", "known_map_facts"],
            question.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Aster Vale", self.GetProperty("name").GetString());
        Assert.Equal("seek_food", question.RootElement.GetProperty("candidates")[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task UnknownConditionStaysUnknownInWords()
    {
        var handler = new RecordingHandler(PersonalReply);
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-test-key",
            new Uri("https://model.test/v1/chat/completions"), "test-model", needFormat: ModelNeedFormat.Words);

        await provider.DecideAsync(new CognitionDecisionRequest("unknown", 2, Observation(1_500, null, null)));

        using var body = JsonDocument.Parse(handler.Body!);
        using var question = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        var self = question.RootElement.GetProperty("self");
        Assert.StartsWith("starving (", question.RootElement.GetProperty("fullness").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, self.GetProperty("warmth").ValueKind);
        Assert.Equal(JsonValueKind.Null, self.GetProperty("illness").ValueKind);
        Assert.Contains("Null condition fields mean unknown",
            body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NumbersStayTheDefaultUntilTheWordsAreCompared()
    {
        Assert.Equal(ModelNeedFormat.Numbers, ModelNeedWords.DefaultFormat);
        var handler = new RecordingHandler(PersonalReply);
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-test-key",
            new Uri("https://model.test/v1/chat/completions"), "test-model");

        await provider.DecideAsync(new CognitionDecisionRequest("numbers", 2, Observation(3_917, 5_123, 2_731)));

        using var body = JsonDocument.Parse(handler.Body!);
        using var question = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal(3_917, question.RootElement.GetProperty("hunger_basis_points").GetInt32());
        Assert.Equal(5_123, question.RootElement.GetProperty("self").GetProperty("warmth_basis_points").GetInt32());
        Assert.Equal(2_731, question.RootElement.GetProperty("self").GetProperty("illness_basis_points").GetInt32());
        Assert.False(question.RootElement.TryGetProperty("fullness", out _));
    }

    [Fact]
    public async Task JevRoutineRequestsUseTheSameFullnessWords()
    {
        var handler = new RecordingHandler(
            """
            {
              "model": "jev-1.13.0",
              "answers": { "selected_candidate": { "type": "choice", "choice": "seek_food", "confidence": 0.8 } }
            }
            """);
        using var client = new HttpClient(handler);
        var provider = new JevDecisionProvider(client, () => "synthetic-test-key",
            new Uri("https://typesafe.test/v1/systemone"), needFormat: ModelNeedFormat.Words);

        var response = await provider.DecideAsync(new CognitionDecisionRequest("jev-words", 1, Observation(1_917, 5_123, 2_731)));

        using var body = JsonDocument.Parse(handler.Body!);
        var state = body.RootElement.GetProperty("state");
        var instructions = body.RootElement.GetProperty("questions").GetProperty("selected_candidate")
            .GetProperty("instructions").GetString()!;
        Assert.Equal(ModelNeedWords.Fullness(1_917), state.GetProperty("fullness").GetString());
        Assert.StartsWith("starving (starving, hungry, fine, full;", state.GetProperty("fullness").GetString(), StringComparison.Ordinal);
        Assert.False(state.TryGetProperty("hunger_basis_points", out _));
        Assert.DoesNotContain("1917", handler.Body, StringComparison.Ordinal);
        Assert.Contains("whole scale from worst to best", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("10000", instructions, StringComparison.Ordinal);
        Assert.Equal(["agent_id", "fullness", "household", "town", "housing", "candidates", "memory_compaction_candidates"],
            state.EnumerateObject().Select(property => property.Name));
        Assert.Equal("seek_food", response.SelectedCandidateId);
    }

    private static InhabitantObservation Observation(int fullness, int? warmth, int? illness) => new(
        "actor-scout", 9, 1, 3, "sha256:need-words", fullness,
        [
            new CognitionCandidate("safe_idle", "Continue safely.", 0),
            new CognitionCandidate("seek_food", "Travel to food.", 10, "berry-patch", "Berry patch"),
        ],
        RetrievedMemories: [new CognitionMemoryExcerpt("m1", "actor-scout", "friend", "Mira shared berries.", 8)],
        KnownMapFacts: [new CognitionKnowledgeFact(4, 5, "grassland", ["berries"], "actor-scout", 7, "firsthand")],
        Self: new CognitionSelfContext("actor-scout", "Aster Vale", "Adult", "Curious", "Explore",
            "household:one", warmth, illness, "I remember the path.", "Aster's household", "First Town"));

    private const string PersonalReply =
        """
        {
          "model": "test-model",
          "choices": [ { "message": { "role": "assistant", "content": "{\"selected_candidate_id\":\"seek_food\",\"confidence\":0.9}" } } ],
          "usage": { "prompt_tokens": 40, "completion_tokens": 8 }
        }
        """;

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody) };
        }
    }
}
