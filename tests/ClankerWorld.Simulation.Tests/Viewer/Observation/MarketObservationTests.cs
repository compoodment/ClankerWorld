using System.Text.Json;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using Client = ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

/// <summary>Used at actual construction/trading boundaries, without repeating their simulation.</summary>
internal static class MarketObservationTests
{
    internal static Client.OwnerWorldSnapshot AssertProjection(PrivateWorldRuntime world)
    {
        var state = world.ExportState();
        var before = PrivateWorldRuntimeCodec.Encode(state);
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<Client.OwnerWorldSnapshot>(JsonSerializer.Serialize(snapshot, options), options)!;
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        foreach (var town in state.Towns!)
        {
            var projectedTown = snapshot.Towns.Single(item => item.Id == town.Id);
            var displayedTown = client.Towns.Single(item => item.Id == town.Id);
            Assert.Equal(town.Markets.Count, projectedTown.Markets.Count);
            Assert.Equal(town.Markets.Count, displayedTown.Markets.Count);
            foreach (var market in town.Markets)
            {
                var projected = projectedTown.Markets.Single(item => item.Id == market.Id);
                var displayed = displayedTown.Markets.Single(item => item.Id == market.Id);
                Assert.Equal(market.ProjectId, displayed.ProjectId);
                Assert.Equal(market.HallBuildingId, displayed.HallBuildingId);
                Assert.Equal(new Client.OwnerWorldPosition(market.Site.X, market.Site.Y), displayed.Site);
                Assert.Equal(new Client.OwnerWorldPosition(market.Site.X - 2, market.Site.Y + 2), displayed.PlazaPosition);
                Assert.Equal(7, displayed.PlazaWidth);
                Assert.Equal(4, displayed.PlazaHeight);
                Assert.Equal(market.RemovedTick, displayed.RemovedTick);
                var buildings = market.Stalls.Where(stall => stall.RemovedTick is null)
                    .Select(stall => state.WorldSimulation!.Buildings.FirstOrDefault(building => building.InstanceId == stall.BuildingId))
                    .OfType<PlacedBuilding>().ToArray();
                Assert.Equal(buildings.Length, projected.Stalls.Count);
                Assert.Equal(buildings.Length, displayed.Stalls.Count);
                foreach (var building in buildings)
                {
                    var expectedStall = market.Stalls.Single(item => item.BuildingId == building.InstanceId);
                    var stall = displayed.Stalls.Single(item => item.BuildingInstanceId == building.InstanceId);
                    Assert.Equal(expectedStall.SlotIndex, stall.SlotIndex);
                    Assert.Equal(new Client.OwnerWorldPosition(building.Position.X, building.Position.Y), stall.Position);
                    var occupancy = market.RemovedTick is null ? market.Occupancies
                        .SingleOrDefault(item => item.StallBuildingId == building.InstanceId && item.EndedTick is null) : null;
                    Assert.Equal(occupancy?.SellerAgentId, stall.SellerId);
                    Assert.Equal(occupancy?.StartedTick, stall.OccupiedTick);
                    Assert.Equal(occupancy is null ? null : Name(occupancy.SellerAgentId), stall.SellerName);
                    var expectedStock = state.Society.Society.Inventory.Lots.Where(lot => lot.ContainerLotId is null &&
                        lot.StorageBuildingId is null && lot.CarrierId is null && lot.DeliveryBuildingId is null &&
                        lot.GroundPosition == new InventoryGroundPosition(building.Position.X, building.Position.Y)).ToArray();
                    Assert.Equal(expectedStock.Length, stall.Stock.Count);
                    foreach (var lot in expectedStock)
                    {
                        var stock = stall.Stock.Single(item => item.LotId == lot.Id);
                        Assert.Equal(lot.ContainerLotId, stock.ParentLotId);
                        Assert.Equal(lot.OwnerId, stock.OwnerId);
                        Assert.Equal(Name(lot.OwnerId), stock.OwnerName);
                        Assert.Equal(lot.ItemKind, stock.Kind);
                        Assert.Equal(lot.Quantity, stock.Quantity);
                        var reserved = state.Society.Society.Inventory.Reservations.Where(claim => claim.LotId == lot.Id &&
                            claim.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                                InventoryReservationState.Committed).Sum(claim => claim.Quantity);
                        Assert.Equal(lot.ConditionBasisPoints == 0 || lot.FreshnessBasisPoints == 0 ? 0 :
                            Math.Max(0, lot.Quantity - reserved), stock.AvailableQuantity);
                    }
                    Assert.Equal(Math.Min(8, market.Trades.Count(trade => trade.StallBuildingId == building.InstanceId)),
                        stall.Trades.Count);
                    foreach (var trade in stall.Trades)
                    {
                        var actual = market.Trades.Single(item => item.OfferId == trade.OfferId);
                        var offer = state.Society.Society.Inventory.GetOffer(trade.OfferId);
                        Assert.Equal(actual.SellerAgentId, trade.SellerId);
                        Assert.Equal(Name(actual.SellerAgentId), trade.SellerName);
                        Assert.Equal(actual.GoodsOwnerId, trade.GoodsOwnerId);
                        Assert.Equal(Name(actual.GoodsOwnerId), trade.GoodsOwnerName);
                        Assert.Equal(actual.PaymentOwnerId, trade.PaymentOwnerId);
                        Assert.Equal(Name(actual.PaymentOwnerId), trade.PaymentOwnerName);
                        Assert.Equal(actual.BuyerId, trade.BuyerId);
                        Assert.Equal(Name(actual.BuyerId), trade.BuyerName);
                        Assert.Equal(actual.GoodsKind, trade.GoodsKind);
                        Assert.Equal(actual.PaymentKind, trade.PaymentKind);
                        Assert.Equal(offer.FirstQuantity, trade.GoodsQuantity);
                        Assert.Equal(offer.SecondQuantity, trade.PaymentQuantity);
                        Assert.Equal(offer.State.ToString().ToLowerInvariant(), trade.Status);
                        Assert.Equal(actual.CancellationReason, trade.CancellationReason);
                        Assert.Equal(offer.AcceptedBy.Contains(offer.FirstPartyId, StringComparer.Ordinal), trade.SellerAccepted);
                        Assert.Equal(offer.AcceptedBy.Contains(offer.SecondPartyId, StringComparer.Ordinal), trade.BuyerAccepted);
                    }
                }
            }
        }
        return client;

        string Name(string id) => state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == id)?.Name ??
            state.Society.Society.Households.FirstOrDefault(household => household.Id == id)?.Name ??
            state.Towns?.FirstOrDefault(town => town.Id == id)?.Name ?? id;
    }
}
