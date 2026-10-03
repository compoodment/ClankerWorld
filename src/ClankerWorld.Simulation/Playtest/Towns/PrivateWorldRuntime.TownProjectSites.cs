using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private IEnumerable<GridPoint> TownProjectFootprintTiles(string? exceptProjectId = null) => towns
        .SelectMany(t => t.Projects)
        .Where(p => p.Id != exceptProjectId && p.Stage is not ("completed" or "cancelled"))
        .SelectMany(p => TownProjectRules.Footprint(p.Plan));

    private IEnumerable<GridPoint> TownProjectProtectedSites(string? exceptProjectId = null) =>
        TownProjectFootprintTiles(exceptProjectId).Concat(towns.SelectMany(town => town.Projects)
            .Where(project => project.Id != exceptProjectId && project.Stage is not ("completed" or "cancelled"))
            .Select(project => project.Plan.Entrance));

    // Proposed project sites, so a second proposal or a household request cannot overlap one while its vote is open.
    private HashSet<GridPoint> PendingTownProjectSiteTiles() => towns
        .SelectMany(town => town.Governance?.Proposals ?? [])
        .Where(proposal => proposal is { Kind: "project", Status: "pending", Project: not null })
        .SelectMany(proposal => TownProjectRules.Footprint(proposal.Project!).Append(proposal.Project!.Entrance))
        .ToHashSet();

    private HashSet<GridPoint> TownProjectLandTiles(TownRuntimeState town, bool ignorePendingRequests = false)
    {
        var claimed = householdLandUseRights.SelectMany(r => r.Tiles)
            .Concat(householdLandUseRequests.Where(r => r.Status == "pending" && !ignorePendingRequests).SelectMany(r => r.Tiles))
            .Concat(townLandTitles.Where(t => t.TownId != town.Id).SelectMany(t => t.Tiles)).ToHashSet();
        return townLandTitles.Where(t => t.TownId == town.Id).SelectMany(t => t.Tiles)
            .Where(p => !claimed.Contains(p)).ToHashSet();
    }

    private string? TownProjectSiteFailure(TownRuntimeState town, TownProjectPayload plan,
        string? projectId = null, string? actor = null, bool ignorePendingRequests = false)
    {
        var definition = TownProjectRules.DefinitionFor(plan.DefinitionId);
        if (definition is null || !worldContent.Buildings.Any(d => d.CanonicalId == plan.DefinitionId))
            return "The Town building content is no longer available.";
        var footprint = TownProjectRules.Footprint(plan).ToArray();
        var legal = TownProjectLandTiles(town, ignorePendingRequests);
        if (footprint.Any(p => !legal.Contains(p)))
            return "The full project site needs uncontested Town title without a household right or pending land request.";
        if (projectId is null && footprint.Append(plan.Entrance).Any(PendingTownProjectSiteTiles().Contains))
            return "Another proposed Town project already uses part of this site.";
        if (definition.Tags.Contains(MarketContent.StallTag, StringComparer.Ordinal) && MarketForStallPlan(town, plan) is null)
            return "An additional stall needs an unused slot in this Town's paid, usable Market.";
        if (definition.Tags.Contains(MarketContent.HallTag, StringComparer.Ordinal) && town.Markets
                .Where(market => market.RemovedTick is null).Any(market => MarketContent.SiteTiles(market.Site).Intersect(footprint).Any()))
            return "The new Market site overlaps an existing Market.";
        if (!CanPlaceBuilding(definition, plan.Site, out var failure, projectId)) return failure;
        var occupied = map.Resources.Select(resource => resource.Position).Concat(map.CampObjects.Select(item => item.Position))
            .Concat(fields.Select(field => field.Position))
            .Concat(worldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building)))
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused)
                .SelectMany(ExpansionTiles)).Concat(TownProjectFootprintTiles(projectId)).ToHashSet();
        if (footprint.Any(point => !map.IsBuildable(point) || occupied.Contains(point)))
            return "The approved building and its plaza need clear buildable ground.";
        if (definition.Tags.Contains(MarketContent.HallTag, StringComparer.Ordinal) &&
            Enumerable.Range(0, MarketContent.MaximumStalls).Any(slot => RoadTiles.Contains(MarketContent.StallSite(plan.Site, slot))))
            return "The Market's stall layout crosses an existing Road.";
        if (!map.IsBuildable(plan.Entrance) ||
            map.Resources.Any(r => r.Position == plan.Entrance) || map.CampObjects.Any(c => c.Position == plan.Entrance) ||
            fields.Any(f => f.Position == plan.Entrance) ||
            worldSimulation.Buildings.Any(b => WorldContentSimulationRules.Footprint(
                worldContent.Buildings.Single(d => d.CanonicalId == b.DefinitionId), b).Contains(plan.Entrance)) ||
            (worldSimulation.BuildingExpansions ?? []).Any(j => j.State is WorldProductionJobState.Running or WorldProductionJobState.Paused &&
                ExpansionTiles(j).Contains(plan.Entrance)) || TownProjectFootprintTiles(projectId).Contains(plan.Entrance))
            return "The approved building's doorway is blocked.";
        if (actor is not null && inhabitants.TryGetValue(actor, out var person) &&
            person.Position != plan.Site && FindUnoccupiedRoute(actor, person.Position, plan.Site, 0).Count == 0)
            return "No usable foot route reaches the approved construction site.";
        return null;
    }

    private static (TownMarketState Market, int Slot)? MarketForStallPlan(TownRuntimeState town, TownProjectPayload plan)
    {
        foreach (var market in town.Markets.Where(item => item.RemovedTick is null))
            for (var slot = 0; slot < MarketContent.MaximumStalls; slot++)
                if (MarketContent.StallSite(market.Site, slot) == plan.Site &&
                    MarketContent.StallEntrance(market.Site, slot) == plan.Entrance &&
                    !market.Stalls.Any(stall => stall.SlotIndex == slot))
                    return (market, slot);
        return null;
    }

    private void AddTownProjectProposalCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        foreach (var definition in TownProjectRules.Definitions.Where(item => !item.Tags.Contains(MarketContent.StallTag, StringComparer.Ordinal)))
        {
            if (!worldContent.Buildings.Any(item => item.CanonicalId == definition.CanonicalId)) continue;
            var layout = CreateTownLayoutContext(actor, building: definition, forTownProject: true);
            var offered = 0;
            foreach (var site in TownLayoutService.RankConstructionSites(layout, definition, TownLayoutService.MaximumCandidateLimit))
            {
                var plan = PlanFor(town, definition, site.Position, definition.DisplayName);
                // The site check also refuses a site that overlaps another proposal whose vote is still open.
                if (plan is null || TownProjectSiteFailure(town, plan, actor: actor) is not null) continue;
                AddTownProjectProposalCandidate(candidates, town, definition, site.Position);
                if (++offered == TownLayoutService.DefaultCandidateLimit) break;
            }
        }
        var stallDefinition = MarketContent.Stall1x1();
        if (!worldContent.Buildings.Any(item => item.CanonicalId == stallDefinition.CanonicalId)) return;
        foreach (var market in town.Markets.Where(item => item.RemovedTick is null && MarketNeedsMoreStalls(item)))
            for (var slot = 0; slot < MarketContent.MaximumStalls; slot++)
            {
                if (market.Stalls.Any(stall => stall.SlotIndex == slot)) continue;
                var site = MarketContent.StallSite(market.Site, slot);
                var plan = PlanFor(town, stallDefinition, site, "Market stall");
                if (plan is not null && TownProjectSiteFailure(town, plan, actor: actor) is null)
                    AddTownProjectProposalCandidate(candidates, town, stallDefinition, site);
            }
    }

    private static bool MarketNeedsMoreStalls(TownMarketState market) => market.Stalls.Where(stall => stall.RemovedTick is null).All(stall =>
        market.Occupancies.Any(occupancy => occupancy.StallBuildingId == stall.BuildingId && occupancy.EndedTick is null));

    private void AddTownProjectProposalCandidate(List<CognitionCandidate> candidates, TownRuntimeState town,
        ClankerWorld.Simulation.Content.BuildingDefinition definition, GridPoint site)
    {
        var coordinates = site.X.ToString(CultureInfo.InvariantCulture) + "," + site.Y.ToString(CultureInfo.InvariantCulture);
        var budget = string.Join(" and ", definition.BuildCosts.Select(cost =>
            cost.Amount.ToString(CultureInfo.InvariantCulture) + " " + cost.ResourceId));
        candidates.Add(new(CivicAction(town.Id, "project", definition.LocalId, coordinates),
            $"Propose a named {definition.DisplayName} at ({coordinates}) in {town.Name}, with a provisional budget of {budget}; put its name in civic_proposal. Council approval creates no goods or private-stock access.", 191));
    }

    private static TownProjectPayload? PlanFor(TownRuntimeState town, ClankerWorld.Simulation.Content.BuildingDefinition definition,
        GridPoint site, string name)
    {
        var entrance = definition.Tags.Contains(TownHallContent.HallTag, StringComparer.Ordinal)
            ? TownHallContent.Entrance(site) : definition.Tags.Contains(MarketContent.HallTag, StringComparer.Ordinal)
                ? MarketContent.HallEntrance(site) : Enumerable.Range(0, MarketContent.MaximumStalls)
                    .SelectMany(slot => town.Markets.Where(market => market.RemovedTick is null &&
                        MarketContent.StallSite(market.Site, slot) == site && !market.Stalls.Any(stall => stall.SlotIndex == slot))
                        .Select(market => (GridPoint?)MarketContent.StallEntrance(market.Site, slot))).SingleOrDefault();
        return entrance is { } door ? new(name, definition.CanonicalId, site, door, definition.BuildCosts) : null;
    }

    private TownProjectPayload? OfferedTownProjectPlan(TownRuntimeState town, string actor, string definition,
        string coordinates, string? name)
    {
        var point = coordinates.Split(',');
        var supported = TownProjectRules.Definitions.SingleOrDefault(item => item.LocalId == definition);
        if (supported is null || point.Length != 2 ||
            !int.TryParse(point[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(point[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y)) return null;
        var label = string.IsNullOrWhiteSpace(name) ? supported.DisplayName : name.Trim();
        var plan = PlanFor(town, supported, new(x, y), label);
        if (plan is null) return null;
        try { TownProjectRules.ValidatePayload(plan); }
        catch (InvalidDataException) { return null; }
        return TownProjectSiteFailure(town, plan, actor: actor) is null ? plan : null;
    }
}
