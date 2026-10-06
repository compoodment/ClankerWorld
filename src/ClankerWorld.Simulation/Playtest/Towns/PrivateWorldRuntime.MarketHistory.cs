using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void RetireMarketHistory()
    {
        foreach (var town in towns.ToArray())
            foreach (var market in town.Markets)
            {
                var plan = MarketHistory.Retain(market, society.Checkpoint.Inventory);
                if (ReferenceEquals(plan.Market, market)) continue;
                SetMarket(town.Id, plan.Market);
                if (plan.RetiredOfferIds.Count == 0) continue;
                var claims = plan.RetiredOfferIds.SelectMany(id => new[] { id + ":first", id + ":second" })
                    .ToHashSet(StringComparer.Ordinal);
                ApplyInventoryTransition(inventory => inventory with
                {
                    Offers = inventory.Offers.Where(offer => !plan.RetiredOfferIds.Contains(offer.Id)).ToArray(),
                    Reservations = inventory.Reservations.Where(claim => !claims.Contains(claim.Id)).ToArray(),
                });
            }
    }
}

/// <summary>Retains current trading authority and a small recent history; inventory and events keep physical outcomes.</summary>
public static class MarketHistory
{
    public const int RecentRecordLimit = 32;

    public sealed record Retention(TownMarketState Market, IReadOnlySet<string> RetiredOfferIds);

    public static Retention Retain(TownMarketState market, InventoryCheckpoint inventory)
    {
        var keepReceipts = market.StockReceipts.TakeLast(RecentRecordLimit).Select(receipt => receipt.Sequence).ToHashSet();
        foreach (var occupancy in market.Occupancies.Where(item => item.EndedTick is null))
        {
            var stall = market.Stalls.Single(item => item.BuildingId == occupancy.StallBuildingId);
            var position = MarketContent.StallSite(market.Site, stall.SlotIndex);
            foreach (var lot in inventory.Lots.Where(lot => MarketTradeRules.IsAt(lot, position)))
                // Repeated collection/deposit of the same goods must not keep every old receipt.
                foreach (var receipt in market.StockReceipts.Where(receipt => receipt.OccupancyId == occupancy.Id &&
                             receipt.ItemKind == lot.ItemKind && MarketTradeRules.IsReceiptLot(receipt, lot))
                             .GroupBy(receipt => receipt.OwnerId).Select(group => group.Last()))
                    keepReceipts.Add(receipt.Sequence);
        }
        var open = market.Trades.Where(trade => trade.SettledTick is null && trade.CancellationReason is null).ToArray();
        foreach (var trade in open) keepReceipts.Add(trade.StockReceiptSequence);

        var activeClaims = inventory.Reservations.Where(claim => claim.State is InventoryReservationState.Reserved or
                InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed)
            .Select(claim => claim.Id).ToHashSet(StringComparer.Ordinal);
        var keepTrades = market.Trades.Where(trade => trade.SettledTick is not null || trade.CancellationReason is not null)
            .OrderByDescending(trade => trade.SettledTick ?? trade.ProposedTick)
            .ThenBy(trade => trade.OfferId, StringComparer.Ordinal).Take(RecentRecordLimit)
            .Concat(open).Select(trade => trade.OfferId).ToHashSet(StringComparer.Ordinal);
        foreach (var receipt in market.StockReceipts.Where(receipt => keepReceipts.Contains(receipt.Sequence)))
            if (receipt.TradeOfferId is { } offer) keepTrades.Add(offer);
        foreach (var trade in market.Trades)
            if (activeClaims.Contains(trade.OfferId + ":first") || activeClaims.Contains(trade.OfferId + ":second"))
                keepTrades.Add(trade.OfferId);
        // A retained settlement keeps its exact payment and acceptances. Its old source receipt
        // may retire below the explicit boundary; only open offers still exercise that authority.
        foreach (var receipt in market.StockReceipts)
            if (receipt.TradeOfferId is { } offer && keepTrades.Contains(offer)) keepReceipts.Add(receipt.Sequence);

        var keepOccupancies = market.Occupancies.Where(item => item.EndedTick is null)
            .Concat(market.Occupancies.Where(item => item.EndedTick is not null)
                .OrderByDescending(item => item.EndedTick).ThenBy(item => item.Id, StringComparer.Ordinal).Take(RecentRecordLimit))
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var receipt in market.StockReceipts.Where(receipt => keepReceipts.Contains(receipt.Sequence)))
            keepOccupancies.Add(receipt.OccupancyId);
        foreach (var trade in market.Trades.Where(trade => keepTrades.Contains(trade.OfferId)))
            keepOccupancies.Add(trade.OccupancyId);

        var retiredOffers = market.Trades.Where(trade => !keepTrades.Contains(trade.OfferId))
            .Select(trade => trade.OfferId).ToHashSet(StringComparer.Ordinal);
        var removedReceipts = market.StockReceipts.Where(receipt => !keepReceipts.Contains(receipt.Sequence)).ToArray();
        if (retiredOffers.Count == 0 && removedReceipts.Length == 0 && keepOccupancies.Count == market.Occupancies.Count)
            return new(market, retiredOffers);
        return new(market with
        {
            Occupancies = market.Occupancies.Where(item => keepOccupancies.Contains(item.Id)).ToArray(),
            StockReceipts = market.StockReceipts.Where(receipt => keepReceipts.Contains(receipt.Sequence)).ToArray(),
            Trades = market.Trades.Where(trade => keepTrades.Contains(trade.OfferId)).ToArray(),
            RetiredStockReceiptThrough = removedReceipts.Length == 0 ? market.RetiredStockReceiptThrough :
                Math.Max(market.RetiredStockReceiptThrough, removedReceipts.Max(receipt => receipt.Sequence)),
        }, retiredOffers);
    }
}
