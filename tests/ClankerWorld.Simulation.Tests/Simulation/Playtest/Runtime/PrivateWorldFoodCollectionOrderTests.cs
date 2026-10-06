using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldCollectionOrderTests
{
    [Theory]
    [InlineData("food", null)]
    [InlineData("berries", null)]
    [InlineData("fruit", null)]
    [InlineData("wild_greens", null)]
    [InlineData("cultivated_greens", null)]
    [InlineData("berries", "berries")]
    [InlineData("fruit", "fruit")]
    [InlineData("wild_greens", "wild_greens")]
    [InlineData("cultivated_greens", "cultivated_greens")]
    public async Task FoodCollectionMovesPersonalFoodWithoutEatingOrHarvesting(string kind, string? targetKind)
    {
        var state = Prepared();
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "collect-food", kind, actor, 2, storageBuildingId: House));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "food", $"collect my {targetKind?.Replace('_', ' ') ?? "food"}");
        Assert.Equal("collect_food", Order(world, receipt).Action);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var order = Order(world, receipt);
        Assert.Equal(("finished", 1, "collection_loads", targetKind),
            (order.Status, order.CompletedUnits, order.ProgressUnit, order.TargetFoodKind));
        Assert.Null(order.TargetMaterialKind);
        var lot = world.Society.Inventory.GetLot("collect-food");
        Assert.Equal((actor, 2), (lot.OwnerId, lot.Quantity));
        Assert.True(PersonalEquipmentRules.IsCarried(lot, actor));
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(targetKind, projected.TargetFoodKind);
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(order, Order(restored, receipt));
        restored.Validate();
    }

    [Fact]
    public async Task FoodCollectionKeepsItsSourceAndExactQuantityAcrossTravelQueueAndReplay()
    {
        var state = Prepared(distant: true);
        var actor = Actor(state);
        var position = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var source = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "food-nearer", "fruit", actor, 3,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "food-source-a", "berries", actor, 2, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "food-source-b", "fruit", actor, 2, storageBuildingId: House);
        using var world = Restore(WithInventory(state, inventory));
        var first = Submit(world, actor, "food-source", $"collect three food items from ({source.X}, {source.Y})");
        var second = Submit(world, actor, "food-remaining", $"collect fruit at tile {source.X},{source.Y}", queue: true);
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
                Assert.Equal(1, world.Society.Inventory.GetLot("food-source-b").Quantity);
                Assert.Equal(House, world.Society.Inventory.GetLot("food-source-b").StorageBuildingId);
            }
        }
        Assert.True(sawPartial);
        Assert.Equal(("finished", 3, "food_items", source),
            (Order(restored, first).Status, Order(restored, first).CompletedUnits, Order(restored, first).ProgressUnit, Order(restored, first).TargetPosition));
        Assert.Equal("finished", Order(restored, second).Status);
        Assert.Equal(3, restored.Society.Inventory.GetLot("food-nearer").Quantity);
        Assert.Equal(new InventoryGroundPosition(position.X, position.Y), restored.Society.Inventory.GetLot("food-nearer").GroundPosition);
        Assert.Equal(4, restored.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
            PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        Assert.Equal(3, restored.ExportState().Events.Count(item => item.Kind == "personal_goods_collected"));
        restored.Validate();
    }

    [Theory]
    [InlineData("food", "grain")]
    [InlineData("food", "potatoes")]
    [InlineData("food", "wood")]
    [InlineData("berries", "fruit")]
    [InlineData("wild greens", "cultivated_greens")]
    [InlineData("cultivated greens", "wild_greens")]
    public async Task FoodCollectionDoesNotSubstituteAnotherKind(string subject, string availableKind)
    {
        var state = Prepared();
        var actor = Actor(state);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "food-wrong-kind", availableKind, actor, 2, storageBuildingId: House));
        using var world = Restore(state);
        var receipt = Submit(world, actor, "food-kind", $"collect {subject}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Contains("No matching personal food", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(House, world.Society.Inventory.GetLot("food-wrong-kind").StorageBuildingId);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
    }

    [Theory]
    [InlineData("collect food source")]
    [InlineData("collect food site")]
    [InlineData("collect berry patch")]
    [InlineData("collect wild cultivated greens")]
    [InlineData("collect greens")]
    [InlineData("collect fresh water")]
    [InlineData("collect bread")]
    [InlineData("collect berries and fruit")]
    [InlineData("collect food from the Warehouse")]
    [InlineData("collect 0 food")]
    [InlineData("collect 1.5 food")]
    public void FoodCollectionRejectsUnsupportedSubjectsWithoutReplacingTheCurrentOrder(string text)
    {
        var state = Prepared();
        using var world = Restore(state);
        var current = Submit(world, Actor(state), "food-current", "keep collecting food until cancelled");
        var rejected = Submit(world, Actor(state), "food-unsupported", text);
        Assert.Equal("waiting", Order(world, current).Status);
        Assert.Equal("not_understood", Order(world, rejected).Status);
        world.Validate();
    }

    [Fact]
    public void FoodCollectionRefusesInvalidSavedTargetsAndProgress()
    {
        var state = Prepared();
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "food-validation", "collect two cultivated greens");
        var saved = world.ExportState();
        var valid = Order(world, receipt);
        Assert.Equal("collect_food", valid.Action);
        foreach (var invalid in new[]
        {
            valid with { TargetFoodKind = "grain" }, valid with { TargetMaterialKind = "wood" },
            valid with { TargetEquipmentKind = "basket" }, valid with { TargetCropKind = "greens" },
            valid with { TargetPosition = new(10_000_001, 1) }, valid with { TargetResourceId = "berry-patch" },
            valid with { RequestedUnits = 0 }, valid with { CompletedUnits = -1 },
            valid with { ProgressUnit = "material_items" }, valid with { LastEffectId = "collect:personal:unearned" },
            valid with { QuantityIsExplicit = false, ProgressUnit = "collection_loads" },
            valid with { CompletedUnits = 1, LastEffectId = "food:consumed:wrong-action" },
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
