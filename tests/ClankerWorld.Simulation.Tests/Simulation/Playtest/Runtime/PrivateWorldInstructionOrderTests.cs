using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
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
    public async Task DirectOrderTheGameCannotActOnClosesAtOnceAndDoesNotHoldUpLaterInstructions()
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
        for (var tick = 0; tick < 3 &&
             !(world.ExportState().CompletedInstructionIds ?? []).Contains(suggestion.InstructionId); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var state = world.ExportState();
        Assert.Contains(suggestion.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.Single(state.Events, item => item.Kind == "instruction_applied" &&
            item.Detail.StartsWith(suggestion.InstructionId + ":", StringComparison.Ordinal));
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

    private static async Task AdvanceWithHostedDecisionsAsync(PrivateWorldRuntime world)
    {
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        // Give a started hosted decision time to finish before the next tick.
        await Task.Delay(5);
    }
}
