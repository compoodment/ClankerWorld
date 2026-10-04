using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record MarketChoice(string Id, string Kind, TownRuntimeState Town, TownMarketState Market,
        MarketStallState Stall, MarketStallOccupancy? Occupancy = null, InventoryLot? Lot = null,
        int Quantity = 0, MarketTradeState? Trade = null, InventoryLot? Payment = null);

    private const int MarketCandidateLimit = 16;

    private static bool IsMarketCandidate(string id) => id.StartsWith("market_", StringComparison.Ordinal);

    // A settled or cancelled trade is history; only the others still need their offer looked up.
    private static bool IsOpenMarketTrade(MarketTradeState trade) =>
        trade.SettledTick is null && trade.CancellationReason is null;

    /// <summary>The Hall, stall slots and aisles of every standing Market, which other building and planting leave clear.</summary>
    private IEnumerable<GridPoint> MarketSiteTiles() => towns.SelectMany(town => town.Markets)
        .Where(market => market.RemovedTick is null).SelectMany(market => MarketContent.SiteTiles(market.Site));

    /// <summary>Household goods stay on sale while one of the household's members borrows the stall they lie on.</summary>
    private bool OnBorrowedMarketStall(InventoryLot lot) => lot.GroundPosition is { } ground &&
        towns.Any(town => town.Markets.Any(market => market.Occupancies.Any(occupancy =>
            occupancy.EndedTick is null && occupancy.SellerHouseholdId == lot.OwnerId &&
            market.Stalls.Any(stall => stall.BuildingId == occupancy.StallBuildingId &&
                MarketContent.StallSite(market.Site, stall.SlotIndex) == new GridPoint(ground.X, ground.Y)))));

    private bool MarketAdult(string actor) => AdultResident(actor) &&
        society.Checkpoint.GetInhabitant(actor).Status == SocietyInhabitantStatus.Active;

    private bool ReadyForMarket(string actor) => MarketAdult(actor) &&
        !NeedsUrgentFood(inhabitants[actor]) && !NeedsUrgentWarmth(inhabitants[actor]) &&
        !IsConversationBusy(actor) && inhabitants[actor].Equipment?.Repair is null &&
        !(inhabitants[actor].Project is { Stage: not ("completed" or "cancelled") } project && !project.RequiresFreshChoice) &&
        CarriedHouseDelivery(actor) is null && !fields.Any(field => field.Work?.WorkerId == actor);

    private void SetMarket(string townId, TownMarketState market)
    {
        var town = towns.Single(item => item.Id == townId);
        SetTown(town with { Markets = town.Markets.Select(item => item.Id == market.Id ? market : item).ToArray() });
    }

    private bool LiveMarketStall(TownRuntimeState town, TownMarketState market, MarketStallState stall) =>
        market.RemovedTick is null && stall.RemovedTick is null &&
        worldSimulation.Buildings.Any(building => building.InstanceId == market.HallBuildingId &&
            building.TownId == town.Id && building.HouseholdId is null && building.Position == market.Site &&
            building.DefinitionId == MarketContent.Hall2x2().CanonicalId) &&
        worldSimulation.Buildings.Any(building => building.InstanceId == stall.BuildingId &&
            building.TownId == town.Id && building.HouseholdId is null &&
            building.Position == MarketContent.StallSite(market.Site, stall.SlotIndex) &&
            building.DefinitionId == MarketContent.Stall1x1().CanonicalId);

    private static MarketStallOccupancy? MarketOccupant(TownMarketState market, MarketStallState stall) =>
        market.Occupancies.SingleOrDefault(item => item.StallBuildingId == stall.BuildingId && item.EndedTick is null);

    private bool HasMarketOccupancy(string actor) => towns.Any(town => town.Markets.Any(market =>
        market.Occupancies.Any(item => item.SellerAgentId == actor && item.EndedTick is null)));

    private static string MarketChoiceId(string kind, params string[] parts) =>
        MarketTradeRules.Identity("market_" + kind + ":", parts);

    private string MarketPlaceMode(string actor, GridPoint destination, int range) =>
        IsWithinInteractionRange(inhabitants[actor].Position, destination, range) ? "at-site" : "walking";

    private bool CanReachMarketPoint(string actor, GridPoint destination, int range) =>
        IsWithinInteractionRange(inhabitants[actor].Position, destination, range) ||
        FindUnoccupiedRoute(actor, inhabitants[actor].Position, destination, range).Count > 0;

    private string MarketTradeMode(string actor, MarketTradeState trade) =>
        MarketPlaceMode(actor, trade.Position, ResourceInteractionRange) == "walking" ? "walking" :
        !IsWithinInteractionRange(inhabitants[trade.BuyerId].Position, trade.Position, ResourceInteractionRange)
            ? "waiting-for-buyer" : "at-site";

    private int MarketStockQuantity(TownMarketState market, MarketStallState stall, InventoryCheckpoint? inventory = null) =>
        MarketTradeRules.StockAt(market, stall.BuildingId, MarketContent.StallSite(market.Site, stall.SlotIndex),
            inventory ?? society.Checkpoint.Inventory).Sum(lot => lot.Quantity);

    private int MarketIncomingPayment(TownMarketState market, MarketStallState stall,
        InventoryCheckpoint? inventory = null) => market.Trades
        .Where(trade => IsOpenMarketTrade(trade) && trade.StallBuildingId == stall.BuildingId)
        .Select(trade => (inventory ?? society.Checkpoint.Inventory).GetOffer(trade.OfferId))
        .Where(offer => offer.State == DirectBarterState.Open)
        .Sum(offer => Math.Max(0, offer.SecondQuantity - offer.FirstQuantity));

    private bool OwnMarketGoods(string actor, InventoryLot lot) =>
        lot.OwnerId == actor || society.Checkpoint.GetInhabitant(actor).HouseholdId is { } household && lot.OwnerId == household;

    private int MarketSurplus(string actor, InventoryLot lot)
    {
        if (!IsEdibleFood(lot.ItemKind)) return AvailableLotQuantity(lot);
        var reserve = lot.OwnerId == actor
            ? Math.Max(2, CaregiverFoodCarryRequirement(actor, inhabitants[actor]))
            : 2 * society.Checkpoint.GetHousehold(lot.OwnerId).MemberIds.Count(id => inhabitants.ContainsKey(id));
        // The owner's other usable food of this kind covers the reserve first, so a load taken from real surplus stays surplus.
        var kept = society.Checkpoint.Inventory.Lots.Where(other => other.Id != lot.Id && other.OwnerId == lot.OwnerId &&
                other.ItemKind == lot.ItemKind && other.ContainerLotId is null && other.DeliveryBuildingId is null &&
                (lot.OwnerId == actor ? PersonalEquipmentRules.IsCarried(other, actor) : other.CarrierId is null) &&
                !OnMarketStall(other))
            .Sum(AvailableLotQuantity);
        return Math.Max(0, AvailableLotQuantity(lot) - Math.Max(0, reserve - kept));
    }

    private bool OnMarketStall(InventoryLot lot) => towns.Any(town => town.Markets.Any(market => market.Stalls.Any(stall =>
        MarketTradeRules.IsAt(lot, MarketContent.StallSite(market.Site, stall.SlotIndex)))));

    // Household goods a member carries away need the household's House to come back to.
    private bool CanCarryMarketGoods(string actor, InventoryLot lot) =>
        lot.OwnerId == actor || HouseForHousehold(lot.OwnerId) is not null;

    private bool ProtectedMarketItem(string actor, InventoryLot lot) =>
        AgentKnowledgeRules.IsArtifactKind(lot.ItemKind) ||
        PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id) ||
        BestUsableToolIds(society.Checkpoint.Inventory, actor).Contains(lot.Id);

    private IEnumerable<InventoryLot> MarketLoadSources(string actor)
    {
        var inventory = society.Checkpoint.Inventory;
        return inventory.Lots.Where(lot => MarketTradeRules.IsLoose(lot) && OwnMarketGoods(actor, lot) &&
            !ProtectedMarketItem(actor, lot) && MarketSurplus(actor, lot) > 0 &&
            (PersonalEquipmentRules.IsCarried(lot, actor) ||
             lot.OwnerId != actor && lot.CarrierId is null &&
             (lot.StorageBuildingId is null || worldSimulation.Buildings.Any(building =>
                 building.InstanceId == lot.StorageBuildingId && building.HouseholdId == lot.OwnerId)) &&
             CanCarryMarketGoods(actor, lot)) &&
            !OnMarketStall(lot))
            .OrderBy(lot => PersonalEquipmentRules.IsCarried(lot, actor) ? 0 : 1)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal);
    }

    private (InventoryLot Goods, InventoryLot Payment)? MarketQuote(string buyer, TownMarketState market,
        MarketStallState stall, MarketStallOccupancy occupancy)
    {
        var inventory = society.Checkpoint.Inventory;
        var position = MarketContent.StallSite(market.Site, stall.SlotIndex);
        if (!MarketAdult(buyer) || buyer == occupancy.SellerAgentId || !ReadyForMarket(buyer) ||
            !MarketAdult(occupancy.SellerAgentId) ||
            society.Checkpoint.GetInhabitant(occupancy.SellerAgentId).HouseholdId != occupancy.SellerHouseholdId ||
            !MarketTradeRules.IsInside(market, inhabitants[occupancy.SellerAgentId].Position) ||
            !CanReachMarketPoint(buyer, position, ResourceInteractionRange) ||
            inventory.Offers.Any(offer => offer.State == DirectBarterState.Open &&
                (offer.FirstPartyId == buyer || offer.SecondPartyId == buyer)) ||
            MarketStockQuantity(market, stall) + MarketIncomingPayment(market, stall) > MarketTradeRules.StallCapacity)
            return null;
        foreach (var goods in MarketTradeRules.StockAt(market, stall.BuildingId, position, inventory)
                     .Where(lot => MarketTradeRules.MaySell(market, occupancy, lot, society.Checkpoint, occupancy.SellerAgentId) &&
                         AvailableLotQuantity(lot) > 0 && BusinessBuyerWants(buyer, lot)))
        {
            foreach (var payment in inventory.Lots.Where(lot => lot.OwnerId == buyer &&
                         PersonalEquipmentRules.IsCarried(lot, buyer) && MarketTradeRules.IsLoose(lot) &&
                         lot.StorageBuildingId is null && lot.GroundPosition is null && lot.ItemKind != goods.ItemKind &&
                         !ProtectedMarketItem(buyer, lot) && !BusinessBuyerWants(buyer, lot) && MarketSurplus(buyer, lot) > 0)
                         .OrderBy(lot => lot.Id, StringComparer.Ordinal))
                // Trial exact barter gives and receives one physical unit, so neither receiving load grows.
                if (PersonalEquipmentRules.CarriedQuantity(inventory, buyer, inhabitants[buyer].Equipment) +
                    ReservedBusinessCarrySpace(buyer) <= PersonalEquipmentRules.Capacity(inventory, buyer, inhabitants[buyer].Equipment))
                    return (goods, payment);
        }
        return null;
    }

    private IEnumerable<MarketChoice> MarketChoices(string actor)
    {
        if (!MarketAdult(actor)) yield break;
        var inventory = society.Checkpoint.Inventory;
        var ready = ReadyForMarket(actor);
        var offeredLoad = false;
        foreach (var town in towns.OrderBy(item => item.Id, StringComparer.Ordinal))
            foreach (var market in town.Markets.OrderBy(item => item.Id, StringComparer.Ordinal))
                foreach (var stall in market.Stalls.OrderBy(item => item.SlotIndex))
                {
                    var position = MarketContent.StallSite(market.Site, stall.SlotIndex);
                    var occupancy = MarketOccupant(market, stall);
                    // Owners retain physical collection even after the building or borrowing ends.
                    foreach (var lot in MarketTradeRules.StockAt(market, stall.BuildingId, position, inventory)
                                 .Where(lot => MarketTradeRules.IsLoose(lot) && OwnMarketGoods(actor, lot) &&
                                     AvailableLotQuantity(lot) > 0 && CanCarryMarketGoods(actor, lot)))
                    {
                        var quantity = Math.Min(MarketTradeRules.LoadQuantity, Math.Min(AvailableLotQuantity(lot), FreeCarryCapacity(actor)));
                        if (quantity > 0 && CanReachMarketPoint(actor, position, ResourceInteractionRange))
                            yield return new(MarketChoiceId("collect", market.Id, stall.BuildingId, lot.Id,
                                quantity.ToString(CultureInfo.InvariantCulture), MarketPlaceMode(actor, position, ResourceInteractionRange)),
                                "collect", town, market, stall, occupancy, lot, quantity);
                    }
                    if (!LiveMarketStall(town, market, stall)) continue;
                    foreach (var trade in market.Trades.Where(item => IsOpenMarketTrade(item) && item.StallBuildingId == stall.BuildingId &&
                                 (item.SellerAgentId == actor || item.BuyerId == actor)).OrderBy(item => item.OfferId, StringComparer.Ordinal))
                    {
                        if (inventory.GetOffer(trade.OfferId).State != DirectBarterState.Open) continue;
                        yield return new(MarketChoiceId("cancel", trade.OfferId), "cancel", town, market, stall, occupancy, Trade: trade);
                        if (!NeedsUrgentWarmth(inhabitants[actor]))
                            yield return new(MarketChoiceId("continue", trade.OfferId, MarketTradeMode(actor, trade)),
                                "continue", town, market, stall, occupancy, Trade: trade);
                    }
                    if (occupancy?.SellerAgentId == actor)
                    {
                        yield return new(MarketChoiceId("leave", occupancy.Id), "leave", town, market, stall, occupancy);
                        if (!ready || society.Checkpoint.GetInhabitant(actor).HouseholdId != occupancy.SellerHouseholdId ||
                            !CanReachMarketPoint(actor, position, ResourceInteractionRange)) continue;
                        var room = MarketTradeRules.StallCapacity - MarketStockQuantity(market, stall) - MarketIncomingPayment(market, stall);
                        foreach (var lot in inventory.Lots.Where(lot => MarketTradeRules.IsLoose(lot) && OwnMarketGoods(actor, lot) &&
                                     PersonalEquipmentRules.IsCarried(lot, actor) && !ProtectedMarketItem(actor, lot) && MarketSurplus(actor, lot) > 0)
                                     .OrderBy(lot => lot.Id, StringComparer.Ordinal))
                        {
                            var quantity = Math.Min(MarketTradeRules.LoadQuantity, Math.Min(room, MarketSurplus(actor, lot)));
                            if (quantity > 0)
                                yield return new(MarketChoiceId("deposit", occupancy.Id, lot.Id,
                                    quantity.ToString(CultureInfo.InvariantCulture), MarketPlaceMode(actor, position, ResourceInteractionRange)),
                                    "deposit", town, market, stall, occupancy, lot, quantity);
                        }
                    }
                    else if (occupancy is not null && MarketQuote(actor, market, stall, occupancy) is { } quote)
                        yield return new(MarketChoiceId("buy", occupancy.Id, actor, quote.Goods.Id, quote.Payment.Id,
                            MarketPlaceMode(actor, position, ResourceInteractionRange)),
                            "buy", town, market, stall, occupancy, quote.Goods, 1, Payment: quote.Payment);
                    if (!ready || occupancy is not null || HasMarketOccupancy(actor) ||
                        society.Checkpoint.GetInhabitant(actor).HouseholdId is null ||
                        !CanReachMarketPoint(actor, position, ResourceInteractionRange) ||
                        MarketStockQuantity(market, stall) + MarketIncomingPayment(market, stall) >= MarketTradeRules.StallCapacity)
                        continue;
                    var offeredBorrow = false;
                    foreach (var lot in MarketLoadSources(actor))
                    {
                        if (PersonalEquipmentRules.IsCarried(lot, actor))
                        {
                            if (!offeredBorrow)
                            {
                                yield return new(MarketChoiceId("borrow", market.Id, stall.BuildingId, actor,
                                    MarketPlaceMode(actor, position, ResourceInteractionRange)),
                                    "borrow", town, market, stall);
                                offeredBorrow = true;
                            }
                            continue;
                        }
                        // One empty stall is enough to plan a load; the stall itself is borrowed on arrival.
                        if (offeredLoad) continue;
                        var quantity = Math.Min(MarketTradeRules.LoadQuantity, Math.Min(MarketSurplus(actor, lot), FreeCarryCapacity(actor)));
                        if (quantity > 0 && CanReachMarketPoint(actor, HouseholdStockPosition(lot), HouseholdStockInteractionRange(lot)))
                            yield return new(MarketChoiceId("load", market.Id, stall.BuildingId, lot.Id,
                                quantity.ToString(CultureInfo.InvariantCulture), MarketPlaceMode(actor, HouseholdStockPosition(lot),
                                    HouseholdStockInteractionRange(lot))), "load", town, market, stall, Lot: lot, Quantity: quantity);
                    }
                    offeredLoad = true;
                }
    }

    private void AddMarketCandidates(List<CognitionCandidate> candidates, string actor)
    {
        // Trade answers and leaving come first, so the limit never hides them behind stock choices.
        foreach (var choice in MarketChoices(actor).OrderBy(item => item.Kind is "continue" or "cancel" or "leave" ? 0 : 1)
                     .Take(MarketCandidateLimit))
        {
            var offer = choice.Trade is { } trade ? society.Checkpoint.Inventory.GetOffer(trade.OfferId) : null;
            var text = choice.Kind switch
            {
                "borrow" => "Walk to the Market and borrow this empty Town-owned stall while you remain at the Market.",
                "load" => $"Collect {choice.Quantity} household-owned {choice.Lot!.ItemKind} to carry to an empty Market stall.",
                "deposit" => $"Place {choice.Quantity} {choice.Lot!.ItemKind} at your borrowed stall; its owner stays the same.",
                "collect" => $"Collect {choice.Quantity} of your {choice.Lot!.ItemKind} from this Market stall.",
                "leave" => "Give up this stall. Goods left here keep their owner and cannot be sold by its next borrower.",
                "buy" => $"Walk to this Market stall and offer 1 {choice.Payment!.ItemKind} for 1 {choice.Lot!.ItemKind}; its named seller may refuse.",
                "cancel" => "Cancel this Market exchange and release its exact goods.",
                _ => actor == choice.Trade!.BuyerId
                    ? $"Wait at the stall for {offer!.FirstQuantity} {choice.Trade.GoodsKind} in exchange for {offer.SecondQuantity} {choice.Trade.PaymentKind}."
                    : MarketTradeMode(actor, choice.Trade) == "waiting-for-buyer"
                        ? $"Wait at your stall for the buyer, who offers {offer!.SecondQuantity} {choice.Trade.PaymentKind} for {offer.FirstQuantity} {choice.Trade.GoodsKind}."
                        : $"Meet the buyer and accept {offer!.SecondQuantity} {choice.Trade.PaymentKind} for {offer.FirstQuantity} {choice.Trade.GoodsKind}; the payment goes to your household.",
            };
            // Above safe idle, like Town project donations: built-in rules cannot act on a Market choice.
            candidates.Add(new(choice.Id, text, choice.Kind switch
            {
                "cancel" or "leave" => 175,
                "collect" => 174,
                "load" or "deposit" or "borrow" => 173,
                "buy" => 172,
                _ => 171,
            }, choice.Stall.BuildingId));
        }
    }

    private bool ApplyMarketCandidate(string actor, PlaytestInhabitantState state, string id)
    {
        if (!IsMarketCandidate(id)) return false;
        var choice = MarketChoices(actor).SingleOrDefault(item => item.Id == id);
        if (choice is null) return true;
        var position = MarketContent.StallSite(choice.Market.Site, choice.Stall.SlotIndex);
        if (choice.Kind == "leave")
        {
            EndMarketOccupancy(choice.Town.Id, choice.Market, choice.Occupancy!, "The seller gave up the stall.");
            return true;
        }
        if (choice.Kind is "continue" or "cancel")
        {
            ContinueMarketTrade(actor, state, choice, choice.Kind == "cancel");
            return true;
        }
        var destination = choice.Kind == "load" ? HouseholdStockPosition(choice.Lot!) : position;
        var range = choice.Kind == "load" ? HouseholdStockInteractionRange(choice.Lot!) : ResourceInteractionRange;
        if (!IsWithinInteractionRange(state.Position, destination, range))
        {
            MoveToward(actor, state, destination, "market_" + choice.Kind, range);
            return true;
        }
        switch (choice.Kind)
        {
            case "borrow":
                // Occupancy is claimed only by physical arrival, not a plan to arrive later.
                var occupancy = new MarketStallOccupancy(MarketTradeRules.Identity("market-occupancy:", choice.Market.Id,
                    choice.Stall.BuildingId, actor, WorldTick.ToString(CultureInfo.InvariantCulture),
                    choice.Market.Occupancies.Count.ToString(CultureInfo.InvariantCulture)),
                    choice.Stall.BuildingId, actor, WorldTick)
                {
                    SellerHouseholdId = society.Checkpoint.GetInhabitant(actor).HouseholdId!,
                };
                SetMarket(choice.Town.Id, choice.Market with { Occupancies = choice.Market.Occupancies.Append(occupancy).ToArray() });
                AppendEvent("market_stall_borrowed", $"{choice.Town.Id}|{choice.Market.Id}|{choice.Stall.BuildingId}|{actor}|{occupancy.Id}", position);
                break;
            case "load":
            case "collect":
                ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
                    MarketTradeRules.Identity("market-pickup:", actor, id, WorldTick.ToString(CultureInfo.InvariantCulture)),
                    choice.Lot!.Id, choice.Lot.OwnerId, choice.Quantity, carrierId: actor));
                AppendEvent(choice.Kind == "load" ? "market_stock_loaded" : "market_stock_collected",
                    $"{choice.Town.Id}|{choice.Market.Id}|{choice.Stall.BuildingId}|{actor}|{choice.Lot!.Id}", destination);
                break;
            case "deposit":
                DepositMarketStock(actor, choice, position);
                break;
            case "buy":
                OfferMarketTrade(actor, choice, position);
                break;
        }
        return true;
    }

    /// <summary>A continued personal intention may walk, but physical arrival needs a new admitted choice.</summary>
    private void ContinueMarketWalk(string actor, string id)
    {
        var choice = MarketChoices(actor).SingleOrDefault(item => item.Id == id);
        if (choice is null || choice.Kind is "leave" or "cancel") return;
        var destination = choice.Kind == "load" ? HouseholdStockPosition(choice.Lot!) :
            MarketContent.StallSite(choice.Market.Site, choice.Stall.SlotIndex);
        var range = choice.Kind == "load" ? HouseholdStockInteractionRange(choice.Lot!) : ResourceInteractionRange;
        if (!IsWithinInteractionRange(inhabitants[actor].Position, destination, range))
            MoveToward(actor, inhabitants[actor], destination, "market_" + choice.Kind, range);
    }

    private void DepositMarketStock(string actor, MarketChoice choice, GridPoint position)
    {
        var lot = choice.Lot!;
        var receiptId = MarketTradeRules.ReceiptId(choice.Occupancy!.Id, lot.Id, lot.OwnerId, choice.Quantity,
            WorldTick, choice.Market.StockReceipts.Count);
        var operation = receiptId + ":deposit";
        var movedId = lot.Quantity == choice.Quantity ? lot.Id : lot.Id + "#move:" + operation;
        long eventId = 0;
        ApplyInventoryTransition(inventory =>
        {
            var next = InventoryFixture.Relocate(inventory, operation, lot.Id, lot.OwnerId, choice.Quantity,
                groundPosition: new(position.X, position.Y));
            eventId = next.Events[^1].EventId;
            return next;
        });
        var receipt = new MarketStockReceipt(receiptId, choice.Occupancy.Id, actor, lot.OwnerId,
            lot.Id, movedId, lot.ItemKind, choice.Quantity, WorldTick, eventId);
        SetMarket(choice.Town.Id, choice.Market with { StockReceipts = choice.Market.StockReceipts.Append(receipt).ToArray() });
        AppendEvent("market_stock_delivered", $"{choice.Town.Id}|{choice.Market.Id}|{choice.Stall.BuildingId}|{actor}|{receipt.Id}", position);
    }

    private void OfferMarketTrade(string buyer, MarketChoice choice, GridPoint position)
    {
        var occupancy = choice.Occupancy!;
        var seller = occupancy.SellerAgentId;
        var owner = occupancy.SellerHouseholdId;
        var id = MarketTradeRules.Identity(MarketTradeRules.OfferPrefix, occupancy.Id, buyer,
            choice.Lot!.Id, choice.Payment!.Id, WorldTick.ToString(CultureInfo.InvariantCulture),
            choice.Market.Trades.Count.ToString(CultureInfo.InvariantCulture));
        ApplyInventoryTransition(inventory => InventoryFixture.AcceptDirectBarterOffer(
            InventoryFixture.CreateDirectBarterOffer(inventory, new(id, 1, choice.Lot.OwnerId, buyer,
                choice.Lot.Id, 1, choice.Payment.Id, 1, WorldTick + MarketTradeRules.OfferLifetimeTicks)),
            id, 1, buyer));
        var trade = new MarketTradeState(id, occupancy.Id, choice.Stall.BuildingId, seller,
            choice.Lot.OwnerId, owner, buyer, position, WorldTick, choice.Lot.ItemKind, choice.Payment.ItemKind);
        SetMarket(choice.Town.Id, choice.Market with { Trades = choice.Market.Trades.Append(trade).ToArray() });
        AppendEvent("market_trade_offered", $"{choice.Town.Id}|{choice.Market.Id}|{choice.Stall.BuildingId}|{seller}|{buyer}|{id}", position);
    }

    private string? MarketTradeFailure(TownRuntimeState town, TownMarketState market, MarketTradeState trade, DirectBarterOffer offer)
    {
        if (offer.ExpiryTick < WorldTick) return "The offer ran out of time.";
        var stall = market.Stalls.Single(item => item.BuildingId == trade.StallBuildingId);
        var occupancy = market.Occupancies.Single(item => item.Id == trade.OccupancyId);
        if (!LiveMarketStall(town, market, stall) || occupancy.EndedTick is not null ||
            MarketOccupant(market, stall)?.Id != occupancy.Id || !MarketAdult(trade.SellerAgentId) ||
            !MarketTradeRules.IsInside(market, inhabitants[trade.SellerAgentId].Position) ||
            society.Checkpoint.GetInhabitant(trade.SellerAgentId).HouseholdId != trade.PaymentOwnerId)
            return "The named seller no longer borrows this stall or represents the receiving household.";
        if (!MarketAdult(trade.BuyerId)) return "The buyer is no longer available.";
        var inventory = society.Checkpoint.Inventory;
        var goods = inventory.Lots.SingleOrDefault(lot => lot.Id == offer.FirstLotId);
        var payment = inventory.Lots.SingleOrDefault(lot => lot.Id == offer.SecondLotId);
        if (goods is null || payment is null || goods.OwnerId != trade.GoodsOwnerId ||
            !MarketTradeRules.IsAt(goods, trade.Position) ||
            !MarketTradeRules.MaySell(market, occupancy, goods, society.Checkpoint, trade.SellerAgentId) ||
            goods.ConditionBasisPoints <= 0 || goods.FreshnessBasisPoints <= 0 || goods.Quantity < offer.FirstQuantity ||
            payment.OwnerId != trade.BuyerId || !MarketTradeRules.IsLoose(payment) ||
            !PersonalEquipmentRules.IsCarried(payment, trade.BuyerId) || payment.GroundPosition is not null ||
            payment.StorageBuildingId is not null || payment.ConditionBasisPoints <= 0 || payment.FreshnessBasisPoints <= 0 ||
            payment.Quantity < offer.SecondQuantity || ProtectedMarketItem(trade.BuyerId, payment) ||
            !MarketTradeValidation.HasExactOpenClaim(inventory, offer, true) ||
            !MarketTradeValidation.HasExactOpenClaim(inventory, offer, false))
            return "The exact reserved goods are no longer usable at their agreed location.";
        if (MarketStockQuantity(market, stall) + MarketIncomingPayment(market, stall) > MarketTradeRules.StallCapacity ||
            PersonalEquipmentRules.CarriedQuantity(inventory, trade.BuyerId, inhabitants[trade.BuyerId].Equipment) +
            ReservedBusinessCarrySpace(trade.BuyerId) > PersonalEquipmentRules.Capacity(inventory, trade.BuyerId, inhabitants[trade.BuyerId].Equipment))
            return "There is no longer enough physical receiving space.";
        if (!IsWithinInteractionRange(inhabitants[trade.BuyerId].Position, trade.Position, ResourceInteractionRange) &&
            FindUnoccupiedRoute(trade.BuyerId, inhabitants[trade.BuyerId].Position, trade.Position, ResourceInteractionRange).Count == 0)
            return "The buyer can no longer reach the stall.";
        return null;
    }

    private void ContinueMarketTrade(string actor, PlaytestInhabitantState state, MarketChoice choice, bool cancel)
    {
        var trade = choice.Trade!;
        var offer = society.Checkpoint.Inventory.GetOffer(trade.OfferId);
        if (cancel || MarketTradeFailure(choice.Town, choice.Market, trade, offer) is { })
        {
            CancelMarketTrade(choice.Town.Id, choice.Market, trade, cancel
                ? "One of the traders cancelled the exchange." : MarketTradeFailure(choice.Town, choice.Market, trade, offer)!);
            return;
        }
        if (!IsWithinInteractionRange(state.Position, trade.Position, ResourceInteractionRange))
        {
            MoveToward(actor, state, trade.Position, "market_trade", ResourceInteractionRange);
            return;
        }
        if (actor != trade.SellerAgentId || !IsWithinInteractionRange(inhabitants[trade.BuyerId].Position,
                trade.Position, ResourceInteractionRange)) return;
        // The named seller's own admitted choice accepts for personal goods or their current household.
        var goods = society.Checkpoint.Inventory.GetLot(offer.FirstLotId);
        var payment = society.Checkpoint.Inventory.GetLot(offer.SecondLotId);
        var goodsId = goods.Quantity == offer.FirstQuantity ? goods.Id : goods.Id + "#barter:" + offer.Id;
        var paymentId = payment.Quantity == offer.SecondQuantity ? payment.Id : payment.Id + "#barter:" + offer.Id;
        var receiptId = MarketTradeRules.ReceiptId(trade.OccupancyId, paymentId, trade.PaymentOwnerId,
            offer.SecondQuantity, WorldTick, choice.Market.StockReceipts.Count, offer.Id);
        long eventId = 0;
        ApplyInventoryTransition(inventory =>
        {
            var settled = InventoryFixture.AcceptDirectBarterOffer(inventory, offer.Id, offer.Revision, trade.GoodsOwnerId);
            settled = InventoryFixture.Relocate(settled, offer.Id + ":collect", goodsId, trade.BuyerId,
                offer.FirstQuantity, carrierId: trade.BuyerId);
            if (trade.GoodsOwnerId != trade.PaymentOwnerId)
                settled = InventoryFixture.Transfer(settled, offer.Id + ":household-payment", trade.GoodsOwnerId,
                    trade.PaymentOwnerId, paymentId, offer.SecondQuantity, "market_household_payment");
            settled = InventoryFixture.Relocate(settled, receiptId + ":deposit", paymentId, trade.PaymentOwnerId,
                offer.SecondQuantity, groundPosition: new(trade.Position.X, trade.Position.Y));
            eventId = settled.Events[^1].EventId;
            return settled;
        });
        var receipt = new MarketStockReceipt(receiptId, trade.OccupancyId, actor, trade.PaymentOwnerId,
            paymentId, paymentId, trade.PaymentKind, offer.SecondQuantity, WorldTick, eventId, offer.Id);
        SetMarket(choice.Town.Id, choice.Market with
        {
            Trades = choice.Market.Trades.Select(item => item.OfferId == trade.OfferId ? trade with { SettledTick = WorldTick } : item).ToArray(),
            StockReceipts = choice.Market.StockReceipts.Append(receipt).ToArray(),
        });
        AppendEvent("market_trade_completed", $"{choice.Town.Id}|{choice.Market.Id}|{choice.Stall.BuildingId}|{actor}|{trade.BuyerId}|{trade.OfferId}", trade.Position);
    }

    private void CancelMarketTrade(string townId, TownMarketState market, MarketTradeState trade, string reason)
    {
        var offer = society.Checkpoint.Inventory.GetOffer(trade.OfferId);
        if (offer.State == DirectBarterState.Open)
            ApplyInventoryTransition(inventory => InventoryFixture.CancelDirectBarterOffer(inventory,
                offer.Id, offer.Revision, offer.FirstPartyId));
        SetMarket(townId, market with
        {
            Trades = market.Trades.Select(item => item.OfferId == trade.OfferId
            ? trade with { CancellationReason = reason } : item).ToArray()
        });
        AppendEvent("market_trade_cancelled", $"{townId}|{market.Id}|{trade.StallBuildingId}|{trade.SellerAgentId}|{trade.BuyerId}|{trade.OfferId}", trade.Position);
    }

    private void EndMarketOccupancy(string townId, TownMarketState market, MarketStallOccupancy occupancy, string reason)
    {
        foreach (var trade in market.Trades.Where(item => IsOpenMarketTrade(item) && item.OccupancyId == occupancy.Id &&
                     society.Checkpoint.Inventory.GetOffer(item.OfferId).State == DirectBarterState.Open).ToArray())
        {
            CancelMarketTrade(townId, market, trade, reason);
            market = towns.Single(town => town.Id == townId).Markets.Single(item => item.Id == market.Id);
        }
        market = market with
        {
            Occupancies = market.Occupancies.Select(item => item.Id == occupancy.Id
            ? occupancy with { EndedTick = WorldTick, ReleaseReason = reason } : item).ToArray()
        };
        SetMarket(townId, market);
        var stall = market.Stalls.Single(item => item.BuildingId == occupancy.StallBuildingId);
        AppendEvent("market_stall_left", $"{townId}|{market.Id}|{stall.BuildingId}|{occupancy.SellerAgentId}|{occupancy.Id}",
            MarketContent.StallSite(market.Site, stall.SlotIndex));
    }

    private void MaintainMarkets()
    {
        foreach (var originalTown in towns.ToArray())
            foreach (var originalMarket in originalTown.Markets.ToArray())
            {
                var market = originalMarket;
                foreach (var occupancy in market.Occupancies.Where(item => item.EndedTick is null).ToArray())
                {
                    var stall = market.Stalls.Single(item => item.BuildingId == occupancy.StallBuildingId);
                    var reason = !LiveMarketStall(originalTown, market, stall) ? "The paid Market stall is no longer available." :
                        !MarketAdult(occupancy.SellerAgentId) ? "The seller is no longer available." :
                        society.Checkpoint.GetInhabitant(occupancy.SellerAgentId).HouseholdId != occupancy.SellerHouseholdId
                            ? "The seller's household changed." :
                        !MarketTradeRules.IsInside(market, inhabitants[occupancy.SellerAgentId].Position) ? "The seller left the Market." : null;
                    if (reason is null) continue;
                    EndMarketOccupancy(originalTown.Id, market, occupancy, reason);
                    market = towns.Single(town => town.Id == originalTown.Id).Markets.Single(item => item.Id == market.Id);
                }
                foreach (var trade in market.Trades.Where(IsOpenMarketTrade).ToArray())
                {
                    var offer = society.Checkpoint.Inventory.GetOffer(trade.OfferId);
                    // The inventory closes an offer at its deadline before this check sees it.
                    var failure = offer.State == DirectBarterState.Open ? MarketTradeFailure(originalTown, market, trade, offer) :
                        offer.State != DirectBarterState.Cancelled ? null :
                        offer.ExpiryTick < WorldTick ? "The offer ran out of time." : "The exact offer was cancelled.";
                    if (failure is null) continue;
                    CancelMarketTrade(originalTown.Id, market, trade, failure);
                    market = towns.Single(town => town.Id == originalTown.Id).Markets.Single(item => item.Id == market.Id);
                }
                // Removal ends borrowing but retains the stock history and owners' physical collection.
                var built = worldSimulation.Buildings.Select(building => building.InstanceId).ToHashSet(StringComparer.Ordinal);
                if ((market.RemovedTick is not null || built.Contains(market.HallBuildingId)) &&
                    market.Stalls.All(stall => stall.RemovedTick is not null || built.Contains(stall.BuildingId)))
                    continue;
                SetMarket(originalTown.Id, market with
                {
                    RemovedTick = built.Contains(market.HallBuildingId) ? market.RemovedTick : market.RemovedTick ?? WorldTick,
                    Stalls = market.Stalls.Select(stall => built.Contains(stall.BuildingId)
                        ? stall : stall with { RemovedTick = stall.RemovedTick ?? WorldTick }).ToArray(),
                });
            }
    }
}
