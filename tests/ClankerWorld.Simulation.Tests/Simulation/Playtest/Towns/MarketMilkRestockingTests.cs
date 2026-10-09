using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketMilkRestockingTests
{
    private const string Store = "market-milk-stock-store";
    private const string Jug = "market-milk-stock-jug";
    private const string Milk = "market-milk-stock-milk";
    private static readonly Lazy<Task<byte[]>> Prepared = new(PrepareAsync);

    [Theory]
    [InlineData("active", false)]
    [InlineData("left", true)]
    [InlineData("house", true)]
    [InlineData("carried", true)]
    public async Task MilkRestockingProtectsActiveSaleButPermitsReleasedAndHomeStock(string boundary, bool expected)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Prepared.Value);
        var (seller, collector, household) = Actors(state);
        var market = PaidMarketWorld.Market(state);
        var source = MarketContent.StallSite(market.Site, 0);
        if (boundary == "left") state = await LeaveAsync(state, seller);
        if (boundary is "house" or "carried")
        {
            var house = PaidMarketWorld.HouseOf(state, household);
            state = PaidMarketWorld.WithInventory(state, InventoryFixture.Relocate(state.Society.Society.Inventory,
                "milk-stock-control-" + boundary, Jug, household, 1,
                storageBuildingId: boundary == "house" ? house.InstanceId : null,
                carrierId: boundary == "carried" ? collector : null));
            source = house.Position;
        }
        state = PaidMarketWorld.At(Fresh(state), collector, source);
        var choices = MilkPolicy(collector, Store);
        using var world = PrivateWorldRuntime.Restore(state, choices.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            MilkPolicy(collector, Store).CreateProvider);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 40; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var final = world.ExportState();
        Assert.Equal(expected, choices.OfferedTo(collector).Any(item => item.Id.StartsWith("animal:milk_stock:", StringComparison.Ordinal) && item.DestinationId == Store));
        var jug = final.Society.Society.Inventory.GetLot(Jug);
        var milk = final.Society.Society.Inventory.GetLot(Milk);
        Assert.Equal((household, 1, expected ? Store : null), (jug.OwnerId, jug.Quantity, jug.StorageBuildingId));
        Assert.Null(jug.CarrierId);
        Assert.Equal(expected ? null : new InventoryGroundPosition(source.X, source.Y), jug.GroundPosition);
        Assert.Equal((household, Jug, 2), (milk.OwnerId, milk.ContainerLotId, milk.Quantity));
        Assert.Equal(jug.StorageBuildingId, milk.StorageBuildingId);
        Assert.Equal(jug.GroundPosition, milk.GroundPosition);
        Assert.Equal(1, final.Society.Society.Inventory.Lots.Count(item => item.Id == Jug));
        Assert.Equal(2, final.Society.Society.Inventory.Lots.Where(item => item.ItemKind == "milk").Sum(item => item.Quantity));
        Assert.Equal(Assert.Single(market.StockReceipts), Assert.Single(PaidMarketWorld.Market(final).StockReceipts));
        Assert.Equal(boundary == "left" ? 0 : 1, PaidMarketWorld.Market(final).Occupancies.Count(item => item.EndedTick is null));
        Assert.Equal(expected, final.Events.Any(item => item.Kind == "milk_stock_delivered" && item.Detail == collector + ":" + Store));
        AssertReload(world);
    }

    [Fact]
    public async Task AnAcceptedRestockingWalkStopsWhenTheSellerBorrowsTheStallAgain()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Prepared.Value);
        var (seller, collector, _) = Actors(state);
        state = await LeaveAsync(state, seller);
        var source = MarketContent.StallSite(PaidMarketWorld.Market(state).Site, 0);
        var away = state.Map.Tiles.Select(item => item.Position).First(point => state.Map.IsBuildable(point) &&
            state.Map.FootDistance(point, source) is >= 6 and <= 8 &&
            state.Inhabitants.All(person => person.Position != point));
        state = PaidMarketWorld.At(state, collector, away);
        var borrowing = false;
        var choices = MilkPolicy(collector, Store);
        var milkChoice = choices.Choose;
        choices.Choose = (actor, candidates) => actor == seller && borrowing
            ? candidates.FirstOrDefault(item => item.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ??
                candidates.Single(item => item.Id == "safe_idle")
            : milkChoice(actor, candidates);
        using var world = PrivateWorldRuntime.Restore(state, choices.CreateProvider);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var walking = world.ExportState();
        Assert.StartsWith("animal:milk_stock:", walking.Society.Cognition.Runtimes.Single(item => item.InhabitantId == collector).CurrentIntention!.CandidateId);
        Assert.Equal(new InventoryGroundPosition(source.X, source.Y), world.Society.Inventory.GetLot(Jug).GroundPosition);
        borrowing = true;
        _ = world.SubmitInstruction(new("milk-stall-reborrow", "owner:test", seller,
            OwnerInstructionKind.Suggestive, "Borrow the Market stall again with your spare goods."));
        for (var tick = 0; tick < 12 && !PaidMarketWorld.Market(world).Occupancies.Any(item => item.EndedTick is null); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Single(PaidMarketWorld.Market(world).Occupancies, item => item.EndedTick is null && item.SellerAgentId == seller);
        for (var tick = 0; tick < 40; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(new InventoryGroundPosition(source.X, source.Y), world.Society.Inventory.GetLot(Jug).GroundPosition);
        Assert.Null(world.Society.Inventory.GetLot(Jug).CarrierId);
        Assert.Equal((Jug, 2), (world.Society.Inventory.GetLot(Milk).ContainerLotId, world.Society.Inventory.GetLot(Milk).Quantity));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "milk_stock_picked_up" && item.Detail == collector + ":" + Jug);
        Assert.Single(PaidMarketWorld.Market(world).StockReceipts);
        AssertReload(world);
    }

    private static async Task<byte[]> PrepareAsync()
    {
        var state = await PaidMarketWorld.StateAsync();
        var (seller, _, household) = Actors(state);
        var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "store-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "milk-stock-store-cost-" + cost.ResourceId, cost.ResourceId, household, cost.Amount);
        using (var builder = PrivateWorldRuntime.Restore(PaidMarketWorld.WithInventory(state, inventory)))
        {
            foreach (var point in PaidMarketWorld.Town(state).BorderTiles.OrderBy(tile => state.Map.FootDistance(PaidMarketWorld.Market(state).Site, tile)))
                if (builder.PlaceBuilding(Store, definition.CanonicalId, point, household).Applied) break;
            Assert.Contains(builder.WorldSimulation.Buildings, item => item.InstanceId == Store && item.HouseholdId == household);
            state = builder.ExportState();
        }
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Jug, "water_jug", household, 1);
        inventory = InventoryFixture.AddLot(inventory, Milk, "milk", household, 2, containerLotId: Jug);
        inventory = InventoryFixture.Relocate(inventory, "milk-stock-seller-cargo", Jug, household, 1, carrierId: seller);
        // Ordinary borrowing needs a loose spare good; milk itself travels only in its whole jug.
        inventory = InventoryFixture.AddLot(inventory, "milk-stock-borrow-stone", "stone", seller, 1);
        state = PaidMarketWorld.At(Fresh(PaidMarketWorld.WithInventory(state, inventory)), seller,
            MarketContent.StallEntrance(PaidMarketWorld.Market(state).Site, 0)) with
        { JevEnabled = false, RoutineHelper = RoutineHelperSettings.Off };
        var stallId = PaidMarketWorld.Market(state).Stalls.Single(item => item.SlotIndex == 0).BuildingId;
        var depositing = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == seller
                ? candidates.FirstOrDefault(item => item.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ??
                    candidates.FirstOrDefault(item => item.Id.StartsWith("animal:milk_stock:", StringComparison.Ordinal) && item.DestinationId == stallId) ??
                    candidates.Single(item => item.Id == "safe_idle")
                : candidates.Single(item => item.Id == "safe_idle"),
        };
        using var stocking = PrivateWorldRuntime.Restore(state, depositing.CreateProvider);
        for (var tick = 0; tick < 40 && !PaidMarketWorld.Market(stocking).StockReceipts.Any(item => item.LotId == Jug); tick++)
            Assert.True((await stocking.AdvanceOneTickAsync()).Advanced);
        var receipt = Assert.Single(PaidMarketWorld.Market(stocking).StockReceipts);
        Assert.Equal((seller, household, Jug, 1), (receipt.SellerAgentId, receipt.OwnerId, receipt.LotId, receipt.Quantity));
        Assert.Equal(new InventoryGroundPosition(MarketContent.StallSite(PaidMarketWorld.Market(stocking).Site, 0).X,
            MarketContent.StallSite(PaidMarketWorld.Market(stocking).Site, 0).Y), stocking.Society.Inventory.GetLot(Jug).GroundPosition);
        stocking.Validate();
        return PrivateWorldRuntimeCodec.Encode(stocking.ExportState());
    }

    private static async Task<PrivateWorldRuntimeState> LeaveAsync(PrivateWorldRuntimeState state, string seller)
    {
        var choices = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == seller
                ? candidates.FirstOrDefault(item => item.Id.StartsWith("market_leave:", StringComparison.Ordinal)) ?? candidates.Single(item => item.Id == "safe_idle")
                : candidates.Single(item => item.Id == "safe_idle"),
        };
        using var world = PrivateWorldRuntime.Restore(Fresh(state), choices.CreateProvider);
        for (var tick = 0; tick < 12 && PaidMarketWorld.Market(world).Occupancies.Any(item => item.EndedTick is null); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(PaidMarketWorld.Market(world).Occupancies, item => item.EndedTick is null);
        return Fresh(world.ExportState());
    }

    private static (string Seller, string Collector, string Household) Actors(PrivateWorldRuntimeState state)
    {
        var seller = state.Inhabitants[0].InhabitantId;
        var household = PaidMarketWorld.HouseholdOf(state, seller);
        return (seller, state.Society.Society.Inhabitants.First(person => person.HouseholdId == household && person.Id != seller).Id, household);
    }

    private static PrivateWorldRuntimeState Fresh(PrivateWorldRuntimeState state) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person with
        { HungerBasisPoints = 9_500, Survival = new SurvivalCondition(), LastDecisionContext = null, Project = null }).ToArray(),
    };

    private static MarketRulesPolicy MilkPolicy(string actor, string destination) => new()
    {
        Choose = (person, candidates) => person == actor
            ? candidates.FirstOrDefault(item => item.Id.StartsWith("animal:milk_stock:", StringComparison.Ordinal) && item.DestinationId == destination) ??
                candidates.Single(item => item.Id == "safe_idle")
            : candidates.Single(item => item.Id == "safe_idle"),
    };

    private static void AssertReload(PrivateWorldRuntime world)
    {
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        restored.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
