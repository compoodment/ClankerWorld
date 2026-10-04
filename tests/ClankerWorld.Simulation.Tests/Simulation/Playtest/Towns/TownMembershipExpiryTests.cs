using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownMembershipTests
{
    private const int AcceptanceDay = 40;
    private static readonly JsonSerializerOptions AdmissionJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SponsoredApprovalLapsesAtItsDeadlineWhilePauseReloadReplayAndRollbackPreserveTheRemainingTime()
    {
        var newcomer = NewAgentId();
        var approved = await OneSponsoredApprovalAsync("membership-approval-expiry", newcomer);
        var proposal = Assert.Single(Town(approved, First).Governance!.Proposals);
        var deadline = proposal.SettledTick!.Value + AcceptanceDay;
        using var world = Reopen(approved, new ScriptedModel());
        var property = AdmissionPhysicalProperty(world);
        AssertAcceptanceDeadlineProjection(world, newcomer, deadline);
        await AdvanceUntil(world, () => world.WorldTick == deadline - 1, AcceptanceDay);
        Assert.Equal("approved", Assert.Single(world.Towns[0].Admissions!).Status);
        AssertAdmissionCodecRefuses(world, records =>
        {
            records[0]!["status"] = "lapsed";
            records[0]!["reason"] = "acceptance_expired";
            records[0]!["decidedTick"] = deadline - 1;
        });

        // An isolated proposed tick may reach the deadline, but a refused commit spends no world time.
        var beforeRejectedTick = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(beforeRejectedTick, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var paused = Paused(world);
        var pausedBytes = PrivateWorldRuntimeCodec.Encode(paused);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.False((await world.AdvanceOneTickAsync()).Advanced);
            Assert.False((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        }
        Assert.Equal(pausedBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(pausedBytes), _ => new ScriptedModel());
        Assert.Equal(pausedBytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        AssertAcceptanceDeadlineProjection(replay, newcomer, deadline);
        world.Resume();
        replay.Resume();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(deadline, world.WorldTick);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));

        var lapsed = Assert.Single(world.Towns[0].Admissions!);
        Assert.Equal((proposal.Id, "lapsed", "acceptance_expired", deadline),
            (lapsed.ProposalId, lapsed.Status, lapsed.Reason, lapsed.DecidedTick));
        AssertAdmissionCodecRefuses(world, records => records[0] = JsonSerializer.SerializeToNode(
            Assert.Single(Town(approved, First).Admissions!), AdmissionJsonOptions));
        Assert.Equal(JsonSerializer.Serialize(proposal), JsonSerializer.Serialize(Assert.Single(world.Towns[0].Governance!.Proposals)));
        Assert.DoesNotContain(world.Towns, town => town.ResidentIds.Contains(newcomer, StringComparer.Ordinal));
        Assert.Contains("approval expired without acceptance; ask again", OwnerView(world, newcomer), StringComparison.Ordinal);
        AssertAcceptanceDeadlineProjection(world, newcomer, null);
        var eventAtDeadline = Assert.Single(world.ExportState().Events, item => item.Kind == "town_admission_lapsed");
        Assert.Equal(deadline, eventAtDeadline.WorldTick);
        Assert.Equal($"{First}|{newcomer}|{proposal.Id}|acceptance_expired", eventAtDeadline.Detail);
        Assert.Equal(property, AdmissionPhysicalProperty(world));

        // Inspect a fresh personal turn at the actual lapsed boundary; the approval and decline are absent.
        var observer = new ScriptedModel();
        using var inspected = Reopen(Paused(world), observer);
        Assert.True((await inspected.AdvanceOneTickAsync()).Advanced);
        var observation = Assert.Single(observer.ObservationsOf(newcomer));
        Assert.DoesNotContain(observation.Candidates, candidate => candidate.Id.Contains("|accept_admission|", StringComparison.Ordinal) ||
            candidate.Id.Contains("|decline_admission|", StringComparison.Ordinal));
        Assert.Contains("approval expired without acceptance; ask again", observation.Self?.TownMembershipNote, StringComparison.Ordinal);
        AssertRoundTrip(inspected);
        inspected.Validate();
    }

    [Fact]
    public async Task APersonalAcceptanceAtTheLastValidTickAdmitsOnlyOnceWithoutChangingHouseholdOrProperty()
    {
        var newcomer = NewAgentId();
        var approved = await OneSponsoredApprovalAsync("membership-last-acceptance", newcomer);
        var proposal = Assert.Single(Town(approved, First).Governance!.Proposals);
        var deadline = proposal.SettledTick!.Value + AcceptanceDay;
        using var waiting = Reopen(approved, new ScriptedModel());
        await AdvanceUntil(waiting, () => waiting.WorldTick == deadline - 2, AcceptanceDay);
        var property = AdmissionPhysicalProperty(waiting);
        var accepting = new ScriptedModel();
        accepting.Scripts[newcomer] = [Civic(First, "accept_admission")];
        // Start a fresh scripted provider phase from the real near-deadline checkpoint; positions and clock are retained.
        using var world = Reopen(Paused(waiting), accepting);
        var step = await world.AdvanceOneTickAsync();
        Assert.True(step.Advanced);
        Assert.Equal(deadline - 1, world.WorldTick);
        Assert.Contains(step.Decisions, decision => decision.InhabitantId == newcomer &&
            decision.Admission is { Accepted: true, FellBack: false } &&
            decision.Admission.Intention?.Provider == DecisionProviderKind.LargeLanguageModel &&
            decision.Admission.Intention.CandidateId == Civic(First, "accept_admission") + proposal.Id + "|");
        var admission = Assert.Single(world.Towns[0].Admissions!);
        Assert.Equal((proposal.Id, "admitted", deadline - 1), (admission.ProposalId, admission.Status, admission.DecidedTick));
        Assert.Equal([newcomer], admission.MemberIds!);
        Assert.Null(admission.Reason);
        Assert.Contains(newcomer, world.Towns[0].ResidentIds);
        Assert.Contains(newcomer, world.Towns[0].Governance!.Members);
        Assert.Null(world.Society.GetInhabitant(newcomer).HouseholdId);
        Assert.Equal(property, AdmissionPhysicalProperty(world));
        AssertAcceptanceDeadlineProjection(world, newcomer, null);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(deadline, world.WorldTick);
        Assert.Equal("admitted", Assert.Single(world.Towns[0].Admissions!).Status);
        AssertAdmissionCodecRefuses(world, records => records[0]!["decidedTick"] = deadline);
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_admission_accepted");
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "town_admission_lapsed");
        world.Validate();
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task AnAcceptanceHeldPastTheDeadlineCannotJoinButALaterOrdinarySponsoredProposalCan()
    {
        var newcomer = NewAgentId();
        var approved = await OneSponsoredApprovalAsync("membership-held-acceptance", newcomer);
        var proposal = Assert.Single(Town(approved, First).Governance!.Proposals);
        var deadline = proposal.SettledTick!.Value + AcceptanceDay;
        using var waiting = Reopen(approved, new ScriptedModel());
        await AdvanceUntil(waiting, () => waiting.WorldTick == deadline - 2, AcceptanceDay);
        var holding = new HeldAdmissionModel(newcomer, Civic(First, "accept_admission") + proposal.Id + "|");
        using var world = Reopen(Paused(waiting), holding);
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            var heldRequest = await holding.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var offered = heldRequest.Observation;
            Assert.Equal(deadline - 1, offered.WorldTick);
            Assert.Contains(offered.Candidates, candidate => candidate.Id == holding.Choice);
            AssertOfferedApprovalClock(offered, proposal, "about 36 world minutes left");
            Assert.Equal("approved", Assert.Single(world.Towns[0].Admissions!).Status);
            var property = AdmissionPhysicalProperty(world);
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(deadline, world.WorldTick);
            Assert.Equal("acceptance_expired", Assert.Single(world.Towns[0].Admissions!).Reason);
            Assert.False(holding.Replied.Task.IsCompleted);
            var beforeReplyEvent = world.ExportState().Events[^1].EventId;
            holding.Release.TrySetResult(true);
            var reply = await holding.Replied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal((heldRequest.RequestId, newcomer, heldRequest.ProviderEpoch),
                (reply.RequestId, reply.InhabitantId, reply.ProviderEpoch));
            Assert.Equal((holding.Choice, offered.RunEpoch, offered.DecisionGeneration, offered.ObservationDigest),
                (reply.SelectedCandidateId, reply.RunEpoch, reply.DecisionGeneration, reply.ObservationDigest));
            Assert.Equal(HeldAdmissionModel.StaleThought, reply.PrivateThought);
            var outcomes = new List<CognitionAdmissionResult>();
            var discarded = false;
            bool NewerIdle(CognitionAdmissionResult outcome) => outcome is { Accepted: true, FellBack: false } &&
                outcome.Intention is { CandidateId: "safe_idle", Provider: DecisionProviderKind.LargeLanguageModel } intention &&
                intention.DecisionGeneration > offered.DecisionGeneration && intention.WorldTick > offered.WorldTick;
            for (var tick = 0; tick < 8 && (!discarded || !outcomes.Any(NewerIdle)); tick++)
            {
                await Task.Delay(2);
                var completed = await world.AdvanceOneTickNonBlockingAsync();
                Assert.True(completed.Advanced);
                discarded |= completed.Events.Any(item => item.Kind == "hosted_decision_discarded" && item.Detail == newcomer);
                outcomes.AddRange(completed.Decisions.Where(decision => decision.InhabitantId == newcomer)
                    .Select(decision => decision.Admission));
            }
            var discard = Assert.Single(world.ExportState().Events, item => item.EventId > beforeReplyEvent &&
                item.Kind == "hosted_decision_discarded" && item.Detail == newcomer);
            Assert.True(discard.WorldTick > deadline);
            Assert.Contains(outcomes, NewerIdle);
            Assert.DoesNotContain(outcomes, outcome => outcome.Intention?.CandidateId == holding.Choice);
            Assert.DoesNotContain(world.Inhabitants.Single(person => person.InhabitantId == newcomer).RecentThoughts ?? [],
                thought => thought.Text == HeldAdmissionModel.StaleThought);
            Assert.DoesNotContain(world.Towns, town => town.ResidentIds.Contains(newcomer, StringComparer.Ordinal));
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "town_admission_accepted");
            Assert.Equal(property, AdmissionPhysicalProperty(world));
            world.Validate();
            AssertRoundTrip(world);

            var again = new ScriptedModel();
            again.Scripts[Founders[0]] = [$"civic|{First}|request_admission|{newcomer}|", Civic(First, "read"), Civic(First, "yes")];
            foreach (var founder in Founders.Skip(1)) again.Scripts[founder] = [Civic(First, "read"), Civic(First, "yes")];
            again.Scripts[newcomer] = [Civic(First, "read"), Civic(First, "accept_admission")];
            using var renewed = Reopen(Paused(world), again);
            await AdvanceUntil(renewed, () => renewed.Towns[0].ResidentIds.Contains(newcomer, StringComparer.Ordinal), AcceptanceDay * 2);
            var records = renewed.Towns[0].Admissions!;
            Assert.Equal(2, records.Count);
            Assert.Equal((proposal.Id, "lapsed", "acceptance_expired"), (records[0].ProposalId, records[0].Status, records[0].Reason));
            Assert.Equal("admitted", records[1].Status);
            Assert.NotEqual(proposal.Id, records[1].ProposalId);
            var fresh = renewed.Towns[0].Governance!.Proposals.Single(item => item.Id == records[1].ProposalId);
            Assert.Equal((Founders[0], newcomer, "admission", "passed"), (fresh.AuthorId, fresh.SubjectId, fresh.Kind, fresh.Status));
            Assert.Equal(3, fresh.Votes.Count(vote => vote.Yes));
            Assert.True(records[1].DecidedTick < fresh.SettledTick!.Value + AcceptanceDay);
            Assert.Single(renewed.ExportState().Events, item => item.Kind == "town_admission_accepted");
            Assert.Single(renewed.ExportState().Events, item => item.Kind == "town_admission_lapsed");
            Assert.Equal(property, AdmissionPhysicalProperty(renewed));
            renewed.Validate();
            AssertRoundTrip(renewed);
        }
        finally
        {
            holding.Release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task ASelfRequestedAdmissionStillJoinsImmediatelyWhenItsCouncilProposalPasses()
    {
        var newcomer = NewAgentId();
        var state = WithTowns(ShortDays(Generated("membership-own-request-unaffected", newcomer), AcceptanceDay), Founders, null);
        var choosing = new ScriptedModel();
        choosing.Scripts[newcomer] = [Civic(First, "admission")];
        foreach (var founder in Founders) choosing.Scripts[founder] = [Civic(First, "read"), Civic(First, "yes")];
        using var world = Reopen(Calm(At(state, Board(state, First), Founders.Append(newcomer).ToArray())), choosing);
        var property = AdmissionPhysicalProperty(world);
        await AdvanceUntil(world, () => world.Towns[0].ResidentIds.Contains(newcomer, StringComparer.Ordinal), AcceptanceDay);
        var proposal = Assert.Single(world.Towns[0].Governance!.Proposals);
        var admission = Assert.Single(world.Towns[0].Admissions!);
        Assert.Equal((newcomer, newcomer, "passed"), (proposal.AuthorId, proposal.SubjectId, proposal.Status));
        Assert.Equal((proposal.Id, "admitted", proposal.SettledTick!.Value), (admission.ProposalId, admission.Status, admission.DecidedTick));
        Assert.Equal([newcomer], admission.MemberIds!);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind is "town_admission_approved" or "town_admission_lapsed");
        AssertAcceptanceDeadlineProjection(world, newcomer, null);
        Assert.Equal(property, AdmissionPhysicalProperty(world));
        Assert.Null(world.Society.GetInhabitant(newcomer).HouseholdId);
        world.Validate();
        AssertRoundTrip(world);
    }

    /// <summary>Actual personal sponsorship, three Council votes and a learned result; no injected approval record.</summary>
    private static async Task<PrivateWorldRuntimeState> OneSponsoredApprovalAsync(string seed, string newcomer)
    {
        var state = WithTowns(ShortDays(Generated(seed, newcomer), AcceptanceDay), Founders, null);
        var model = new ScriptedModel();
        model.Scripts[Founders[0]] = [$"civic|{First}|request_admission|{newcomer}|", Civic(First, "read"), Civic(First, "yes")];
        foreach (var founder in Founders.Skip(1)) model.Scripts[founder] = [Civic(First, "read"), Civic(First, "yes")];
        model.Scripts[newcomer] = [Civic(First, "read")];
        using var world = Reopen(Calm(At(state, Board(state, First), Founders.Append(newcomer).ToArray())), model);
        await AdvanceUntil(world, () => model.ObservationsOf(newcomer).Any(observation => observation.Candidates.Any(candidate =>
            candidate.Id.StartsWith(Civic(First, "accept_admission"), StringComparison.Ordinal))), AcceptanceDay);
        var approved = Paused(world);
        var town = Town(approved, First);
        var proposal = Assert.Single(town.Governance!.Proposals);
        Assert.Equal((Founders[0], newcomer, "admission", "passed"), (proposal.AuthorId, proposal.SubjectId, proposal.Kind, proposal.Status));
        Assert.Equal(3, proposal.Votes.Count(vote => vote.Yes));
        AssertApproval(town, proposal.Id, newcomer);
        Assert.Equal(proposal.SettledTick!.Value, Assert.Single(town.Admissions!).DecidedTick);
        Assert.Contains(town.Governance.Knowledge, receipt => receipt.AgentId == newcomer && town.Governance.Notices.Any(notice =>
            notice.Id == receipt.NoticeId && notice.Kind == "result" && notice.SubjectId == proposal.Id));
        var offered = model.ObservationsOf(newcomer).First(observation => observation.Candidates.Any(candidate =>
            candidate.Id == Civic(First, "accept_admission") + proposal.Id + "|"));
        // With forty ticks per day, each tick is exactly thirty-six clock minutes.
        var remainingMinutes = (proposal.SettledTick!.Value + AcceptanceDay - offered.WorldTick) * 36;
        Assert.InRange(remainingMinutes, 1, 24 * 60 - 1);
        var duration = remainingMinutes >= 60
            ? FormattableString.Invariant($"about {remainingMinutes / 60} world hours") +
                (remainingMinutes % 60 == 0 ? " left" : FormattableString.Invariant($" {remainingMinutes % 60} minutes left"))
            : FormattableString.Invariant($"about {remainingMinutes} world minutes left");
        AssertOfferedApprovalClock(offered, proposal, duration);
        return approved;
    }

    private static void AssertOfferedApprovalClock(InhabitantObservation observation, TownProposal proposal, string duration)
    {
        var deadline = proposal.SettledTick!.Value + AcceptanceDay;
        Assert.NotEqual(0, deadline % AcceptanceDay);
        var clockMinutes = deadline % AcceptanceDay * 36;
        var window = FormattableString.Invariant($"world day {deadline / AcceptanceDay + 1} at {clockMinutes / 60:00}:{clockMinutes % 60:00}; {duration}; paused time does not count");
        var candidate = Assert.Single(observation.Candidates, item => item.Id == Civic(First, "accept_admission") + proposal.Id + "|");
        Assert.Contains("accept before " + window, candidate.Description, StringComparison.Ordinal);
        Assert.Contains("council approved admission; accept before " + window, observation.Self?.TownMembershipNote, StringComparison.Ordinal);
    }

    private static void AssertAdmissionCodecRefuses(PrivateWorldRuntime world, Action<JsonArray> damage)
    {
        var source = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var document = JsonNode.Parse(source)!;
        damage(document["state"]!["towns"]![0]!["admissions"]!.AsArray());
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        Assert.Equal("A Town's saved admission record does not match a passed admission proposal.", error.Message);
        Assert.Equal(source, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static void AssertAcceptanceDeadlineProjection(PrivateWorldRuntime world, string newcomer, long? expected)
    {
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var factor = Assert.Single(snapshot.Inhabitants.Single(person => person.Id == newcomer).DecisionFactors, item => item.Key == "town-membership");
        Assert.Equal(expected, factor.AcceptanceDeadlineTick);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldSnapshot>(JsonSerializer.Serialize(snapshot, AdmissionJsonOptions), AdmissionJsonOptions)!;
        var clientFactor = Assert.Single(client.Inhabitants.Single(person => person.Id == newcomer).DecisionFactors, item => item.Key == "town-membership");
        Assert.Equal(expected, clientFactor.AcceptanceDeadlineTick);
        Assert.Equal(factor.Detail, clientFactor.Detail);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static string AdmissionPhysicalProperty(PrivateWorldRuntime world) => JsonSerializer.Serialize(new
    {
        Buildings = world.WorldSimulation.Buildings,
        Lots = world.Society.Inventory.Lots.OrderBy(lot => lot.Id, StringComparer.Ordinal).Select(lot => new
        {
            lot.Id,
            lot.OwnerId,
            lot.ItemKind,
            lot.Quantity,
            lot.StorageBuildingId,
            lot.ContainerLotId,
            lot.CarrierId,
            lot.GroundPosition,
            lot.DeliveryBuildingId,
        }),
    });

    private sealed class HeldAdmissionModel(string newcomer, string choice) : IDecisionProvider
    {
        public const string StaleThought = "This held admission reply must not commit after expiry.";
        public string Choice { get; } = choice;
        public TaskCompletionSource<CognitionDecisionRequest> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CognitionDecisionResponse> Replied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public DecisionProviderKind KindFor(InhabitantObservation observation) => observation.InhabitantId == newcomer
            ? DecisionProviderKind.LargeLanguageModel : DecisionProviderKind.Deterministic;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = "safe_idle";
            var held = observation.InhabitantId == newcomer && !Started.Task.IsCompleted &&
                observation.Candidates.Any(candidate => candidate.Id == Choice);
            if (held)
            {
                selected = Choice;
                Started.TrySetResult(request);
                // Deliberately return despite cancellation: the actual deadline must defeat a previously valid personal reply.
                await Release.Task;
            }
            var response = new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, KindFor(observation), ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected ? 1d : 0d, StringComparer.Ordinal),
                PrivateThought: held ? StaleThought : null);
            if (held) Replied.TrySetResult(response);
            return response;
        }
    }
}
