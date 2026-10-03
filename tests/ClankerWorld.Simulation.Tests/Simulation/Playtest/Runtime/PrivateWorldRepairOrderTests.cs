using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldRepairOrderTests
{
    private const string Household = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private const string Shop = "alpha-tailor";
    private static readonly Lazy<byte[]> Generated = new(() =>
        PrivateWorldRuntimeCodec.Encode(TailorTestWorld.Create("equipment-repair-orders", 0).State));

    [Theory]
    [InlineData("clothing")]
    [InlineData("padded_coat")]
    [InlineData("rain_cloak")]
    [InlineData("basket")]
    [InlineData("sack")]
    public async Task RepairOrderCountsOnlyFinishedWorkAndSpendsItsExactMaterials(string kind)
    {
        var state = Prepared(kind);
        var actor = Actor(state);
        var inventory = AddWorn(state.Society.Society.Inventory, actor, "repair-target", kind);
        inventory = AddMaterials(inventory, actor, kind, 2);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "repair", "repair my " + kind.Replace('_', ' '));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 0, "repairs"), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ProgressUnit));
        var work = Assert.IsType<EquipmentRepairWork>(world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.Repair);
        Assert.Equal(receipt.InstructionId, work.OrderInstructionId);
        Assert.Equal(3_000, world.Society.Inventory.GetLot("repair-target").ConditionBasisPoints);
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(kind, projected.TargetEquipmentKind);
        await Finish(world, receipt);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(9_000, world.Society.Inventory.GetLot("repair-target").ConditionBasisPoints);
        Assert.Equal(actor, world.Society.Inventory.GetLot("repair-target").OwnerId);
        foreach (var input in PersonalEquipmentRules.RepairMaterials(kind))
            Assert.Equal(1, Quantity(world, actor, input.ResourceId));
        Assert.All(work.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(id).State));
        Assert.Single(world.ExportState().Events, item => item.Kind == "equipment_repaired");
        world.Validate();
    }

    [Fact]
    public async Task CountedRepairsQueueAndPartlyWorkedRepairsReplayWithoutDuplicateCosts()
    {
        var state = Prepared("sack");
        var actor = Actor(state);
        var inventory = AddWorn(state.Society.Society.Inventory, actor, "repair-a", "sack");
        inventory = AddWorn(inventory, actor, "repair-b", "sack");
        inventory = AddWorn(inventory, actor, "repair-c", "clothing");
        inventory = AddMaterials(inventory, actor, "sack", 3);
        using var world = Restore(WithInventory(state, inventory));
        var first = Submit(world, actor, "two", "repair two sacks");
        var second = Submit(world, actor, "queued", "repair a basic garment", queue: true);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, first).CompletedUnits);
        Assert.Equal("queued", Order(world, second).Status);
        Assert.Equal(3, world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair!.WorkDone);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 30 && Order(world, second).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        Assert.Equal(("finished", 2), (Order(restored, first).Status, Order(restored, first).CompletedUnits));
        Assert.Equal(("finished", 1), (Order(restored, second).Status, Order(restored, second).CompletedUnits));
        Assert.Equal(0, Quantity(restored, actor, "cloth"));
        Assert.Equal(3, restored.ExportState().Events.Count(item => item.Kind == "equipment_repaired"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrReplacementReleasesUnspentRepairMaterialsImmediately(bool replace)
    {
        var state = Prepared("padded_coat");
        var actor = Actor(state);
        var inventory = AddMaterials(AddWorn(state.Society.Society.Inventory, actor, "repair-cancel", "padded_coat"), actor, "padded_coat", 1);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "active", "keep repairing padded coats");
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var repair = world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair!;
        if (replace) _ = Submit(world, actor, "replacement", "store wood");
        else Assert.True(world.CancelOrder(new("cancel", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair);
        Assert.Equal("cancelled", Order(world, receipt).Status);
        Assert.Equal(1, Quantity(world, actor, "cloth"));
        Assert.All(repair.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation(id).State));
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 12; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(3_000, restored.Society.Inventory.GetLot("repair-cancel").ConditionBasisPoints);
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind == "equipment_repaired");
        restored.Validate();
    }

    [Theory]
    [InlineData("repair padded clothing")]
    [InlineData("repair rain garment")]
    [InlineData("repair a sword")]
    [InlineData("repair wood")]
    [InlineData("repair clothing and a basket")]
    [InlineData("do not repair clothing")]
    [InlineData("repair -2 baskets")]
    [InlineData("repair 1.5 sacks")]
    [InlineData("repair clothing at (1, 2)")]
    [InlineData("repair clothing in another Tailor Shop")]
    [InlineData("keep repair clothing")]
    public void UnsupportedRepairTextDoesNotReplaceTheActiveTask(string text)
    {
        var state = Prepared("clothing");
        using var world = Restore(state);
        var current = Submit(world, Actor(state), "current", "repair clothing");
        var rejected = Submit(world, Actor(state), "rejected", text);
        Assert.Equal("not_understood", Order(world, rejected).Status);
        Assert.Equal("waiting", Order(world, current).Status);
        world.Validate();
    }

    [Theory]
    [InlineData("borrowed")]
    [InlineData("reserved")]
    [InlineData("promised")]
    [InlineData("stored")]
    [InlineData("healthy")]
    [InlineData("wrong-kind")]
    [InlineData("materials")]
    public async Task RepairOrderCannotUseUnavailableItemsOrMaterials(string boundary)
    {
        var state = Prepared("padded_coat");
        var actor = Actor(state);
        var inventory = AddWorn(state.Society.Society.Inventory, boundary == "borrowed" ? Household : actor,
            "repair-unavailable", boundary == "wrong-kind" ? "basket" : "padded_coat");
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "repair-unavailable" ? lot with
            {
                CarrierId = boundary == "borrowed" ? actor : lot.CarrierId,
                StorageBuildingId = boundary == "stored" ? House : null,
                DeliveryBuildingId = boundary == "promised" ? House : null,
                ConditionBasisPoints = boundary == "healthy" ? 10_000 : lot.ConditionBasisPoints,
            } : lot).ToArray(),
        };
        if (boundary != "materials") inventory = AddMaterials(inventory, actor, "padded_coat", 1);
        if (boundary == "reserved") inventory = InventoryFixture.Reserve(inventory, "reserved-target", actor, "repair-unavailable", 1, "other_work", 120);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "blocked", "repair padded coat");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.Repair);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "equipment_repair_started");
        world.Validate();
    }

    [Fact]
    public async Task RepairOrderRequiresItsHouseholdsPrivateWorkSite()
    {
        var state = Prepared("clothing");
        var actor = Actor(state);
        var inventory = AddMaterials(AddWorn(state.Society.Society.Inventory, actor, "repair-homeless", "clothing"), actor, "clothing", 1);
        using var world = Restore(WithInventory(state, inventory));
        Assert.True(world.DisplaceAdult(actor));
        var receipt = Submit(world, actor, "site", "repair clothing");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("household's Tailor Shop", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(1, Quantity(world, actor, "cloth"));
    }

    [Fact]
    public async Task SurvivalInterruptsRepairWithoutSpendingMaterialsOrCreditingWork()
    {
        var state = Prepared("clothing");
        var actor = Actor(state);
        var inventory = AddMaterials(AddWorn(state.Society.Society.Inventory, actor, "repair-hungry", "clothing"), actor, "clothing", 1);
        inventory = InventoryFixture.AddLot(inventory, "repair-food", "berries", actor, 1);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "survival", "repair clothing");
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var saved = world.ExportState();
        var repair = saved.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair!;
        saved = saved with
        {
            Inhabitants = saved.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { HungerBasisPoints = 1_000 } : person).ToArray()
        };
        using var restored = Restore(saved);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("interrupted", 0), (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits));
        Assert.Equal(1, Quantity(restored, actor, "cloth"));
        Assert.All(repair.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Released, restored.Society.Inventory.GetReservation(id).State));
        await Finish(restored, receipt, 20);
        Assert.Equal(("finished", 1), (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits));
        Assert.Equal(0, Quantity(restored, actor, "cloth"));
        restored.Validate();
    }

    [Fact]
    public async Task SavedRepairCannotBeReboundToAnotherOrderOrEquipmentKind()
    {
        var state = Prepared("clothing");
        var actor = Actor(state);
        var inventory = AddMaterials(AddWorn(state.Society.Society.Inventory, actor, "repair-validation", "clothing"), actor, "clothing", 1);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "validate", "repair clothing");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var saved = world.ExportState();
        var corrupt = saved with
        {
            Inhabitants = saved.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Equipment = person.Equipment! with { Repair = person.Equipment!.Repair! with { OrderInstructionId = "another-order" } },
            } : person).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => Restore(corrupt));
        var valid = Order(world, receipt);
        foreach (var invalid in new[]
        {
            valid with { TargetEquipmentKind = "sword" }, valid with { TargetEquipmentKind = "basket" },
            valid with { TargetMaterialKind = "wood" }, valid with { TargetFoodKind = "berries" },
            valid with { TargetResourceId = "tree" }, valid with { TargetPosition = new(1, 1) },
            valid with { ProgressUnit = "material_items" }, valid with { LastEffectId = "repair:equipment:unearned" },
        })
        {
            corrupt = saved with
            {
                Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                ? item with { Order = invalid } : item).ToArray()
            };
            Assert.Throws<InvalidDataException>(() => Restore(corrupt));
        }
    }

    [Fact]
    public async Task NewRepairOrderReleasesAnOrdinaryRepairsInputsBeforeStartingItsOwnWork()
    {
        var state = Prepared("clothing");
        var actor = Actor(state);
        var inventory = AddMaterials(AddWorn(state.Society.Society.Inventory, actor, "repair-existing", "clothing"), actor, "clothing", 1);
        var started = state.Society.Society.WorldTick;
        inventory = InventoryFixture.Reserve(inventory, "ordinary-repair-cloth", actor, "repair-input-cloth", 1, "equipment_repair", started + 120);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Equipment = new(Repair: new("repair-existing", Shop, started, 0, ["ordinary-repair-cloth"])),
            } : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, actor, "take-over", "repair clothing");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation("ordinary-repair-cloth").State);
        Assert.Equal(receipt.InstructionId, world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair!.OrderInstructionId);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        await Finish(world, receipt);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(0, Quantity(world, actor, "cloth"));
    }

    [Fact]
    public async Task RepairOrderCollectsRealHouseholdClothAndWalksToTheTailorWithoutProgressCredit()
    {
        var state = Prepared("clothing");
        var actor = Actor(state);
        var inventory = AddWorn(state.Society.Society.Inventory, actor, "repair-travel", "clothing");
        inventory = InventoryFixture.AddLot(inventory, "repair-shared-cloth", "cloth", Household, 1, storageBuildingId: House);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = house.Position } : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, actor, "travel", "repair clothing");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(1, Quantity(world, actor, "cloth"));
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.Repair);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        await Finish(restored, receipt, 30);
        Assert.Equal("finished", Order(restored, receipt).Status);
        Assert.Equal(restored.WorldSimulation.Buildings.Single(building => building.InstanceId == Shop).Position,
            restored.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Equal(0, Quantity(restored, actor, "cloth"));
        Assert.Equal(0, Quantity(restored, Household, "cloth"));
    }

    [Fact]
    public async Task RepeatingRepairUsesOneModelDecisionAcrossSeveralJobsAndCanBeCancelled()
    {
        var state = Prepared("sack");
        var actor = Actor(state);
        var inventory = AddWorn(AddWorn(state.Society.Society.Inventory, actor, "repair-model-a", "sack"), actor, "repair-model-b", "sack");
        inventory = AddMaterials(inventory, actor, "sack", 2);
        var provider = new RepairChoices(DecisionProviderKind.LargeLanguageModel);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), id => id == actor ? provider : new RepairChoices());
        var receipt = Submit(world, actor, "model", "keep repairing sacks until cancelled");
        for (var tick = 0; tick < 20; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Single(provider.Requests, request => request.OperativeOrderInstructionId == receipt.InstructionId);
        Assert.Equal(0, Quantity(world, actor, "cloth"));
        Assert.True(world.CancelOrder(new("stop", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal("cancelled", Order(restored, receipt).Status);
    }

    [Fact]
    public async Task CancelledRepairCannotBeResurrectedByAHeldModelReply()
    {
        var state = Prepared("clothing");
        var actor = Actor(state);
        var inventory = AddMaterials(AddWorn(state.Society.Society.Inventory, actor, "repair-held", "clothing"), actor, "clothing", 1);
        var provider = new RepairChoices(DecisionProviderKind.LargeLanguageModel, hold: true);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), id => id == actor ? provider : new RepairChoices());
        var receipt = Submit(world, actor, "held", "repair clothing");
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var repair = world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair!;
            Assert.True(world.CancelOrder(new("stop-held", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var tick = 0; tick < 12; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Equal(1, Quantity(world, actor, "cloth"));
            Assert.All(repair.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation(id).State));
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "equipment_repaired");
            world.Validate();
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Fact]
    public async Task RepairReceiptStaysBoundedAndFinishedProgressCannotExceedTheRequest()
    {
        var state = Prepared("clothing");
        var actor = Actor(state);
        var lotId = "repair-long-" + new string('x', 600);
        var inventory = AddMaterials(AddWorn(state.Society.Society.Inventory, actor, lotId, "clothing"), actor, "clothing", 1);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "long", "repair one garment");
        await Finish(world, receipt);
        Assert.Equal("finished", Order(world, receipt).Status);
        Assert.InRange(Order(world, receipt).LastEffectId!.Length, 1, 512);
        var saved = world.ExportState();
        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var corrupt = saved with
        {
            Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
            ? item with { Order = item.Order! with { CompletedUnits = 2 } } : item).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => Restore(corrupt));
    }

    [Fact]
    public async Task RepairOrderDoesNotGiveChildrenAdultRepairWork()
    {
        var state = Prepared("clothing");
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
        state = WithInventory(state, AddWorn(state.Society.Society.Inventory, actor, "repair-child", "clothing"));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "child", "repair clothing");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("too young", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(3_000, world.Society.Inventory.GetLot("repair-child").ConditionBasisPoints);
        world.Validate();
    }

    private static PrivateWorldRuntimeState Prepared(string kind)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value);
        var actor = Actor(state);
        var site = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == (kind == "basket" ? House : Shop));
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray(),
        });
        return state with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? site.Position : person.Position,
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                Survival = new(),
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
            }).ToArray(),
        };
    }

    private static InventoryCheckpoint AddWorn(InventoryCheckpoint inventory, string owner, string id, string kind) =>
        InventoryFixture.WearSingleUnit(InventoryFixture.AddLot(inventory, id, kind, owner, 1), id, 7_000);
    private static InventoryCheckpoint AddMaterials(InventoryCheckpoint inventory, string actor, string kind, int quantity)
    {
        foreach (var input in PersonalEquipmentRules.RepairMaterials(kind))
            inventory = InventoryFixture.AddLot(inventory, "repair-input-" + input.ResourceId, input.ResourceId, actor, quantity);
        return inventory;
    }
    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household).Id;
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new RepairChoices());
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text, Queue: queue));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static int Quantity(PrivateWorldRuntime world, string actor, string kind) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == kind).Sum(lot => lot.Quantity);
    private static async Task Finish(PrivateWorldRuntime world, OwnerInstructionReceipt receipt, int maximum = 12)
    {
        for (var tick = 0; tick < maximum && Order(world, receipt).Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    }

    private sealed class RepairChoices(DecisionProviderKind kind = DecisionProviderKind.Deterministic, bool hold = false) : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 0;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            Requests.Enqueue(observation);
            if (hold && Requests.Count == 1 && observation.OperativeOrderInstructionId is not null)
            {
                Started.TrySetResult(true);
                await Release.Task;
                Returned.TrySetResult(true);
            }
            var selected = observation.OperativeOrderInstructionId is not null && observation.Candidates.Any(candidate => candidate.Id == "repair_equipment")
                ? "repair_equipment" : "safe_idle";
            return new(request.RequestId, observation.InhabitantId, Kind, request.ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 });
        }
    }
}
