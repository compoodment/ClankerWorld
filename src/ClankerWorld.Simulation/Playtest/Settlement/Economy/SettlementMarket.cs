using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private IEnumerable<GridPoint> MarketPlotTiles(MarketPlot plot) =>
        Enumerable.Range(0, BusinessContent.PlotHeight).SelectMany(y =>
            Enumerable.Range(0, BusinessContent.PlotWidth).Select(x =>
                new GridPoint(plot.Position.X + x, plot.Position.Y + y)));

    private IEnumerable<GridPoint> MarketReservedTiles() => businessTrade.Markets.SelectMany(MarketPlotTiles);

    public IReadOnlyList<GridPoint> MarketStallPositions(string marketId) =>
        businessTrade.Markets.FirstOrDefault(plot => plot.MarketId == marketId) is { } plot
            ? Enumerable.Range(0, BusinessContent.PlotHeight / 2).SelectMany(y =>
                Enumerable.Range(0, BusinessContent.PlotWidth / 2).Select(x =>
                    new GridPoint(plot.Position.X + x * 2, plot.Position.Y + y * 2))).ToArray()
            : [];

    private bool TryFindMarketPlot(GridPoint position, out GridPoint plotPosition)
    {
        var occupied = map.Resources.Select(item => item.Position).Concat(map.CampObjects.Select(item => item.Position))
            .Concat(RoadAndBridgeTiles()).Concat(MarketReservedTiles()).Concat(LooseStockTiles()).Concat(
                FarmFields.Select(field => field.Position)).Concat((worldSimulation.BuildingExpansions ?? [])
                .Where(job => job.State == WorldProductionJobState.Running).SelectMany(ExpansionTiles)).Concat(
                worldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                    worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)))
            .Concat(WorldContentSimulationRules.Footprint(BusinessContent.Market(), position)).ToHashSet();
        return BusinessMarketLayout.TryFindPlot(map, occupied, position, out plotPosition);
    }

    private void RegisterMarket(PlacedBuilding building, GridPoint plotPosition)
    {
        var plot = new MarketPlot(building.InstanceId, building.TownId!, plotPosition);
        businessTrade = businessTrade with { Markets = businessTrade.Markets.Append(plot).ToArray() };
        towns = towns.Select(town => town.Id == building.TownId
            ? town with { BorderTiles = TownBorderRules.Expand(map, town, MarketPlotTiles(plot)) }
            : town).ToList();
        AppendEvent("market_plot_reserved", $"{building.InstanceId}|{plotPosition.X}|{plotPosition.Y}|10|12");
    }

    public BusinessActionResult DeliverToMarketStall(string actor, string marketId, string lotId, int quantity) =>
        BusinessAction(() => DeliverToMarketStallCore(actor, marketId, lotId, quantity));

    private BusinessActionResult DeliverToMarketStallCore(string actor, string marketId, string lotId, int quantity)
    {
        if (!AdultResident(actor) || HouseholdFor(actor) is not { } householdId ||
            businessTrade.Markets.FirstOrDefault(item => item.MarketId == marketId) is not { } plot)
            return new(false, Failure: "An adult household member needs a completed Market.");
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId);
        if (lot is null || lot.OwnerId != actor || lot.StorageBuildingId is not null || lot.DeliveryBuildingId is not null ||
            !BusinessLotCanMove(lot) || quantity <= 0 || AvailableLotQuantity(lot) < quantity)
            return new(false, Failure: "Bring the actual uncommitted goods personally.");
        var held = businessTrade.Stalls.FirstOrDefault(item => item.MarketId == marketId && item.HouseholdId == householdId);
        var stall = held is null ? null : worldSimulation.Buildings.Single(item => item.InstanceId == held.BuildingId);
        var position = inhabitants[actor].Position;
        if (stall is not null && position != stall.Position || stall is null &&
            (!MarketStallPositions(marketId).Contains(position) || LooseStockTiles().Contains(position) ||
                worldSimulation.Buildings.Any(item => item.Position == position)))
            return new(false, Failure: "Reach the household stall, or a free marked stall with the goods, to reserve it.");
        var id = stall?.InstanceId ?? marketId + "-stall-" + position.X + "-" + position.Y;
        var load = InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, quantity);
        if (stall is null ? load > 64 : BusinessStorageRoom(id) < load)
            return new(false, Failure: "The stall lacks space for the complete load.");
        var transferred = InventoryFixture.Transfer(society.Checkpoint.Inventory,
            $"market-delivery:{WorldTick}:{actor}:{businessTrade.NextSequence}", actor, householdId,
            lot.Id, quantity, "market_stock_delivered", id);
        // Its structure was funded with the Market; reserving it never grants goods.
        if (stall is null)
        {
            stall = new PlacedBuilding(id, BusinessContent.Stall().CanonicalId, position, WorldTick, plot.TownId, householdId);
            worldSimulation = worldSimulation with
            {
                Buildings = worldSimulation.Buildings.Append(stall)
                .OrderBy(item => item.InstanceId, StringComparer.Ordinal).ToArray()
            };
            businessTrade = businessTrade with { Stalls = businessTrade.Stalls.Append(new(marketId, id, householdId)).ToArray() };
            AssignBuildingToTown(stall, BusinessContent.Stall());
        }
        ApplyInventoryTransition(_ => transferred);
        businessTrade = businessTrade with { NextSequence = businessTrade.NextSequence + 1 };
        AppendEvent("market_stall_stocked", $"{actor}|{marketId}|{id}|{lot.ItemKind}|{quantity}");
        return new(true, id);
    }

    public BusinessActionResult WithdrawBusinessStock(string actor, string buildingId, string lotId, int quantity) =>
        BusinessAction(() => WithdrawBusinessStockCore(actor, buildingId, lotId, quantity));

    private BusinessActionResult WithdrawBusinessStockCore(string actor, string buildingId, string lotId, int quantity,
        string? deliveryBuildingId = null)
    {
        var site = BusinessSite(buildingId);
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId);
        if (site?.HouseholdId is not { } householdId || !AdultResident(actor) || HouseholdFor(actor) != householdId ||
            inhabitants[actor].Position != site.Position || lot is null || lot.OwnerId != householdId ||
            lot.StorageBuildingId != buildingId || !BusinessLotCanMove(lot) || quantity <= 0 ||
            AvailableLotQuantity(lot) < quantity || BusinessCarryingRoom(actor) <
            InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, quantity))
            return new(false, Failure: "A holding household adult at the business needs uncommitted stock and room to carry it.");
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"business-withdrawal:{WorldTick}:{actor}:{businessTrade.NextSequence}", householdId,
            actor, lot.Id, quantity, "business_stock_withdrawn", destinationDeliveryBuildingId: deliveryBuildingId));
        businessTrade = businessTrade with
        {
            NextSequence = businessTrade.NextSequence + 1,
            Listings = businessTrade.Listings.Where(listing => listing.GoodsLotId != lot.Id).ToArray()
        };
        MaintainMarketStalls();
        AppendEvent("business_stock_withdrawn", $"{actor}|{buildingId}|{lot.ItemKind}|{quantity}");
        return new(true, buildingId);
    }

    private void MaintainMarketStalls()
    {
        foreach (var stall in businessTrade.Stalls.Where(stall => !society.Checkpoint.Inventory.Lots.Any(lot =>
                     lot.OwnerId == stall.HouseholdId && lot.StorageBuildingId == stall.BuildingId && lot.Quantity > 0)).ToArray())
        {
            foreach (var offer in businessTrade.Offers.Where(offer => offer.BuildingId == stall.BuildingId &&
                         offer.State == BusinessOfferState.Open).ToArray())
                CancelBusinessOfferCore(offer.SellerId, offer.Id, "The stall is empty.");
            worldSimulation = worldSimulation with { Buildings = worldSimulation.Buildings.Where(item => item.InstanceId != stall.BuildingId).ToArray() };
            businessTrade = businessTrade with
            {
                Stalls = businessTrade.Stalls.Where(item => item.BuildingId != stall.BuildingId).ToArray(),
                Listings = businessTrade.Listings.Where(item => item.BuildingId != stall.BuildingId).ToArray(),
            };
            towns = towns.Select(town => town with
            {
                AssignedBuildingIds = town.AssignedBuildingIds.Where(id => id != stall.BuildingId).ToArray(),
            }).ToList();
            AppendEvent("market_stall_released", $"{stall.HouseholdId}|{stall.BuildingId}");
        }
    }

    private void AddBusinessBuildingPlans(List<CognitionCandidate> candidates, string actor)
    {
        if (TownForResident(actor) is not { } townId || !AdultResident(actor) ||
            worldSimulation.Buildings.Any(building => building.TownId == townId &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                    definition.Tags.Contains("market", StringComparer.Ordinal))) ||
            inhabitants.Values.Any(person => person.Project is { Stage: not ("completed" or "cancelled") } project &&
                TownForResident(person.InhabitantId) == townId &&
                TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection) &&
                selection.DefinitionId == BusinessContent.Market().CanonicalId) ||
            !worldContent.Buildings.Any(definition => definition.CanonicalId == BusinessContent.Market().CanonicalId))
            return;
        var definition = BusinessContent.Market();
        var owner = HouseholdFor(actor) ?? actor;
        if (!CanAcquireProjectInputs(definition.BuildCosts, owner, actor)) return;
        foreach (var site in TownLayoutService.RankConstructionSites(CreateTownLayoutContext(actor), definition)
                     .Where(site => TryFindMarketPlot(site.Position, out _)))
            candidates.Add(new(TownConstructionCandidateIds.Building(definition.CanonicalId, site.Position),
                "Build the Town Market with a clear 10 by 12 plot for household stalls.", 42,
                $"build-site:{site.Position.X},{site.Position.Y}"));
    }
}
