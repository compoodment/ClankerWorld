using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketReservationBoundaryTests
{
    private const string Seller = "founder:00000000000000000000000000000001";
    private const string Buyer = "founder:00000000000000000000000000000003";
    private static readonly Lazy<Task<byte[]>> Pending = new(BuildOfferAsync);

    [Theory]
    [InlineData("lot")]
    [InlineData("quantity")]
    [InlineData("purpose")]
    [InlineData("expiry")]
    [InlineData("exclusive")]
    public async Task OpenMarketOfferRefusesARetainedReservationThatDoesNotBindItsExactExchange(string changed)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Pending.Value);
        using (var valid = PrivateWorldRuntime.Restore(state)) valid.Validate();
        var offer = Assert.Single(state.Society.Society.Inventory.Offers, item => item.State == DirectBarterState.Open);
        var inventory = state.Society.Society.Inventory;
        var original = inventory.GetReservation(offer.Id + ":first");
        var altered = changed switch
        {
            "lot" => original with { LotId = "reservation-other-stock" },
            "quantity" => original with { Quantity = original.Quantity + 1 },
            "purpose" => original with { Purpose = "another-inventory-operation" },
            "expiry" => original with { ExpiryTick = original.ExpiryTick + 1 },
            _ => original with { IsExclusive = false },
        };
        inventory = inventory with
        {
            Reservations = inventory.Reservations.Select(claim => claim.Id == original.Id ? altered : claim).ToArray(),
        };
        _ = InventoryCheckpointCodec.Decode(InventoryCheckpointCodec.Encode(inventory));
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(PaidMarketWorld.WithInventory(state, inventory)));
        Assert.Contains("Market", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrReleasedClaimsRemainLoadableAndCancelBeforeTradeCanContinue(bool retained)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Pending.Value);
        var offer = Assert.Single(state.Society.Society.Inventory.Offers, item => item.State == DirectBarterState.Open);
        var inventory = state.Society.Society.Inventory;
        var id = offer.Id + ":first";
        inventory = inventory with
        {
            Reservations = retained ? inventory.Reservations.Select(claim => claim.Id == id
                ? claim with { State = InventoryReservationState.Released } : claim).ToArray()
                : inventory.Reservations.Where(claim => claim.Id != id).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PaidMarketWorld.WithInventory(state, inventory), new MarketRulesPolicy().CreateProvider);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(DirectBarterState.Cancelled, world.Society.Inventory.GetOffer(offer.Id).State);
        Assert.NotNull(Assert.Single(PaidMarketWorld.Market(world).Trades).CancellationReason);
        world.Validate();
    }

    private static async Task<byte[]> BuildOfferAsync()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "reservation-stock", "wooden_axe", Seller, 4);
        inventory = InventoryFixture.AddLot(inventory, "reservation-best-axe", "iron_axe", Seller, 1);
        inventory = InventoryFixture.AddLot(inventory, "reservation-other-stock", "cloth", Seller, 2);
        inventory = InventoryFixture.AddLot(inventory, "reservation-payment", "wood", Buyer, 4);
        state = PaidMarketWorld.At(PaidMarketWorld.Borrowing(PaidMarketWorld.WithInventory(state, inventory), Seller, 0), Buyer, stall);
        var policy = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == Seller
                ? candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) &&
                    candidate.Description.Contains(" wooden_axe ", StringComparison.Ordinal))
                : actor == Buyer ? candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("market_buy:", StringComparison.Ordinal) &&
                    candidate.Description.Contains("offer 1 wood for 1 wooden_axe", StringComparison.Ordinal)) : null,
        };
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        for (var tick = 0; tick < 120 && PaidMarketWorld.Market(world).Trades.Count == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Single(PaidMarketWorld.Market(world).Trades);
        Assert.Single(PaidMarketWorld.Market(world).StockReceipts);
        world.Validate();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }
}
