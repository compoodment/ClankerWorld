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
        var (system, input) = await PersonalRequestAsync(ModelNeedFormat.Words, Observation(3_917, 5_123, 2_731));
        var (_, numbersInput) = await PersonalRequestAsync(ModelNeedFormat.Numbers, Observation(3_917, 5_123, 2_731));
        using var question = JsonDocument.Parse(input);
        using var numbersQuestion = JsonDocument.Parse(numbersInput);
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
        Assert.Equal(FieldsWithNeedsInWords(numbersQuestion.RootElement), FieldNames(question.RootElement));
        Assert.Equal(FieldsWithNeedsInWords(numbersQuestion.RootElement.GetProperty("self")), FieldNames(self));
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
    public async Task NumbersStayTheDefault()
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
    public async Task JevRoutineRequestsDescribeEveryNeedInWordsWithoutExactValues()
    {
        var (body, instructions) = await JevRequestAsync(ModelNeedFormat.Words, Observation(1_917, 5_123, 2_731));

        using var document = JsonDocument.Parse(body);
        var state = document.RootElement.GetProperty("state");
        Assert.Equal("starving (starving, hungry, fine, full; starving is worst, full is best)", state.GetProperty("fullness").GetString());
        Assert.Equal("chilly (freezing, chilly, warm; freezing is worst, warm is best)", state.GetProperty("warmth").GetString());
        Assert.Equal("unwell (very ill, ill, unwell, well; very ill is worst, well is best)", state.GetProperty("illness").GetString());
        foreach (var exact in new[] { "hunger_basis_points", "warmth_basis_points", "illness_basis_points", "1917", "5123", "2731" })
            Assert.DoesNotContain(exact, body, StringComparison.Ordinal);
        Assert.Contains("whole scale from worst to best", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("10000", instructions, StringComparison.Ordinal);
        Assert.Null(RetiredWording.Find(instructions));
        var (numbersBody, _) = await JevRequestAsync(ModelNeedFormat.Numbers, Observation(1_917, 5_123, 2_731));
        using var numbersDocument = JsonDocument.Parse(numbersBody);
        Assert.Equal(FieldsWithNeedsInWords(numbersDocument.RootElement.GetProperty("state")), FieldNames(state));
    }

    [Theory]
    [InlineData(ModelNeedFormat.Numbers)]
    [InlineData(ModelNeedFormat.Words)]
    public async Task JevRoutineRequestsKeepUnknownConditionUnknown(ModelNeedFormat format)
    {
        var (body, instructions) = await JevRequestAsync(format, Observation(5_000, null, null));

        using var document = JsonDocument.Parse(body);
        var state = document.RootElement.GetProperty("state");
        var (warmth, illness) = format == ModelNeedFormat.Words ? ("warmth", "illness") : ("warmth_basis_points", "illness_basis_points");
        Assert.Equal(JsonValueKind.Null, state.GetProperty(warmth).ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty(illness).ValueKind);
        Assert.Contains("A null need is unknown.", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JevRoutineRequestsSendAllThreeNeedsAsNumbersByDefault()
    {
        var (body, instructions) = await JevRequestAsync(null, Observation(1_917, 5_123, 2_731));

        using var document = JsonDocument.Parse(body);
        var state = document.RootElement.GetProperty("state");
        Assert.Equal(1_917, state.GetProperty("hunger_basis_points").GetInt32());
        Assert.Equal(5_123, state.GetProperty("warmth_basis_points").GetInt32());
        Assert.Equal(2_731, state.GetProperty("illness_basis_points").GetInt32());
        Assert.False(state.TryGetProperty("fullness", out _));
        Assert.Contains("10000 is full and 0 is starving", instructions, StringComparison.Ordinal);
        Assert.Contains("illness_basis_points is 0 well to 10000 severely ill", instructions, StringComparison.Ordinal);
        Assert.Null(RetiredWording.Find(instructions));
    }

    private static async Task<(string System, string Input)> PersonalRequestAsync(ModelNeedFormat format, InhabitantObservation observation)
    {
        var handler = new RecordingHandler(PersonalReply);
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-test-key",
            new Uri("https://model.test/v1/chat/completions"), "test-model", needFormat: format);

        await provider.DecideAsync(new CognitionDecisionRequest("words", 2, observation));

        using var body = JsonDocument.Parse(handler.Body!);
        var messages = body.RootElement.GetProperty("messages");
        return (messages[0].GetProperty("content").GetString()!, messages[1].GetProperty("content").GetString()!);
    }

    private static string[] FieldNames(JsonElement request) => request.EnumerateObject().Select(property => property.Name).ToArray();

    /// <summary>The numbers request's field order with each need field renamed to its words field.</summary>
    private static string[] FieldsWithNeedsInWords(JsonElement numbersRequest) => numbersRequest.EnumerateObject()
        .Select(property => property.Name switch
        {
            "hunger_basis_points" => "fullness",
            "warmth_basis_points" => "warmth",
            "illness_basis_points" => "illness",
            var name => name,
        }).ToArray();

    private static async Task<(string Body, string Instructions)> JevRequestAsync(ModelNeedFormat? format, InhabitantObservation observation)
    {
        var handler = new RecordingHandler(
            """
            {
              "model": "jev-1.13.0",
              "answers": { "selected_candidate": { "type": "choice", "choice": "seek_food", "confidence": 0.8 } }
            }
            """);
        using var client = new HttpClient(handler);
        var endpoint = new Uri("https://typesafe.test/v1/systemone");
        var provider = format is { } chosen
            ? new JevDecisionProvider(client, () => "synthetic-test-key", endpoint, needFormat: chosen)
            : new JevDecisionProvider(client, () => "synthetic-test-key", endpoint);

        var response = await provider.DecideAsync(new CognitionDecisionRequest("jev-needs", 1, observation));

        Assert.Equal("seek_food", response.SelectedCandidateId);
        using var document = JsonDocument.Parse(handler.Body!);
        return (handler.Body!, document.RootElement.GetProperty("questions").GetProperty("selected_candidate")
            .GetProperty("instructions").GetString()!);
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
