using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    [Theory]
    [InlineData(8, 0, 6)]
    [InlineData(8, 7, 1)]
    [InlineData(8, 8, 0)]
    [InlineData(64, 0, 0)]
    public async Task StoreReplenishesUsableFoodAcrossReloadWithoutDeletingSpoiledGoodsOrTakingPersonalReserves(
        int spoiledStock, int usableStock, int deliveredQuantity)
    {
        var (state, actor, household, _, store) = CreateStoreStockFixture();
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != household && lot.OwnerId != actor).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "old-store-berries", "berries", household, spoiledStock,
            freshnessBasisPoints: 1, storageBuildingId: store);
        if (usableStock > 0)
            inventory = InventoryFixture.AddLot(inventory, "fresh-store-berries", "berries", household, usableStock,
                storageBuildingId: store);
        inventory = InventoryFixture.AddLot(inventory, "carried-replacement-berries", "berries", actor, 8);
        var position = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == store).Position;
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? position : person.Position,
                HungerBasisPoints = 10_000,
                Project = null,
                LastDecisionContext = null,
            }).ToArray(),
        };
        var choices = new ShopProvider("business_stock_store", "haul_household_stock");
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? choices : new ShopProvider("safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? new ShopProvider("business_stock_store", "haul_household_stock") : new ShopProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 1; tick < 48; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(0, world.Society.Inventory.GetLot("old-store-berries").FreshnessBasisPoints);
        Assert.Equal(deliveredQuantity > 0, choices.Seen.Contains("business_stock_store"));
        var stock = world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == store && lot.ItemKind == "berries").ToArray();
        Assert.Equal(usableStock + deliveredQuantity, stock.Where(lot => lot.FreshnessBasisPoints > 0).Sum(lot => lot.Quantity));
        Assert.Equal(spoiledStock, world.Society.Inventory.GetLot("old-store-berries").Quantity);
        Assert.Equal(household, world.Society.Inventory.GetLot("old-store-berries").OwnerId);
        Assert.Equal(store, world.Society.Inventory.GetLot("old-store-berries").StorageBuildingId);
        Assert.Equal(8 - deliveredQuantity, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
            lot.ItemKind == "berries" && PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        Assert.Equal(spoiledStock + usableStock + 8, world.Society.Inventory.Lots.Where(lot =>
            lot.ItemKind == "berries" && (lot.OwnerId == household || lot.OwnerId == actor)).Sum(lot => lot.Quantity));
        Assert.All(stock, lot => Assert.Equal(household, lot.OwnerId));
        world.Validate();
    }
}
