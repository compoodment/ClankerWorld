using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class CivicBallotProviderTests
{
    private const string BallotCandidate = "civic|town:test|ballot|town:test:election:1:main:0|";

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public async Task MissingOrNullOptionalBallotPreservesAnUnrelatedPersonalAction(string? ballot)
    {
        using var handler = new ReplyHandler("seek_food", ballot);
        using var client = new HttpClient(handler);
        var provider = Provider(client);
        var runtime = new CognitionRuntime("actor", provider);

        var admission = await runtime.RequestAndDecideAsync(Observation("seek_food"));

        Assert.True(admission.Accepted);
        Assert.False(admission.FellBack);
        Assert.Equal("provider_decision", admission.Outcome);
        Assert.Equal("seek_food", admission.Intention!.CandidateId);
        Assert.Null(admission.CivicBallot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public async Task MissingOrNullBallotOnACivicChoiceSuppliesNoBallot(string? ballot)
    {
        using var handler = new ReplyHandler(BallotCandidate, ballot);
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));

        var admission = await runtime.RequestAndDecideAsync(Observation(BallotCandidate));

        Assert.True(admission.Accepted);
        Assert.False(admission.FellBack);
        Assert.Equal(BallotCandidate, admission.Intention!.CandidateId);
        // An absent payload is not an empty ballot that could replace an existing vote.
        Assert.Null(admission.CivicBallot);
    }

    [Theory]
    [InlineData("[]", 0)]
    [InlineData("[\"candidate:1\"]", 1)]
    [InlineData("[\"candidate:1\",\"candidate:2\",\"candidate:3\"]", 3)]
    public async Task PresentValidBallotRetainsItsChoicesIncludingExplicitAbstention(string ballot, int count)
    {
        using var handler = new ReplyHandler(BallotCandidate, ballot);
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));

        var admission = await runtime.RequestAndDecideAsync(Observation(BallotCandidate));

        Assert.True(admission.Accepted);
        Assert.False(admission.FellBack);
        Assert.NotNull(admission.CivicBallot);
        Assert.Equal(count, admission.CivicBallot.Count);
        Assert.Equal(Enumerable.Range(1, count).Select(index => $"candidate:{index}"), admission.CivicBallot);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("\"candidate:1\"")]
    [InlineData("[7]")]
    [InlineData("[{}]")]
    [InlineData("[null]")]
    [InlineData("[\"candidate:1\",\"candidate:2\",\"candidate:3\",\"candidate:4\"]")]
    public async Task MalformedNonNullBallotStillFailsProviderParsing(string ballot)
    {
        using var handler = new ReplyHandler("seek_food", ballot);
        using var client = new HttpClient(handler);
        var provider = Provider(client);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await provider.DecideAsync(new("request", provider.ProviderEpoch, Observation("seek_food"))));
    }

    [Fact]
    public async Task ParsedDuplicateChoicesStillCannotBecomeAnElectionVote()
    {
        using var handler = new ReplyHandler(BallotCandidate, "[\"candidate:1\",\"candidate:1\"]");
        using var client = new HttpClient(handler);
        var provider = Provider(client);
        var response = await provider.DecideAsync(new("request", provider.ProviderEpoch, Observation(BallotCandidate)));
        var adults = Enumerable.Range(1, 8).Select(index => $"candidate:{index}").ToArray();
        var governance = TownGovernanceState.Create(adults);
        foreach (var candidate in adults.Take(3))
            governance = TownGovernanceRules.Register(governance, candidate, true, null, adults, 0);
        governance = TownGovernanceRules.Advance(governance, "town:test", "synthetic-seed", adults, 0, 10);

        Assert.NotNull(response.CivicBallot);
        Assert.Equal(2, response.CivicBallot.Count);
        Assert.All(response.CivicBallot, choice => Assert.Equal("candidate:1", choice));
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.VoteElection(
            governance, governance.Election!.Id, adults[0], response.CivicBallot!, 1));
        Assert.Empty(governance.Election!.Ballots);
    }

    private static OpenAiCompatibleDecisionProvider Provider(HttpClient client) =>
        new(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model");

    private static InhabitantObservation Observation(string selectedCandidate) =>
        new("actor", 0, 0, 0, "digest", 8_000,
            [new("safe_idle", "Continue safely.", 0), new(selectedCandidate, "Choose this legal action.", 10)]);

    private sealed class ReplyHandler(string selectedCandidate, string? ballot) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var answer = new Dictionary<string, object?>
            {
                ["selected_candidate_id"] = selectedCandidate,
                ["confidence"] = 1,
            };
            if (ballot is not null) answer["civic_ballot"] = JsonSerializer.Deserialize<JsonElement>(ballot);
            var response = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = JsonSerializer.Serialize(answer) } } },
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            });
        }
    }
}
