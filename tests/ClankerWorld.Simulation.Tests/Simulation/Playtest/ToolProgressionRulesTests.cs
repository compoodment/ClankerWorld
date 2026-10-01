using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ToolProgressionRulesTests
{
    [Fact]
    public void PickaxeTiersGateDepositsAndImproveYieldAndDurability()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [
            new("wood-pick", "wooden_pickaxe", "actor", 1, 10_000, 10_000, 0),
            new("stone-pick", "stone_pickaxe", "actor", 1, 10_000, 10_000, 0),
        ]);
        var rareInventory = InventoryFixture.AddLot(inventory, "iron-pick", "iron_pickaxe", "actor", 1);
        var iron = new MapResource("iron", "iron_ore", new GridPoint(4, 5), false,
            NaturalObjectKind: "iron_outcrop");
        var gold = new MapResource("gold", "gold_ore", new GridPoint(4, 6), false,
            NaturalObjectKind: "gold_outcrop");
        var diamond = new MapResource("diamond", "diamond", new GridPoint(4, 7), false,
            NaturalObjectKind: "diamond_outcrop");

        var ironPlan = ToolProgressionRules.PlanGather("iron_ore", iron, inventory, "actor", 3);
        var goldPlan = ToolProgressionRules.PlanGather("gold_ore", gold, rareInventory, "actor", 3);
        var diamondPlan = ToolProgressionRules.PlanGather("diamond", diamond, rareInventory, "actor", 3);

        Assert.Equal((7, "stone-pick", 1_250),
            (ironPlan!.Quantity, ironPlan.ToolLotId, ironPlan.WearLossBasisPoints));
        Assert.Equal((8, "iron-pick", 1_000),
            (goldPlan!.Quantity, goldPlan.ToolLotId, goldPlan.WearLossBasisPoints));
        Assert.Equal((8, "iron-pick", 1_000),
            (diamondPlan!.Quantity, diamondPlan.ToolLotId, diamondPlan.WearLossBasisPoints));
    }

    [Fact]
    public void LowerTierOrReservedToolsCannotExtractHigherTierOrchardMaterials()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [
            new("wood-pick", "wooden_pickaxe", "actor", 1, 10_000, 10_000, 0),
            new("stone-pick", "stone_pickaxe", "actor", 1, 10_000, 10_000, 0),
        ]);
        inventory = InventoryFixture.Reserve(inventory, "hold-stone-pick", "actor", "stone-pick", 1,
            "production", 10);
        var iron = new MapResource("iron", "iron_ore", new GridPoint(4, 5), false,
            NaturalObjectKind: "iron_outcrop");
        var gold = new MapResource("gold", "gold_ore", new GridPoint(4, 6), false,
            NaturalObjectKind: "gold_outcrop");

        Assert.Null(ToolProgressionRules.PlanGather("iron_ore", iron, inventory, "actor", 3));
        Assert.Null(ToolProgressionRules.PlanGather("gold_ore", gold, inventory, "actor", 3));
    }

    [Fact]
    public void OnlyFellingAWoodTreeAwardsASeedAndLooseWoodNeedsNoAxe()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [new("axe", "wooden_axe", "actor", 2, 10_000, 10_000, 0)]);
        var tree = new MapResource("tree", "construction", new GridPoint(4, 5), true, "broadleaf");
        var fallenWood = new MapResource("fallen", "wood", new GridPoint(5, 5), false,
            NaturalObjectKind: "fallen_wood");

        var felling = ToolProgressionRules.PlanGather("wood", tree, inventory, "actor", 1);
        var handGathering = ToolProgressionRules.PlanGather("wood", fallenWood,
            InventoryFixture.CreateGenesis([]), "actor", 3);

        Assert.Equal((6, "axe", TreeGrowthRules.TreeSeedsPerFelledTree, true),
            (felling!.Quantity, felling.ToolLotId, felling.TreeSeedQuantity, felling.FellTree));
        Assert.Equal((1, null, 0, false),
            (handGathering!.Quantity, handGathering.ToolLotId, handGathering.TreeSeedQuantity, handGathering.FellTree));
    }

    [Fact]
    public void StoredDeliveryAndBrokenToolsNeverSatisfyAGatheringGate()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [
            new("broken", "iron_pickaxe", "actor", 1, 0, 10_000, 0),
            new("stored", "stone_pickaxe", "actor", 1, 10_000, 10_000, 0, StorageBuildingId: "smith"),
            new("delivery", "stone_pickaxe", "actor", 1, 10_000, 10_000, 0, DeliveryBuildingId: "smith"),
        ]);
        var iron = new MapResource("iron", "iron_ore", new GridPoint(4, 5), false,
            NaturalObjectKind: "iron_outcrop");

        Assert.Null(ToolProgressionRules.PlanGather("iron_ore", iron, inventory, "actor", 3));
    }

    [Fact]
    public void WorkUsesBestCarriedHammerAndHoeWithTierSpecificPaceAndWear()
    {
        var inventory = InventoryFixture.CreateGenesis(
        [
            new("wood-hammer", "wooden_hammer", "actor", 1, 10_000, 10_000, 0),
            new("stone-hammer", "stone_hammer", "actor", 1, 10_000, 10_000, 0),
            new("iron-hoe", "iron_hoe", "actor", 1, 10_000, 10_000, 0),
        ]);

        var hammer = ToolProgressionRules.PlanWork(inventory, "actor", ToolFamily.Hammer);
        var hoe = ToolProgressionRules.PlanWork(inventory, "actor", ToolFamily.Hoe);
        Assert.Equal(("stone-hammer", 3, 1_250), (hammer!.ToolLotId, hammer.WorkUnits, hammer.WearLossBasisPoints));
        Assert.Equal(("iron-hoe", 3, 1_000), (hoe!.ToolLotId, hoe.WorkUnits, hoe.WearLossBasisPoints));

        inventory = InventoryFixture.Reserve(inventory, "hammer-reserved", "actor", "stone-hammer", 1,
            "production", 20);
        Assert.Equal("wood-hammer", ToolProgressionRules.PlanWork(inventory, "actor", ToolFamily.Hammer)!.ToolLotId);
    }

    [Theory]
    [InlineData("wooden_axe", "wood")]
    [InlineData("stone_pickaxe", "wood,stone")]
    [InlineData("iron_knife", "iron")]
    [InlineData("sickle", "wood,iron")]
    public void EachTierNamesActualRepairMaterials(string itemKind, string expected)
    {
        Assert.Equal(expected, string.Join(',', ToolProgressionRules.RepairMaterials(itemKind)
            .Select(input => input.ResourceId)));
    }
}
