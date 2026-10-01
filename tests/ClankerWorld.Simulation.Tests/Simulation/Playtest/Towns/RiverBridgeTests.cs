using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Seeded bridge and Road routing cases on small hand-drawn maps:
/// <c>.</c> meadow, <c>~</c> river, <c>L</c> lake, <c>O</c> ocean, <c>M</c> mountain.
/// </summary>
public sealed class RiverBridgeTests
{
    [Fact]
    public void OneNarrowStreamGetsAOneTileBridgeThatMovementCanUse()
    {
        var map = Map(
            "...~...",
            "...~...",
            "...~...");
        var request = Request(map, [new(0, 1)], [new(6, 1)]);
        var result = RoadRoutePlanner.Plan(request);

        var proposal = Assert.IsType<RoadRouteProposal>(result.Proposal);
        var crossing = Assert.Single(proposal.NewCrossings);
        Assert.Equal("bridge-3-1-ew-1", crossing.Id);
        Assert.Equal(BridgeDesigns.PlankSpanOne, crossing.Design);
        Assert.Equal(BridgeAxis.EastWest, crossing.Axis);
        Assert.Equal(new GridPoint(2, 1), crossing.EntranceA);
        Assert.Equal(new GridPoint(4, 1), crossing.EntranceB);
        Assert.Equal([new GridPoint(3, 1)], crossing.Span);
        Assert.Contains(crossing.EntranceA, proposal.RoadTiles);
        Assert.Contains(crossing.EntranceB, proposal.RoadTiles);
        Assert.DoesNotContain(new GridPoint(3, 1), proposal.RoadTiles);
        Assert.All(proposal.RoadTiles, tile => Assert.True(map.IsBuildable(tile)));
        Assert.Null(RoadRoutePlanner.Validate(request, proposal));

        var bridged = WithBridges(map, RiverBridgeRules.ToBridge(crossing, BridgeTriggers.Road, 0, "road:test"));
        Assert.Equal(200, map.FootTravelCost(new(3, 1)));
        Assert.Equal(100, bridged.FootTravelCost(new(3, 1)));
        Assert.True(bridged.CanFootStep(new(2, 1), new(3, 1)));
        Assert.True(bridged.CanFootStep(new(3, 1), new(4, 1)));
        Assert.False(bridged.CanFootStep(new(3, 1), new(3, 0)));
        Assert.False(bridged.CanFootStep(new(2, 0), new(3, 1)));
        Assert.False(bridged.IsBuildable(new(3, 1)));
    }

    [Fact]
    public void ThreeTileRiverLakeAndOceanAreNeverBridged()
    {
        foreach (var water in new[] { "~~~", "L", "O" })
        {
            var rows = Enumerable.Repeat("..." + water + "...", 3).ToArray();
            var map = Map(rows);
            var width = rows[0].Length;
            var result = Plan(map, [new(0, 1)], [new(width - 1, 1)]);
            Assert.Null(result.Proposal);
            Assert.Equal(RoadRouteOutcomes.RouteUnavailable, result.Outcome);
            Assert.False(RiverBridgeRules.TryFindCrossing(map, new(2, 1), 1, 0, out _));
        }
    }

    [Fact]
    public void MountainBankIsNotABridgeLanding()
    {
        var map = Map(
            "...~M..",
            "...~M..",
            "...~M..");
        Assert.False(RiverBridgeRules.TryFindCrossing(map, new(2, 1), 1, 0, out _));
        Assert.Equal(RoadRouteOutcomes.RouteUnavailable, Plan(map, [new(0, 1)], [new(6, 1)]).Outcome);
    }

    [Fact]
    public void TwoCloseSeparateStreamsEachGetTheirOwnBridge()
    {
        var map = Map(
            "...~.~...",
            "...~.~...",
            "...~.~...");
        var proposal = Assert.IsType<RoadRouteProposal>(Plan(map, [new(0, 1)], [new(8, 1)]).Proposal);
        Assert.Equal(["bridge-3-1-ew-1", "bridge-5-1-ew-1"], proposal.NewCrossings.Select(item => item.Id));

        // An existing bridge over the first stream is reused and does not
        // block the second stream's bridge next to it.
        var first = RiverBridgeRules.ToBridge(proposal.NewCrossings[0], BridgeTriggers.Road, 0, "road:first");
        var bridged = WithBridges(map, first);
        var request = Request(bridged, [new(0, 1)], [new(8, 1)], [first]);
        var second = Assert.IsType<RoadRouteProposal>(RoadRoutePlanner.Plan(request).Proposal);
        Assert.Equal([first.Id], second.UsedBridgeIds);
        Assert.Equal(["bridge-5-1-ew-1"], second.NewCrossings.Select(item => item.Id));
        Assert.Null(RoadRoutePlanner.Validate(request, second));
    }

    [Fact]
    public void StreamsJoiningDownstreamStillHaveSeparateBanks()
    {
        // Two streams meet at row 5 and leave through one channel: the land
        // between them is a separate bank, so each stream may need a bridge.
        var map = Map(
            "...~..~...",
            "...~..~...",
            "...~..~...",
            "...~..~...",
            "...~..~...",
            "...~~~~...",
            "....~.....",
            "....~.....",
            "....~.....",
            "....~.....");
        Assert.True(RiverBridgeRules.TryFindCrossing(map, new(2, 1), 1, 0, out var west));
        Assert.True(RiverBridgeRules.TryFindCrossing(map, new(5, 1), 1, 0, out var east));
        Assert.True(RiverBridgeRules.TryFindCrossing(map, new(2, 3), 1, 0, out var westLower));
        Assert.False(RiverBridgeRules.SharesBanks(map, west!, east!));
        Assert.True(RiverBridgeRules.SharesBanks(map, west!, westLower!));

        var proposal = Assert.IsType<RoadRouteProposal>(Plan(map, [new(0, 1)], [new(9, 1)]).Proposal);
        Assert.Equal(2, proposal.NewCrossings.Count);
    }

    [Fact]
    public void CrossingOverTheEastWestSeamIsStableFromEitherBank()
    {
        var map = Map(
            ".........~",
            ".........~",
            ".........~") with
        { WrapsEastWest = true };
        Assert.True(RiverBridgeRules.TryFindCrossing(map, new(8, 1), 1, 0, out var fromWest));
        Assert.True(RiverBridgeRules.TryFindCrossing(map, new(0, 1), -1, 0, out var fromEast));
        Assert.Equal(fromWest!.Id, fromEast!.Id);
        Assert.Equal(new GridPoint(8, 1), fromEast.EntranceA);
        Assert.Equal(new GridPoint(0, 1), fromEast.EntranceB);

        var proposal = Assert.IsType<RoadRouteProposal>(Plan(map, [new(1, 1)], [new(7, 1)]).Proposal);
        var crossing = Assert.Single(proposal.NewCrossings);
        Assert.Equal("bridge-9-1-ew-1", crossing.Id);
        var bridged = WithBridges(map, RiverBridgeRules.ToBridge(crossing, BridgeTriggers.Road, 0, "road:seam"));
        Assert.True(bridged.CanFootStep(new(9, 1), new(0, 1)));
        Assert.True(bridged.CanFootStep(new(8, 1), new(9, 1)));
        Assert.False(bridged.CanFootStep(new(9, 1), new(9, 2)));
    }

    [Fact]
    public void OccupiedBankMovesTheCrossingOrLeavesTheRouteUnconnected()
    {
        var map = Map(
            "...~...",
            "...~...",
            "...~...");
        var proposal = Assert.IsType<RoadRouteProposal>(
            Plan(map, [new(0, 1)], [new(6, 1)], blocked: [new(4, 1)]).Proposal);
        var crossing = Assert.Single(proposal.NewCrossings);
        Assert.DoesNotContain(new GridPoint(4, 1), crossing.Entrances);
        Assert.DoesNotContain(new GridPoint(4, 1), proposal.RoadTiles);

        var result = Plan(map, [new(0, 1)], [new(6, 1)], blocked: [new(4, 0), new(4, 1), new(4, 2)]);
        Assert.Null(result.Proposal);
        Assert.Equal(RoadRouteOutcomes.RouteUnavailable, result.Outcome);
    }

    [Fact]
    public void SavedBridgeValidationRefusesTamperedGeometryAndRoadBridgesWithoutRoads()
    {
        var map = Map(
            "...~~...",
            "...~~...",
            "...~~...");
        Assert.True(RiverBridgeRules.TryFindCrossing(map, new(2, 1), 1, 0, out var crossing));
        var bridge = RiverBridgeRules.ToBridge(crossing!, BridgeTriggers.Road, 3, "road:town:first:b");
        RiverBridgeRules.ValidateSaved([bridge], map, [new(2, 1), new(5, 1)], worldTick: 3);

        BridgeState[] invalid =
        [
            bridge with { Design = BridgeDesigns.PlankSpanOne },
            bridge with { Span = [new(3, 1)] },
            bridge with { Id = "bridge-3-1-ew-1" },
            bridge with { Trigger = "walking" },
            bridge with { BuiltTick = 4 },
            bridge with { RouteId = null },
            bridge with { Trigger = BridgeTriggers.Traffic },
        ];
        foreach (var item in invalid)
            Assert.Throws<InvalidDataException>(() => RiverBridgeRules.ValidateSaved([item], map, [new(2, 1), new(5, 1)], 3));
        Assert.Throws<InvalidDataException>(() => RiverBridgeRules.ValidateSaved([bridge], map, [new(2, 1)], 3));
        Assert.Throws<InvalidDataException>(() => RiverBridgeRules.ValidateSaved([bridge, bridge], map, [new(2, 1), new(5, 1)], 3));
        RiverBridgeRules.ValidateSaved([bridge with { Trigger = BridgeTriggers.Traffic, RouteId = null }], map, [], 3);
    }

    [Fact]
    public void StreetRunningOnCrossesANarrowRiverOnlyWhereABridgeIsAllowed()
    {
        var twoWide = Map(
            "..........",
            "...~~.....",
            "...~~.....");
        var (path, crossings) = RunOn(twoWide, [new(0, 1), new(1, 1), new(2, 1)], length: 3);
        Assert.Equal([new GridPoint(5, 1), new GridPoint(6, 1), new GridPoint(7, 1)], path);
        Assert.Equal(["bridge-3-1-ew-2"], crossings.Select(item => item.Id));

        // Without a crossing rule, as in the first Town's layout, streets keep to dry land.
        var streets = new TownStreets(twoWide, [], [new GridPoint(0, 1), new GridPoint(1, 1), new GridPoint(2, 1)]);
        Assert.Empty(streets.Wander(new(2, 1), 0, 3, 0, Pcg32XshRrV1.Create("run-on", "test")));

        // Wider water is never crossed, and a street too short to keep takes its bridge with it.
        Assert.Empty(RunOn(Map("..........", "...~~~....", "...~~~...."), [new(1, 1), new(2, 1)], length: 3).Path);
        var (shortPath, shortCrossings) = RunOn(twoWide, [new(1, 1), new(2, 1)], length: 1, minimum: 2);
        Assert.Empty(shortPath);
        Assert.Empty(shortCrossings);

        // A bridge already joining the same banks upstream refuses a second one.
        var river = Map("...~~.....", "...~~.....", "...~~.....");
        Assert.True(RiverBridgeRules.TryFindCrossing(river, new(2, 0), 1, 0, out var upstream));
        var bridged = WithBridges(river, RiverBridgeRules.ToBridge(upstream!, BridgeTriggers.Traffic, 0, null));
        Assert.Single(RunOn(river, [new(1, 2), new(2, 2)], length: 3).Crossings);
        Assert.Empty(RunOn(bridged, [new(1, 2), new(2, 2)], length: 3, existing: [upstream!]).Path);
    }

    private static (List<GridPoint> Path, List<RiverCrossing> Crossings) RunOn(SeededMap map, GridPoint[] street,
        int length, int minimum = 1, RiverCrossing[]? existing = null)
    {
        var streets = new TownStreets(map, [], street);
        var crossings = new List<RiverCrossing>();
        var path = streets.Wander(street[^1], 0, length, 0, Pcg32XshRrV1.Create("run-on", "test"), minimum,
            (from, direction) =>
            {
                var (dx, dy) = TownStreets.Directions[direction];
                return (dx == 0 || dy == 0) && RiverBridgeRules.TryFindCrossing(map, from, dx, dy, out var crossing) &&
                    !RiverBridgeRules.IsRedundant(map, crossing!, existing ?? [])
                    ? crossing : null;
            }, crossings);
        return (path, crossings);
    }

    internal static SeededMap Map(params string[] rows)
    {
        var tiles = new List<TerrainTile>();
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
                tiles.Add(new TerrainTile(new GridPoint(x, y), rows[y][x] switch
                {
                    '~' => TerrainKind.River,
                    'L' => TerrainKind.Lake,
                    'O' => TerrainKind.Ocean,
                    'M' => TerrainKind.Mountain,
                    _ => TerrainKind.Meadow,
                }));
        return new SeededMap(rows[0].Length, rows.Length, 0, tiles, [], [], "sha256:test-map");
    }

    internal static SeededMap WithBridges(SeededMap map, params BridgeState[] bridges) =>
        map with { BridgeDecks = RiverBridgeRules.Decks(bridges) };

    private static RoadRouteRequest Request(SeededMap map, GridPoint[] starts, IEnumerable<GridPoint> network,
        IReadOnlyList<BridgeState>? bridges = null, IEnumerable<GridPoint>? blocked = null) =>
        new(map, starts, network.ToHashSet(), (blocked ?? []).ToHashSet(), bridges ?? []);

    private static RoadRouteResult Plan(SeededMap map, GridPoint[] starts, GridPoint[] network,
        GridPoint[]? blocked = null) =>
        RoadRoutePlanner.Plan(Request(map, starts, network, blocked: blocked));
}
