using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    private const string OrderedAgent = "founder-ilya";

    [Theory]
    [InlineData("heat the house", false)]
    [InlineData("be good", false)]
    [InlineData("that was long ago", false)]
    [InlineData("build a house", false)]
    [InlineData("Eat!", true)]
    [InlineData("gathering berries", true)]
    [InlineData("go to the berry patch", true)]
    [InlineData("are you hungry?", true)]
    public void DirectOrdersAreReadAsWholeWords(string text, bool understood)
    {
        using var world = new PrivateWorldRuntime("whole-word-orders");
        var order = world.SubmitInstruction(new OwnerInstructionRequest("order", "owner:test",
            OrderedAgent, OwnerInstructionKind.MustDo, text));

        var state = world.ExportState();
        var closed = state.Events.Where(item => item.Kind == "instruction_not_understood").ToArray();
        if (understood)
        {
            Assert.DoesNotContain(order.InstructionId, state.CompletedInstructionIds ?? []);
            Assert.Empty(closed);
        }
        else
        {
            Assert.Contains(order.InstructionId, state.CompletedInstructionIds ?? []);
            Assert.Single(closed);
        }
    }

    [Fact]
    public async Task DirectOrderTheGameCannotActOnClosesAtOnceAndLocalChoicesDoNotMarkSuggestionsHeard()
    {
        var provider = new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true);
        using var world = new PrivateWorldRuntime("unknown-order", _ => provider);
        for (var tick = 0; tick < 5; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var order = world.SubmitInstruction(new OwnerInstructionRequest("build-house", "owner:test",
            OrderedAgent, OwnerInstructionKind.MustDo, "build a house"));
        var closed = world.ExportState();
        Assert.Contains(order.InstructionId, closed.CompletedInstructionIds ?? []);
        var notUnderstood = Assert.Single(closed.Events, item => item.Kind == "instruction_not_understood");
        Assert.Equal(OrderedAgent + ":" + order.InstructionId, notUnderstood.Detail);
        Assert.NotNull(notUnderstood.Position);

        var suggestion = world.SubmitInstruction(new OwnerInstructionRequest("rest", "owner:test",
            OrderedAgent, OwnerInstructionKind.Suggestive, "rest"));
        for (var tick = 0; tick < 3; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var state = world.ExportState();
        var savedSuggestion = Assert.Single(state.Instructions!, item => item.InstructionId == suggestion.InstructionId);
        Assert.DoesNotContain(suggestion.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.Null(savedSuggestion.ObservedTick);
        Assert.Null(savedSuggestion.ObserverReply);
        Assert.NotNull(savedSuggestion.GuidancePromptedTick);
        Assert.DoesNotContain(state.Events, item => item.Kind == "instruction_applied" &&
            item.Detail.StartsWith(order.InstructionId + ":", StringComparison.Ordinal));
        world.Validate();

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(order.InstructionId, restored.ExportState().CompletedInstructionIds ?? []);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "instruction_not_understood");
        restored.Validate();
    }

    [Fact]
    public async Task UnheardSuggestionDoesNotBlockRecognizedMustDoWithoutAPersonalModel()
    {
        using var genesis = new PrivateWorldRuntime("unheard-suggestion-before-order");
        var state = genesis.ExportState();
        const string foodLotId = "guidance-test-berries";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            foodLotId, "berries", OrderedAgent, 1);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == OrderedAgent
                ? item with { HungerBasisPoints = 3_000 }
                : item).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
        };
        var provider = new GuidanceRecordingProvider();
        using var world = PrivateWorldRuntime.Restore(state, id =>
            id == OrderedAgent ? provider : new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));

        var suggestion = world.SubmitInstruction(new OwnerInstructionRequest(
            "unheard-suggestion", "owner:test", OrderedAgent, OwnerInstructionKind.Suggestive,
            "Try the berries when you feel like it."));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var afterSuggestion = world.ExportState();
        var pendingSuggestion = Assert.Single(afterSuggestion.Instructions!, item => item.InstructionId == suggestion.InstructionId);
        Assert.NotNull(pendingSuggestion.GuidancePromptedTick);
        Assert.Null(pendingSuggestion.ObservedTick);
        Assert.DoesNotContain(suggestion.InstructionId, afterSuggestion.CompletedInstructionIds ?? []);

        var callsAfterSuggestion = provider.Requests.Count;
        for (var tick = 0; tick < 4; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(callsAfterSuggestion, provider.Requests.Count);

        var order = world.SubmitInstruction(new OwnerInstructionRequest(
            "recognized-order-after-suggestion", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo,
            "Please eat the food now."));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var stateAfterOrder = world.ExportState();
        Assert.Contains(order.InstructionId, stateAfterOrder.CompletedInstructionIds ?? []);
        Assert.DoesNotContain(suggestion.InstructionId, stateAfterOrder.CompletedInstructionIds ?? []);
        Assert.Null(Assert.Single(stateAfterOrder.Instructions!, item => item.InstructionId == suggestion.InstructionId).ObservedTick);
        var orderPrompt = Assert.Single(provider.Requests, request =>
            request.ObserverGuidance?.Any(message => message.InstructionId == order.InstructionId) == true);
        Assert.Contains(orderPrompt.ObserverGuidance!, message => message.InstructionId == suggestion.InstructionId);
        Assert.DoesNotContain(stateAfterOrder.Society.Society.Inventory.Lots,
            lot => lot.Id == foodLotId);
        Assert.Contains(stateAfterOrder.Events, item => item.Kind == "food_consumed" && item.Detail == OrderedAgent);
        Assert.Single(stateAfterOrder.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == order.InstructionId + ":consume_food");
        world.Validate();
    }

    [Fact]
    public async Task GuidanceIsTargetOnlyAndItsExactWordsChangeTheObservationDigest()
    {
        var targetProvider = new GuidanceRecordingProvider();
        var otherProvider = new GuidanceRecordingProvider();
        using var first = new PrivateWorldRuntime("guidance-digest", id => id switch
        {
            OrderedAgent => targetProvider,
            "founder-mira" => otherProvider,
            _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true),
        });
        _ = first.SubmitInstruction(new OwnerInstructionRequest("digest-message", "owner:test",
            OrderedAgent, OwnerInstructionKind.Suggestive, "Try the berries beside the river."));
        Assert.True((await first.AdvanceOneTickAsync()).Advanced);

        var targetObservation = Assert.Single(targetProvider.Requests);
        Assert.Equal("Try the berries beside the river.", Assert.Single(targetObservation.ObserverGuidance!).Text);
        Assert.Empty(Assert.Single(otherProvider.Requests).ObserverGuidance ?? []);

        var changedProvider = new GuidanceRecordingProvider();
        using var changed = new PrivateWorldRuntime("guidance-digest", id => id == OrderedAgent
            ? changedProvider
            : new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        _ = changed.SubmitInstruction(new OwnerInstructionRequest("digest-message", "owner:test",
            OrderedAgent, OwnerInstructionKind.Suggestive, "Try the berries beside the old bridge."));
        Assert.True((await changed.AdvanceOneTickAsync()).Advanced);

        Assert.NotEqual(targetObservation.ObservationDigest, Assert.Single(changedProvider.Requests).ObservationDigest);
    }

    [Fact]
    public async Task DirectOrderQueuedBeforeCurrentMatchingIsClosedOnTheNextTick()
    {
        var provider = new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true);
        using var world = new PrivateWorldRuntime("earlier-order", _ => provider);
        var order = world.SubmitInstruction(new OwnerInstructionRequest("heat", "owner:test",
            OrderedAgent, OwnerInstructionKind.MustDo, "heat the house"));
        // Earlier substring matching read "heat" as "eat" and left this pending.
        var queuedEarlier = world.ExportState() with { CompletedInstructionIds = [] };

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(queuedEarlier)), _ => provider);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

        var state = restored.ExportState();
        Assert.Contains(order.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.Equal(2, state.Events.Count(item => item.Kind == "instruction_not_understood" &&
            item.Detail == OrderedAgent + ":" + order.InstructionId));
        Assert.DoesNotContain(state.Events, item => item.Kind == "instruction_applied");
        restored.Validate();
    }

    [Fact]
    public async Task BlockedDirectOrderPromptsOneDecisionThenWaitsForTheUsualPace()
    {
        const int ticks = 120;
        var controlCalls = await DecisionsDuringBlockedOrderAsync(submitOrder: false, ticks);
        var orderedCalls = await DecisionsDuringBlockedOrderAsync(submitOrder: true, ticks);

        Assert.True(orderedCalls <= controlCalls + 1,
            $"The blocked order led to {orderedCalls} decisions in {ticks} ticks; without it there were {controlCalls}.");
    }

    [Fact]
    public async Task BlockedDirectOrderDoesNotStartAHostedDecisionEveryTick()
    {
        var hosted = new CountingSelectingProvider(DecisionProviderKind.LargeLanguageModel, chooseIdle: true);
        var local = new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true);
        using var world = new PrivateWorldRuntime("must-do-illegal-review", id => id == OrderedAgent ? hosted : local);
        for (var tick = 0; tick < 5; tick++)
            await AdvanceWithHostedDecisionsAsync(world);

        var callsBefore = hosted.CallCount;
        var order = world.SubmitInstruction(new OwnerInstructionRequest("hosted-eat", "owner:test",
            OrderedAgent, OwnerInstructionKind.MustDo, "eat food"));
        for (var tick = 0; tick < 60; tick++)
            await AdvanceWithHostedDecisionsAsync(world);

        var hostedCalls = hosted.CallCount - callsBefore;
        Assert.DoesNotContain(order.InstructionId, world.ExportState().CompletedInstructionIds ?? []);
        Assert.InRange(hostedCalls, 1, 10);
    }

    private static async Task<int> DecisionsDuringBlockedOrderAsync(bool submitOrder, int ticks)
    {
        // This founder carries no food here, so "eat food" cannot progress.
        var ordered = new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true);
        var others = new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true);
        using var world = new PrivateWorldRuntime("must-do-illegal-review",
            id => id == OrderedAgent ? ordered : others);
        for (var tick = 0; tick < 5; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var callsBefore = ordered.CallCount;
        OwnerInstructionReceipt? order = submitOrder
            ? world.SubmitInstruction(new OwnerInstructionRequest("blocked-eat", "owner:test",
                OrderedAgent, OwnerInstructionKind.MustDo, "eat food"))
            : null;
        for (var tick = 0; tick < ticks; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        if (order is not null)
        {
            Assert.DoesNotContain(order.InstructionId, world.ExportState().CompletedInstructionIds ?? []);
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind is "instruction_applied" or "instruction_not_understood");
        }
        return ordered.CallCount - callsBefore;
    }

    private sealed class GuidanceRecordingProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 1;
        public List<InhabitantObservation> Requests { get; } = [];

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.Observation);
            var selected = request.Observation.Candidates
                .OrderBy(candidate => candidate.Id == "safe_idle" ? int.MinValue : candidate.DeterministicPriority)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .First();
            return ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1d,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }

    private static async Task AdvanceWithHostedDecisionsAsync(PrivateWorldRuntime world)
    {
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        // Give a started hosted decision time to finish before the next tick.
        await Task.Delay(5);
    }
}
