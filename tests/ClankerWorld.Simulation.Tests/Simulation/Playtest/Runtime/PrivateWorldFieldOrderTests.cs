using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldFieldOrderTests
{
    private static readonly Lazy<(byte[] Bytes, string Actor, string Household, GridPoint Point)> Generated = new(() =>
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("field-owner-orders");
        using var world = Restore(state);
        world.AdvanceOneTickAsync().AsTask().GetAwaiter().GetResult();
        world.AdvanceOneTickAsync().AsTask().GetAwaiter().GetResult();
        return (PrivateWorldRuntimeCodec.Encode(world.ExportState()), actor, household, point);
    });
    private static string Actor => Generated.Value.Actor;
    private static string Household => Generated.Value.Household;
    private static GridPoint Point => Generated.Value.Point;

    [Theory]
    [InlineData("grain")]
    [InlineData("potatoes")]
    [InlineData("cultivated_greens")]
    public async Task PlantingOrdersSpendARealSeedOnlyWhenTheRequestedCropIsPlanted(string crop)
    {
        var state = WithFields(WithSeeds(Prepared(), crop, 1), new FarmFieldState(Point, Household, FarmFieldStage.Prepared));
        using var world = Restore(state);
        var receipt = Submit(world, "plant", "plant " + crop.Replace('_', ' '));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var work = Assert.Single(world.Fields).Work!;
        Assert.Equal(receipt.InstructionId, work.OrderInstructionId);
        Assert.Equal(("doing", 0, "fields"), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ProgressUnit));
        Assert.Equal(1, SeedQuantity(world, crop));
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(work.SeedReservationId!).State);
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions).Order!;
        Assert.Equal(crop, projected.TargetCropKind);
        await Finish(world, receipt);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(crop, Assert.Single(world.Fields).Crop);
        Assert.Equal(0, SeedQuantity(world, crop));
        Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(work.SeedReservationId!).State);
        Assert.Single(world.ExportState().Events, item => item.Kind == "field_planted");
        world.Validate();
    }

    [Theory]
    [InlineData("grain")]
    [InlineData("potatoes")]
    [InlineData("cultivated_greens")]
    public async Task HarvestOrdersLeaveRealHouseholdProduceAndItsReplantingReserveOnTheField(string crop)
    {
        var state = WithFields(Prepared(), ReadyField(Point, crop));
        using var world = Restore(state);
        var receipt = Submit(world, "harvest", "harvest " + crop.Replace('_', ' '));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        await Finish(world, receipt);
        var field = Assert.Single(world.Fields);
        Assert.Equal((FarmFieldStage.Harvested, 1), (field.Stage, field.Cycle));
        var produce = Assert.Single(world.Society.Inventory.Lots, lot => lot.Id == FarmFieldRules.FieldId(Point) + ":harvest:1:crop");
        Assert.Equal((Household, crop, new InventoryGroundPosition(Point.X, Point.Y)), (produce.OwnerId, produce.ItemKind, produce.GroundPosition));
        Assert.InRange(produce.Quantity, 1, 20);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(field.ReplantingReservationId!).State);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        world.Validate();
    }

    [Fact]
    public async Task TillingAndTendingCountFinishedFieldsAndWearTheActualHoe()
    {
        using var world = Restore(Prepared());
        var condition = world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints;
        var till = Submit(world, "till", "till two fields");
        await Finish(world, till, 35);
        Assert.Equal(("finished", 2), (Order(world, till).Status, Order(world, till).CompletedUnits));
        Assert.Equal(2, world.Fields.Count);
        Assert.All(world.Fields, field => Assert.Equal(FarmFieldStage.Prepared, field.Stage));
        Assert.True(world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints < condition);
        var saved = world.ExportState();
        saved = saved with
        {
            Fields = saved.Fields!.Select(field => field with
            { Stage = FarmFieldStage.Growing, Crop = "grain", ReadyTick = 10_000 }).ToArray()
        };
        saved = FarmFieldTests.WithInventory(saved, InventoryFixture.AddLot(saved.Society.Society.Inventory, "tending-hoe", "iron_hoe", Actor, 1));
        using var tending = Restore(saved);
        var tend = Submit(tending, "tend", "tend two fields of grain");
        await Finish(tending, tend, 25);
        Assert.Equal(("finished", 2), (Order(tending, tend).Status, Order(tending, tend).CompletedUnits));
        Assert.All(tending.Fields, field => Assert.True(field.Tended));
        Assert.True(tending.Society.Inventory.GetLot("tending-hoe").ConditionBasisPoints < 10_000);
        tending.Validate();
    }

    [Fact]
    public async Task QueuedFieldWorkReplaysFromAPartialPlantingWithoutDuplicateSeedUse()
    {
        var state = WithFields(WithSeeds(Prepared(), "grain", 1), new FarmFieldState(Point, Household, FarmFieldStage.Prepared));
        using var world = Restore(state);
        var plant = Submit(world, "plant", "plant a field of grain");
        var tend = Submit(world, "tend", "tend grain", queue: true);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("queued", Order(world, tend).Status);
        Assert.Equal(0, Order(world, plant).CompletedUnits);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 14 && Order(world, tend).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(("finished", 1), (Order(restored, plant).Status, Order(restored, plant).CompletedUnits));
        Assert.Equal(("finished", 1), (Order(restored, tend).Status, Order(restored, tend).CompletedUnits));
        Assert.Equal(0, SeedQuantity(restored, "grain"));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Single(restored.ExportState().Events, item => item.Kind == "field_planted");
        Assert.Single(restored.ExportState().Events, item => item.Kind == "field_tended");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancellationAndReplacementReleaseUnspentPlantingStockImmediately(bool replace, bool explicitLocation)
    {
        using var world = Restore(WithFields(WithSeeds(Prepared(), "grain", 1), new FarmFieldState(Point, Household, FarmFieldStage.Prepared)));
        var text = "plant grain" + (explicitLocation ? $" at ({Point.X}, {Point.Y})" : "");
        var receipt = Submit(world, "plant", text);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var reservation = Assert.Single(world.Fields).Work!.SeedReservationId!;
        if (replace) _ = Submit(world, "replace", "harvest potatoes");
        else Assert.True(world.CancelOrder(new("cancel", "owner:test", world.Society.WorldId, Actor, receipt.InstructionId)).Changed);
        Assert.Null(Assert.Single(world.Fields).Work);
        Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation(reservation).State);
        Assert.Equal("cancelled", Order(world, receipt).Status);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 8; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(restored.Fields).Stage);
        Assert.Equal(1, SeedQuantity(restored, "grain"));
        restored.Validate();
    }

    [Fact]
    public async Task CancelledTillingRemovesTheUnfinishedFieldWithoutCreditingIt()
    {
        using var world = Restore(Prepared());
        var receipt = Submit(world, "till", "till a field");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(FarmFieldStage.Preparing, Assert.Single(world.Fields).Stage);
        Assert.True(world.CancelOrder(new("cancel-till", "owner:test", world.Society.WorldId, Actor, receipt.InstructionId)).Changed);
        Assert.Empty(world.Fields);
        Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        world.Validate();
    }

    [Theory]
    [InlineData("plant two grain")]
    [InlineData("harvest 5 potatoes")]
    [InlineData("till grain")]
    [InlineData("plant fields")]
    [InlineData("plant two fields of apples")]
    [InlineData("tend cultivated")]
    [InlineData("plant cultivated grain")]
    [InlineData("plant grain and potatoes")]
    [InlineData("plant grain at (1,)")]
    [InlineData("plant grain in another household")]
    [InlineData("do not till fields")]
    [InlineData("till -2 fields")]
    [InlineData("till 1.5 fields")]
    [InlineData("keep till fields")]
    public void UnsupportedOrAmbiguousFieldTextDoesNotReplaceTheCurrentOrder(string text)
    {
        using var world = Restore(Prepared());
        var current = Submit(world, "current", "till fields");
        var rejected = Submit(world, "bad", text);
        Assert.Equal("not_understood", Order(world, rejected).Status);
        Assert.Equal("waiting", Order(world, current).Status);
        world.Validate();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("unripe")]
    [InlineData("wrong-crop")]
    public async Task HarvestCannotSubstituteForeignUnripeOrDifferentCrops(string boundary)
    {
        var state = Prepared();
        var field = ReadyField(Point, boundary == "wrong-crop" ? "potatoes" : "grain");
        if (boundary == "foreign") field = field with { HouseholdId = state.Society.Society.Households.First(home => home.Id != Household).Id };
        if (boundary == "unripe") field = field with { Stage = FarmFieldStage.Growing, ReadyTick = 10_000 };
        using var world = Restore(WithFields(state, field));
        var receipt = Submit(world, "harvest", "harvest grain");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Null(Assert.Single(world.Fields).Work);
        world.Validate();
    }

    [Theory]
    [InlineData("hoe")]
    [InlineData("farmhouse")]
    [InlineData("seed")]
    [InlineData("reserved-seed")]
    [InlineData("carrying-space")]
    public async Task FieldOrdersWaitWhenToolsPlantingStockOrHouseholdAccessAreUnavailable(string boundary)
    {
        var state = Prepared();
        var inventory = state.Society.Society.Inventory;
        if (boundary == "hoe") inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.Id != "carried-hoe").ToArray() };
        if (boundary == "reserved-seed")
            inventory = InventoryFixture.Reserve(InventoryFixture.AddLot(inventory, "reserved-seed", "grain_seed", Actor, 1),
                "seed-held", Actor, "reserved-seed", 1, "other_work", 100);
        if (boundary == "carrying-space")
        {
            inventory = InventoryFixture.AddLot(inventory, "heavy-load", "wood", Actor, 100);
            inventory = InventoryFixture.AddLot(inventory, "shared-seed", "grain_seed", Household, 1);
        }
        state = WithFields(FarmFieldTests.WithInventory(state, inventory), new FarmFieldState(Point, Household, FarmFieldStage.Prepared));
        using var world = Restore(state);
        if (boundary == "farmhouse") Assert.True(world.DisplaceAdult(Actor));
        var receipt = Submit(world, "blocked", boundary is "hoe" or "farmhouse" ? "till fields" : "plant grain");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.All(world.Fields, field => Assert.Null(field.Work));
        world.Validate();
    }

    [Fact]
    public async Task UrgentFoodReleasesTheSeedThenTheRemainingPlantingResumes()
    {
        var state = WithFields(WithSeeds(Prepared(), "grain", 1), new FarmFieldState(Point, Household, FarmFieldStage.Prepared));
        state = FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "order-food", "berries", Actor, 1));
        using var world = Restore(state);
        var receipt = Submit(world, "plant", "plant grain");
        for (var tick = 0; tick < 2; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var saved = world.ExportState();
        var reservation = Assert.Single(world.Fields).Work!.SeedReservationId!;
        saved = saved with { Inhabitants = saved.Inhabitants.Select(person => person.InhabitantId == Actor ? person with { HungerBasisPoints = 1_000 } : person).ToArray() };
        using var restored = Restore(saved);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("interrupted", 0), (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits));
        Assert.Equal(InventoryReservationState.Released, restored.Society.Inventory.GetReservation(reservation).State);
        Assert.Equal(1, SeedQuantity(restored, "grain"));
        await Finish(restored, receipt, 20);
        Assert.Equal(("finished", 1), (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits));
        Assert.Equal(0, SeedQuantity(restored, "grain"));
        restored.Validate();
    }

    [Fact]
    public async Task PlantingCollectsHouseholdStockAtItsRealLocationBeforeAnyProgress()
    {
        var state = Prepared();
        var farmhouse = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == Household && building.InstanceId == "first-town-farmhouse");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "stored-seed", "grain_seed", Household, 1, storageBuildingId: farmhouse.InstanceId);
        using var world = Restore(WithFields(FarmFieldTests.WithInventory(state, inventory), new FarmFieldState(Point, Household, FarmFieldStage.Prepared)));
        var receipt = Submit(world, "plant", "plant grain");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        await Finish(world, receipt, 40);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(0, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain_seed").Sum(lot => lot.Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "field_planted");
        world.Validate();
    }

    [Fact]
    public async Task ANewFieldOrderReleasesAnOrdinaryJobsUnusedSeed()
    {
        var state = WithFields(WithSeeds(Prepared(), "grain", 1), new FarmFieldState(Point, Household, FarmFieldStage.Prepared));
        using var world = Restore(state);
        Assert.True(world.StartFieldWork(Actor, Point, FarmWorkKind.Plant, "grain", "order-seed").Accepted);
        var reservation = Assert.Single(world.Fields).Work!.SeedReservationId!;
        var receipt = Submit(world, "till", "till a field");
        await Finish(world, receipt, 25);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation(reservation).State);
        Assert.Equal(1, SeedQuantity(world, "grain"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "field_planted");
        world.Validate();
    }

    [Fact]
    public async Task RepeatingFieldWorkUsesOneModelDecisionThenWaitsForMoreMatchingWork()
    {
        var state = Prepared();
        var second = OtherPoint(state);
        state = WithFields(WithSeeds(state, "grain", 2), new FarmFieldState(Point, Household, FarmFieldStage.Prepared), new FarmFieldState(second, Household, FarmFieldStage.Prepared));
        var provider = new FieldChoices(DecisionProviderKind.LargeLanguageModel);
        using var world = PrivateWorldRuntime.Restore(state, actor => actor == Actor ? provider : new FieldChoices());
        var receipt = Submit(world, "repeat", "keep planting grain");
        for (var tick = 0; tick < 30 && Order(world, receipt).CompletedUnits < 2; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, Order(world, receipt).CompletedUnits);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Single(provider.Requests, item => item.OperativeOrderInstructionId == receipt.InstructionId);
        Assert.Equal(0, SeedQuantity(world, "grain"));
        Assert.True(world.CancelOrder(new("stop", "owner:test", world.Society.WorldId, Actor, receipt.InstructionId)).Changed);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal("cancelled", Order(restored, receipt).Status);
    }

    [Fact]
    public async Task SavedFieldWorkMustMatchItsActiveInstructionAndCrop()
    {
        using var world = Restore(WithFields(WithSeeds(Prepared(), "grain", 1), new FarmFieldState(Point, Household, FarmFieldStage.Prepared)));
        var receipt = Submit(world, "plant", "plant grain");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        var field = Assert.Single(state.Fields!);
        Assert.Throws<InvalidDataException>(() => Restore(state with { Fields = [field with { Work = field.Work! with { OrderInstructionId = "wrong-order" } }] }));
        foreach (var invalid in new[]
        {
            Order(world, receipt) with { TargetCropKind = "potatoes" },
            Order(world, receipt) with { TargetCropKind = "apples" },
            Order(world, receipt) with { TargetCropKind = null },
            Order(world, receipt) with { Action = "tend_field" },
            Order(world, receipt) with { TargetFoodKind = "berries" },
            Order(world, receipt) with { TargetMaterialKind = "wood" },
            Order(world, receipt) with { TargetEquipmentKind = "basket" },
            Order(world, receipt) with { TargetAgentId = Actor },
            Order(world, receipt) with { TargetPosition = new(1, 2) },
            Order(world, receipt) with { ProgressUnit = "food_items" },
            Order(world, receipt) with { LastEffectId = "field:work:unearned" },
        })
            Assert.Throws<InvalidDataException>(() => Restore(state with
            { Instructions = state.Instructions!.Select(item => item.InstructionId == receipt.InstructionId ? item with { Order = invalid } : item).ToArray() }));
        Assert.Throws<InvalidDataException>(() => Restore(state with { SchemaVersion = 71 }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACancelledHeldModelReplyCannotSpendTheReleasedSeed(bool explicitLocation)
    {
        var provider = new FieldChoices(DecisionProviderKind.LargeLanguageModel, hold: true);
        using var world = PrivateWorldRuntime.Restore(WithFields(WithSeeds(Prepared(), "grain", 1), new FarmFieldState(Point, Household, FarmFieldStage.Prepared)),
            actor => actor == Actor ? provider : new FieldChoices());
        var text = "plant grain" + (explicitLocation ? $" at ({Point.X}, {Point.Y})" : "");
        var receipt = Submit(world, "held", text);
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var reservation = Assert.Single(world.Fields).Work!.SeedReservationId!;
            Assert.True(world.CancelOrder(new("stop-held", "owner:test", world.Society.WorldId, Actor, receipt.InstructionId)).Changed);
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Equal(1, SeedQuantity(world, "grain"));
            Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation(reservation).State);
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "field_planted");
            world.Validate();
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Fact]
    public async Task AToolBreakingDuringTillingEarnsNoProgressAndAReplacementResumesTheTask()
    {
        var state = Prepared();
        state = FarmFieldTests.WithInventory(state, InventoryFixture.WearSingleUnit(state.Society.Society.Inventory, "carried-hoe", 9_000));
        using var world = Restore(state);
        var receipt = Submit(world, "tool-break", "till a field");
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Empty(world.Fields);
        Assert.Equal(0, world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        var saved = world.ExportState();
        saved = FarmFieldTests.WithInventory(saved, InventoryFixture.AddLot(saved.Society.Society.Inventory, "replacement-hoe", "iron_hoe", Actor, 1));
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        await Finish(restored, receipt);
        Assert.Equal(("finished", 1), (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits));
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(restored.Fields).Stage);
        Assert.Equal(7_000, restored.Society.Inventory.GetLot("replacement-hoe").ConditionBasisPoints);
        restored.Validate();
    }

    [Fact]
    public async Task AnOrderDoesNotGiveChildrenAdultFieldWork()
    {
        var state = Prepared();
        var checkpoint = state.Society.Society;
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick) - 4 * checkpoint.Config.TicksPerLifecycleAge;
        state = state with
        {
            Society = state.Society with
            {
                Society = checkpoint with
                {
                    Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == Actor ? person with
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
                Governance = TownGovernanceState.Create(town.ResidentIds.Where(id => id != Actor &&
                    checkpoint.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)),
            }).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, "child", "till fields");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Contains("too young", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Empty(world.Fields);
        Assert.Equal(10_000, world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        world.Validate();
    }

    private static PrivateWorldRuntimeState Prepared()
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value.Bytes);
        var inventory = state.Society.Society.Inventory;
        return FarmFieldTests.WithInventory(state, inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.ItemKind is not ("grain_seed" or "cultivated_green_seed" or "potatoes")).ToArray(),
        }) with
        {
            Fields = [],
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            { HungerBasisPoints = 10_000, Survival = new(), LastDecisionContext = null, Position = person.InhabitantId == Actor ? Point : person.Position }).ToArray(),
        };
    }
    private static PrivateWorldRuntimeState WithFields(PrivateWorldRuntimeState state, params FarmFieldState[] fields) =>
        state with { Fields = fields.OrderBy(field => field.Position.Y).ThenBy(field => field.Position.X).ToArray() };
    private static FarmFieldState ReadyField(GridPoint point, string crop) => new(point, Household, FarmFieldStage.Ready, crop, ReadyTick: 1, Tended: true);
    private static PrivateWorldRuntimeState WithSeeds(PrivateWorldRuntimeState state, string crop, int count) =>
        FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "order-seed", FarmFieldRules.PlantingItem(crop), Actor, count));
    private static int SeedQuantity(PrivateWorldRuntime world, string crop) => world.Society.Inventory.Lots.Where(lot => lot.ItemKind == FarmFieldRules.PlantingItem(crop)).Sum(lot => lot.Quantity);
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new FieldChoices());
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", Actor, OwnerInstructionKind.MustDo, text, Queue: queue));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static async Task Finish(PrivateWorldRuntime world, OwnerInstructionReceipt receipt, int maximum = 14)
    {
        for (var tick = 0; tick < maximum && Order(world, receipt).Status != "finished"; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    }
    private static GridPoint OtherPoint(PrivateWorldRuntimeState state)
    {
        var occupied = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(state.RoadTiles ?? [])
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var fertility = new LandFertility(state.Map, state.WorldSeed);
        return state.Map.Tiles.Select(tile => tile.Position).Where(point => point != Point && fertility.CanFarm(point) &&
            !occupied.Contains(point) && !state.Inhabitants.Any(person => person.Position == point) && state.Map.IsReachableOnFoot(Point, point))
            .OrderBy(point => state.Map.FootDistance(Point, point)).First();
    }
    private sealed class FieldChoices(DecisionProviderKind kind = DecisionProviderKind.Deterministic, bool hold = false) : IDecisionProvider
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
            var selected = observation.OperativeOrderInstructionId is not null && observation.Candidates.Any(candidate => candidate.Id == "work_field")
                ? "work_field" : "safe_idle";
            return new(request.RequestId, observation.InhabitantId, Kind, request.ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 });
        }
    }
}
