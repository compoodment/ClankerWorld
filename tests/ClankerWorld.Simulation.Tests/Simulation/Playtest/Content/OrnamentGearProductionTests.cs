using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class OrnamentGearProductionTests
{
    [Theory]
    [InlineData("refine-gold", "gold")]
    [InlineData("gold-ornament", "gold_ornament")]
    [InlineData("set-diamond", "diamond_ornament")]
    [InlineData("spear", "spear")]
    [InlineData("sword", "sword")]
    [InlineData("shield", "shield")]
    [InlineData("basic-armor", "basic_armor")]
    public async Task SmithCraftsAcceptedGoodsFromItsActualReservedStockAcrossReload(string localId, string outputKind)
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Idle());
        var state = seed.ExportState();
        var smith = seed.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var outsider = state.Society.Society.Inhabitants.First(person => person.HouseholdId != smith.HouseholdId).Id;
        var recipe = seed.WorldContent.Recipes.Single(item => item.LocalId == localId);
        var inventory = state.Society.Society.Inventory;
        foreach (var input in recipe.Inputs)
            inventory = InventoryFixture.AddLot(inventory, "craft-input:" + input.ResourceId, input.ResourceId,
                smith.HouseholdId!, input.Amount, storageBuildingId: smith.InstanceId);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = smith.Position } : person).ToArray(),
        };
        using var crafting = PrivateWorldRuntime.Restore(state, _ => new Idle());
        var before = crafting.Society.Inventory;
        var refusal = crafting.StartProduction(recipe.CanonicalId, smith.InstanceId, outsider);
        Assert.False(refusal.Applied);
        Assert.Equal(before, crafting.Society.Inventory);
        var result = crafting.StartProduction(recipe.CanonicalId, smith.InstanceId, actor);
        Assert.True(result.Applied, result.Failure);
        var job = crafting.WorldSimulation.ProductionJobs.Single(item => item.JobId == result.JobId);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            crafting.Society.Inventory.GetReservation(id).State));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(crafting.ExportState())),
            _ => new Idle());
        for (var tick = 0; tick < recipe.DurationTicks; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var output = Assert.Single(restored.Society.Inventory.Lots, lot => lot.Id.StartsWith(job.JobId + ":output:", StringComparison.Ordinal));
        Assert.Equal(outputKind, output.ItemKind);
        Assert.Equal(smith.HouseholdId, output.OwnerId);
        Assert.Equal(smith.InstanceId, output.StorageBuildingId);
        Assert.Equal(1, output.Quantity);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
            restored.Society.Inventory.GetReservation(id).State));
        Assert.Equal(WorldProductionJobState.Completed, restored.WorldSimulation.ProductionJobs.Single(item => item.JobId == result.JobId).State);
    }

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}
