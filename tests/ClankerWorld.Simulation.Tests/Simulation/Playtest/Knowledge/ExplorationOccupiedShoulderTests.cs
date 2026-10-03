using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ExplorationOccupiedShoulderTests
{
    private const string Actor = "founder-ilya";
    private static readonly GridPoint Start = new(4, 1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutwardExplorerMakesProgressWithoutCrossingOccupiedShoulders(bool blocked)
    {
        GridPoint[] others = blocked
            ? [new(4, 0), new(5, 1), new(0, 0)]
            : [new(2, 0), new(1, 0), new(0, 0)];
        using var world = CreateWorld(Start, [new(3, 1), Start], others);
        var map = world.ExportState().Map;
        Assert.True(map.CanFootStep(Start, new GridPoint(5, 0)));
        Assert.True(map.CanFootStep(Start, new GridPoint(4, 2)));
        Assert.True(map.CanFootStep(Start, new GridPoint(3, 2)));

        var moves = 0;
        for (var tick = 0; tick < 20; tick++) moves += await Step(world);
        using var restored = Reload(world);
        for (var tick = 0; tick < 20; tick++)
        {
            moves += await Step(restored);
            _ = await Step(world);
            AssertSameState(world, restored);
        }
        restored.Validate();
        var result = Scout(restored);
        Assert.True(moves > 0 || result.Exploration!.OutingPath.Count == 0,
            "An available legal exit must allow progress, or the outing must end within 40 normal ticks.");
    }

    [Theory]
    [InlineData(true, 5, 1)]
    [InlineData(false, 3, 0)]
    public async Task EitherOccupiedShoulderExcludesTheDiagonalBeforeChoosingTheNextStep(
        bool upperShoulderOccupied, int expectedX, int expectedY)
    {
        var shoulder = upperShoulderOccupied ? new GridPoint(4, 0) : new GridPoint(5, 1);
        using var world = CreateWorld(Start, [new(3, 1), Start], [shoulder, new(1, 0), new(0, 0)]);
        var diagonal = new GridPoint(5, 0);
        Assert.True(world.ExportState().Map.CanFootStep(Start, diagonal));
        Assert.DoesNotContain(world.Inhabitants, person => person.Position == diagonal);

        Assert.Equal(1, await Step(world));
        var expected = new GridPoint(expectedX, expectedY);
        Assert.Equal(expected, Scout(world).Position);
        Assert.Equal([new GridPoint(3, 1), Start, expected], Scout(world).Exploration!.VisitedTiles);
        // A local outing chooses a legal neighboring destination. It must not
        // start a longer detour toward the diagonal that failed the corner rule.
        Assert.DoesNotContain(Actor, world.ExportStateWithDiagnostics().Diagnostics.PlannedRoutes.Keys);
        using var restored = Reload(world);
        for (var tick = 0; tick < 8; tick++)
        {
            _ = await Step(world);
            _ = await Step(restored);
            AssertSameState(world, restored);
        }
        restored.Validate();
    }

    [Fact]
    public async Task NoLegalOutwardExitFallsBackToABoundedReturnAcrossReload()
    {
        var corner = new GridPoint(0, 0);
        GridPoint[] path = [new(2, 2), new(1, 1), corner];
        using var world = CreateWorld(corner, path, [new(1, 0), new(0, 1), new(1, 1)]);
        Assert.Equal(0, await Step(world));
        Assert.True(Scout(world).Exploration!.Returning);
        Assert.Equal(path, Scout(world).Exploration!.VisitedTiles);
        Assert.Equal(path, Scout(world).Exploration!.OutingPath);
        for (var tick = 1; tick < 15; tick++) Assert.Equal(0, await Step(world));

        using var restored = Reload(world);
        for (var tick = 0; tick < 25; tick++)
        {
            Assert.Equal(0, await Step(world));
            Assert.Equal(0, await Step(restored));
            AssertSameState(world, restored);
        }
        var result = Scout(restored);
        Assert.Equal(corner, result.Position);
        Assert.Equal(path, result.Exploration!.VisitedTiles);
        Assert.Empty(result.Exploration.OutingPath);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "exploration_aborted" &&
            item.Detail == Actor + ":return_blocked");
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind == "exploration_discovered" &&
            item.Detail.StartsWith(Actor + ":", StringComparison.Ordinal));
        restored.Validate();
    }

    private static PrivateWorldRuntime CreateWorld(GridPoint start, GridPoint[] path, GridPoint[] others)
    {
        using var initial = new PrivateWorldRuntime("explore-audit-0");
        var state = initial.ExportState();
        var otherIndex = 0;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == Actor ? start : others[otherIndex++],
                HungerBasisPoints = 9_500,
                MoveWaitTicks = 0,
                TravelCooldownTicks = 0,
                Exploration = person.InhabitantId == Actor ? new SettlementExploration(path, path, 0, false) : null,
            }).ToArray(),
        };
        Assert.All(state.Inhabitants, person => Assert.True(state.Map.IsPassable(person.Position)));
        return PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Provider);
    }

    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world)
    {
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), Provider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        return restored;
    }

    private static PlaytestInhabitantState Scout(PrivateWorldRuntime world) =>
        world.Inhabitants.Single(person => person.InhabitantId == Actor);

    private static void AssertSameState(PrivateWorldRuntime world, PrivateWorldRuntime restored) =>
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
            PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

    private static async Task<int> Step(PrivateWorldRuntime world)
    {
        var before = Scout(world);
        var occupied = world.Inhabitants.Where(person => person.InhabitantId != Actor)
            .Select(person => person.Position).ToHashSet();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var after = Scout(world);
        Assert.All(after.Exploration!.VisitedTiles,
            point => Assert.True(before.Exploration!.VisitedTiles.Contains(point) || point == after.Position));
        if (after.Position == before.Position)
        {
            Assert.Equal(before.Exploration!.VisitedTiles, after.Exploration.VisitedTiles);
            return 0;
        }
        var map = world.ExportState().Map;
        Assert.True(map.CanFootStep(before.Position, after.Position));
        Assert.DoesNotContain(after.Position, occupied);
        if (map.IsDiagonalFootStep(before.Position, after.Position))
        {
            Assert.DoesNotContain(new GridPoint(after.Position.X, before.Position.Y), occupied);
            Assert.DoesNotContain(new GridPoint(before.Position.X, after.Position.Y), occupied);
        }
        Assert.Contains(after.Position, after.Exploration.VisitedTiles);
        return 1;
    }

    private static IDecisionProvider Provider(string id) => new ExploreProvider(id == Actor);

    private sealed class ExploreProvider(bool explore) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(item => explore && item.Id == "explore")
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
