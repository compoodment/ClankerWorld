using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    [Fact]
    public async Task IslandCustomerCannotReserveMainlandGoodsOrRequestAnUnreachableTool()
    {
        const string buyer = "agent:00000000000000000000000000000099";
        var state = Initial("island-food-review-0");
        var seller = Actor(state, Alpha);
        var farmhouse = Building(state, Farmhouse);
        state = Stock(At(state, seller, farmhouse.Position), "mainland-food", "grain", Alpha, 3, Farmhouse);
        using var setup = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        setup.AddAgent(buyer, new GridPoint(22, 13));
        var listing = setup.ListBusinessGoods(seller, Farmhouse, "mainland-food", 1, "wood", 1);
        Assert.True(listing.Applied, listing.Failure);
        state = Stock(setup.ExportState(), "island-payment", "wood", buyer, 2);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == buyer
            ? person with { HungerBasisPoints = 7_000 } : person).ToArray()
        };
        Assert.False(state.Map.IsReachableOnFoot(new GridPoint(22, 13), farmhouse.Position));
        var chooser = new Preferred(["business_buy:", "business_payment:", "business_request_tool:"]);
        using var world = PrivateWorldRuntime.Restore(state, id => id == buyer ? chooser : new Preferred([]));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.AcceptBusinessListing(buyer, listing.Id!, "island-payment").Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(chooser.Offered, candidate => candidate.StartsWith("business_buy:", StringComparison.Ordinal) ||
            candidate.StartsWith("business_payment:", StringComparison.Ordinal) || candidate.StartsWith("business_request_tool:", StringComparison.Ordinal));
        Assert.Empty(world.BusinessTrade.Offers);
        Assert.Empty(world.BusinessTrade.ToolOrders);
        Assert.Equal(Alpha, world.Society.Inventory.GetLot("mainland-food").OwnerId);
        Assert.Equal(3, world.Society.Inventory.GetLot("mainland-food").Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "movement_blocked" &&
            item.Detail.StartsWith(buyer + ":no_route", StringComparison.Ordinal));
        world.Validate();
    }
}
