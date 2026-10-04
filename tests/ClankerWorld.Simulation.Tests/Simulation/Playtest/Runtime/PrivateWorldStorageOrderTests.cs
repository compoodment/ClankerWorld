using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldStorageOrderTests
{
    private const string Household = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private static readonly Lazy<byte[]> Generated = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("personal-storage-orders", _ => new StorageChoices());
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Theory]
    [InlineData("wood")]
    [InlineData("stone")]
    [InlineData("fiber")]
    [InlineData("clay")]
    [InlineData("iron_ore")]
    [InlineData("gold_ore")]
    [InlineData("diamond")]
    public async Task StorageOrderMovesRealPersonalGoodsWithoutDonatingThem(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-goods", kind, actor, 2));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "store", $"store {kind.Replace('_', ' ')} in my House");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = Order(world, receipt);
        Assert.Equal(("store_material", "finished", 1, "storage_loads", kind),
            (order.Action, order.Status, order.CompletedUnits, order.ProgressUnit, order.TargetMaterialKind));
        var lot = world.Society.Inventory.GetLot("storage-goods");
        Assert.Equal((actor, House, 2, (string?)null), (lot.OwnerId, lot.StorageBuildingId, lot.Quantity, lot.CarrierId));
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(kind, projected.TargetMaterialKind);
        Assert.Equal("storage_loads", projected.ProgressUnit);
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_stored");
        world.Validate();
    }

    [Fact]
    public async Task StorageOrderCountsExactQuantitiesAcrossLotsQueueAndReplay()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-a", "wood", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "storage-b", "wood", actor, 2);
        using var world = Restore(WithInventory(state, inventory));
        var first = Submit(world, actor, "three", "store three wood");
        var second = Submit(world, actor, "remaining", "store wood", queue: true);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 2), (Order(world, first).Status, Order(world, first).CompletedUnits));
        Assert.Equal("queued", Order(world, second).Status);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("finished", 3), (Order(world, first).Status, Order(world, first).CompletedUnits));
        Assert.Equal(1, world.Society.Inventory.GetLot("storage-b").Quantity);
        Assert.Null(world.Society.Inventory.GetLot("storage-b").StorageBuildingId);
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        Assert.Equal("finished", Order(restored, second).Status);
        Assert.Equal(4, restored.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "wood" &&
            lot.StorageBuildingId == House).Sum(lot => lot.Quantity));
        Assert.Equal(3, restored.ExportState().Events.Count(item => item.Kind == "personal_goods_stored"));
    }

    [Fact]
    public async Task StorageOrderWalksHomeWithoutCreditingTravelAndResumesAfterReload()
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-distant", "clay", actor, 2));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "walk", "store clay at home");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(world.Society.Inventory.GetLot("storage-distant").StorageBuildingId);
        Assert.NotEqual(state.Inhabitants.Single(person => person.InhabitantId == actor).Position,
            world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 20 && Order(world, receipt).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        Assert.Equal("finished", Order(restored, receipt).Status);
        Assert.Equal(House, restored.Society.Inventory.GetLot("storage-distant").StorageBuildingId);
    }

    [Theory]
    [InlineData("store wood and stone")]
    [InlineData("do not store wood")]
    [InlineData("store -2 wood")]
    [InlineData("store 1.5 wood")]
    [InlineData("store wood in the Warehouse")]
    [InlineData("store wood at (1, 2)")]
    [InlineData("store wood in another House")]
    [InlineData("store food")]
    [InlineData("store iron")]
    [InlineData("store cloth")]
    [InlineData("keep store wood")]
    public void StorageOrderRejectsUnsupportedTextWithoutReplacingTheCurrentOrder(string text)
    {
        var state = Prepared();
        using var world = Restore(state);
        var current = Submit(world, Actor(state), "current", "keep storing wood until cancelled");
        var rejected = Submit(world, Actor(state), "unsupported", text);
        Assert.Equal("not_understood", Order(world, rejected).Status);
        Assert.Equal("waiting", Order(world, current).Status);
        world.Validate();
    }

    [Theory]
    [InlineData("borrowed")]
    [InlineData("reserved")]
    [InlineData("promised")]
    [InlineData("ground")]
    [InlineData("stored")]
    [InlineData("wrong-kind")]
    public async Task StorageOrderCannotUseUnavailableGoods(string boundary)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-boundary",
            boundary == "wrong-kind" ? "stone" : "wood", boundary == "borrowed" ? Household : actor, 2,
            storageBuildingId: boundary == "stored" ? House : null,
            groundPosition: boundary == "ground" ? new(state.Inhabitants.Single(person => person.InhabitantId == actor).Position.X,
                state.Inhabitants.Single(person => person.InhabitantId == actor).Position.Y) : null);
        if (boundary is "borrowed" or "promised") inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "storage-boundary" ? lot with
            {
                CarrierId = boundary == "borrowed" ? actor : lot.CarrierId,
                DeliveryBuildingId = boundary == "promised" ? House : null,
            } : lot).ToArray(),
        };
        if (boundary == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "storage-reserved", actor, "storage-boundary", 2, "other_work", 120);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "blocked", "store wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Contains("No matching personal material", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        // The ordinary inventory clock still advances while the order waits.
        Assert.Equal(inventory.GetLot("storage-boundary") with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot("storage-boundary"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "personal_goods_stored");
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal("blocked", Order(restored, receipt).Status);
    }

    [Fact]
    public async Task StorageOrderMovesOnlyUnreservedUnitsAndLeavesReservationsIntact()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-partial", "wood", actor, 4);
        inventory = InventoryFixture.Reserve(inventory, "storage-partial-reservation", actor, "storage-partial", 2, "other_work", 120);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "partial", "store wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        Assert.Equal((2, (string?)null), (world.Society.Inventory.GetLot("storage-partial").Quantity,
            world.Society.Inventory.GetLot("storage-partial").StorageBuildingId));
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("storage-partial-reservation").State);
        Assert.Equal(2, Assert.Single(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "storage-partial").Quantity);
        world.Validate();
    }

    [Fact]
    public async Task StorageOrderWaitsForRoomThenResumesWithoutDuplicatingStoredGoods()
    {
        var state = Prepared();
        var actor = Actor(state);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var inventory = state.Society.Society.Inventory;
        var room = BuildingStorageRules.Capacity(definition, house)!.Value - inventory.Lots.Where(lot => lot.StorageBuildingId == House).Sum(lot => lot.Quantity);
        inventory = InventoryFixture.AddLot(inventory, "storage-filler", "stone", Household, room - 1, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "storage-capacity", "wood", actor, 3);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "three", "store three wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("storage is full", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        var paused = world.ExportState();
        inventory = paused.Society.Society.Inventory with { Lots = paused.Society.Society.Inventory.Lots.Where(lot => lot.Id != "storage-filler").ToArray() };
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(WithInventory(paused, inventory))));
        for (var tick = 0; tick < 8 && Order(restored, receipt).Status != "finished"; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("finished", 3), (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits));
        Assert.Equal(3, restored.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "wood" && lot.StorageBuildingId == House).Sum(lot => lot.Quantity));
        Assert.Equal(2, restored.ExportState().Events.Count(item => item.Kind == "personal_goods_stored"));
    }

    [Fact]
    public async Task StorageOrderRepeatsUntilCancellationAndKeepsTheRemainingLoad()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-repeat-a", "fiber", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "storage-repeat-b", "fiber", actor, 1);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "repeat", "keep storing fiber");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.True(world.CancelOrder(new("storage-cancel", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 3; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("cancelled", Order(restored, receipt).Status);
        Assert.Null(restored.Society.Inventory.GetLot("storage-repeat-b").StorageBuildingId);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "personal_goods_stored");
    }

    [Fact]
    public async Task StorageOrderCannotTreatSurvivalFoodAsAStoredLoad()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-urgent-food", "berries", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "storage-after-food", "clay", actor, 2);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, actor, "survival", "store clay");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("interrupted", Order(world, receipt).Status);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(world.Society.Inventory.GetLot("storage-after-food").StorageBuildingId);
        for (var tick = 0; tick < 6 && Order(world, receipt).Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        world.Validate();
    }

    [Fact]
    public void StorageOrderRejectsCorruptedTargetsAndProgress()
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "validation", "store two wood");
        var saved = world.ExportState();
        var valid = Order(world, receipt);
        foreach (var invalid in new[]
        {
            valid with { TargetMaterialKind = "cloth" }, valid with { TargetFoodKind = "berries" },
            valid with { TargetAgentId = Actor(state) },
            valid with { TargetPosition = new(1, 1) }, valid with { TargetResourceId = "tree" },
            valid with { RequestedUnits = 0 }, valid with { CompletedUnits = -1 },
            valid with { ProgressUnit = "harvests" }, valid with { LastEffectId = "store:personal:unearned" },
            valid with { CompletedUnits = 1, LastEffectId = "gather:material:wrong-action" },
        })
        {
            var corrupt = saved with
            {
                Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId ? item with { Order = invalid } : item).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => Restore(corrupt));
        }
    }

    [Fact]
    public async Task StorageOrderKeepsItsReceiptBoundedForLongInventoryLotIds()
    {
        var state = Prepared();
        var actor = Actor(state);
        // Inventory split identities can grow through successive physical moves.
        var lotId = "storage-split-" + new string('x', 600);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, lotId, "wood", actor, 2));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "long-lot", "store wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        Assert.InRange(Order(world, receipt).LastEffectId!.Length, 1, 512);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal((actor, House, 2), (restored.Society.Inventory.GetLot(lotId).OwnerId,
            restored.Society.Inventory.GetLot(lotId).StorageBuildingId, restored.Society.Inventory.GetLot(lotId).Quantity));
    }

    [Fact]
    public async Task StorageOrderRejectsFinishedProgressBeyondTheExactRequestedQuantity()
    {
        var state = Prepared();
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-exact", "wood", actor, 2));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "exact", "store two wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        var saved = world.ExportState();
        var corrupt = saved with
        {
            Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                ? item with { Order = item.Order! with { CompletedUnits = 3 } } : item).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => Restore(corrupt));
    }

    [Fact]
    public async Task StorageOrderNeedsTheActorsOwnHouseAfterDeparture()
    {
        var state = Prepared();
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-no-home", "wood", actor, 2));
        using var world = Restore(state);
        Assert.True(world.DisplaceAdult(actor));
        var receipt = Submit(world, actor, "no-home", "store wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("needs a House", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Null(world.Society.Inventory.GetLot("storage-no-home").StorageBuildingId);
        world.Validate();
    }

    [Fact]
    public async Task StorageOrderDoesNotGiveChildrenAdultStorageWork()
    {
        var state = Prepared();
        var actor = Actor(state);
        var checkpoint = state.Society.Society;
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick) - 4 * checkpoint.Config.TicksPerLifecycleAge;
        state = state with
        {
            Society = state.Society with
            {
                Society = checkpoint with
                {
                    Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                        AgeBand = SocietyAgeBand.Child,
                        LastLifecycleYearChecked = 4,
                        CurrentRole = SocietyWorkRole.Unassigned,
                    } : person).ToArray(),
                }
            },
            Towns = state.Towns!.Select(town => town with
            {
                Governance = TownGovernanceState.Create(town.ResidentIds.Where(id => id != actor &&
                    checkpoint.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)),
            }).ToArray(),
        };
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-child", "wood", actor, 1));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "child", "store wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("too young", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Null(world.Society.Inventory.GetLot("storage-child").StorageBuildingId);
        world.Validate();
    }

    [Fact]
    public async Task StorageOrderUsesOnePersonalDecisionAcrossSeveralLoads()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = state.Society.Society.Inventory;
        for (var index = 0; index < 3; index++) inventory = InventoryFixture.AddLot(inventory, $"storage-model-{index}", "clay", actor, 1);
        var provider = new StorageChoices(DecisionProviderKind.LargeLanguageModel);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), id => id == actor ? provider : new StorageChoices());
        var receipt = Submit(world, actor, "model", "keep storing clay");
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(3, Order(world, receipt).CompletedUnits);
        var request = Assert.Single(provider.Requests, item => item.OperativeOrderInstructionId == receipt.InstructionId);
        Assert.Contains(request.ObserverGuidance!, message => message.UnderstoodTask == "store your own carried material in your House");
    }

    [Fact]
    public async Task StorageOrderCancelledWhileAModelReplyIsHeldCannotStoreAnotherLoad()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "storage-held-a", "clay", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "storage-held-b", "clay", actor, 1);
        var provider = new StorageChoices(DecisionProviderKind.LargeLanguageModel, hold: true);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), id => id == actor ? provider : new StorageChoices());
        var receipt = Submit(world, actor, "held", "keep storing clay");
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1, Order(world, receipt).CompletedUnits);
            Assert.True(world.CancelOrder(new("cancel-held", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var tick = 0; tick < 5; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal("cancelled", Order(world, receipt).Status);
            Assert.Null(world.Society.Inventory.GetLot("storage-held-b").StorageBuildingId);
            Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_stored");
        }
        finally { provider.Release.TrySetResult(true); }
    }

    private static PrivateWorldRuntimeState Prepared(bool distant = false)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value);
        var actor = Actor(state);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position).ToHashSet();
        bool Clear(GridPoint point) => state.Map.IsPassable(point) && !occupied.Contains(point) &&
            state.Map.Resources.All(resource => resource.Position != point) && state.Map.CampObjects.All(item => item.Position != point);
        var position = distant ? (from near in state.Map.FootNeighbors(house.Position)
                                  where Clear(near)
                                  from far in state.Map.FootNeighbors(near)
                                  where Clear(far) && state.Map.FootDistance(far, house.Position) >= 2
                                  select far).First() : house.Position;
        return state with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? position : person.Position,
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
            }).ToArray(),
        };
    }

    private static string Actor(PrivateWorldRuntimeState state) =>
        state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household).Id;
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new StorageChoices());
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text, Queue: queue));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;

    private sealed class StorageChoices(DecisionProviderKind kind = DecisionProviderKind.Deterministic, bool hold = false) : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long ProviderEpoch => 0;
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            Requests.Enqueue(request.Observation);
            if (hold && Requests.Count == 1 && request.Observation.OperativeOrderInstructionId is not null)
            {
                Started.TrySetResult(true);
                await Release.Task;
                Returned.TrySetResult(true);
            }
            var selected = request.Observation.Candidates.Any(candidate => candidate.Id == "store_material") ? "store_material" : "safe_idle";
            return new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 });
        }
    }
}
