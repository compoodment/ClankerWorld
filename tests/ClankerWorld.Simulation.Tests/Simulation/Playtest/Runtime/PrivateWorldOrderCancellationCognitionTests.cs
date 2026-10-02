using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Fact]
    public async Task CancellingAnAdmittedTravelOrderRequestsOrdinaryPlanningInsteadOfContinuingItsSeekFood()
    {
        var provider = new CancellationPlanningProvider();
        using var world = CreateCancellationTravelWorld(provider);
        var before = world.ExportState();
        var order = SubmitCancellationTravelOrder(world, "admitted-cancel-travel");

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var admitted = world.ExportState();
        var intention = Assert.Single(admitted.Society.Cognition.Runtimes,
            item => item.InhabitantId == HarvestInstructionActor).CurrentIntention!;
        Assert.Equal("seek_food", intention.CandidateId);
        Assert.Equal(order.InstructionId, intention.OperativeOrderInstructionId);
        var position = CancellationActorPosition(admitted);
        Assert.NotEqual(CancellationActorPosition(before), position);
        Assert.Equal("doing", CancellationOrder(admitted, order.InstructionId).Status);

        CancelTravelOrder(world, order.InstructionId, "cancel-admitted-travel");
        for (var tick = 0; tick < 5; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(position, CancellationActorPosition(world.ExportState()));
        }

        Assert.Single(provider.Requests, request => request.OperativeOrderInstructionId == order.InstructionId);
        var ordinary = Assert.Single(provider.Requests, request => request.OperativeOrderInstructionId is null);
        // Food seeking remains legal: the stop comes from a fresh personal idle
        // choice, rather than fullness, arrival, depletion or a blocked route.
        Assert.Contains(ordinary.Candidates, candidate => candidate.Id == "seek_food");
        Assert.Equal("cancelled", CancellationOrder(world.ExportState(), order.InstructionId).Status);
        Assert.Equal(0, CancellationOrder(world.ExportState(), order.InstructionId).CompletedUnits);
        world.Validate();
    }

    [Fact]
    public async Task ACancelledHeldTravelReplyCannotInstallSeekFoodThatMovesOnFollowingTicks()
    {
        var provider = new CancellationPlanningProvider(holdFirstOrder: true);
        using var world = CreateCancellationTravelWorld(provider);
        var order = SubmitCancellationTravelOrder(world, "held-cancel-travel");
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var position = CancellationActorPosition(world.ExportState());
            Assert.Equal("doing", CancellationOrder(world.ExportState(), order.InstructionId).Status);
            CancelTravelOrder(world, order.InstructionId, "cancel-held-travel");

            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // The fresh ordinary request starts on a background task, so a slow runner
            // may need a few more ticks; the agent must stay put on every one of them.
            for (var tick = 0; tick < 8 || tick < 60 &&
                     !provider.Requests.Any(request => request.OperativeOrderInstructionId is null); tick++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
                Assert.Equal(position, CancellationActorPosition(world.ExportState()));
                if (tick >= 8) await Task.Delay(10);
            }

            Assert.Single(provider.Requests, request => request.OperativeOrderInstructionId == order.InstructionId);
            Assert.Single(provider.Requests, request => request.OperativeOrderInstructionId is null);
            Assert.Equal("cancelled", CancellationOrder(world.ExportState(), order.InstructionId).Status);
            Assert.Equal(0, CancellationOrder(world.ExportState(), order.InstructionId).CompletedUnits);
            world.Validate();
        }
        finally
        {
            provider.Release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task CancellingAHeldTravelOrderCanDispatchOrdinaryPlanningBeforeTheObsoleteProviderReturns()
    {
        var provider = new CancellationPlanningProvider(holdFirstOrder: true);
        using var world = CreateCancellationTravelWorld(provider);
        var order = SubmitCancellationTravelOrder(world, "unreturned-cancel-travel");
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var position = CancellationActorPosition(world.ExportState());
            CancelTravelOrder(world, order.InstructionId, "cancel-unreturned-travel");

            for (var tick = 0; tick < 5; tick++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
                Assert.Equal(position, CancellationActorPosition(world.ExportState()));
            }

            Assert.False(provider.Returned.Task.IsCompleted);
            var ordinary = Assert.Single(provider.Requests, request => request.OperativeOrderInstructionId is null);
            Assert.Contains(ordinary.Candidates, candidate => candidate.Id == "seek_food");
            Assert.Single(provider.Requests, request => request.OperativeOrderInstructionId == order.InstructionId);
            Assert.Equal("cancelled", CancellationOrder(world.ExportState(), order.InstructionId).Status);
            world.Validate();
        }
        finally
        {
            provider.Release.TrySetResult(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PausedHeldTravelOrderRefreshesItsPersonalRequestInTheResumedEpoch(bool reload)
    {
        var provider = new CancellationPlanningProvider(holdFirstOrder: true);
        using var world = CreateCancellationTravelWorld(provider);
        var order = SubmitCancellationTravelOrder(world, "paused-held-travel");
        PrivateWorldRuntime? restored = null;
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var originalRequest = Assert.Single(provider.Requests);
            world.Pause();
            var paused = world.ExportState();
            Assert.Contains(paused.Society.Cognition.Queue, entry =>
                entry.InhabitantId == HarvestInstructionActor &&
                entry.Observation.OperativeOrderInstructionId == order.InstructionId);
            var resumeWorld = world;
            if (reload)
            {
                restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
                    PrivateWorldRuntimeCodec.Encode(paused)), CancellationProviderFactory(provider));
                resumeWorld = restored;
            }
            resumeWorld.Resume();
            for (var tick = 0; tick < 5; tick++)
                Assert.True((await resumeWorld.AdvanceOneTickNonBlockingAsync()).Advanced);

            var freshRequest = Assert.Single(provider.Requests, request => request.RunEpoch != originalRequest.RunEpoch);
            Assert.Equal(resumeWorld.Society.RunEpoch, freshRequest.RunEpoch);
            Assert.Equal(order.InstructionId, freshRequest.OperativeOrderInstructionId);
            Assert.Contains(freshRequest.ObserverGuidance!, message => message.InstructionId == order.InstructionId);
            Assert.NotNull(Assert.Single(resumeWorld.ExportState().Instructions!,
                instruction => instruction.InstructionId == order.InstructionId).ObservedTick);
            Assert.Equal(2, provider.Requests.Count);
            Assert.False(provider.Returned.Task.IsCompleted);
            resumeWorld.Validate();
        }
        finally
        {
            provider.Release.TrySetResult(true);
            restored?.Dispose();
        }
    }

    private static PrivateWorldRuntime CreateCancellationTravelWorld(CancellationPlanningProvider provider)
    {
        using var setup = CreateKnownBerryOrderWorld(_ =>
            new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var state = setup.ExportState();
        var source = Assert.Single(state.Map.Resources, resource => resource.Id == "berry-patch");
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != HarvestInstructionActor)
            .Select(person => person.Position).ToHashSet();
        occupied.UnionWith(state.WorldSimulation!.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(
                setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId),
                building.Position)));
        var start = state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => state.Map.IsPassable(point) && !occupied.Contains(point) &&
                state.Map.FootDistance(point, source.Position) is >= 14 and <= 20 &&
                !state.Map.Resources.Any(resource => resource.Position == point))
            .OrderBy(point => state.Map.FootDistance(point, source.Position)).ThenBy(point => point.Y).ThenBy(point => point.X)
            .First(point => DeterministicRouteFinder.TryFind(state.Map, point, source.Position, out var route) &&
                route.All(step => !occupied.Contains(step)));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { Position = start, HungerBasisPoints = 6_000, TravelCooldownTicks = 0 }
                : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = WithoutFoodLots(state.Society.Society.Inventory),
                },
            },
        };
        return PrivateWorldRuntime.Restore(state, CancellationProviderFactory(provider));
    }

    private static Func<string, IDecisionProvider> CancellationProviderFactory(CancellationPlanningProvider provider) =>
        id => id == HarvestInstructionActor ? provider :
            new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true);

    private static OwnerInstructionReceipt SubmitCancellationTravelOrder(PrivateWorldRuntime world, string key) =>
        world.SubmitInstruction(new OwnerInstructionRequest(key, "owner:test", HarvestInstructionActor,
            OwnerInstructionKind.MustDo, "go to the berry patch"));

    private static void CancelTravelOrder(PrivateWorldRuntime world, string orderId, string key) =>
        Assert.True(world.CancelOrder(new OwnerOrderCancelRequest(key, "owner:test", world.Society.WorldId,
            HarvestInstructionActor, orderId)).Changed);

    private static GridPoint CancellationActorPosition(PrivateWorldRuntimeState state) =>
        state.Inhabitants.Single(person => person.InhabitantId == HarvestInstructionActor).Position;

    private static OwnerInstructionOrder CancellationOrder(PrivateWorldRuntimeState state, string orderId) =>
        Assert.Single(state.Instructions!, instruction => instruction.InstructionId == orderId).Order!;

    private sealed class CancellationPlanningProvider(bool holdFirstOrder = false) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            Requests.Enqueue(request.Observation);
            if (holdFirstOrder && Requests.Count == 1 && request.Observation.OperativeOrderInstructionId is not null)
            {
                Started.TrySetResult(true);
                // Simulate a remote request that does not acknowledge cancellation.
                // A new valid request must not depend on this obsolete reply.
                await Release.Task;
                Returned.TrySetResult(true);
            }
            return HostedResponse(request, Kind, ProviderEpoch,
                request.Observation.OperativeOrderInstructionId is null ? "safe_idle" : "seek_food");
        }
    }
}
