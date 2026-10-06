using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record MilkStockChoice(InventoryLot Jug, string BuildingId, GridPoint Position,
        TownRuntimeState? Town = null, TownMarketState? Market = null, MarketStallOccupancy? Occupancy = null)
    {
        public string Id => "animal:milk_stock:" + AnimalKey(System.Text.Json.JsonSerializer.Serialize(new[] { Jug.Id, BuildingId }));
    }
    private sealed record MilkSaleChoice(string Seller, string Buyer, string Milk, string Jug, string Payment,
        string Building, GridPoint Position)
    {
        public string Id => "animal:milk_offer:" + AnimalKey(System.Text.Json.JsonSerializer.Serialize(new[] { Seller, Buyer, Milk, Jug, Payment, Building }));
    }
    private bool IsMilkJug(InventoryLot jug) => jug.ItemKind == InventoryContainerRules.WaterJug && jug.ConditionBasisPoints > 0 &&
        society.Checkpoint.Inventory.Lots.Any(lot => lot.ContainerLotId == jug.Id && lot.ItemKind == "milk" && AvailableLotQuantity(lot) > 0);
    private IEnumerable<InventoryLot> SpoiledMilkJugs(string actor)
    {
        if (!AdultResident(actor)) yield break;
        var inventory = society.Checkpoint.Inventory;
        foreach (var jug in inventory.Lots.Where(jug => jug.ItemKind == InventoryContainerRules.WaterJug &&
                     (jug.OwnerId == actor || jug.OwnerId == HouseholdFor(actor)) && jug.DeliveryBuildingId is null &&
                     !HasActiveContainerReservation(inventory, jug.Id) &&
                     (PersonalEquipmentRules.IsCarried(jug, actor) || jug.CarrierId is null && CanReachSharedItem(actor, jug))))
            if (inventory.Lots.Any(lot => lot.ContainerLotId == jug.Id && lot.ItemKind == "milk" && lot.FreshnessBasisPoints == 0)) yield return jug;
    }
    private bool ApplySpoiledMilkChoice(string actor, string id)
    {
        if (!id.StartsWith("animal:empty_milk:", StringComparison.Ordinal)) return false;
        var jug = SpoiledMilkJugs(actor).FirstOrDefault(jug => id == "animal:empty_milk:" + AnimalKey(jug.Id));
        if (jug is null) return true;
        var position = HouseholdStockPosition(jug);
        if (!PersonalEquipmentRules.IsCarried(jug, actor) && !IsWithinInteractionRange(inhabitants[actor].Position, position, HouseholdStockInteractionRange(jug)))
        { MoveToward(actor, inhabitants[actor], position, "empty_spoiled_milk", HouseholdStockInteractionRange(jug)); return true; }
        ApplyInventoryTransition(inventory => InventoryFixture.EmptySpoiledMilk(inventory, jug.OwnerId, jug.Id));
        AppendEvent("spoiled_milk_emptied", actor + ":" + jug.Id);
        return true;
    }
    private IEnumerable<MilkStockChoice> MilkStockChoices(string actor)
    {
        if (!AdultResident(actor) || HouseholdFor(actor) is not { } home) yield break;
        var inventory = society.Checkpoint.Inventory;
        foreach (var jug in inventory.Lots.Where(jug => jug.OwnerId == home && IsMilkJug(jug) && jug.DeliveryBuildingId is null &&
                     !HasActiveContainerReservation(inventory, jug.Id) &&
                     (jug.CarrierId is null || PersonalEquipmentRules.IsCarried(jug, actor)) &&
                     CanRemoveWorkstationStock(inventory, jug, 1)).OrderBy(jug => jug.Id, StringComparer.Ordinal))
        {
            if (MilkSaleSite(actor, inventory.Lots.First(lot => lot.ContainerLotId == jug.Id && lot.ItemKind == "milk"), out _, out _)) continue;
            if (!PersonalEquipmentRules.IsCarried(jug, actor) && !VesselFits(jug, FreeCarryCapacity(actor))) continue;
            if (HouseholdBuildingWithTag(home, "store") is { } store && jug.StorageBuildingId != store.InstanceId &&
                VesselFits(jug, RemainingDeliveryRoom(inventory, store.InstanceId)))
                yield return new(jug, store.InstanceId, store.Position);
            foreach (var town in towns)
                foreach (var market in town.Markets.Where(market => market.RemovedTick is null))
                    foreach (var occupancy in market.Occupancies.Where(occupancy => occupancy.EndedTick is null &&
                                 occupancy.SellerAgentId == actor && occupancy.SellerHouseholdId == home))
                    {
                        var stall = market.Stalls.Single(stall => stall.BuildingId == occupancy.StallBuildingId);
                        var position = MarketContent.StallSite(market.Site, stall.SlotIndex);
                        if (!MarketTradeRules.IsAt(jug, position) &&
                            ContainerFamilyQuantity(inventory, jug.Id) <= MarketTradeRules.StallCapacity - MarketStockQuantity(market, stall))
                            yield return new(jug, stall.BuildingId, position, town, market, occupancy);
                    }
        }
    }
    private bool ApplyMilkStockChoice(string actor, string id)
    {
        if (!id.StartsWith("animal:milk_stock:", StringComparison.Ordinal)) return false;
        var choice = MilkStockChoices(actor).FirstOrDefault(choice => choice.Id == id);
        if (choice is null) return true;
        var jug = choice.Jug;
        if (!PersonalEquipmentRules.IsCarried(jug, actor))
        {
            var source = HouseholdStockPosition(jug);
            var range = HouseholdStockInteractionRange(jug);
            if (!IsWithinInteractionRange(inhabitants[actor].Position, source, range))
            { MoveToward(actor, inhabitants[actor], source, "milk_stock_pickup", range); return true; }
            ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory, "milk-pickup-" + WorldTick + "-" + nextEventId,
                jug.Id, jug.OwnerId, 1, carrierId: actor));
            AppendEvent("milk_stock_picked_up", actor + ":" + jug.Id);
            return true;
        }
        if (inhabitants[actor].Position != choice.Position)
        { MoveToward(actor, inhabitants[actor], choice.Position, "milk_stock_delivery"); return true; }
        var receiptId = choice.Market is null ? null : MarketTradeRules.ReceiptId(choice.Occupancy!.Id, jug.Id, jug.OwnerId, 1,
            WorldTick, choice.Market.StockReceipts.Count);
        ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory, receiptId is null ? "milk-deposit-" + WorldTick + "-" + nextEventId : receiptId + ":deposit",
            jug.Id, jug.OwnerId, 1, storageBuildingId: choice.Market is null ? choice.BuildingId : null,
            groundPosition: choice.Market is null ? null : new(choice.Position.X, choice.Position.Y)));
        if (choice.Market is { } market)
        {
            var receipt = new MarketStockReceipt(receiptId!,
                choice.Occupancy!.Id, actor, jug.OwnerId, jug.Id, jug.Id, jug.ItemKind, 1, WorldTick,
                society.Checkpoint.Inventory.Events[^1].EventId);
            SetTown(choice.Town! with
            {
                Markets = choice.Town.Markets.Select(item => item.Id == market.Id ?
                market with { StockReceipts = market.StockReceipts.Append(receipt).ToArray() } : item).ToArray()
            });
        }
        AppendEvent("milk_stock_delivered", actor + ":" + choice.BuildingId);
        return true;
    }
    private bool MilkSaleSite(string seller, InventoryLot milk, out string buildingId, out GridPoint position)
    {
        buildingId = ""; position = default;
        if (milk.ContainerLotId is not { } rootId || !AdultResident(seller)) return false;
        var root = society.Checkpoint.Inventory.GetLot(rootId);
        if (root.CarrierId is not null || !(root.OwnerId == seller || root.OwnerId == HouseholdFor(seller))) return false;
        if (root.StorageBuildingId is { } storeId && worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == storeId &&
                building.HouseholdId == HouseholdFor(seller) && worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                    definition.Tags.Contains("store"))) is { } store)
        { buildingId = store.InstanceId; position = store.Position; return true; }
        foreach (var market in towns.SelectMany(town => town.Markets).Where(market => market.RemovedTick is null))
            foreach (var occupancy in market.Occupancies.Where(occupancy => occupancy.EndedTick is null && occupancy.SellerAgentId == seller &&
                         occupancy.SellerHouseholdId == HouseholdFor(seller)))
            {
                var stall = market.Stalls.Single(stall => stall.BuildingId == occupancy.StallBuildingId);
                var site = MarketContent.StallSite(market.Site, stall.SlotIndex);
                if (!MarketTradeRules.IsAt(root, site) || !MarketTradeRules.HasReceipt(market, occupancy, root)) continue;
                buildingId = stall.BuildingId; position = site; return true;
            }
        return false;
    }
    private IEnumerable<MilkSaleChoice> MilkSaleChoices(string seller)
    {
        if (!AdultResident(seller) || animalWorld.MilkOffers.Any(offer => offer.SellerId == seller)) yield break;
        var inventory = society.Checkpoint.Inventory;
        foreach (var milk in inventory.Lots.Where(lot => lot.ItemKind == "milk" && AvailableLotQuantity(lot) > 0)
                     .OrderBy(lot => lot.Id, StringComparer.Ordinal))
        {
            if (!MilkSaleSite(seller, milk, out var building, out var position) ||
                !IsWithinInteractionRange(inhabitants[seller].Position, position, 1)) continue;
            foreach (var buyer in inhabitants.Keys.Where(buyer => buyer != seller && AdultResident(buyer) &&
                         HouseholdFor(buyer) != HouseholdFor(seller) && !animalWorld.MilkOffers.Any(offer => offer.BuyerId == buyer) &&
                         IsWithinInteractionRange(inhabitants[buyer].Position, position, 1)).Order(StringComparer.Ordinal))
            {
                var jug = inventory.Lots.FirstOrDefault(jug => CanReceiveTradedMilk(buyer, jug));
                if (jug is null) continue;
                foreach (var payment in inventory.Lots.Where(lot => lot.OwnerId == buyer && PersonalEquipmentRules.IsCarried(lot, buyer) &&
                             lot.ContainerLotId is null && !InventoryContainerRules.IsContainer(lot.ItemKind) && lot.DeliveryBuildingId is null &&
                             AvailableLotQuantity(lot) > 0 && !PersonalEquipmentRules.IsSelected(inhabitants[buyer].Equipment, lot.Id))
                             .OrderBy(lot => lot.Id, StringComparer.Ordinal).Take(4))
                    yield return new(seller, buyer, milk.Id, jug.Id, payment.Id, building, position);
            }
        }
    }
    private bool CanReceiveTradedMilk(string buyer, InventoryLot jug) => jug.ItemKind == InventoryContainerRules.WaterJug &&
        jug.ConditionBasisPoints > 0 && (jug.OwnerId == buyer || jug.OwnerId == HouseholdFor(buyer)) &&
        PersonalEquipmentRules.IsCarried(jug, buyer) && jug.DeliveryBuildingId is null &&
        !HasActiveContainerReservation(society.Checkpoint.Inventory, jug.Id) &&
        society.Checkpoint.Inventory.Lots.Where(lot => lot.ContainerLotId == jug.Id).All(lot => lot.ItemKind == "milk" && lot.FreshnessBasisPoints > 0) &&
        ContainerContentsQuantity(society.Checkpoint.Inventory, jug.Id) < InventoryContainerRules.WaterJugCapacity;
    private bool ValidMilkOffer(MilkSaleOffer offer)
    {
        var inventory = society.Checkpoint.Inventory;
        return WorldTick - offer.OfferedTick < 120 && inventory.Lots.FirstOrDefault(lot => lot.Id == offer.MilkLotId) is { } milk &&
            milk.FreshnessBasisPoints > 0 && MilkSaleSite(offer.SellerId, milk, out var building, out var position) && building == offer.BuildingId &&
            position == offer.Position && AdultResident(offer.BuyerId) && HouseholdFor(offer.BuyerId) != HouseholdFor(offer.SellerId) &&
            IsWithinInteractionRange(inhabitants[offer.SellerId].Position, position, 1) && IsWithinInteractionRange(inhabitants[offer.BuyerId].Position, position, 1) &&
            inventory.Lots.Any(jug => jug.Id == offer.ReceivingJugId && CanReceiveTradedMilk(offer.BuyerId, jug)) &&
            inventory.Lots.Any(lot => lot.Id == offer.PaymentLotId && lot.OwnerId == offer.BuyerId && PersonalEquipmentRules.IsCarried(lot, offer.BuyerId) &&
                lot.ContainerLotId is null && !InventoryContainerRules.IsContainer(lot.ItemKind) && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
                !PersonalEquipmentRules.IsSelected(inhabitants[offer.BuyerId].Equipment, lot.Id)) &&
            inventory.Reservations.Any(held => held.Id == offer.Id + "-milk" && held.LotId == milk.Id && held.OwnerId == milk.OwnerId &&
                held.Quantity == 1 && held.IsExclusive && held.State == InventoryReservationState.Reserved && held.Purpose == "milk-sale:" + offer.Id);
    }
    private void ReconcileMilkOffers()
    {
        foreach (var offer in animalWorld.MilkOffers.Where(offer => !ValidMilkOffer(offer)).ToArray()) CloseMilkOffer(offer, "cancelled");
    }
    private void CloseMilkOffer(MilkSaleOffer offer, string reason)
    {
        if (society.Checkpoint.Inventory.Reservations.Any(held => held.Id == offer.Id + "-milk" && held.State == InventoryReservationState.Reserved))
            ApplyInventoryTransition(inventory => InventoryFixture.ReleaseReservation(inventory, offer.Id + "-milk", reason));
        animalWorld = animalWorld with { MilkOffers = animalWorld.MilkOffers.Where(item => item.Id != offer.Id).ToArray() };
        AppendEvent("milk_sale_" + reason, offer.Id);
    }
    private void AddMilkCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var jug in SpoiledMilkJugs(actor).Take(4)) candidates.Add(new("animal:empty_milk:" + AnimalKey(jug.Id),
            "Pour away spoiled milk at its actual jug; keep the reusable jug and any fresh milk.", 21));
        foreach (var stock in MilkStockChoices(actor).Take(8)) candidates.Add(new(stock.Id,
            "Carry the actual household milk jug to the Store or borrowed Market stall; its owner stays the same.", 36, stock.BuildingId));
        foreach (var sale in MilkSaleChoices(actor).Take(12)) candidates.Add(new(sale.Id,
            $"Offer one milk to {society.Checkpoint.GetInhabitant(sale.Buyer).Name} for one {society.Checkpoint.Inventory.GetLot(sale.Payment).ItemKind.Replace('_', ' ')} from their named carried lot; pour it into their jug only if they accept.", 190, sale.Building));
        foreach (var offer in animalWorld.MilkOffers.Where(offer => offer.BuyerId == actor && ValidMilkOffer(offer)))
        {
            candidates.Add(new("animal:milk_accept:" + offer.Id, "Accept the exact offered milk and payment; receive one milk in your carried jug while both jugs keep their owners.", 190, offer.BuildingId));
            candidates.Add(new("animal:milk_decline:" + offer.Id, "Decline the offered milk; no goods change owners.", 190));
        }
    }
    private bool ApplyMilkSaleDecision(SocietyCognitionDispatchResult decision)
    {
        var selected = decision.Admission.Intention;
        if (selected is null || !(selected.CandidateId.StartsWith("animal:milk_offer:", StringComparison.Ordinal) ||
            selected.CandidateId.StartsWith("animal:milk_accept:", StringComparison.Ordinal) || selected.CandidateId.StartsWith("animal:milk_decline:", StringComparison.Ordinal))) return false;
        if (!decision.Admission.Accepted || decision.Admission.FellBack || selected.Provider != DecisionProviderKind.LargeLanguageModel ||
            selected.InhabitantId != decision.InhabitantId || PendingInstructionFor(decision.InhabitantId)?.Kind == OwnerInstructionKind.MustDo) return true;
        var actor = decision.InhabitantId;
        if (MilkSaleChoices(actor).FirstOrDefault(choice => choice.Id == selected.CandidateId) is { } sale)
        {
            var offer = new MilkSaleOffer("milk-sale-" + AnimalKey(sale.Id + ":" + WorldTick + ":" + nextEventId), actor, sale.Buyer,
                sale.Milk, sale.Jug, sale.Payment, sale.Building, sale.Position, WorldTick);
            var milk = society.Checkpoint.Inventory.GetLot(sale.Milk);
            ApplyInventoryTransition(inventory => InventoryFixture.Reserve(inventory, offer.Id + "-milk", milk.OwnerId,
                milk.Id, 1, "milk-sale:" + offer.Id, WorldTick + 120));
            animalWorld = animalWorld with { MilkOffers = animalWorld.MilkOffers.Append(offer).OrderBy(offer => offer.Id, StringComparer.Ordinal).ToArray() };
            AppendEvent("milk_sale_offered", offer.Id, offer.Position);
        }
        else if (animalWorld.MilkOffers.FirstOrDefault(offer => offer.BuyerId == actor && ValidMilkOffer(offer) &&
                     (selected.CandidateId == "animal:milk_accept:" + offer.Id || selected.CandidateId == "animal:milk_decline:" + offer.Id)) is { } offer)
        {
            if (selected.CandidateId.StartsWith("animal:milk_accept:", StringComparison.Ordinal))
            {
                var payment = society.Checkpoint.Inventory.GetLot(offer.PaymentLotId);
                var paymentId = payment.Quantity == 1 ? payment.Id : payment.Id + "#transfer:" + offer.Id + "-payment";
                ApplyInventoryTransition(inventory => InventoryFixture.ExchangeMilk(inventory, offer.Id,
                    offer.Id + "-milk", offer.ReceivingJugId, actor, offer.PaymentLotId, new(offer.Position.X, offer.Position.Y)));
                RecordMilkMarketPayment(offer, paymentId);
                CloseMilkOffer(offer, "completed");
            }
            else CloseMilkOffer(offer, "declined");
        }
        return true;
    }

    private void RecordMilkMarketPayment(MilkSaleOffer offer, string paymentId)
    {
        foreach (var town in towns)
            foreach (var market in town.Markets)
            {
                var occupancy = market.Occupancies.FirstOrDefault(item => item.EndedTick is null &&
                    item.SellerAgentId == offer.SellerId && item.StallBuildingId == offer.BuildingId);
                if (occupancy is null) continue;
                var payment = society.Checkpoint.Inventory.GetLot(paymentId);
                var receiptId = MarketTradeRules.ReceiptId(occupancy.Id, payment.Id, payment.OwnerId, 1, WorldTick, market.StockReceipts.Count);
                ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory, receiptId + ":deposit", payment.Id,
                    payment.OwnerId, 1, groundPosition: new(offer.Position.X, offer.Position.Y)));
                var receipt = new MarketStockReceipt(receiptId, occupancy.Id, offer.SellerId, payment.OwnerId, payment.Id,
                    payment.Id, payment.ItemKind, 1, WorldTick, society.Checkpoint.Inventory.Events[^1].EventId);
                SetMarket(town.Id, market with { StockReceipts = market.StockReceipts.Append(receipt).ToArray() });
                return;
            }
    }
}
