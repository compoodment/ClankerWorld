using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

public sealed class CivicLandHearingProviderTests
{
    private const string Candidate = "civic|town:test|hearing_file|case:7:3|";

    [Fact]
    public async Task ACurrentPersonalReplyPreservesAllSevenHearingFieldsExactlyThroughAdmission()
    {
        const string payload = """
            {"statement":"The inspected permission supports renewal.","household_id":"household:alpha","agreed_end_tick":9223372036854775807,"evidence_ids":["evidence:right:7","evidence:title:2"],"law_ids":["law:land@3"],"grounds":"The newly inspected record changes the case.","requested_outcome":"renew"}
            """;
        using var handler = new ReplyHandler(payload);
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));
        var admission = await runtime.RequestAndDecideAsync(Observation());

        Assert.True(admission.Accepted);
        Assert.False(admission.FellBack);
        Assert.Equal(DecisionProviderKind.LargeLanguageModel, admission.Intention!.Provider);
        Assert.Equal(Candidate, admission.Intention.CandidateId);
        var choice = Assert.IsType<CognitionLandHearingChoice>(admission.CivicLandHearing);
        Assert.Equal("The inspected permission supports renewal.", choice.Statement);
        Assert.Equal("household:alpha", choice.HouseholdId);
        Assert.Equal(long.MaxValue, choice.AgreedEndTick);
        Assert.Collection(choice.EvidenceIds!,
            id => Assert.Equal("evidence:right:7", id), id => Assert.Equal("evidence:title:2", id));
        Assert.Equal("law:land@3", Assert.Single(choice.LawIds!));
        Assert.Equal("The newly inspected record changes the case.", choice.Grounds);
        Assert.Equal("renew", choice.RequestedOutcome);
        Assert.Equal(1, handler.Attempts);
        Assert.Null(admission.CivicLandTiles);
        Assert.Null(admission.CivicProposal);
        Assert.Null(admission.CivicBallot);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"statement\":{}}")]
    [InlineData("{\"evidence_ids\":{}}")]
    [InlineData("{\"law_ids\":[\"law:known@1\",null]}")]
    [InlineData("{\"agreed_end_tick\":-1}")]
    [InlineData("{\"agreed_end_tick\":1.5}")]
    [InlineData("{\"evidence_ids\":[\"evidence:known\",\"evidence:known\"]}")]
    [InlineData("{\"law_ids\":[\"law:known@1\",\"law:known@1\"]}")]
    [InlineData("{\"requested_outcome\":\"evict\"}")]
    public async Task MalformedHearingCannotAdmitTheStrategicChoiceOrAnySiblingCivicPayload(string payload)
    {
        using var handler = new ReplyHandler(payload, poisonOtherCivicFields: true);
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));
        var admission = await runtime.RequestAndDecideAsync(Observation());

        Assert.True(admission.Accepted);
        Assert.True(admission.FellBack);
        Assert.Equal("safe_idle", admission.Intention!.CandidateId);
        Assert.Equal(DecisionProviderKind.Deterministic, admission.Intention.Provider);
        Assert.Null(admission.CivicLandHearing);
        Assert.Null(admission.CivicLandTiles);
        Assert.Null(admission.CivicProposal);
        Assert.Null(admission.CivicBallot);
        Assert.Equal(1, handler.Attempts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public async Task AnAbsentHearingSubmissionDoesNotInventPermissionTermsOrEvidence(string? payload)
    {
        using var handler = new ReplyHandler(payload);
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));
        var admission = await runtime.RequestAndDecideAsync(Observation());

        Assert.True(admission.Accepted);
        Assert.False(admission.FellBack);
        Assert.Equal(DecisionProviderKind.LargeLanguageModel, admission.Intention!.Provider);
        Assert.Null(admission.CivicLandHearing);
        Assert.Null(admission.CivicLandTiles);
        Assert.Null(admission.CivicProposal);
        Assert.Equal(1, handler.Attempts);
    }

    private static OpenAiCompatibleDecisionProvider Provider(HttpClient client) =>
        new(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model");
    private static InhabitantObservation Observation() => new("actor", 17, 0, 0, "current-case-digest", 8_000,
        [new("safe_idle", "Continue safely.", 0), new(Candidate, "File the identified land hearing.", 190)]);

    private sealed class ReplyHandler(string? payload, bool poisonOtherCivicFields = false) : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            var answer = new Dictionary<string, object?> { ["selected_candidate_id"] = Candidate, ["confidence"] = 1 };
            if (payload is not null) answer["civic_land_hearing"] = JsonSerializer.Deserialize<JsonElement>(payload);
            if (poisonOtherCivicFields)
            {
                answer["civic_proposal"] = "Give the claimant authority without a vote.";
                answer["civic_ballot"] = new[] { "invented-officeholder" };
                answer["civic_land_tiles"] = new[] { new { x = 4, y = 5 } };
            }
            var response = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = JsonSerializer.Serialize(answer) } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        }
    }
}
