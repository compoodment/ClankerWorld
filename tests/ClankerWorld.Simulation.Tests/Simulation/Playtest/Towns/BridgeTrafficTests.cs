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

    [Fact]
    public void OnlyAStepOntoTheFarBankCompletesACrossing()
    {
        var wading = BridgeTrafficRules.RecordStep(BridgeTrafficState.Empty, Map, "a", North, Water, 1);
        Assert.Equal(new BridgeTrafficWade("a", CrossingId, North), Assert.Single(wading.InProgress));
        Assert.Empty(wading.Completed);

        var crossed = BridgeTrafficRules.RecordStep(wading, Map, "a", Water, South, 3);
        Assert.Empty(crossed.InProgress);
        Assert.Equal(new BridgeTrafficCrossing(CrossingId, "a", 3), Assert.Single(crossed.Completed));

        var turnedBack = BridgeTrafficRules.RecordStep(wading, Map, "a", Water, North, 3);
        Assert.True(turnedBack.IsEmpty);
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

    [Fact]
    public void BridgeNeedsSixCrossingsByTwoAgentsWithinTwoDays()
    {
        var state = BridgeTrafficState.Empty;
        for (var tick = 1; tick <= 12; tick += 2)
            state = Cross(state, "a", tick);
        Assert.Equal(BridgeTrafficRules.MaximumCrossingsPerAgent, state.Completed.Count);
        Assert.Equal([3L, 5, 7, 9, 11], state.Completed.Select(item => item.Tick));
        Assert.Empty(BridgeTrafficRules.ReadyCrossings(state));

        var ready = Cross(state, "b", 14);
        Assert.Equal([CrossingId], BridgeTrafficRules.ReadyCrossings(ready));

        // Two world days at ten ticks a day: by tick 32 the first agent's
        // crossings have expired and the evidence falls below the threshold.
        var expired = BridgeTrafficRules.Prune(Cross(state, "b", 30), Map, 32, ticksPerDay: 10,
            new Dictionary<string, GridPoint>());
        Assert.Equal(["b"], expired.Completed.Select(item => item.AgentId).Distinct());
        Assert.Empty(BridgeTrafficRules.ReadyCrossings(expired));

        Assert.True(BridgeTrafficRules.Forget(ready, CrossingId).IsEmpty);
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

    private static BridgeTrafficState Cross(BridgeTrafficState state, string agent, int tick)
    {
        var wading = BridgeTrafficRules.RecordStep(state, Map, agent, North, Water, tick - 1);
        return BridgeTrafficRules.RecordStep(wading, Map, agent, Water, South, tick);
    }
}
