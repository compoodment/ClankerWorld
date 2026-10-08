using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string BusinessTradePrefix = "business-trade:";
    private List<BusinessTradeState> businessTrades = [];

    public IReadOnlyList<BusinessTradeState> BusinessTrades => businessTrades
        .OrderBy(trade => trade.OfferId, StringComparer.Ordinal).ToArray();

    private IEnumerable<(BusinessTradeState Trade, DirectBarterOffer Offer)> OpenBusinessTrades(InventoryCheckpoint? inventory = null) =>
        businessTrades.Select(trade => (Trade: trade, Offer: (inventory ?? society.Checkpoint.Inventory).Offers
                .Single(offer => offer.Id == trade.OfferId)))
            .Where(pair => pair.Offer.State == DirectBarterState.Open);

    private int ReservedBusinessCarrySpace(string actor, InventoryCheckpoint? inventory = null) => OpenBusinessTrades(inventory)
        .Where(pair => pair.Trade.BuyerId == actor)
        .Sum(pair => Math.Max(0, pair.Offer.FirstQuantity - pair.Offer.SecondQuantity));

    private int ReservedBusinessStorageSpace(string buildingId, InventoryCheckpoint? inventory = null) => OpenBusinessTrades(inventory)
        .Where(pair => pair.Trade.BuildingInstanceId == buildingId)
        .Sum(pair => Math.Max(0, pair.Offer.SecondQuantity - pair.Offer.FirstQuantity));

    private static bool IsLooseBusinessLot(InventoryLot lot) => lot.ContainerLotId is null &&
        !InventoryContainerRules.IsContainer(lot.ItemKind) && lot.DeliveryBuildingId is null && lot.GroundPosition is null &&
        lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0;

    private bool MayRespondAtBusiness(string actor, BusinessTradeState trade) => AdultResident(actor) &&
        HouseholdFor(actor) == trade.SellerHouseholdId &&
        worldSimulation.Buildings.Any(building => building.InstanceId == trade.BuildingInstanceId &&
            building.HouseholdId == trade.SellerHouseholdId && building.Position == trade.Position);

    private bool BusinessBuyerWants(string actor, InventoryLot lot) =>
        BusinessBuyerWantsWithoutRestaurant(actor, lot) || RestaurantInputDemand(actor, lot.ItemKind) > 0;

    private int RestaurantInputDemand(string actor, string itemKind, int spentPersonalQuantity = 0)
    {
        if (HouseholdFor(actor) is not { } householdId) return 0;
        var inventory = society.Checkpoint.Inventory;
        var restaurants = worldSimulation.Buildings.Where(building => building.HouseholdId == householdId &&
            worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains("restaurant", StringComparer.Ordinal))).ToArray();
        var missing = restaurants.Sum(building => Math.Max(0, WorkstationInputTarget(building, itemKind) -
            WorkstationOnsiteQuantity(inventory, building, itemKind) -
            WorkstationIncomingQuantity(inventory, building, itemKind)));
        if (missing == 0) return 0;

        // Personal stock is usable only by its actual owner; other members'
        // uncommitted possessions are not a household promise.
        var carried = inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == itemKind &&
                lot.DeliveryBuildingId is null && PersonalEquipmentRules.IsPhysicallyCarried(inventory, lot, actor))
            .Where(lot => restaurants.Any(building => WorkstationInputTarget(building, itemKind) >
                    WorkstationOnsiteQuantity(inventory, building, itemKind) +
                    WorkstationIncomingQuantity(inventory, building, itemKind) &&
                WorkstationDeliveryRoom(inventory, building.InstanceId) > 0 &&
                (lot.ContainerLotId is not { } containerId ||
                 !HasActiveContainerReservation(inventory, containerId) &&
                 !UnusableDeliveryStock(inventory, inventory.GetLot(containerId)) &&
                 ContainerFamilyQuantity(inventory, containerId) <= WorkstationDeliveryRoom(inventory, building.InstanceId))))
            .Sum(lot => UsableWorkstationQuantity(inventory, lot));
        var householdStock = inventory.Lots.Where(lot => lot.OwnerId == householdId && lot.ItemKind == itemKind &&
                lot.CarrierId is null && lot.DeliveryBuildingId is null && UsableWorkstationQuantity(inventory, lot) > 0 &&
                (lot.StorageBuildingId is null || worldSimulation.Buildings.Any(building =>
                    building.InstanceId == lot.StorageBuildingId && worldContent.Buildings.Any(definition =>
                        definition.CanonicalId == building.DefinitionId &&
                        definition.Tags.Any(tag => tag is "house" or "silo" or "farmhouse" or "restaurant")))))
            .Where(lot => lot.ContainerLotId is not { } containerId ||
                CanRemoveWorkstationStock(inventory, inventory.GetLot(containerId), 1))
            .Where(lot => restaurants.Any(building => WorkstationInputTarget(building, itemKind) >
                    WorkstationOnsiteQuantity(inventory, building, itemKind) +
                    WorkstationIncomingQuantity(inventory, building, itemKind) &&
                WorkstationPickupQuantity(actor, inventory,
                    lot.ContainerLotId is { } containerId ? inventory.GetLot(containerId) : lot,
                    building.InstanceId, int.MaxValue) > 0))
            .Where(lot => IsWithinInteractionRange(inhabitants[actor].Position, HouseholdStockPosition(lot),
                    HouseholdStockInteractionRange(lot)) ||
                FindUnoccupiedRoute(actor, inhabitants[actor].Position, HouseholdStockPosition(lot),
                    HouseholdStockInteractionRange(lot)).Count > 0)
            .GroupBy(lot => lot.StorageBuildingId)
            // Reserve the source's target once across all of its lots.
            .Sum(group => Math.Min(group.Sum(lot => UsableWorkstationQuantity(inventory, lot)),
                WorkstationSourceSurplus(inventory, group.First())));
        var purchases = OpenBusinessTrades(inventory).Where(pair => pair.Offer.ExpiryTick >= WorldTick &&
                pair.Trade.GoodsKind == itemKind &&
                society.Checkpoint.Inhabitants.Any(person => person.Id == pair.Trade.BuyerId &&
                    person.HouseholdId == householdId && person.Status == SocietyInhabitantStatus.Active) &&
                pair.Offer.AcceptedBy.Contains(pair.Trade.BuyerId, StringComparer.Ordinal))
            .Where(pair => inventory.Lots.Any(lot => lot.Id == pair.Offer.FirstLotId &&
                    lot.OwnerId == pair.Trade.SellerHouseholdId &&
                    lot.StorageBuildingId == pair.Trade.BuildingInstanceId &&
                    lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0 &&
                    lot.Quantity >= pair.Offer.FirstQuantity) &&
                inventory.Reservations.Any(reservation => reservation.Id == pair.Offer.Id + ":first" &&
                    reservation.State == InventoryReservationState.Reserved &&
                    reservation.Quantity >= pair.Offer.FirstQuantity))
            .Sum(pair => pair.Offer.FirstQuantity);
        return Math.Max(0, missing - Math.Max(0, carried - spentPersonalQuantity) - householdStock - purchases);
    }

    private bool BusinessBuyerWantsWithoutRestaurant(string actor, InventoryLot lot)
    {
        if (MedicalSupplyWanted(actor, lot.ItemKind)) return true;
        if (WantsFieldPlantingStock(actor, lot.ItemKind)) return true;
        if (WantsOrnamentInput(actor, lot.ItemKind)) return true;
        var inventory = society.Checkpoint.Inventory;
        if (ToolProgressionRules.Find(lot.ItemKind) is { } offeredTool)
        {
            var carriedTool = ToolProgressionRules.BestUsableTool(inventory, actor, offeredTool.Family);
            return carriedTool is null ||
                ToolProgressionRules.Find(carriedTool.ItemKind)!.Tier < offeredTool.Tier;
        }
        if (PersonalEquipmentRules.IsGarment(lot.ItemKind))
        {
            var weather = WeatherAt(inhabitants[actor].Position);
            var bestProtection = inventory.Lots.Where(item =>
                    ToolProgressionRules.IsTopLevelCarriedLot(item, actor) &&
                    PersonalEquipmentRules.IsGarment(item.ItemKind) && AvailableLotQuantity(item) > 0)
                .Select(item => PersonalEquipmentRules.Protection(item, weather)).DefaultIfEmpty(0).Max();
            return PersonalEquipmentRules.Protection(lot, weather) > bestProtection;
        }
        if (WantsTradeItem(actor, lot)) return true;
        if (lot.ItemKind == "iron")
        {
            var household = HouseholdFor(actor);
            // Trial reserve for real iron-tool work, not a tool to equip.
            return HouseholdBuildingWithTag(household, "blacksmith") is not null &&
                society.Checkpoint.Inventory.Lots.Where(item => item.ItemKind == "iron" &&
                    (item.OwnerId == actor || item.OwnerId == household)).Sum(AvailableLotQuantity) < 2;
        }
        if (PersonalEquipmentRules.IsCarryAid(lot.ItemKind))
            return PersonalEquipmentRules.Capacity(society.Checkpoint.Inventory, actor, inhabitants[actor].Equipment) <
                (lot.ItemKind == "leather_sack" ? 32 : lot.ItemKind == "sack" ? PersonalEquipmentRules.SackCapacity : PersonalEquipmentRules.BasketCapacity);
        return false;
    }

    private bool BusinessPaymentUseful(PlacedBuilding building, InventoryLot payment) =>
        IsEdibleFood(payment.ItemKind) || worldContent.Recipes.Any(recipe =>
            recipe.WorkstationBuildingId == building.DefinitionId &&
            recipe.Inputs.Any(input => input.ResourceId == payment.ItemKind)) ||
        worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
            .Tags.Contains("blacksmith", StringComparer.Ordinal) && OrnamentContent.IsOrnament(payment.ItemKind) ||
        worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
            .Tags.Contains("store", StringComparer.Ordinal);

    private sealed record BusinessQuote(InventoryLot Goods, int GoodsQuantity, InventoryLot Payment, int PaymentQuantity);

    private (int Goods, int Payment) BusinessQuoteAmounts(PlacedBuilding building, InventoryLot goods, InventoryLot payment)
    {
        // Trial barter terms follow an on-site recipe when the offered payment
        // is one of its inputs. Fresh Farmhouse produce is offered in pairs.
        var recipe = worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == building.DefinitionId &&
                recipe.Outputs.Any(output => output.ResourceId == goods.ItemKind) &&
                recipe.Inputs.Any(input => input.ResourceId == payment.ItemKind))
            .OrderBy(recipe => recipe.CanonicalId, StringComparer.Ordinal).FirstOrDefault();
        return recipe is not null
            ? (recipe.Outputs.Single(output => output.ResourceId == goods.ItemKind).Amount,
                recipe.Inputs.Single(input => input.ResourceId == payment.ItemKind).Amount)
            : (BusinessRules.KindOf(worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)) == "farmhouse" &&
                IsEdibleFood(goods.ItemKind) ? 2 : 1, 1);
    }

    private bool BusinessReceivingSpace(string buyer, PlacedBuilding building, int goods, int payment)
    {
        var inventory = society.Checkpoint.Inventory;
        var equipment = inhabitants[buyer].Equipment;
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        var capacity = IsFarmStorage(building) ? FarmFieldRules.FarmStorageCapacity :
            BuildingStorageRules.Capacity(definition, building) ?? int.MaxValue;
        return PersonalEquipmentRules.CarriedQuantity(inventory, buyer, equipment) - payment + goods +
                ReservedBusinessCarrySpace(buyer) <= PersonalEquipmentRules.Capacity(inventory, buyer, equipment) + HorseCargoCapacity(buyer) &&
            (long)StoredQuantity(building.InstanceId) - goods + payment + ReservedStorageGrowth(building.InstanceId) +
                ReservedBusinessStorageSpace(building.InstanceId) + InboundDeliveryQuantity(inventory, building.InstanceId) <= capacity;
    }

    private static HashSet<string> BestUsableToolIds(InventoryCheckpoint inventory, string actor) =>
        ToolProgressionRules.All.Select(tool => tool.Family).Distinct()
            .Select(family => ToolProgressionRules.BestUsableTool(inventory, actor, family)?.Id)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);

    private IEnumerable<InventoryLot> BusinessPaymentLots(string buyer, PlacedBuilding building)
    {
        var inventory = society.Checkpoint.Inventory;
        var protectedToolIds = BestUsableToolIds(inventory, buyer);
        return inventory.Lots.Where(lot => lot.OwnerId == buyer && PersonalEquipmentRules.IsCarried(lot, buyer) &&
                IsLooseBusinessLot(lot) && AvailableLotQuantity(lot) > 0 &&
                !protectedToolIds.Contains(lot.Id) && !BusinessBuyerWants(buyer, lot) &&
                BusinessPaymentUseful(building, lot) &&
                !PersonalEquipmentRules.IsSelected(inhabitants[buyer].Equipment, lot.Id) &&
                !AgentKnowledgeRules.IsArtifactKind(lot.ItemKind));
    }

    private bool MayVisitBusiness(string buyer, PlacedBuilding building)
    {
        if (!AdultResident(buyer) || NeedsUrgentWarmth(inhabitants[buyer]) ||
            building.HouseholdId is not { } seller || seller == HouseholdFor(buyer) ||
            !society.Checkpoint.Households.Any(household => household.Id == seller) ||
            !inhabitants.Keys.Any(actor => AdultResident(actor) && HouseholdFor(actor) == seller) ||
            IsWithinInteractionRange(inhabitants[buyer].Position, building.Position, ResourceInteractionRange) ||
            society.Checkpoint.Inventory.Offers.Any(offer => offer.State == DirectBarterState.Open &&
                (offer.FirstPartyId == buyer || offer.SecondPartyId == buyer))) return false;
        return true;
    }

    private bool RestaurantIngredientShopTrip(string buyer, PlacedBuilding building)
    {
        if (!MayVisitBusiness(buyer, building) || HouseholdFor(buyer) is not { } householdId) return false;
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        if (BusinessRules.KindOf(definition) is not { } kind) return false;
        // A Town shop's location and kind can motivate a visit. Its private
        // stock and exact terms are inspected only after the buyer arrives.
        var inputs = worldSimulation.Buildings.Where(site => site.HouseholdId == householdId &&
                worldContent.Buildings.Any(item => item.CanonicalId == site.DefinitionId &&
                    item.Tags.Contains("restaurant", StringComparer.Ordinal)))
            .SelectMany(site => worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == site.DefinitionId)
                .SelectMany(recipe => recipe.Inputs)).Select(input => input.ResourceId).Distinct(StringComparer.Ordinal);
        return inputs.Any(itemKind => BusinessRules.MaySell(kind, itemKind) && RestaurantInputDemand(buyer, itemKind) > 0) &&
            BusinessPaymentLots(buyer, building).Any(payment => RestaurantInputDemand(buyer, payment.ItemKind, 1) ==
                RestaurantInputDemand(buyer, payment.ItemKind)) &&
            FindUnoccupiedRoute(buyer, inhabitants[buyer].Position, building.Position, ResourceInteractionRange).Count > 0;
    }

    private bool RestaurantMealShopTrip(string buyer, PlacedBuilding building)
    {
        if (!MayVisitBusiness(buyer, building)) return false;
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        if (BusinessRules.KindOf(definition) != "restaurant") return false;
        // The menu describes possible meals, never whether the private shelf
        // holds them. Exact available goods and payment are checked on site.
        var mealKinds = worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == building.DefinitionId)
            .SelectMany(recipe => recipe.Outputs).Select(output => output.ResourceId).Distinct(StringComparer.Ordinal);
        return mealKinds.Any(kind => BusinessRules.MaySell("restaurant", kind) && WantsTradeFoodKind(buyer, kind)) &&
            BusinessPaymentLots(buyer, building).Any(payment => RestaurantInputDemand(buyer, payment.ItemKind, 1) ==
                RestaurantInputDemand(buyer, payment.ItemKind)) &&
            FindUnoccupiedRoute(buyer, inhabitants[buyer].Position, building.Position, ResourceInteractionRange).Count > 0;
    }

    private BusinessQuote? BusinessOpportunity(string buyer, PlacedBuilding building, string? exactGoodsLotId = null, bool requestedGoods = false)
    {
        var inventory = society.Checkpoint.Inventory;
        if (!AdultResident(buyer) || NeedsUrgentWarmth(inhabitants[buyer]) ||
            building.HouseholdId is not { } seller || seller == HouseholdFor(buyer) ||
            !society.Checkpoint.Households.Any(household => household.Id == seller) ||
            !inhabitants.Keys.Any(actor => AdultResident(actor) && HouseholdFor(actor) == seller) ||
            !IsWithinInteractionRange(inhabitants[buyer].Position, building.Position, ResourceInteractionRange) ||
            inventory.Offers.Any(offer => offer.State == DirectBarterState.Open &&
                (offer.FirstPartyId == buyer || offer.SecondPartyId == buyer)))
            return null;
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        if (BusinessRules.KindOf(definition) is not { } kind) return null;
        foreach (var goods in inventory.Lots.Where(lot => lot.OwnerId == seller &&
                     lot.StorageBuildingId == building.InstanceId && IsLooseBusinessLot(lot) &&
                     AvailableLotQuantity(lot) > 0 && BusinessRules.MaySell(kind, lot.ItemKind) &&
                     (exactGoodsLotId is null || lot.Id == exactGoodsLotId) && (requestedGoods || BusinessBuyerWants(buyer, lot))).OrderBy(lot => lot.Id, StringComparer.Ordinal))
        {
            // Payment comes from the buyer's own goods, never borrowed household ones.
            foreach (var payment in BusinessPaymentLots(buyer, building).Where(lot => lot.ItemKind != goods.ItemKind)
                .OrderBy(lot => lot.Id, StringComparer.Ordinal))
            {
                var amounts = BusinessQuoteAmounts(building, goods, payment);
                if (!requestedGoods && !BusinessBuyerWantsWithoutRestaurant(buyer, goods) &&
                    amounts.Goods > RestaurantInputDemand(buyer, goods.ItemKind)) continue;
                // A satisfied input reserve can become short after payment:
                // protect the exact quoted quantity, not just a desire flag.
                if (RestaurantInputDemand(buyer, payment.ItemKind, amounts.Payment) >
                    RestaurantInputDemand(buyer, payment.ItemKind)) continue;
                if (AvailableLotQuantity(goods) >= amounts.Goods && AvailableLotQuantity(payment) >= amounts.Payment &&
                    BusinessReceivingSpace(buyer, building, amounts.Goods, amounts.Payment))
                    return new(goods, amounts.Goods, payment, amounts.Payment);
            }
        }
        return null;
    }

    private bool IsBusinessFoodCandidate(string actor, string candidateId)
    {
        // Only the buyer's ready food can interrupt an order. Raw ingredients,
        // equipment and a seller's response still follow ordinary work rules.
        if (candidateId.StartsWith("business_continue:", StringComparison.Ordinal))
            return OpenBusinessTrades().Any(pair => pair.Trade.BuyerId == actor &&
                candidateId == "business_continue:" + pair.Offer.Id && IsEdibleFood(pair.Trade.GoodsKind));
        if (!candidateId.StartsWith("business_shop:", StringComparison.Ordinal)) return false;
        var building = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == candidateId[14..]);
        if (building is null) return false;
        if (BusinessOpportunity(actor, building) is { } quote)
            return IsEdibleFood(quote.Goods.ItemKind) && WantsTradeFoodKind(actor, quote.Goods.ItemKind);
        return RestaurantMealShopTrip(actor, building);
    }

    private void AddBusinessCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor) || NeedsUrgentWarmth(inhabitants[actor])) return;
        if (!NeedsUrgentFood(inhabitants[actor])) AddStoreStockCandidate(candidates, actor);
        foreach (var (trade, offer) in OpenBusinessTrades().OrderBy(pair => pair.Trade.OfferId, StringComparer.Ordinal))
        {
            var buyer = trade.BuyerId == actor;
            if (!buyer && !MayRespondAtBusiness(actor, trade)) continue;
            var goods = society.Checkpoint.Inventory.GetLot(offer.FirstLotId);
            var payment = society.Checkpoint.Inventory.GetLot(offer.SecondLotId);
            candidates.Add(new("business_continue:" + offer.Id, buyer
                ? $"Wait at the shop to receive {offer.FirstQuantity} {goods.ItemKind} for {offer.SecondQuantity} {payment.ItemKind}."
                : $"Meet the customer at the shop and accept {offer.SecondQuantity} {payment.ItemKind} for {offer.FirstQuantity} {goods.ItemKind}.",
                12, trade.BuildingInstanceId));
            candidates.Add(new("business_cancel:" + offer.Id,
                "Cancel this exchange and release the goods and receiving space.", 65, trade.BuildingInstanceId));
        }
        foreach (var building in worldSimulation.Buildings.OrderBy(building => building.InstanceId, StringComparer.Ordinal))
            if (BusinessOpportunity(actor, building) is { } choice)
                candidates.Add(new("business_shop:" + building.InstanceId,
                    $"Offer {choice.PaymentQuantity} {choice.Payment.ItemKind} for {choice.GoodsQuantity} {choice.Goods.ItemKind} stocked at this shop; the household may refuse.",
                    14, building.InstanceId));
            else if (RestaurantIngredientShopTrip(actor, building))
                candidates.Add(new("business_shop:" + building.InstanceId,
                    "Visit this Town shop to ask about missing Restaurant ingredients; any offer is checked there.",
                    14, building.InstanceId));
            else if (RestaurantMealShopTrip(actor, building))
                candidates.Add(new("business_shop:" + building.InstanceId,
                    "Visit this Restaurant to ask about a meal; any offer is checked there.",
                    14, building.InstanceId));
    }

    private void ApplyBusinessCandidate(string actor, PlaytestInhabitantState state, string candidate)
    {
        if (candidate == "business_stock_store")
        {
            StockStore(actor, state);
            return;
        }
        if (candidate.StartsWith("business_shop:", StringComparison.Ordinal))
        {
            var building = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == candidate[14..]);
            if (building is null) return;
            if (!IsWithinInteractionRange(state.Position, building.Position, ResourceInteractionRange))
            {
                if (RestaurantIngredientShopTrip(actor, building) || RestaurantMealShopTrip(actor, building))
                    MoveToward(actor, state, building.Position, "business_shop", ResourceInteractionRange);
                return;
            }
            if (BusinessOpportunity(actor, building) is not { } choice) return;
            _ = OpenBusinessQuote(actor, building, choice);
            return;
        }
        var cancel = candidate.StartsWith("business_cancel:", StringComparison.Ordinal);
        var offerId = candidate[(cancel ? 16 : 18)..];
        var trade = businessTrades.SingleOrDefault(item => item.OfferId == offerId);
        var offer = society.Checkpoint.Inventory.Offers.SingleOrDefault(item => item.Id == offerId);
        if (trade is null || offer is not { State: DirectBarterState.Open } ||
            (actor != trade.BuyerId && !MayRespondAtBusiness(actor, trade))) return;
        if (cancel)
        {
            CancelBusinessTrade(trade, offer, "One of the traders cancelled the exchange.");
            return;
        }
        if (BusinessTradeFailure(trade, offer) is { } failure)
        {
            CancelBusinessTrade(trade, offer, failure);
            return;
        }
        if (!IsWithinInteractionRange(state.Position, trade.Position, ResourceInteractionRange))
        {
            MoveToward(actor, state, trade.Position, "business_trade", ResourceInteractionRange);
            return;
        }
        if (actor == trade.BuyerId || !IsWithinInteractionRange(inhabitants[trade.BuyerId].Position,
                trade.Position, ResourceInteractionRange)) return;
        ApplyInventoryTransition(inventory =>
        {
            var payment = inventory.GetLot(offer.SecondLotId);
            var paymentId = payment.Quantity == offer.SecondQuantity ? payment.Id : $"{payment.Id}#barter:{offer.Id}";
            var settled = InventoryFixture.AcceptDirectBarterOffer(inventory, offer.Id, offer.Revision, trade.SellerHouseholdId);
            return settled with
            {
                Lots = settled.Lots.Select(lot => lot.Id == paymentId
                    ? lot with { StorageBuildingId = trade.BuildingInstanceId } : lot).ToArray(),
            };
        });
        ReplaceBusinessTrade(trade with { SellerActorId = actor });
        AppendEvent("business_trade_completed", actor + ":" + offer.Id);
    }

    private string OpenBusinessQuote(string actor, PlacedBuilding building, BusinessQuote choice)
    {
        var id = $"{BusinessTradePrefix}{WorldTick}:{actor}:{building.InstanceId}";
        // Quotes name exact actual quantities; future goods are never reserved.
        ApplyInventoryTransition(inventory => InventoryFixture.AcceptDirectBarterOffer(
            InventoryFixture.CreateDirectBarterOffer(inventory, new(id, 1,
                building.HouseholdId!, actor, choice.Goods.Id, choice.GoodsQuantity,
                choice.Payment.Id, choice.PaymentQuantity, WorldTick + 120)),
            id, 1, actor));
        businessTrades.Add(new(id, building.InstanceId, building.HouseholdId!, actor, building.Position, WorldTick,
            choice.Goods.ItemKind, choice.Payment.ItemKind));
        AppendEvent("business_trade_offered", actor + ":" + id);
        return id;
    }

    private string? BusinessTradeFailure(BusinessTradeState trade, DirectBarterOffer offer)
    {
        if (offer.ExpiryTick < WorldTick) return "The offer ran out of time.";
        if (!inhabitants.TryGetValue(trade.BuyerId, out var buyerState) || !AdultResident(trade.BuyerId))
            return "The buyer is no longer available.";
        var building = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == trade.BuildingInstanceId);
        if (building is null || building.HouseholdId != trade.SellerHouseholdId || building.Position != trade.Position ||
            !inhabitants.Keys.Any(actor => MayRespondAtBusiness(actor, trade)))
            return "The selling household no longer has access to this shop.";
        var inventory = society.Checkpoint.Inventory;
        var goods = inventory.Lots.SingleOrDefault(lot => lot.Id == offer.FirstLotId);
        var payment = inventory.Lots.SingleOrDefault(lot => lot.Id == offer.SecondLotId);
        if (goods is null || payment is null || !IsLooseBusinessLot(goods) || !IsLooseBusinessLot(payment) ||
            goods.OwnerId != trade.SellerHouseholdId || goods.StorageBuildingId != trade.BuildingInstanceId ||
            goods.GroundPosition is not null || !PersonalEquipmentRules.IsCarried(payment, trade.BuyerId) ||
            goods.Quantity < offer.FirstQuantity || payment.Quantity < offer.SecondQuantity ||
            new[] { offer.Id + ":first", offer.Id + ":second" }.Any(id => !inventory.Reservations.Any(reservation =>
                reservation.Id == id && reservation.State == InventoryReservationState.Reserved)))
            return "The reserved goods are no longer available at the agreed location.";
        var kind = BusinessRules.KindOf(worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId));
        if (kind is null || !BusinessRules.MaySell(kind, goods.ItemKind))
            return "This shop cannot sell the offered goods.";
        var equipment = buyerState.Equipment;
        var otherCarryReservations = ReservedBusinessCarrySpace(trade.BuyerId) -
            Math.Max(0, offer.FirstQuantity - offer.SecondQuantity);
        var afterCarry = PersonalEquipmentRules.CarriedQuantity(inventory, trade.BuyerId, equipment) -
            offer.SecondQuantity + offer.FirstQuantity + otherCarryReservations;
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        var capacity = IsFarmStorage(building) ? FarmFieldRules.FarmStorageCapacity :
            BuildingStorageRules.Capacity(definition, building) ?? int.MaxValue;
        var otherStorageReservations = ReservedBusinessStorageSpace(trade.BuildingInstanceId) -
            Math.Max(0, offer.SecondQuantity - offer.FirstQuantity);
        var afterStorage = (long)StoredQuantity(trade.BuildingInstanceId) - offer.FirstQuantity +
            offer.SecondQuantity + ReservedStorageGrowth(trade.BuildingInstanceId) + otherStorageReservations +
            InboundDeliveryQuantity(inventory, trade.BuildingInstanceId);
        if (afterCarry > PersonalEquipmentRules.Capacity(inventory, trade.BuyerId, equipment) + HorseCargoCapacity(trade.BuyerId) || afterStorage > capacity)
            return "There is no longer enough receiving space.";
        if (!IsWithinInteractionRange(buyerState.Position, trade.Position, ResourceInteractionRange) &&
            FindUnoccupiedRoute(trade.BuyerId, buyerState.Position, trade.Position, ResourceInteractionRange).Count == 0)
            return "The buyer can no longer reach the shop.";
        return null;
    }

    private void ReplaceBusinessTrade(BusinessTradeState trade) =>
        businessTrades[businessTrades.FindIndex(item => item.OfferId == trade.OfferId)] = trade;

    private void CancelBusinessTrade(BusinessTradeState trade, DirectBarterOffer offer, string reason)
    {
        if (offer.State == DirectBarterState.Open)
            ApplyInventoryTransition(inventory => InventoryFixture.CancelDirectBarterOffer(inventory,
                offer.Id, offer.Revision, offer.FirstPartyId));
        ReplaceBusinessTrade(trade with { CancellationReason = reason });
        AppendEvent("business_trade_cancelled", trade.BuyerId + ":" + trade.OfferId);
    }

    private void MaintainBusinessTrades()
    {
        foreach (var trade in businessTrades.ToArray())
        {
            var offer = society.Checkpoint.Inventory.GetOffer(trade.OfferId);
            if (offer.State == DirectBarterState.Open && BusinessTradeFailure(trade, offer) is { } failure)
                CancelBusinessTrade(trade, offer, failure);
            else if (offer.State == DirectBarterState.Cancelled && trade.CancellationReason is null)
                CancelBusinessTrade(trade, offer, offer.ExpiryTick < WorldTick
                    ? "The offer ran out of time." : "The exchange was interrupted.");
        }
        var retired = businessTrades.Where(trade => society.Checkpoint.Inventory.GetOffer(trade.OfferId).State != DirectBarterState.Open)
            .OrderByDescending(trade => trade.ProposedTick).ThenBy(trade => trade.OfferId, StringComparer.Ordinal)
            .Skip(32).Select(trade => trade.OfferId).ToHashSet(StringComparer.Ordinal);
        if (retired.Count == 0) return;
        businessTrades.RemoveAll(trade => retired.Contains(trade.OfferId));
        ApplyInventoryTransition(inventory => inventory with
        {
            Offers = inventory.Offers.Where(offer => !retired.Contains(offer.Id)).ToArray(),
            Reservations = inventory.Reservations.Where(reservation => !retired.Any(id =>
                reservation.Id == id + ":first" || reservation.Id == id + ":second")).ToArray(),
        });
    }

    private static void ValidateBusinessTrades(IReadOnlyList<BusinessTradeState>? trades,
        SocietyCheckpoint societyState, SeededMap map, long worldTick)
    {
        var inventory = societyState.Inventory;
        if (trades is null || trades.Any(trade => trade is null) || !trades.Select(trade => trade.OfferId).SequenceEqual(
                trades.Select(trade => trade.OfferId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("The checkpoint is missing canonical business exchange records.");
        foreach (var trade in trades)
        {
            var offer = inventory.Offers.SingleOrDefault(item => item.Id == trade.OfferId);
            if (offer is null || !offer.Id.StartsWith(BusinessTradePrefix, StringComparison.Ordinal) ||
                offer.Id != $"{BusinessTradePrefix}{trade.ProposedTick}:{trade.BuyerId}:{trade.BuildingInstanceId}" ||
                trade.ProposedTick < 0 || trade.ProposedTick > worldTick ||
                !BoundedBusinessText(trade.BuildingInstanceId, 256) ||
                !societyState.Households.Any(household => household.Id == trade.SellerHouseholdId) ||
                !societyState.Inhabitants.Any(person => person.Id == trade.BuyerId) ||
                !BoundedBusinessText(trade.GoodsKind, 128) || !BoundedBusinessText(trade.PaymentKind, 128) ||
                trade.Position.X < 0 || trade.Position.X >= map.Width || trade.Position.Y < 0 || trade.Position.Y >= map.Height ||
                inventory.Lots.Any(lot => lot.Id == offer.FirstLotId && lot.ItemKind != trade.GoodsKind) ||
                inventory.Lots.Any(lot => lot.Id == offer.SecondLotId && lot.ItemKind != trade.PaymentKind) ||
                offer.FirstPartyId != trade.SellerHouseholdId ||
                offer.SecondPartyId != trade.BuyerId || offer.FirstPartyId == offer.SecondPartyId ||
                offer.State == DirectBarterState.Open &&
                    !offer.AcceptedBy.SequenceEqual([trade.BuyerId], StringComparer.Ordinal) ||
                offer.State == DirectBarterState.Settled &&
                    !offer.AcceptedBy.SequenceEqual(new[] { offer.FirstPartyId, offer.SecondPartyId }
                        .Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                offer.State == DirectBarterState.Settled && (trade.SellerActorId is null ||
                    !societyState.Inhabitants.Any(person => person.Id == trade.SellerActorId)) ||
                trade.SellerActorId is not null && offer.State != DirectBarterState.Settled ||
                offer.State == DirectBarterState.Cancelled && !BoundedBusinessText(trade.CancellationReason, 160) ||
                trade.CancellationReason is not null && offer.State != DirectBarterState.Cancelled)
                throw new InvalidDataException("A business exchange does not match its authoritative inventory offer.");
        }
        if (inventory.Offers.Any(offer => offer.Id.StartsWith(BusinessTradePrefix, StringComparison.Ordinal) &&
                !trades.Any(trade => trade.OfferId == offer.Id)))
            throw new InvalidDataException("An inventory business offer has no physical shop binding.");
    }

    private static bool BoundedBusinessText(string? text, int limit) => text is { Length: > 0 } &&
        text.Length <= limit && text == text.Trim() && !text.Any(char.IsControl);
}
