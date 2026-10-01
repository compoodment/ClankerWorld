using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    [Theory]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    public async Task AutomaticStoreStockingLeavesTheHousesActualTwoBatchCraftInputs(int stocked, int spare)
    {
        var state = WithBusinessBuilding(Initial("business-production-reserve"), "reserve-store", BusinessContent.Store1x1());
        var seller = Actor(state, Alpha);
        state = Stock(state, "a-house-fiber", "fiber", Alpha, stocked, "first-town-house-a");
        state = At(state, seller, Building(state, "first-town-house-a").Position);
        var provider = new Preferred(["business_stock:reserve-store", "haul_household_stock"]);
        using var world = PrivateWorldRuntime.Restore(state, actor => actor == seller ? provider : new Preferred([]));
        for (var tick = 0; tick < 120 && !world.Society.Inventory.Lots.Any(lot =>
            lot.ProvenanceLotId == "a-house-fiber" && lot.StorageBuildingId == "reserve-store"); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (spare == 0 && tick == 2) break;
        }
        Assert.Equal(6, world.Society.Inventory.GetLot("a-house-fiber").Quantity);
        Assert.Equal("first-town-house-a", world.Society.Inventory.GetLot("a-house-fiber").StorageBuildingId);
        var moved = world.Society.Inventory.Lots.Where(lot => lot.ProvenanceLotId == "a-house-fiber").ToArray();
        Assert.Equal(spare, moved.Sum(lot => lot.Quantity));
        Assert.All(moved, lot => { Assert.Equal(Alpha, lot.OwnerId); Assert.Equal("reserve-store", lot.StorageBuildingId); });
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new Preferred([]));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task ActualRestaurantGrainReceiptStaysForCookingButItsOwnerCanExplicitlyWithdrawIt()
    {
        var state = Initial("business-receipt-cooking");
        state = WithBusinessBuilding(state, "receipt-restaurant", state.WorldContent!.Buildings.Single(definition =>
            definition.Tags.Contains("restaurant", StringComparer.Ordinal)));
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, Alpha);
        state = Stock(Stock(state, "sold-bread", "bread", Alpha, 1, "receipt-restaurant"), "real-payment", "grain", buyer, 1);
        var site = Building(state, "receipt-restaurant");
        state = At(At(state, seller, site.Position), buyer, state.Map.FootNeighbors(site.Position).First());
        var provider = new Preferred(["business_receipts:receipt-restaurant"]);
        using var world = PrivateWorldRuntime.Restore(state, actor => actor == seller ? provider : new Preferred([]));
        var listing = world.ListBusinessGoods(seller, site.InstanceId, "sold-bread", 1, "grain", 1);
        Assert.True(listing.Applied, listing.Failure);
        var accepted = world.AcceptBusinessListing(buyer, listing.Id!, "real-payment");
        Assert.True(accepted.Applied, accepted.Failure);
        Assert.True(world.SettleBusinessOffer(buyer, accepted.Id!).Applied);
        Assert.Equal(BusinessOfferState.Settled, Assert.Single(world.BusinessTrade.Offers).State);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("business_receipts:receipt-restaurant", provider.Offered);
        var grain = world.Society.Inventory.GetLot("real-payment");
        Assert.Equal(Alpha, grain.OwnerId);
        Assert.Equal(site.InstanceId, grain.StorageBuildingId);
        Assert.Equal(1, grain.Quantity);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new Preferred([]));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.True(restored.WithdrawBusinessStock(seller, site.InstanceId, grain.Id, 1).Applied);
        Assert.Equal(seller, restored.Society.Inventory.GetLot(grain.Id).OwnerId);
        Assert.Null(restored.Society.Inventory.GetLot(grain.Id).StorageBuildingId);
        restored.Validate();
    }

    [Fact]
    public async Task ARealWorkstationPickupInvalidatesOnlyTheListingWhoseAdvertisedQuantityNoLongerExists()
    {
        var state = FarmFieldTests.FeedHouseholdFromAvailableStock(Initial("business-listing-pickup"), Alpha);
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        state = Stock(Stock(Stock(state, "a-listed-grain", "grain", Alpha, 3, Farmhouse),
            "unaffected-fruit", "fruit", Alpha, 1, Farmhouse), "held-payment", "wood", buyer, 1);
        state = At(state, seller, Building(state, Farmhouse).Position);
        state = At(state, buyer, state.Map.FootNeighbors(Building(state, Farmhouse).Position).First());
        var provider = new Preferred(["supply_workstation:grain"]);
        using var world = PrivateWorldRuntime.Restore(state, actor => actor == seller ? provider : new Preferred([]));
        var listing = world.ListBusinessGoods(seller, Farmhouse, "a-listed-grain", 3, "wood", 1);
        Assert.True(listing.Applied, listing.Failure);
        var heldListing = world.ListBusinessGoods(seller, Farmhouse, "unaffected-fruit", 1, "wood", 1);
        Assert.True(heldListing.Applied, heldListing.Failure);
        var accepted = world.AcceptBusinessListing(buyer, heldListing.Id!, "held-payment");
        Assert.True(accepted.Applied, accepted.Failure);
        Assert.Contains(world.BusinessTrade.Listings, item => item.Id == heldListing.Id);
        var acceptedBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using (var acceptedWorld = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(acceptedBytes), _ => new Preferred([])))
            Assert.Equal(acceptedBytes, PrivateWorldRuntimeCodec.Encode(acceptedWorld.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, world.Society.Inventory.GetLot("a-listed-grain").Quantity);
        var picked = Assert.Single(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "a-listed-grain");
        Assert.Equal(2, picked.Quantity);
        Assert.Equal(seller, picked.OwnerId);
        Assert.Equal("first-town-house-a", picked.DeliveryBuildingId);
        Assert.DoesNotContain(world.BusinessTrade.Listings, item => item.Id == listing.Id);
        Assert.Contains(world.BusinessTrade.Listings, item => item.Id == heldListing.Id);
        Assert.Equal(BusinessOfferState.Open, Assert.Single(world.BusinessTrade.Offers).State);
        foreach (var suffix in new[] { ":goods", ":payment" })
            Assert.Contains(world.Society.Inventory.Reservations, reservation => reservation.Id == accepted.Id + suffix &&
                reservation.Quantity == 1 && reservation.State == InventoryReservationState.Reserved);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new Preferred([]));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
