using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private BoatTransportState boatTransport = BoatTransportState.Empty();
    public IReadOnlyList<BoatState> Boats => boatTransport.Boats;
    public IReadOnlyList<BoatTripRequest> BoatRequests => boatTransport.Requests;

    private PlacedBuilding? Port(string id) => worldSimulation.Buildings.FirstOrDefault(building =>
        building.InstanceId == id && worldContent.Buildings.Any(definition =>
            definition.CanonicalId == building.DefinitionId && PortNavigationRules.IsPort(definition)));

    private PortGeometry PortGeometryFor(PlacedBuilding port) => PortNavigationRules.Geometry(map,
        worldContent.Buildings.Single(definition => definition.CanonicalId == port.DefinitionId), port.Position);

    private HashSet<GridPoint> PortObstacles(string? exceptProjectId = null, string? exceptPortId = null, bool protectDocks = false) =>
        map.Resources.Select(resource => resource.Position).Concat(map.CampObjects.Select(item => item.Position))
            .Concat(fields.Select(field => field.Position)).Concat(RoadAndBridgeTiles())
            .Concat(worldSimulation.Buildings.Where(building => building.InstanceId != exceptPortId)
                .SelectMany(building => protectDocks
                    ? PortNavigationRules.ProtectedBuildingTiles(map, worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)
                    : WorldContentSimulationRules.Footprint(worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)))
            .Concat((worldSimulation.BuildingExpansions ?? [])
                .Where(job => job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused)
                .SelectMany(ExpansionTiles)).Concat(TownProjectProtectedSites(exceptProjectId)).ToHashSet();

    private bool PortIsLegal(PlacedBuilding port) => port.TownId is { } townId &&
        towns.SingleOrDefault(town => town.Id == townId) is { } town && port.Entrance is { } approach &&
        PortGeometryFor(port).LandTiles.Append(approach).All(TownProjectLandTiles(town).Contains) &&
        (!PortObstacles(exceptPortId: port.InstanceId, protectDocks: true).Contains(approach) || roadTiles.Contains(approach)) &&
        PortNavigationRules.Fits(map, worldContent.Buildings.Single(item => item.CanonicalId == port.DefinitionId),
            port.Position, PortObstacles(exceptPortId: port.InstanceId, protectDocks: true), out _, roadTiles);

    private string? PortProjectSiteFailure(TownRuntimeState town, TownProjectPayload plan,
        BuildingDefinition definition, string? projectId, string? actor, bool ignorePendingRequests)
    {
        var geometry = PortNavigationRules.Geometry(map, definition, plan.Site);
        var legal = TownProjectLandTiles(town, ignorePendingRequests);
        if (geometry.LandTiles.Any(point => !legal.Contains(point)) || !legal.Contains(plan.Entrance))
            return "The Port's land end and approach need uncontested Town title.";
        if (plan.BoatPortId is { } portId)
        {
            var port = Port(portId);
            if (port is null || port.TownId != town.Id || port.DefinitionId != plan.DefinitionId ||
                port.Position != plan.Site || port.Entrance != plan.Entrance || !PortIsLegal(port))
                return "The boat needs its approved, usable Town Port.";
            if (FreePortDock(port) is null) return "The Port has no free docking space for the finished boat.";
        }
        else
        {
            if (projectId is null && geometry.LandTiles.Concat(geometry.WaterTiles).Concat(geometry.DockingTiles)
                    .Append(plan.Entrance).Any(PendingTownProjectSiteTiles().Contains))
                return "Another proposed Town project already uses the Port site or docking space.";
            var occupied = PortObstacles(projectId, protectDocks: true);
            if (!PortNavigationRules.Fits(map, definition, plan.Site, occupied, out var failure, roadTiles)) return failure;
            if (occupied.Contains(plan.Entrance) && !roadTiles.Contains(plan.Entrance))
                return "The approved Port's land approach is blocked.";
        }
        var work = TownProjectRules.WorkSite(plan);
        if (actor is not null && inhabitants.TryGetValue(actor, out var person) &&
            person.Position != work && FindUnoccupiedRoute(actor, person.Position, work, 0).Count == 0)
            return "No usable foot route reaches the Port's land end.";
        return null;
    }

    private IEnumerable<GridPoint> FreePortDocks(PlacedBuilding port, string? exceptBoatId = null) =>
        PortGeometryFor(port).DockingTiles.Where(dock => !boatTransport.Boats.Any(boat => boat.Id != exceptBoatId &&
            (boat.Position == dock || boat.Journey?.ReservedDock == dock)));

    private GridPoint? FreePortDock(PlacedBuilding port, string? exceptBoatId = null) =>
        FreePortDocks(port, exceptBoatId).Cast<GridPoint?>().FirstOrDefault();

    private void AddBoatAccessCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        if (!worldSimulation.Buildings.Any(building => building.TownId == town.Id && Port(building.InstanceId) is not null)) return;
        if (town.Government?.Laws.Any(law => TownLawRules.IsInForce(law) && TownLawRules.Current(law).BoatAccess is { VisitorId: null }) != true)
            candidates.Add(new(CivicAction(town.Id, "boat_access", "all", ""),
                "Ask the Council to adopt standing permission for all visitors to use the Town's communal boats; this grants no ownership or membership.", 193));
        foreach (var visitor in inhabitants.Keys.Where(id => !town.ResidentIds.Contains(id, StringComparer.Ordinal) &&
                     IsWithinInteractionRange(inhabitants[id].Position, inhabitants[actor].Position, ResourceInteractionRange) &&
                     !TownBoatAccessRules.Allows(town, id, WorldTick)).Order(StringComparer.Ordinal).Take(8))
            candidates.Add(new(CivicAction(town.Id, "boat_access", visitor, ""),
                $"Ask the Council to allow {society.Checkpoint.GetInhabitant(visitor).Name} to use the Town's communal boats; this grants no ownership or membership.", 194));
    }

    private void AddPortProjectProposalCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        var ports = worldSimulation.Buildings.Where(building => building.TownId == town.Id && Port(building.InstanceId) is not null).ToArray();
        var legal = TownProjectLandTiles(town);
        var occupied = PortObstacles(protectDocks: true);
        foreach (var definition in PortContent.Definitions.Where(definition => worldContent.Buildings.Any(d => d.CanonicalId == definition.CanonicalId)))
        {
            var offered = 0;
            foreach (var land in legal.OrderBy(point => point.Y).ThenBy(point => point.X))
            {
                var site = PortNavigationRules.Facing(definition) switch
                {
                    PortFacing.North => new GridPoint(land.X, land.Y - 3),
                    PortFacing.West => new GridPoint(land.X - 3, land.Y),
                    _ => land,
                };
                if (!PortNavigationRules.Fits(map, definition, site, occupied, out _, roadTiles)) continue;
                var geometry = PortNavigationRules.Geometry(map, definition, site);
                if (geometry.LandTiles.Any(point => !legal.Contains(point)) || !geometry.ApproachTiles.Any(legal.Contains)) continue;
                var plan = PlanFor(town, definition, site, definition.DisplayName);
                if (plan is null || TownProjectSiteFailure(town, plan, actor: actor) is not null) continue;
                AddTownProjectProposalCandidate(candidates, town, definition, site);
                if (++offered == 2) break;
            }
        }
        foreach (var port in ports.Where(PortIsLegal).OrderBy(port => port.InstanceId, StringComparer.Ordinal))
        {
            if (FreePortDock(port) is null || town.Projects.Any(project => IsLiveTownProject(project) && project.Plan.BoatPortId == port.InstanceId) ||
                (town.Governance?.Proposals ?? []).Any(proposal => proposal.Status == "pending" && proposal.Project?.BoatPortId == port.InstanceId)) continue;
            var plan = BoatProjectPlan(port, "Communal boat");
            if (TownProjectSiteFailure(town, plan, actor: actor) is not null) continue;
            candidates.Add(new(CivicAction(town.Id, "boat_project", port.InstanceId, ""),
                $"Propose building a communal boat at this Town's Port, with 8 wood, 2 rope and 2 refined iron; put its name in civic_proposal. Council approval creates no goods.", 192));
        }
    }

    private static TownProjectPayload BoatProjectPlan(PlacedBuilding port, string name) =>
        new(name, port.DefinitionId, port.Position, port.Entrance!.Value, PortContent.BoatCosts) { BoatPortId = port.InstanceId };

    private void CompletePaidBoatProject(string townId, TownConstructionProject project)
    {
        var port = Port(project.Plan.BoatPortId!)!;
        var dock = FreePortDock(port) ?? throw new InvalidOperationException("The completed boat has no reserved launch space.");
        var id = TownProjectRules.BuildingId(project.Id);
        SetBoat(new(id, townId, project.Id, dock, port.InstanceId));
        SetTownProject(townId, project with
        {
            Stage = "completed",
            CompletedBoatId = id,
            Blocker = null,
            LastTransitionTick = WorldTick,
        });
        AppendEvent("communal_boat_launched", $"{townId}:{id}:{port.InstanceId}:{project.Plan.Name}", dock);
    }

    private void SetBoat(BoatState boat) => boatTransport = boatTransport with
    {
        Boats = boatTransport.Boats.Where(item => item.Id != boat.Id).Append(boat).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
    };
}
