using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Viewer.Observation;

public sealed partial class OwnerWorldObservationStore
{
    private static ViewerProductionRecipe ProjectProductionRecipe(RecipeDefinition recipe) => new(
        recipe.CanonicalId, recipe.DisplayName,
        recipe.Inputs.Select(input => new ViewerMaterialQuantity(input.ResourceId, input.Amount)).ToArray(),
        recipe.Outputs.Select(output => new ViewerMaterialQuantity(output.ResourceId, output.Amount)).ToArray());
}
