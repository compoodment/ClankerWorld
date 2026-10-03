using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandHearingHeldReplyTests
{
    private const string Actor = "founder:00000000000000000000000000000003";
    private const string Newcomer = "agent:00000000000000000000000000000093";
    private const int Day = 40;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("hearing_answer")]
    [InlineData("hearing_waive")]
    public async Task AReplyHeldByThePlayableHostCannotAnswerAnOldWindowAfterAnAdultJoins(string action)
    {
        var provider = new HeldReplyProvider(action);
        using var world = NewWorld(provider);
        try
        {
            var initial = world.ExportState();
            var household = world.Society.GetInhabitant(Actor).HouseholdId!;
            var originalRights = JsonSerializer.Serialize(initial.HouseholdLandUseRights);
            var originalBuildings = JsonSerializer.Serialize(initial.WorldSimulation!.Buildings);
            var refreshed = false;
            for (var attempt = 0; attempt < 20 && !provider.Started.Task.IsCompleted; attempt++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(Deadline)).Advanced);
                if (!refreshed && world.Towns[0].LandHearings.Cases.Count > 0)
                {
                    world.SubmitInstruction(new("consider-hearing", "owner:test", Actor, OwnerInstructionKind.Suggestive,
                        "Read the posted land hearing and consider your own response."));
                    refreshed = true;
                }
                await Task.Delay(10);
            }
            Assert.True(provider.Started.Task.IsCompleted, "The actual posted notice and response were not offered within twenty hosted ticks.");
            await provider.Started.Task.WaitAsync(Deadline);
            Assert.NotNull(provider.ReadCandidateId);
            Assert.Contains("|read|", provider.ReadCandidateId, StringComparison.Ordinal);
            var item = Assert.Single(world.Towns[0].LandHearings.Cases);
            var oldRevision = Assert.Single(item.Revisions);
            Assert.Equal("expiry", item.Kind);
            Assert.Equal(1, oldRevision.PublishedTick);
            Assert.Equal(41, oldRevision.DeadlineTick);
            Assert.Contains("|" + action + "|" + item.Id + ":1|", provider.HeldCandidateId!, StringComparison.Ordinal);
            Assert.Contains(world.Towns[0].Governance!.Knowledge,
                receipt => receipt.AgentId == Actor && receipt.NoticeId == oldRevision.NoticeId);
            Assert.Empty(item.Responses);
            var oldParty = Assert.Single(oldRevision.Parties, party => party.HouseholdId == household);
            Assert.DoesNotContain(Newcomer, oldParty.AdultIds);

            // Actual House placement changes the affected adult roster while the exact old reply remains outstanding.
            var house = world.WorldSimulation.Buildings.First(building => building.HouseholdId == household &&
                building.InstanceId.Contains("house-", StringComparison.Ordinal));
            Assert.Equal(household, world.AddAgent(Newcomer, house.Position));
            Assert.Equal(household, world.Society.GetInhabitant(Newcomer).HouseholdId);
            await Task.Run(provider.Release).WaitAsync(Deadline);

            PrivateWorldStepResult? completed = null;
            for (var attempt = 0; attempt < 10 && completed is null; attempt++)
            {
                var step = await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(Deadline);
                Assert.True(step.Advanced);
                if (step.Decisions.Any(decision => decision.InhabitantId == Actor) ||
                    step.Events.Any(item => item.Kind == "hosted_decision_discarded" && item.Detail == Actor)) completed = step;
                else await Task.Delay(10);
            }
            Assert.NotNull(completed);
            Assert.True(completed.Decisions.Any(decision => decision.InhabitantId == Actor && decision.Admission.FellBack) ||
                completed.Events.Any(item => item.Kind == "hosted_decision_discarded" && item.Detail == Actor ||
                    item.Kind == "town_civic_action_rejected" && item.Detail == world.Towns[0].Id + "|" + Actor + "|" + action),
                "The completed old response must be refused by admission or by the hearing's current-action preflight.");

            item = Assert.Single(world.Towns[0].LandHearings.Cases);
            Assert.Equal("pending", item.Status);
            Assert.Equal(2, item.Revisions.Count);
            Assert.Equal(oldRevision, item.Revisions[0]);
            var current = item.Revisions[^1];
            Assert.Equal(2, current.Number);
            Assert.Equal(current.PublishedTick + Day, current.DeadlineTick);
            Assert.True(current.DeadlineTick > oldRevision.DeadlineTick);
            Assert.True(world.WorldTick < current.DeadlineTick);
            Assert.All(oldParty.AdultIds, adult => Assert.Contains(adult, Assert.Single(current.Parties, party => party.HouseholdId == household).AdultIds));
            Assert.Contains(Newcomer, Assert.Single(current.Parties, party => party.HouseholdId == household).AdultIds);
            Assert.NotEqual(oldRevision.NoticeId, current.NoticeId);
            Assert.Contains(world.Towns[0].Governance!.Notices,
                notice => notice.Id == current.NoticeId && notice.SubjectId == item.Id + ":2" && notice.PostedTick == current.PublishedTick);
            Assert.DoesNotContain(world.Towns[0].Governance!.Knowledge,
                receipt => receipt.AgentId == Actor && receipt.NoticeId == current.NoticeId);
            Assert.Empty(item.Responses);
            Assert.Empty(item.Rulings);
            Assert.Empty(world.Towns[0].LandHearings.Adjustments);
            Assert.Equal(originalRights, JsonSerializer.Serialize(world.HouseholdLandUseRights));
            Assert.Equal(originalBuildings, JsonSerializer.Serialize(world.WorldSimulation.Buildings));
            Assert.Equal(initial.TownLandTitles, world.TownLandTitles);

            world.Pause();
            world.Validate();
            var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), _ => new ActionCoverageRecorder(chooseIdle: true));
            restored.Validate();
            Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            var savedCase = Assert.Single(restored.Towns[0].LandHearings.Cases);
            Assert.Equal(current.DeadlineTick, savedCase.Revisions[^1].DeadlineTick);
            Assert.Empty(savedCase.Responses);
            Assert.Equal(originalRights, JsonSerializer.Serialize(restored.HouseholdLandUseRights));
        }
        finally { provider.Release(); }
    }

    private static PrivateWorldRuntime NewWorld(HeldReplyProvider provider)
    {
        using var generated = NormalPathWorld.CreateGenerated("government-personal-path", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var society = state.Society.Society;
        var oldDay = society.Config.TicksPerWorldDay;
        var household = society.GetInhabitant(Actor).HouseholdId;
        // A household can hold several connected starter allocations; only this real right is near expiry.
        var rights = Assert.IsAssignableFrom<IReadOnlyList<HouseholdLandUseRight>>(state.HouseholdLandUseRights);
        var ending = rights.First(right => right.HouseholdId == household);
        return PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRights = rights.Select(right => right.Id == ending.Id ? right with { AgreedEndTick = 1 } : right).ToArray(),
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
        }, id => id == Actor ? provider : new ActionCoverageRecorder(chooseIdle: true));
    }

    private sealed class HeldReplyProvider(string action) : IDecisionProvider
    {
        // Inline completion lets Release finish after the host's awaiting task receives the old exact response.
        private readonly TaskCompletionSource<CognitionDecisionResponse> held = new();
        private CognitionDecisionResponse? response;
        private bool released;
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? ReadCandidateId { get; private set; }
        public string? HeldCandidateId { get; private set; }

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var choice = released ? null : observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|" + action + "|", StringComparison.Ordinal));
            choice ??= observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            var value = new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, choice.Id, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandHearing: choice.Id.Contains("|hearing_answer|", StringComparison.Ordinal) ? new(Statement: "I personally answer this noticed hearing.") : null);
            if (choice.Id.Contains("|read|", StringComparison.Ordinal)) ReadCandidateId = choice.Id;
            if (!released && choice.Id.Contains("|" + action + "|", StringComparison.Ordinal))
            {
                response = value;
                HeldCandidateId = choice.Id;
                Started.TrySetResult(true);
                return new(held.Task);
            }
            return ValueTask.FromResult(value);
        }

        public void Release()
        {
            released = true;
            if (response is { } value) held.TrySetResult(value);
        }
    }
}
