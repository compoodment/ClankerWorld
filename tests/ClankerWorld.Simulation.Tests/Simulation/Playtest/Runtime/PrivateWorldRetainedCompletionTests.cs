using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Fact]
    public async Task AnAcceptedReplyWithNewerQueuedWorkTakesNoSafeRoutineTheSameTick()
    {
        var provider = new HeldFirstReplyProvider("seek_food");
        using var setup = CreateKnownBerryOrderWorld(_ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var farStand = FarFromBerries(state, 15, 60);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == HarvestInstructionActor
                ? item with { Position = farStand, HungerBasisPoints = 0 }
                : item).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = WithoutFoodLots(state.Society.Society.Inventory) },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, id => id == HarvestInstructionActor
            ? provider
            : new ActionCoverageRecorder(chooseIdle: true));
        GridPoint Position() => world.Inhabitants.Single(item => item.InhabitantId == HarvestInstructionActor).Position;
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        // Newer guidance keeps the actor queued after its held reply is accepted.
        world.SubmitInstruction(new OwnerInstructionRequest("retained-suggestion", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.Suggestive, "Find something to eat."));
        for (var tick = 0; tick < 3; tick++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        provider.Release.TrySetResult(true);
        await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(50);

        var before = Position();
        var completion = await world.AdvanceOneTickNonBlockingAsync();
        Assert.Contains(completion.Decisions, item => item.InhabitantId == HarvestInstructionActor &&
            item.Admission.Intention?.CandidateId == "seek_food");
        Assert.Contains(world.ExportState().Society.Cognition.Queue, entry => entry.InhabitantId == HarvestInstructionActor);
        Assert.True(world.ExportState().Map.FootDistance(before, Position()) <= 1);
        Assert.True(completion.Events.Count(item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal)) <= 1);
    }

    [Fact]
    public async Task ASuggestionQueuedBehindAFinishedOrderReachesAFreshRequest()
    {
        var provider = new HeldFirstReplyProvider("seek_food", replyToSuggestions: true);
        using var setup = CreateKnownBerryOrderWorld(_ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var farStand = FarFromBerries(state, 7, 40);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == HarvestInstructionActor
                ? item with { Position = farStand, HungerBasisPoints = 10_000 }
                : item).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, id => id == HarvestInstructionActor
            ? provider
            : new DeterministicDecisionProvider());
        var order = world.SubmitInstruction(new OwnerInstructionRequest("held-harvest", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "gather 2 berries from berry-patch"));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var suggestion = world.SubmitInstruction(new OwnerInstructionRequest("behind-finished-order", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.Suggestive, "Please rest a little afterwards."));
        for (var tick = 0; tick < 80 && Assert.Single(world.ExportState().Instructions!,
                 item => item.InstructionId == order.InstructionId).Order!.Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal("finished", Assert.Single(world.ExportState().Instructions!,
            item => item.InstructionId == order.InstructionId).Order!.Status);
        Assert.Equal(1, provider.CallCount);

        provider.Release.TrySetResult(true);
        for (var tick = 0; tick < 10 && provider.CallCount < 2; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(20);
        }
        for (var tick = 0; tick < 5; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(20);
        }

        var second = provider.Requests.ToArray()[1];
        Assert.Null(second.OperativeOrderInstructionId);
        Assert.Contains(second.ObserverGuidance!, item => item.InstructionId == suggestion.InstructionId);
        var final = world.ExportState();
        // Only the held reply for the order that has since finished is set aside.
        Assert.Single(final.Events, item => item.Kind == "instruction_order_stale_decision" &&
            item.Detail == HarvestInstructionActor);
        Assert.Contains(suggestion.InstructionId, final.CompletedInstructionIds ?? []);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task AnOrderThatFinishesBeforeItsRequestIsSentStillReachesTheModel()
    {
        var actorProvider = new ImmediateOrderReplyProvider();
        var blocker = new HoldingProvider();
        using var setup = CreateKnownBerryOrderWorld(_ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var farStand = FarFromBerries(state, 7, 40);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == HarvestInstructionActor
                ? item with { Position = farStand, HungerBasisPoints = 10_000 }
                : item).ToArray(),
        };
        var blockerId = state.Society.Society.Inhabitants
            .Where(item => item.Id != HarvestInstructionActor && item.AgeBand != ClankerWorld.Simulation.Society.SocietyAgeBand.Infant)
            .Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal).First();
        using var world = PrivateWorldRuntime.Restore(state, id => id == HarvestInstructionActor
            ? actorProvider
            : id == blockerId ? blocker : new DeterministicDecisionProvider(), maxCognitionDispatchPerCycle: 1);
        for (var tick = 0; tick < 400 && !blocker.Started.Task.IsCompleted; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(5);
        }
        Assert.True(blocker.Started.Task.IsCompleted);
        var callsBefore = actorProvider.CallCount;
        var order = world.SubmitInstruction(new OwnerInstructionRequest("queued-harvest", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "gather 2 berries from berry-patch"));
        for (var tick = 0; tick < 80 && Assert.Single(world.ExportState().Instructions!,
                 item => item.InstructionId == order.InstructionId).Order!.Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal("finished", Assert.Single(world.ExportState().Instructions!,
            item => item.InstructionId == order.InstructionId).Order!.Status);
        Assert.Equal(callsBefore, actorProvider.CallCount);
        // One more tick after it finished while still queued.
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);

        blocker.Release.TrySetResult(true);
        for (var tick = 0; tick < 12; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(20);
        }
        var saved = Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == order.InstructionId);
        var sawOrder = actorProvider.Requests.Any(request =>
            request.ObserverGuidance?.Any(message => message.InstructionId == order.InstructionId) == true);
        Assert.True(sawOrder, $"order never reached the model; calls={actorProvider.CallCount - callsBefore} observed={saved.ObservedTick} reply={saved.ObserverReply}");
        Assert.NotNull(saved.ObservedTick);
        Assert.Equal("On it.", saved.ObserverReply);
    }

    private sealed class ImmediateOrderReplyProvider : IDecisionProvider
    {
        private int callCount;
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public int CallCount => Volatile.Read(ref callCount);
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Interlocked.Increment(ref callCount);
            Requests.Enqueue(request.Observation);
            var replies = (request.Observation.ObserverGuidance ?? [])
                .Where(item => item.ReplyAllowed)
                .Select(item => new CognitionObserverReply(item.InstructionId, "On it."))
                .ToArray();
            return ValueTask.FromResult(HostedResponse(request, Kind, ProviderEpoch, "seek_food", replies));
        }
    }

    private sealed class HoldingProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return HostedResponse(request, Kind, ProviderEpoch, "safe_idle");
        }
    }

    [Fact]
    public async Task ASuggestionCarriedWithAFinishedOrderReachesALaterFreshRequest()
    {
        var actorProvider = new ImmediateOrderReplyProvider();
        var blocker = new HoldingProvider();
        using var setup = CreateKnownBerryOrderWorld(_ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var farStand = FarFromBerries(state, 7, 40);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == HarvestInstructionActor
                ? item with { Position = farStand, HungerBasisPoints = 10_000 }
                : item).ToArray(),
        };
        var blockerId = state.Society.Society.Inhabitants
            .Where(item => item.Id != HarvestInstructionActor && item.AgeBand != ClankerWorld.Simulation.Society.SocietyAgeBand.Infant)
            .Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal).First();
        using var world = PrivateWorldRuntime.Restore(state, id => id == HarvestInstructionActor
            ? actorProvider
            : id == blockerId ? blocker : new DeterministicDecisionProvider(), maxCognitionDispatchPerCycle: 1);
        for (var tick = 0; tick < 400 && !blocker.Started.Task.IsCompleted; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(5);
        }
        Assert.True(blocker.Started.Task.IsCompleted);
        var callsBefore = actorProvider.CallCount;
        var order = world.SubmitInstruction(new OwnerInstructionRequest("queued-harvest-with-suggestion", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "gather 2 berries from berry-patch"));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        var suggestion = world.SubmitInstruction(new OwnerInstructionRequest("suggestion-with-queued-order", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.Suggestive, "Rest a little afterwards."));
        for (var tick = 0; tick < 80 && Assert.Single(world.ExportState().Instructions!,
                 item => item.InstructionId == order.InstructionId).Order!.Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(callsBefore, actorProvider.CallCount);

        blocker.Release.TrySetResult(true);
        for (var tick = 0; tick < 12; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(20);
        }
        var requests = actorProvider.Requests.Skip(callsBefore).ToArray();
        Assert.Contains(requests, request => request.OperativeOrderInstructionId == order.InstructionId &&
            request.ObserverGuidance?.Any(message => message.InstructionId == suggestion.InstructionId) == true);
        Assert.Contains(requests, request => request.OperativeOrderInstructionId is null &&
            request.ObserverGuidance?.Any(message => message.InstructionId == suggestion.InstructionId) == true);
        Assert.Contains(suggestion.InstructionId, world.ExportState().CompletedInstructionIds ?? []);
    }

    private static GridPoint FarFromBerries(PrivateWorldRuntimeState state, int nearest, int farthest)
    {
        var source = state.Map.Resources.Single(item => item.Id == "berry-patch");
        var occupied = state.Inhabitants.Where(item => item.InhabitantId != HarvestInstructionActor)
            .Select(item => item.Position).ToHashSet();
        return state.Map.Tiles.Select(item => item.Position)
            .Where(point => state.Map.IsPassable(point) && !occupied.Contains(point) &&
                state.Map.Resources.All(resource => resource.Position != point))
            .Select(point => (Point: point, Distance: state.Map.FootDistance(point, source.Position)))
            .Where(item => item.Distance >= nearest && item.Distance < farthest)
            .OrderByDescending(item => item.Distance).ThenBy(item => item.Point.X).ThenBy(item => item.Point.Y)
            .Select(item => item.Point).First();
    }

    // Holds the first reply until released; later calls answer at once.
    private sealed class HeldFirstReplyProvider(string firstChoice, bool replyToSuggestions = false) : IDecisionProvider
    {
        private int callCount;
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public int CallCount => Volatile.Read(ref callCount);
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var call = Interlocked.Increment(ref callCount);
            Requests.Enqueue(request.Observation);
            if (call == 1)
            {
                Started.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
                Returned.TrySetResult(true);
                return HostedResponse(request, Kind, ProviderEpoch, firstChoice);
            }
            if (!replyToSuggestions)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("Later calls stay pending in this test.");
            }
            var replies = (request.Observation.ObserverGuidance ?? [])
                .Where(item => item.Kind == "suggestive" && item.ReplyAllowed)
                .Select(item => new CognitionObserverReply(item.InstructionId, "I will rest."))
                .ToArray();
            return HostedResponse(request, Kind, ProviderEpoch, "safe_idle", replies);
        }
    }
}
