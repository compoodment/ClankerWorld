namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void StageOrnamentContent() => StageBuiltInContent(OrnamentContent.PackageId,
        BlacksmithContent.PackageId, OrnamentContent.Create, "ornament_content_staged");

    private bool WantsOrnamentInput(string actor, string kind)
    {
        if (kind is not ("gold_ore" or OrnamentContent.Gold or "diamond") ||
            HouseholdFor(actor) is not { } household || BlacksmithForHousehold(household) is not { } smith)
            return false;
        var target = worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == smith.DefinitionId &&
                recipe.Tags.Contains("ornament", StringComparer.Ordinal) && NeedsRecipeOutput(recipe, household))
            .SelectMany(recipe => recipe.Inputs).Where(input => input.ResourceId == kind)
            .Select(input => input.Amount * SupplyBatches).DefaultIfEmpty(0).Max();
        return target > 0 && society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == kind &&
                (lot.OwnerId == household || lot.OwnerId == actor)).Sum(AvailableLotQuantity) < target;
    }
}
