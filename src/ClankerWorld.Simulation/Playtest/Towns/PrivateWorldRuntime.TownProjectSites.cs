using System.Globalization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private IEnumerable<GridPoint> TownProjectFootprintTiles(string? exceptProjectId = null) => towns
        .SelectMany(t => t.Projects)
        .Where(p => p.Id != exceptProjectId && p.Stage is not ("completed" or "cancelled"))
        .SelectMany(p => WorldContentSimulationRules.Footprint(
            TownProjectRules.Definition(p.Plan.DefinitionId) ?? throw new InvalidDataException("An active Town project has unsupported content."), p.Plan.Site));

    private IEnumerable<GridPoint> TownProjectProtectedSites(string? exceptProjectId = null) =>
        TownProjectFootprintTiles(exceptProjectId).Concat(towns.SelectMany(town => town.Projects)
            .Where(project => project.Id != exceptProjectId && project.Stage is not ("completed" or "cancelled"))
            .Select(project => project.Plan.Entrance));

    private HashSet<GridPoint> TownProjectLandTiles(TownRuntimeState town)
    {
        var claimed = householdLandUseRights.SelectMany(r => r.Tiles)
            .Concat(householdLandUseRequests.SelectMany(r => r.Tiles))
            .Concat(townLandTitles.Where(t => t.TownId != town.Id).SelectMany(t => t.Tiles)).ToHashSet();
        return townLandTitles.Where(t => t.TownId == town.Id).SelectMany(t => t.Tiles)
            .Where(p => !claimed.Contains(p)).ToHashSet();
    }

    private string? TownProjectSiteFailure(TownRuntimeState town, TownProjectPayload plan,
        string? projectId = null, string? actor = null)
    {
        var definition = TownProjectRules.Definition(plan.DefinitionId);
        var lantern = StreetLanternContent.IsLantern(plan.DefinitionId);
        if (definition is null || !worldContent.Buildings.Any(d => d.CanonicalId == plan.DefinitionId))
            return lantern ? "The street lantern content is no longer available." : "The Town Hall content is no longer available.";
        var footprint = WorldContentSimulationRules.Footprint(definition, plan.Site).ToArray();
        var legal = TownProjectLandTiles(town);
        if (footprint.Any(p => !legal.Contains(p)))
            return lantern ? "The lantern post needs uncontested Town title without a household right or pending land request." :
                "The full Hall site needs uncontested Town title without a household right or pending land request.";
        if (lantern && (!StreetLanternContent.IsRoadEdge(plan.Site, plan.Entrance) ||
            !roadTiles.Contains(plan.Entrance) || !legal.Contains(plan.Entrance)))
            return "The lantern must stand beside an actual Road on uncontested Town-titled land.";
        if (!CanPlaceBuilding(definition, plan.Site, out var failure, projectId)) return failure;
        if ((!lantern && plan.Entrance != TownHallContent.Entrance(plan.Site)) || !map.IsBuildable(plan.Entrance) ||
            map.Resources.Any(r => r.Position == plan.Entrance) || map.CampObjects.Any(c => c.Position == plan.Entrance) ||
            fields.Any(f => f.Position == plan.Entrance) ||
            worldSimulation.Buildings.Any(b => WorldContentSimulationRules.Footprint(
                worldContent.Buildings.Single(d => d.CanonicalId == b.DefinitionId), b).Contains(plan.Entrance)) ||
            (worldSimulation.BuildingExpansions ?? []).Any(j => j.State is WorldProductionJobState.Running or WorldProductionJobState.Paused &&
                ExpansionTiles(j).Contains(plan.Entrance)) || TownProjectFootprintTiles(projectId).Contains(plan.Entrance))
            return lantern ? "The lantern's Road edge is blocked." : "The Hall's south doorway is blocked.";
        if (actor is not null && inhabitants.TryGetValue(actor, out var person) &&
            person.Position != plan.Site && FindUnoccupiedRoute(actor, person.Position, plan.Site, 0).Count == 0)
            return lantern ? "No usable foot route reaches the approved lantern post." : "No usable foot route reaches the approved Hall site.";
        return null;
    }

    private void AddTownProjectProposalCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        var hall = TownHallContent.Hall3x4();
        if (worldContent.Buildings.Any(d => d.CanonicalId == hall.CanonicalId))
        {
            var layout = CreateTownLayoutContext(actor, building: hall, forTownProject: true);
            foreach (var site in TownLayoutService.RankConstructionSites(layout, hall))
            {
                var coordinates = site.Position.X.ToString(CultureInfo.InvariantCulture) + "," + site.Position.Y.ToString(CultureInfo.InvariantCulture);
                candidates.Add(new(CivicAction(town.Id, "project", hall.LocalId, coordinates),
                    $"Propose a named Town Hall at ({coordinates}) in {town.Name}, with a provisional budget of 24 wood and 12 stone; put its name in civic_proposal. Council approval creates no goods or private-stock access.", 191));
            }
        }
        AddStreetLanternProposalCandidates(candidates, actor, town, StreetLanternContent.Stone());
        AddStreetLanternProposalCandidates(candidates, actor, town, StreetLanternContent.Hanging());
    }

    private void AddStreetLanternProposalCandidates(List<CognitionCandidate> candidates, string actor,
        TownRuntimeState town, BuildingDefinition definition)
    {
        if (!worldContent.Buildings.Any(item => item.CanonicalId == definition.CanonicalId)) return;
        var ordinary = CreateTownLayoutContext(actor, building: definition);
        var legal = TownProjectLandTiles(town);
        var layout = new TownLayoutContext(map, town, ordinary.OccupiedTiles, ordinary.ReachableFootCosts,
            ordinary.Resources, ordinary.Buildings, roadTiles: roadTiles, requiredLandTiles: legal);
        var edges = new List<(TownConstructionSiteCandidate Site, GridPoint Road)>();
        foreach (var road in roadTiles.Where(legal.Contains).OrderBy(point => point.Y).ThenBy(point => point.X))
            foreach (var post in map.FootNeighbors(road).Where(point => StreetLanternContent.IsRoadEdge(point, road)))
                if (TownLayoutService.TryEvaluateConstructionSite(layout, definition, post, out var site) && site is not null &&
                    TownProjectSiteFailure(town, new(definition.DisplayName, definition.CanonicalId, post, road, definition.BuildCosts)) is null)
                    edges.Add((site, road));
        foreach (var edge in edges.OrderByDescending(edge => edge.Site.Score)
                     .ThenBy(edge => edge.Site.TownBorderGrowthTiles).ThenBy(edge => edge.Site.RouteCost)
                     .ThenBy(edge => edge.Site.Position.Y).ThenBy(edge => edge.Site.Position.X)
                     .ThenBy(edge => edge.Road.Y).ThenBy(edge => edge.Road.X).Take(4))
        {
            var coordinates = FormattableString.Invariant($"{edge.Site.Position.X},{edge.Site.Position.Y},{edge.Road.X},{edge.Road.Y}");
            var budget = definition.CanonicalId == StreetLanternContent.Stone().CanonicalId ? "4 stone" : "4 wood and 1 refined iron";
            candidates.Add(new(CivicAction(town.Id, "project", definition.LocalId, coordinates),
                FormattableString.Invariant($"Propose a named {definition.DisplayName} at ({edge.Site.Position.X},{edge.Site.Position.Y}) beside Road ({edge.Road.X},{edge.Road.Y}) in {town.Name}, with a provisional budget of {budget}; put its name in civic_proposal. Council approval creates no goods or private-stock access."), 190));
        }
    }

    private TownProjectPayload? OfferedTownProjectPlan(TownRuntimeState town, string actor, string definition,
        string coordinates, string? name)
    {
        var hall = TownHallContent.Hall3x4();
        var stone = StreetLanternContent.Stone();
        var hanging = StreetLanternContent.Hanging();
        var building = definition == hall.LocalId ? hall : definition == stone.LocalId ? stone :
            definition == hanging.LocalId ? hanging : null;
        var lantern = building is not null && StreetLanternContent.IsLantern(building.CanonicalId);
        var point = coordinates.Split(',');
        if (building is null || point.Length != (lantern ? 4 : 2) ||
            !int.TryParse(point[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(point[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y)) return null;
        var entrance = TownHallContent.Entrance(new(x, y));
        if (lantern)
        {
            if (!int.TryParse(point[2], NumberStyles.None, CultureInfo.InvariantCulture, out var roadX) ||
                !int.TryParse(point[3], NumberStyles.None, CultureInfo.InvariantCulture, out var roadY)) return null;
            entrance = new(roadX, roadY);
        }
        var label = string.IsNullOrWhiteSpace(name) ? building.DisplayName : name.Trim();
        var plan = new TownProjectPayload(label, building.CanonicalId, new(x, y), entrance, building.BuildCosts);
        try { TownProjectRules.ValidatePayload(plan); }
        catch (InvalidDataException) { return null; }
        return TownProjectSiteFailure(town, plan, actor: actor) is null ? plan : null;
    }
}
