using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandTransferRulesTests
{
    private static readonly GridPoint[] Plot = [new(1, 0), new(2, 0)];
    private static readonly string[] Agents = ["a", "a2", "b", "c", "new-adult"];
    private static readonly string[] Households = ["alpha", "beta", "gamma"];
    private static readonly TownLandTransferParty[] Parties = [new("alpha", "source", ["a", "a2"]), new("beta", "beneficiary", ["b"])];

    [Fact]
    public void TheCurrentLedgerContractRequiresTransfersWhileAnEmptyLedgerRoundtripsNormally()
    {
        var json = JsonSerializer.Serialize(TownLandHearingState.Create());
        var document = JsonNode.Parse(json)!.AsObject();
        Assert.Empty(document[nameof(TownLandHearingState.Transfers)]!.AsArray());
        Assert.Empty(JsonSerializer.Deserialize<TownLandHearingState>(json)!.Transfers);
        document.Remove(nameof(TownLandHearingState.Transfers));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TownLandHearingState>(document.ToJsonString()));
        document[nameof(TownLandHearingState.Transfers)] = null;
        var nullLedger = JsonSerializer.Deserialize<TownLandHearingState>(document.ToJsonString())!;
        Assert.Throws<InvalidDataException>(() => Validate(nullLedger, Rights(), TownGovernanceState.Create(Agents), 0));

        var (pending, council) = Proposed();
        council = TownGovernanceRules.LearnNotice(council, "a", "notice:1", 1);
        pending = Respond(pending, pending.Transfers[0], "a", "alpha", "accept", council, Parties);
        Assert.Single(pending.Transfers[0].Responses);
        var pendingDocument = JsonNode.Parse(JsonSerializer.Serialize(pending))!.AsObject();
        pendingDocument.Remove(nameof(TownLandHearingState.Transfers));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TownLandHearingState>(pendingDocument.ToJsonString()));
        Validate(pending, Rights(), council, 2);
    }

    [Fact]
    public void PublicationFilingAndSilenceSupplyNoConsentAndEveryAdultMustActuallyReadTheNotice()
    {
        var (state, council) = Proposed();
        var request = Assert.Single(state.Transfers);
        Assert.Empty(request.Responses);
        Assert.Empty(council.Knowledge);
        Assert.Same(state, TownLandTransferRules.Propose(state, Map(), "town", "b", Plot, "beta", Rights(), Parties, [], 0, "notice:unused"));
        Assert.Throws<InvalidOperationException>(() => Respond(state, request, "a", "alpha", "accept", council, Parties));
        council = TownGovernanceRules.LearnNotice(council, "a", request.NoticeId, 1);
        Assert.Throws<InvalidOperationException>(() => TownLandTransferRules.Respond(state, request.Id, "stale-terms", "a", "alpha", "accept",
            council.Knowledge, Rights(), Parties, [], 2));
        state = Respond(state, request, "a", "alpha", "accept", council, Parties);
        var (pending, rights) = Advance(state, council, Parties, 70);
        Assert.Equal("pending", Assert.Single(pending.Transfers).Status);
        Assert.Equal("a", Assert.Single(pending.Transfers[0].Responses).AgentId);
        Assert.Empty(pending.Adjustments);
        Assert.Equal(JsonSerializer.Serialize(Rights()), JsonSerializer.Serialize(rights));
        Validate(pending, rights, council, 70);
    }

    [Fact]
    public void AnAddedAdultsSeparateConsentCompletesOnlyTheNoticedPlotAndPreservesOriginalGrantTerms()
    {
        var (state, council) = Proposed();
        var original = Rights()[0];
        var token = TownLandTransferRules.TermsToken(state.Transfers[0]);
        foreach (var party in Parties)
            foreach (var adult in party.AdultIds)
            {
                council = TownGovernanceRules.LearnNotice(council, adult, "notice:1", 1);
                state = Respond(state, state.Transfers[0], adult, party.HouseholdId, "accept", council, Parties);
            }
        TownLandTransferParty[] grown = [Parties[0] with { AdultIds = ["a", "a2", "new-adult"] }, Parties[1]];
        var (pending, unchanged) = Advance(state, council, grown, 3);
        Assert.Equal("pending", pending.Transfers[0].Status);
        Assert.Equal(token, TownLandTransferRules.TermsToken(pending.Transfers[0]));
        Assert.Equal(2, TownLandTransferRules.AcceptedAdults(pending.Transfers[0], grown[0], council.Knowledge, 3).Count);
        Assert.Throws<InvalidOperationException>(() => Respond(pending, pending.Transfers[0], "new-adult", "alpha", "accept", council, grown));
        council = TownGovernanceRules.LearnNotice(council, "new-adult", "notice:1", 3);
        state = TownLandTransferRules.Respond(pending, pending.Transfers[0].Id, token, "new-adult", "alpha", "accept", council.Knowledge,
            unchanged, grown, [], 4);
        var (completed, rights) = Advance(state, council, grown, 4);
        var request = Assert.Single(completed.Transfers);
        Assert.Equal("transferred", request.Status);
        Assert.Equal(3, Assert.Single(request.Receipt!.Parties, party => party.HouseholdId == "alpha").AdultIds.Count);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(Assert.Single(completed.OriginalRights).Right));
        Assert.Equal("beta", Assert.Single(rights, right => right.Tiles.Contains(new GridPoint(1, 0))).HouseholdId);
        Assert.Equal("alpha", Assert.Single(rights, right => right.Tiles.Contains(new GridPoint(0, 0))).HouseholdId);
        Assert.Equal("alpha", Assert.Single(rights, right => right.Tiles.Contains(new GridPoint(3, 0))).HouseholdId);
        Assert.All(rights.Where(right => right.GrantSource == "grant:alpha"), right =>
        {
            Assert.Equal(80, right.AgreedEndTick);
            Assert.Equal(0, right.GrantedTick);
        });
        Assert.Equal(JsonSerializer.Serialize(Rights()[1]), JsonSerializer.Serialize(Assert.Single(rights, right => right.Id == "right:beta")));
        Assert.Equal(JsonSerializer.Serialize(Rights()), JsonSerializer.Serialize(TownLandHearingRules.OriginalGrantRights(completed, rights)));
        Assert.Equal(JsonSerializer.Serialize(rights), JsonSerializer.Serialize(TownLandHearingRules.ApplyAdjustments(completed, Rights())));
        Validate(completed, rights, council, 4);
    }

    [Fact]
    public void APlotWithTwoSourceHouseholdsRequiresBothHouseholdsAndKeepsTheirDifferentEnds()
    {
        GridPoint[] plot = [new(3, 0), new(4, 0)];
        TownLandTransferParty[] parties = [Parties[0], new("beta", "source", ["b"]), new("gamma", "beneficiary", ["c"])];
        var state = TownLandTransferRules.Propose(TownLandHearingState.Create(), Map(), "town", "c", plot, "gamma", Rights(), parties, [], 0, "notice:1");
        var request = state.Transfers[0];
        var council = TownGovernanceRules.PostNotice(TownGovernanceState.Create(Agents), "land_transfer", TownLandTransferRules.TermsToken(request),
            "Transfer the existing permissions from both source households to gamma", 0);
        foreach (var party in parties.Where(party => party.HouseholdId != "beta"))
            foreach (var adult in party.AdultIds)
            {
                council = TownGovernanceRules.LearnNotice(council, adult, "notice:1", 1);
                state = Respond(state, request, adult, party.HouseholdId, "accept", council, parties);
            }
        var (pending, _) = Advance(state, council, parties, 3);
        Assert.Equal("pending", pending.Transfers[0].Status);
        Assert.Empty(pending.Adjustments);
        council = TownGovernanceRules.LearnNotice(council, "b", "notice:1", 3);
        state = Respond(pending, request, "b", "beta", "accept", council, parties, 3);
        var (completed, rights) = Advance(state, council, parties, 3);
        Assert.Equal("transferred", completed.Transfers[0].Status);
        Assert.Equal(("gamma", (long?)80), (rights.Single(right => right.Tiles.Contains(new GridPoint(3, 0))).HouseholdId,
            rights.Single(right => right.Tiles.Contains(new GridPoint(3, 0))).AgreedEndTick));
        Assert.Equal(("gamma", (long?)100), (rights.Single(right => right.Tiles.Contains(new GridPoint(4, 0))).HouseholdId,
            rights.Single(right => right.Tiles.Contains(new GridPoint(4, 0))).AgreedEndTick));
        Assert.Equal("alpha", rights.Single(right => right.Tiles.Contains(new GridPoint(2, 0))).HouseholdId);
        Assert.Equal("beta", rights.Single(right => right.Tiles.Contains(new GridPoint(5, 0))).HouseholdId);
        Validate(completed, rights, council, 3);
    }

    [Fact]
    public void MovingHouseholdsCannotReuseSourceConsentAsBeneficiaryConsentAndAnEmptyPartyCannotSettle()
    {
        var (state, council) = Proposed();
        foreach (var party in Parties)
            foreach (var adult in party.AdultIds)
            {
                council = TownGovernanceRules.LearnNotice(council, adult, "notice:1", 1);
                state = Respond(state, state.Transfers[0], adult, party.HouseholdId, "accept", council, Parties);
            }
        TownLandTransferParty[] moved = [Parties[0] with { AdultIds = ["a2"] }, Parties[1] with { AdultIds = ["a", "b"] }];
        Assert.Equal("b", Assert.Single(TownLandTransferRules.AcceptedAdults(state.Transfers[0], moved[1], council.Knowledge, 3)));
        var (pending, _) = Advance(state, council, moved, 3);
        Assert.Equal("pending", pending.Transfers[0].Status);
        state = Respond(pending, pending.Transfers[0], "a", "beta", "accept", council, moved, 3);
        TownLandTransferParty[] unrepresented = [moved[0] with { AdultIds = [] }, moved[1]];
        (pending, _) = Advance(state, council, unrepresented, 3);
        Assert.Equal("pending", pending.Transfers[0].Status);
        Assert.Empty(pending.Adjustments);
        var (completed, rights) = Advance(state, council, moved, 3);
        Assert.Equal("transferred", completed.Transfers[0].Status);
        Assert.Contains(completed.Transfers[0].Responses, response => response.AgentId == "a" && response.HouseholdId == "alpha");
        Assert.Contains(completed.Transfers[0].Responses, response => response.AgentId == "a" && response.HouseholdId == "beta");
        Validate(completed, rights, council, 3);
    }

    [Fact]
    public void ADeclineOrTheFilersWithdrawalPreservesEarlierResponsesAndMovesNoPermissions()
    {
        var (state, council) = Proposed();
        council = TownGovernanceRules.LearnNotice(council, "a", "notice:1", 1);
        council = TownGovernanceRules.LearnNotice(council, "b", "notice:1", 1);
        state = Respond(state, state.Transfers[0], "a", "alpha", "accept", council, Parties);
        var rejected = Respond(state, state.Transfers[0], "b", "beta", "decline", council, Parties);
        Assert.Equal("rejected", rejected.Transfers[0].Status);
        Assert.Collection(rejected.Transfers[0].Responses, response => Assert.Equal("accept", response.Kind), response => Assert.Equal("decline", response.Kind));
        Assert.Empty(rejected.Adjustments);
        Validate(rejected, Rights(), council, 2);
        Assert.Throws<InvalidOperationException>(() => TownLandTransferRules.Withdraw(state, state.Transfers[0].Id, "b", 2));
        var withdrawn = TownLandTransferRules.Withdraw(state, state.Transfers[0].Id, "a", 2);
        Assert.Equal("withdrawn", withdrawn.Transfers[0].Status);
        Assert.Single(withdrawn.Transfers[0].Responses);
        Assert.Empty(withdrawn.Adjustments);
        Validate(withdrawn, Rights(), council, 2);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("disputed")]
    [InlineData("hearing")]
    [InlineData("unrecorded")]
    public void ProvisionalDisputedOrUnrecordedLandCannotUseTheConsensualRoute(string blocker)
    {
        var state = TownLandHearingState.Create();
        var rights = Rights();
        HouseholdLandUseRequest[] claims = blocker == "disputed" ? [new("claim", "town", "gamma", "c", Plot, 0)] : [];
        if (blocker == "expired") rights = rights.Select(right => right.Id == "right:alpha" ? right with { AgreedEndTick = 0 } : right).ToArray();
        if (blocker == "unrecorded") rights = rights.Where(right => right.Id != "right:alpha").ToArray();
        if (blocker == "hearing") state = TownLandHearingRules.File(state, "town", new("a", "dispute", "Review this permission", new("confirm"), 0),
            Plot, rights, [new("alpha", "household", "alpha", "town", ["a", "a2"])], 0, 10, "notice:hearing");
        Assert.False(TownLandTransferRules.CanPropose(state, Map(), "town", "a", Plot, "beta", rights, Parties, claims, 0));
        Assert.Throws<InvalidOperationException>(() => TownLandTransferRules.Propose(state, Map(), "town", "a", Plot, "beta", rights, Parties, claims, 0, "notice:1"));
        Assert.Empty(state.Transfers);
        Assert.Empty(state.Adjustments);
    }

    [Fact]
    public void AChangedExactPermissionInvalidatesTheProposalWithoutRewritingItsTermsOrConsents()
    {
        var (state, council) = Proposed();
        council = TownGovernanceRules.LearnNotice(council, "a", "notice:1", 1);
        state = Respond(state, state.Transfers[0], "a", "alpha", "accept", council, Parties);
        var request = state.Transfers[0];
        GridPoint[] footprint = [new(0, 0)];
        var changed = TownLandRightsRules.ReassignFootprintRights(Map(), Rights(), footprint.ToHashSet(), "beta", 3);
        state = TownLandHearingRules.RecordBuildingTransfer(state, Map(), "existing-house", footprint, "beta", Rights(), changed, 3);
        var (invalidated, rights) = TownLandTransferRules.Advance(state, Map(), changed,
            new Dictionary<string, IReadOnlyList<TownLandTransferParty>> { [request.Id] = Parties }, [], council.Knowledge, 3);
        var saved = invalidated.Transfers[0];
        Assert.Equal("invalidated", saved.Status);
        Assert.Equal("rights_changed", saved.Reason);
        Assert.Equal(request.RightVersions, saved.RightVersions);
        Assert.Equal(request.Responses, saved.Responses);
        Assert.Null(saved.Receipt);
        Assert.Equal("building_transfer", Assert.Single(invalidated.Adjustments).Kind);
        Assert.Equal(JsonSerializer.Serialize(changed), JsonSerializer.Serialize(rights));
        Validate(invalidated, rights, council, 3);
    }

    [Fact]
    public void ReloadValidationRejectsMissingAdultConsentInventedReceiptsAndExtendedTerms()
    {
        var (state, council) = Proposed();
        foreach (var party in Parties)
            foreach (var adult in party.AdultIds)
            {
                council = TownGovernanceRules.LearnNotice(council, adult, "notice:1", 1);
                state = Respond(state, state.Transfers[0], adult, party.HouseholdId, "accept", council, Parties);
            }
        var (completed, rights) = Advance(state, council, Parties, 3);
        Validate(completed, rights, council, 3);
        var roundtrip = JsonSerializer.Deserialize<TownLandHearingState>(JsonSerializer.Serialize(completed))!;
        Validate(roundtrip, rights, council, 3);
        var missing = completed with { Transfers = [completed.Transfers[0] with { Responses = completed.Transfers[0].Responses.Where(response => response.AgentId != "a2").ToArray() }] };
        Assert.Throws<InvalidDataException>(() => Validate(missing, rights, council, 3));
        var invented = completed with { Transfers = [completed.Transfers[0] with { Receipt = completed.Transfers[0].Receipt! with { AdjustmentId = "invented" } }] };
        Assert.Throws<InvalidDataException>(() => Validate(invented, rights, council, 3));
        var extended = completed with
        {
            Adjustments = [completed.Adjustments[0] with
            { ResultRights = completed.Adjustments[0].ResultRights.Select(right => right with { AgreedEndTick = 500 }).ToArray() }]
        };
        Assert.Throws<InvalidDataException>(() => Validate(extended, rights, council, 3));
        var expiredClosure = completed with
        {
            Transfers = [completed.Transfers[0] with
            { SettledTick = 80, Receipt = completed.Transfers[0].Receipt! with { Tick = 80 } }],
            Adjustments = [completed.Adjustments[0] with { Tick = 80 }]
        };
        Assert.Throws<InvalidDataException>(() => Validate(expiredClosure, rights, council, 80));
    }

    private static (TownLandHearingState State, TownGovernanceState Council) Proposed()
    {
        var state = TownLandTransferRules.Propose(TownLandHearingState.Create(), Map(), "town", "a", Plot, "beta", Rights(), Parties, [], 0, "notice:1");
        var council = TownGovernanceRules.PostNotice(TownGovernanceState.Create(Agents), "land_transfer", TownLandTransferRules.TermsToken(state.Transfers[0]),
            "Transfer only the noticed existing permission to beta; every source and beneficiary adult must agree", 0);
        return (state, council);
    }
    private static TownLandHearingState Respond(TownLandHearingState state, TownLandTransferRequest request, string actor, string household,
        string kind, TownGovernanceState council, IReadOnlyList<TownLandTransferParty> parties, long tick = 2) =>
        TownLandTransferRules.Respond(state, request.Id, TownLandTransferRules.TermsToken(request), actor, household, kind, council.Knowledge, Rights(), parties, [], tick);
    private static (TownLandHearingState State, IReadOnlyList<HouseholdLandUseRight> Rights) Advance(TownLandHearingState state,
        TownGovernanceState council, IReadOnlyList<TownLandTransferParty> parties, long tick) => TownLandTransferRules.Advance(state, Map(), Rights(),
        state.Transfers.ToDictionary(request => request.Id, _ => parties, StringComparer.Ordinal), [], council.Knowledge, tick);
    private static HouseholdLandUseRight[] Rights() =>
        [new("right:alpha", "town", "alpha", [new(0, 0), new(1, 0), new(2, 0), new(3, 0)], 0, "grant:alpha", 80),
         new("right:beta", "town", "beta", [new(4, 0), new(5, 0)], 0, "grant:beta", 100)];
    private static SeededMap Map() => new(8, 2, 0,
        (from y in Enumerable.Range(0, 2) from x in Enumerable.Range(0, 8) select new TerrainTile(new(x, y), TerrainKind.Meadow)).ToArray(), [], [], "transfer-fixture");
    private static void Validate(TownLandHearingState state, IReadOnlyList<HouseholdLandUseRight> rights, TownGovernanceState council, long tick) =>
        TownLandHearingValidation.Validate(Map(), tick, "town", state, rights,
            [new("title", "town", [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0), new(5, 0)], 0)],
            Agents.ToHashSet(StringComparer.Ordinal), Households.ToHashSet(StringComparer.Ordinal), council, 10);
}
