using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandHearingRuntimeTests
{
    private const string Judge = "founder:00000000000000000000000000000001";
    private const string Filer = "founder:00000000000000000000000000000003";
    private const string Waiver = "founder:00000000000000000000000000000004";
    private const int Day = 40;

    [Fact]
    public async Task FreshPersonalTurnsPublishHearAndConfirmHouseholdCaseWithoutChangingPrivateProperty()
    {
        var provider = new HearingProvider();
        using var world = NewWorld(provider);
        var generated = world.ExportState();
        var household = generated.Society.Society.GetInhabitant(Filer).HouseholdId!;
        var right = generated.HouseholdLandUseRights!.Where(item => item.HouseholdId == household)
            .OrderBy(item => item.Id, StringComparer.Ordinal).First();
        provider.Plot = right.Tiles.ToArray();

        // Government authority is created by the same admitted personal turns as a normal mayor election.
        await UntilAsync(world, () => world.Towns[0].Government!.Offices.Any(), Day * 3, provider);
        var office = Assert.Single(world.Towns[0].Government!.Offices);
        Assert.Equal(Judge, office.HolderId);
        Assert.Equal("land", office.Mandates);
        var property = PrivateProperty(world.ExportState());
        var permissions = Permissions(world.ExportState());
        provider.HearingsEnabled = true;
        await UntilAsync(world, () => world.Towns[0].LandHearings.Cases.Count > 0, 20, provider);

        var opened = Assert.Single(world.Towns[0].LandHearings.Cases);
        var revision = Assert.Single(opened.Revisions);
        Assert.Equal("pending", opened.Status);
        Assert.Equal(right.Tiles, revision.Tiles);
        Assert.Equal(revision.PublishedTick + Day, revision.DeadlineTick);
        Assert.Equal(Filer, Assert.Single(opened.Filings).AgentId);
        Assert.Equal("confirm", revision.RequestedOutcome.Kind);
        var governance = world.Towns[0].Governance!;
        var notice = Assert.Single(governance.Notices, item => item.Id == revision.NoticeId);
        Assert.Equal("land_hearing", notice.Kind);
        Assert.Equal(opened.Id + ":1", notice.SubjectId);
        Assert.Contains(governance.Knowledge, receipt => receipt.NoticeId == notice.Id && receipt.AgentId == Filer);
        Assert.DoesNotContain(governance.Knowledge, receipt => receipt.NoticeId == notice.Id && receipt.AgentId == Judge);
        Assert.Empty(opened.Responses);

        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var replayProvider = new HearingProvider { HearingsEnabled = true, Plot = provider.Plot };
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => replayProvider);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var step = 0; step < Day * 2 && world.Towns[0].LandHearings.Cases[0].Status != "settled"; step++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }

        var settled = Assert.Single(world.Towns[0].LandHearings.Cases);
        Assert.Equal("settled", settled.Status);
        var ruling = Assert.Single(settled.Rulings);
        Assert.Equal(1, ruling.Revision);
        Assert.Equal("confirm", ruling.Outcome.Kind);
        Assert.Equal(Judge, ruling.Judge.AgentId);
        Assert.Equal("land_mayor", ruling.Judge.Kind);
        Assert.Equal(office.ElectionId, ruling.Judge.AuthorityId);
        Assert.True(ruling.Tick < revision.DeadlineTick);
        Assert.Empty(ruling.AdjustmentIds);
        Assert.Empty(world.Towns[0].LandHearings.Adjustments);
        Assert.Contains(settled.Responses, response => response.Revision == 1 && response.AgentId == Filer && response.Kind == "answer");
        Assert.Contains(settled.Responses, response => response.Revision == 1 && response.AgentId == Waiver && response.Kind == "waive");
        Assert.Equal(new[] { Filer, Waiver }, settled.Responses.Select(response => response.AgentId).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(settled.Reads, read => read.AgentId == Judge && read.Revision == 1 && ruling.EvidenceIds.All(read.EvidenceIds.Contains));
        Assert.NotEmpty(ruling.EvidenceIds);
        Assert.All(ruling.EvidenceIds, id => Assert.Contains(settled.Evidence, evidence => evidence.Id == id && evidence.Kind == "record"));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "land_case_ruling");
        foreach (var action in new[] { "hearing_file", "read", "hearing_inspect", "hearing_answer", "hearing_waive", "hearing_rule" })
            Assert.Contains(provider.Selected, item => item.Contains("|" + action + "|", StringComparison.Ordinal));
        Assert.Equal(permissions, Permissions(world.ExportState()));
        Assert.Equal(property, PrivateProperty(world.ExportState()));
        Assert.Equal(generated.Towns![0].ResidentIds, world.Towns[0].ResidentIds);
        world.Validate();
        replay.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using (var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), _ => new HearingProvider { HearingsEnabled = true, Plot = provider.Plot }))
        {
            restored.Validate();
            Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            Assert.Equal(property, PrivateProperty(restored.ExportState()));
        }
        AssertMalformedHearingSavesRefused(final);
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task ActualPermissionExpiryPublishesUnlearnedCaseAndRetainsProvisionalPermissionAcrossPauseAndReload()
    {
        using var generated = NormalPathWorld.CreateGenerated("government-personal-path", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var original = state.HouseholdLandUseRights![0];
        var ending = original with { AgreedEndTick = 1 };
        // A near-event term fixture changes only an existing permission's agreed expiry.
        // No Council decision, title, physical property, founder placement or hearing is fabricated.
        using var world = PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRights = state.HouseholdLandUseRights.Select(right => right.Id == original.Id ? ending : right).ToArray()
        }, _ => new ActionCoverageRecorder(chooseIdle: true));
        var permissions = Permissions(world.ExportState());
        var property = PrivateProperty(world.ExportState());

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var item = Assert.Single(world.Towns[0].LandHearings.Cases);
        var revision = Assert.Single(item.Revisions);
        Assert.Equal("pending", item.Status);
        Assert.Equal("expiry", item.Kind);
        Assert.Equal(original.Id, Assert.Single(item.Filings).AuthorityId);
        Assert.Null(item.Filings[0].AgentId);
        Assert.Equal(1, revision.PublishedTick);
        Assert.Equal(1 + state.Society.Society.Config.TicksPerWorldDay, revision.DeadlineTick);
        Assert.Equal(original.Tiles, revision.Tiles);
        Assert.Contains(world.Towns[0].Governance!.Notices, notice => notice.Id == revision.NoticeId && notice.Kind == "land_hearing");
        Assert.DoesNotContain(world.Towns[0].Governance!.Knowledge, receipt => receipt.NoticeId == revision.NoticeId);
        Assert.Empty(item.Responses);
        Assert.Empty(item.Rulings);
        Assert.Equal(permissions, Permissions(world.ExportState()));
        Assert.Equal(property, PrivateProperty(world.ExportState()));
        world.Validate();
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(paused), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(permissions, Permissions(restored.ExportState()));
        Assert.Equal(property, PrivateProperty(restored.ExportState()));
    }

    [Fact]
    public async Task OtherPersonalProviderCannotFileTheOfferedHouseholdCase()
    {
        var provider = new HearingProvider(DecisionProviderKind.Jev) { HearingsEnabled = true };
        using var world = NewWorld(provider);
        var original = world.ExportState();
        var household = original.Society.Society.GetInhabitant(Filer).HouseholdId;
        provider.Plot = original.HouseholdLandUseRights!.Where(item => item.HouseholdId == household)
            .OrderBy(item => item.Id, StringComparer.Ordinal).First().Tiles.ToArray();
        var acceptedFileChoice = false;
        for (var step = 0; step < 4; step++)
        {
            var result = await world.AdvanceOneTickAsync();
            Assert.True(result.Advanced);
            acceptedFileChoice |= result.Decisions.Any(decision => decision.InhabitantId == Filer &&
                decision.Admission.Accepted && !decision.Admission.FellBack &&
                decision.Admission.Intention is { Provider: DecisionProviderKind.Jev } intention &&
                intention.CandidateId.Contains("|hearing_file|", StringComparison.Ordinal));
        }
        Assert.Contains(provider.Selected, item => item.Contains("|hearing_file|", StringComparison.Ordinal));
        Assert.True(acceptedFileChoice);
        Assert.Empty(world.Towns[0].LandHearings.Cases);
        Assert.Equal(Permissions(original), Permissions(world.ExportState()));
        Assert.Equal(PrivateProperty(original), PrivateProperty(world.ExportState()));
        world.Validate();
    }

    [Fact]
    public async Task ASettledCaseAsksNothingMoreOnceItsRulingIsReadAndAnUnchangedLedgerIsNotRewritten()
    {
        var settled = await SettledDispute.Value;
        var provider = new HearingProvider { Plot = settled.Plot, InspectWhenOffered = true };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(settled.Save), _ => provider);
        var adults = world.Towns[0].Governance!.Members;
        Assert.Contains(Judge, adults);
        Assert.Contains(Filer, adults);
        // Every adult stands at the notice place, is prompted for a fresh personal turn and reads whatever case file is offered.
        foreach (var adult in adults) Prompt(world, adult, "Read the land ruling posted at the notice place.");
        for (var step = 0; step < 8; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var item = Assert.Single(world.Towns[0].LandHearings.Cases);
        var ruling = Assert.Single(item.Rulings);
        Assert.Equal("settled", item.Status);
        Assert.All(adults, adult => Assert.Single(item.Reads, read => read.AgentId == adult && read.ReadTick > ruling.Tick));
        var ledger = world.Towns[0].LandHearings;
        for (var step = 0; step < 3; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Same(ledger, world.Towns[0].LandHearings);

        // The closed case now offers its parties only the grounded rehearing request, and other residents nothing.
        var about = "|" + TownLandHearingRules.RevisionToken(item) + "|";
        string[] Asked(string adult) => provider.Offered[adult].Where(id => id.Contains(about, StringComparison.Ordinal))
            .Select(id => id.Split('|')[2] + ":" + id.Split('|')[4]).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["hearing_reopen:material_evidence", "hearing_reopen:procedural_error"], Asked(Filer));
        Assert.Equal(Asked(Filer), Asked(Waiver));
        Assert.All(adults.Where(adult => adult is not (Filer or Waiver)), adult => Assert.Empty(Asked(adult)));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task AfterEveryoneReadsTheRulingAPartyCanStillRecordANewObservationAndReopenTheCaseOnIt()
    {
        var settled = await SettledDispute.Value;
        var state = PrivateWorldRuntimeCodec.Decode(settled.Save);
        var board = state.Towns![0].OriginSite!.Value;
        // The filer stands where both the notice place and a tile of the settled plot are within reach.
        var spot = new[] { board, new GridPoint(board.X + 1, board.Y), new GridPoint(board.X - 1, board.Y), new GridPoint(board.X, board.Y + 1), new GridPoint(board.X, board.Y - 1) }
            .Select(state.Map.WrapColumn).First(point => state.Map.IsLand(point) && state.Map.FootDistance(point, board) <= 1 &&
                settled.Plot.Any(tile => state.Map.FootDistance(point, tile) <= 1));
        var provider = new HearingProvider { Plot = settled.Plot, InspectWhenOffered = true, AcceptRehearings = true };
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Filer ? person with { Position = spot } : person).ToArray()
        }, _ => provider);
        var adults = world.Towns[0].Governance!.Members;
        foreach (var adult in adults) Prompt(world, adult, "Read the land ruling posted at the notice place.");
        for (var step = 0; step < 8; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var item = Assert.Single(world.Towns[0].LandHearings.Cases);
        Assert.Equal("settled", item.Status);
        Assert.All(adults, adult => Assert.Contains(item.Reads, read => read.AgentId == adult && read.ReadTick > item.Rulings[0].Tick));
        Assert.DoesNotContain(item.Evidence, evidence => evidence.Kind == "observation");
        var token = "|" + TownLandHearingRules.RevisionToken(item) + "|";
        // Having read the ruling, the filer can still record what is on the plot; a resident who is not a party cannot.
        Assert.Contains(provider.Offered[Filer], id => id.Contains("|hearing_observe" + token, StringComparison.Ordinal));
        Assert.DoesNotContain(provider.Offered[Judge], id => id.Contains(token, StringComparison.Ordinal));

        // A recorded permission on the plot changes, so its file is offered again to read the new version.
        var changedState = world.ExportState();
        changedState = changedState with
        {
            HouseholdLandUseRights = changedState.HouseholdLandUseRights!.Select(right => right.Id == settled.RightId
                ? right with { AgreedEndTick = settled.End + Day } : right).ToArray()
        };
        using var changed = PrivateWorldRuntime.Restore(changedState, _ => provider);
        Prompt(changed, Judge, "Look at the notice place again.");
        Assert.True((await changed.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(changed.Towns[0].LandHearings.Cases[0].Evidence, evidence => evidence.Kind == "record" &&
            evidence.SourceRecordId == settled.RightId && evidence.Text.Contains(" agreed end " + (settled.End + Day) + ".", StringComparison.Ordinal));

        // The filer observes the plot, reads the updated file, cites the observation and the land mayor accepts it.
        provider.ObserveAndReopen = true;
        Prompt(world, Filer, "Look at your household's plot.");
        await UntilAsync(world, () => world.Towns[0].LandHearings.Cases[0].Evidence.Any(evidence => evidence.Kind == "observation"), 10, provider);
        Prompt(world, Filer, "Read the case file again and consider whether the ruling should be heard again.");
        await UntilAsync(world, () => world.Towns[0].LandHearings.Cases[0].Status == "pending", 30, provider);
        var reopened = world.Towns[0].LandHearings.Cases[0];
        var request = Assert.Single(reopened.ReopenRequests);
        Assert.Equal(("material_evidence", "accepted", Filer), (request.Kind, request.Status, request.AgentId));
        var observed = Assert.Single(reopened.Evidence, evidence => evidence.Kind == "observation");
        Assert.Equal(new[] { observed.Id }, request.EvidenceIds);
        Assert.True(observed.SubmittedTick > reopened.Rulings[0].Tick);
        Assert.Equal(2, reopened.Revisions.Count);
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task APermissionEndingAfterItsDisputeWasSettledStillGetsAReviewThatMustRenewAmendOrEndIt()
    {
        var settled = await SettledDispute.Value;
        var provider = new HearingProvider { Plot = settled.Plot };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(settled.Save), _ => provider);
        var permissions = Permissions(world.ExportState());
        while (world.WorldTick < settled.End) Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        // The dispute was ruled before the agreed end, so that ruling never reviewed the expiry.
        Assert.Equal(2, world.Towns[0].LandHearings.Cases.Count);
        var dispute = world.Towns[0].LandHearings.Cases[0];
        var review = world.Towns[0].LandHearings.Cases[1];
        Assert.Equal(("dispute", "settled"), (dispute.Kind, dispute.Status));
        Assert.True(Assert.Single(dispute.Rulings).Tick < settled.End);
        Assert.Equal(("expiry", "pending", settled.End), (review.Kind, review.Status, review.FiledTick));
        Assert.Equal(settled.RightId, Assert.Single(review.Filings).AuthorityId);
        Assert.Equal(settled.Plot, TownLandHearingRules.CurrentRevision(review).Tiles);
        Assert.Equal(permissions, Permissions(world.ExportState()));

        // Both household adults respond and the land mayor reads the file. The lapsed permission cannot be left as it is.
        provider.HearingsEnabled = true;
        var rule = "|hearing_rule|" + TownLandHearingRules.RevisionToken(review) + "|";
        await UntilAsync(world, () => provider.Offered.TryGetValue(Judge, out var offered) &&
            offered.Any(id => id.Contains(rule, StringComparison.Ordinal)), 20, provider);
        Assert.Equal(["amend", "end", "renew"], provider.Offered[Judge].Where(id => id.Contains(rule, StringComparison.Ordinal))
            .Select(id => id.Split('|')[4]).Order(StringComparer.Ordinal));
        Assert.Equal("pending", world.Towns[0].LandHearings.Cases[1].Status);
        Assert.Equal(permissions, Permissions(world.ExportState()));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task ARehearingRequestIsAssessedOnlyWhileItsCaseIsSettled()
    {
        var settled = await SettledDispute.Value;
        var state = PrivateWorldRuntimeCodec.Decode(settled.Save);
        var town = Assert.Single(state.Towns!);
        var item = Assert.Single(town.LandHearings.Cases);
        var tick = state.Society.Society.WorldTick;
        Assert.True(tick > item.Rulings[0].Tick);
        // Two parties ask for a rehearing over the same new observation. These are ordinary case records made through the rules.
        var observation = new TownLandEvidence("land-evidence:new-boundary-marker", 1, "observation", "firsthand", Filer, null, null,
            tick, Filer, tick, "Personally observed nearby plot: a new boundary marker stands on it.");
        var ledger = TownLandHearingRules.AddEvidence(town.LandHearings, item.Id, 1, observation, town.Governance!.Knowledge);
        ledger = TownLandHearingRules.RequestReopen(ledger, item.Id, Filer, "material_evidence", [observation.Id], "A new marker changes the facts", tick);
        ledger = TownLandHearingRules.RequestReopen(ledger, item.Id, Waiver, "material_evidence", [observation.Id], "The same new marker", tick);
        var provider = new HearingProvider { Plot = settled.Plot, InspectWhenOffered = true, AcceptRehearings = true };
        using var world = PrivateWorldRuntime.Restore(state with { Towns = [town with { LandHearings = ledger }] }, _ => provider);

        await UntilAsync(world, () => world.Towns[0].LandHearings.Cases[0].Status == "pending", 20, provider);
        var reopened = world.Towns[0].LandHearings.Cases[0];
        Assert.Equal(["accepted", "pending"], reopened.ReopenRequests.Select(request => request.Status));
        Assert.Equal(2, reopened.Revisions.Count);
        var waiting = reopened.ReopenRequests[1];
        // The mayor reads the fresh notice's file, which still lists the other request.
        await UntilAsync(world, () => world.Towns[0].LandHearings.Cases[0].Reads.Any(read => read.AgentId == Judge && read.Revision == 2 &&
            read.ReopenRequestIds.Contains(waiting.Id, StringComparer.Ordinal)), 20, provider);
        for (var step = 0; step < 3; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        // While the rehearing is under way that request cannot be decided, so it is neither offered nor attempted.
        Assert.DoesNotContain(provider.Offered[Judge], id => id.Contains("|hearing_assess_reopen|", StringComparison.Ordinal));
        Assert.DoesNotContain(provider.Selected, selected => selected.StartsWith(Judge, StringComparison.Ordinal) &&
            selected.Contains(waiting.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(world.ExportState().Events, entry => entry.Kind == "town_civic_action_rejected" &&
            entry.Detail.EndsWith("|hearing_assess_reopen", StringComparison.Ordinal));
        var current = world.Towns[0].LandHearings.Cases[0];
        Assert.Equal(("pending", "pending"), (current.Status, current.ReopenRequests[1].Status));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task ACouncilVoteStandsWhenItsTownHearingCanNoLongerOpenAndAnOverlappingProposalIsRefusedWhenMade()
    {
        var provider = new HearingProvider { SeekMayor = false };
        using var generated = NewWorld(provider);
        var initial = generated.ExportState();
        var household = initial.Society.Society.GetInhabitant(Filer).HouseholdId!;
        var lapsing = initial.HouseholdLandUseRights!.Where(right => right.HouseholdId == household && right.Tiles.Count > 1)
            .OrderBy(right => right.Id, StringComparer.Ordinal).First();
        var other = initial.HouseholdLandUseRights!.Where(right => right.HouseholdId != household)
            .OrderBy(right => right.Id, StringComparer.Ordinal).First();
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            HouseholdLandUseRights = initial.HouseholdLandUseRights!.Select(right => right.Id == lapsing.Id ? right with { AgreedEndTick = 1 } : right).ToArray()
        }, _ => provider);
        await UntilAsync(world, () => world.Towns[0].LandHearings.Cases.Count == 1, 3, provider);
        var review = Assert.Single(world.Towns[0].LandHearings.Cases);
        Assert.Equal(lapsing.Tiles, TownLandHearingRules.CurrentRevision(review).Tiles);

        // Part of a plot already under a pending hearing: the Council is not asked to vote on a filing that could not open.
        provider.TownProposals.Enqueue(([lapsing.Tiles[0]], new(Statement: "Review part of this lapsed plot for the Town.", RequestedOutcome: "confirm")));
        Prompt(world, Judge, "Consider asking the Council to authorize a Town land case.");
        await UntilAsync(world, () => world.ExportState().Events.Any(entry => entry.Kind == "town_civic_action_rejected" &&
            entry.Detail == world.Towns[0].Id + "|" + Judge + "|hearing_propose_town"), 10, provider);
        Assert.DoesNotContain(world.Towns[0].Governance!.Proposals, proposal => proposal.Kind == "land_hearing");

        // A renewal whose requested end date passes before the Council finishes voting.
        var end = world.WorldTick + 12;
        provider.TownProposals.Enqueue((other.Tiles.ToArray(), new(Statement: "Renew this household's recorded permission.",
            RequestedOutcome: "renew", HouseholdId: other.HouseholdId, AgreedEndTick: end)));
        Prompt(world, Judge, "Consider asking the Council to authorize a different Town land case.");
        await UntilAsync(world, () => world.Towns[0].Governance!.Proposals.Any(proposal => proposal.Kind == "land_hearing"), 10, provider);
        var opened = Assert.Single(world.Towns[0].Governance!.Proposals, proposal => proposal.Kind == "land_hearing");
        Assert.Equal(("pending", Judge), (opened.Status, opened.AuthorId));
        while (world.WorldTick < end) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        provider.VoteYes = true;
        foreach (var voter in opened.Voters) Prompt(world, voter, "Consider your vote on the Town land case proposal.");
        await UntilAsync(world, () => world.Towns[0].Governance!.Proposals.Single(proposal => proposal.Id == opened.Id).Status != "pending", 10, provider);

        var passed = world.Towns[0].Governance!.Proposals.Single(proposal => proposal.Id == opened.Id);
        Assert.Equal("passed", passed.Status);
        Assert.Equal(passed.RequiredYes, passed.Votes.Count(vote => vote.Yes));
        Assert.DoesNotContain(world.ExportState().Events, entry => entry.Kind == "town_civic_action_rejected" &&
            entry.Detail.EndsWith("|yes", StringComparison.Ordinal));
        for (var step = 0; step < 3; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var notice = Assert.Single(world.Towns[0].Governance!.Notices, entry => entry.Kind == "result" && entry.SubjectId == opened.Id + ":unopened");
        Assert.Equal("The Town land hearing that the Council authorized could not open. Its requested end date has passed.", notice.Text);
        Assert.Equal((review.Id, "pending"), (Assert.Single(world.Towns[0].LandHearings.Cases).Id, world.Towns[0].LandHearings.Cases[0].Status));
        Assert.Equal(JsonSerializer.Serialize(other), JsonSerializer.Serialize(world.HouseholdLandUseRights.Single(right => right.Id == other.Id)));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        restored.Validate();
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private sealed record SettledSave(byte[] Save, GridPoint[] Plot, string RightId, long End);

    /// <summary>
    /// One real personal-path run shared by the tests that begin after a ruling: residents elect a land mayor, a
    /// household files over its own permission, and the mayor confirms it well before that permission's agreed end.
    /// </summary>
    private static readonly Lazy<Task<SettledSave>> SettledDispute = new(async () =>
    {
        var provider = new HearingProvider();
        using var elected = NewWorld(provider);
        await UntilAsync(elected, () => elected.Towns[0].Government!.Offices.Any(), Day * 3, provider);
        var state = elected.ExportState();
        var household = state.Society.Society.GetInhabitant(Filer).HouseholdId!;
        var right = state.HouseholdLandUseRights!.Where(item => item.HouseholdId == household)
            .OrderBy(item => item.Id, StringComparer.Ordinal).First();
        var end = state.Society.Society.WorldTick + Day * 3;
        provider.Plot = right.Tiles.ToArray();
        provider.HearingsEnabled = true;
        using var world = PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRights = state.HouseholdLandUseRights!.Select(item => item.Id == right.Id ? item with { AgreedEndTick = end } : item).ToArray()
        }, _ => provider);
        await UntilAsync(world, () => world.Towns[0].LandHearings.Cases is [{ Status: "settled" }], Day * 2, provider);
        // Later records, such as a rehearing request, must postdate the ruling.
        provider.HearingsEnabled = false;
        for (var step = 0; step < 2; step++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("confirm", Assert.Single(Assert.Single(world.Towns[0].LandHearings.Cases).Rulings).Outcome.Kind);
        return new(PrivateWorldRuntimeCodec.Encode(world.ExportState()), provider.Plot, right.Id, end);
    });

    private static PrivateWorldRuntime NewWorld(HearingProvider provider)
    {
        using var generated = NormalPathWorld.CreateGenerated("government-personal-path", _ => provider);
        var state = generated.ExportState();
        var society = state.Society.Society;
        var oldDay = society.Config.TicksPerWorldDay;
        // Only time scale, comfort and proximity to the actual public board are arranged;
        // generated Town title, household allocations, buildings, goods and founder identities remain actual.
        return PrivateWorldRuntime.Restore(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            { Position = state.Towns![0].OriginSite!.Value, HungerBasisPoints = 8_000 }).ToArray(),
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            { Config = state.WorldSystems.Config with { TicksPerDay = Day, CalendarOffsetTicks = 0 }, RegionalWeather = null }, state.Map),
            Society = state.Society with
            {
                Society = society with
                {
                    Config = society.Config with { TicksPerWorldDay = Day },
                    Inhabitants = society.Inhabitants.Select(person => person with
                    { BirthTick = person.BirthTick / oldDay * Day, BirthLifeTick = person.BirthLifeTick is { } birth ? birth / oldDay * Day : null }).ToArray()
                }
            }
        }, _ => provider);
    }

    /// <summary>Asks for one fresh personal-model turn. It supplies no choice, answer or permission.</summary>
    private static void Prompt(PrivateWorldRuntime world, string agent, string text) =>
        world.SubmitInstruction(new("prompt-" + world.WorldTick + "-" + agent[^1], "owner:test", agent, OwnerInstructionKind.Suggestive, text));

    private static async Task UntilAsync(PrivateWorldRuntime world, Func<bool> complete, int limit, HearingProvider provider)
    {
        for (var step = 0; step < limit && !complete(); step++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(complete(), "Missing personal-path boundary; recent selected actions: " + string.Join(", ", provider.Selected.TakeLast(16)));
    }

    private static string Permissions(PrivateWorldRuntimeState state) => JsonSerializer.Serialize(
        state.HouseholdLandUseRights!.OrderBy(right => right.Id, StringComparer.Ordinal));

    private static string PrivateProperty(PrivateWorldRuntimeState state) => JsonSerializer.Serialize(new
    {
        Titles = state.TownLandTitles!.OrderBy(title => title.Id, StringComparer.Ordinal),
        Buildings = state.WorldSimulation!.Buildings.OrderBy(building => building.InstanceId, StringComparer.Ordinal),
        state.Fields,
        Membership = state.Society.Society.Inhabitants.OrderBy(person => person.Id, StringComparer.Ordinal)
            .Select(person => new { person.Id, person.HouseholdId }),
        Lots = state.Society.Society.Inventory.Lots.OrderBy(lot => lot.Id, StringComparer.Ordinal)
            .Select(lot => new
            {
                lot.Id,
                lot.ItemKind,
                lot.OwnerId,
                lot.Quantity,
                lot.ProvenanceLotId,
                lot.StorageBuildingId,
                lot.DeliveryBuildingId,
                lot.ContainerLotId,
                lot.GroundPosition,
                lot.CarrierId
            })
    });

    private static void AssertMalformedHearingSavesRefused(byte[] encoded)
    {
        foreach (var damage in new[] { "missing", "null", "cases", "case", "revisions", "revision", "tiles", "parties", "party", "filings", "filing", "notice", "judge", "source", "read" })
        {
            var document = JsonNode.Parse(encoded)!;
            var town = document["state"]!["towns"]![0]!;
            var hearing = town["landHearings"]!;
            var item = hearing["cases"]![0]!;
            switch (damage)
            {
                case "missing": town.AsObject().Remove("landHearings"); break;
                case "null": town["landHearings"] = null; break;
                case "cases": hearing["cases"] = null; break;
                case "case": hearing["cases"]![0] = null; break;
                case "revisions": item["revisions"] = null; break;
                case "revision": item["revisions"]![0] = null; break;
                case "tiles": item["revisions"]![0]!["tiles"] = null; break;
                case "parties": item["revisions"]![0]!["parties"] = null; break;
                case "party": item["revisions"]![0]!["parties"]![0] = null; break;
                case "filings": item["filings"] = null; break;
                case "filing": item["filings"]![0] = null; break;
                case "notice": item["revisions"]![0]!["noticeId"] = "notice:invented"; break;
                case "judge": item["rulings"]![0]!["judge"]!["kind"] = "ordinary_mayor"; break;
                case "source": item["evidence"]![0]!["sourceVersion"] = "invented"; break;
                case "read":
                    foreach (var read in item["reads"]!.AsArray().Where(read => read!["agentId"]!.GetValue<string>() == Judge))
                        read!["evidenceIds"] = new JsonArray();
                    break;
            }
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        }

        // Direct typed restore must refuse the same damage without depending on
        // the codec's JSON null-member and null-list-entry guards.
        var saved = PrivateWorldRuntimeCodec.Decode(encoded);
        var savedTowns = saved.Towns!;
        var savedTown = savedTowns[0];
        var ledger = savedTown.LandHearings;
        var savedCase = Assert.Single(ledger.Cases);
        var savedRevision = Assert.Single(savedCase.Revisions);
        foreach (var malformed in new TownLandHearingState[]
        {
            null!,
            ledger with { Cases = null! },
            ledger with { Cases = [null!] },
            ledger with { Cases = [savedCase with { Revisions = null! }] },
            ledger with { Cases = [savedCase with { Revisions = [null!] }] },
            ledger with { Cases = [savedCase with { Revisions = [savedRevision with { Tiles = null! }] }] },
            ledger with { Cases = [savedCase with { Revisions = [savedRevision with { Parties = null! }] }] },
            ledger with { Cases = [savedCase with { Revisions = [savedRevision with { Parties = [null!] }] }] },
            ledger with { Cases = [savedCase with { Filings = null! }] },
            ledger with { Cases = [savedCase with { Filings = [null!] }] },
            // The valid first case has actual record evidence, whose provenance
            // lookup must not traverse a later malformed case before refusing it.
            ledger with
            {
                Cases = [savedCase, savedCase with
                {
                    Id = savedCase.Id[..(savedCase.Id.LastIndexOf(':') + 1)] + ledger.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Revisions = [savedRevision with { RightVersions = null! }]
                }]
            }
        })
        {
            var damaged = saved with
            {
                Towns = savedTowns.Select(town => town.Id == savedTown.Id ? town with { LandHearings = malformed } : town).ToArray()
            };
            Assert.Throws<InvalidDataException>(() =>
            {
                using var restored = PrivateWorldRuntime.Restore(damaged, _ => new ActionCoverageRecorder(chooseIdle: true));
            });
        }
    }

    private sealed class HearingProvider(DecisionProviderKind kind = DecisionProviderKind.LargeLanguageModel) : IDecisionProvider
    {
        public ConcurrentQueue<string> Selected { get; } = new();
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 1;
        public bool HearingsEnabled { get; set; }
        public GridPoint[] Plot { get; set; } = [];
        /// <summary>The choices each adult was last offered, so a test can see what a case still asks of them.</summary>
        public ConcurrentDictionary<string, string[]> Offered { get; } = new(StringComparer.Ordinal);
        public bool SeekMayor { get; set; } = true;
        public bool InspectWhenOffered { get; set; }
        public bool AcceptRehearings { get; set; }
        /// <summary>The filer observes the plot once, then asks for a rehearing citing that observation.</summary>
        public bool ObserveAndReopen { get; set; }
        public bool Observed { get; private set; }
        public bool VoteYes { get; set; }
        /// <summary>Exact plots and requests the Council member proposes for the Town, one attempt each.</summary>
        public ConcurrentQueue<(GridPoint[] Plot, CognitionLandHearingChoice Request)> TownProposals { get; } = new();

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var choices = observation.Candidates;
            Offered[observation.InhabitantId] = choices.Select(candidate => candidate.Id).ToArray();
            var choice = choices.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal));
            CognitionLandHearingChoice? hearing = null;
            IReadOnlyList<CognitionLandTile>? tiles = null;
            if (AcceptRehearings && observation.InhabitantId == Judge)
                choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_assess_reopen|", StringComparison.Ordinal) &&
                    candidate.Id.EndsWith(":accept", StringComparison.Ordinal));
            if (ObserveAndReopen && !Observed && observation.InhabitantId == Filer)
                choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_observe|", StringComparison.Ordinal));
            if (InspectWhenOffered)
                choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_inspect|", StringComparison.Ordinal));
            if (ObserveAndReopen && Observed && observation.InhabitantId == Filer)
                choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_reopen|", StringComparison.Ordinal) &&
                    candidate.Id.EndsWith("|material_evidence", StringComparison.Ordinal));
            if (VoteYes)
                choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|yes|", StringComparison.Ordinal));
            if (choice is null && observation.InhabitantId == Judge && !TownProposals.IsEmpty)
                choice = choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_propose_town|", StringComparison.Ordinal));
            if (HearingsEnabled)
            {
                if (observation.InhabitantId == Judge)
                    choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_rule|", StringComparison.Ordinal) && candidate.Id.EndsWith("|confirm", StringComparison.Ordinal)) ??
                        choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_inspect|", StringComparison.Ordinal));
                if (observation.InhabitantId == Filer)
                {
                    choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_answer|", StringComparison.Ordinal));
                    if (choice is null && !choices.Any(candidate => candidate.Id.Contains("|hearing_statement|", StringComparison.Ordinal) ||
                            candidate.Id.Contains("|hearing_inspect|", StringComparison.Ordinal) || candidate.Id.Contains("|hearing_reopen|", StringComparison.Ordinal)))
                        choice = choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_file|", StringComparison.Ordinal));
                }
                if (observation.InhabitantId == Waiver)
                    choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_waive|", StringComparison.Ordinal));
            }
            if (Kind == DecisionProviderKind.LargeLanguageModel && SeekMayor)
            {
                choice ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|government_yes|", StringComparison.Ordinal)) ??
                    choices.FirstOrDefault(candidate => candidate.Id.Contains("|mayor_vote|", StringComparison.Ordinal) && candidate.Id.EndsWith("|" + Judge, StringComparison.Ordinal));
                if (choice is null && observation.InhabitantId == Judge)
                    choice = choices.FirstOrDefault(candidate => candidate.Id.Contains("|mayor_register|land|", StringComparison.Ordinal)) ??
                        choices.FirstOrDefault(candidate => candidate.Id.Contains("|government_propose|council+mayor|", StringComparison.Ordinal));
            }
            choice ??= choices.Single(candidate => candidate.Id == "safe_idle");
            if (choice.Id.Contains("|hearing_file|", StringComparison.Ordinal))
            {
                tiles = Plot.Select(point => new CognitionLandTile(point.X, point.Y)).ToArray();
                hearing = new(Statement: "Please confirm our recorded household permission on this exact starter plot.", RequestedOutcome: "confirm");
            }
            else if (choice.Id.Contains("|hearing_propose_town|", StringComparison.Ordinal) && TownProposals.TryDequeue(out var proposal))
            {
                tiles = proposal.Plot.Select(point => new CognitionLandTile(point.X, point.Y)).ToArray();
                hearing = proposal.Request;
            }
            else if (choice.Id.Contains("|hearing_observe|", StringComparison.Ordinal))
                Observed = true;
            else if (choice.Id.Contains("|hearing_reopen|", StringComparison.Ordinal))
                hearing = new(Grounds: "I personally observed the plot after the ruling; that observation was not before the judge.",
                    EvidenceIds: choice.Description.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries)
                        .Where(token => token.StartsWith("land-evidence:", StringComparison.Ordinal) && token.EndsWith("=observation", StringComparison.Ordinal))
                        .Select(token => token.Split('=')[0]).Distinct(StringComparer.Ordinal).ToArray());
            else if (choice.Id.Contains("|hearing_assess_reopen|", StringComparison.Ordinal))
                hearing = new(Statement: "The newly recorded observation was not before the earlier ruling.");
            else if (choice.Id.Contains("|hearing_answer|", StringComparison.Ordinal))
                hearing = new(Statement: "I answer for myself: our household asks to retain its recorded permission.");
            else if (choice.Id.Contains("|hearing_rule|", StringComparison.Ordinal))
            {
                var evidence = choice.Description.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Where(token => token.StartsWith("land-evidence:", StringComparison.Ordinal)).Select(token => token.Split('=')[0])
                    .Distinct(StringComparer.Ordinal).ToArray();
                hearing = new(Statement: "The inspected public record and both personal responses support confirming this permission.", EvidenceIds: evidence);
            }
            Selected.Enqueue(observation.InhabitantId + ":" + choice.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, choice.Id, 1,
                choices.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandTiles: tiles, CivicLandHearing: hearing));
        }
    }
}
