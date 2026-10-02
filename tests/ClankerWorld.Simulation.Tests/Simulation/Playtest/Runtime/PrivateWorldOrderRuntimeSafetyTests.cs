using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Theory]
    [InlineData("gather berries from berry-patch")]
    [InlineData("go to the berry patch")]
    public async Task UrgentCarriedFoodInterruptsFoodGatheringAndTravelOrders(string text)
    {
        using var setup = CreateKnownBerryOrderWorld(_ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "urgent-order-fruit", "fruit", HarvestInstructionActor, 1);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { HungerBasisPoints = 1_000 }
                : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new DeterministicDecisionProvider());
        var positionBefore = world.ExportState().Inhabitants
            .Single(item => item.InhabitantId == HarvestInstructionActor).Position;
        var order = world.SubmitInstruction(new OwnerInstructionRequest(
            "urgent-order", "owner:test", HarvestInstructionActor, OwnerInstructionKind.MustDo, text));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var after = world.ExportState();
        Assert.Single(after.Events, item => item.Kind == "food_consumed" && item.Detail == HarvestInstructionActor);
        Assert.DoesNotContain(after.Events, item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal));
        Assert.DoesNotContain(after.Society.Society.Inventory.Lots, item => item.Id == "urgent-order-fruit");
        Assert.Equal(positionBefore, after.Inhabitants.Single(item => item.InhabitantId == HarvestInstructionActor).Position);
        var savedOrder = Assert.Single(after.Instructions!, item => item.InstructionId == order.InstructionId);
        Assert.Equal("interrupted", savedOrder.Order!.Status);
        Assert.Equal(0, savedOrder.Order.CompletedUnits);
        Assert.DoesNotContain(order.InstructionId, after.CompletedInstructionIds ?? []);
        world.Validate();
    }

    [Fact]
    public async Task FullCarrierBlocksKnownFoodHarvestWithStableReasonAcrossReload()
    {
        var provider = new CountingHostedOrderProvider();
        using var setup = CreateKnownBerryOrderWorld(id => id == HarvestInstructionActor
            ? provider
            : new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var person = state.Inhabitants.Single(item => item.InhabitantId == HarvestInstructionActor);
        var inventory = FillActorCarry(state.Society.Society.Inventory, person, "full-harvest-cargo");
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == HarvestInstructionActor
                ? item with { HungerBasisPoints = 3_000 }
                : item).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, id => id == HarvestInstructionActor
            ? provider
            : new DeterministicDecisionProvider());
        var order = world.SubmitInstruction(new OwnerInstructionRequest("full-harvest", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "gather berries from berry-patch"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var blocked = world.ExportState();
        var blockedOrder = Assert.Single(blocked.Instructions!, item => item.InstructionId == order.InstructionId).Order!;
        Assert.Equal("blocked", blockedOrder.Status);
        Assert.Contains("carry", blockedOrder.BlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, provider.CallCount);
        Assert.DoesNotContain(blocked.Events, item => item.Kind is "food_harvested" or "carrying_full");
        var savedInventory = blocked.Society.Society.Inventory;

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(blocked)), id => id == HarvestInstructionActor
                ? provider
                : new DeterministicDecisionProvider());
        for (var tick = 0; tick < 5; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

        var after = restored.ExportState();
        Assert.Equal(
            savedInventory.Lots.Where(item => item.OwnerId == HarvestInstructionActor)
                .Select(item => item with { LastProcessedTick = 0 })
                .OrderBy(item => item.Id, StringComparer.Ordinal),
            after.Society.Society.Inventory.Lots.Where(item => item.OwnerId == HarvestInstructionActor)
                .Select(item => item with { LastProcessedTick = 0 })
                .OrderBy(item => item.Id, StringComparer.Ordinal));
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(after.Society.Society.Inventory,
            HarvestInstructionActor,
            after.Inhabitants.Single(item => item.InhabitantId == HarvestInstructionActor).Equipment));
        Assert.Single(after.Instructions!, item => item.InstructionId == order.InstructionId &&
            item.Order!.Status == "blocked" && item.Order.BlockedReason == blockedOrder.BlockedReason);
        Assert.Single(after.Events, item => item.Kind == "instruction_order_status" &&
            item.Detail == $"{HarvestInstructionActor}:{order.InstructionId}:blocked");
        Assert.Equal(1, provider.CallCount);
        Assert.DoesNotContain(after.Events, item => item.Kind is "food_harvested" or "carrying_full");
        restored.Validate();
    }

    [Fact]
    public async Task FullCarrierDoesNotCollectHouseholdFoodForAnEatOrder()
    {
        var provider = new CountingHostedOrderProvider();
        using var setup = CreateOrderWorldWithoutFood("full-household-food-order", id => id == OrderedAgent
            ? provider
            : new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var person = state.Inhabitants.Single(item => item.InhabitantId == OrderedAgent);
        var householdId = state.Society.Society.GetInhabitant(OrderedAgent).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "shared-order-berries", "berries", householdId, 1);
        inventory = FillActorCarry(inventory, person, "full-shared-food-cargo");
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
        using var world = PrivateWorldRuntime.Restore(state, id => id == OrderedAgent
            ? provider
            : new DeterministicDecisionProvider());
        var order = world.SubmitInstruction(new OwnerInstructionRequest("full-shared-food", "owner:test",
            OrderedAgent, OwnerInstructionKind.MustDo, "eat food"));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var blocked = world.ExportState();
        var blockedOrder = Assert.Single(blocked.Instructions!, item => item.InstructionId == order.InstructionId).Order!;
        Assert.Equal("blocked", blockedOrder.Status);
        Assert.Contains("carry", blockedOrder.BlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, Assert.Single(blocked.Society.Society.Inventory.Lots,
            item => item.Id == "shared-order-berries").Quantity);
        Assert.DoesNotContain(blocked.Events, item => item.Kind is "household_food_collected" or "food_consumed");

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(blocked)), id => id == OrderedAgent
                ? provider
                : new DeterministicDecisionProvider());
        for (var tick = 0; tick < 5; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var after = restored.ExportState();
        Assert.Equal(1, Assert.Single(after.Society.Society.Inventory.Lots,
            item => item.Id == "shared-order-berries").Quantity);
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(after.Society.Society.Inventory, OrderedAgent,
            after.Inhabitants.Single(item => item.InhabitantId == OrderedAgent).Equipment));
        Assert.Single(after.Events, item => item.Kind == "instruction_order_status" &&
            item.Detail == $"{OrderedAgent}:{order.InstructionId}:blocked");
        Assert.Equal(1, provider.CallCount);
        Assert.DoesNotContain(after.Events, item => item.Kind is "household_food_collected" or "food_consumed");
        restored.Validate();
    }

    [Fact]
    public async Task EatOrderCollectsOnlyTheRequestedHouseholdFoodKind()
    {
        using var setup = CreateOrderWorldWithoutFood("requested-household-food-kind",
            _ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var householdId = state.Society.Society.GetInhabitant(OrderedAgent).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "wrong-kind-fruit", "fruit", householdId, 1);
        inventory = InventoryFixture.AddLot(inventory,
            "requested-kind-berries", "berries", householdId, 1);
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
        using var world = PrivateWorldRuntime.Restore(state, _ => new DeterministicDecisionProvider());
        var order = world.SubmitInstruction(new OwnerInstructionRequest("requested-kind-eat", "owner:test",
            OrderedAgent, OwnerInstructionKind.MustDo, "eat berries"));

        for (var tick = 0; tick < 120 && !world.ExportState().Events.Any(item =>
                 item.Kind == "food_consumed" && item.Detail == OrderedAgent); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var after = world.ExportState();
        Assert.Contains(order.InstructionId, after.CompletedInstructionIds ?? []);
        Assert.DoesNotContain(after.Society.Society.Inventory.Lots, item => item.Id == "requested-kind-berries");
        var otherFood = Assert.Single(after.Society.Society.Inventory.Lots, item => item.Id == "wrong-kind-fruit");
        Assert.Equal(("fruit", householdId, 1), (otherFood.ItemKind, otherFood.OwnerId, otherFood.Quantity));
        Assert.Single(after.Events, item => item.Kind == "food_consumed" && item.Detail == OrderedAgent);
        Assert.Single(after.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == order.InstructionId + ":consume_food");
        world.Validate();
    }

    [Fact]
    public async Task RepeatedEatOrderWaitsForNormalHungerBeforeConsumingMoreCarriedOrHouseholdFood()
    {
        using var setup = CreateOrderWorldWithoutFood("repeat-eat-order-fullness",
            _ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var householdId = state.Society.Society.GetInhabitant(OrderedAgent).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "repeat-eat-carried-berries", "berries", OrderedAgent, 2);
        inventory = InventoryFixture.AddLot(inventory,
            "repeat-eat-household-berries", "berries", householdId, 1);
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
        using var world = PrivateWorldRuntime.Restore(state, _ => new DeterministicDecisionProvider());
        var order = world.SubmitInstruction(new OwnerInstructionRequest("repeat-eat", "owner:test",
            OrderedAgent, OwnerInstructionKind.MustDo, "eat food until cancelled"));
        Assert.True(Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == order.InstructionId)
            .Order!.RepeatUntilCancelled);

        for (var tick = 0; tick < 20; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var full = world.ExportState();
        Assert.Single(full.Events, item => item.Kind == "food_consumed" && item.Detail == OrderedAgent);
        Assert.Equal(1, Assert.Single(full.Society.Society.Inventory.Lots,
            item => item.Id == "repeat-eat-carried-berries").Quantity);
        Assert.Equal(1, Assert.Single(full.Society.Society.Inventory.Lots,
            item => item.Id == "repeat-eat-household-berries").Quantity);
        Assert.True(full.Inhabitants.Single(item => item.InhabitantId == OrderedAgent).HungerBasisPoints >= 4_000);
        var waitingOrder = Assert.Single(full.Instructions!, item => item.InstructionId == order.InstructionId).Order!;
        Assert.Equal("blocked", waitingOrder.Status);
        Assert.Contains("hungry", waitingOrder.BlockedReason, StringComparison.OrdinalIgnoreCase);

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(full)), _ => new DeterministicDecisionProvider());
        for (var tick = 0; tick < 10; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Single(restored.ExportState().Events,
            item => item.Kind == "food_consumed" && item.Detail == OrderedAgent);
        Assert.Equal(1, Assert.Single(restored.ExportState().Society.Society.Inventory.Lots,
            item => item.Id == "repeat-eat-carried-berries").Quantity);
        Assert.Equal(1, Assert.Single(restored.ExportState().Society.Society.Inventory.Lots,
            item => item.Id == "repeat-eat-household-berries").Quantity);

        for (var tick = 0; tick < 300 && restored.ExportState().Events.Count(item =>
                 item.Kind == "food_consumed" && item.Detail == OrderedAgent) < 2; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

        var resumed = restored.ExportState();
        Assert.Equal(2, resumed.Events.Count(item => item.Kind == "food_consumed" && item.Detail == OrderedAgent));
        Assert.DoesNotContain(resumed.Society.Society.Inventory.Lots,
            item => item.Id == "repeat-eat-carried-berries");
        Assert.Equal(1, Assert.Single(resumed.Society.Society.Inventory.Lots,
            item => item.Id == "repeat-eat-household-berries").Quantity);
        Assert.DoesNotContain(order.InstructionId, resumed.CompletedInstructionIds ?? []);
        restored.Validate();
    }

    [Fact]
    public async Task HeldPersonalResponseDoesNotStallOrRepeatAQuantifiedHarvestAndCanReplyAfterItFinishes()
    {
        var provider = new HeldOrderReplyProvider();
        using var setup = CreateKnownBerryOrderWorld(_ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var source = state.Map.Resources.Single(item => item.Id == "berry-patch");
        var actor = state.Inhabitants.Single(item => item.InhabitantId == HarvestInstructionActor);
        var occupied = state.Inhabitants.Where(item => item.InhabitantId != HarvestInstructionActor)
            .Select(item => item.Position).ToHashSet();
        var farStand = state.Map.Tiles.Select(item => item.Position)
            .Where(point => state.Map.IsPassable(point) && !occupied.Contains(point) &&
                state.Map.Resources.All(resource => resource.Position != point))
            .Select(point => (Point: point, Distance: state.Map.FootDistance(point, source.Position)))
            .Where(item => item.Distance is >= 7 and < 40)
            .OrderByDescending(item => item.Distance).ThenBy(item => item.Point.X).ThenBy(item => item.Point.Y)
            .Select(item => item.Point).First();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == HarvestInstructionActor
                ? item with { Position = farStand, HungerBasisPoints = 10_000 }
                : item).ToArray(),
        };
        using var restoredWorld = PrivateWorldRuntime.Restore(state, id => id == HarvestInstructionActor
            ? provider
            : new DeterministicDecisionProvider());
        var order = restoredWorld.SubmitInstruction(new OwnerInstructionRequest("held-harvest", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "gather 2 berries from berry-patch"));
        Assert.True((await restoredWorld.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var initialRequest = Assert.Single(provider.Requests);
        Assert.Equal(state.Society.Society.WorldId, initialRequest.WorldId);
        Assert.Equal(HarvestInstructionActor, initialRequest.InhabitantId);
        var exactMessage = Assert.Single(initialRequest.ObserverGuidance!, item => item.InstructionId == order.InstructionId);
        Assert.Equal("must_do", exactMessage.Kind);
        Assert.Equal("gather 2 berries from berry-patch", exactMessage.Text);
        Assert.True(exactMessage.ReplyAllowed);
        Assert.Equal("gather several food servings from a nearby food source", exactMessage.UnderstoodTask);

        for (var tick = 0; tick < 80 && Assert.Single(restoredWorld.ExportState().Instructions!,
                 item => item.InstructionId == order.InstructionId).Order!.Status != "finished"; tick++)
            Assert.True((await restoredWorld.AdvanceOneTickNonBlockingAsync()).Advanced);

        var finished = restoredWorld.ExportState();
        var finishedInstruction = Assert.Single(finished.Instructions!, item => item.InstructionId == order.InstructionId);
        Assert.Equal("finished", finishedInstruction.Order!.Status);
        Assert.True(finishedInstruction.Order.CompletedUnits >= finishedInstruction.Order.RequestedUnits);
        Assert.Null(finishedInstruction.ObservedTick);
        Assert.Equal(1, provider.CallCount);
        Assert.Single(finished.Events, item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal));

        provider.Release.TrySetResult(true);
        await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var tick = 0; tick < 5 && !restoredWorld.ExportState().Events.Any(item =>
                 item.Kind == "hosted_decision_completed" && item.Detail == HarvestInstructionActor); tick++)
            Assert.True((await restoredWorld.AdvanceOneTickNonBlockingAsync()).Advanced);

        var afterReply = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(restoredWorld.ExportState()));
        var repliedInstruction = Assert.Single(afterReply.Instructions!, item => item.InstructionId == order.InstructionId);
        Assert.Equal("finished", repliedInstruction.Order!.Status);
        Assert.Equal(finishedInstruction.Order.CompletedUnits, repliedInstruction.Order.CompletedUnits);
        Assert.Equal(1, afterReply.Events.Count(item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal)));
        Assert.NotNull(repliedInstruction.ObservedTick);
        Assert.Equal("I will gather the berries.", repliedInstruction.ObserverReply);
        Assert.Contains(afterReply.Events, item => item.Kind == "instruction_order_stale_decision" &&
            item.Detail == HarvestInstructionActor);
        Assert.Single(afterReply.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == order.InstructionId + ":harvest_food");
        using var validated = PrivateWorldRuntime.Restore(afterReply);
        validated.Validate();
    }

    [Fact]
    public async Task HeldOrderResponseAndUrgentFoodCannotConsumeTwiceInOneTick()
    {
        var provider = new HeldOrderReplyProvider();
        using var setup = CreateKnownBerryOrderWorld(_ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "held-urgent-fruit", "fruit", HarvestInstructionActor, 2);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == HarvestInstructionActor
                ? item with { HungerBasisPoints = 0 }
                : item).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, id => id == HarvestInstructionActor
            ? provider
            : new DeterministicDecisionProvider());
        var order = world.SubmitInstruction(new OwnerInstructionRequest("held-urgent-order", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "gather berries from berry-patch"));

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var afterOneTick = world.ExportState();
        Assert.Single(afterOneTick.Events, item => item.Kind == "food_consumed" &&
            item.Detail == HarvestInstructionActor);
        Assert.Equal(1, Assert.Single(afterOneTick.Society.Society.Inventory.Lots,
            item => item.Id == "held-urgent-fruit").Quantity);
        Assert.Equal("interrupted", Assert.Single(afterOneTick.Instructions!,
            item => item.InstructionId == order.InstructionId).Order!.Status);
        Assert.Equal(1, provider.CallCount);
        Assert.DoesNotContain(afterOneTick.Events, item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal));
        world.Validate();
    }

    [Fact]
    public async Task HeldReplyDoesNotEatNewHarvestUntilTheNextTickAfterTheOrderFinishes()
    {
        var provider = new HeldOrderReplyProvider();
        using var setup = CreateKnownBerryOrderWorld(_ => new DeterministicDecisionProvider());
        var state = setup.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == HarvestInstructionActor
                ? item with { HungerBasisPoints = 0 }
                : item).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = WithoutFoodLots(state.Society.Society.Inventory),
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, id => id == HarvestInstructionActor
            ? provider
            : new DeterministicDecisionProvider());
        var order = world.SubmitInstruction(new OwnerInstructionRequest("held-urgent-harvest", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "gather berries from berry-patch"));

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var afterHarvestTick = world.ExportState();
        Assert.Equal("finished", Assert.Single(afterHarvestTick.Instructions!,
            item => item.InstructionId == order.InstructionId).Order!.Status);
        Assert.Single(afterHarvestTick.Events, item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal));
        Assert.DoesNotContain(afterHarvestTick.Events, item => item.Kind == "food_consumed" &&
            item.Detail == HarvestInstructionActor);
        Assert.Null(Assert.Single(afterHarvestTick.Instructions!,
            item => item.InstructionId == order.InstructionId).ObservedTick);
        Assert.Equal(1, provider.CallCount);

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        var afterFollowingTick = world.ExportState();
        Assert.Single(afterFollowingTick.Events, item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal));
        Assert.Single(afterFollowingTick.Events, item => item.Kind == "food_consumed" &&
            item.Detail == HarvestInstructionActor);

        provider.Release.TrySetResult(true);
        await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var tick = 0; tick < 5 && !world.ExportState().Events.Any(item =>
                 item.Kind == "hosted_decision_completed" && item.Detail == HarvestInstructionActor); tick++)
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);

        var final = world.ExportState();
        var savedOrder = Assert.Single(final.Instructions!, item => item.InstructionId == order.InstructionId);
        Assert.Equal("finished", savedOrder.Order!.Status);
        Assert.NotNull(savedOrder.ObservedTick);
        Assert.Equal("I will gather the berries.", savedOrder.ObserverReply);
        Assert.Single(final.Events, item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal));
        Assert.Contains(final.Events, item => item.Kind == "food_consumed" &&
            item.Detail == HarvestInstructionActor);
        Assert.Contains(final.Events, item => item.Kind == "instruction_order_stale_decision" &&
            item.Detail == HarvestInstructionActor);
        Assert.Equal(1, provider.CallCount);
        using var validated = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(final)));
        validated.Validate();
    }

    [Fact]
    public async Task FailedOrderDecisionPersistsWaitAndRecoversAtTheNormalRetryInterval()
    {
        var provider = new FailOnceHostedOrderProvider();
        using var world = CreateKnownBerryOrderWorld(id => id == HarvestInstructionActor
            ? provider
            : new DeterministicDecisionProvider());
        var order = world.SubmitInstruction(new OwnerInstructionRequest("retry-order", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "gather berries from berry-patch"));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var failed = world.ExportState();
        var failedOrder = Assert.Single(failed.Instructions!, item => item.InstructionId == order.InstructionId).Order!;
        Assert.Equal("blocked", failedOrder.Status);
        Assert.True(failedOrder.WaitForDecisionAfterFailure);
        Assert.Equal(1, provider.CallCount);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(failed)), id => id == HarvestInstructionActor
                ? provider
                : new DeterministicDecisionProvider());

        for (var tick = 0; tick < 15; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, provider.CallCount);
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind == "food_harvested");

        for (var tick = 0; tick < 20 && provider.CallCount < 2; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

        var recovered = restored.ExportState();
        Assert.Equal(2, provider.CallCount);
        Assert.Contains(order.InstructionId, recovered.CompletedInstructionIds ?? []);
        Assert.Single(recovered.Events, item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal));
        Assert.False(Assert.Single(recovered.Instructions!, item => item.InstructionId == order.InstructionId)
            .Order!.WaitForDecisionAfterFailure);
        restored.Validate();
    }

    private static PrivateWorldRuntime CreateKnownBerryOrderWorld(Func<string, IDecisionProvider> providerFactory)
    {
        using var setup = CreateHarvestInstructionWorld(orchard: false);
        var state = setup.ExportState();
        var source = state.Map.Resources.Single(item => item.Id == "berry-patch");
        var occupied = state.Inhabitants.Where(item => item.InhabitantId != HarvestInstructionActor)
            .Select(item => item.Position).ToHashSet();
        var stand = state.Map.FootNeighbors(source.Position)
            .First(point => state.Map.IsPassable(point) && !occupied.Contains(point));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == HarvestInstructionActor
                ? item with { Position = stand }
                : item).ToArray(),
        };
        return PrivateWorldRuntime.Restore(state, providerFactory);
    }

    private static InventoryCheckpoint FillActorCarry(
        InventoryCheckpoint inventory,
        PlaytestInhabitantState person,
        string lotId)
    {
        var free = PersonalEquipmentRules.FreeCapacity(inventory, person.InhabitantId, person.Equipment);
        if (free > 0)
            inventory = InventoryFixture.AddLot(inventory, lotId, "stone", person.InhabitantId, free);
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(inventory, person.InhabitantId, person.Equipment));
        return inventory;
    }

    private static InventoryCheckpoint WithoutFoodLots(InventoryCheckpoint inventory)
    {
        var foodKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "food", "fruit", "berries", "wild_greens", "cultivated_greens", "bread", "porridge", "stew",
        };
        var lots = inventory.Lots.Where(item => !foodKinds.Contains(item.ItemKind)).ToArray();
        var lotIds = lots.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return inventory with
        {
            Lots = lots,
            Reservations = inventory.Reservations.Where(item => lotIds.Contains(item.LotId)).ToArray(),
            Offers = inventory.Offers.Where(item => lotIds.Contains(item.FirstLotId) &&
                lotIds.Contains(item.SecondLotId)).ToArray(),
        };
    }

    private static CognitionDecisionResponse HostedResponse(
        CognitionDecisionRequest request,
        DecisionProviderKind kind,
        long providerEpoch,
        string preferredCandidate,
        IReadOnlyList<CognitionObserverReply>? replies = null)
    {
        var candidates = request.Observation.Candidates;
        var selected = candidates.FirstOrDefault(item => item.Id == preferredCandidate) ??
            candidates.First(item => item.Id == "safe_idle");
        return new CognitionDecisionResponse(
            request.RequestId,
            request.Observation.InhabitantId,
            kind,
            providerEpoch,
            request.Observation.RunEpoch,
            request.Observation.DecisionGeneration,
            request.Observation.ObservationDigest,
            selected.Id,
            1d,
            candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
            ObserverReplies: replies);
    }

    private sealed class HeldOrderReplyProvider : IDecisionProvider
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
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            Interlocked.Increment(ref callCount);
            Requests.Enqueue(request.Observation);
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            var message = request.Observation.ObserverGuidance!.Single();
            var response = HostedResponse(request, Kind, ProviderEpoch, "seek_food",
                [new CognitionObserverReply(message.InstructionId, "I will gather the berries.")]);
            Returned.TrySetResult(true);
            return response;
        }
    }

    private sealed class CountingHostedOrderProvider : IDecisionProvider
    {
        private int callCount;

        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public int CallCount => Volatile.Read(ref callCount);

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref callCount);
            return ValueTask.FromResult(HostedResponse(request, Kind, ProviderEpoch, "harvest_food"));
        }
    }

    private sealed class FailOnceHostedOrderProvider : IDecisionProvider
    {
        private int callCount;

        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public int CallCount => Volatile.Read(ref callCount);

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            var call = Interlocked.Increment(ref callCount);
            if (call == 1)
                throw new InvalidOperationException("temporary provider outage");
            return ValueTask.FromResult(HostedResponse(request, Kind, ProviderEpoch, "harvest_food"));
        }
    }
}
