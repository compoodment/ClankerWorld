using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Tests;

public sealed class CivicLandClaimProviderTests
{
    private const string Candidate = "civic|town:test|claim_land||";

    [Theory]
    [InlineData(null, 0)]
    [InlineData("null", 0)]
    [InlineData("[{\"x\":4,\"y\":5},{\"x\":5,\"y\":5}]", 2)]
    public async Task AdmittedClaimPreservesExactCoordinatesAndAnAbsentPayloadIsNotAPlot(string? payload, int count)
    {
        using var handler = new ReplyHandler(payload);
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));
        var admission = await runtime.RequestAndDecideAsync(Observation());
        Assert.True(admission.Accepted);
        Assert.False(admission.FellBack);
        Assert.Equal(Candidate, admission.Intention!.CandidateId);
        Assert.Equal(count, admission.CivicLandTiles?.Count ?? 0);
        if (count > 0) Assert.Equal([new CognitionLandTile(4, 5), new CognitionLandTile(5, 5)], admission.CivicLandTiles);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[7]")]
    [InlineData("[{\"x\":1}]")]
    [InlineData("[{\"x\":1,\"y\":\"2\"}]")]
    [InlineData("[{\"x\":1.5,\"y\":2}]")]
    [InlineData("[{\"x\":2147483648,\"y\":2}]")]
    public async Task MalformedClaimIsRejectedAsProviderData(string payload)
    {
        using var handler = new ReplyHandler(payload);
        using var client = new HttpClient(handler);
        var provider = Provider(client);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await provider.DecideAsync(new("request", provider.ProviderEpoch, Observation())));
    }

    [Fact]
    public async Task OversizedClaimIsRejectedBeforeAdmission()
    {
        using var handler = new ReplyHandler(JsonSerializer.Serialize(Enumerable.Range(0, 65).Select(x => new { x, y = 0 })));
        using var client = new HttpClient(handler);
        var runtime = new CognitionRuntime("actor", Provider(client));
        var admission = await runtime.RequestAndDecideAsync(Observation());
        Assert.True(admission.FellBack);
        Assert.Null(admission.CivicLandTiles);
    }

    private static OpenAiCompatibleDecisionProvider Provider(HttpClient client) =>
        new(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model");

    private static InhabitantObservation Observation() =>
        new("actor", 0, 0, 0, "digest", 8_000,
            [new("safe_idle", "Continue safely.", 0), new(Candidate, "Request a land claim.", 190)]);

    private sealed class ReplyHandler(string? payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var answer = new Dictionary<string, object?> { ["selected_candidate_id"] = Candidate, ["confidence"] = 1 };
            if (payload is not null) answer["civic_land_tiles"] = JsonSerializer.Deserialize<JsonElement>(payload);
            var response = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = JsonSerializer.Serialize(answer) } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        }
    }
}
