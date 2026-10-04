using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketStockBoundaryTests
{
    private const string Seller = "founder:00000000000000000000000000000001";
    private const string Housemate = "founder:00000000000000000000000000000002";
    private const string Household = "household:camp-alpha";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryPersonalCollectionCannotReclaimMarketStock(bool ownerOrder)
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "boundary-personal-wood", "wood", Seller, 2, groundPosition: new(stall.X, stall.Y)));
        state = PaidMarketWorld.Borrowing(state, Seller, 0);
        var policy = new MarketRulesPolicy(DecisionProviderKind.Deterministic)
        {
            Choose = (actor, candidates) => actor == Seller
                ? candidates.FirstOrDefault(candidate => candidate.Id == "household_collect:boundary-personal-wood") : null,
        };
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        if (ownerOrder) world.SubmitInstruction(new("boundary-collect-order", "owner:test", Seller,
            OwnerInstructionKind.MustDo, "collect two wood"));
        for (var tick = 0; tick < 12; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var lot = world.Society.Inventory.GetLot("boundary-personal-wood");
        Assert.Equal((Seller, 2, (string?)null, new InventoryGroundPosition(stall.X, stall.Y)),
            (lot.OwnerId, lot.Quantity, lot.CarrierId, lot.GroundPosition));
        Assert.DoesNotContain(policy.OfferedTo(Seller), candidate => candidate.Id == "household_collect:boundary-personal-wood");
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task PersonalPotDroppedOnAStallRemainsCollectible()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "boundary-personal-pot", InventoryContainerRules.StoragePot, Seller, 1, groundPosition: new(stall.X, stall.Y)));
        state = PaidMarketWorld.At(PaidMarketWorld.Borrowing(state, Seller, 0), Seller, stall);
        var policy = new MarketRulesPolicy(DecisionProviderKind.Deterministic)
        {
            Choose = (actor, candidates) => actor == Seller
                ? candidates.FirstOrDefault(candidate => candidate.Id == "household_collect:boundary-personal-pot") : null,
        };
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(policy.OfferedTo(Seller), candidate => candidate.Id == "household_collect:boundary-personal-pot");
        var pot = world.Society.Inventory.GetLot("boundary-personal-pot");
        Assert.True(PersonalEquipmentRules.IsCarried(pot, Seller));
        Assert.Equal((Seller, 1), (pot.OwnerId, pot.Quantity));
        world.Validate();
    }

    [Fact]
    public async Task SharedFoodCollectionLeavesBorrowedStallStockForTrade()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "a-boundary-stall-food", "food", Household, 4, groundPosition: new(stall.X, stall.Y)));
        state = PaidMarketWorld.At(PaidMarketWorld.Borrowing(state, Seller, 0), Housemate, stall) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Housemate
                ? person with { Position = stall, HungerBasisPoints = 1_000 }
                : person.InhabitantId == Seller ? person with { Position = MarketContent.StallEntrance(PaidMarketWorld.Market(state).Site, 0) }
                : person).ToArray(),
        };
        var policy = new MarketRulesPolicy(DecisionProviderKind.Deterministic)
        {
            Choose = (actor, candidates) => actor == Housemate
                ? candidates.FirstOrDefault(candidate => candidate.Id == "collect_shared_food") : null,
        };
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        for (var tick = 0; tick < 12; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var lot = world.Society.Inventory.GetLot("a-boundary-stall-food");
        Assert.Equal((Household, 4, (string?)null, new InventoryGroundPosition(stall.X, stall.Y)),
            (lot.OwnerId, lot.Quantity, lot.CarrierId, lot.GroundPosition));
        world.Validate();
    }

    [Fact]
    public async Task HouseholdConstructionCannotSpendGoodsDisplayedAtABorrowedStall()
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != Household || lot.ItemKind != "wood").ToArray(),
        };
        state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(inventory, "boundary-stall-wood", "wood", Household, 8,
            groundPosition: new(stall.X, stall.Y)));
        GridPoint? validSite = null;
        using (var control = PrivateWorldRuntime.Restore(state))
        {
            foreach (var tile in state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(stall, tile.Position)))
            {
                if (!control.PlaceBuilding("boundary-house", HouseContent.House1x1().CanonicalId, tile.Position, Household).Applied) continue;
                validSite = tile.Position;
                break;
            }
            Assert.NotNull(validSite);
        }
        using var borrowed = PrivateWorldRuntime.Restore(PaidMarketWorld.Borrowing(state, Seller, 0));
        var result = borrowed.PlaceBuilding("boundary-house", HouseContent.House1x1().CanonicalId, validSite!.Value, Household);
        Assert.False(result.Applied);
        Assert.Equal(8, borrowed.Society.Inventory.GetLot("boundary-stall-wood").Quantity);
        borrowed.Validate();
    }

    [Fact]
    public async Task BlacksmithGatheringDoesNotWaitForWoodProtectedOnABorrowedStall()
    {
        var state = await PaidMarketWorld.StateAsync();
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in BlacksmithContent.Blacksmith1x2().BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "boundary-smith-cost-" + cost.ResourceId, cost.ResourceId, Household, cost.Amount);
        using (var builder = PrivateWorldRuntime.Restore(PaidMarketWorld.WithInventory(state, inventory)))
        {
            foreach (var tile in PaidMarketWorld.Town(state).BorderTiles.OrderBy(point => state.Map.FootDistance(PaidMarketWorld.Market(state).Site, point)))
            {
                if (!builder.PlaceBuilding("boundary-smith", BlacksmithContent.Blacksmith1x2().CanonicalId, tile, Household).Applied) continue;
                break;
            }
            Assert.Contains(builder.ExportState().WorldSimulation!.Buildings, building => building.InstanceId == "boundary-smith");
            builder.Validate();
            state = builder.ExportState();
        }
        inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != "wood" ||
                lot.OwnerId != Household && lot.OwnerId != Housemate).ToArray(),
        };
        foreach (var kind in new[] { "stone", "iron", "iron_ore", "gold_ore", "gold", "gold_ornament", "diamond" })
            inventory = InventoryFixture.AddLot(inventory, "boundary-smith-stock-" + kind, kind, Household, 8, storageBuildingId: "boundary-smith");
        inventory = InventoryFixture.AddLot(inventory, "boundary-smith-axe", "iron_axe", Housemate, 1);
        state = PaidMarketWorld.WithInventory(state, inventory);
        var controlPolicy = new MarketRulesPolicy();
        using (var control = PrivateWorldRuntime.Restore(state, controlPolicy.CreateProvider))
        {
            for (var tick = 0; tick < 4; tick++) Assert.True((await control.AdvanceOneTickAsync()).Advanced);
            Assert.Contains(controlPolicy.OfferedTo(Housemate), candidate => candidate.Id == "gather_smith_input:wood");
        }
        var stall = PaidMarketWorld.StallTile(state, 0);
        state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(inventory, "boundary-smith-stall-wood", "wood", Household, 8,
            groundPosition: new(stall.X, stall.Y)));
        var borrowedPolicy = new MarketRulesPolicy();
        using var borrowed = PrivateWorldRuntime.Restore(PaidMarketWorld.Borrowing(state, Seller, 0), borrowedPolicy.CreateProvider);
        for (var tick = 0; tick < 4; tick++) Assert.True((await borrowed.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(borrowedPolicy.OfferedTo(Housemate), candidate => candidate.Id == "gather_smith_input:wood");
        Assert.Equal(8, borrowed.Society.Inventory.GetLot("boundary-smith-stall-wood").Quantity);
        borrowed.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WearingAHouseholdOrnamentHonorsStallBorrowing(bool borrowed)
    {
        var state = await PaidMarketWorld.StateAsync();
        var stall = PaidMarketWorld.StallTile(state, 0);
        state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "boundary-stall-ornament", OrnamentContent.GoldOrnament, Household, 1, groundPosition: new(stall.X, stall.Y)));
        if (borrowed) state = PaidMarketWorld.Borrowing(state, Seller, 0);
        using var world = PrivateWorldRuntime.Restore(PaidMarketWorld.At(state, Housemate, stall));
        var result = world.WearOrnament(Housemate, "boundary-stall-ornament");
        Assert.Equal(!borrowed, result.Applied);
        Assert.Equal(borrowed ? Household : Housemate, world.Society.Inventory.GetLot("boundary-stall-ornament").OwnerId);
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreRefusesAHouseOnAReservedMarketSlotOrAisle(bool aisle)
    {
        var state = await PaidMarketWorld.StateAsync();
        var market = PaidMarketWorld.Market(state);
        var tile = aisle ? new GridPoint(market.Site.X + 4, market.Site.Y + 3) : PaidMarketWorld.StallTile(state, 1);
        state = PaidMarketWorld.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "boundary-house-cost", "wood", Household, 8));
        using (var control = PrivateWorldRuntime.Restore(state))
        {
            foreach (var position in PaidMarketWorld.Town(state).BorderTiles.OrderBy(point => state.Map.FootDistance(market.Site, point)))
            {
                if (!control.PlaceBuilding("boundary-plaza-house", HouseContent.House1x1().CanonicalId, position, Household).Applied) continue;
                break;
            }
            Assert.Contains(control.ExportState().WorldSimulation!.Buildings, item => item.InstanceId == "boundary-plaza-house");
            control.Validate();
            state = control.ExportState();
        }
        using (var valid = PrivateWorldRuntime.Restore(state)) valid.Validate();
        state = state with
        {
            WorldSimulation = state.WorldSimulation! with
            {
                Buildings = state.WorldSimulation.Buildings.Select(building => building.InstanceId == "boundary-plaza-house"
                    ? building with { Position = tile, Entrance = null } : building).ToArray(),
            },
        };
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state));
        Assert.Contains("standing Market plaza", error.Message, StringComparison.Ordinal);
    }
}
