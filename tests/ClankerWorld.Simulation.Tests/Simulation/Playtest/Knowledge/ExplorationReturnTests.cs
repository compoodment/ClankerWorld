using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ExplorationReturnTests
{
    [Theory]
    [InlineData("direct")]
    [InlineData("detour")]
    [InlineData("displaced_finish")]
    [InlineData("blocked")]
    public async Task ReturnKeepsItsDestinationAndDiscoveriesAcrossReload(string scenario)
    {
        using var initial = new PrivateWorldRuntime("return-detour-regression");
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var origin = scenario == "blocked" ? new GridPoint(2, 2) : new GridPoint(0, 0);
        var position = scenario switch
        {
            "blocked" => new GridPoint(0, 0),
            "displaced_finish" => new GridPoint(1, 0),
            _ => new GridPoint(1, 1),
        };
        GridPoint[] path = scenario switch
        {
            "blocked" => [origin, new GridPoint(1, 1), position],
            "displaced_finish" => [origin],
            _ => [origin, position],
        };
        GridPoint[] otherPositions = scenario == "blocked"
            ? [new(1, 0), new(0, 1), new(1, 1)]
            : [scenario == "detour" ? new(0, 1) : new(4, 1), new(4, 2), new(4, 3)];
        var fact = new AgentKnowledgeFact("return-discovery", actor, actor, origin,
            (state.Map.TerrainKindAt(origin) ?? throw new InvalidOperationException()).ToString(),
            state.Map.Resources.Where(resource => resource.Position == origin).Select(resource => resource.Kind).ToArray(),
            0, "firsthand");
        state = state with
        {
            Knowledge = new PrivateWorldKnowledgeState([fact], []),
            Inhabitants = state.Inhabitants.Select((person, index) => person with
            {
                Position = index == 0 ? position : otherPositions[index - 1],
                HungerBasisPoints = 9_500,
                MoveWaitTicks = index == 0 && scenario == "blocked" ? 29 : 0,
                TravelCooldownTicks = 0,
                Exploration = index == 0 ? new SettlementExploration(path, path, 0, true, [origin]) : null,
            }).ToArray(),
        };
        IDecisionProvider Provider(string id) => new ReturnProvider(id == actor);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Provider);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), Provider);
        for (var tick = 0; tick < 5; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        restored.Validate();
        var result = restored.ExportState();
        var explorer = restored.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Empty(explorer.Exploration!.OutingPath);
        Assert.DoesNotContain(result.Events, item => item.Kind == "exploration_aborted" && item.Detail == actor + ":interrupted_movement");
        Assert.Equal(origin, Assert.Single(Assert.Single(result.Knowledge!.Artifacts).Facts).Position);
        if (scenario == "blocked")
        {
            Assert.Equal(position, explorer.Position);
            Assert.Contains(result.Events, item => item.Kind == "exploration_aborted" && item.Detail == actor + ":return_blocked");
            Assert.DoesNotContain(result.Events, item => item.Kind == "exploration_completed");
        }
        else
        {
            Assert.Equal(origin, explorer.Position);
            Assert.Contains(result.Events, item => item.Kind == "exploration_completed" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        }
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(result));
    }

    private sealed class ReturnProvider(bool explore) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(item => explore && item.Id == "explore")
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
