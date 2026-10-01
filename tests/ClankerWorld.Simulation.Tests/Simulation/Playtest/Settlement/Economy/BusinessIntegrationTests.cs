using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    [Fact]
    public async Task TailorSellsLeatherAndABasketWearerBuysTheLargerActualSatchel()
    {
        var state = WithBusinessBuilding(Initial("business-leather"), "leather-tailor", TailorContent.TailorShop1x1());
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        state = Stock(Stock(Stock(Stock(state, "leather-stock", "leather", Alpha, 2, "leather-tailor"),
            "satchel-stock", "leather_satchel", Alpha, 1, "leather-tailor"), "buyer-basket", "basket", buyer, 1),
            "buyer-wood", "wood", buyer, 1);
        state = At(state, seller, Building(state, "leather-tailor").Position);
        using var world = PrivateWorldRuntime.Restore(state, actor => new Preferred(actor == seller ? ["business_serve:"] :
            actor == buyer ? ["business_collect:", "business_buy:"] : []));
        Assert.True(world.EquipItem(buyer, "buyer-basket").Applied);
        Assert.Equal(48, CarryEquipmentRules.Capacity(world.Society.Inventory, world.Inhabitants.Single(person => person.InhabitantId == buyer)));
        Assert.True(world.ListBusinessGoods(seller, "leather-tailor", "leather-stock", 1, "wood", 1).Applied);
        var satchel = world.ListBusinessGoods(seller, "leather-tailor", "satchel-stock", 1, "wood", 1);
        Assert.True(satchel.Applied, satchel.Failure);
        for (var tick = 0; tick < 160 && !world.BusinessTrade.Offers.Any(offer => offer.State == BusinessOfferState.Settled); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var purchase = Assert.Single(world.BusinessTrade.Offers, offer => offer.State == BusinessOfferState.Settled);
        Assert.Equal(satchel.Id, purchase.ListingId);
        Assert.Equal("leather_satchel", purchase.GoodsKind);
        Assert.Equal(buyer, world.Society.Inventory.GetLot("satchel-stock").OwnerId);
        Assert.Equal(2, world.Society.Inventory.GetLot("leather-stock").Quantity);
        Assert.True(world.EquipItem(buyer, "satchel-stock").Applied);
        Assert.Equal(64, CarryEquipmentRules.Capacity(world.Society.Inventory, world.Inhabitants.Single(person => person.InhabitantId == buyer)));
        world.Validate();
    }

    [Fact]
    public async Task BookCustomerChoosesTheListedDiscoveryAndLearnsThatExactTransferredArtifact()
    {
        var state = WithBusinessBuilding(Initial("business-books"), "book-store", BusinessContent.Store1x1());
        var seller = Actor(state, Alpha);
        var buyer = Actor(state, Beta);
        var sites = state.Map.Tiles.Where(tile => state.Map.IsPassable(tile.Position)).Select(tile => tile.Position).Take(2).ToArray();
        var facts = sites.Select((point, index) => new AgentKnowledgeFact("author-site-" + index, seller, seller, point,
            state.Map.TerrainKindAt(point)!.Value.ToString(), [], 0, "firsthand")).ToArray();
        var known = facts[0] with { Id = "buyer-known-site", OwnerId = buyer, DiscovererId = buyer };
        state = state with { Knowledge = new([.. facts, known], []) };
        state = Stock(Stock(Stock(state, "writing-paper", "paper", Alpha, 4, "first-town-house-a"),
            "writing-cloth", "cloth", Alpha, 2, "first-town-house-a"), "buyer-payment", "wood", buyer, 1);
        using var writing = PrivateWorldRuntime.Restore(At(state, seller, Building(state, "first-town-house-a").Position), _ => new Preferred([]));
        var familiar = writing.WriteKnowledgeArtifact(seller, "book", [sites[0]]);
        var novel = writing.WriteKnowledgeArtifact(seller, "book", [sites[1]]);
        Assert.True(familiar.Applied, familiar.Failure);
        Assert.True(novel.Applied, novel.Failure);
        state = writing.ExportState();
        var inventory = state.Society.Society.Inventory;
        var familiarLot = state.Knowledge!.Artifacts.Single(item => item.Id == familiar.ArtifactId).LotId;
        var novelLot = state.Knowledge.Artifacts.Single(item => item.Id == novel.ArtifactId).LotId;
        inventory = InventoryFixture.Transfer(inventory, "stock-known-book", seller, Alpha, familiarLot, 1, "store_delivery", "book-store");
        inventory = InventoryFixture.Transfer(inventory, "stock-new-book", seller, Alpha, novelLot, 1, "store_delivery", "book-store");
        state = At(WithInventory(state, inventory), seller, Building(state, "book-store").Position);
        using var world = PrivateWorldRuntime.Restore(state, actor => new Preferred(actor == seller ? ["business_serve:"] :
            actor == buyer ? ["business_collect:", "business_buy:"] : []));
        Assert.True(world.ListBusinessGoods(seller, "book-store", familiarLot, 1, "wood", 1).Applied);
        var novelListing = world.ListBusinessGoods(seller, "book-store", novelLot, 1, "wood", 1);
        Assert.True(novelListing.Applied, novelListing.Failure);
        for (var tick = 0; tick < 160 && !world.BusinessTrade.Offers.Any(offer => offer.State == BusinessOfferState.Settled); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(novelListing.Id, Assert.Single(world.BusinessTrade.Offers, offer => offer.State == BusinessOfferState.Settled).ListingId);
        Assert.Equal(Alpha, world.Society.Inventory.GetLot(familiarLot).OwnerId);
        Assert.Equal(buyer, world.Society.Inventory.GetLot(novelLot).OwnerId);
        var learned = Assert.Single(world.ExportState().Knowledge!.Facts, fact => fact.OwnerId == buyer && fact.Position == sites[1]);
        Assert.Equal(seller, learned.DiscovererId);
        Assert.Equal(novel.ArtifactId, learned.SourceArtifactId);
        Assert.Equal("read", learned.Acquisition);
        world.Validate();
    }

    private static PrivateWorldRuntimeState WithBusinessBuilding(PrivateWorldRuntimeState state, string id, BuildingDefinition definition)
    {
        foreach (var input in definition.BuildCosts)
            state = Stock(state, "business-build-" + input.ResourceId, input.ResourceId, Alpha, input.Amount);
        using var setup = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        foreach (var tile in state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) &&
            state.Map.IsReachableFromCampOnFoot(tile.Position)).OrderBy(tile => state.Map.FootDistance(Building(state, Farmhouse).Position, tile.Position)))
            if (setup.PlaceBuilding(id, definition.CanonicalId, tile.Position, Alpha).Applied) return setup.ExportState();
        throw new InvalidOperationException("No legal business building site was found.");
    }
}
