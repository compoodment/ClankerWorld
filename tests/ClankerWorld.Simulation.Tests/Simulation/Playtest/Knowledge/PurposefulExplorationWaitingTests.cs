using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PurposefulExplorationTests
{
    [Fact]
    public async Task AChosenResourcePurposeKeepsMovingWhileItsNextPersonalReplyIsHeldAcrossPauseAndReplay()
    {
        var (state, actor, _, source, _) = await MaterialCourse();
        state = UnpaidWoodProject(state, actor);
        const string choice = "explore_for:resource:wood";
        var provider = new HeldScoutProvider(choice, holdFromCall: 2);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new ScoutProvider("safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var goal = Assert.IsType<SettlementExplorationGoal>(Person(world, actor).Exploration!.Goal);
        Assert.Equal(new SettlementExplorationGoal("resource", "wood"), goal);
        Assert.Equal(choice, world.ExportState().Society.Cognition.Runtimes.Single(item => item.InhabitantId == actor).CurrentIntention!.CandidateId);
        world.SubmitInstruction(new("held-scout-suggestion", "owner:test", actor, OwnerInstructionKind.Suggestive, "Think about your next task."));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var pending = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickNonBlockingAsync(() => false)).Advanced);
        Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var position = Person(world, actor).Position;
        var count = Person(world, actor).Exploration!.OutingPath.Count;
        var completions = world.ExportState().Events.Count(item => item.Kind == "hosted_decision_completed");
        for (var tick = 0; tick < 5; tick++)
        {
            var step = await world.AdvanceOneTickNonBlockingAsync();
            Assert.True(step.Advanced);
            Assert.DoesNotContain(step.Decisions, item => item.InhabitantId == actor);
            Assert.Equal(goal, Person(world, actor).Exploration!.Goal);
            Assert.DoesNotContain(world.ExportState().Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == source.Position);
        }
        Assert.NotEqual(position, Person(world, actor).Position);
        Assert.True(Person(world, actor).Exploration!.OutingPath.Count > count);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal(completions, world.ExportState().Events.Count(item => item.Kind == "hosted_decision_completed"));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "wood");
        Assert.Equal(0, Person(world, actor).Project!.WorkDone);
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(paused));
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(paused),
            id => id == actor ? new HeldScoutProvider(choice) : new ScoutProvider("safe_idle"));
        using var duplicate = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(paused),
            id => id == actor ? new HeldScoutProvider(choice) : new ScoutProvider("safe_idle"));
        replay.Resume();
        duplicate.Resume();
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await replay.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.True((await duplicate.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(replay.ExportState()), PrivateWorldRuntimeCodec.Encode(duplicate.ExportState()));
        }
        replay.Validate();
    }

    private static PrivateWorldRuntimeState UnpaidWoodProject(PrivateWorldRuntimeState state, string actor)
    {
        // Controlled unpaid starting plan; all purposes and movements are admitted natively.
        var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "house-1x1");
        state = FarmFieldTests.WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != "wood").ToArray(),
        });
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Project = new(TownConstructionCandidateIds.Building(definition.CanonicalId, person.Position),
                    "Build a House", state.Society.Society.WorldTick, "paused",
                    LastTransitionTick: state.Society.Society.WorldTick, RequiresFreshChoice: true),
            } : person).ToArray(),
        };
    }

    private sealed class HeldScoutProvider(string choice, int holdFromCall = 1) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request.Observation);
            if (Requests.Count >= holdFromCall)
            {
                Started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            Assert.Contains(request.Observation.Candidates, candidate => candidate.Id == choice);
            return new(request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                choice, 1d, request.Observation.Candidates.ToDictionary(item => item.Id,
                    item => item.Id == choice ? 1d : 0d, StringComparer.Ordinal));
        }
    }
}
