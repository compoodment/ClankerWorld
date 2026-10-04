using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void CompletePaidMarketConstruction(string townId, TownConstructionProject project, PlacedBuilding placed)
    {
        var town = towns.Single(item => item.Id == townId);
        if (project.Plan.DefinitionId == MarketContent.Hall2x2().CanonicalId)
        {
            var definition = MarketContent.Stall1x1();
            var stalls = MarketContent.StarterSlotIndexes.Select(slot => new MarketStallState(
                MarketContent.StarterStallBuildingId(project.Id, slot), slot, project.Id, WorldTick)).ToArray();
            var buildings = stalls.Select(stall => new PlacedBuilding(stall.BuildingId, definition.CanonicalId,
                MarketContent.StallSite(project.Plan.Site, stall.SlotIndex), WorldTick, townId,
                Entrance: MarketContent.StallEntrance(project.Plan.Site, stall.SlotIndex))).ToArray();
            if (buildings.Any(building => worldSimulation.Buildings.Any(existing => existing.InstanceId == building.InstanceId)))
                throw new InvalidDataException("A paid Market starter stall already exists.");
            worldSimulation = worldSimulation with
            {
                Buildings = worldSimulation.Buildings.Concat(buildings).OrderBy(building => building.InstanceId, StringComparer.Ordinal).ToArray(),
            };
            SetTown(town with
            {
                Markets = town.Markets.Append(new TownMarketState(MarketContent.MarketId(project.Id), project.Id,
                    placed.InstanceId, project.Plan.Site, stalls, [], [])).OrderBy(market => market.Id, StringComparer.Ordinal).ToArray(),
            });
            foreach (var building in buildings) AssignBuildingToTown(building, definition);
            AppendEvent("market_built", $"{townId}|{MarketContent.MarketId(project.Id)}|{placed.InstanceId}|{stalls.Length}", project.Plan.Site);
        }
        else if (project.Plan.DefinitionId == MarketContent.Stall1x1().CanonicalId)
        {
            var target = MarketForStallPlan(town, project.Plan)
                ?? throw new InvalidDataException("The paid stall lost its approved Market slot.");
            var market = target.Market with
            {
                Stalls = target.Market.Stalls.Append(new MarketStallState(placed.InstanceId, target.Slot, project.Id, WorldTick))
                    .OrderBy(stall => stall.SlotIndex).ToArray(),
            };
            SetTown(town with { Markets = town.Markets.Select(item => item.Id == market.Id ? market : item).ToArray() });
            AppendEvent("market_stall_built", $"{townId}|{market.Id}|{placed.InstanceId}|{target.Slot}", placed.Position);
        }
    }

    private void RecordMarketBuildingRemoval(string buildingId)
    {
        var removed = false;
        foreach (var town in towns.ToArray())
        {
            var changed = town.Markets.Any(market => market.HallBuildingId == buildingId || market.Stalls.Any(stall => stall.BuildingId == buildingId));
            if (!changed) continue;
            removed = true;
            SetTown(town with
            {
                Markets = town.Markets.Select(market => market with
                {
                    RemovedTick = market.HallBuildingId == buildingId ? WorldTick : market.RemovedTick,
                    Stalls = market.Stalls.Select(stall => stall.BuildingId == buildingId ? stall with { RemovedTick = WorldTick } : stall).ToArray(),
                }).ToArray(),
            });
        }
        MaintainMarkets();
        // A stall project of a removed Market has lost its site, and the world may be saved before the next tick.
        if (removed) MaintainTownProjects();
    }

    private static void ValidatePaidMarkets(IReadOnlyList<TownRuntimeState> towns, SocietyCheckpoint society,
        SeededMap map, WorldContentSimulationState simulation, DeclarativeWorldContentState content,
        IReadOnlyList<FarmFieldState> fields, IReadOnlyList<HouseholdLandUseRight> rights,
        IReadOnlyList<HouseholdLandUseRequest> requests, IReadOnlyList<GridPoint> roads, IReadOnlyList<BridgeState> bridges)
    {
        var marketIds = new HashSet<string>(StringComparer.Ordinal);
        var stallIds = new HashSet<string>(StringComparer.Ordinal);
        var stallProjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var town in towns)
        {
            if (town.Markets is null) throw new InvalidDataException("A Town is missing its Market ledger.");
            foreach (var market in town.Markets)
            {
                if (market is null) throw new InvalidDataException("A Market ledger entry is missing.");
                var project = town.Projects.SingleOrDefault(item => item.Id == market.ProjectId);
                if (!marketIds.Add(market.Id) || project is not { Stage: "completed" } ||
                    project.Plan.DefinitionId != MarketContent.Hall2x2().CanonicalId ||
                    market.Id != MarketContent.MarketId(project.Id) || market.HallBuildingId != project.CompletedBuildingId ||
                    market.Site != project.Plan.Site || market.RemovedTick != project.RemovedTick || market.Stalls is null ||
                    market.Stalls.Any(stall => stall is null) ||
                    market.Stalls.Count is < 2 or > MarketContent.MaximumStalls ||
                    market.Stalls.Select(stall => stall.SlotIndex).Distinct().Count() != market.Stalls.Count ||
                    !content.Buildings.Any(definition => definition.CanonicalId == MarketContent.Hall2x2().CanonicalId) ||
                    !content.Buildings.Any(definition => definition.CanonicalId == MarketContent.Stall1x1().CanonicalId) ||
                    MarketContent.SiteTiles(market.Site).Any(point => !map.Contains(point) || !map.IsBuildable(point)))
                    throw new InvalidDataException("A Market disagrees with its completed paid Council project.");
                foreach (var stall in market.Stalls)
                {
                    if (stall is null || !stallIds.Add(stall.BuildingId) || stall.SlotIndex is < 0 or >= MarketContent.MaximumStalls ||
                        stall.BuiltTick < project.LastTransitionTick || stall.BuiltTick > society.WorldTick ||
                        stall.RemovedTick is { } removed && (removed < stall.BuiltTick || removed > society.WorldTick))
                        throw new InvalidDataException("Market stall identities, slots or construction history are invalid.");
                    if (stall.ProjectId == project.Id)
                    {
                        if (!MarketContent.StarterSlotIndexes.Contains(stall.SlotIndex) ||
                            stall.BuildingId != MarketContent.StarterStallBuildingId(project.Id, stall.SlotIndex) ||
                            stall.BuiltTick != project.LastTransitionTick)
                            throw new InvalidDataException("A starter stall is not paid by its Market's exact approved budget.");
                    }
                    else
                    {
                        var paid = town.Projects.SingleOrDefault(item => item.Id == stall.ProjectId);
                        if (!stallProjects.Add(stall.ProjectId) || paid is not { Stage: "completed" } ||
                            paid.Plan.DefinitionId != MarketContent.Stall1x1().CanonicalId || paid.CompletedBuildingId != stall.BuildingId ||
                            paid.Plan.Site != MarketContent.StallSite(market.Site, stall.SlotIndex) ||
                            paid.Plan.Entrance != MarketContent.StallEntrance(market.Site, stall.SlotIndex) ||
                            paid.LastTransitionTick != stall.BuiltTick || paid.RemovedTick != stall.RemovedTick ||
                            market.RemovedTick is { } ended && stall.BuiltTick > ended)
                            throw new InvalidDataException("An additional stall lacks its own exact paid Council project.");
                    }
                    var placed = simulation.Buildings.FirstOrDefault(building => building.InstanceId == stall.BuildingId);
                    if (stall.RemovedTick is not null && placed is not null || stall.RemovedTick is null &&
                        (placed is null || placed.DefinitionId != MarketContent.Stall1x1().CanonicalId || placed.TownId != town.Id ||
                         placed.HouseholdId is not null || placed.Position != MarketContent.StallSite(market.Site, stall.SlotIndex) ||
                         placed.Entrance != MarketContent.StallEntrance(market.Site, stall.SlotIndex) || placed.PlacedTick != stall.BuiltTick))
                        throw new InvalidDataException("A Market's placed stall disagrees with its paid history or retained removal.");
                }
                if (market.RemovedTick is null)
                {
                    var site = MarketContent.SiteTiles(market.Site).ToHashSet();
                    var ownBuildings = market.Stalls.Select(stall => stall.BuildingId).Append(market.HallBuildingId)
                        .ToHashSet(StringComparer.Ordinal);
                    var occupied = simulation.Buildings.Where(building => !ownBuildings.Contains(building.InstanceId))
                        .SelectMany(building => WorldContentSimulationRules.Footprint(
                            content.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
                        .Concat(fields.Select(field => field.Position))
                        .Concat(map.Resources.Select(resource => resource.Position))
                        .Concat(map.CampObjects.Select(item => item.Position))
                        .Concat(rights.SelectMany(right => right.Tiles))
                        .Concat(requests.Where(request => request.Status == "pending").SelectMany(request => request.Tiles))
                        .Concat((simulation.BuildingExpansions ?? []).Where(job => job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused)
                            .SelectMany(job => Enumerable.Range(0, job.TargetFootprint.Height).SelectMany(y =>
                                Enumerable.Range(0, job.TargetFootprint.Width).Select(x => new GridPoint(job.TargetPosition.X + x, job.TargetPosition.Y + y)))))
                        .Concat(bridges.SelectMany(bridge => bridge.Entrances))
                        .Concat(towns.SelectMany(other => other.Projects.Select(project => (Town: other, Project: project)))
                            .Where(item => item.Project.Stage is not ("completed" or "cancelled") &&
                                !(item.Town.Id == town.Id && item.Project.Plan.DefinitionId == MarketContent.Stall1x1().CanonicalId &&
                                  Enumerable.Range(0, MarketContent.MaximumStalls).Any(slot =>
                                      MarketContent.StallSite(market.Site, slot) == item.Project.Plan.Site)))
                            .SelectMany(item => TownProjectRules.Footprint(item.Project.Plan).Append(item.Project.Plan.Entrance)));
                    var builtOrReserved = WorldContentSimulationRules.Footprint(MarketContent.Hall2x2(), market.Site)
                        .Concat(Enumerable.Range(0, MarketContent.MaximumStalls).Select(slot => MarketContent.StallSite(market.Site, slot)))
                        .ToHashSet();
                    if (occupied.Any(site.Contains) || roads.Any(builtOrReserved.Contains))
                        throw new InvalidDataException("A standing Market plaza must retain its reserved slots and clear aisles.");
                }
                if (MarketContent.StarterSlotIndexes.Any(slot => !market.Stalls.Any(stall => stall.ProjectId == project.Id && stall.SlotIndex == slot)))
                    throw new InvalidDataException("The Market is missing an approved starter stall.");
            }
            if (town.Markets.Where(market => market.RemovedTick is null).SelectMany(market => MarketContent.SiteTiles(market.Site))
                .GroupBy(tile => tile).Any(tiles => tiles.Count() > 1))
                throw new InvalidDataException("Two standing Markets in one Town cannot share ground.");
            if (town.Projects.Where(project => project.Stage == "completed" && project.Plan.DefinitionId == MarketContent.Hall2x2().CanonicalId)
                    .Any(project => town.Markets.Count(market => market.ProjectId == project.Id) != 1) ||
                town.Projects.Where(project => project.Stage == "completed" && project.Plan.DefinitionId == MarketContent.Stall1x1().CanonicalId)
                    .Any(project => !stallProjects.Contains(project.Id)))
                throw new InvalidDataException("A completed Market or stall project is missing its retained Market binding.");
        }
        if (simulation.Buildings.Any(building => building.DefinitionId == MarketContent.Stall1x1().CanonicalId && !stallIds.Contains(building.InstanceId)))
            throw new InvalidDataException("A Market stall has no paid construction ledger.");
    }
}
