using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class MarketAnimalFoodReserveTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task AnimalCareProtectsMealsWhileFamilyReserveIsOnSale(bool leaveFirst, bool removeStock, bool canCare)
    {
        var state = await PaidMarketWorld.StateAsync();
        var seller = state.Inhabitants[0].InhabitantId;
        var household = PaidMarketWorld.HouseholdOf(state, seller);
        var members = state.Society.Society.GetHousehold(household).MemberIds;
        Assert.Equal(2, members.Count);
        var actor = members.Single(id => id != seller);
        var market = PaidMarketWorld.Market(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "reserve-yard-wood", "wood", household, 8);
        inventory = InventoryFixture.AddLot(inventory, "reserve-yard-rope", "rope", household, 2);
        using var placing = PrivateWorldRuntime.Restore(PaidMarketWorld.WithInventory(state, inventory));
        var definition = placing.WorldContent.Buildings.Single(item => item.Tags.Contains("animal-yard"));
        var yard = state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, state.Inhabitants[0].Position))
            .Select(tile => placing.PlaceBuilding("reserve-yard", definition.CanonicalId, tile.Position, household))
            .First(result => result.Applied);
        state = placing.ExportState();
        var house = PaidMarketWorld.HouseOf(state, household);
        inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot =>
                !InventoryContainerRules.IsFood(lot.ItemKind) || lot.OwnerId != household && !members.Contains(lot.OwnerId)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "retained-berries", "berries", household, 4, storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "sale-berries", "berries", household, 4);
        inventory = InventoryFixture.Relocate(inventory, "seller-berries", "sale-berries", household, 4, carrierId: seller);
        state = PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), seller, MarketContent.StallEntrance(market.Site, 0));
        var deposit = new MarketRulesPolicy
        {
            Choose = (id, candidates) => id == seller ? candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("market_borrow:", StringComparison.Ordinal)) ?? candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("market_deposit:", StringComparison.Ordinal) && candidate.Description.Contains(" berries ", StringComparison.Ordinal)) ??
                candidates.Single(candidate => candidate.Id == "safe_idle") : candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        using var stocking = PrivateWorldRuntime.Restore(state, deposit.CreateProvider);
        for (var tick = 0; tick < 20 && PaidMarketWorld.Market(stocking).StockReceipts.Count == 0; tick++)
            Assert.True((await stocking.AdvanceOneTickAsync()).Advanced);
        var receipt = Assert.Single(PaidMarketWorld.Market(stocking).StockReceipts);
        Assert.Equal(4, receipt.Quantity);
        state = stocking.ExportState();
        if (leaveFirst)
        {
            var leave = new MarketRulesPolicy
            {
                Choose = (id, candidates) => id == seller ? candidates.FirstOrDefault(candidate =>
                    candidate.Id.StartsWith("market_leave:", StringComparison.Ordinal)) ?? candidates.Single(candidate => candidate.Id == "safe_idle") :
                    candidates.Single(candidate => candidate.Id == "safe_idle"),
            };
            using var leaving = PrivateWorldRuntime.Restore(state, leave.CreateProvider);
            for (var tick = 0; tick < 20 && PaidMarketWorld.Market(leaving).Occupancies.Any(item => item.EndedTick is null); tick++)
                Assert.True((await leaving.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain(PaidMarketWorld.Market(leaving).Occupancies, item => item.EndedTick is null);
            Assert.Contains(leaving.ExportState().Events, item => item.Kind == "market_stall_left");
            state = leaving.ExportState();
        }
        inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.Id != "retained-berries" &&
                (!removeStock || !MarketTradeRules.IsReceiptLot(receipt, lot))).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "care-greens", "cultivated_greens", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "care-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "care-water", "fresh_water", actor, 2, containerLotId: "care-jug");
        var placedYard = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == yard.InstanceId);
        var cow = new AnimalState("market-reserve-cow", "Moss", "cow", "female",
            state.Society.Society.WorldTick - AnimalRules.Definition("cow").AdultDays * state.WorldSystems!.Config.TicksPerDay,
            placedYard.Position, "household:" + household, household, placedYard.InstanceId);
        state = PaidMarketWorld.At(PaidMarketWorld.WithInventory(state, inventory), actor, placedYard.Position) with
        {
            AnimalWorld = new(true, [cow], []),
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Inhabitants = state.Inhabitants.Select(person => person with
            { Position = person.InhabitantId == actor ? placedYard.Position : person.Position, HungerBasisPoints = 9_500, LastDecisionContext = null, Project = null }).ToArray(),
        };
        var care = new MarketRulesPolicy
        {
            Choose = (id, candidates) => id == actor ? candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("animal:care:", StringComparison.Ordinal)) ?? candidates.Single(candidate => candidate.Id == "safe_idle") :
                candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        var initial = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial), care.CreateProvider);
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(canCare, world.Animals.Single().CareUntilTick > world.WorldTick);
        Assert.Equal(canCare ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "animal_cared"));
        Assert.Equal(canCare ? 0 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id == "care-greens").Sum(lot => lot.Quantity));
        Assert.Equal(canCare ? 0 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id == "care-water").Sum(lot => lot.Quantity));
        Assert.Equal(removeStock ? 0 : 4, world.Society.Inventory.Lots.Where(lot => MarketTradeRules.IsReceiptLot(receipt, lot)).Sum(lot => lot.Quantity));
        Assert.Equal(leaveFirst ? 0 : 1, PaidMarketWorld.Market(world).Occupancies.Count(item => item.EndedTick is null));
        Assert.Equal(canCare, care.OfferedTo(actor).Any(candidate => candidate.Id.StartsWith("animal:care:", StringComparison.Ordinal)));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), care.CreateProvider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
    }
}
