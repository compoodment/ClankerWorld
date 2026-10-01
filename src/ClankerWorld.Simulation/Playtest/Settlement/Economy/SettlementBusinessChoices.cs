using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void AddBusinessCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor)) return;
        var pending = businessTrade.Offers.FirstOrDefault(offer => offer.BuyerId == actor && offer.State == BusinessOfferState.Open);
        if (pending is not null)
        {
            candidates.Add(new("business_collect:" + pending.Id,
                $"Bring the agreed payment and collect {pending.GoodsQuantity} {pending.GoodsKind} at the business.", 11, pending.BuildingId));
            candidates.Add(new("business_cancel:" + pending.Id, "Cancel the exchange and release the goods and receiving space.", 65));
        }
        foreach (var offer in businessTrade.Offers.Where(offer => offer.SellerId == actor && offer.State == BusinessOfferState.Open))
            candidates.Add(new("business_serve:" + offer.Id, "Meet the customer at the stocked business to complete exact barter.", 15, offer.BuildingId));
        if (pending is null)
            foreach (var listing in businessTrade.Listings.Where(ListingIsLive).Where(listing =>
                         listing.HouseholdId != HouseholdFor(actor)).OrderBy(listing => listing.Id, StringComparer.Ordinal))
            {
                var goods = society.Checkpoint.Inventory.GetLot(listing.GoodsLotId);
                var payment = PersonalBusinessPayment(actor, listing);
                if (!BusinessWantsItem(actor, goods)) continue;
                if (payment is null && BusinessPaymentOpportunity(actor, listing))
                    candidates.Add(new("business_payment:" + listing.Id,
                        $"Collect or gather {listing.PaymentQuantity} {listing.PaymentKind} to bring to the business.",
                        FoodItems.IsEdible(goods.ItemKind) ? 10 : 27, listing.BuildingId));
                if (payment is not null &&
                    BusinessCarryingRoom(actor) >= Math.Max(0,
                        InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, goods.Id, listing.GoodsQuantity) -
                        InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, payment.Id, listing.PaymentQuantity)))
                    candidates.Add(new("business_buy:" + listing.Id,
                        $"Buy {listing.GoodsQuantity} {goods.ItemKind} for {listing.PaymentQuantity} {listing.PaymentKind}; bring both sides to the business.",
                        FoodItems.IsEdible(goods.ItemKind) ? 9 : 23, listing.BuildingId));
            }
        if (HouseholdFor(actor) is not { } householdId) return;
        foreach (var site in worldSimulation.Buildings.Where(building => building.HouseholdId == householdId))
        {
            if (BusinessSite(site.InstanceId) is null) continue;
            if (BusinessStockToClear(site.InstanceId) is not null && BusinessCleanupGround(actor, site) is not null)
                candidates.Add(new("business_clear:" + site.InstanceId,
                    "Move rejected business stock onto adjacent ground, preserving the goods and freeing storage.", 24, site.InstanceId));
            if (BusinessReceipt(site.InstanceId) is { } receipt &&
                BusinessTransferQuantity(receipt, 4, BusinessCarryingRoom(actor)) > 0)
                candidates.Add(new("business_receipts:" + site.InstanceId, "Collect the household's barter receipts from its business.", 22, site.InstanceId));
            if (BusinessGoodsToList(site) is { } goods)
                candidates.Add(new("business_list:" + site.InstanceId,
                    $"Offer actual stocked {goods.ItemKind} for an exact quantity of household supplies.", 26, site.InstanceId));
            if (MarketUnsoldStock(actor, site) is not null)
                candidates.Add(new("market_withdraw:" + site.InstanceId,
                    "Withdraw the household's unsold Market goods; the stall remains held while any goods or barter receipts remain.",
                    110, site.InstanceId));
        }
        if (CarriedHouseDelivery(actor) is null && BusinessSupplyOpportunity(actor) is { } supply)
            candidates.Add(new("business_stock:" + supply.Site.InstanceId,
                "Physically carry spare household goods into the Store before offering them.", 29, supply.Site.InstanceId));
        if (PersonalBusinessSurplus(actor) is not null)
            foreach (var market in businessTrade.Markets.Where(plot => !businessTrade.Stalls.Any(stall =>
                         stall.MarketId == plot.MarketId && stall.HouseholdId == householdId)))
                if (FreeMarketStall(actor, market.MarketId) is not null)
                    candidates.Add(new("market_deliver:" + market.MarketId,
                        "Carry spare goods into a free Market stall to reserve it for the household.", 29, market.MarketId));
        AddBusinessToolCandidates(candidates, actor);
    }

    private bool ApplyBusinessCandidate(string actor, PlaytestInhabitantState state, string candidate)
    {
        if (candidate.StartsWith("business_payment:", StringComparison.Ordinal))
        {
            var listing = businessTrade.Listings.FirstOrDefault(item => item.Id == candidate[17..] && ListingIsLive(item));
            if (listing is not null) CollectBusinessPayment(actor, state, listing);
            return true;
        }
        if (candidate.StartsWith("business_clear:", StringComparison.Ordinal))
        {
            var site = BusinessSite(candidate[15..]);
            if (site is null || BusinessStockToClear(site.InstanceId) is not { } stock ||
                BusinessCleanupGround(actor, site) is not { } ground) return true;
            if (state.Position != ground) MoveToward(actor, state, ground, "business_stock_cleanup", 0);
            else ClearRejectedBusinessStockCore(actor, site.InstanceId, stock.Id);
            return true;
        }
        if (candidate.StartsWith("business_buy:", StringComparison.Ordinal))
        {
            var listing = businessTrade.Listings.FirstOrDefault(item => item.Id == candidate[13..]);
            if (listing is not null && PersonalBusinessPayment(actor, listing) is { } payment)
                AcceptBusinessListingCore(actor, listing.Id, payment.Id);
            return true;
        }
        if (candidate.StartsWith("business_collect:", StringComparison.Ordinal) || candidate.StartsWith("business_serve:", StringComparison.Ordinal))
        {
            var collect = candidate.StartsWith("business_collect:", StringComparison.Ordinal);
            var offer = businessTrade.Offers.FirstOrDefault(item => item.Id == candidate[(collect ? 17 : 15)..] &&
                item.State == BusinessOfferState.Open);
            if (offer is null || BusinessSite(offer.BuildingId) is not { } site) return true;
            if (!IsWithinInteractionRange(state.Position, site.Position, ResourceInteractionRange))
                MoveToward(actor, state, site.Position, "business_exchange", ResourceInteractionRange);
            else if (collect) SettleBusinessOfferCore(actor, offer.Id);
            else if (inhabitants.TryGetValue(offer.BuyerId, out var buyer) &&
                     IsWithinInteractionRange(buyer.Position, site.Position, ResourceInteractionRange))
                SettleBusinessOfferCore(offer.BuyerId, offer.Id);
            return true;
        }
        if (candidate.StartsWith("business_cancel:", StringComparison.Ordinal))
        {
            CancelBusinessOfferCore(actor, candidate[16..], "cancelled");
            return true;
        }
        if (candidate.StartsWith("business_list:", StringComparison.Ordinal))
        {
            var site = BusinessSite(candidate[14..]);
            if (site is null || BusinessGoodsToList(site) is not { } goods) return true;
            if (state.Position != site.Position) MoveToward(actor, state, site.Position, "business_listing", 0);
            else ListBusinessGoodsCore(actor, site.InstanceId, goods.Id, 1, BusinessPaymentKind(site, goods.ItemKind), 1);
            return true;
        }
        if (candidate.StartsWith("business_receipts:", StringComparison.Ordinal))
        {
            var site = BusinessSite(candidate[18..]);
            if (site is null || BusinessReceipt(site.InstanceId) is not { } receipt) return true;
            if (state.Position != site.Position) MoveToward(actor, state, site.Position, "business_receipts", 0);
            else WithdrawBusinessStockCore(actor, site.InstanceId, receipt.Id,
                BusinessTransferQuantity(receipt, 4, BusinessCarryingRoom(actor)));
            return true;
        }
        if (candidate.StartsWith("market_withdraw:", StringComparison.Ordinal))
        {
            var site = BusinessSite(candidate[16..]);
            if (site is null || MarketUnsoldStock(actor, site) is not { } stock) return true;
            if (state.Position != site.Position) MoveToward(actor, state, site.Position, "market_withdrawal", 0);
            else
            {
                var quantity = BusinessTransferQuantity(stock, 4, BusinessCarryingRoom(actor));
                var home = HouseForHousehold(HouseholdFor(actor));
                var delivery = home is not null && StorageRoom(home.InstanceId) >=
                    InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, stock.Id, quantity) ? home.InstanceId : null;
                WithdrawBusinessStockCore(actor, site.InstanceId, stock.Id, quantity, delivery);
            }
            return true;
        }
        if (candidate.StartsWith("business_stock:", StringComparison.Ordinal))
        {
            SupplyBusinessStock(actor, state, candidate[15..]);
            return true;
        }
        if (candidate.StartsWith("market_deliver:", StringComparison.Ordinal))
        {
            var marketId = candidate[15..];
            if (PersonalBusinessSurplus(actor) is not { } goods || FreeMarketStall(actor, marketId) is not { } position) return true;
            if (state.Position != position) MoveToward(actor, state, position, "market_stock", 0);
            else DeliverToMarketStallCore(actor, marketId, goods.Id,
                BusinessTransferQuantity(goods, Math.Min(4, AvailableLotQuantity(goods) - 1), 64));
            return true;
        }
        return ApplyBusinessToolCandidate(actor, state, candidate);
    }

    private InventoryLot? PersonalBusinessPayment(string actor, BusinessListing listing) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == listing.PaymentKind &&
            lot.StorageBuildingId is null && lot.DeliveryBuildingId is null && BusinessLotCanMove(lot) &&
            AvailableLotQuantity(lot) >= listing.PaymentQuantity &&
            (!FoodItems.IsEdible(lot.ItemKind) || inhabitants[actor].HungerBasisPoints >= 6_500 &&
                AvailableLotQuantity(lot) > listing.PaymentQuantity))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private InventoryLot? HouseholdBusinessPayment(string actor, BusinessListing listing) =>
        SharedItem(listing.PaymentKind, actor) is { } stock && BusinessLotCanMove(stock) &&
            AvailableLotQuantity(stock) >= listing.PaymentQuantity &&
            BusinessTransferQuantity(stock, listing.PaymentQuantity, BusinessCarryingRoom(actor)) == listing.PaymentQuantity
            ? stock : null;

    private bool BusinessPaymentOpportunity(string actor, BusinessListing listing) =>
        HouseholdBusinessPayment(actor, listing) is not null ||
        BusinessCarryingRoom(actor) >= listing.PaymentQuantity && MaterialSource(listing.PaymentKind, actor) is not null;

    private void CollectBusinessPayment(string actor, PlaytestInhabitantState person, BusinessListing listing)
    {
        if (PersonalBusinessPayment(actor, listing) is not null) return;
        if (HouseholdBusinessPayment(actor, listing) is { } stock)
        {
            var position = HouseholdStockPosition(stock);
            var range = HouseholdStockInteractionRange(stock);
            if (!IsWithinInteractionRange(person.Position, position, range))
                MoveToward(actor, person, position, "business_payment", range);
            else ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"business-payment-pickup:{WorldTick}:{actor}:{listing.Id}", stock.OwnerId, actor, stock.Id,
                listing.PaymentQuantity, "business_payment_collected"));
        }
        else if (BusinessCarryingRoom(actor) >= listing.PaymentQuantity && MaterialSource(listing.PaymentKind, actor) is { } source)
            GatherProjectMaterial(actor, person, listing.PaymentKind, source);
    }

    private bool BusinessWantsItem(string actor, InventoryLot goods)
    {
        var kind = goods.ItemKind;
        var personal = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == kind &&
            lot.StorageBuildingId is null && lot.DeliveryBuildingId is null && lot.GroundPosition is null).Sum(AvailableLotQuantity);
        if (BusinessIngredientWanted(actor, kind)) return true;
        if (FoodItems.IsEdible(kind)) return personal < 2 && inhabitants[actor].HungerBasisPoints < 8_500;
        if (CarryEquipmentRules.IsClothing(kind)) return personal == 0 &&
            CarryEquipmentRules.Protection(goods, WeatherAt(inhabitants[actor].Position)) >
                CarryEquipmentRules.Protection(EquippedClothing(actor), WeatherAt(inhabitants[actor].Position));
        if (CarryEquipmentRules.IsCarryAid(kind)) return personal == 0 &&
            CarryEquipmentRules.Capacity(society.Checkpoint.Inventory, inhabitants[actor]) < CarryEquipmentRules.CapacityForKind(kind);
        if (ToolCapabilities.ForItem(kind) is { } tool) return ToolCapabilities.Best(society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == actor && lot.StorageBuildingId is null && lot.GroundPosition is null), tool.Kind) is not { } existing ||
                ToolCapabilities.ForItem(existing.ItemKind)!.Tier < tool.Tier;
        if (businessTrade.ToolOrders.Any(order => order.BuyerId == actor && order.ToolKind == kind &&
                order.State is "queued" or "running" or "ready")) return personal == 0;
        return WantsTradeItem(actor, goods);
    }

    private bool BusinessIngredientWanted(string actor, string kind)
    {
        if (HouseholdFor(actor) is not { } household) return false;
        var owned = society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == kind && lot.GroundPosition is null &&
            (lot.OwnerId == household || inhabitants.ContainsKey(lot.OwnerId) && HouseholdFor(lot.OwnerId) == household))
            .Sum(lot => (long)AvailableLotQuantity(lot));
        return worldSimulation.Buildings.Where(site => site.HouseholdId == household).Any(site =>
            worldContent.Recipes.Any(recipe => recipe.WorkstationBuildingId == site.DefinitionId &&
                NeedsRecipeOutput(recipe, household) && recipe.Inputs.Any(input => input.ResourceId == kind &&
                    owned < (long)input.Amount * SupplyBatches)));
    }

    private bool IsBusinessReceipt(InventoryLot lot) => businessTrade.Offers.Any(offer => offer.State == BusinessOfferState.Settled &&
        offer.BuildingId == lot.StorageBuildingId && offer.HouseholdId == lot.OwnerId &&
        (lot.Id == offer.PaymentLotId || lot.ProvenanceLotId == offer.PaymentLotId));

    private InventoryLot? BusinessReceipt(string buildingId) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.StorageBuildingId == buildingId && IsBusinessReceipt(lot) && BusinessLotCanMove(lot) && AvailableLotQuantity(lot) > 0)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private InventoryLot? MarketUnsoldStock(string actor, PlacedBuilding site) =>
        site.HouseholdId == HouseholdFor(actor) && worldContent.Buildings.Single(definition =>
            definition.CanonicalId == site.DefinitionId).Tags.Contains(BusinessContent.StallKind, StringComparer.Ordinal) &&
            FindUnoccupiedRoute(actor, inhabitants[actor].Position, site.Position, 0).Count > 0
            ? society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == site.HouseholdId &&
                lot.StorageBuildingId == site.InstanceId && BusinessLotCanMove(lot) && !IsBusinessReceipt(lot) &&
                BusinessTransferQuantity(lot, 4, BusinessCarryingRoom(actor)) > 0 &&
                !businessTrade.Offers.Any(offer => offer.State == BusinessOfferState.Open && offer.GoodsLotId == lot.Id))
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault()
            : null;

    private InventoryLot? BusinessGoodsToList(PlacedBuilding site) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == site.HouseholdId && lot.StorageBuildingId == site.InstanceId &&
            BusinessLotCanMove(lot) && BusinessCatalogAccepts(site, lot.ItemKind) && !IsBusinessReceipt(lot) &&
            BusinessSaleAllowance(site, lot) > 0 &&
            AvailableLotQuantity(lot) > (FoodItems.IsEdible(lot.ItemKind) && worldContent.Buildings.Single(
                definition => definition.CanonicalId == site.DefinitionId).Tags.Contains("farmhouse", StringComparer.Ordinal) ? 2 : 0) &&
            !businessTrade.Listings.Any(listing => listing.GoodsLotId == lot.Id && listing.ExpiryTick >= WorldTick))
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private string BusinessPaymentKind(PlacedBuilding site, string goodsKind)
    {
        var tags = worldContent.Buildings.Single(definition => definition.CanonicalId == site.DefinitionId).Tags;
        if (tags.Contains("restaurant", StringComparer.Ordinal) && FoodItems.IsEdible(goodsKind)) return "grain";
        if (tags.Contains("clinic", StringComparer.Ordinal) && goodsKind == "bandage") return "cloth";
        return goodsKind == "wood" ? "stone" : "wood";
    }

    private InventoryLot? PersonalBusinessSurplus(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
            BusinessLotCanMove(lot) && AvailableLotQuantity(lot) > (FoodItems.IsEdible(lot.ItemKind) ? 4 : 1))
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private (PlacedBuilding Site, InventoryLot Stock)? BusinessSupplyOpportunity(string actor)
    {
        if (HouseholdFor(actor) is not { } householdId) return null;
        foreach (var site in worldSimulation.Buildings.Where(building => building.HouseholdId == householdId &&
                     worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                         definition.Tags.Contains("store", StringComparer.Ordinal))))
        {
            if (BusinessStorageRoom(site.InstanceId) < 1) continue;
            var carried = PersonalBusinessSurplus(actor);
            if (carried is not null && BusinessTransferQuantity(carried,
                Math.Min(4, AvailableLotQuantity(carried) - 1), BusinessStorageRoom(site.InstanceId)) > 0) return (site, carried);
            var stock = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                    lot.StorageBuildingId != site.InstanceId && BusinessLotCanMove(lot) &&
                    AvailableLotQuantity(lot) > (FoodItems.IsEdible(lot.ItemKind) ? 4 : 1) &&
                    !businessTrade.Offers.Any(offer => offer.State == BusinessOfferState.Open && offer.GoodsLotId == lot.Id))
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
            if (stock is not null && BusinessTransferQuantity(stock, Math.Min(4, AvailableLotQuantity(stock) - 1),
                Math.Min(BusinessCarryingRoom(actor), BusinessStorageRoom(site.InstanceId))) > 0) return (site, stock);
        }
        return null;
    }

    private void SupplyBusinessStock(string actor, PlaytestInhabitantState state, string buildingId)
    {
        if (BusinessSupplyOpportunity(actor) is not { } opportunity || opportunity.Site.InstanceId != buildingId) return;
        var (site, stock) = opportunity;
        if (stock.OwnerId == actor)
        {
            if (state.Position != site.Position) MoveToward(actor, state, site.Position, "store_stock", 0);
            else
            {
                var quantity = BusinessTransferQuantity(stock, Math.Min(4, AvailableLotQuantity(stock) - 1),
                    BusinessStorageRoom(site.InstanceId));
                if (quantity > 0) ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                    $"store-stock:{WorldTick}:{actor}", actor, site.HouseholdId!, stock.Id, quantity, "store_stock_delivered", site.InstanceId));
            }
            return;
        }
        var position = HouseholdStockPosition(stock);
        var range = HouseholdStockInteractionRange(stock);
        if (!IsWithinInteractionRange(state.Position, position, range)) MoveToward(actor, state, position, "store_stock", range);
        else
        {
            var quantity = BusinessTransferQuantity(stock, Math.Min(4, AvailableLotQuantity(stock) - 1),
                Math.Min(BusinessStorageRoom(site.InstanceId), BusinessCarryingRoom(actor)));
            if (quantity > 0) ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"store-stock-pickup:{WorldTick}:{actor}", site.HouseholdId!, actor, stock.Id, quantity,
                "store_stock_picked_up", destinationDeliveryBuildingId: site.InstanceId));
        }
    }

    private GridPoint? FreeMarketStall(string actor, string marketId) => MarketStallPositions(marketId)
        .Where(position => !LooseStockTiles().Contains(position) && !worldSimulation.Buildings.Any(building => building.Position == position) &&
            FindUnoccupiedRoute(actor, inhabitants[actor].Position, position, 0).Count > 0)
        .OrderBy(position => map.FootDistance(inhabitants[actor].Position, position))
        .ThenBy(position => position.Y).ThenBy(position => position.X).Cast<GridPoint?>().FirstOrDefault();
}
