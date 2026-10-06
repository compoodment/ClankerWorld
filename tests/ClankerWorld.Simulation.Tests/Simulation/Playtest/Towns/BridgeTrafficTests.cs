using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class BridgeTrafficTests
{
    private const string CrossingId = "bridge-1-1-ns-1";
    private static readonly GridPoint North = new(1, 0);
    private static readonly GridPoint Water = new(1, 1);
    private static readonly GridPoint South = new(1, 2);
    private static readonly SeededMap Map = RiverBridgeTests.Map(
        "...",
        "~~~",
        "...");

    // The same crossing point over a river two tiles wide.
    private const string WideCrossingId = "bridge-1-1-ns-2";
    private static readonly GridPoint NearWater = new(1, 1);
    private static readonly GridPoint FarWater = new(1, 2);
    private static readonly GridPoint WideSouth = new(1, 3);
    private static readonly SeededMap WideMap = RiverBridgeTests.Map(
        "...",
        "~~~",
        "~~~",
        "...");

    [Fact]
    public void ATwoTileWadeStaysOpenMidstreamUntilAStepReachesTheFarBank()
    {
        var wading = BridgeTrafficRules.RecordStep(BridgeTrafficState.Empty, WideMap, "a", North, NearWater, 1);
        Assert.Equal(new BridgeTrafficWade("a", WideCrossingId, North), Assert.Single(wading.InProgress));

        var midstream = BridgeTrafficRules.RecordStep(wading, WideMap, "a", NearWater, FarWater, 4);
        Assert.Equal(wading.InProgress, midstream.InProgress);
        Assert.Empty(midstream.Completed);
        Assert.Same(midstream, BridgeTrafficRules.Prune(midstream, WideMap, 5, 10,
            new Dictionary<string, GridPoint> { ["a"] = FarWater }));

        var crossed = BridgeTrafficRules.RecordStep(midstream, WideMap, "a", FarWater, WideSouth, 7);
        Assert.Empty(crossed.InProgress);
        Assert.Equal(new BridgeTrafficCrossing(WideCrossingId, "a", 7), Assert.Single(crossed.Completed));

        // Turning back midstream and leaving by the bank it entered from is not a crossing.
        var backAgain = BridgeTrafficRules.RecordStep(midstream, WideMap, "a", FarWater, NearWater, 7);
        Assert.Equal(wading.InProgress, backAgain.InProgress);
        Assert.True(BridgeTrafficRules.RecordStep(backAgain, WideMap, "a", NearWater, North, 10).IsEmpty);

        // Wading in from the other bank counts toward the same crossing.
        var fromSouth = BridgeTrafficRules.RecordStep(BridgeTrafficState.Empty, WideMap, "b", WideSouth, FarWater, 1);
        Assert.Equal(new BridgeTrafficWade("b", WideCrossingId, WideSouth), Assert.Single(fromSouth.InProgress));
    }

    [Fact]
    public void WalkingOnLandOrAnExistingBridgeIsNotWading()
    {
        Assert.True(BridgeTrafficRules.RecordStep(BridgeTrafficState.Empty, Map, "a", new(0, 0), North, 1).IsEmpty);
        Assert.True(RiverBridgeRules.TryFindCrossing(Map, North, 0, 1, out var crossing));
        var bridged = RiverBridgeTests.WithBridges(Map, RiverBridgeRules.ToBridge(crossing!, BridgeTriggers.Traffic, 0, null));
        var onDeck = BridgeTrafficRules.RecordStep(BridgeTrafficState.Empty, bridged, "a", North, Water, 1);
        Assert.True(BridgeTrafficRules.RecordStep(onDeck, bridged, "a", Water, South, 2).IsEmpty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BridgeNeedsSixCrossingsByTwoAgentsWithinTwoDays(bool twoTiles)
    {
        var (map, crossingId) = twoTiles ? (WideMap, WideCrossingId) : (Map, CrossingId);
        var state = BridgeTrafficState.Empty;
        for (var tick = 1; tick <= 12; tick += 2)
            state = Cross(state, "a", tick, twoTiles);
        Assert.Equal(BridgeTrafficRules.MaximumCrossingsPerAgent, state.Completed.Count);
        Assert.Equal([3L, 5, 7, 9, 11], state.Completed.Select(item => item.Tick));
        Assert.Empty(BridgeTrafficRules.ReadyCrossings(state));

        var ready = Cross(state, "b", 14, twoTiles);
        Assert.Equal([crossingId], BridgeTrafficRules.ReadyCrossings(ready));

        // Two world days at ten ticks a day: by tick 32 the first agent's
        // crossings have expired and the evidence falls below the threshold.
        var expired = BridgeTrafficRules.Prune(Cross(state, "b", 30, twoTiles), map, 32, ticksPerDay: 10,
            new Dictionary<string, GridPoint>());
        Assert.Equal(["b"], expired.Completed.Select(item => item.AgentId).Distinct());
        Assert.Empty(BridgeTrafficRules.ReadyCrossings(expired));

        Assert.True(BridgeTrafficRules.Forget(ready, crossingId).IsEmpty);
    }

    [Fact]
    public void PruneDropsWadesThatWereAbandoned()
    {
        var wading = BridgeTrafficRules.RecordStep(BridgeTrafficState.Empty, Map, "a", North, Water, 1);
        Assert.Same(wading, BridgeTrafficRules.Prune(wading, Map, 2, 10, new Dictionary<string, GridPoint> { ["a"] = Water }));
        Assert.Empty(BridgeTrafficRules.Prune(wading, Map, 2, 10, new Dictionary<string, GridPoint> { ["a"] = North }).InProgress);
        Assert.Empty(BridgeTrafficRules.Prune(wading, Map, 2, 10, new Dictionary<string, GridPoint>()).InProgress);
    }

    [Fact]
    public void SavedEvidenceMustBeBoundedRecentAndForARealUnbridgedCrossing()
    {
        var agents = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };
        var positions = new Dictionary<string, GridPoint> { ["a"] = Water, ["b"] = new(0, 0) };
        var valid = new BridgeTrafficState([new BridgeTrafficWade("a", CrossingId, North)],
            [new BridgeTrafficCrossing(CrossingId, "b", 5)]);
        BridgeTrafficRules.Validate(valid, Map, 10, 10, agents, positions, []);

        Assert.True(RiverBridgeRules.TryFindCrossing(Map, North, 0, 1, out var crossing));
        BridgeTrafficState[] invalid =
        [
            valid with { Completed = [new BridgeTrafficCrossing(CrossingId, "b", -10)] },
            valid with { Completed = [new BridgeTrafficCrossing(CrossingId, "stranger", 5)] },
            valid with { Completed = [new BridgeTrafficCrossing(CrossingId, "b", 11)] },
            valid with { Completed = [new BridgeTrafficCrossing("bridge-9-9-ns-1", "b", 5)] },
            valid with { Completed = [.. valid.Completed, .. valid.Completed] },
            valid with { Completed = Enumerable.Range(4, 6).Select(tick => new BridgeTrafficCrossing(CrossingId, "b", tick)).ToArray() },
            valid with { InProgress = [new BridgeTrafficWade("b", CrossingId, North)] },
            valid with { InProgress = [new BridgeTrafficWade("a", CrossingId, new(0, 0))] },
        ];
        foreach (var state in invalid)
            Assert.Throws<InvalidDataException>(() => BridgeTrafficRules.Validate(state, Map, 10, 10, agents, positions, []));
        Assert.Throws<InvalidDataException>(() => BridgeTrafficRules.Validate(valid, Map, 10, 10, agents, positions,
            [RiverBridgeRules.ToBridge(crossing!, BridgeTriggers.Traffic, 0, null)]));
    }

    [Fact]
    public void SavedTwoTileEvidenceIsValidOnlyWhileTheWaderStandsInItsWater()
    {
        var agents = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };
        var valid = new BridgeTrafficState([new BridgeTrafficWade("a", WideCrossingId, WideSouth)],
            [new BridgeTrafficCrossing(WideCrossingId, "b", 5)]);
        foreach (var standing in new[] { NearWater, FarWater })
            BridgeTrafficRules.Validate(valid, WideMap, 10, 10, agents,
                new Dictionary<string, GridPoint> { ["a"] = standing, ["b"] = new(0, 0) }, []);

        var positions = new Dictionary<string, GridPoint> { ["a"] = NearWater, ["b"] = new(0, 0) };
        Assert.True(RiverBridgeRules.TryFindCrossing(WideMap, North, 0, 1, out var crossing));
        BridgeTrafficState[] invalid =
        [
            valid with { InProgress = [new BridgeTrafficWade("a", WideCrossingId, NearWater)] },
            valid with { InProgress = [new BridgeTrafficWade("a", "bridge-1-1-ns-1", North)] },
            valid with { Completed = [new BridgeTrafficCrossing("bridge-1-1-ns-3", "b", 5)] },
        ];
        foreach (var state in invalid)
            Assert.Throws<InvalidDataException>(() => BridgeTrafficRules.Validate(state, WideMap, 10, 10, agents, positions, []));
        Assert.Throws<InvalidDataException>(() => BridgeTrafficRules.Validate(valid, WideMap, 10, 10, agents,
            new Dictionary<string, GridPoint> { ["a"] = WideSouth, ["b"] = new(0, 0) }, []));
        Assert.Throws<InvalidDataException>(() => BridgeTrafficRules.Validate(valid, WideMap, 10, 10, agents, positions,
            [RiverBridgeRules.ToBridge(crossing!, BridgeTriggers.Traffic, 0, null)]));
    }

    private static BridgeTrafficState Cross(BridgeTrafficState state, string agent, int tick, bool twoTiles = false)
    {
        if (!twoTiles)
        {
            var wading = BridgeTrafficRules.RecordStep(state, Map, agent, North, Water, tick - 1);
            return BridgeTrafficRules.RecordStep(wading, Map, agent, Water, South, tick);
        }
        var entered = BridgeTrafficRules.RecordStep(state, WideMap, agent, North, NearWater, tick - 1);
        var midstream = BridgeTrafficRules.RecordStep(entered, WideMap, agent, NearWater, FarWater, tick - 1);
        return BridgeTrafficRules.RecordStep(midstream, WideMap, agent, FarWater, WideSouth, tick);
    }
}
