using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ToolProgressionRuntimeTests
{
    [Theory]
    [InlineData("wooden_axe", "collect_wooden_axe")]
    [InlineData("wooden_pickaxe", "collect_wooden_pickaxe")]
    public async Task LegacyHouseholdStarterToolsArePhysicalStockAndCanBeCollected(
        string itemKind, string candidateId)
    {
        const string actor = "founder-scout";
        var chooser = new CandidateProvider(candidateId);
        using var world = new PrivateWorldRuntime("legacy-tool-stock-" + itemKind,
            id => id == actor ? chooser : new CandidateProvider("safe_idle"));

        var stock = Assert.Single(world.Society.Inventory.Lots, lot =>
            lot.OwnerId == "household:camp-alpha" && lot.ItemKind == itemKind);
        Assert.Equal(1, stock.Quantity);
        Assert.Equal(10_000, stock.ConditionBasisPoints);
        Assert.Null(stock.StorageBuildingId);
        Assert.Null(stock.GroundPosition);

        for (var tick = 0; tick < 120 && !world.ExportState().Events.Any(item =>
                 item.Kind == "equipment_collected" && item.Detail == $"{actor}:{itemKind}"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidateId));
        Assert.Contains(world.ExportState().Events, item =>
            item.Kind == "equipment_collected" && item.Detail == $"{actor}:{itemKind}");
        var collected = Assert.Single(world.Society.Inventory.Lots, lot =>
            lot.OwnerId == actor && lot.ItemKind == itemKind);
        Assert.Equal(1, collected.Quantity);
        Assert.Null(collected.StorageBuildingId);
        Assert.Null(collected.GroundPosition);
    }

    private sealed class CandidateProvider(string candidateId) : IDecisionProvider
    {
        public List<string[]> ObservedCandidateSets { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            ObservedCandidateSets.Add(candidates.Select(item => item.Id).ToArray());
            var selected = candidates.FirstOrDefault(item => item.Id == candidateId) ??
                candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d)));
        }
    }
}
