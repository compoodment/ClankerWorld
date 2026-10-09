using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private bool KnowsRecipe(string actor, string recipeId) =>
        knowledge.Recipes.Any(item => item.OwnerId == actor && item.RecipeId == recipeId);

    private static string RecipeKnowledgeId(string actor, string recipeId) =>
        "recipe-knowledge:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{actor}|{recipeId}")));

    private void LearnProducedRecipe(WorldProductionJob job, RecipeDefinition recipe, long tick)
    {
        if (!inhabitants.ContainsKey(job.WorkerId) ||
            society.Checkpoint.GetInhabitant(job.WorkerId).Status != SocietyInhabitantStatus.Active ||
            KnowsRecipe(job.WorkerId, recipe.CanonicalId) ||
            knowledge.Recipes.Count(item => item.OwnerId == job.WorkerId) >= AgentKnowledgeRules.MaximumRecipesPerAgent) return;
        knowledge = knowledge with
        {
            Recipes = knowledge.Recipes.Append(new AgentRecipeKnowledge(
            RecipeKnowledgeId(job.WorkerId, recipe.CanonicalId), job.WorkerId, recipe.CanonicalId, job.WorkerId,
            tick, "practice", job.JobId)).ToArray()
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("agent_recipe_learned", $"{job.WorkerId}|{recipe.CanonicalId}|practice");
    }

    private int LearnArtifactRecipes(string actor, string sourceActor, AgentKnowledgeArtifact artifact, string acquisition)
    {
        var recipes = knowledge.Recipes.ToList();
        var count = 0;
        foreach (var source in artifact.Recipes)
        {
            if (recipes.Any(item => item.OwnerId == actor && item.RecipeId == source.RecipeId) ||
                recipes.Count(item => item.OwnerId == actor) >= AgentKnowledgeRules.MaximumRecipesPerAgent) continue;
            recipes.Add(source with
            {
                Id = RecipeKnowledgeId(actor, source.RecipeId),
                OwnerId = actor,
                LearnedTick = WorldTick,
                Acquisition = acquisition,
                SourceAgentId = sourceActor,
                SourceArtifactId = artifact.Id,
            });
            count++;
        }
        if (count > 0) knowledge = knowledge with { Recipes = recipes.ToArray() };
        return count;
    }

    private bool HasUnknownArtifactContents(string actor, AgentKnowledgeArtifact artifact) =>
        knowledge.Facts.Count(item => item.OwnerId == actor) < AgentKnowledgeRules.MaximumFactsPerAgent &&
            artifact.Facts.Any(item => !KnowsMapFact(actor, item.Position)) ||
        knowledge.Recipes.Count(item => item.OwnerId == actor) < AgentKnowledgeRules.MaximumRecipesPerAgent &&
            artifact.Recipes.Any(item => !KnowsRecipe(actor, item.RecipeId));

    private AgentRecipeKnowledge[] RecipesToWrite(string actor, string kind, AgentKnowledgeArtifact? source = null)
    {
        var learned = knowledge.Recipes.Where(item => item.OwnerId == actor).ToArray();
        if (source is null) return learned.OrderByDescending(item => item.LearnedTick).ThenBy(item => item.RecipeId, StringComparer.Ordinal)
            .Take(AgentKnowledgeRules.RecipeCapacity(kind)).ToArray();
        if (source.Recipes.Any(item => !learned.Any(known => known.RecipeId == item.RecipeId))) return [];
        return source.Recipes.Select(item => item with
        {
            Id = learned.Single(known => known.RecipeId == item.RecipeId).Id,
            OwnerId = actor,
            LearnedTick = WorldTick,
            Acquisition = "read",
            SourceAgentId = source.CreatorId,
            SourceArtifactId = source.Id,
        }).ToArray();
    }

    private bool HasCompleteCopyContents(string actor, AgentKnowledgeArtifact source, AgentKnowledgeFact[] facts, AgentRecipeKnowledge[] recipes) =>
        facts.Length == source.Facts.Count && recipes.Length == source.Recipes.Count &&
        source.Facts.All(item => KnowsMapFact(actor, item.Position)) && source.Recipes.All(item => KnowsRecipe(actor, item.RecipeId));
}
