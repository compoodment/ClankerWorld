using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

// Production-specific fixtures begin with previously worked soil. Field lifecycle
// tests exercise the actual tilling, permission and carrying operations separately.
internal static class FarmTestFields
{
    internal static PrivateWorldRuntimeState Prepare(PrivateWorldRuntimeState state, string actor, GridPoint point,
        RecipeDefinition recipe)
    {
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "fixture-hoe:" + actor, "iron_hoe", actor, 1, state.Society.Society.WorldTick);
        foreach (var input in recipe.Inputs)
            inventory = InventoryFixture.AddLot(inventory, "fixture-crop:" + actor + ":" + input.ResourceId,
                input.ResourceId, actor, input.Amount, state.Society.Society.WorldTick);
        return state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            WorldSimulation = state.WorldSimulation! with
            {
                Fields = [new FarmFieldTile(point, household, FarmFieldStage.Prepared, state.Society.Society.WorldTick, WorkDone: 8)],
            },
        };
    }

    internal static async Task Harvest(PrivateWorldRuntime world, string actor, GridPoint point)
    {
        var work = 0;
        for (var turns = 0; turns < 20 && work < 4; turns++)
        {
            var result = world.HarvestField(actor, point);
            if (result.Applied) work++;
            else Assert.Contains("Illness", result.Failure, StringComparison.Ordinal);
            if (work < 4) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(4, work);
    }
}

internal sealed class FarmCarryProvider(bool active) : IDecisionProvider
{
    public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
    public long ProviderEpoch => 0;
    public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var preferred = active ? request.Observation.Candidates.Where(candidate =>
            candidate.Id is "haul_household_stock" or "farm:collect").OrderBy(candidate => candidate.DeterministicPriority).FirstOrDefault() : null;
        return new DeterministicDecisionProvider().DecideAsync(request with
        {
            Observation = request.Observation with { Candidates = [preferred ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")] },
        }, cancellationToken);
    }
}
