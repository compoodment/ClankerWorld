using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using System.Text.Json;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownNonviolentAuthorityRuntimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ACompletedFindingSurvivesScopeRepealInTheSameTickAndItsJudgeLosesCurrentAuthority()
    {
        var state = await NonviolentRuntimeFixture.FindingAsync();
        var town = state.Towns![0];
        var finding = Assert.Single(Assert.Single(town.Nonviolent.Cases).Findings);
        var tick = state.Society.Society.WorldTick;
        Assert.Equal(tick, finding.Tick);
        var (council, government) = TownGovernmentRules.Propose(town.Governance!, town.Government!, town.Id,
            NonviolentRuntimeFixture.Subject, town.Government!.Arrangement with { NonLand = TownArrangementRules.NoOffice },
            false, town.ResidentIds, tick, NonviolentRuntimeFixture.Day);
        foreach (var voter in town.ResidentIds.Take(3))
            government = TownGovernmentRules.Vote(government, government.Changes[^1].Id, voter, true, tick);
        (council, government) = TownGovernmentRules.Advance(council, government, town.Id, town.Name,
            state.WorldSeed, town.ResidentIds, tick, NonviolentRuntimeFixture.Day);
        Assert.Equal(TownArrangementRules.NoOffice, government.Arrangement.NonLand);
        var households = state.Society.Society.Inhabitants.ToDictionary(person => person.Id, person => person.HouseholdId, StringComparer.Ordinal);
        var (cases, notices) = TownCaseJudgeRules.Advance(town.Nonviolent, council, government, town.ResidentIds,
            households, tick, NonviolentRuntimeFixture.Day);
        Assert.Null(Assert.Single(cases.Cases).Judge);
        Assert.Single(cases.Cases[0].JudgeHistory);
        state = NonviolentRuntimeFixture.Strict(state with
        { Towns = [town with { Governance = notices, Government = government, Nonviolent = cases }] });
        using var world = NonviolentRuntimeFixture.Create(state, new NonviolentTestProvider());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var restored = NonviolentRuntimeFixture.Strict(world.ExportState());
        Assert.Equal(JsonSerializer.Serialize(finding), JsonSerializer.Serialize(Assert.Single(restored.Towns![0].Nonviolent.Cases[0].Findings)));
        Assert.Null(restored.Towns[0].Nonviolent.Cases[0].Judge);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARealHeldFindingRequiresTheSameCurrentAuthorityWhenItArrives(bool repealWhileHeld)
    {
        var state = await NonviolentRuntimeFixture.ReadyToFindAsync();
        var town = state.Towns![0];
        // All voters begin at the public board; the shared fixture already created the actual
        // observed act, case, elected office, accepted extension and completed response window.
        state = NonviolentRuntimeFixture.Strict(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            { Position = town.OriginSite!.Value, LastDecisionContext = null }).ToArray()
        });
        var originalCase = Assert.Single(town.Nonviolent.Cases);
        var originalAuthority = Assert.IsType<TownNonLandAuthority>(
            TownGovernmentRules.CurrentNonLandAuthority(town.Government!, state.Society.Society.WorldTick));
        Assert.Equal(NonviolentRuntimeFixture.Judge, originalAuthority.HolderId);
        Assert.Empty(originalCase.Findings);
        var evidence = originalCase.Evidence.Where(item => item.SourceRecordId == originalCase.Allegation.IncidentId &&
                item.Kind is "observation" or "record")
            .Select(item => item.Id).ToArray();
        Assert.NotEmpty(evidence);
        var held = new HeldFindingProvider(evidence);
        var repeal = new RepealProvider(town.Government!.Arrangement with { NonLand = TownArrangementRules.NoOffice });
        using var world = PrivateWorldRuntime.Restore(state, actor => actor == NonviolentRuntimeFixture.Judge ? held : repeal);
        try
        {
            await AdvanceHostedUntilAsync(world, () => held.Started.Task.IsCompleted, 12);
            await held.Started.Task.WaitAsync(Deadline);
            Assert.Contains("|law_case_find|", held.CandidateId!, StringComparison.Ordinal);
            Assert.Equal(originalAuthority, TownGovernmentRules.CurrentNonLandAuthority(world.Towns[0].Government!, world.WorldTick));
            Assert.Empty(Assert.Single(world.Towns[0].Nonviolent.Cases).Findings);

            if (repealWhileHeld)
            {
                repeal.Enabled = true;
                foreach (var actor in town.ResidentIds.Where(actor => actor != NonviolentRuntimeFixture.Judge))
                    NonviolentRuntimeFixture.Wake(world, actor, "consider-scope-repeal:" + actor);
                await AdvanceHostedUntilAsync(world,
                    () => world.Towns[0].Government!.Changes.Count > town.Government.Changes.Count, 6);
                foreach (var actor in town.ResidentIds.Where(actor => actor != NonviolentRuntimeFixture.Judge))
                    NonviolentRuntimeFixture.Wake(world, actor, "read-scope-proposal:" + actor);
                await AdvanceHostedUntilAsync(world, () => town.ResidentIds.Where(actor => actor != NonviolentRuntimeFixture.Judge)
                    .All(actor => world.Towns[0].Governance!.Knowledge.Any(receipt => receipt.AgentId == actor &&
                        world.Towns[0].Governance!.Notices.Any(notice => notice.Id == receipt.NoticeId &&
                            notice.SubjectId == TownGovernmentRules.VoteNoticeToken(world.Towns[0].Government!.Changes[^1])))), 6);
                foreach (var actor in town.ResidentIds.Where(actor => actor != NonviolentRuntimeFixture.Judge))
                    NonviolentRuntimeFixture.Wake(world, actor, "vote-scope-proposal:" + actor);
                await AdvanceHostedUntilAsync(world,
                    () => world.Towns[0].Government!.Arrangement.NonLand == TownArrangementRules.NoOffice, 6);

                var government = world.Towns[0].Government!;
                var change = Assert.Single(government.Changes, item => item.Target == repeal.Target && item.Status == "completed" &&
                    !town.Government!.Changes.Any(previous => previous.Id == item.Id));
                Assert.Equal(3, change.Votes.Count(vote => vote.Yes));
                Assert.DoesNotContain(change.Votes, vote => vote.AgentId == NonviolentRuntimeFixture.Judge);
                Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(government, world.WorldTick));
                Assert.True(world.WorldTick < originalAuthority.TermEndTick);
                Assert.Contains(government.Offices, office => office.Mandates == "land" &&
                    office.HolderId == NonviolentRuntimeFixture.Judge && office.TermEndTick == originalAuthority.TermEndTick);
                var pending = Assert.Single(world.Towns[0].Nonviolent.Cases);
                Assert.Null(pending.Judge);
                Assert.Equal(originalCase.Responses, pending.Responses);
                Assert.Empty(pending.Findings);
            }

            // Inline completion propagates the exact original provider reply before the next
            // admission tick; neither the case nor the request is reconstructed for this reply.
            await Task.Run(held.Release).WaitAsync(Deadline);
            PrivateWorldStepResult? admitted = null;
            for (var attempt = 0; attempt < 12 && admitted is null; attempt++)
            {
                var step = await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(Deadline);
                Assert.True(step.Advanced);
                if (step.Decisions.Any(decision => decision.InhabitantId == NonviolentRuntimeFixture.Judge) ||
                    step.Events.Any(item => item.Kind == "hosted_decision_discarded" && item.Detail == NonviolentRuntimeFixture.Judge))
                    admitted = step;
                else await Task.Delay(10);
            }
            Assert.NotNull(admitted);
            var file = Assert.Single(world.Towns[0].Nonviolent.Cases);
            if (repealWhileHeld)
            {
                Assert.True(admitted.Decisions.Any(decision => decision.InhabitantId == NonviolentRuntimeFixture.Judge &&
                        (!decision.Admission.Accepted || decision.Admission.FellBack)) ||
                    admitted.Events.Any(item => item.Kind == "hosted_decision_discarded" && item.Detail == NonviolentRuntimeFixture.Judge));
                Assert.Equal("pending", file.Status);
                Assert.Empty(file.Findings);
                Assert.Empty(world.Towns[0].Nonviolent.Agreements);
                Assert.Empty(world.Towns[0].Nonviolent.Effects);
            }
            else
            {
                Assert.Contains(admitted.Decisions, decision => decision.InhabitantId == NonviolentRuntimeFixture.Judge &&
                    decision.Admission.Accepted && !decision.Admission.FellBack && decision.Admission.Intention!.CandidateId == held.CandidateId);
                var finding = Assert.Single(file.Findings);
                Assert.Equal("supported", finding.Result);
                Assert.Equal("warning", finding.Consequence);
                Assert.Equal(originalAuthority.AuthorityId, finding.Judge.AuthorityId);
            }
            world.Pause();
            var restored = NonviolentRuntimeFixture.Strict(world.ExportState());
            var savedFile = Assert.Single(restored.Towns![0].Nonviolent.Cases);
            Assert.Equal(JsonSerializer.Serialize(file.Findings), JsonSerializer.Serialize(savedFile.Findings));
            Assert.Equal(JsonSerializer.Serialize(originalCase.Responses), JsonSerializer.Serialize(savedFile.Responses));
        }
        finally { held.Release(); }
    }

    private static async Task AdvanceHostedUntilAsync(PrivateWorldRuntime world, Func<bool> complete, int limit)
    {
        for (var attempt = 0; attempt < limit && !complete(); attempt++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(Deadline)).Advanced);
            await Task.Delay(10);
        }
        Assert.True(complete(), "The actual hosted civic action did not reach its bounded admission checkpoint. " +
            string.Join("\n", world.ExportState().Events.TakeLast(20)));
    }

    private sealed class HeldFindingProvider(IReadOnlyList<string> evidence) : IDecisionProvider
    {
        private readonly TaskCompletionSource<CognitionDecisionResponse> pending = new();
        private CognitionDecisionResponse? response;
        private bool released;
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? CandidateId { get; private set; }

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = !released ? request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.Contains("|law_case_find|", StringComparison.Ordinal) && candidate.Id.EndsWith("|warning", StringComparison.Ordinal)) : null;
            choice ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            var result = Reply(request, choice, choice.Id == "safe_idle" ? null : new(
                Statement: "The inspected firsthand account supports a warning for the recorded passage.",
                Uncertainty: "The record cannot establish the person's intention.", EvidenceIds: evidence));
            if (!released && choice.Id != "safe_idle")
            {
                response = result;
                CandidateId = choice.Id;
                Started.TrySetResult(true);
                return new(pending.Task);
            }
            return ValueTask.FromResult(result);
        }

        internal void Release()
        {
            released = true;
            if (response is { } result) pending.TrySetResult(result);
        }
    }

    private sealed class RepealProvider(TownArrangement target) : IDecisionProvider
    {
        private bool proposed;
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        internal bool Enabled { get; set; }
        internal TownArrangement Target { get; } = target;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            CognitionCandidate? choice = null;
            if (Enabled)
            {
                if (!proposed && observation.InhabitantId == NonviolentRuntimeFixture.Subject)
                {
                    choice = observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains(
                        "|government_propose|" + TownArrangementRules.Key(Target) + "|", StringComparison.Ordinal));
                    if (choice is not null) proposed = true;
                }
                choice ??= observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|government_yes|", StringComparison.Ordinal));
            }
            choice ??= observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(Reply(request, choice));
        }
    }

    private static CognitionDecisionResponse Reply(CognitionDecisionRequest request, CognitionCandidate candidate,
        CognitionNonviolentChoice? payload = null) => new(request.RequestId, request.Observation.InhabitantId,
        DecisionProviderKind.LargeLanguageModel, 1, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
        request.Observation.ObservationDigest, candidate.Id, 1,
        request.Observation.Candidates.ToDictionary(item => item.Id, item => item.Id == candidate.Id ? 1d : 0d, StringComparer.Ordinal),
        CivicNonviolent: payload);
}
