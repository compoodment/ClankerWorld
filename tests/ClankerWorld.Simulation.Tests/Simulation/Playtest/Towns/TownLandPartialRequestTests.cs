using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandPartialRequestTests
{
    [Fact]
    public void CurrentRequestFormatCannotSilentlyDropItsHearingReceipts()
    {
        var encoded = JsonSerializer.Serialize(PartlyHeardRequest());
        Assert.Single(JsonSerializer.Deserialize<HouseholdLandUseRequest>(encoded)!.HearingResolutions);
        var tampered = JsonNode.Parse(encoded)!;
        Assert.True(tampered.AsObject().Remove("HearingResolutions"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HouseholdLandUseRequest>(tampered.ToJsonString()));
    }

    [Fact]
    public void CancellingTheSupersededCouncilVoteLeavesOnlyTheUnheardPlotPending()
    {
        var request = PartlyHeardRequest();
        var proposal = CancelledProposal(request);

        Assert.Null(HouseholdLandGrantRules.RefusalReason(request, ["adult"], proposal, 5));
        Assert.Equal(new[] { new GridPoint(1, 0) }, TownLandRightsRules.UnresolvedRequestTiles(request));
        Assert.False(HouseholdLandGrantRules.IsAvailable(request, [], [request]));
        Assert.NotNull(HouseholdLandGrantRules.RefusalReason(request with { HearingResolutions = [] },
            ["adult"], proposal, 5));
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("expiry")]
    [InlineData("no_adult")]
    public void APartialHearingDoesNotPreventTheRemainingRequestBeingRefused(string cause)
    {
        var request = PartlyHeardRequest();
        string[] adults = ["adult"];
        if (cause == "decline") request = request with { Consents = [new("adult", false, 5)] };
        if (cause == "expiry") request = request with { AgreedEndTick = 5 };
        if (cause == "no_adult") adults = [];

        Assert.NotNull(HouseholdLandGrantRules.RefusalReason(request, adults, CancelledProposal(request), 5));
        Assert.Single(request.HearingResolutions);
        Assert.Empty(request.GrantAdults);
    }

    private static HouseholdLandUseRequest PartlyHeardRequest() =>
        new("request", "town", "household", "adult", [new(0, 0), new(1, 0)], 0)
        {
            CouncilProposalId = "proposal",
            HearingResolutions = [new("request", "town", "case", "ruling", [new(0, 0)], 4)],
        };

    private static TownProposal CancelledProposal(HouseholdLandUseRequest request) =>
        new("proposal", HouseholdLandGrantRules.RequestKey(request), "land_use", "adult", request.Id,
            "The original plot request", "council:0", 0, 0, 10, ["adult"], 1, [], "cancelled", 4);
}
