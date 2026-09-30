using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementRestTests
{
    [Fact]
    public async Task IllnessAndHungerNeverOfferSleepAndOrdinaryTicksStillAdvance()
    {
        using var seed = new PrivateWorldRuntime("needs-without-sleep");
        var state = seed.ExportState();
        var provider = new RecordingProvider();
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 2_500,
                Survival = new SurvivalCondition(WarmthBasisPoints: 2_000, IllnessBasisPoints: 7_000),
            }).ToArray(),
        }, _ => provider);

        var result = await world.AdvanceOneTickAsync();

        Assert.True(result.Advanced);
        Assert.Equal(1, result.WorldTick);
        Assert.NotEmpty(provider.Candidates);
        Assert.DoesNotContain(provider.Candidates, id => id == "sleep");
        Assert.DoesNotContain(result.Events, item => item.Kind is "inhabitant_slept" or "inhabitant_rested_outdoors");
        Assert.Contains(world.Inhabitants, person => person.HungerBasisPoints != 2_500);
        Assert.Contains(provider.Candidates, id => id is "consume_food" or "seek_food" or "harvest_food" or "collect_shared_food");
    }

    private sealed class RecordingProvider : IDecisionProvider
    {
        private readonly ConcurrentBag<string> candidates = [];
        public IReadOnlyCollection<string> Candidates => candidates.ToArray();
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates)
                candidates.Add(candidate.Id);
            return new DeterministicDecisionProvider().DecideAsync(request, cancellationToken);
        }
    }
}
