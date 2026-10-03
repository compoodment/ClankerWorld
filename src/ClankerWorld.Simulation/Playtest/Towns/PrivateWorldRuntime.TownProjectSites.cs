using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private IEnumerable<GridPoint> TownProjectFootprintTiles(string? exceptProjectId = null) => towns
        .SelectMany(t => t.Projects)
        .Where(p => p.Id != exceptProjectId && p.Stage is not ("completed" or "cancelled"))
        .SelectMany(p => WorldContentSimulationRules.Footprint(TownHallContent.Hall3x4(), p.Plan.Site));

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
        if (!worldContent.Buildings.Any(d => d.CanonicalId == plan.DefinitionId))
            return "The Town Hall content is no longer available.";
        var hall = TownHallContent.Hall3x4();
        var footprint = WorldContentSimulationRules.Footprint(hall, plan.Site).ToArray();
        var legal = TownProjectLandTiles(town);
        if (footprint.Any(p => !legal.Contains(p)))
            return "The full Hall site needs uncontested Town title without a household right or pending land request.";
        if (!CanPlaceBuilding(hall, plan.Site, out var failure, projectId)) return failure;
        if (plan.Entrance != TownHallContent.Entrance(plan.Site) || !map.IsBuildable(plan.Entrance) ||
            map.Resources.Any(r => r.Position == plan.Entrance) || map.CampObjects.Any(c => c.Position == plan.Entrance) ||
            fields.Any(f => f.Position == plan.Entrance) ||
            worldSimulation.Buildings.Any(b => WorldContentSimulationRules.Footprint(
                worldContent.Buildings.Single(d => d.CanonicalId == b.DefinitionId), b).Contains(plan.Entrance)) ||
            (worldSimulation.BuildingExpansions ?? []).Any(j => j.State is WorldProductionJobState.Running or WorldProductionJobState.Paused &&
                ExpansionTiles(j).Contains(plan.Entrance)) || TownProjectFootprintTiles(projectId).Contains(plan.Entrance))
            return "The Hall's south doorway is blocked.";
        if (actor is not null && inhabitants.TryGetValue(actor, out var person) &&
            person.Position != plan.Site && FindUnoccupiedRoute(actor, person.Position, plan.Site, 0).Count == 0)
            return "No usable foot route reaches the approved Hall site.";
        return null;
    }

    private void AddTownProjectProposalCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        var hall = TownHallContent.Hall3x4();
        if (!worldContent.Buildings.Any(d => d.CanonicalId == hall.CanonicalId)) return;
        var layout = CreateTownLayoutContext(actor, building: hall, forTownProject: true);
        foreach (var site in TownLayoutService.RankConstructionSites(layout, hall))
        {
            var coordinates = site.Position.X.ToString(CultureInfo.InvariantCulture) + "," + site.Position.Y.ToString(CultureInfo.InvariantCulture);
            candidates.Add(new(CivicAction(town.Id, "project", hall.LocalId, coordinates),
                $"Propose a named Town Hall at ({coordinates}) in {town.Name}, with a provisional budget of 24 wood and 12 stone; put its name in civic_proposal. Council approval creates no goods or private-stock access.", 191));
        }
    }

    private TownProjectPayload? OfferedTownProjectPlan(TownRuntimeState town, string actor, string definition,
        string coordinates, string? name)
    {
        var point = coordinates.Split(',');
        if (definition != TownHallContent.Hall3x4().LocalId || point.Length != 2 ||
            !int.TryParse(point[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(point[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y)) return null;
        var label = string.IsNullOrWhiteSpace(name) ? "Town Hall" : name.Trim();
        var hall = TownHallContent.Hall3x4();
        var plan = new TownProjectPayload(label, hall.CanonicalId, new(x, y),
            TownHallContent.Entrance(new(x, y)), hall.BuildCosts);
        try { TownProjectRules.ValidatePayload(plan); }
        catch (InvalidDataException) { return null; }
        return TownProjectSiteFailure(town, plan, actor: actor) is null ? plan : null;
    }
}
