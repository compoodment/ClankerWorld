using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class BuildingVariantRestaurantTests
{
    [Fact]
    public async Task BuiltRestaurantVariantSellsItsActualPorridgeAfterBothSidesAcceptAcrossReload()
    {
        var fixture = await BuildingVariantTestWorld.BuildAsync("restaurant");
        var state = fixture.State;
        var restaurant = fixture.Building;
        var buyer = state.Society.Society.Inhabitants.First(person =>
            person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder &&
            person.HouseholdId != fixture.Household).Id;
        var buyerHousehold = state.Society.Society.GetInhabitant(buyer).HouseholdId;
        var recipe = state.WorldContent!.Recipes.Single(item =>
            item.WorkstationBuildingId == restaurant.DefinitionId &&
            item.Outputs.Any(output => output.ResourceId == "porridge"));
        const string jugId = "variant-restaurant-jug";
        const string paymentId = "variant-restaurant-payment";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            jugId, InventoryContainerRules.WaterJug, fixture.Household, 1,
            storageBuildingId: restaurant.InstanceId);
        foreach (var input in recipe.Inputs)
            inventory = InventoryFixture.AddLot(inventory, "variant-restaurant-input-" + input.ResourceId,
                input.ResourceId, fixture.Household, input.Amount, storageBuildingId: restaurant.InstanceId,
                containerLotId: input.ResourceId == InventoryContainerRules.FreshWater ? jugId : null);
        inventory = InventoryFixture.AddLot(inventory, paymentId, "wood", buyer, 1);

        var occupied = state.Inhabitants.Where(person => person.InhabitantId != buyer &&
                person.InhabitantId != fixture.Actor).Select(person => person.Position).ToHashSet();
        var footprints = state.WorldSimulation!.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(definition =>
                definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
        var buyerPosition = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.IsPassable(point) && state.Map.FootDistance(point, restaurant.Position) == 1 &&
            !occupied.Contains(point) && !footprints.Contains(point));
        state = BuildingVariantTestWorld.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == fixture.Actor ? restaurant.Position :
                    person.InhabitantId == buyer ? buyerPosition : person.Position,
                HungerBasisPoints = person.InhabitantId == buyer ? 6_000 : 10_000,
                LastDecisionContext = null,
                Project = null,
                TravelCooldownTicks = 0,
            }).ToArray(),
        };

        using var cooking = PrivateWorldRuntime.Restore(BuildingVariantTestWorld.Strict(state),
            _ => new VariantActionProvider());
        var started = cooking.StartProduction(recipe.CanonicalId, restaurant.InstanceId, fixture.Actor);
        Assert.True(started.Applied, started.Failure);
        var job = Assert.Single(cooking.WorldSimulation.ProductionJobs, item => item.JobId == started.JobId);
        Assert.All(job.InputReservationIds, id =>
            Assert.Equal(InventoryReservationState.Reserved, cooking.Society.Inventory.GetReservation(id).State));
        await BuildingVariantTestWorld.UntilAsync(cooking, () => cooking.WorldSimulation.ProductionJobs
            .Single(item => item.JobId == job.JobId).State == WorldProductionJobState.Completed);
        var outputId = job.JobId + ":output:00";
        var output = cooking.Society.Inventory.GetLot(outputId);
        Assert.Equal(("porridge", 2, fixture.Household, restaurant.InstanceId),
            (output.ItemKind, output.Quantity, output.OwnerId, output.StorageBuildingId));
        Assert.All(job.InputReservationIds, id =>
            Assert.Equal(InventoryReservationState.Completed, cooking.Society.Inventory.GetReservation(id).State));

        state = BuildingVariantTestWorld.Strict(cooking.ExportState()) with
        {
            Inhabitants = cooking.ExportState().Inhabitants.Select(person =>
                person with { LastDecisionContext = null }).ToArray(),
        };
        var buyerProvider = new VariantActionProvider(id => id == "business_shop:" + restaurant.InstanceId);
        using var offering = PrivateWorldRuntime.Restore(state, id =>
            id == buyer ? buyerProvider : new VariantActionProvider());
        await BuildingVariantTestWorld.UntilAsync(offering, () => offering.BusinessTrades.Count > 0);
        var trade = Assert.Single(offering.BusinessTrades);
        var offer = offering.Society.Inventory.GetOffer(trade.OfferId);
        Assert.Contains("business_shop:" + restaurant.InstanceId, buyerProvider.Chosen);
        Assert.Equal((restaurant.InstanceId, fixture.Household, buyer),
            (trade.BuildingInstanceId, trade.SellerHouseholdId, trade.BuyerId));
        Assert.Equal((DirectBarterState.Open, outputId, 2, paymentId, 1),
            (offer.State, offer.FirstLotId, offer.FirstQuantity, offer.SecondLotId, offer.SecondQuantity));
        Assert.Equal(new[] { buyer }, offer.AcceptedBy);
        Assert.Equal(fixture.Household, offering.Society.Inventory.GetLot(outputId).OwnerId);
        Assert.Equal(buyer, offering.Society.Inventory.GetLot(paymentId).OwnerId);
        foreach (var reservationId in new[] { offer.Id + ":first", offer.Id + ":second" })
            Assert.Equal(InventoryReservationState.Reserved,
                offering.Society.Inventory.GetReservation(reservationId).State);

        var beforeSale = offering.Society.Inventory.Lots.GroupBy(lot => lot.ItemKind)
            .ToDictionary(group => group.Key, group => group.Sum(lot => lot.Quantity));
        var saved = PrivateWorldRuntimeCodec.Encode(offering.ExportState());
        state = PrivateWorldRuntimeCodec.Decode(saved);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(state));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
        };
        var sellerProvider = new VariantActionProvider(id => id == "business_continue:" + offer.Id);
        using var settling = PrivateWorldRuntime.Restore(state, id =>
            id == fixture.Actor ? sellerProvider : new VariantActionProvider());
        await BuildingVariantTestWorld.UntilAsync(settling, () =>
            settling.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Settled);
        Assert.Contains("business_continue:" + offer.Id, sellerProvider.Chosen);
        Assert.Equal(new[] { fixture.Household, buyer }.Order(StringComparer.Ordinal),
            settling.Society.Inventory.GetOffer(offer.Id).AcceptedBy);
        var purchased = settling.Society.Inventory.GetLot(outputId);
        Assert.Equal((buyer, "porridge", 2), (purchased.OwnerId, purchased.ItemKind, purchased.Quantity));
        Assert.True(PersonalEquipmentRules.IsCarried(purchased, buyer));
        Assert.Null(purchased.StorageBuildingId);
        var payment = settling.Society.Inventory.GetLot(paymentId);
        Assert.Equal((fixture.Household, restaurant.InstanceId, 1),
            (payment.OwnerId, payment.StorageBuildingId, payment.Quantity));
        Assert.Null(payment.CarrierId);
        foreach (var kind in new[] { "porridge", "wood" })
            Assert.Equal(beforeSale[kind], settling.Society.Inventory.Lots
                .Where(lot => lot.ItemKind == kind).Sum(lot => lot.Quantity));
        Assert.Equal(buyerHousehold, settling.Society.GetInhabitant(buyer).HouseholdId);
        settling.Validate();
    }
}
