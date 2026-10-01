using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

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
        foreach (var definition in PlannableHouseholdBuildings(householdId, inhabitant.Id))
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
    private IEnumerable<BuildingDefinition> PlannableHouseholdBuildings(string householdId, string actor) => worldContent.Buildings
        .Where(definition => !RetiredBuildings.Contains(definition) &&
            HouseholdBuildingKind(definition) is { } kind && HouseholdMayPlan(householdId, kind, actor))
        .OrderBy(definition => HouseholdBuildingKinds.PlanOrder(HouseholdBuildingKind(definition)))
        .ThenBy(definition => definition.CanonicalId, StringComparer.Ordinal);

    private bool HouseholdMayPlan(string householdId, string kind, string actor) =>
        HouseholdBuildingWithTag(householdId, kind) is null &&
        (kind != "silo" || FarmhouseForHousehold(householdId) is not null) &&
        !HouseholdBuildingProjectInProgress(householdId, kind, actor);

    /// <summary>Whether a member already has a live plan for this kind, so the household plans at most one.</summary>
    private bool HouseholdBuildingProjectInProgress(string householdId, string? kind = null,
        string? retryingActor = null) =>
        inhabitants.Values.Any(person =>
            person.Project is { } project && project.Stage is not ("completed" or "cancelled") &&
            TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection) && selection.IsBuilding &&
            society.Checkpoint.GetInhabitant(person.InhabitantId).HouseholdId == householdId &&
            worldContent.Buildings.FirstOrDefault(definition => definition.CanonicalId == selection.DefinitionId) is { } planned &&
            HouseholdBuildingKind(planned) is { } plannedKind && (kind is null || plannedKind == kind) &&
            !(person.InhabitantId == retryingActor && project.Stage == "blocked" &&
                WorldTick - project.LastTransitionTick >= BlockedProjectRetryDelayTicks));

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
        if (HouseholdBuildingProjectInProgress(householdId, retryingActor: actor))
            return null;
        foreach (var definition in PlannableHouseholdBuildings(householdId, actor))
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
        if (NeededBuildingMaterial(actor, householdId) is not { } need ||
            MaterialSource(need.Material.ResourceId, actor) is not { } source)
            return;
        var useHarvestBonus = UseHarvestBonusForBuildingMaterial(actor, need.Material.ResourceId, source);
        if (FreeCarryCapacity(actor) < ProjectMaterialCarryUnits(actor, need.Material.ResourceId, source, useHarvestBonus))
            return;
        var load = useHarvestBonus ? "using its faster whole load" : "as a smaller whole load that fits your carrying space";
        candidates.Add(new CognitionCandidate(GatherBuildingMaterialPrefix + need.Material.ResourceId,
            $"Gather {need.Material.ResourceId} {load} so the household has what it needs to build its own {need.Building.DisplayName}.",
            34));
    }

    /// <summary>Use the faster harvest only when its complete output fits the current carry space.</summary>
    private bool UseHarvestBonusForBuildingMaterial(string actor, string itemKind, MapResource source)
    {
        var plan = ProjectMaterialHarvest(actor, itemKind, source, useHarvestBonus: true);
        return plan is { ToolLotId: not null, Quantity: > 4 } &&
            FreeCarryCapacity(actor) >= checked(plan.Quantity + plan.TreeSeedQuantity);
    }

    /// <summary>
    /// A carried project tool is kept at home until preparation materials have
    /// room to travel. Building work can collect it again from the household.
    /// </summary>
    private InventoryLot? BuildingPreparationToolToStore(string actor, string householdId)
    {
        if (inhabitants[actor].Project is { Stage: not ("completed" or "cancelled") } ||
            HouseForHousehold(householdId) is not { } house || StorageRoom(house.InstanceId) == 0 ||
            NeededBuildingMaterial(actor, householdId) is not { } need ||
            MaterialSource(need.Material.ResourceId, actor) is not { } source)
            return null;

        var free = FreeCarryCapacity(actor);
        var required = ProjectMaterialCarryUnits(actor, need.Material.ResourceId, source, useHarvestBonus: false);
        if (free >= required)
            return null;

        var tool = society.Checkpoint.Inventory.Lots.Where(lot =>
                PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null &&
                lot.ContainerLotId is null &&
                lot.ItemKind == "tool" && AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
        return tool is not null && free + Math.Min(1, AvailableLotQuantity(tool)) >= required ? tool : null;
    }

    private void GatherBuildingMaterial(string actor, PlaytestInhabitantState state, string itemKind)
    {
        if (!AdultResident(actor) || society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            NeededBuildingMaterial(actor, householdId) is not { } need || need.Material.ResourceId != itemKind ||
            MaterialSource(itemKind, actor) is not { } source)
            return;
        var useHarvestBonus = UseHarvestBonusForBuildingMaterial(actor, itemKind, source);
        var deliveryBuildingId = HouseForHousehold(householdId)?.InstanceId;
        GatherProjectMaterial(actor, state, itemKind, source, useHarvestBonus, deliveryBuildingId);
    }
}
