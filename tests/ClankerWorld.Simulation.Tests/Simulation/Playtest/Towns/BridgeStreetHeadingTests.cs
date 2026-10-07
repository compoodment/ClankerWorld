using System.Reflection;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class BridgeStreetHeadingTests
{
    [Theory]
    [InlineData(true, false, false, 1)]
    [InlineData(true, false, true, 1)]
    [InlineData(true, false, false, 2)]
    [InlineData(true, false, true, 2)]
    [InlineData(false, false, false, 1)]
    [InlineData(false, false, true, 1)]
    [InlineData(false, false, false, 2)]
    [InlineData(false, false, true, 2)]
    [InlineData(false, true, false, 1)]
    [InlineData(false, true, true, 1)]
    [InlineData(false, true, false, 2)]
    [InlineData(false, true, true, 2)]
    public void RunOnContinuesAwayFromEitherBridgeBank(bool seam, bool northSouth, bool reverse, int spanLength)
    {
        var (map, bridge) = Crossing(seam, northSouth, spanLength);
        var end = reverse ? bridge.EntranceA : bridge.EntranceB;
        var sign = reverse ? -1 : 1;
        var expected = Enumerable.Range(1, TownStreets.RunOnTiles).Select(step =>
            new GridPoint(end.X + (northSouth ? 0 : sign * step), end.Y + (northSouth ? sign * step : 0))).ToArray();

        var result = Extend(map, bridge.Entrances, [bridge], end, expected);

        Assert.Equal(expected, result.Tiles);
        Assert.Empty(result.Crossings);
    }

    [Theory]
    [InlineData(4, 1, false, false)]
    [InlineData(4, 1, true, false)]
    [InlineData(5, 2, false, false)]
    [InlineData(5, 2, true, false)]
    [InlineData(4, 1, false, true)]
    [InlineData(4, 1, true, true)]
    [InlineData(5, 2, false, true)]
    [InlineData(5, 2, true, true)]
    public void NarrowWrappedBridgeUsesItsCrossingDirectionEvenWhenTheOtherWayIsAsShortOrShorter(
        int width, int spanLength, bool reverse, bool pending)
    {
        var (map, bridge) = Crossing(true, false, spanLength, width);
        Assert.Equal(width - spanLength - 1, bridge.EntranceA.X);
        Assert.Equal(0, bridge.EntranceB.X);
        Assert.True(map.WrapsEastWest);
        var from = reverse ? bridge.EntranceB : bridge.EntranceA;
        var to = reverse ? bridge.EntranceA : bridge.EntranceB;

        // Both banks are too close for street clearance on these tiny maps;
        // check the directed crossing independently of that separate limit.
        var actual = Heading(map, from, to, pending ? [] : [bridge], pending ? [bridge] : []);

        Assert.Equal(reverse ? 4 : 0, actual);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    public void OrdinaryGroundLinkKeepsItsHeadingWhenABridgeSharesOnlyOneEndpoint(int dx, int dy)
    {
        var (map, bridge) = Crossing(false, false, 1);
        var from = bridge.EntranceA;
        var end = new GridPoint(from.X + dx, from.Y + dy);
        Assert.Equal(TownStreets.DirectionBetween(map, from, end), Heading(map, from, end, [bridge], []));
        // Exercise the ordinary link through the same street-extension entry point.
        var ground = RiverBridgeTests.Map(Enumerable.Repeat(new string('.', 15), 15).ToArray());
        var expected = Enumerable.Range(1, TownStreets.RunOnTiles)
            .Select(step => new GridPoint(end.X + dx * step, end.Y + dy * step)).ToArray();
        var clear = expected.Concat(expected.SelectMany(tile => new[]
        {
            new GridPoint(tile.X - dx, tile.Y), new GridPoint(tile.X, tile.Y - dy),
        }));
        var result = Extend(ground, [from, end], [], end, clear);
        Assert.Equal(expected, result.Tiles);
        Assert.Empty(result.Crossings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BridgeRunOnStopsAtBlockedLandWithoutLayingAnotherBridge(int clearTiles)
    {
        var (map, bridge) = Crossing(true, false, 1);
        var end = bridge.EntranceB;
        var expected = Enumerable.Range(1, clearTiles).Select(step => new GridPoint(step, end.Y)).ToArray();

        var result = Extend(map, bridge.Entrances, [bridge], end, expected);

        Assert.Equal(expected, result.Tiles);
        Assert.Empty(result.Crossings);
    }

    [Fact]
    public void RunOnStopsAtTheBoundedMapEdge()
    {
        var (map, bridge) = Crossing(false, false, 1, width: 9);
        var end = bridge.EntranceB;
        var straight = Enumerable.Range(1, TownStreets.RunOnTiles)
            .Select(step => end with { X = end.X + step }).ToArray();

        var result = Extend(map, bridge.Entrances, [bridge], end, straight);

        Assert.Equal([new GridPoint(8, end.Y)], result.Tiles);
        Assert.Empty(result.Crossings);
    }

    [Fact]
    public void BridgeEndpointAlreadyThreeRoadStepsPastADoorDoesNotRunOn()
    {
        var (map, bridge) = Crossing(false, false, 1);
        var door = bridge.EntranceA with { X = bridge.EntranceA.X - 2 };
        GridPoint[] roads = [door, bridge.EntranceA with { X = bridge.EntranceA.X - 1 }, .. bridge.Entrances];
        var clear = Enumerable.Range(1, TownStreets.RunOnTiles)
            .Select(step => bridge.EntranceB with { X = bridge.EntranceB.X + step }).ToArray();

        var result = Extend(map, roads, [bridge], door, clear);

        Assert.Empty(result.Tiles);
        Assert.Empty(result.Crossings);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    public void GroundAndBridgeLinksToTheSameNeighborDoNotBecomeADeadEnd(int width, int spanLength)
    {
        var (map, bridge) = Crossing(true, false, spanLength, width);
        Assert.Contains(bridge.EntranceB, TownStreets.Linked(map, bridge.Entrances.ToHashSet(), bridge.EntranceA));

        var result = Extend(map, bridge.Entrances, [bridge], bridge.EntranceB,
            map.Tiles.Where(tile => map.IsBuildable(tile.Position)).Select(tile => tile.Position));

        Assert.Empty(result.Tiles);
        Assert.Empty(result.Crossings);
    }

    [Fact]
    public async Task PlacingADoorAtTheEastSeamBridgeEntranceExtendsOntoClearLandAndReplaysAfterReload()
    {
        var geography = new GeographyOptions("street-seam-audit-10", WorldSizePreset.Small,
            WaterPercent: 50, HydrologyVersion: 1);
        using var setup = new PrivateWorldRuntime(geography.Seed,
            _ => new ActionCoverageRecorder(chooseIdle: true),
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var map = setup.ExportState().Map;
        var anchor = NormalPathWorld.FindStartingTownSite(map);
        setup.InitializeFirstTownContent();
        setup.AcceptFirstTownLayout(anchor);
        var blocked = map.Resources.Select(item => item.Position).Concat(map.CampObjects.Select(item => item.Position))
            .Concat(setup.RoadTiles).Concat(setup.WorldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(setup.WorldContent.Buildings.Single(definition =>
                    definition.CanonicalId == building.DefinitionId), building))).ToHashSet();
        var founders = map.Tiles.Select(tile => tile.Position).Where(point => map.IsBuildable(point) &&
            !blocked.Contains(point) && Math.Abs(point.X - anchor.X) <= 5 && Math.Abs(point.Y - anchor.Y) <= 5)
            .Take(PrivateWorldRuntime.RequiredFounders).ToArray();
        Assert.Equal(PrivateWorldRuntime.RequiredFounders, founders.Length);
        for (var index = 0; index < founders.Length; index++)
            setup.PlaceFounder($"founder:{index + 1:D32}", founders[index]);
        setup.StartWorld();
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        Assert.True(RiverBridgeRules.TryResolve(state.Map, "bridge-255-15-ew-1", out var crossing));
        var bridge = RiverBridgeRules.ToBridge(crossing!, BridgeTriggers.Road, setup.WorldTick, "road:town:first:audit");
        Assert.Equal([new GridPoint(254, 15), new GridPoint(0, 15)], bridge.Entrances);
        var roads = state.RoadTiles!.Concat(bridge.Entrances).Distinct().OrderBy(point => point.Y)
            .ThenBy(point => point.X).ToArray();
        var town = Assert.Single(state.Towns!);
        state = state with
        {
            Bridges = [bridge],
            RoadTiles = roads,
            Towns = [town with { BorderTiles = TownBorderRules.Expand(state.Map, town, bridge.Entrances) }],
        };
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
            _ => new ActionCoverageRecorder(chooseIdle: true));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
            _ => new ActionCoverageRecorder(chooseIdle: true));
        world.Validate();
        var clear = new GridPoint[] { new(1, 15), new(2, 15), new(3, 15) };
        Assert.All(clear, tile =>
        {
            Assert.True(state.Map.IsBuildable(tile));
            Assert.DoesNotContain(tile, blocked);
            Assert.DoesNotContain(tile, world.RoadTiles);
        });
        var workshop = world.WorldContent.Buildings.Single(definition => definition.LocalId == "workshop");

        var placed = world.PlaceBuilding("seam-door", workshop.CanonicalId, new GridPoint(0, 14));
        var replayed = replay.PlaceBuilding("seam-door", workshop.CanonicalId, new GridPoint(0, 14));

        Assert.True(placed.Applied, placed.Failure);
        Assert.Equal(placed, replayed);
        Assert.Equal(new GridPoint(0, 15), world.WorldSimulation.Buildings.Single(building =>
            building.InstanceId == "seam-door").Entrance);
        world.Validate();
        var after = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(after),
            _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.All(clear, tile => Assert.Contains(tile, world.RoadTiles));
        Assert.Equal(state.Bridges!.Select(item => item.Id), world.Bridges.Select(item => item.Id));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "town_road_extended" &&
            item.Detail.StartsWith("town:first:seam-door:", StringComparison.Ordinal));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();
    }

    private static (SeededMap Map, RiverCrossing Bridge) Crossing(
        bool seam, bool northSouth, int spanLength, int width = 15)
    {
        var waterStart = seam ? width - spanLength : 6;
        var rows = Enumerable.Range(0, 15).Select(y => new string(Enumerable.Range(0, width)
            .Select(x => (northSouth ? y : x) >= waterStart &&
                         (northSouth ? y : x) < waterStart + spanLength ? '~' : '.').ToArray())).ToArray();
        var map = RiverBridgeTests.Map(rows) with { WrapsEastWest = seam };
        var first = northSouth ? new GridPoint(7, waterStart - 1) : new GridPoint(waterStart - 1, 7);
        Assert.True(RiverBridgeRules.TryFindCrossing(map, first, northSouth ? 0 : 1, northSouth ? 1 : 0,
            out var crossing));
        return (map, Assert.IsType<RiverCrossing>(crossing));
    }

    private static int Heading(SeededMap map, GridPoint from, GridPoint to,
        IReadOnlyList<RiverCrossing> existing, IReadOnlyList<RiverCrossing> pending) =>
        (int)typeof(PrivateWorldRuntime).GetMethod("RoadHeading", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [map, from, to, existing, pending])!;

    private static (List<GridPoint> Tiles, List<RiverCrossing> Crossings) Extend(
        SeededMap map, IEnumerable<GridPoint> roads, IReadOnlyList<RiverCrossing> bridges,
        GridPoint door, IEnumerable<GridPoint> clear)
    {
        // Hand-drawn geometry isolates run-on limits; the generated-world test
        // above covers public placement, validation and saved continuation.
        using var world = new PrivateWorldRuntime("bridge-street-run-on",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var building = new PlacedBuilding("run-on-door", "geometry-only", new(0, 0), 0, Entrance: door);
        var roadTiles = roads.ToHashSet();
        var allowed = clear.Concat(roadTiles).ToHashSet();
        var occupied = map.Tiles.Select(tile => tile.Position).Where(tile => !allowed.Contains(tile)).ToHashSet();
        void SetField(string name, object value) => typeof(PrivateWorldRuntime)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, value);
        SetField("map", map);
        SetField("roadTiles", roadTiles);
        SetField("bridges", bridges.Select(bridge => RiverBridgeRules.ToBridge(bridge, BridgeTriggers.Road, 0, "road:test")).ToList());
        SetField("worldSimulation", WorldContentSimulationState.Empty with { Buildings = [building] });
        var result = typeof(PrivateWorldRuntime).GetMethod("ExtendStreetsPastDoors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(world, [building, occupied]);
        return Assert.IsType<(List<GridPoint> Tiles, List<RiverCrossing> Crossings)>(result);
    }
}
