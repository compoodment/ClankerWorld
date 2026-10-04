using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldCustodyOrderParserTests
{
    private const string Actor = "founder-ilya";

    [Theory]
    [InlineData("collect ropes", "collect_goods", "rope")]
    [InlineData("store cloth", "store_goods", "cloth")]
    [InlineData("collect cloth", "collect_goods", "cloth")]
    [InlineData("collect iron", "collect_goods", "iron")]
    [InlineData("store iron", "store_goods", "iron")]
    [InlineData("collect refined iron", "collect_goods", "iron")]
    [InlineData("store refined gold", "store_goods", "gold")]
    [InlineData("collect gold", "collect_goods", "gold")]
    [InlineData("collect workshop tools", "collect_goods", "tool")]
    [InlineData("collect grain", "collect_goods", "grain")]
    [InlineData("store flour", "store_goods", "flour")]
    [InlineData("collect potatoes", "collect_goods", "potatoes")]
    [InlineData("store tree seeds", "store_goods", "tree_seed")]
    [InlineData("collect grain seeds", "collect_goods", "grain_seed")]
    [InlineData("store cultivated green seeds", "store_goods", "cultivated_green_seed")]
    [InlineData("collect orchard seeds", "collect_goods", "orchard_seed")]
    [InlineData("store medicinal herbs", "store_goods", "medicinal_herbs")]
    [InlineData("collect bandages", "collect_goods", "bandage")]
    [InlineData("store medicine", "store_goods", "medicine")]
    [InlineData("collect storage pots", "collect_goods", "storage_pot")]
    [InlineData("store water jugs", "store_goods", "water_jug")]
    [InlineData("collect gold ornaments", "collect_goods", "gold_ornament")]
    [InlineData("store diamond ornaments", "store_goods", "diamond_ornament")]
    public void ExactGoodsNamesSelectPersonalCustodyWithoutRequiringProductionContent(
        string text, string action, string kind)
    {
        using var world = new PrivateWorldRuntime("custody-parser-names");
        Assert.Empty(world.WorldContent.Recipes);
        var order = Submit(world, "goods", text);

        Assert.Equal((action, "waiting", kind, 1, 0),
            (order.Action, order.Status, order.TargetItemKind, order.RequestedUnits, order.CompletedUnits));
        Assert.Equal(action == "collect_goods" ? "collection_loads" : "storage_loads", order.ProgressUnit);
        Assert.False(order.QuantityIsExplicit);
        Assert.False(order.RepeatUntilCancelled);
        Assert.Null(order.TargetMaterialKind);
        Assert.Null(order.TargetEquipmentKind);
        Assert.Null(order.TargetFoodKind);
        Assert.Null(order.TargetCropKind);
        Assert.Null(order.TargetRecipeId);
        Assert.Null(order.TargetResourceId);
        Assert.Null(order.TargetLotId);
        Assert.Null(order.TargetStorageBuildingId);
        Assert.Null(order.TargetStorageOwnerId);
        Assert.Null(order.TargetStoragePosition);
        world.Validate();
    }

    [Theory]
    [InlineData("collect two cloth at (1, 2)", "collect_goods", "cloth", 2, "goods_items")]
    [InlineData("store three grain seeds in my House at (1, 2)", "store_goods", "grain_seed", 3, "goods_items")]
    [InlineData("return two borrowed cloth to its House at (1, 2)", "return_borrowed", "cloth", 2, "goods_items")]
    [InlineData("store wood at (1, 2)", "store_material", "wood", 1, "storage_loads")]
    [InlineData("store basket at (1, 2)", "store_equipment", "basket", 1, "storage_loads")]
    public void CoordinatesConstrainTheRequestedLocationWithoutInventingAStorageBinding(
        string text, string action, string kind, int quantity, string progress)
    {
        using var world = new PrivateWorldRuntime("custody-parser-coordinate");
        var order = Submit(world, "coordinate", text);
        Assert.Equal((action, kind, quantity, progress),
            (order.Action, order.TargetItemKind ?? order.TargetMaterialKind ?? order.TargetEquipmentKind,
                order.RequestedUnits, order.ProgressUnit));
        Assert.Equal(new GridPoint(1, 2), order.TargetPosition);
        Assert.Equal(progress == "goods_items", order.QuantityIsExplicit);
        Assert.Null(order.TargetStorageBuildingId);
        Assert.Null(order.TargetStorageOwnerId);
        Assert.Null(order.TargetStoragePosition);
        Assert.Null(order.TargetLotId);
    }

    [Theory]
    [InlineData("keep collecting cloth until cancelled", "collect_goods", 1, "collection_loads", false, true)]
    [InlineData("keep storing cloth", "store_goods", 1, "storage_loads", false, true)]
    [InlineData("return borrowed cloth", "return_borrowed", 1, "return_loads", false, false)]
    [InlineData("repeat return two borrowed cloth", "return_borrowed", 2, "goods_items", true, true)]
    public void DefaultLoadsAndRepeatingItemCountsRetainTheirMeaning(string text, string action,
        int quantity, string progress, bool explicitQuantity, bool repeat)
    {
        using var world = new PrivateWorldRuntime("custody-parser-repeat");
        var order = Submit(world, "repeat", text);
        Assert.Equal((action, "cloth", quantity, progress, explicitQuantity, repeat),
            (order.Action, order.TargetItemKind, order.RequestedUnits, order.ProgressUnit,
                order.QuantityIsExplicit, order.RepeatUntilCancelled));
    }

    [Theory]
    [InlineData("collect wood", "collect_material", "wood", null, null)]
    [InlineData("store iron ore", "store_material", "iron_ore", null, null)]
    [InlineData("collect iron knives", "collect_equipment", null, "iron_knife", null)]
    [InlineData("store baskets", "store_equipment", null, "basket", null)]
    [InlineData("collect cultivated greens", "collect_food", null, null, "cultivated_greens")]
    public void ExistingSubjectsKeepTheirSpecializedActions(string text, string action,
        string? material, string? equipment, string? food)
    {
        using var world = new PrivateWorldRuntime("custody-parser-existing");
        var order = Submit(world, "existing", text);
        Assert.Equal((action, material, equipment, food),
            (order.Action, order.TargetMaterialKind, order.TargetEquipmentKind, order.TargetFoodKind));
        Assert.Null(order.TargetItemKind);
    }

    [Theory]
    [InlineData("return two borrowed wood", "wood")]
    [InlineData("return two borrowed iron pickaxes", "iron_pickaxe")]
    [InlineData("return two borrowed crude wooden axes", "crude_wooden_axe")]
    [InlineData("return two borrowed crude wooden pickaxes", "crude_wooden_pickaxe")]
    [InlineData("return two borrowed diamond ornaments", "diamond_ornament")]
    [InlineData("return two borrowed iron ore", "iron_ore")]
    [InlineData("return two borrowed iron knives", "iron_knife")]
    public void ReturningOverlappingSubjectsUsesTheWholeBorrowedItemName(string text, string kind)
    {
        using var world = new PrivateWorldRuntime("custody-parser-return");
        var order = Submit(world, "return", text);
        Assert.Equal(("return_borrowed", kind, 2, "goods_items"),
            (order.Action, order.TargetItemKind, order.RequestedUnits, order.ProgressUnit));
        Assert.Null(order.TargetMaterialKind);
        Assert.Null(order.TargetEquipmentKind);
        Assert.Null(order.TargetLotId);
    }

    [Theory]
    [InlineData("collect tools")]
    [InlineData("store seeds")]
    [InlineData("return borrowed ornaments")]
    [InlineData("collect cloth and rope")]
    [InlineData("store cloth in the Warehouse")]
    [InlineData("return borrowed cloth to another House")]
    [InlineData("collect 0 cloth")]
    [InlineData("return 1001 borrowed cloth")]
    [InlineData("store 1.5 cloth")]
    [InlineData("collect cloth at (1, 2) and (3, 4)")]
    [InlineData("store cloth at (1, 2) then return borrowed cloth")]
    [InlineData("return borrowed water")]
    [InlineData("return borrowed berries")]
    [InlineData("gather cloth")]
    [InlineData("gather refined iron")]
    [InlineData("repair gold ornaments")]
    [InlineData("repair storage pots")]
    public void NewGoodsCannotBroadenOtherActionsOrPartiallyReplaceTheCurrentOrder(string text)
    {
        using var world = new PrivateWorldRuntime("custody-parser-rejection");
        var current = Submit(world, "current", "eat berries");
        var rejected = Submit(world, "unsupported", text);
        Assert.Equal(("unknown", "not_understood"), (rejected.Action, rejected.Status));
        Assert.Null(rejected.TargetItemKind);
        Assert.Equal(current, world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order);
        Assert.Single(world.ExportState().Events, item => item.Kind == "instruction_not_understood");
    }

    [Fact]
    public void UnresolvedCustodyTargetsRoundTripAndMalformedBindingsAreRefused()
    {
        using var world = new PrivateWorldRuntime("custody-parser-checkpoint");
        var original = Submit(world, "save", "store two cloth in my House at (1, 2)");
        Assert.Equal(("store_goods", "cloth", 2),
            (original.Action, original.TargetItemKind, original.RequestedUnits));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var saved = JsonNode.Parse(bytes)!;
        var savedOrder = Assert.Single(saved["state"]!["instructions"]!.AsArray())!["order"]!.AsObject();
        foreach (var member in new[] { "targetStorageBuildingId", "targetStorageOwnerId", "targetStoragePosition", "targetLotId" })
            Assert.False(savedOrder.ContainsKey(member));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(original, Assert.Single(restored.ExportState().Instructions!).Order);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        (string Member, JsonNode? Value)[] corruptions =
        [
            ("targetItemKind", null),
            ("targetItemKind", JsonValue.Create("unknown_goods")),
            ("targetMaterialKind", JsonValue.Create("wood")),
            ("targetEquipmentKind", JsonValue.Create("basket")),
            ("targetFoodKind", JsonValue.Create("berries")),
            ("targetCropKind", JsonValue.Create("grain")),
            ("targetRecipeId", JsonValue.Create("unrelated-recipe")),
            ("targetResourceId", JsonValue.Create("berry-patch")),
            ("targetLotId", JsonValue.Create("return-only-lot")),
            ("targetStorageBuildingId", JsonValue.Create("unbound-house")),
            ("targetStorageOwnerId", JsonValue.Create(Actor)),
            ("targetStoragePosition", JsonNode.Parse("""{"x":1,"y":2}""")),
            ("progressUnit", JsonValue.Create("material_items")),
            ("quantityIsExplicit", JsonValue.Create(false)),
            ("requestedUnits", JsonValue.Create(0)),
            ("action", JsonValue.Create("gather_material")),
            ("lastEffectId", JsonValue.Create("store:personal:unearned")),
        ];
        foreach (var (member, value) in corruptions)
        {
            var document = JsonNode.Parse(bytes)!;
            var order = Assert.Single(document["state"]!["instructions"]!.AsArray())!["order"]!;
            order[member] = value;
            Assert.Throws<InvalidDataException>(() =>
                PrivateWorldRuntimeCodec.Decode(JsonSerializer.SerializeToUtf8Bytes(document)));
        }
    }

    private static OwnerInstructionOrder Submit(PrivateWorldRuntime world, string key, string text)
    {
        var receipt = world.SubmitInstruction(new(key, "owner:test", Actor, OwnerInstructionKind.MustDo, text));
        return world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    }
}
