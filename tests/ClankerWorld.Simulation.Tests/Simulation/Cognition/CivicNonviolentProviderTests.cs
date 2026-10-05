using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

public sealed class CivicNonviolentProviderTests
{
    private const string Candidate = "civic|town:test|remedy_offer|case:7:3|";

    [Fact]
    public async Task APersonalSubmissionPreservesEvidenceAndAllRemedyTermsThroughRealProviderAdmission()
    {
        const string payload = """
            {"statement":"I saw the recorded delivery.","uncertainty":"I did not see the earlier exchange.","evidence_ids":["conduct:7","law:quiet@2"],"grounds":"The newly inspected delivery changes the account.","terms":[{"kind":"return_goods","contributor_id":"actor","beneficiary_id":"resident:b","item_kind":"wood","quantity":2},{"kind":"repair_equipment","contributor_id":"resident:b","quantity":1,"target_id":"tool:7"},{"kind":"public_service_goods","contributor_id":"actor","beneficiary_id":"town:test","item_kind":"stone","quantity":3,"target_id":"warehouse:1"}],"completion_ticks":9223372036854775807}
            """;
        using var handler = new ReplyHandler(payload);
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));

        var admission = await runtime.RequestAndDecideAsync(Observation());

        Assert.True(admission.Accepted);
        Assert.False(admission.FellBack);
        Assert.Equal(DecisionProviderKind.LargeLanguageModel, admission.Intention!.Provider);
        Assert.Equal(Candidate, admission.Intention.CandidateId);
        var choice = Assert.IsType<CognitionNonviolentChoice>(admission.CivicNonviolent);
        Assert.Equal("I saw the recorded delivery.", choice.Statement);
        Assert.Equal("I did not see the earlier exchange.", choice.Uncertainty);
        Assert.Equal("The newly inspected delivery changes the account.", choice.Grounds);
        Assert.Equal(long.MaxValue, choice.CompletionTicks);
        Assert.Collection(choice.EvidenceIds!,
            id => Assert.Equal("conduct:7", id), id => Assert.Equal("law:quiet@2", id));
        Assert.Collection(choice.Terms!,
            term => Assert.Equal(new CognitionRemedyTerm("return_goods", "actor", "resident:b", "wood", 2, null), term),
            term => Assert.Equal(new CognitionRemedyTerm("repair_equipment", "resident:b", null, null, 1, "tool:7"), term),
            term => Assert.Equal(new CognitionRemedyTerm("public_service_goods", "actor", "town:test", "stone", 3, "warehouse:1"), term));
        Assert.Null(admission.CivicLandHearing);
        Assert.Null(admission.CivicLandTiles);
        Assert.Null(admission.CivicProposal);
        Assert.Null(admission.CivicBallot);
        Assert.Equal(1, handler.Attempts);
        Assert.Contains("civic_nonviolent", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("no transfer, authority, consent or completion is created by prose", handler.RequestBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"terms\":[null]}")]
    [InlineData("{\"terms\":[{\"kind\":\"return_goods\",\"contributor_id\":null,\"quantity\":1}]}")]
    [InlineData("{\"terms\":[{\"kind\":\"seize_goods\",\"contributor_id\":\"actor\",\"quantity\":1}]}")]
    [InlineData("{\"terms\":[{\"kind\":\"return_goods\",\"contributor_id\":\"actor\",\"quantity\":0}]}")]
    [InlineData("{\"terms\":[{\"kind\":\"return_goods\",\"contributor_id\":\"actor\",\"quantity\":\"2\"}]}")]
    [InlineData("{\"evidence_ids\":[\"conduct:7\",\"conduct:7\"]}")]
    [InlineData("{\"completion_ticks\":\"24\"}")]
    [InlineData("{\"authority\":\"mayor\"}")]
    public async Task MalformedSubmissionFallsBackWithoutAdmittingItsChoiceOrSiblingCivicPayload(string payload)
    {
        using var handler = new ReplyHandler(payload, poisonSiblingFields: true);
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));

        var admission = await runtime.RequestAndDecideAsync(Observation());

        Assert.True(admission.Accepted);
        Assert.True(admission.FellBack);
        Assert.Equal("safe_idle", admission.Intention!.CandidateId);
        Assert.Equal(DecisionProviderKind.Deterministic, admission.Intention.Provider);
        Assert.Null(admission.CivicNonviolent);
        Assert.Null(admission.CivicLandHearing);
        Assert.Null(admission.CivicLandTiles);
        Assert.Null(admission.CivicProposal);
        Assert.Null(admission.CivicBallot);
        Assert.Equal(1, handler.Attempts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public async Task AnAbsentSubmissionDoesNotInventEvidenceOrAnAgreement(string? payload)
    {
        using var handler = new ReplyHandler(payload);
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));

        var admission = await runtime.RequestAndDecideAsync(Observation());

        Assert.True(admission.Accepted);
        Assert.False(admission.FellBack);
        Assert.Equal(Candidate, admission.Intention!.CandidateId);
        Assert.Equal(DecisionProviderKind.LargeLanguageModel, admission.Intention.Provider);
        Assert.Null(admission.CivicNonviolent);
        Assert.Equal(1, handler.Attempts);
    }

    private static OpenAiCompatibleDecisionProvider Provider(HttpClient client) =>
        new(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model");

    private static InhabitantObservation Observation() => new("actor", 17, 0, 0, "current-hearing-digest", 8_000,
        [new("safe_idle", "Continue safely.", 0), new(Candidate, "Offer a remedy for the inspected case.", 190)]);

    private sealed class ReplyHandler(string? payload, bool poisonSiblingFields = false) : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var answer = new Dictionary<string, object?> { ["selected_candidate_id"] = Candidate, ["confidence"] = 1 };
            if (payload is not null) answer["civic_nonviolent"] = JsonSerializer.Deserialize<JsonElement>(payload);
            if (poisonSiblingFields)
            {
                answer["civic_proposal"] = "Give the author authority without a vote.";
                answer["civic_ballot"] = new[] { "invented-officeholder" };
                answer["civic_land_tiles"] = new[] { new { x = 4, y = 5 } };
                answer["civic_land_hearing"] = new { statement = "An unrelated land request." };
            }
            var response = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = JsonSerializer.Serialize(answer) } } } });
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
