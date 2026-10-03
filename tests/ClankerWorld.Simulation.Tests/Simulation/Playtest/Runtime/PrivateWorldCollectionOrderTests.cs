using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldCollectionOrderTests
{
    private const string Household = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private static readonly Lazy<byte[]> Generated = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("personal-collection-orders", _ => new CollectionChoices());
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
    public async Task CollectionOrderMovesRealPersonalGoodsIntoCarriedInventory(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "collect-goods", kind, actor, 2, storageBuildingId: House));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "collect", $"collect my {kind.Replace('_', ' ')}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = Order(world, receipt);
        Assert.Equal(("collect_material", "finished", 1, "collection_loads", kind),
            (order.Action, order.Status, order.CompletedUnits, order.ProgressUnit, order.TargetMaterialKind));
        var lot = world.Society.Inventory.GetLot("collect-goods");
        Assert.Equal((actor, 2), (lot.OwnerId, lot.Quantity));
        Assert.True(PersonalEquipmentRules.IsCarried(lot, actor));
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(kind, projected.TargetMaterialKind);
        Assert.Equal("collection_loads", projected.ProgressUnit);
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
        world.Validate();
    }

    [Fact]
    public async Task CollectionOrderCountsExactQuantitiesAcrossLotsQueueAndReplay()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-a", "wood", actor, 2, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "collect-b", "wood", actor, 2, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var first = Submit(world, actor, "three", "collect three wood");
        var second = Submit(world, actor, "remaining", "collect wood", queue: true);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 2), (Order(world, first).Status, Order(world, first).CompletedUnits));
        Assert.Equal("queued", Order(world, second).Status);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            if (tick == 0)
            {
                Assert.Equal(("finished", 3), (Order(world, first).Status, Order(world, first).CompletedUnits));
                Assert.Equal(1, world.Society.Inventory.GetLot("collect-b").Quantity);
                Assert.Equal(House, world.Society.Inventory.GetLot("collect-b").StorageBuildingId);
            }
        }
        Assert.Equal("finished", Order(restored, second).Status);
        Assert.Equal(4, restored.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "wood" &&
            PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        Assert.Equal(3, restored.ExportState().Events.Count(item => item.Kind == "personal_goods_collected"));
    }

    [Fact]
    public async Task CollectionOrderWalksToGoodsWithoutCreditingTravelAndResumesAfterReload()
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-distant", "clay", actor, 2, storageBuildingId: House));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "walk", "collect clay");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(House, world.Society.Inventory.GetLot("collect-distant").StorageBuildingId);
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
        Assert.True(PersonalEquipmentRules.IsCarried(restored.Society.Inventory.GetLot("collect-distant"), actor));
    }

    [Theory]
    [InlineData("collect wood and stone")]
    [InlineData("do not collect wood")]
    [InlineData("collect -2 wood")]
    [InlineData("collect 1.5 wood")]
    [InlineData("collect wood from the Warehouse")]
    [InlineData("collect wood at (1,)")]
    [InlineData("collect ornaments")]
    [InlineData("collect cloth and rope")]
    [InlineData("collect tools")]
    [InlineData("keep collect wood")]
    public void CollectionOrderRejectsUnsupportedTextWithoutReplacingTheCurrentOrder(string text)
    {
        var state = Prepared();
        using var world = Restore(state);
        var current = Submit(world, Actor(state), "current", "keep collecting wood until cancelled");
        var rejected = Submit(world, Actor(state), "unsupported", text);
        Assert.Equal("not_understood", Order(world, rejected).Status);
        Assert.Equal("waiting", Order(world, current).Status);
        world.Validate();
    }

    [Theory]
    [InlineData("household-owned", false)]
    [InlineData("household-owned", true)]
    [InlineData("another-person", false)]
    [InlineData("another-person", true)]
    [InlineData("reserved", false)]
    [InlineData("reserved", true)]
    [InlineData("promised", false)]
    [InlineData("promised", true)]
    [InlineData("carried", false)]
    [InlineData("carried", true)]
    [InlineData("other-carrier", false)]
    [InlineData("other-carrier", true)]
    [InlineData("foreign-house", false)]
    [InlineData("foreign-house", true)]
    [InlineData("wrong-kind", false)]
    [InlineData("wrong-kind", true)]
    public async Task CollectionOrderCannotUseUnavailableGoods(string boundary, bool food)
    {
        var state = Prepared();
        var actor = Actor(state);
        var other = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-boundary",
            boundary == "wrong-kind" ? "stone" : food ? "fruit" : "wood", boundary == "household-owned" ? Household :
            boundary == "another-person" ? other : actor, 2,
            storageBuildingId: boundary is "carried" or "other-carrier" or "promised" ? null :
                boundary == "foreign-house" ? "first-town-house-b" : House);
        if (boundary is "promised" or "other-carrier") inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "collect-boundary" ? lot with
            {
                CarrierId = boundary == "other-carrier" ? other : lot.CarrierId,
                DeliveryBuildingId = boundary == "promised" ? House : null,
            } : lot).ToArray(),
        };
        if (boundary == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "collect-reserved", actor, "collect-boundary", 2, "other_work", 120);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "blocked", food ? "collect food" : "collect wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Contains(food ? "No matching personal food" : "No matching personal material", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(inventory.GetLot("collect-boundary") with { LastProcessedTick = world.WorldTick }, world.Society.Inventory.GetLot("collect-boundary"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal("blocked", Order(restored, receipt).Status);
    }

    [Theory]
    [InlineData("stone")]
    [InlineData("basket")]
    public async Task CollectionOrderCanRetrievePersonalGoodsAfterDepartureWithoutTakingSharedStock(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-former", kind, actor, 2, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "collect-shared", kind, Household, 3, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        Assert.True(world.DisplaceAdult(actor));
        var departure = Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == actor).Departures!);
        var receipt = Submit(world, actor, "former", $"collect {kind}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        Assert.True(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("collect-former"), actor));
        Assert.Equal(House, world.Society.Inventory.GetLot("collect-shared").StorageBuildingId);
        Assert.Equal(Household, world.Society.Inventory.GetLot("collect-shared").OwnerId);
        Assert.Null(world.Society.GetInhabitant(actor).HouseholdId);
        Assert.Equal(departure.AllowancePortions, Assert.Single(world.Inhabitants.Single(person => person.InhabitantId == actor).Departures!).AllowancePortions);
        world.Validate();
    }

    [Theory]
    [InlineData("fiber")]
    [InlineData("fruit")]
    [InlineData("basket")]
    public async Task CollectionOrderPicksUpDroppedGoodsAndPreservesPartialReservations(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var position = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-ground", kind, actor, 3,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.Reserve(inventory, "collect-held", actor, "collect-ground", 1, "other_work", 120);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "ground", $"collect {kind}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        Assert.Equal(1, world.Society.Inventory.GetLot("collect-ground").Quantity);
        Assert.NotNull(world.Society.Inventory.GetLot("collect-ground").GroundPosition);
        Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == kind && lot.OwnerId == actor &&
            PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.Reservations.Single(item => item.Id == "collect-held").State);
        world.Validate();
    }

    [Theory]
    [InlineData("wood")]
    [InlineData("fruit")]
    [InlineData("basket")]
    public async Task CollectionOrderHonorsCarryingSpaceThenResumesItsRemainingQuantity(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-fill", "stone", actor, 7);
        inventory = InventoryFixture.AddLot(inventory, "collect-limited", kind, actor, 3, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "limited", $"collect three {kind}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, Order(world, receipt).CompletedUnits);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("Carrying space is full", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        var saved = world.ExportState();
        inventory = InventoryFixture.Relocate(saved.Society.Society.Inventory, "free-carry", "collect-fill", actor, 7, storageBuildingId: House);
        using var restored = Restore(WithInventory(saved, inventory));
        for (var tick = 0; tick < 65 && Order(restored, receipt).Status != "finished"; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("finished", 3), (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits));
        Assert.Equal(2, restored.ExportState().Events.Count(item => item.Kind == "personal_goods_collected"));
        restored.Validate();
    }

    [Fact]
    public async Task CollectionReceiptsStayBoundedForLongLotIdentities()
    {
        var state = Prepared();
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "collect-" + new string('x', 600), "wood", actor, 3, storageBuildingId: House));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "long", "collect one wood");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        Assert.InRange(Order(world, receipt).LastEffectId!.Length, 1, 512);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData("wood")]
    [InlineData("fruit")]
    [InlineData("basket")]
    public async Task CollectionOrderDoesNotGiveChildrenAdultCollectionWork(string kind)
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
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-child", kind, actor, 1, storageBuildingId: House));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "child", $"collect {kind}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("too young", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(House, world.Society.Inventory.GetLot("collect-child").StorageBuildingId);
        world.Validate();
    }

    [Theory]
    [InlineData("clay", "material")]
    [InlineData("fruit", "food")]
    [InlineData("basket", "equipment")]
    public async Task CollectionOrderUsesOnePersonalDecisionAcrossSeveralLoads(string kind, string goodsName)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = state.Society.Society.Inventory;
        for (var index = 0; index < 3; index++) inventory = InventoryFixture.AddLot(inventory, $"collect-model-{index}", kind, actor, 1, storageBuildingId: House);
        var provider = new CollectionChoices(DecisionProviderKind.LargeLanguageModel);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), id => id == actor ? provider : new CollectionChoices());
        var receipt = Submit(world, actor, "model", $"keep collecting {kind}");
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(3, Order(world, receipt).CompletedUnits);
        var request = Assert.Single(provider.Requests, item => item.OperativeOrderInstructionId == receipt.InstructionId);
        Assert.Contains(request.ObserverGuidance!, message => message.UnderstoodTask == $"collect your own stored or dropped {goodsName}");
    }

    [Theory]
    [InlineData(false, "clay")]
    [InlineData(true, "clay")]
    [InlineData(false, "fruit")]
    [InlineData(true, "fruit")]
    [InlineData(false, "basket")]
    [InlineData(true, "basket")]
    public async Task CollectionOrderCancelledWhileAModelReplyIsHeldCannotCollectAnotherLoad(bool explicitSource, string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-held-a", kind, actor, 1, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "collect-held-b", kind, actor, 1, storageBuildingId: House);
        var provider = new CollectionChoices(DecisionProviderKind.LargeLanguageModel, hold: true);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), id => id == actor ? provider : new CollectionChoices());
        var source = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
        var text = $"keep collecting {kind}" + (explicitSource ? $" from ({source.X}, {source.Y})" : "");
        var receipt = Submit(world, actor, "held", text);
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
            Assert.Equal(House, world.Society.Inventory.GetLot("collect-held-b").StorageBuildingId);
            Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Theory]
    [InlineData(false, "clay")]
    [InlineData(true, "clay")]
    [InlineData(false, "fruit")]
    [InlineData(true, "fruit")]
    [InlineData(false, "basket")]
    [InlineData(true, "basket")]
    public async Task CollectionOrderCannotTreatSurvivalFoodAsACollectedLoad(bool explicitSource, string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-urgent-food", "berries", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "collect-after-food", kind, actor, 2, storageBuildingId: House);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
        };
        using var world = Restore(state);
        var source = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
        var text = $"collect {kind}" + (explicitSource ? $" from ({source.X}, {source.Y})" : "");
        var receipt = Submit(world, actor, "survival", text);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("interrupted", Order(world, receipt).Status);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(House, world.Society.Inventory.GetLot("collect-after-food").StorageBuildingId);
        for (var tick = 0; tick < 6 && Order(world, receipt).Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
        world.Validate();
    }

    [Fact]
    public void CollectionOrderRejectsCorruptedTargetsAndProgress()
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "validation", "collect two wood");
        var saved = world.ExportState();
        var valid = Order(world, receipt);
        foreach (var invalid in new[]
        {
            valid with { TargetMaterialKind = "cloth" }, valid with { TargetFoodKind = "berries" },
            valid with { TargetPosition = new(10_000_001, 1) }, valid with { TargetResourceId = "tree" },
            valid with { RequestedUnits = 0 }, valid with { CompletedUnits = -1 },
            valid with { ProgressUnit = "harvests" }, valid with { LastEffectId = "collect:personal:unearned" },
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

    [Theory]
    [InlineData("wood")]
    [InlineData("fruit")]
    [InlineData("basket")]
    public async Task CollectionOrderRejectsFinishedProgressBeyondTheExactRequestedQuantity(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "collect-exact", kind, actor, 2, storageBuildingId: House));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "exact", $"collect two {kind}");
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

    private static PrivateWorldRuntimeState Prepared(bool distant = false)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value);
        var actor = Actor(state);
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray(),
        });
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
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                LastDecisionContext = null,
            }).ToArray(),
        };
    }

    private static string Actor(PrivateWorldRuntimeState state) =>
        state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household).Id;
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new CollectionChoices());
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text, Queue: queue));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;

    private sealed class CollectionChoices(DecisionProviderKind kind = DecisionProviderKind.Deterministic, bool hold = false) : IDecisionProvider
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
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id is "collect_material" or "collect_food" or "collect_equipment")?.Id ?? "safe_idle";
            return new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 });
        }
    }
}
