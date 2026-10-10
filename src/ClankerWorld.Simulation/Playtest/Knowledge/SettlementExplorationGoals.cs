using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string ExplorationGoalPrefix = "explore_for:";

    private static string ExplorationGoalCandidateId(SettlementExplorationGoal goal) =>
        $"{ExplorationGoalPrefix}{goal.Kind}:{goal.Target}";

    private static bool ValidExplorationGoal(SettlementExplorationGoal? goal) => goal is null ||
        (goal.Kind == "resource" && (PrivateWorldInstructionOrderParser.IsMaterialKind(goal.Target) ||
            goal.Target is "food" or "berries" or "wild_greens" or "fruit") ||
         goal.Kind == "terrain" && goal.Target is "Forest" or "Mountain") &&
        (goal.OrderInstructionId is null || goal.OrderInstructionId.Length is > 0 and <= 160 &&
            !goal.OrderInstructionId.Any(char.IsControl));

    private static SettlementExplorationGoal? ExplorationGoalForOrder(OwnerQueuedInstruction instruction) =>
        instruction.Order is { TargetResourceId: null, TargetPosition: null } order
            ? order.Action == "gather_material" ? new("resource", order.TargetMaterialKind!, instruction.InstructionId)
            : order.Action is "seek_food" or "harvest_food" ? new("resource", order.TargetFoodKind ?? "food", instruction.InstructionId)
            : null : null;

    private string? NeededExplorationResource(string actor, PlaytestInhabitantState person)
    {
        // Needs and recipe inputs identify a purpose. No unseen site's position
        // participates in this choice or in ranking the next scouting step.
        if (person.HungerBasisPoints < 7_000 && FreeCarryCapacity(actor) > 0 &&
            !PreferredFood(actor, actor).Any() && AvailableSharedFood(actor) is null &&
            !KnownExplorationSources(actor, person, "food").Any())
            return "food";
        if (person.Project is not
            {
                JobId: null, OrderInstructionId: null, ToolMakingRequestId: null,
                Stage: not ("completed" or "cancelled" or "waiting")
            } project ||
            !TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection))
            return null;
        var building = selection.IsBuilding
            ? worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId) : null;
        var recipe = selection.IsBuilding
            ? null : worldContent.Recipes.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId);
        var inputs = building?.BuildCosts ?? recipe?.Inputs;
        var recipeBuilding = recipe is not null && TryFindRecipeSite(recipe, out var site, out _, actor)
            ? worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == site) : null;
        var owner = building is not null ? BuildingConstructionOwner(actor, building)
            : recipe is not null && IsHandcartRecipe(recipe) ? actor : ProductionOwnerFor(recipeBuilding, actor);
        return inputs?.Where(input => PrivateWorldInstructionOrderParser.IsMaterialKind(input.ResourceId) &&
                !HasAvailableQuantities([input], owner) &&
                !KnownExplorationSources(actor, person, input.ResourceId).Any())
            .Select(input => input.ResourceId).FirstOrDefault();
    }

    private IEnumerable<MapResource> KnownExplorationSources(string actor, PlaytestInhabitantState person, string target)
    {
        var projectReturn = ExplorationProjectReturn(actor, person, target);
        var positions = knowledge.Facts.Where(fact => fact.OwnerId == actor &&
                (target == "food" ? fact.ResourceKinds.Any(kind => kind is "berries" or "wild_greens" or "fruit")
                    : fact.ResourceKinds.Contains(target, StringComparer.Ordinal) ||
                      target == "wood" && fact.ResourceKinds.Contains("construction", StringComparer.Ordinal)))
            .Select(fact => fact.Position).ToHashSet();
        var tools = new Dictionary<(ToolFamily Family, int Tier), ToolDefinition?>();
        return map.Resources.Where(source => (positions.Contains(source.Position) ||
                IsWithinInteractionRange(person.Position, source.Position, ResourceInteractionRange)) &&
                (target == "food" ? source.Kind is "food" or "fruit"
                    : FoodKnowledgeKind(source) == target ||
                      target == "wood" && source.Kind == "construction") &&
                resources.GetValueOrDefault(source.Id) == ResourceState.Available)
            .Where(source => target is "food" or "berries" or "wild_greens" or "fruit"
                ? FreeCarryCapacity(actor) >= FoodHarvestCarryUnits(source)
                : CanGatherFromSource(actor, target, source, tools) &&
                  ProjectMaterialCarryUnits(actor, target, source) <= FreeCarryCapacity(actor))
            .Where(source => IsWithinInteractionRange(person.Position, source.Position, ResourceInteractionRange) ||
                FindUnoccupiedRoute(actor, person.Position, source.Position, ResourceInteractionRange).Count > 0)
            .Where(source => projectReturn is not { } route || route.Destination is { } destination &&
                CanReturnWithHarvest(actor, target, source, destination, route.Range, useHarvestBonus: false));
    }

    private (GridPoint? Destination, int Range)? ExplorationProjectReturn(string actor,
        PlaytestInhabitantState person, string target)
    {
        if (!PrivateWorldInstructionOrderParser.IsMaterialKind(target))
            return null;
        if (person.Project is not
            {
                JobId: null, OrderInstructionId: null, ToolMakingRequestId: null,
                Stage: not ("completed" or "cancelled" or "waiting")
            } project || !TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection))
            return null;
        var building = selection.IsBuilding
            ? worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId) : null;
        var recipe = selection.IsBuilding
            ? null : worldContent.Recipes.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId);
        var inputs = building?.BuildCosts ?? recipe?.Inputs;
        if (inputs is null || !inputs.Any(input => input.ResourceId == target))
            return null;
        PlacedBuilding? recipeBuilding = null;
        GridPoint? workSite = selection.SitePosition;
        if (recipe is not null)
        {
            workSite = TryFindRecipeSite(recipe, out var site, out var position, actor) ? position : null;
            recipeBuilding = worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == site);
        }
        var owner = building is not null ? BuildingConstructionOwner(actor, building)
            : IsHandcartRecipe(recipe!) ? actor : ProductionOwnerFor(recipeBuilding, actor);
        if (owner == actor && workSite is null && building is not null)
        {
            var sites = TownLayoutService.RankConstructionSites(CreateTownLayoutContext(actor, null, building), building);
            if (sites.Count > 0) workSite = sites[0].Position;
        }
        var house = HouseForHousehold(owner);
        return (owner == actor ? workSite : house?.Position ?? SettlementStoragePosition,
            owner == actor || house is not null ? 0 : ResourceInteractionRange);
    }

    private bool ExplorationGoalReached(string actor, PlaytestInhabitantState person, SettlementExplorationGoal goal)
    {
        if (goal.OrderInstructionId is { } id)
        {
            var instruction = PendingInstructionFor(actor);
            return instruction?.InstructionId == id && OrderCandidateFor(instruction, person) is { Id: not "explore" };
        }
        if (goal.Kind == "terrain")
            return map.TerrainKindAt(person.Position)?.ToString() == goal.Target;
        return KnownExplorationSources(actor, person, goal.Target).Any();
    }

    private static void ValidateExplorationGoalBindings(IEnumerable<PlaytestInhabitantState> people,
        IEnumerable<OwnerQueuedInstruction> instructions)
    {
        var byId = instructions.ToDictionary(item => item.InstructionId, StringComparer.Ordinal);
        foreach (var person in people)
            if (person.Exploration?.Goal is { OrderInstructionId: { } id } goal &&
                (!byId.TryGetValue(id, out var instruction) || instruction.TargetInhabitantId != person.InhabitantId ||
                 ExplorationGoalForOrder(instruction) != goal))
                throw new InvalidDataException("The saved exploration purpose does not match its gathering order.");
    }
}
