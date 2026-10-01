using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// A household plans only the buildings it needs for itself: one of each
/// household kind it does not hold, and only once the materials are in hand.
/// Buildings the Town shares wait for governance and are never offered here.
/// </summary>
public sealed partial class PrivateWorldRuntime
{
    private const string GatherBuildingMaterialPrefix = "gather_building_material:";

    private void AddHouseholdBuildingPlans(List<CognitionCandidate> candidates, SocietyInhabitant inhabitant,
        PlaytestInhabitantState state, string householdId)
    {
        TownLayoutContext? sharedLayout = null;
        foreach (var definition in PlannableHouseholdBuildings(householdId))
        {
            if (NeedsUrgentWarmth(state) && !definition.Tags.Any(tag => tag is "shelter" or "warmth" or "cooking"))
                continue;
            if (worldSimulation.Buildings.Any(item => item.InstanceId == BuildInstanceId(inhabitant.Id, definition)) ||
                !HouseholdHasMaterialsInHand(householdId, definition.BuildCosts))
                continue;

            var layout = HouseholdBuildingKind(definition) == "silo"
                ? CreateTownLayoutContext(inhabitant.Id, building: definition)
                : sharedLayout ??= CreateTownLayoutContext(inhabitant.Id);
            var sites = TownLayoutService.RankConstructionSites(layout, definition);
            for (var rank = 0; rank < sites.Count; rank++)
            {
                var site = sites[rank];
                var description = string.Join(" ", site.Reasons.Select(reason => reason.Description));
                candidates.Add(new CognitionCandidate(
                    TownConstructionCandidateIds.Building(definition.CanonicalId, site.Position),
                    $"Plan {definition.DisplayName} at ({site.Position.X}, {site.Position.Y}): {description}",
                    20 + rank,
                    $"build-site:{site.Position.X},{site.Position.Y}"));
            }
        }

        AddBuildingMaterialCandidate(candidates, inhabitant.Id, householdId);
    }

    /// <summary>Active building designs this household may plan now, in a stable order.</summary>
    private IEnumerable<BuildingDefinition> PlannableHouseholdBuildings(string householdId) => worldContent.Buildings
        .Where(definition => !RetiredBuildings.Contains(definition) &&
            HouseholdBuildingKind(definition) is { } kind && HouseholdMayPlan(householdId, kind))
        .OrderBy(definition => HouseholdBuildingKinds.PlanOrder(HouseholdBuildingKind(definition)))
        .ThenBy(definition => definition.CanonicalId, StringComparer.Ordinal);

    private bool HouseholdMayPlan(string householdId, string kind) =>
        HouseholdBuildingWithTag(householdId, kind) is null &&
        (kind != "silo" || FarmhouseForHousehold(householdId) is not null) &&
        !HouseholdBuildingProjectInProgress(householdId, kind);

    /// <summary>Whether a member already has a live plan for this kind, so the household plans at most one.</summary>
    private bool HouseholdBuildingProjectInProgress(string householdId, string? kind = null) =>
        inhabitants.Values.Any(person =>
            person.Project is { Stage: not ("completed" or "cancelled") } project &&
            TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection) && selection.IsBuilding &&
            society.Checkpoint.GetInhabitant(person.InhabitantId).HouseholdId == householdId &&
            worldContent.Buildings.FirstOrDefault(definition => definition.CanonicalId == selection.DefinitionId) is { } planned &&
            HouseholdBuildingKind(planned) is { } plannedKind && (kind is null || plannedKind == kind));

    /// <summary>Materials the household owns, wherever it stores them, plus what its members carry.</summary>
    private bool HouseholdHasMaterialsInHand(string householdId, IReadOnlyList<ContentQuantity> costs) =>
        costs.All(cost => HouseholdMaterialInHand(householdId, cost.ResourceId) >= cost.Amount);

    private long HouseholdMaterialInHand(string householdId, string itemKind) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == itemKind &&
                (lot.OwnerId == householdId ||
                 inhabitants.ContainsKey(lot.OwnerId) && HouseholdFor(lot.OwnerId) == householdId))
            .Sum(lot => (long)AvailableLotQuantity(lot));

    /// <summary>
    /// The first material missing for the first building the household still
    /// needs, when it can be gathered from a reachable source. One such offer
    /// at a time keeps the choice list short.
    /// </summary>
    private (BuildingDefinition Building, ContentQuantity Material)? NeededBuildingMaterial(string actor, string householdId)
    {
        if (HouseholdBuildingProjectInProgress(householdId))
            return null;
        foreach (var definition in PlannableHouseholdBuildings(householdId))
        {
            var missing = definition.BuildCosts.FirstOrDefault(cost =>
                HouseholdMaterialInHand(householdId, cost.ResourceId) < cost.Amount);
            if (missing.Amount == 0)
                continue;
            if (MaterialSource(missing.ResourceId, actor) is not null)
                return (definition, missing);
        }
        return null;
    }

    private void AddBuildingMaterialCandidate(List<CognitionCandidate> candidates, string actor, string householdId)
    {
        if (NeededBuildingMaterial(actor, householdId) is not { } need)
            return;
        candidates.Add(new CognitionCandidate(GatherBuildingMaterialPrefix + need.Material.ResourceId,
            $"Gather {need.Material.ResourceId} so the household has what it needs to build its own {need.Building.DisplayName}.",
            34));
    }

    private void GatherBuildingMaterial(string actor, PlaytestInhabitantState state, string itemKind)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            NeededBuildingMaterial(actor, householdId) is not { } need || need.Material.ResourceId != itemKind ||
            MaterialSource(itemKind, actor) is not { } source)
            return;
        if (CollectGatheringTool(actor, state, itemKind)) return;
        GatherProjectMaterial(actor, state, itemKind, source);
    }
}
