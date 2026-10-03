using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class SeededHarnessTests
{
    public static IEnumerable<object[]> SeedCorpus =>
    [
        // Digests include the named grain-seed and wild-greens starter roster;
        // the earlier fertile-land resource was intentionally removed.
        ["camp-alpha", "38e6b29b7ffb88a05f5ad02b85a886870c02310aaf524bb0df9d8ac3970f39ab",
            "832fd6fd13ec067e8dd701202404386f3cf5578b7ee6f2a7ea096f02d7cd8f91"],
        ["camp-beta", "ec4a4154ae52135d75578abd5f8f73c91485d419095696f2baa8e5cee85ec756",
            "5d092afce379cf930bbc32da7b36813a0276625c7e9873b1079dcd9f26ea2fec"],
        ["camp-gamma", "b986e0278085a50956090a3f67fcfc5c11ee1ee199d4d3e12759289501002c67",
            "9c57833d83b33bdc7a689384123c6a3eefe4a45c6e4d26a09a475aae2c5c0c50"],
    ];

    [Theory]
    [MemberData(nameof(SeedCorpus))]
    public void FixedSeedCorpusProducesValidCanonicalMapManifest(
        string seed, string expectedManifestDigest, string expectedLegacyBedrollDigest)
    {
        var first = SeededMapGenerator.Generate(seed);
        var second = SeededMapGenerator.Generate(seed);
        var withLegacyBedroll = SeededMapGenerator.Generate(seed, includeLegacyBedroll: true);
        var repeatedLegacyBedroll = SeededMapGenerator.Generate(seed, includeLegacyBedroll: true);

        Assert.True(MapAcceptance.Validate(first).IsValid);
        Assert.True(MapAcceptance.Validate(withLegacyBedroll).IsValid);
        Assert.Equal(expectedManifestDigest, first.ManifestDigest);
        Assert.Equal(expectedLegacyBedrollDigest, withLegacyBedroll.ManifestDigest);
        Assert.Equal(MapManifestCodec.Digest(first), first.ManifestDigest);
        Assert.Equal(MapManifestCodec.Digest(withLegacyBedroll), withLegacyBedroll.ManifestDigest);
        Assert.Equal(first.ManifestDigest, second.ManifestDigest);
        Assert.Equal(withLegacyBedroll.ManifestDigest, repeatedLegacyBedroll.ManifestDigest);
        Assert.NotEqual(first.ManifestDigest, withLegacyBedroll.ManifestDigest);
        Assert.DoesNotContain(first.CampObjects, item => item.Kind == "bedroll");
        Assert.Contains(withLegacyBedroll.CampObjects, item => item.Kind == "bedroll");
        Assert.Contains(first.Resources, item => item.Id == "grain-seed-patch" &&
            item.Kind == "grain_seed" && item.NaturalObjectKind == "wild_seed_patch");
        Assert.Contains(first.Resources, item => item.Id == "wild-greens-patch" &&
            item.Kind == "food" && item.NaturalObjectKind == "wild_greens");
        Assert.DoesNotContain(first.Resources, item => item.Id == "fertile-land");
        Assert.True(MapManifestCodec.Encode(first).SequenceEqual(MapManifestCodec.Encode(second)));
        Assert.True(MapManifestCodec.Encode(withLegacyBedroll).SequenceEqual(MapManifestCodec.Encode(repeatedLegacyBedroll)));
    }

    [Fact]
    public void EqualCostRouteUsesTheDeclaredStableTieBreakOrder()
    {
        var map = TerrainMap(3, 2, _ => TerrainKind.Meadow);
        var origin = new GridPoint(0, 0);
        var destination = new GridPoint(2, 1);
        var expected = new[] { origin, new GridPoint(1, 1), destination };

        for (var run = 0; run < 10; run++)
        {
            Assert.Equal(expected, DeterministicRouteFinder.Find(map, origin, destination));
        }
    }

    [Fact]
    public void UnreachableLandReturnsNoRouteWithoutThrowingDuringObservation()
    {
        var map = TerrainMap(5, 3, point => point.X == 2 ? TerrainKind.Peak : TerrainKind.Meadow);
        var origin = new GridPoint(0, 1);
        var destination = new GridPoint(4, 1);

        Assert.False(DeterministicRouteFinder.TryFind(map, origin, destination, out var route));
        Assert.Empty(route);
        Assert.Throws<InvalidOperationException>(() => DeterministicRouteFinder.Find(map, origin, destination));
        Assert.True(DeterministicRouteFinder.TryFind(map, origin, new GridPoint(1, 1), out var reachable));
        Assert.Equal([origin, new GridPoint(1, 1)], reachable);
    }

    [Fact]
    public void DiagonalRoutesHaveAStableCostAndCannotClipBlockedCorners()
    {
        var open = TerrainMap(4, 4, _ => TerrainKind.Meadow);
        var origin = new GridPoint(0, 0);
        var diagonal = new GridPoint(1, 1);
        Assert.True(open.CanFootStep(origin, diagonal));
        Assert.Equal(141, open.FootStepCost(origin, diagonal));
        Assert.Equal(new[] { origin, diagonal, new GridPoint(2, 2) },
            DeterministicRouteFinder.Find(open, origin, new GridPoint(2, 2)));

        var wall = open with
        {
            Tiles = open.Tiles.Select(tile => tile.Position == new GridPoint(1, 0)
            ? tile with { Terrain = TerrainKind.Peak } : tile).ToArray()
        };
        Assert.False(wall.CanFootStep(origin, diagonal));
        Assert.DoesNotContain(diagonal, wall.FootNeighbors(origin));
        Assert.Equal(new[] { origin, new GridPoint(0, 1), diagonal },
            DeterministicRouteFinder.Find(wall, origin, diagonal));
    }

    [Fact]
    public void WrappedDiagonalRequiresBothShouldersAtTheSeam()
    {
        var origin = new GridPoint(0, 1);
        var destination = new GridPoint(4, 2);
        var wrapped = TerrainMap(5, 4, _ => TerrainKind.Meadow) with { WrapsEastWest = true };
        Assert.True(wrapped.CanFootStep(origin, destination));
        Assert.Contains(destination, wrapped.FootNeighbors(origin));
        Assert.Equal(new[] { origin, destination }, DeterministicRouteFinder.Find(wrapped, origin, destination));

        var blocked = wrapped with
        {
            Tiles = wrapped.Tiles.Select(tile => tile.Position == new GridPoint(4, 1)
            ? tile with { Terrain = TerrainKind.Peak } : tile).ToArray()
        };
        Assert.False(blocked.CanFootStep(origin, destination));
        Assert.DoesNotContain(destination, blocked.FootNeighbors(origin));
    }

    [Fact]
    public void NarrowRiverCanBeCrossedOnFootButNeitherRiverNorMountainCanBeBuiltOn()
    {
        var river = new GridPoint(2, 1);
        var mountain = new GridPoint(1, 0);
        var map = TerrainMap(5, 3, point => point == river ? TerrainKind.River :
            point.Y == 1 ? TerrainKind.Meadow : TerrainKind.Ocean);
        var mountainMap = TerrainMap(5, 3, point => point == mountain
            ? TerrainKind.Mountain : TerrainKind.Meadow);

        Assert.True(map.IsPassable(river));
        Assert.False(map.IsBuildable(river));
        Assert.Equal(200, map.FootTravelCost(river));
        Assert.True(map.CanFootStep(new GridPoint(1, 1), river));
        Assert.True(map.CanFootStep(river, new GridPoint(3, 1)));
        Assert.False(map.CanFootStep(new GridPoint(1, 1), new GridPoint(2, 0)));
        Assert.True(mountainMap.IsPassable(mountain));
        Assert.False(mountainMap.IsBuildable(mountain));
        Assert.Equal(200, mountainMap.FootTravelCost(mountain));
        Assert.Contains(river, DeterministicRouteFinder.Find(map, new GridPoint(0, 1), new GridPoint(4, 1)));

        var vertical = TerrainMap(3, 5, point => point == new GridPoint(1, 2)
            ? TerrainKind.River : point.X == 1 ? TerrainKind.Meadow : TerrainKind.Ocean);
        Assert.Contains(new GridPoint(1, 2), DeterministicRouteFinder.Find(vertical,
            new GridPoint(1, 0), new GridPoint(1, 4)));

        var channel = TerrainMap(5, 4, point => point.X == 2
            ? TerrainKind.River : TerrainKind.Meadow);
        Assert.True(channel.CanFootStep(new GridPoint(1, 1), new GridPoint(2, 1)));
        Assert.True(channel.CanFootStep(new GridPoint(2, 1), new GridPoint(3, 1)));
        Assert.False(channel.CanFootStep(new GridPoint(2, 1), new GridPoint(2, 2)));
        Assert.False(channel.CanFootStep(new GridPoint(1, 1), new GridPoint(2, 2)));
        Assert.False(channel.CanFootStep(new GridPoint(2, 1), new GridPoint(3, 2)));
        var alongChannel = DeterministicMovementResolver.Resolve(channel,
            [new MovementActor("walker", new GridPoint(2, 1), 0)],
            [new MovementIntent("walker", new GridPoint(2, 2))]);
        Assert.Equal(new GridPoint(2, 1), alongChannel.GetActor("walker").Position);
    }

    [Fact]
    public void LayeredTerrainRulesUseElevationAndHydrologyInsteadOfTheLegacyTerrainProjection()
    {
        const int width = 5;
        const int height = 3;
        var mountainWest = new GridPoint(1, 1);
        var river = new GridPoint(2, 1);
        var mountainEast = new GridPoint(3, 1);
        var peak = new GridPoint(4, 1);
        var index = (GridPoint point) => point.Y * width + point.X;
        var elevation = Enumerable.Repeat((byte)100, width * height).ToArray();
        var hydrology = Enumerable.Repeat((byte)WaterKind.Land, width * height).ToArray();
        var surface = Enumerable.Repeat((byte)SurfaceKind.Grass, width * height).ToArray();
        var vegetation = Enumerable.Repeat((byte)VegetationCover.Grass, width * height).ToArray();
        elevation[index(mountainWest)] = 220;
        elevation[index(mountainEast)] = 220;
        surface[index(mountainWest)] = (byte)SurfaceKind.Rock;
        surface[index(mountainEast)] = (byte)SurfaceKind.Rock;
        vegetation[index(mountainWest)] = (byte)VegetationCover.None;
        vegetation[index(mountainEast)] = (byte)VegetationCover.None;
        elevation[index(peak)] = 250;
        surface[index(peak)] = (byte)SurfaceKind.Rock;
        vegetation[index(peak)] = (byte)VegetationCover.None;
        hydrology[index(river)] = (byte)WaterKind.River;
        surface[index(river)] = (byte)SurfaceKind.Water;
        vegetation[index(river)] = (byte)VegetationCover.None;
        var layered = TerrainMap(width, height, _ => TerrainKind.Meadow) with
        {
            ElevationLevels = elevation,
            HydrologyKinds = hydrology,
            SurfaceKinds = surface,
            VegetationKinds = vegetation,
        };

        Assert.True(layered.IsPassable(mountainWest));
        Assert.Equal(200, layered.FootTravelCost(mountainWest));
        Assert.False(layered.IsBuildable(mountainWest));
        Assert.True(layered.IsPassable(river));
        Assert.Equal(200, layered.FootTravelCost(river));
        Assert.False(layered.IsBuildable(river));
        Assert.True(layered.CanFootStep(mountainWest, river));
        Assert.True(layered.CanFootStep(river, mountainEast));
        Assert.False(layered.IsPassable(peak));
        Assert.False(layered.IsBuildable(peak));
        Assert.Throws<ArgumentOutOfRangeException>(() => layered.FootTravelCost(peak));
        Assert.True(layered.IsBuildable(new GridPoint(0, 1)));
    }

    [Fact]
    public void TwoTileWideRiverIsWadedBankToBankMoreSlowlyThanAOneTileRiver()
    {
        var map = TerrainMap(6, 3, point => point.X is 2 or 3 ? TerrainKind.River : TerrainKind.Meadow);
        var westBank = new GridPoint(1, 1);
        var westWater = new GridPoint(2, 1);
        var eastWater = new GridPoint(3, 1);
        var eastBank = new GridPoint(4, 1);

        Assert.All(map.Tiles.Where(tile => tile.Terrain == TerrainKind.River), tile =>
        {
            Assert.True(map.IsPassable(tile.Position));
            Assert.False(map.IsBuildable(tile.Position));
            Assert.Equal(SeededMap.TwoTileWadingFootCost, map.FootTravelCost(tile.Position));
        });
        Assert.True(map.CanFootStep(westBank, westWater));
        Assert.True(map.CanFootStep(westWater, eastWater));
        Assert.True(map.CanFootStep(eastWater, eastBank));
        Assert.True(map.CanFootStep(eastWater, westWater));
        Assert.Equal(new[] { new GridPoint(0, 1), westBank, westWater, eastWater, eastBank, new GridPoint(5, 1) },
            DeterministicRouteFinder.Find(map, new GridPoint(0, 1), new GridPoint(5, 1)));

        // Wading never runs along the channel, nor diagonally into, through
        // or out of the water.
        Assert.False(map.CanFootStep(westWater, new GridPoint(2, 2)));
        Assert.False(map.CanFootStep(eastWater, new GridPoint(3, 0)));
        Assert.False(map.CanFootStep(westBank, new GridPoint(2, 2)));
        Assert.False(map.CanFootStep(westWater, new GridPoint(3, 2)));
        Assert.False(map.CanFootStep(eastWater, new GridPoint(4, 0)));
        var alongChannel = DeterministicMovementResolver.Resolve(map,
            [new MovementActor("walker", westWater, 0)],
            [new MovementIntent("walker", new GridPoint(2, 2))]);
        Assert.Equal(westWater, alongChannel.GetActor("walker").Position);

        // Each tile of a two-tile river is slower than a one-tile river's.
        var narrow = TerrainMap(5, 3, point => point.X == 2 ? TerrainKind.River : TerrainKind.Meadow);
        Assert.Equal(200, narrow.FootTravelCost(new GridPoint(2, 1)));
        Assert.True(SeededMap.TwoTileWadingFootCost > 200);
        Assert.Equal(2 * SeededMap.TwoTileWadingFootCost + 100, RouteCost(map, westBank, westWater, eastWater, eastBank));
        Assert.Equal(200 + 100, RouteCost(narrow, westBank, new GridPoint(2, 1), new GridPoint(3, 1)));
    }

    [Fact]
    public void ARiverOneTileThickRunningDiagonallyIsNotWadedAlongItsChannel()
    {
        // A staircase river: each pair of neighbouring water tiles has dry
        // land at both ends of its own line, but the channel runs diagonally.
        var water = new HashSet<GridPoint>
        {
            new(1, 6), new(1, 5), new(2, 5), new(2, 4), new(3, 4), new(3, 3), new(4, 3), new(4, 2), new(5, 2),
        };
        var map = TerrainMap(8, 8, point => water.Contains(point) ? TerrainKind.River : TerrainKind.Meadow);

        // Its end tiles are ordinary one-tile crossings; the corners between
        // them cannot be waded, so nobody turns inside the water.
        Assert.All(water.Where(tile => tile != new GridPoint(1, 6) && tile != new GridPoint(5, 2)),
            tile => Assert.False(map.IsPassable(tile)));
        Assert.True(map.CanFootStep(new GridPoint(0, 6), new GridPoint(1, 6)));
        Assert.False(map.CanFootStep(new GridPoint(1, 6), new GridPoint(1, 5)));
        Assert.False(map.CanFootStep(new GridPoint(1, 5), new GridPoint(2, 5)));
        Assert.False(map.CanFootStep(new GridPoint(0, 5), new GridPoint(1, 5)));
    }

    [Fact]
    public void RiversThreeTilesWideLakesAndTheSeaStayImpassableOnFoot()
    {
        var wide = TerrainMap(7, 3, point => point.X is >= 2 and <= 4 ? TerrainKind.River : TerrainKind.Meadow);
        Assert.All(wide.Tiles.Where(tile => tile.Terrain == TerrainKind.River),
            tile => Assert.False(wide.IsPassable(tile.Position)));
        Assert.False(wide.CanFootStep(new GridPoint(1, 1), new GridPoint(2, 1)));
        Assert.Throws<InvalidOperationException>(() =>
            DeterministicRouteFinder.Find(wide, new GridPoint(1, 1), new GridPoint(5, 1)));

        // Generated worlds read the water layer. Two tiles of lake or sea are
        // not a crossing, and neither is a river tile beside lake or sea water.
        var river = WaterMap(6, 3, point => point.X is 2 or 3 ? WaterKind.River : WaterKind.Land);
        Assert.True(river.IsReachableOnFoot(new GridPoint(1, 1), new GridPoint(4, 1)));
        foreach (var still in new[] { WaterKind.Lake, WaterKind.Ocean })
        {
            foreach (var west in new[] { still, WaterKind.River })
            {
                var map = WaterMap(6, 3, point => point.X == 2 ? west : point.X == 3 ? still : WaterKind.Land);
                Assert.All(map.Tiles.Where(tile => tile.Position.X is 2 or 3),
                    tile => Assert.False(map.IsPassable(tile.Position)));
                Assert.False(map.IsReachableOnFoot(new GridPoint(1, 1), new GridPoint(4, 1)));
            }
        }
    }

    [Fact]
    public void WrappedRoutesAndMovementCrossEitherSeamOnlyWhenTheWorldWraps()
    {
        var bounded = TerrainMap(5, 3, _ => TerrainKind.Meadow);
        var wrapped = bounded with { WrapsEastWest = true };
        var west = new GridPoint(0, 1);
        var east = new GridPoint(4, 1);

        Assert.Equal(new[] { west, east }, DeterministicRouteFinder.Find(wrapped, west, east));
        Assert.Equal(new[] { east, west }, DeterministicRouteFinder.Find(wrapped, east, west));
        Assert.Equal(new[] { new GridPoint(0, 0), new GridPoint(4, 0) },
            DeterministicRouteFinder.Find(wrapped, new GridPoint(0, 0), new GridPoint(4, 0)));
        Assert.Equal(5, DeterministicRouteFinder.Find(bounded, west, east).Count);

        var blockedSeam = wrapped with
        {
            Tiles = wrapped.Tiles.Select(tile => tile.Position == east
            ? tile with { Terrain = TerrainKind.Peak } : tile).ToArray()
        };
        Assert.Equal(new[] { west, new GridPoint(1, 1), new GridPoint(2, 1), new GridPoint(3, 1) },
            DeterministicRouteFinder.Find(blockedSeam, west, new GridPoint(3, 1)));

        var across = DeterministicMovementResolver.Resolve(wrapped,
            [new MovementActor("walker", west, 0)], [new MovementIntent("walker", east)]);
        var blocked = DeterministicMovementResolver.Resolve(bounded,
            [new MovementActor("walker", west, 0)], [new MovementIntent("walker", east)]);
        Assert.Equal(east, across.GetActor("walker").Position);
        Assert.Equal(west, blocked.GetActor("walker").Position);
        Assert.Empty(blocked.Events);
    }

    [Fact]
    public void WrappedSeamRiverIsWalkableOnlyWhenItsOppositeBanksAreDry()
    {
        var seamRiver = new GridPoint(0, 1);
        var map = TerrainMap(5, 3, point => point == seamRiver ? TerrainKind.River :
            point.Y != 1 && point.X is 0 or 1 ? TerrainKind.Ocean : TerrainKind.Meadow)
            with
        { WrapsEastWest = true };
        Assert.True(map.IsPassable(seamRiver));
        Assert.Equal(200, map.FootTravelCost(seamRiver));
        Assert.True(map.CanFootStep(new GridPoint(4, 1), seamRiver));
        Assert.True(map.CanFootStep(seamRiver, new GridPoint(1, 1)));
        Assert.Equal(new[] { new GridPoint(4, 1), seamRiver, new GridPoint(1, 1) },
            DeterministicRouteFinder.Find(map, new GridPoint(4, 1), new GridPoint(1, 1)));

        // Two tiles of water across the seam are waded slowly bank to bank;
        // a third tile makes the river too wide to wade. The peaks leave the
        // seam as the only way between the banks.
        SeededMap Seam(params int[] water) => TerrainMap(6, 3, point => water.Contains(point.X)
            ? point.Y == 1 ? TerrainKind.River : TerrainKind.Ocean
            : point.X == 2 ? TerrainKind.Peak : TerrainKind.Meadow) with
        { WrapsEastWest = true };
        var twoWide = Seam(5, 0);
        Assert.Equal(SeededMap.TwoTileWadingFootCost, twoWide.FootTravelCost(new GridPoint(5, 1)));
        Assert.Equal(SeededMap.TwoTileWadingFootCost, twoWide.FootTravelCost(new GridPoint(0, 1)));
        Assert.True(twoWide.CanFootStep(new GridPoint(5, 1), new GridPoint(0, 1)));
        Assert.Equal(new[] { new GridPoint(4, 1), new GridPoint(5, 1), new GridPoint(0, 1), new GridPoint(1, 1) },
            DeterministicRouteFinder.Find(twoWide, new GridPoint(4, 1), new GridPoint(1, 1)));
        var threeWide = Seam(4, 5, 0);
        foreach (var x in new[] { 4, 5, 0 })
            Assert.False(threeWide.IsPassable(new GridPoint(x, 1)));
        Assert.False(threeWide.IsReachableOnFoot(new GridPoint(3, 1), new GridPoint(1, 1)));
    }

    private static SeededMap TerrainMap(int width, int height, Func<GridPoint, TerrainKind> terrain) =>
        new(width, height, 0,
            [.. from y in Enumerable.Range(0, height)
                from x in Enumerable.Range(0, width)
                let point = new GridPoint(x, y)
                select new TerrainTile(point, terrain(point))],
            [], [], string.Empty);

    /// <summary>A map whose water comes from the hydrology layer, as in generated worlds.</summary>
    private static SeededMap WaterMap(int width, int height, Func<GridPoint, WaterKind> water) =>
        TerrainMap(width, height, point => water(point) switch
        {
            WaterKind.River => TerrainKind.River,
            WaterKind.Lake => TerrainKind.Lake,
            WaterKind.Ocean => TerrainKind.Ocean,
            _ => TerrainKind.Meadow,
        }) with
        {
            HydrologyKinds = [.. from y in Enumerable.Range(0, height)
                from x in Enumerable.Range(0, width)
                select (byte)water(new GridPoint(x, y))],
        };

    private static int RouteCost(SeededMap map, params GridPoint[] route) =>
        route.Zip(route.Skip(1), map.FootStepCost).Sum();

    [Fact]
    public void SaveReloadAndPhysicalReplayProduceTheSameFinalDigestsAsTheCleanRun()
    {
        var clean = ScriptedHarness.RunEntireSequence("camp-gamma");
        var beforeSave = ScriptedHarness.CreateGenesis("camp-gamma");
        while (beforeSave.Actor.FoodItems == 0)
        {
            Assert.True(ScriptedHarness.TryAdvanceOneAction(beforeSave, out var next));
            beforeSave = next;
        }
        var save = HarnessPersistence.Save(beforeSave);
        var loaded = HarnessPersistence.Load(save);
        var resumed = ScriptedHarness.ApplyConsume(loaded);
        var persistedFinal = HarnessPersistence.Save(resumed);

        Assert.Equal(HarnessPersistence.StateDigest(clean), HarnessPersistence.StateDigest(resumed));
        Assert.Equal(HarnessPersistence.EventDigest(clean), HarnessPersistence.EventDigest(resumed));
        Assert.True(
            persistedFinal.SnapshotBytes.SequenceEqual(HarnessPersistence.Save(resumed).SnapshotBytes));
        Assert.True(
            persistedFinal.EventLogBytes.SequenceEqual(HarnessPersistence.Save(resumed).EventLogBytes));
    }
}
