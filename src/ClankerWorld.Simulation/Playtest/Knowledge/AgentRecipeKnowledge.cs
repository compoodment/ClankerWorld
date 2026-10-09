using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A person's recipe account, grounded in paid production or an actual written source.</summary>
public sealed record AgentRecipeKnowledge(string Id, string OwnerId, string RecipeId, string DiscovererId,
    long LearnedTick, string Acquisition, string SourceProductionJobId,
    string? SourceAgentId = null, string? SourceArtifactId = null);

internal static partial class AgentKnowledgeRules
{
    public const int MaximumRecipesPerAgent = 128;
    public static int RecipeCapacity(string kind) => kind switch { "field_record" => 1, "book" => 9, _ => 0 };
    public static bool SameRecipe(AgentRecipeKnowledge first, AgentRecipeKnowledge second) =>
        first.RecipeId == second.RecipeId && first.DiscovererId == second.DiscovererId &&
        first.SourceProductionJobId == second.SourceProductionJobId;

    public static void ValidateRecipes(PrivateWorldKnowledgeState knowledge, SocietyCheckpoint society,
        DeclarativeWorldContentState content, WorldContentSimulationState simulation, long worldTick)
    {
        if (knowledge.Recipes is null || knowledge.Recipes.Any(item => item is null))
            throw new InvalidDataException("Recipe knowledge is missing or incomplete.");
        var agents = society.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var definitions = content.Recipes.Select(item => item.CanonicalId).ToHashSet(StringComparer.Ordinal);
        if (simulation.ProductionJobs.Select(item => item.JobId).Distinct(StringComparer.Ordinal).Count() != simulation.ProductionJobs.Count)
            throw new InvalidDataException("Recipe production evidence contains duplicate job identities.");
        var jobs = simulation.ProductionJobs.ToDictionary(item => item.JobId, StringComparer.Ordinal);
        var artifacts = knowledge.Artifacts.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (knowledge.Recipes.Count > checked(agents.Count * MaximumRecipesPerAgent) ||
            knowledge.Recipes.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != knowledge.Recipes.Count ||
            knowledge.Recipes.Select(item => (item.OwnerId, item.RecipeId)).Distinct().Count() != knowledge.Recipes.Count ||
            knowledge.Recipes.GroupBy(item => item.OwnerId).Any(group => group.Count() > MaximumRecipesPerAgent) ||
            knowledge.Recipes.Any(item => !ValidRecipe(item, worldTick)))
            throw new InvalidDataException("A person's recipe knowledge has invalid ownership, bounds or evidence.");
        foreach (var artifact in knowledge.Artifacts)
            if (!ValidContents(artifact.Recipes, artifact.CreatorId, artifact.Kind, artifact.CreatedTick, artifact.SourceArtifactId))
                throw new InvalidDataException("Written recipes differ from their author's knowledge or copied source.");
        foreach (var project in knowledge.WritingProjects)
            if (!ValidContents(project.Recipes, project.ActorId, project.Kind, project.StartedTick, project.SourceArtifactId))
                throw new InvalidDataException("Writing recipes must come from the writer's existing knowledge.");

        bool ValidRecipe(AgentRecipeKnowledge recipe, long latestTick) =>
            ValidText(recipe.Id, 160) && agents.Contains(recipe.OwnerId) && agents.Contains(recipe.DiscovererId) &&
            definitions.Contains(recipe.RecipeId) && recipe.LearnedTick >= 0 && recipe.LearnedTick <= latestTick &&
            ValidText(recipe.SourceProductionJobId, 128) && jobs.TryGetValue(recipe.SourceProductionJobId, out var job) && job.State == WorldProductionJobState.Completed &&
            job.WorkerId == recipe.DiscovererId && job.RecipeId == recipe.RecipeId && job.CompletionTick <= recipe.LearnedTick &&
            (recipe.Acquisition == "practice" && recipe.OwnerId == recipe.DiscovererId && recipe.SourceAgentId is null && recipe.SourceArtifactId is null ||
             recipe.Acquisition is "read" or "shared" && recipe.SourceAgentId is { } source && agents.Contains(source) &&
             recipe.SourceArtifactId is { } sourceId && artifacts.TryGetValue(sourceId, out var artifact) &&
             artifact.CreatedTick <= recipe.LearnedTick && artifact.Recipes.Any(original => SameRecipe(recipe, original)));

        bool ValidContents(IReadOnlyList<AgentRecipeKnowledge> recipes, string owner, string kind, long tick, string? sourceId)
        {
            if (recipes is null || recipes.Count > RecipeCapacity(kind) || recipes.Any(item => item is null) ||
                recipes.Select(item => item.RecipeId).Distinct(StringComparer.Ordinal).Count() != recipes.Count) return false;
            if (sourceId is not null && (!artifacts.TryGetValue(sourceId, out var source) ||
                source.Recipes.Count != recipes.Count || source.Recipes.Any(item => !recipes.Any(copy => SameRecipe(item, copy))))) return false;
            return recipes.All(recipe => recipe.OwnerId == owner && ValidRecipe(recipe, tick) &&
                (sourceId is null ? knowledge.Recipes.Contains(recipe) :
                    recipe.Acquisition == "read" && recipe.SourceArtifactId == sourceId &&
                    recipe.SourceAgentId == artifacts[sourceId].CreatorId && knowledge.Recipes.Any(known =>
                        known.OwnerId == owner && known.Id == recipe.Id && known.RecipeId == recipe.RecipeId && known.LearnedTick <= recipe.LearnedTick)));
        }
    }
}
