using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    // Existing Store stocking protects a singleton work tool. Actual counted
    // collection can leave two tools in one lot; only one should be kept.
    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public async Task StoreStocksCollectedSpareToolsRegardlessOfLotShape(int quantity, bool split)
    {
        var (state, actor, household, house, store) = CreateStoreStockFixture();
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [], Offers = [] };
        inventory = InventoryFixture.AddLot(inventory, "collected-store-axes", "wooden_axe", actor, quantity,
            storageBuildingId: house);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Equipment = null,
                Project = null,
                LastDecisionContext = null,
                HungerBasisPoints = 9_500,
            }).ToArray(),
        };
        var policy = new MarketRulesPolicy
        {
            Choose = (id, candidates) => id == actor
                ? candidates.FirstOrDefault(candidate => candidate.Id is "collect_equipment" or "business_stock_store" or "haul_household_stock") ??
                    candidates.Single(candidate => candidate.Id == "safe_idle")
                : candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        using (var collecting = PrivateWorldRuntime.Restore(state, policy.CreateProvider))
        {
            var receipt = collecting.SubmitInstruction(new("collect-store-axes", "owner:test", actor,
                OwnerInstructionKind.MustDo, quantity == 2 ? "collect two wooden axes" : "collect one wooden axe"));
            for (var tick = 0; tick < 16 && collecting.ExportState().Instructions!.Single(item =>
                     item.InstructionId == receipt.InstructionId).Order!.Status != "finished"; tick++)
                Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
            state = collecting.ExportState();
            var order = state.Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
            Assert.Equal(("finished", quantity, "equipment_items"), (order.Status, order.CompletedUnits, order.ProgressUnit));
            var carried = state.Society.Society.Inventory.GetLot("collected-store-axes");
            Assert.Equal(quantity, carried.Quantity);
            Assert.True(PersonalEquipmentRules.IsCarried(carried, actor));
        }
        if (split)
            state = WithInventory(state, InventoryFixture.SplitLot(state.Society.Society.Inventory,
                "collected-store-axes", 1, "collected-store-axes-spare"));
        var destination = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == store).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = destination, LastDecisionContext = null } : person).ToArray(),
        };
        policy.Offered.Clear();
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 24; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var stocked = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wooden_axe" &&
            lot.OwnerId == household && lot.StorageBuildingId == store).Sum(lot => lot.Quantity);
        var retained = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wooden_axe" &&
            lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity);
        Assert.Equal((quantity - 1, 1), (stocked, retained));
        Assert.Equal(quantity, TotalQuantity(world.Society.Inventory, "wooden_axe"));
        Assert.NotNull(ToolProgressionRules.BestUsableTool(world.Society.Inventory, actor, ToolFamily.Axe));
        Assert.Equal(quantity > 1, policy.OfferedTo(actor).Any(candidate => candidate.Id == "business_stock_store"));
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
