using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Fact]
    public void NewOrdersReplaceUnlessQueuedAndCancellationSurvivesReloadIdempotently()
    {
        using var world = new PrivateWorldRuntime("order-replace-queue-cancel");
        var first = world.SubmitInstruction(new OwnerInstructionRequest(
            "order-one", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "gather berries"));
        var replacement = world.SubmitInstruction(new OwnerInstructionRequest(
            "order-two", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "eat berries"));
        var queued = world.SubmitInstruction(new OwnerInstructionRequest(
            "order-three", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo,
            "go to the berry patch", Queue: true));

        var submitted = world.ExportState();
        Assert.Equal("cancelled", Assert.Single(submitted.Instructions!, item => item.InstructionId == first.InstructionId).Order!.Status);
        Assert.Equal("waiting", Assert.Single(submitted.Instructions!, item => item.InstructionId == replacement.InstructionId).Order!.Status);
        Assert.Equal("queued", Assert.Single(submitted.Instructions!, item => item.InstructionId == queued.InstructionId).Order!.Status);

        var cancellation = new OwnerOrderCancelRequest(
            "cancel-current", "owner:test", submitted.Society.Society.WorldId, OrderedAgent, replacement.InstructionId);
        var receipt = world.CancelOrder(cancellation);
        Assert.True(receipt.Changed);
        Assert.Equal("cancelled", receipt.Status);
        Assert.Equal("queued", Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == queued.InstructionId).Order!.Status);

        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
        Assert.Equal(receipt, restored.CancelOrder(cancellation));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        var conflictingReuse = cancellation with { OrderId = queued.InstructionId };
        Assert.Throws<InvalidOperationException>(() => restored.CancelOrder(conflictingReuse));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var foreignWorld = cancellation with { IdempotencyKey = "wrong-world", WorldId = "another-world" };
        Assert.Throws<InvalidOperationException>(() => restored.CancelOrder(foreignWorld));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();

        var archivedState = restored.ExportState() with
        {
            Events = [],
            EventHistoryFloor = receipt.LatestEventId,
            HistoryArchiveHead = new string('a', 64),
        };
        using var archived = PrivateWorldRuntime.Restore(archivedState);
        archived.Validate();
    }

    [Fact]
    public void QuantityParsingDoesNotReadCoordinatesAndUnsupportedCountsStayUnrecognized()
    {
        using var world = new PrivateWorldRuntime("bounded-order-quantity");
        var quantity = world.SubmitInstruction(new OwnerInstructionRequest(
            "two-berries", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "gather two berries"));
        var location = world.SubmitInstruction(new OwnerInstructionRequest(
            "food-location", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "go to berries at 12,4"));
        var defaultHarvestAtLocation = world.SubmitInstruction(new OwnerInstructionRequest(
            "one-harvest-at-location", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo,
            "gather berries at 10,12"));
        var countedHarvestAtLocation = world.SubmitInstruction(new OwnerInstructionRequest(
            "three-harvest-items-at-location", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo,
            "gather three berries at10,12"));
        var namedTarget = world.SubmitInstruction(new OwnerInstructionRequest(
            "known-resource-target", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo,
            "gather berries from berry-patch"));
        var unknownTarget = world.SubmitInstruction(new OwnerInstructionRequest(
            "unknown-resource-target", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo,
            "gather berries from berry-patch-unknown"));
        var repeated = world.SubmitInstruction(new OwnerInstructionRequest(
            "repeat-food", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo,
            "keep gathering food until cancelled"));
        var oversized = world.SubmitInstruction(new OwnerInstructionRequest(
            "too-many-berries", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "gather 1001 berries"));
        var unbounded = world.SubmitInstruction(new OwnerInstructionRequest(
            "unbounded-berries", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo,
            "gather 1000000000 berries"));

        var state = world.ExportState();
        var quantityOrder = Assert.Single(state.Instructions!, item => item.InstructionId == quantity.InstructionId).Order!;
        Assert.Equal(2, quantityOrder.RequestedUnits);
        Assert.Equal("food_items", quantityOrder.ProgressUnit);
        Assert.True(quantityOrder.QuantityIsExplicit);

        var locationOrder = Assert.Single(state.Instructions!, item => item.InstructionId == location.InstructionId).Order!;
        Assert.Equal(1, locationOrder.RequestedUnits);
        Assert.False(locationOrder.QuantityIsExplicit);
        Assert.Equal(new GridPoint(12, 4), locationOrder.TargetPosition);

        var defaultHarvestOrder = Assert.Single(state.Instructions!, item => item.InstructionId == defaultHarvestAtLocation.InstructionId).Order!;
        Assert.Equal(1, defaultHarvestOrder.RequestedUnits);
        Assert.False(defaultHarvestOrder.QuantityIsExplicit);
        Assert.Equal(new GridPoint(10, 12), defaultHarvestOrder.TargetPosition);

        var countedHarvestOrder = Assert.Single(state.Instructions!, item => item.InstructionId == countedHarvestAtLocation.InstructionId).Order!;
        Assert.Equal(3, countedHarvestOrder.RequestedUnits);
        Assert.True(countedHarvestOrder.QuantityIsExplicit);
        Assert.Equal(new GridPoint(10, 12), countedHarvestOrder.TargetPosition);

        var namedTargetOrder = Assert.Single(state.Instructions!, item => item.InstructionId == namedTarget.InstructionId).Order!;
        Assert.Equal("berry-patch", namedTargetOrder.TargetResourceId);
        Assert.Equal("not_understood", Assert.Single(state.Instructions!, item => item.InstructionId == unknownTarget.InstructionId).Order!.Status);

        var repeatedOrder = Assert.Single(state.Instructions!, item => item.InstructionId == repeated.InstructionId).Order!;
        Assert.True(repeatedOrder.RepeatUntilCancelled);
        Assert.Equal(1, repeatedOrder.RequestedUnits);
        Assert.Equal("not_understood", Assert.Single(state.Instructions!, item => item.InstructionId == oversized.InstructionId).Order!.Status);
        Assert.Contains(oversized.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.Equal("not_understood", Assert.Single(state.Instructions!, item => item.InstructionId == unbounded.InstructionId).Order!.Status);
        Assert.Contains(unbounded.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.Equal("not_understood", Assert.Single(state.Instructions!, item => item.InstructionId == unknownTarget.InstructionId).Order!.Status);
        Assert.Contains(unknownTarget.InstructionId, state.CompletedInstructionIds ?? []);
        world.Validate();
    }

    [Fact]
    public async Task ExactFoodOrderStaysBlockedOnWrongKindAndResumesAfterReloadWhenAvailable()
    {
        using var genesis = new PrivateWorldRuntime("exact-food-order");
        var state = genesis.ExportState();
        const string wrongFoodId = "order-wrong-kind-food";
        const string requestedFoodId = "order-requested-berries";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            wrongFoodId, "wild_greens", OrderedAgent, 1);
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
        var provider = new OrderCandidateRecordingProvider("consume_food");
        using var world = PrivateWorldRuntime.Restore(state, id => id == OrderedAgent
            ? provider
            : new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var order = world.SubmitInstruction(new OwnerInstructionRequest(
            "eat-berries-only", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "eat berries"));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var blocked = world.ExportState();
        var savedOrder = Assert.Single(blocked.Instructions!, item => item.InstructionId == order.InstructionId).Order!;
        Assert.Equal("blocked", savedOrder.Status);
        Assert.False(blocked.CompletedInstructionIds?.Contains(order.InstructionId) ?? false);
        Assert.Contains(blocked.Society.Society.Inventory.Lots, item => item.Id == wrongFoodId && item.Quantity == 1);
        Assert.Equal(["safe_idle"], Assert.Single(provider.Requests).Candidates.Select(item => item.Id));
        world.Validate();

        var reloaded = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(blocked));
        var withRequestedFood = InventoryFixture.AddLot(reloaded.Society.Society.Inventory,
            requestedFoodId, "berries", OrderedAgent, 1);
        reloaded = reloaded with
        {
            Society = reloaded.Society with
            {
                Society = reloaded.Society.Society with { Inventory = withRequestedFood },
            },
        };
        using var resumed = PrivateWorldRuntime.Restore(reloaded, id => id == OrderedAgent
            ? new OrderCandidateRecordingProvider("consume_food")
            : new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);

        var finished = resumed.ExportState();
        Assert.Contains(order.InstructionId, finished.CompletedInstructionIds ?? []);
        Assert.Equal("finished", Assert.Single(finished.Instructions!, item => item.InstructionId == order.InstructionId).Order!.Status);
        Assert.Contains(finished.Society.Society.Inventory.Lots, item => item.Id == wrongFoodId && item.Quantity == 1);
        Assert.DoesNotContain(finished.Society.Society.Inventory.Lots, item => item.Id == requestedFoodId);
        Assert.Contains(finished.Events, item => item.Kind == "food_consumed" && item.Detail == OrderedAgent);
        resumed.Validate();
    }

    [Fact]
    public async Task UrgentFoodMayInterruptExactKindOrderAndTheOutstandingTaskResumes()
    {
        using var genesis = new PrivateWorldRuntime("urgent-exact-food-order");
        var state = genesis.ExportState();
        const string survivalFoodId = "order-urgent-wild-greens";
        const string requestedFoodId = "order-after-interruption-berries";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            survivalFoodId, "wild_greens", OrderedAgent, 1);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == OrderedAgent
                ? item with { HungerBasisPoints = 0 }
                : item).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, id => id == OrderedAgent
            ? new OrderCandidateRecordingProvider("consume_food")
            : new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var order = world.SubmitInstruction(new OwnerInstructionRequest(
            "urgent-eat-berries", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "eat berries"));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var interrupted = world.ExportState();
        Assert.DoesNotContain(order.InstructionId, interrupted.CompletedInstructionIds ?? []);
        Assert.Contains(interrupted.Events, item => item.Kind == "instruction_order_status" &&
            item.Detail == $"{OrderedAgent}:{order.InstructionId}:interrupted");
        Assert.Contains(interrupted.Events, item => item.Kind == "food_consumed" && item.Detail == OrderedAgent);
        Assert.DoesNotContain(interrupted.Society.Society.Inventory.Lots, item => item.Id == survivalFoodId);
        Assert.DoesNotContain(interrupted.Events, item => item.Kind == "instruction_applied" &&
            item.Detail.StartsWith(order.InstructionId + ":", StringComparison.Ordinal));
        world.Validate();

        var reloaded = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(interrupted));
        var withRequestedFood = InventoryFixture.AddLot(reloaded.Society.Society.Inventory,
            requestedFoodId, "berries", OrderedAgent, 1);
        reloaded = reloaded with
        {
            Society = reloaded.Society with
            {
                Society = reloaded.Society.Society with { Inventory = withRequestedFood },
            },
        };
        using var resumed = PrivateWorldRuntime.Restore(reloaded);
        Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        var finished = resumed.ExportState();
        Assert.Contains(order.InstructionId, finished.CompletedInstructionIds ?? []);
        Assert.DoesNotContain(finished.Society.Society.Inventory.Lots, item => item.Id == requestedFoodId);
        Assert.Contains(finished.Events, item => item.Kind == "food_consumed" && item.Detail == OrderedAgent);
        resumed.Validate();
    }

    [Fact]
    public async Task GatheringWildGreensProducesWildGreensAndCompletesByTheHarvestEffect()
    {
        using var genesis = new PrivateWorldRuntime("order-wild-greens-harvest");
        var state = genesis.ExportState();
        var source = Assert.Single(state.Map.Resources, item => item.NaturalObjectKind == "wild_greens");
        var occupied = state.Inhabitants.Select(item => item.Position).ToHashSet();
        var stand = state.Map.FootNeighbors(source.Position)
            .First(position => state.Map.IsPassable(position) && !occupied.Contains(position) &&
                state.Map.Resources.All(resource => resource.Position != position));
        var terrain = state.Map.Tiles.Single(tile => tile.Position == source.Position).Terrain.ToString();
        var fact = new AgentKnowledgeFact(
            "order-wild-greens-known-site", OrderedAgent, OrderedAgent, source.Position,
            terrain, ["wild_greens"], state.Society.Society.WorldTick, "firsthand");
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == OrderedAgent
                ? item with { Position = stand, HungerBasisPoints = 5_000 }
                : item).ToArray(),
            Knowledge = state.Knowledge! with { Facts = state.Knowledge.Facts.Append(fact).ToArray() },
        };
        using var world = PrivateWorldRuntime.Restore(state, _ =>
            new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var order = world.SubmitInstruction(new OwnerInstructionRequest(
            "gather-wild-greens", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "gather wild greens"));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var after = world.ExportState();
        Assert.Contains(order.InstructionId, after.CompletedInstructionIds ?? []);
        Assert.Equal("finished", Assert.Single(after.Instructions!, item => item.InstructionId == order.InstructionId).Order!.Status);
        Assert.Equal(1, Assert.Single(after.Instructions!, item => item.InstructionId == order.InstructionId).Order!.CompletedUnits);
        Assert.Contains(after.Society.Society.Inventory.Lots, item =>
            item.OwnerId == OrderedAgent && item.ItemKind == "wild_greens" && item.Quantity > 0);
        Assert.Contains(after.Events, item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith(OrderedAgent + ":", StringComparison.Ordinal));
        world.Validate();
    }

    private sealed class OrderCandidateRecordingProvider(string preferredCandidate) : IDecisionProvider
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
            var selected = request.Observation.Candidates.SingleOrDefault(item => item.Id == preferredCandidate)
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1d,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}
