using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateBusinessTrade(PrivateWorldRuntimeState state)
    {
        if (state.BusinessTrade is not { } business) return;
        if (business.NextSequence <= 0 || business.Listings is null || business.Offers is null ||
            business.Markets is null || business.Stalls is null || business.ToolOrders is null)
            throw new InvalidDataException("Business trade has invalid collections or sequence.");
        if (state.SchemaVersion < 32 && (business.NextSequence != 1 || business.Listings.Count > 0 ||
            business.Offers.Count > 0 || business.Markets.Count > 0 || business.Stalls.Count > 0 || business.ToolOrders.Count > 0))
            throw new InvalidDataException("Physical business trade requires private-world schema 32.");
        var inventory = state.Society.Society.Inventory;
        var buildings = state.WorldSimulation!.Buildings.ToDictionary(building => building.InstanceId, StringComparer.Ordinal);
        var definitions = state.WorldContent!.Buildings.ToDictionary(definition => definition.CanonicalId, StringComparer.Ordinal);
        var people = state.Inhabitants.ToDictionary(person => person.InhabitantId, StringComparer.Ordinal);
        var identities = state.Society.Society.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var ids = business.Listings.Select(item => item.Id).Concat(business.Offers.Select(item => item.Id))
            .Concat(business.ToolOrders.Select(item => item.Id)).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(id =>
                string.IsNullOrWhiteSpace(id) || !long.TryParse(id[(id.LastIndexOf(':') + 1)..], out var sequence) ||
                sequence <= 0 || sequence >= business.NextSequence))
            throw new InvalidDataException("Business identities must be unique allocated sequence values.");
        foreach (var listing in business.Listings)
            if (listing.GoodsQuantity <= 0 || listing.PaymentQuantity <= 0 || listing.ExpiryTick < 0 ||
                string.IsNullOrWhiteSpace(listing.PaymentKind) ||
                !buildings.TryGetValue(listing.BuildingId, out var site) || site.HouseholdId != listing.HouseholdId ||
                !identities.TryGetValue(listing.SellerId, out var seller) || seller.HouseholdId != listing.HouseholdId ||
                seller.Status != SocietyInhabitantStatus.Active || seller.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
                !inventory.Lots.Any(lot => lot.Id == listing.GoodsLotId && lot.OwnerId == listing.HouseholdId &&
                    lot.StorageBuildingId == listing.BuildingId && lot.ContainerLotId is null && lot.GroundPosition is null &&
                    lot.Quantity >= listing.GoodsQuantity && lot.ItemKind != listing.PaymentKind &&
                    BusinessCatalogAccepts(definitions[site.DefinitionId], lot.ItemKind)))
                throw new InvalidDataException("A business listing must identify its real on-site household stock and terms.");
        foreach (var offer in business.Offers)
        {
            if (!Enum.IsDefined(offer.State) || offer.GoodsQuantity <= 0 || offer.PaymentQuantity <= 0 ||
                offer.ExpiryTick < 0 || offer.ReservedCarrySpace < 0 || offer.ReservedStorageSpace < 0 ||
                string.IsNullOrWhiteSpace(offer.GoodsKind) || string.IsNullOrWhiteSpace(offer.PaymentKind) ||
                offer.GoodsKind == offer.PaymentKind || offer.SellerId == offer.BuyerId)
                throw new InvalidDataException("A business offer has invalid exact terms.");
            if (offer.State != BusinessOfferState.Open) continue;
            if (!buildings.TryGetValue(offer.BuildingId, out var site) || site.HouseholdId != offer.HouseholdId ||
                !identities.TryGetValue(offer.SellerId, out var seller) || seller.HouseholdId != offer.HouseholdId ||
                seller.Status != SocietyInhabitantStatus.Active || seller.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
                !identities.TryGetValue(offer.BuyerId, out var buyer) || buyer.HouseholdId == offer.HouseholdId ||
                buyer.Status != SocietyInhabitantStatus.Active || buyer.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
                !people.ContainsKey(offer.SellerId) || !people.ContainsKey(offer.BuyerId) ||
                !inventory.Lots.Any(lot => lot.Id == offer.GoodsLotId && lot.OwnerId == offer.HouseholdId &&
                    lot.StorageBuildingId == offer.BuildingId && lot.ItemKind == offer.GoodsKind &&
                    lot.ContainerLotId is null && lot.GroundPosition is null && lot.Quantity >= offer.GoodsQuantity &&
                    BusinessCatalogAccepts(definitions[site.DefinitionId], lot.ItemKind)) ||
                !inventory.Lots.Any(lot => lot.Id == offer.PaymentLotId && lot.OwnerId == offer.BuyerId &&
                    lot.StorageBuildingId is null && lot.DeliveryBuildingId is null &&
                    lot.ItemKind == offer.PaymentKind && lot.ContainerLotId is null && lot.GroundPosition is null &&
                    lot.Quantity >= offer.PaymentQuantity) ||
                !ExactBusinessReservation(inventory, offer.Id + ":goods", offer.HouseholdId, offer.GoodsLotId, offer.GoodsQuantity, offer.ExpiryTick) ||
                !ExactBusinessReservation(inventory, offer.Id + ":payment", offer.BuyerId, offer.PaymentLotId, offer.PaymentQuantity, offer.ExpiryTick))
                throw new InvalidDataException("An open business offer must retain both exact lots, locations and reservations.");
            var goodsLoad = InventoryFixture.TransferLoadQuantity(inventory, offer.GoodsLotId, offer.GoodsQuantity);
            var paymentLoad = InventoryFixture.TransferLoadQuantity(inventory, offer.PaymentLotId, offer.PaymentQuantity);
            if (offer.ReservedCarrySpace != Math.Max(0, goodsLoad - paymentLoad) ||
                offer.ReservedStorageSpace != Math.Max(0, paymentLoad - goodsLoad))
                throw new InvalidDataException("An offer's receiving space must match its complete physical transfer load.");
            var contents = offer.Contents ?? [];
            var actualContentIds = inventory.Lots.Where(lot => lot.ContainerLotId == offer.GoodsLotId || lot.ContainerLotId == offer.PaymentLotId)
                .Select(lot => lot.Id).Order(StringComparer.Ordinal);
            if (!actualContentIds.SequenceEqual(contents.Select(content => content.LotId).Order(StringComparer.Ordinal)))
                throw new InvalidDataException("The exact contents of traded vessels must be committed with their vessels.");
            foreach (var content in contents)
                if (!inventory.Lots.Any(lot => lot.Id == content.LotId && lot.ContainerLotId == content.ContainerId &&
                        lot.ItemKind == content.ItemKind && lot.OwnerId == content.OwnerId && lot.Quantity == content.Quantity) ||
                    !ExactBusinessReservation(inventory, content.ReservationId, content.OwnerId, content.LotId, content.Quantity, offer.ExpiryTick))
                    throw new InvalidDataException("A vessel exchange lost its exact contents reservation.");
        }
        var openOffers = business.Offers.Where(offer => offer.State == BusinessOfferState.Open).ToArray();
        if (openOffers.Select(offer => offer.BuyerId).Distinct(StringComparer.Ordinal).Count() != openOffers.Length)
            throw new InvalidDataException("A customer can commit to one business exchange at a time.");
        foreach (var offers in openOffers.GroupBy(offer => offer.BuyerId))
            if (offers.Sum(offer => (long)offer.ReservedCarrySpace) >
                CarryEquipmentRules.Room(inventory, people[offers.Key]))
                throw new InvalidDataException("Business offers overbook personally carried receiving space.");
        foreach (var offers in openOffers.GroupBy(offer => offer.BuildingId))
        {
            var building = buildings[offers.Key];
            if (BuildingStorageRules.Capacity(definitions[building.DefinitionId], building) is not { } capacity) continue;
            var stock = inventory.Lots.Where(lot => lot.StorageBuildingId == offers.Key).Sum(lot => (long)lot.Quantity);
            var productionSpace = state.WorldSimulation.ProductionJobs.Where(job => job.BuildingInstanceId == offers.Key &&
                job.State == WorldProductionJobState.Running).Sum(job =>
            {
                var recipe = state.WorldContent.Recipes.Single(recipe => recipe.CanonicalId == job.RecipeId);
                var freeing = job.InputReservationIds.Select(inventory.GetReservation).Where(reservation =>
                    inventory.GetLot(reservation.LotId).StorageBuildingId == offers.Key).Sum(reservation => reservation.Quantity);
                return Math.Max(0, recipe.Outputs.Sum(output => output.Amount) - freeing);
            });
            if (offers.Sum(offer => (long)offer.ReservedStorageSpace) > Math.Max(0, capacity - stock - productionSpace))
                throw new InvalidDataException("Business offers overbook their transaction building's receiving space.");
        }
        if (business.Markets.Select(plot => plot.MarketId).Distinct(StringComparer.Ordinal).Count() != business.Markets.Count ||
            business.Stalls.Select(stall => stall.BuildingId).Distinct(StringComparer.Ordinal).Count() != business.Stalls.Count ||
            business.Stalls.Select(stall => (stall.MarketId, stall.HouseholdId)).Distinct().Count() != business.Stalls.Count)
            throw new InvalidDataException("Markets and held stalls must have unique identities.");
        var reservedPlotTiles = new HashSet<GridPoint>();
        foreach (var plot in business.Markets)
        {
            if (!buildings.TryGetValue(plot.MarketId, out var market) || market.TownId != plot.TownId ||
                !definitions[market.DefinitionId].Tags.Contains("market", StringComparer.Ordinal))
                throw new InvalidDataException("A stall plot requires its actual shared Market.");
            foreach (var point in Enumerable.Range(0, BusinessContent.PlotHeight).SelectMany(y =>
                         Enumerable.Range(0, BusinessContent.PlotWidth).Select(x => new GridPoint(plot.Position.X + x, plot.Position.Y + y))))
            {
                if (!state.Map.IsBuildable(point) || !reservedPlotTiles.Add(point) ||
                    state.Map.Resources.Any(resource => resource.Position == point) ||
                    state.Map.CampObjects.Any(item => item.Position == point) ||
                    (state.RoadTiles ?? []).Contains(point) && (point.X - plot.Position.X) % 2 == 0 &&
                        (point.Y - plot.Position.Y) % 2 == 0 ||
                    (state.WorldSimulation.Fields ?? []).Any(field => field.Position == point) ||
                    (state.WorldSimulation.BuildingExpansions ?? []).Any(job => job.State == WorldProductionJobState.Running &&
                        point.X >= job.TargetPosition.X && point.Y >= job.TargetPosition.Y &&
                        point.X < job.TargetPosition.X + job.TargetFootprint.Width &&
                        point.Y < job.TargetPosition.Y + job.TargetFootprint.Height) ||
                    buildings.Values.Any(building => WorldContentSimulationRules.Footprint(
                        BuildingStorageRules.EffectiveDefinition(definitions[building.DefinitionId], building), building.Position).Contains(point) &&
                        !business.Stalls.Any(stall => stall.BuildingId == building.InstanceId && stall.MarketId == plot.MarketId)))
                    throw new InvalidDataException("The reserved Market plot must remain clear except for its own stalls.");
            }
        }
        foreach (var stall in business.Stalls)
        {
            var plot = business.Markets.SingleOrDefault(plot => plot.MarketId == stall.MarketId);
            if (plot is null || !buildings.TryGetValue(stall.BuildingId, out var building) ||
                building.HouseholdId != stall.HouseholdId || building.TownId != plot.TownId ||
                !definitions[building.DefinitionId].Tags.Contains(BusinessContent.StallKind, StringComparer.Ordinal) ||
                building.Position.X < plot.Position.X || building.Position.Y < plot.Position.Y ||
                building.Position.X >= plot.Position.X + BusinessContent.PlotWidth ||
                building.Position.Y >= plot.Position.Y + BusinessContent.PlotHeight ||
                (building.Position.X - plot.Position.X) % 2 != 0 || (building.Position.Y - plot.Position.Y) % 2 != 0)
                throw new InvalidDataException("A held household stall must occupy one of the Market's marked 1 by 1 sites.");
        }
        foreach (var order in business.ToolOrders)
            if (order.State is not ("queued" or "running" or "ready" or "completed" or "cancelled") ||
                order.ExpiryTick < 0 || string.IsNullOrWhiteSpace(order.ToolKind) ||
                order.State is "queued" or "running" or "ready" &&
                (!buildings.TryGetValue(order.BuildingId, out var site) || site.HouseholdId != order.HouseholdId ||
                 !people.ContainsKey(order.BuyerId) || !state.WorldContent.Recipes.Any(recipe => recipe.CanonicalId == order.RecipeId &&
                     recipe.WorkstationBuildingId == site.DefinitionId && recipe.Outputs.Any(output => output.ResourceId == order.ToolKind))))
                throw new InvalidDataException("A tool request must retain its customer, actual business and supported recipe.");
    }

    private static bool ExactBusinessReservation(InventoryCheckpoint inventory, string id, string owner, string lot, int quantity, long expiry) =>
        inventory.Reservations.Any(reservation => reservation.Id == id && reservation.OwnerId == owner &&
            reservation.LotId == lot && reservation.Quantity == quantity && reservation.ExpiryTick == expiry &&
            reservation.Purpose == "business_exchange" && reservation.State == InventoryReservationState.Reserved);
}
