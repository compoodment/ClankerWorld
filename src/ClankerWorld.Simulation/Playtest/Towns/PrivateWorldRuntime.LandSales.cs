using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static bool LandSaleKind(string kind) => TownLandHearingRules.ValidText(kind, 128) &&
        !kind.Any(char.IsWhiteSpace) && !InventoryContainerRules.IsContainer(kind) && kind != "handcart";

    private InventoryLot[] LandSaleCarriedLots(string actor, TownLandSalePrice price) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.ItemKind == price.ItemKind && PersonalEquipmentRules.IsCarried(lot, actor) &&
            lot.DeliveryBuildingId is null && !PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id) && MarketSurplus(actor, lot) > 0)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();

    private InventoryLot? LandSaleStock(string actor, TownLandSalePrice price) => SharedItem(price.ItemKind, actor) is { } lot &&
        lot.OwnerId == society.Checkpoint.GetInhabitant(actor).HouseholdId && lot.DeliveryBuildingId is null &&
        lot.ItemKind != "tool" && ToolProgressionRules.Find(lot.ItemKind) is null && MarketSurplus(actor, lot) > 0 &&
        (lot.ItemKind != "food" || MayCollectSharedFood(actor)) ? lot : null;

    private void AddTownLandSaleCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town,
        TownLandTransferRequest request, IReadOnlyList<TownLandTransferParty> parties, string token)
    {
        if (request.Price is not { } price || town.Governance is not { } council ||
            !TownLandTransferRules.HasAllConsents(request, parties, council.Knowledge, WorldTick)) return;
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (household == request.TargetHouseholdId)
        {
            var carried = LandSaleCarriedLots(actor, price).Sum(lot => MarketSurplus(actor, lot));
            if (carried < price.Quantity && FreeCarryCapacity(actor) > 0 && LandSaleStock(actor, price) is not null)
                candidates.Add(new(CivicAction(town.Id, "land_transfer_collect_payment", token),
                    "Collect available household goods you may carry for this accepted land-use sale. " + LandTransferTerms(request), 175));
            if (carried >= price.Quantity && CanWalkToCivicBoard(actor, town))
                candidates.Add(new(CivicAction(town.Id, "land_transfer_pay", token),
                    "Bring the exact personally owned goods to the notice place; pay the seller household only when an accepted seller adult is present, and transfer the recorded permission together. " + LandTransferTerms(request), 180));
        }
        else if (household == price.SellerHouseholdId && !NearCivicBoard(actor, town) && CanWalkToCivicBoard(actor, town))
            candidates.Add(new(CivicAction(town.Id, "land_transfer_meet", token),
                "Meet the buyer at the notice place for the accepted goods sale. Walking supplies no payment or permission change. " + LandTransferTerms(request), 175));
    }

    /// <summary>Only the chosen physical trip continues locally; arrival needs a fresh personal choice.</summary>
    private void ContinueTownLandSaleWalk(string actor, string candidate)
    {
        var parts = candidate.Split('|');
        if (parts.Length != 5 || NeedsUrgentWarmth(inhabitants[actor])) return;
        var town = towns.SingleOrDefault(item => item.Id == parts[1]);
        if (town is null) return;
        var candidates = new List<CognitionCandidate>();
        AddTownLandTransferCandidates(candidates, actor, town);
        if (!candidates.Any(item => item.Id == candidate)) return;
        var request = town.LandHearings.Transfers.Single(item => item.Status == "pending" && LandTransferActionToken(item) == parts[3]);
        var lot = parts[2] == "land_transfer_collect_payment" ? LandSaleStock(actor, request.Price!) : null;
        var destination = lot is not null ? HouseholdStockPosition(lot) : CivicBoard(town);
        var range = lot is not null ? HouseholdStockInteractionRange(lot) : ResourceInteractionRange;
        if (destination is { } position && !IsWithinInteractionRange(inhabitants[actor].Position, position, range))
            MoveToward(actor, inhabitants[actor], position, "land_sale", range);
    }

    private void CollectLandSalePayment(string actor, TownLandTransferRequest request)
    {
        var price = request.Price!;
        var missing = price.Quantity - LandSaleCarriedLots(actor, price).Sum(lot => MarketSurplus(actor, lot));
        if (missing <= 0 || LandSaleStock(actor, price) is not { } lot) return;
        var position = HouseholdStockPosition(lot);
        var range = HouseholdStockInteractionRange(lot);
        if (!IsWithinInteractionRange(inhabitants[actor].Position, position, range))
        {
            MoveToward(actor, inhabitants[actor], position, "land_sale_payment", range);
            return;
        }
        var quantity = Math.Min(missing, Math.Min(FreeCarryCapacity(actor), MarketSurplus(actor, lot)));
        if (lot.ItemKind == "food") quantity = Math.Min(quantity, SharedFoodCollectionAllowance(actor));
        if (quantity <= 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"land-sale-collect:{WorldTick}:{actor}:{request.Id}",
            lot.OwnerId, actor, lot.Id, quantity, "land_sale_payment_collected"));
    }

    private (TownGovernanceState Council, TownLandHearingState LandHearings) SettleLandSale(TownRuntimeState town,
        TownGovernanceState council, TownLandTransferRequest request, string buyer)
    {
        var board = CivicBoard(town) ?? throw new InvalidOperationException("The sale needs its actual public notice place.");
        if (!NearCivicBoard(buyer, town))
        {
            MoveToward(buyer, inhabitants[buyer], board, "land_sale_payment", ResourceInteractionRange);
            return (council, town.LandHearings);
        }
        var price = request.Price!;
        var parties = LandTransferParties(request);
        var seller = parties.Single(party => party.HouseholdId == price.SellerHouseholdId).AdultIds
            .FirstOrDefault(actor => NearCivicBoard(actor, town));
        if (seller is null) return (council, town.LandHearings);
        var inventory = society.Checkpoint.Inventory;
        var remaining = price.Quantity;
        var paymentLots = new List<TownLandSalePaymentLot>();
        foreach (var lot in LandSaleCarriedLots(buyer, price))
        {
            var quantity = Math.Min(remaining, Math.Min(AvailableLotQuantity(inventory, lot), MarketSurplus(buyer, lot)));
            inventory = InventoryFixture.Transfer(inventory, TownLandTransferRules.PaymentTransferId(request, buyer, paymentLots.Count),
                buyer, price.SellerHouseholdId, lot.Id, quantity, "land_use_right_payment",
                destinationGroundPosition: new(board.X, board.Y));
            paymentLots.Add(new(lot.Id, quantity, inventory.Events[^1].EventId));
            remaining -= quantity;
            if (remaining == 0) break;
        }
        if (remaining != 0) throw new InvalidOperationException("The exact personally carried goods price is unavailable.");
        var payment = new TownLandSalePayment(buyer, seller, board, paymentLots.ToArray());
        var (hearings, rights) = TownLandTransferRules.Advance(town.LandHearings, map, householdLandUseRights,
            town.LandHearings.Transfers.Where(item => item.Status == "pending").ToDictionary(item => item.Id, LandTransferParties, StringComparer.Ordinal),
            householdLandUseRequests, council.Knowledge, WorldTick, new Dictionary<string, TownLandSalePayment> { [request.Id] = payment });
        if (hearings.Transfers.Single(item => item.Id == request.Id).Status != "transferred")
            throw new InvalidOperationException("The accepted sale no longer has valid permission terms.");
        // Both immutable plans are complete before publishing either side of the exchange.
        ApplyInventoryTransition(_ => inventory);
        householdLandUseRights = rights.ToList();
        council = TownGovernanceRules.PostNotice(council, "result", request.Id,
            "Goods paid and voluntary household permission transfer completed. " + LandTransferTerms(request), WorldTick);
        LandTransferEvent("settled", town, request, buyer, "transferred");
        return (council, hearings);
    }
}
