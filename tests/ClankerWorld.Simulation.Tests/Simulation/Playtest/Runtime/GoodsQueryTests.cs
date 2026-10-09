using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class GoodsQueryTests
{
    [Theory]
    [InlineData("goods-query-a")]
    [InlineData("goods-query-b")]
    public void NativeQueriesMatchAPlainScanAndEveryConsumptionMatchIsAcceptedByInventoryAuthority(string seed)
    {
        using var world = NormalPathWorld.CreateGenerated(seed, _ => new Idle());
        var state = world.ExportState();
        var inventory = state.Society.Society.Inventory;
        var owners = new GoodsOwners(inventory.Lots.Select(lot => lot.OwnerId).Distinct(StringComparer.Ordinal).ToArray());
        var kinds = new GoodsKinds(inventory.Lots.Select(lot => lot.ItemKind).Distinct(StringComparer.Ordinal).ToArray());
        var before = PrivateWorldRuntimeCodec.Encode(state);
        foreach (var use in new[] { GoodsUse.Holdings, GoodsUse.ConsumeAt })
        {
            foreach (var building in new string?[] { null, "first-town-house-a" })
            {
                var request = new GoodsRequest(use, null, owners, kinds, AtBuilding: building, Explain: true);
                var answer = world.FindGoods(request);
                var expected = PlainScan(inventory, use, building);
                Assert.Equal(expected, answer.Matches.Select(match => (match.Lot.Id, match.Quantity)).ToArray());
                Assert.Equal(expected.Sum(match => (long)match.Quantity), answer.Total);
                Assert.Equal(answer.Matches.Count == 0 ? null : answer.Matches[0], answer.First);
                Assert.Equal(answer.Matches, world.FindGoods(request with { Explain = false }).Matches);
                var repeated = world.FindGoods(request);
                Assert.Equal(answer.Matches, repeated.Matches);
                Assert.Equal(answer.Excluded, repeated.Excluded);
                foreach (var match in answer.Matches)
                {
                    Assert.Equal(match, world.RecheckGoods(request, match.Lot.Id));
                    Assert.Equal(match.Lot.ContainerLotId ?? match.Lot.Id, match.Root.Id);
                    if (use != GoodsUse.ConsumeAt) continue;
                    var accepted = InventoryFixture.Reserve(inventory, "query-proof:" + match.Lot.Id,
                        match.Lot.OwnerId, match.Lot.Id, match.Quantity, "query-proof", inventory.WorldTick + 1);
                    Assert.Equal(match.Quantity, accepted.GetReservation("query-proof:" + match.Lot.Id).Quantity);
                }
                Assert.Null(world.RecheckGoods(request, "not-a-lot"));
            }
        }
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Validate();
    }

    [Fact]
    public void QueriesRespectPartialReservationsBrokenParentsAndDeliveryPromisesWithoutWeakeningConsumptionAuthority()
    {
        using var generated = NormalPathWorld.CreateGenerated("goods-query-boundaries", _ => new Idle());
        var state = generated.ExportState();
        const string household = "household:camp-alpha";
        const string house = "first-town-house-a";
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "query-partial", "flour", household, 3, storageBuildingId: house);
        inventory = InventoryFixture.Reserve(inventory, "query-reservation", household, "query-partial", 2, "other-work", 100);
        inventory = InventoryFixture.AddLot(inventory, "query-broken-pot", "storage_pot", household, 1, storageBuildingId: house);
        inventory = InventoryFixture.AddLot(inventory, "query-broken-flour", "flour", household, 2,
            storageBuildingId: house, containerLotId: "query-broken-pot");
        inventory = InventoryFixture.AddLot(inventory, "query-inbound", "water_jug", household, 1);
        inventory = InventoryFixture.AddLot(inventory, "query-water", "fresh_water", household, 3, containerLotId: "query-inbound");
        inventory = InventoryFixture.Transfer(inventory, "pickup", household, actor, "query-inbound", 1,
            "household_stock_picked_up", destinationDeliveryBuildingId: house);
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "query-broken-pot" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray(),
        };
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new Idle());
        var flour = new GoodsRequest(GoodsUse.ConsumeAt, null, GoodsOwners.One(household), GoodsKinds.One("flour"),
            AtBuilding: house, Explain: true);
        var answer = world.FindGoods(flour);
        Assert.Equal(1, answer.Total);
        Assert.Equal(("query-partial", 1), (answer.First!.Lot.Id, answer.First.Quantity));
        Assert.Contains(("query-broken-flour", GoodsReason.Vessel), answer.Excluded);
        Assert.Null(world.RecheckGoods(flour, "query-broken-flour"));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.Reserve(inventory,
            "reject-broken", household, "query-broken-flour", 1, "cook", 100));
        var water = new GoodsRequest(GoodsUse.Holdings, actor, GoodsOwners.One(actor), GoodsKinds.One("fresh_water"), Explain: true);
        var holdings = world.FindGoods(water);
        Assert.DoesNotContain(holdings.Matches, match => match.Lot.Id == "query-water");
        Assert.Contains(("query-water", GoodsReason.Delivery), holdings.Excluded);
        // ConsumeAt reports the exact reservation boundary. Higher-level callers
        // can add custody rules, but cannot remove this use's inventory checks.
        var consumption = world.FindGoods(water with { Use = GoodsUse.ConsumeAt });
        var inbound = Assert.Single(consumption.Matches, match => match.Lot.Id == "query-water");
        Assert.Equal(3, InventoryFixture.Reserve(inventory, "consume-inbound", actor,
            inbound.Lot.Id, inbound.Quantity, "cook", 100).GetReservation("consume-inbound").Quantity);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task HoldingsExcludeTheWholeSaleFamilyAtAnActuallyPaidAndBorrowedMarketStall()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        const string owner = "household:camp-alpha";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "query-sale-pot", "storage_pot", owner, 1,
            groundPosition: new(stall.X, stall.Y));
        inventory = InventoryFixture.AddLot(inventory, "query-sale-flour", "flour", owner, 2, containerLotId: "query-sale-pot");
        state = PaidMarketWorld.WithInventory(state, inventory);
        var request = new GoodsRequest(GoodsUse.Holdings, null, GoodsOwners.One(owner), GoodsKinds.One("flour"), Explain: true);
        using (var ordinary = PrivateWorldRuntime.Restore(state, _ => new Idle()))
            Assert.Contains(ordinary.FindGoods(request).Matches, match => match.Lot.Id == "query-sale-flour" && match.Quantity == 2);
        var seller = state.Society.Society.Inhabitants.First(person => person.HouseholdId == owner).Id;
        using var sale = PrivateWorldRuntime.Restore(PaidMarketWorld.Borrowing(state, seller, 0), _ => new Idle());
        var before = PrivateWorldRuntimeCodec.Encode(sale.ExportState());
        var answer = sale.FindGoods(request);
        Assert.DoesNotContain(answer.Matches, match => match.Lot.Id == "query-sale-flour");
        Assert.Contains(("query-sale-flour", GoodsReason.MarketStall), answer.Excluded);
        Assert.Null(sale.RecheckGoods(request, "query-sale-flour"));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(sale.ExportState()));
    }

    private static (string Id, int Quantity)[] PlainScan(InventoryCheckpoint inventory, GoodsUse use, string? building) =>
        inventory.Lots.Where(lot => building is null || lot.StorageBuildingId == building)
            .Where(lot => lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0 &&
                (lot.ContainerLotId is null || inventory.Lots.Single(parent => parent.Id == lot.ContainerLotId).ConditionBasisPoints > 0) &&
                (use != GoodsUse.ConsumeAt || !InventoryContainerRules.IsContainer(lot.ItemKind)) &&
                (use != GoodsUse.Holdings || lot.DeliveryBuildingId is null))
            .Select(lot => (lot.Id, Quantity: Math.Max(0, lot.Quantity - inventory.Reservations.Where(reservation =>
                reservation.LotId == lot.Id && reservation.State is InventoryReservationState.Reserved or
                    InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed).Sum(reservation => reservation.Quantity))))
            .Where(match => match.Quantity > 0).OrderBy(match => match.Id, StringComparer.Ordinal).ToArray();

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1, new Dictionary<string, double> { ["safe_idle"] = 1 }));
    }
}
