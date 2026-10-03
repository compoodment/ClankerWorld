using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldToolRepairOrderTests
{
    private static readonly Lazy<byte[]> Generated = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("personal-tool-repair-orders", _ => new RepairChoices());
        world.AdvanceOneTickAsync().AsTask().GetAwaiter().GetResult();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Theory]
    [InlineData("wooden_axe")]
    [InlineData("stone_axe")]
    [InlineData("iron_axe")]
    [InlineData("wooden_pickaxe")]
    [InlineData("stone_pickaxe")]
    [InlineData("iron_pickaxe")]
    [InlineData("wooden_hoe")]
    [InlineData("iron_hoe")]
    [InlineData("wooden_hammer")]
    [InlineData("stone_hammer")]
    [InlineData("wooden_sickle")]
    [InlineData("iron_sickle")]
    [InlineData("iron_knife")]
    public async Task ExactToolKindRepairsOneRealUnitWithItsActualMaterialCosts(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = AddMaterials(AddTool(state.Society.Society.Inventory, actor, "target", kind), actor, kind, 2);
        inventory = AddTool(inventory, actor, "a-wrong-kind", kind == "wooden_axe" ? "iron_knife" : "wooden_axe");
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "repair", "repair my " + kind.Replace('_', ' '));
        await Finish(world, receipt);
        Assert.Equal(("finished", 1, "repairs"), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ProgressUnit));
        Assert.Equal(10_000, world.Society.Inventory.GetLot("target").ConditionBasisPoints);
        Assert.Equal(3_000, world.Society.Inventory.GetLot("a-wrong-kind").ConditionBasisPoints);
        Assert.Equal(actor, world.Society.Inventory.GetLot("target").OwnerId);
        foreach (var input in ToolProgressionRules.RepairMaterials(kind)) Assert.Equal(1, Quantity(world, actor, input.ResourceId));
        Assert.Single(world.ExportState().Events, item => item.Kind == "tool_repaired");
        Assert.Equal(kind, Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions).Order!.TargetEquipmentKind);
        world.Validate();
    }

    [Fact]
    public async Task QuantitySplitsAStackAndQueuedWorkReplaysWithoutDuplicateCosts()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = AddTool(state.Society.Society.Inventory, actor, "axes", "stone_axe", 2);
        inventory = AddTool(inventory, actor, "knife", "iron_knife");
        inventory = AddMaterials(inventory, actor, "stone_axe", 2);
        inventory = AddMaterials(inventory, actor, "iron_knife", 1);
        using var world = Restore(WithInventory(state, inventory));
        var first = Submit(world, actor, "axes", "repair two stone axes");
        var second = Submit(world, actor, "knife", "repair an iron knife", queue: true);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 1), (Order(world, first).Status, Order(world, first).CompletedUnits));
        Assert.Equal("queued", Order(world, second).Status);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 8 && Order(world, second).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(("finished", 2), (Order(replay, first).Status, Order(replay, first).CompletedUnits));
        Assert.Equal(("finished", 1), (Order(replay, second).Status, Order(replay, second).CompletedUnits));
        Assert.Equal(2, Quantity(replay, actor, "stone_axe"));
        Assert.All(replay.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor), lot => Assert.Equal(10_000, lot.ConditionBasisPoints));
        Assert.Equal(3, replay.ExportState().Events.Count(item => item.Kind == "tool_repaired"));
    }

    [Theory]
    [InlineData("repair an axe")]
    [InlineData("repair wooden knives")]
    [InlineData("repair stone hoes")]
    [InlineData("repair iron hammers")]
    [InlineData("repair diamond axe")]
    [InlineData("repair two iron knives and a basket")]
    [InlineData("repair stone axe at (1, 2)")]
    [InlineData("repair stone axe at another Blacksmith")]
    [InlineData("do not repair iron knife")]
    [InlineData("keep repair wooden axe")]
    public void UnsupportedTextPreservesTheExistingOrder(string text)
    {
        var state = Prepared();
        using var world = Restore(state);
        var current = Submit(world, Actor(state), "current", "repair wooden axe");
        var rejected = Submit(world, Actor(state), "rejected", text);
        Assert.Equal("not_understood", Order(world, rejected).Status);
        Assert.Equal("waiting", Order(world, current).Status);
        world.Validate();
    }

    [Theory]
    [InlineData("borrowed")]
    [InlineData("reserved")]
    [InlineData("delivery")]
    [InlineData("stored")]
    [InlineData("healthy")]
    [InlineData("broken")]
    [InlineData("materials")]
    public async Task UnavailableToolsAndInputsDoNotEarnProgress(string boundary)
    {
        var state = Prepared();
        var actor = Actor(state);
        var household = Household(state);
        var house = House(state);
        var inventory = AddTool(state.Society.Society.Inventory, boundary == "borrowed" ? household : actor, "target", "iron_knife");
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "target" ? lot with
            {
                CarrierId = boundary == "borrowed" ? actor : lot.CarrierId,
                StorageBuildingId = boundary == "stored" ? house : null,
                DeliveryBuildingId = boundary == "delivery" ? house : null,
                ConditionBasisPoints = boundary == "healthy" ? 10_000 : boundary == "broken" ? 0 : lot.ConditionBasisPoints,
            } : lot).ToArray()
        };
        if (boundary != "materials") inventory = AddMaterials(inventory, actor, "iron_knife", 1);
        if (boundary == "reserved") inventory = InventoryFixture.Reserve(inventory, "held-tool", actor, "target", 1, "other_work", 1000);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "blocked", "repair iron knife");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "tool_repaired");
        world.Validate();
    }

    [Fact]
    public async Task RepeatingRepairsUseOneModelDecisionAndCancellationSurvivesReload()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = AddMaterials(AddTool(state.Society.Society.Inventory, actor, "knives", "iron_knife", 2), actor, "iron_knife", 2);
        var provider = new RepairChoices(DecisionProviderKind.LargeLanguageModel);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), id => id == actor ? provider : new RepairChoices());
        var receipt = Submit(world, actor, "repeat", "keep repairing iron knives until cancelled");
        for (var tick = 0; tick < 5; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Single(provider.Requests, request => request.OperativeOrderInstructionId == receipt.InstructionId);
        Assert.Equal(0, Quantity(world, actor, "iron"));
        Assert.True(world.CancelOrder(new("cancel", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(("cancelled", 2), (Order(replay, receipt).Status, Order(replay, receipt).CompletedUnits));
    }

    [Fact]
    public async Task MaterialPickupAndTravelEarnNoCreditAndResumeAfterReload()
    {
        var state = PreparedForPickup();
        var actor = Actor(state);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "pickup", "repair wooden axe");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(1, Quantity(world, actor, "wood"));
        Assert.Equal(3_000, world.Society.Inventory.GetLot("target").ConditionBasisPoints);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        await Finish(resumed, receipt, 50);
        Assert.Equal(("finished", 1), (Order(resumed, receipt).Status, Order(resumed, receipt).CompletedUnits));
        Assert.Equal(0, Quantity(resumed, actor, "wood"));
        Assert.Equal(0, Quantity(resumed, Household(state), "wood"));
        Assert.Equal(state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith").Position,
            resumed.Inhabitants.Single(person => person.InhabitantId == actor).Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrReplaceDuringPreparationKeepsCollectedGoodsAndCannotRepairLater(bool replace)
    {
        var state = PreparedForPickup();
        var actor = Actor(state);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "preparing", "repair wooden axe");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, Quantity(world, actor, "wood"));
        if (replace) _ = Submit(world, actor, "replacement", "repair iron knife");
        else Assert.True(world.CancelOrder(new("stop", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 5; tick++) Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("cancelled", 0), (Order(resumed, receipt).Status, Order(resumed, receipt).CompletedUnits));
        Assert.Equal(1, Quantity(resumed, actor, "wood"));
        Assert.Equal(3_000, resumed.Society.Inventory.GetLot("target").ConditionBasisPoints);
        Assert.DoesNotContain(resumed.ExportState().Events, item => item.Kind == "tool_repaired");
    }

    [Fact]
    public async Task LateModelReplyCannotRestartACancelledPreparation()
    {
        var state = PreparedForPickup();
        var actor = Actor(state);
        var provider = new RepairChoices(DecisionProviderKind.LargeLanguageModel, hold: true);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new RepairChoices());
        var receipt = Submit(world, actor, "held", "repair wooden axe");
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, Order(world, receipt).CompletedUnits);
            Assert.True(world.CancelOrder(new("stop", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var tick = 0; tick < 5; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Equal(3_000, world.Society.Inventory.GetLot("target").ConditionBasisPoints);
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "tool_repaired");
            world.Validate();
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Fact]
    public async Task SurvivalInterruptsPreparationAndResumesTheUnfinishedRepair()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = AddMaterials(AddTool(state.Society.Society.Inventory, actor, "target", "iron_knife"), actor, "iron_knife", 1);
        inventory = InventoryFixture.AddLot(inventory, "survival-food", "berries", actor, 1);
        state = WithInventory(state, inventory) with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { HungerBasisPoints = 1_000 } : person).ToArray() };
        using var world = Restore(state);
        var receipt = Submit(world, actor, "survival", "repair iron knife");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("interrupted", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(1, Quantity(world, actor, "iron"));
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        await Finish(resumed, receipt, 20);
        Assert.Equal(("finished", 1), (Order(resumed, receipt).Status, Order(resumed, receipt).CompletedUnits));
        Assert.Equal(0, Quantity(resumed, actor, "iron"));
    }

    [Fact]
    public async Task LeavingTheHouseholdDoesNotGrantAccessToItsBlacksmith()
    {
        var state = Prepared();
        var actor = Actor(state);
        using var world = Restore(WithInventory(state, AddMaterials(AddTool(state.Society.Society.Inventory, actor, "target", "iron_knife"), actor, "iron_knife", 1)));
        Assert.True(world.DisplaceAdult(actor));
        var receipt = Submit(world, actor, "private", "repair iron knife");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("household's Blacksmith", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(3_000, world.Society.Inventory.GetLot("target").ConditionBasisPoints);
        Assert.Equal(1, Quantity(world, actor, "iron"));
    }

    [Fact]
    public async Task LongLotIdsHaveBoundedReceiptsAndInvalidSavedOrdersAreRefused()
    {
        var state = Prepared();
        var actor = Actor(state);
        var lotId = "target-" + new string('x', 600);
        using var world = Restore(WithInventory(state, AddMaterials(AddTool(state.Society.Society.Inventory, actor, lotId, "iron_knife"), actor, "iron_knife", 1)));
        var receipt = Submit(world, actor, "long", "repair iron knife");
        await Finish(world, receipt);
        var order = Order(world, receipt);
        Assert.Equal("finished", order.Status);
        Assert.InRange(order.LastEffectId!.Length, 1, 512);
        var saved = world.ExportState();
        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        foreach (var invalid in new[]
        {
            order with { TargetEquipmentKind = "wooden_knife" }, order with { TargetEquipmentKind = "basket" },
            order with { TargetMaterialKind = "iron" }, order with { TargetFoodKind = "berries" },
            order with { TargetPosition = new(1, 2) }, order with { TargetCropKind = "grain" },
            order with { TargetResourceId = "tree" }, order with { ProgressUnit = "material_items" },
            order with { CompletedUnits = 2 }, order with { LastEffectId = "repair:equipment:wrong" },
        })
            Assert.Throws<InvalidDataException>(() => Restore(saved with { Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId ? item with { Order = invalid } : item).ToArray() }));
        Assert.Throws<InvalidDataException>(() => Restore(saved with { SchemaVersion = 57 }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationMakesSpaceWithoutStowingTheRepairOrGatheringTool(bool gather)
    {
        var state = Prepared();
        var actor = Actor(state);
        var household = Household(state);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House(state));
        var tree = state.Map.Resources.Where(resource => TreeGrowthRules.IsWoodTree(resource.TreeKind) &&
                state.Map.IsReachableOnFoot(house.Position, resource.Position))
            .OrderBy(resource => state.Map.FootDistance(house.Position, resource.Position)).First();
        var inventory = AddTool(state.Society.Society.Inventory, actor, "target", "wooden_axe");
        inventory = InventoryFixture.AddLot(inventory, "z-harvest-tool", "wooden_axe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "ballast", "fiber", actor, 14);
        inventory = InventoryFixture.AddLot(inventory, "basket", "basket", actor, 1);
        if (!gather) inventory = InventoryFixture.AddLot(inventory, "shared-wood", "wood", household, 1, storageBuildingId: house.InstanceId);
        state = WithInventory(state, inventory) with
        {
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == tree.Id && gather
                        ? resource with { Quantity = 1, State = EcologyResourceState.Available }
                        : resource with { Quantity = 0, State = EcologyResourceState.Depleted }).ToArray(),
                }
            },
            Resources = state.Resources.Select(resource => resource with { State = resource.ResourceId == tree.Id && gather ? ResourceState.Available : ResourceState.Depleted }).ToArray(),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Position = house.Position, Equipment = new(CarryAidLotId: "basket") } : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, actor, "capacity", "repair wooden axe");
        for (var tick = 0; tick < 100 && Quantity(world, actor, "wood") == 0; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(gather ? 6 : 1, Quantity(world, actor, "wood"));
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(3_000, world.Society.Inventory.GetLot("target").ConditionBasisPoints);
        Assert.Equal(actor, world.Society.Inventory.GetLot("target").OwnerId);
        Assert.Equal(actor, world.Society.Inventory.GetLot("z-harvest-tool").OwnerId);
        Assert.Equal(gather ? 8_000 : 10_000, world.Society.Inventory.GetLot("z-harvest-tool").ConditionBasisPoints);
        Assert.Equal(14, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "fiber").Sum(lot => lot.Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "spare_cargo_stored");
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        await Finish(resumed, receipt, 100);
        Assert.Equal(("finished", 1), (Order(resumed, receipt).Status, Order(resumed, receipt).CompletedUnits));
        Assert.Equal(gather ? 5 : 0, Quantity(resumed, actor, "wood"));
        Assert.Equal(10_000, resumed.Society.Inventory.GetLot("target").ConditionBasisPoints);
        resumed.Validate();
    }

    [Fact]
    public async Task ChildCannotPerformAdultToolRepair()
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
                        CurrentRole = SocietyWorkRole.Unassigned
                    } : person).ToArray(),
                }
            },
            Towns = state.Towns!.Select(town => town with
            { Governance = TownGovernanceState.Create(town.ResidentIds.Where(id => id != actor && checkpoint.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)) }).ToArray(),
        };
        using var world = Restore(WithInventory(state, AddMaterials(AddTool(state.Society.Society.Inventory, actor, "target", "iron_knife"), actor, "iron_knife", 1)));
        var receipt = Submit(world, actor, "child", "repair iron knife");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("too young", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(1, Quantity(world, actor, "iron"));
        Assert.Equal(3_000, world.Society.Inventory.GetLot("target").ConditionBasisPoints);
    }

    private static PrivateWorldRuntimeState PreparedForPickup()
    {
        var state = Prepared();
        var actor = Actor(state);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House(state));
        var inventory = AddTool(state.Society.Society.Inventory, actor, "target", "wooden_axe");
        inventory = InventoryFixture.AddLot(inventory, "shared-wood", "wood", Household(state), 1, storageBuildingId: house.InstanceId);
        return WithInventory(state, inventory) with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = house.Position } : person).ToArray() };
    }

    private static PrivateWorldRuntimeState Prepared()
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value);
        var actor = Actor(state);
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        return WithInventory(state, state.Society.Society.Inventory with { Lots = [], Reservations = [] }) with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? smith.Position : person.Position,
                Equipment = null,
                Project = null,
                Survival = new(),
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
            }).ToArray(),
        };
    }
    private static string Household(PrivateWorldRuntimeState state) => state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith").HouseholdId!;
    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household(state) && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
    private static string House(PrivateWorldRuntimeState state) => state.WorldSimulation!.Buildings.First(building => building.HouseholdId == Household(state) && building.InstanceId.StartsWith("first-town-house", StringComparison.Ordinal)).InstanceId;
    private static InventoryCheckpoint AddTool(InventoryCheckpoint inventory, string actor, string id, string kind, int quantity = 1) => InventoryFixture.AddLot(inventory, id, kind, actor, quantity, conditionBasisPoints: 3_000);
    private static InventoryCheckpoint AddMaterials(InventoryCheckpoint inventory, string actor, string kind, int quantity)
    {
        foreach (var input in ToolProgressionRules.RepairMaterials(kind)) inventory = InventoryFixture.AddLot(inventory, "input-" + input.ResourceId, input.ResourceId, actor, quantity);
        return inventory;
    }
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new RepairChoices());
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text, bool queue = false) => world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text, Queue: queue));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) => world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static int Quantity(PrivateWorldRuntime world, string actor, string kind) => world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == kind).Sum(lot => lot.Quantity);
    private static async Task Finish(PrivateWorldRuntime world, OwnerInstructionReceipt receipt, int maximum = 10)
    {
        for (var tick = 0; tick < maximum && Order(world, receipt).Status != "finished"; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
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
            var selected = observation.OperativeOrderInstructionId is not null && observation.Candidates.Any(candidate => candidate.Id == "repair_tool") ? "repair_tool" : "safe_idle";
            return new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, request.ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 });
        }
    }
}
