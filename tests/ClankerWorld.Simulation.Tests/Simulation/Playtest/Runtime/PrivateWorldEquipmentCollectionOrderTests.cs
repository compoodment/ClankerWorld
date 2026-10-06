using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldCollectionOrderTests
{
    [Theory]
    [InlineData("crude_wooden_axe")]
    [InlineData("crude_wooden_pickaxe")]
    [InlineData("clothing")]
    [InlineData("padded_coat")]
    [InlineData("rain_cloak")]
    [InlineData("basket")]
    [InlineData("sack")]
    [InlineData("wooden_axe")]
    [InlineData("stone_axe")]
    [InlineData("iron_axe")]
    [InlineData("stone_pickaxe")]
    [InlineData("iron_pickaxe")]
    [InlineData("wooden_hoe")]
    [InlineData("iron_hoe")]
    [InlineData("stone_hammer")]
    [InlineData("wooden_hammer")]
    [InlineData("wooden_sickle")]
    [InlineData("iron_sickle")]
    [InlineData("wooden_pickaxe")]
    [InlineData("iron_knife")]
    public async Task EquipmentCollectionMovesTheExactKindWithoutEquippingOrRepairing(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "equipment-source", kind, actor, 2, conditionBasisPoints: 3_000, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "equipment", $"collect my {kind.Replace('_', ' ')}");
        Assert.Equal("collect_equipment", Order(world, receipt).Action);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = Order(world, receipt);
        Assert.Equal(("finished", 1, "collection_loads", kind),
            (order.Status, order.CompletedUnits, order.ProgressUnit, order.TargetEquipmentKind));
        Assert.Null(order.TargetMaterialKind);
        Assert.Null(order.TargetFoodKind);
        var lot = world.Society.Inventory.GetLot("equipment-source");
        Assert.Equal((actor, 2, 3_000), (lot.OwnerId, lot.Quantity, lot.ConditionBasisPoints));
        Assert.True(PersonalEquipmentRules.IsCarried(lot, actor));
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment);
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(kind, projected.TargetEquipmentKind);
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(order, Order(restored, receipt));
        restored.Validate();
    }

    [Theory]
    [InlineData("household-owned")]
    [InlineData("another-person")]
    [InlineData("reserved")]
    [InlineData("promised")]
    [InlineData("carried")]
    [InlineData("other-carrier")]
    [InlineData("foreign-house")]
    [InlineData("wrong-kind")]
    public async Task EquipmentCollectionRespectsOwnershipAccessAndExactKind(string boundary)
    {
        var state = Prepared();
        var actor = Actor(state);
        var other = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "equipment-boundary",
            boundary == "wrong-kind" ? "stone_axe" : "stone_pickaxe", boundary == "household-owned" ? Household :
            boundary == "another-person" ? other : actor, 2,
            storageBuildingId: boundary is "carried" or "other-carrier" or "promised" ? null :
                boundary == "foreign-house" ? "first-town-house-b" : House);
        if (boundary is "promised" or "other-carrier") inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "equipment-boundary" ? lot with
            {
                CarrierId = boundary == "other-carrier" ? other : lot.CarrierId,
                DeliveryBuildingId = boundary == "promised" ? House : null,
            } : lot).ToArray(),
        };
        if (boundary == "reserved") inventory = InventoryFixture.Reserve(inventory,
            "equipment-reserved", actor, "equipment-boundary", 2, "other_work", 120);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "equipment-blocked", "collect stone pickaxes");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Contains("No matching personal equipment", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(inventory.GetLot("equipment-boundary") with { LastProcessedTick = world.WorldTick },
            world.Society.Inventory.GetLot("equipment-boundary"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal("blocked", Order(restored, receipt).Status);
    }

    [Fact]
    public async Task EquipmentCollectionKeepsItsSourceAndExactQuantityAcrossTravelQueueAndReplay()
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        var position = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var source = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "equipment-nearer", "stone_pickaxe", actor, 3,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "equipment-source-a", "stone_pickaxe", actor, 2, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "equipment-source-b", "stone_pickaxe", actor, 2, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "equipment-other-kind", "iron_pickaxe", actor, 1, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var first = Submit(world, actor, "equipment-source", $"collect my three stone pickaxes from ({source.X}, {source.Y})");
        var second = Submit(world, actor, "equipment-remaining", $"collect stone pickaxe at tile {source.X},{source.Y}", queue: true);
        world.Pause();
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        world.Resume();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, first).CompletedUnits);
        Assert.Equal("queued", Order(world, second).Status);
        Assert.NotEqual(position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        var sawPartial = false;
        for (var tick = 0; tick < 20 && Order(world, second).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            if (Order(world, first).Status == "finished" && Order(world, second).Status != "finished")
            {
                sawPartial = true;
                Assert.Equal(1, world.Society.Inventory.GetLot("equipment-source-b").Quantity);
                Assert.Equal(House, world.Society.Inventory.GetLot("equipment-source-b").StorageBuildingId);
            }
        }
        Assert.True(sawPartial);
        Assert.Equal(("finished", 3, "equipment_items", source),
            (Order(restored, first).Status, Order(restored, first).CompletedUnits, Order(restored, first).ProgressUnit, Order(restored, first).TargetPosition));
        Assert.Equal("finished", Order(restored, second).Status);
        Assert.Equal(3, restored.Society.Inventory.GetLot("equipment-nearer").Quantity);
        Assert.Equal(new InventoryGroundPosition(position.X, position.Y), restored.Society.Inventory.GetLot("equipment-nearer").GroundPosition);
        Assert.Equal(House, restored.Society.Inventory.GetLot("equipment-other-kind").StorageBuildingId);
        Assert.Equal(4, restored.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
            PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        Assert.Equal(3, restored.ExportState().Events.Count(item => item.Kind == "personal_goods_collected"));
        restored.Validate();
    }

    [Theory]
    [InlineData("collect equipment")]
    [InlineData("collect tools")]
    [InlineData("collect axe")]
    [InlineData("collect stone sword")]
    [InlineData("collect stone hoe")]
    [InlineData("collect basket and sack")]
    [InlineData("collect basket from the Warehouse")]
    [InlineData("collect 0 baskets")]
    [InlineData("collect 1.5 sacks")]
    public void EquipmentCollectionRejectsUnsupportedSubjectsWithoutReplacingTheCurrentOrder(string text)
    {
        var state = Prepared();
        using var world = Restore(state);
        var current = Submit(world, Actor(state), "equipment-current", "keep collecting baskets until cancelled");
        var rejected = Submit(world, Actor(state), "equipment-unsupported", text);
        Assert.Equal("waiting", Order(world, current).Status);
        Assert.Equal("not_understood", Order(world, rejected).Status);
        world.Validate();
    }

    [Fact]
    public void EquipmentCollectionRefusesInvalidSavedTargetsAndProgress()
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "equipment-validation", "collect two padded coats");
        var saved = world.ExportState();
        var valid = Order(world, receipt);
        Assert.Equal("collect_equipment", valid.Action);
        foreach (var invalid in new[]
        {
            valid with { TargetEquipmentKind = null }, valid with { TargetEquipmentKind = "stone_hoe" },
            valid with { TargetFoodKind = "berries" }, valid with { TargetMaterialKind = "stone" },
            valid with { TargetCropKind = "greens" }, valid with { TargetResourceId = "stone" },
            valid with { TargetPosition = new(10_000_001, 1) }, valid with { RequestedUnits = 0 },
            valid with { CompletedUnits = -1 }, valid with { ProgressUnit = "repairs" },
            valid with { ProgressUnit = "material_items" }, valid with { LastEffectId = "collect:personal:unearned" },
            valid with { QuantityIsExplicit = false, ProgressUnit = "collection_loads" },
            valid with { CompletedUnits = 1, LastEffectId = "repair:tool:wrong-action" },
        })
        {
            var corrupt = saved with
            {
                Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId ? item with { Order = invalid } : item).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => Restore(corrupt));
        }
    }
}
