using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldStorageOrderTests
{
    [Theory]
    [InlineData("clothing", "store basic garments")]
    [InlineData("padded_coat", "store my padded coats at home")]
    [InlineData("basket", "please store baskets in my House now")]
    [InlineData("stone_pickaxe", "store stone pickaxes")]
    [InlineData("iron_knife", "store iron knives")]
    public async Task EquipmentStorageMovesTheExactUnwornGoodsAndPreservesTheirCondition(string kind, string text)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "equipment-storage", kind, actor, 2, conditionBasisPoints: 3_000);
        inventory = InventoryFixture.AddLot(inventory, "equipment-other", "sack", actor, 1);
        using var world = Restore(WithInventory(state, inventory));
        var receipt = Submit(world, actor, "equipment", text);
        Assert.Equal("store_equipment", Order(world, receipt).Action);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = Order(world, receipt);
        Assert.Equal(("finished", 1, "storage_loads", kind),
            (order.Status, order.CompletedUnits, order.ProgressUnit, order.TargetEquipmentKind));
        Assert.Null(order.TargetMaterialKind);
        Assert.Null(order.TargetFoodKind);
        var lot = world.Society.Inventory.GetLot("equipment-storage");
        Assert.Equal((actor, House, 2, 3_000, (string?)null),
            (lot.OwnerId, lot.StorageBuildingId, lot.Quantity, lot.ConditionBasisPoints, lot.CarrierId));
        Assert.Null(world.Society.Inventory.GetLot("equipment-other").StorageBuildingId);
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(kind, projected.TargetEquipmentKind);
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_stored");
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData("padded_coat")]
    [InlineData("basket")]
    public async Task EquipmentStorageLeavesWornGoodsWithTheirWearer(string kind)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "equipment-worn", kind, actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "equipment-spare", kind, actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Equipment = kind == "basket" ? new(CarryAidLotId: "equipment-worn") : new(ClothingLotId: "equipment-worn"),
            } : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, actor, "worn", $"store two {kind.Replace('_', ' ')}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 1, "equipment_items"),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ProgressUnit));
        Assert.Equal(House, world.Society.Inventory.GetLot("equipment-spare").StorageBuildingId);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", Order(world, receipt).Status);
        Assert.Contains("not worn", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.True(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot("equipment-worn"), actor));
        Assert.True(PersonalEquipmentRules.IsSelected(world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment, "equipment-worn"));
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(1, Order(restored, receipt).CompletedUnits);
    }

    [Theory]
    [InlineData("store tools")]
    [InlineData("store stone sword")]
    [InlineData("store stone hoe")]
    [InlineData("store basket and sack")]
    [InlineData("store basket in the Warehouse")]
    [InlineData("store basket at another House")]
    public void EquipmentStorageRejectsUnsupportedTargetsWithoutReplacingTheCurrentOrder(string text)
    {
        var state = Prepared();
        using var world = Restore(state);
        var current = Submit(world, Actor(state), "current-equipment", "keep storing baskets until cancelled");
        var rejected = Submit(world, Actor(state), "unsupported-equipment", text);
        Assert.Equal("not_understood", Order(world, rejected).Status);
        Assert.Equal("waiting", Order(world, current).Status);
        world.Validate();
    }

    [Fact]
    public void EquipmentStorageRefusesInvalidSavedTargetsAndProgress()
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "equipment-validation", "store two stone pickaxes");
        var saved = world.ExportState();
        var valid = Order(world, receipt);
        Assert.Equal("store_equipment", valid.Action);
        foreach (var invalid in new[]
        {
            valid with { TargetEquipmentKind = null }, valid with { TargetEquipmentKind = "stone_hoe" },
            valid with { TargetFoodKind = "berries" }, valid with { TargetMaterialKind = "stone" },
            valid with { TargetCropKind = "greens" }, valid with { TargetResourceId = "stone" },
            valid with { TargetPosition = new(10_000_001, 1) }, valid with { RequestedUnits = 0 },
            valid with { CompletedUnits = -1 }, valid with { ProgressUnit = "collection_loads" },
            valid with { ProgressUnit = "material_items" }, valid with { LastEffectId = "store:personal:unearned" },
            valid with { QuantityIsExplicit = false, ProgressUnit = "storage_loads" },
            valid with { CompletedUnits = 1, LastEffectId = "collect:personal:wrong-action" },
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
