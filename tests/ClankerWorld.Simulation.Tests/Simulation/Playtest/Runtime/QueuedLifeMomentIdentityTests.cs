using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class QueuedLifeMomentIdentityTests
{
    private const string Actor = "founder:00000000000000000000000000000001";
    private const string FirstPersonality = "Patient and curious";
    private const string LaterPersonality = "More willing to take chances";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedOrdinaryWorkWaitsForAnActiveLifeMomentThenUsesItsIdentity(bool midlife)
    {
        using var initial = NormalPathWorld.CreateGenerated("queued-life-moment", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = initial.ExportState();
        var checkpoint = state.Society.Society;
        var age = midlife ? 30 : 20;
        state = state with
        {
            Society = state.Society with
            {
                Society = checkpoint with
                {
                    Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == Actor ? person with
                    {
                        BirthTick = checkpoint.WorldTick - age * (long)checkpoint.Config.TicksPerWorldDay + 1,
                        BirthLifeTick = null,
                        LastLifecycleYearChecked = age - 1,
                        NeedsName = false,
                    } : person).ToArray(),
                    Inventory = checkpoint.Inventory with
                    {
                        Lots = checkpoint.Inventory.Lots.Where(lot => !InventoryContainerRules.IsFood(lot.ItemKind)).ToArray(),
                    },
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { HungerBasisPoints = 7_010, IdentityChoicePending = true } : person).ToArray(),
        };
        var provider = new LifeMomentProvider();
        using var world = PrivateWorldRuntime.Restore(state, id => id == Actor
            ? provider : new ActionCoverageRecorder(chooseIdle: true));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync(cancellationToken: deadline.Token)).Advanced);
            await provider.InitialStarted.Task.WaitAsync(deadline.Token);
            var original = Assert.Single(provider.InitialRequests);
            Assert.DoesNotContain(original.Candidates, candidate => candidate.Id is "seek_food" or "harvest_food");
            for (var tick = 0; tick < 2; tick++)
                Assert.True((await world.AdvanceOneTickNonBlockingAsync(cancellationToken: deadline.Token)).Advanced);
            Assert.Contains(Assert.Single(world.ExportState().Society.Cognition.Queue,
                entry => entry.InhabitantId == Actor).Observation.Candidates,
                candidate => candidate.Id is "seek_food" or "harvest_food");
            Assert.Empty(provider.RoutineRequests);

            provider.ReleaseInitial.TrySetResult();
            for (var tick = 0; tick < 20 && world.Inhabitants.Single(person => person.InhabitantId == Actor).IdentityChoicePending; tick++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync(cancellationToken: deadline.Token)).Advanced);
                await Task.Delay(5, deadline.Token);
            }
            Assert.False(world.Inhabitants.Single(person => person.InhabitantId == Actor).IdentityChoicePending);
            Assert.Equal(FirstPersonality, world.Inhabitants.Single(person => person.InhabitantId == Actor).Personality);
            Assert.Empty(provider.RoutineRequests);
            Assert.Single(world.ExportState().Society.Cognition.Queue, entry => entry.InhabitantId == Actor);

            if (midlife)
            {
                await provider.MomentStarted.Task.WaitAsync(deadline.Token);
                Assert.Equal("requested", Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == Actor).IdentityMoments!).Outcome);
                for (var tick = 0; tick < 2; tick++)
                {
                    var step = await world.AdvanceOneTickNonBlockingAsync(cancellationToken: deadline.Token);
                    Assert.True(step.Advanced);
                    Assert.DoesNotContain(step.Decisions, decision => decision.InhabitantId == Actor);
                    Assert.Empty(provider.RoutineRequests);
                    Assert.Single(world.ExportState().Society.Cognition.Queue, entry => entry.InhabitantId == Actor);
                }
                provider.ReleaseMoment.TrySetResult();
                for (var tick = 0; tick < 20 && world.Inhabitants.Single(person => person.InhabitantId == Actor).Personality != LaterPersonality; tick++)
                {
                    Assert.True((await world.AdvanceOneTickNonBlockingAsync(cancellationToken: deadline.Token)).Advanced);
                    await Task.Delay(5, deadline.Token);
                }
                Assert.Equal(LaterPersonality, world.Inhabitants.Single(person => person.InhabitantId == Actor).Personality);
                Assert.Single(provider.MomentRequests);
            }
            else Assert.Empty(provider.MomentRequests);

            Assert.True((await world.AdvanceOneTickNonBlockingAsync(cancellationToken: deadline.Token)).Advanced);
            var fresh = Assert.Single(provider.RoutineRequests);
            Assert.Equal(midlife ? LaterPersonality : FirstPersonality, fresh.Self!.Personality);
            Assert.Equal(world.WorldTick, fresh.WorldTick);
            Assert.DoesNotContain(world.ExportState().Society.Cognition.Queue, entry => entry.InhabitantId == Actor);
            Assert.Single(provider.InitialRequests);
            world.Validate();
        }
        finally
        {
            provider.ReleaseInitial.TrySetResult();
            provider.ReleaseMoment.TrySetResult();
        }
    }

    private sealed class LifeMomentProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<InhabitantObservation> InitialRequests { get; } = new();
        public ConcurrentQueue<InhabitantObservation> MomentRequests { get; } = new();
        public ConcurrentQueue<InhabitantObservation> RoutineRequests { get; } = new();
        public TaskCompletionSource InitialStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MomentStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseInitial { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseMoment { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DecisionProviderKind KindFor(InhabitantObservation observation) =>
            observation.NeedsPersonality || observation.NeedsAspiration
                ? DecisionProviderKind.LargeLanguageModel : DecisionProviderKind.Deterministic;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            string? personality = null;
            var selected = "safe_idle";
            if (observation.IdentityMoment is not null)
            {
                MomentRequests.Enqueue(observation);
                MomentStarted.TrySetResult();
                await ReleaseMoment.Task.WaitAsync(cancellationToken);
                personality = LaterPersonality;
                selected = "identity_optional";
            }
            else if (KindFor(observation) == DecisionProviderKind.LargeLanguageModel)
            {
                InitialRequests.Enqueue(observation);
                InitialStarted.TrySetResult();
                await ReleaseInitial.Task.WaitAsync(cancellationToken);
                personality = FirstPersonality;
            }
            else RoutineRequests.Enqueue(observation);
            return new(request.RequestId, observation.InhabitantId, KindFor(observation), ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected ? 1d : 0d),
                ChosenPersonality: personality, ChosenAspiration: personality is null ? null : "Explore the riverbanks");
        }
    }
}
