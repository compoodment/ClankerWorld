using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketAnimalSupplyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AnimalSupplyLeavesHouseholdSaleStockUntilTheSellerLeaves(bool leaveFirst, bool yardStock)
    {
        var state = await PaidMarketWorld.StateAsync();
        var seller = state.Inhabitants[0].InhabitantId;
        var household = PaidMarketWorld.HouseholdOf(state, seller);
        var collector = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household && person.Id != seller).Id;
        var market = PaidMarketWorld.Market(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "market-yard-wood", "wood", household, 8);
        inventory = InventoryFixture.AddLot(inventory, "market-yard-rope", "rope", household, 2);
        using var placing = PrivateWorldRuntime.Restore(PaidMarketWorld.WithInventory(state, inventory));
        var definition = placing.WorldContent.Buildings.Single(item => item.Tags.Contains("animal-yard"));
        var yard = state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, state.Inhabitants[0].Position))
            .Select(tile => placing.PlaceBuilding("market-supply-yard", definition.CanonicalId,
                tile.Position, household)).First(result => result.Applied);
        Assert.Equal(inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood").Sum(lot => lot.Quantity) - 8,
            placing.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "rope").Sum(lot => lot.Quantity) - 2,
            placing.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "rope").Sum(lot => lot.Quantity));
        state = placing.ExportState();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "market-animal-grain", "grain", household, 4);
        inventory = InventoryFixture.Relocate(inventory, "seller-carried-grain", "market-animal-grain", household, 4, carrierId: seller);
        state = PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), seller, MarketContent.StallEntrance(market.Site, 0));
        var deposit = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == seller ? candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ?? candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) && candidate.Description.Contains(" grain ", StringComparison.Ordinal)) ??
                candidates.Single(candidate => candidate.Id == "safe_idle") : candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        using var stocking = PrivateWorldRuntime.Restore(state, deposit.CreateProvider);
        for (var tick = 0; tick < 20 && PaidMarketWorld.Market(stocking).StockReceipts.Count == 0; tick++)
            Assert.True((await stocking.AdvanceOneTickAsync()).Advanced);
        var receipt = Assert.Single(PaidMarketWorld.Market(stocking).StockReceipts);
        Assert.Equal((household, 4), (receipt.OwnerId, receipt.Quantity));
        Assert.Single(PaidMarketWorld.Market(stocking).Occupancies, occupancy => occupancy.EndedTick is null && occupancy.SellerAgentId == seller);
        state = stocking.ExportState();
        if (leaveFirst)
        {
            var leaving = new MarketRulesPolicy
            {
                Choose = (actor, candidates) => actor == seller ? candidates.FirstOrDefault(candidate =>
                    candidate.Id.StartsWith("market_leave:", StringComparison.Ordinal)) ?? candidates.Single(candidate => candidate.Id == "safe_idle")
                    : candidates.Single(candidate => candidate.Id == "safe_idle"),
            };
            using var left = PrivateWorldRuntime.Restore(Fresh(state), leaving.CreateProvider);
            for (var tick = 0; tick < 12 && PaidMarketWorld.Market(left).Occupancies.Any(item => item.EndedTick is null); tick++)
                Assert.True((await left.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain(PaidMarketWorld.Market(left).Occupancies, item => item.EndedTick is null);
            Assert.Contains(left.ExportState().Events, item => item.Kind == "market_stall_left");
            state = left.ExportState();
        }
        var placedYard = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == yard.InstanceId);
        var cow = new AnimalState("market-supply-cow", "Moss", "cow", "female",
            state.Society.Society.WorldTick - AnimalRules.Definition("cow").AdultDays * state.WorldSystems!.Config.TicksPerDay,
            placedYard.Position, "market-supply-herd", household, placedYard.InstanceId);
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "market-supply-jug", "water_jug", collector, 1);
        inventory = InventoryFixture.AddLot(inventory, "market-supply-water", "fresh_water", collector, 2, containerLotId: "market-supply-jug");
        state = Fresh(PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), collector, PaidMarketWorld.StallTile(state, 0))) with
        {
            AnimalWorld = new(true, [cow], []),
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
        };
        var supply = new MarketRulesPolicy
        {
            Choose = (actor, candidates) => actor == collector ? candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("animal:supply:", StringComparison.Ordinal) && candidate.DestinationId == (yardStock ? placedYard.InstanceId : cow.Id) &&
                candidate.Description.Contains(" grain ", StringComparison.Ordinal)) ?? candidates.Single(candidate => candidate.Id == "safe_idle")
                : candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), supply.CreateProvider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var stock = world.Society.Inventory.Lots.Where(lot => MarketTradeRules.IsReceiptLot(receipt, lot)).ToArray();
        Assert.Equal(4, stock.Sum(lot => lot.Quantity));
        Assert.All(stock, lot => Assert.Equal(household, lot.OwnerId));
        var collected = leaveFirst ? yardStock ? 4 : 2 : 0;
        Assert.Equal(4 - collected, stock.Where(lot => lot.GroundPosition is not null).Sum(lot => lot.Quantity));
        Assert.Equal(collected, stock.Where(lot => PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, lot, collector)).Sum(lot => lot.Quantity));
        Assert.Equal(leaveFirst ? 0 : 1, PaidMarketWorld.Market(world).Occupancies.Count(item => item.EndedTick is null));
        Assert.Equal(2, world.Society.Inventory.GetLot("market-supply-water").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("market-supply-jug").Quantity);
        bool GrainSupply(ClankerWorld.Simulation.Cognition.CognitionCandidate candidate) =>
            candidate.Id.StartsWith("animal:supply:", StringComparison.Ordinal) &&
            candidate.DestinationId == (yardStock ? placedYard.InstanceId : cow.Id) && candidate.Description.Contains(" grain ", StringComparison.Ordinal);
        if (leaveFirst)
        {
            Assert.Contains(supply.OfferedTo(collector), GrainSupply);
            var trip = Assert.Single(world.ExportState().AnimalWorld.SupplyTrips, item => item.ActorId == collector);
            Assert.Equal((placedYard.InstanceId, yardStock ? null : cow.Id, yardStock ? null : "care"),
                (trip.YardId, trip.AnimalId, trip.Action));
            Assert.Contains(stock, lot => lot.Id == trip.LotId && PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, lot, collector));
        }
        else
        {
            Assert.DoesNotContain(supply.OfferedTo(collector), GrainSupply);
            Assert.DoesNotContain(world.ExportState().AnimalWorld.SupplyTrips, item => item.ActorId == collector);
        }
        world.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final));
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
    }

    private static PrivateWorldRuntimeState Fresh(PrivateWorldRuntimeState state) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null, Project = null, HungerBasisPoints = 9_500 }).ToArray(),
    };
}
