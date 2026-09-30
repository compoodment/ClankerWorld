using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class FirstTownLayoutPlannerTests
{
    [Fact]
    public void AcceptedPausedLayoutPersistsAndCanBeRedoneBeforeFounders()
    {
        var geography = new GeographyOptions("starter-layout-accept", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        Assert.Empty(world.Towns);
        Assert.Empty(world.ExportState().Map.CampObjects);
        var initialSite = world.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position;
        var first = world.AcceptFirstTownLayout(initialSite);
        Assert.Equal(5, first.Buildings.Count);
        Assert.Equal(5, world.WorldSimulation.Buildings.Count);
        Assert.Equal(initialSite, Assert.Single(world.Towns).OriginSite);
        AssertStarterStock(world);
        Assert.Equal(0, world.WorldTick);
        Assert.True(world.Society.IsPaused);
        world.Validate();

        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(first.RoadTiles, restored.RoadTiles);
        Assert.Equal(first.Buildings.OrderBy(building => "first-town-" + building.Role, StringComparer.Ordinal)
                .Select(building => (GridPoint?)building.Entrance),
            restored.WorldSimulation.Buildings.Select(building => building.Entrance));
        var newSite = restored.ExportState().Map.FootNeighbors(initialSite)
            .First(point => point != initialSite &&
                FirstTownLayoutPlanner.Plan(restored.ExportState().Map, point) is not null);
        var second = restored.AcceptFirstTownLayout(newSite);
        Assert.Equal(5, second.Buildings.Count);
        Assert.Equal(newSite, Assert.Single(restored.Towns).OriginSite);
        Assert.Equal(5, restored.WorldSimulation.Buildings.Count);
        AssertStarterStock(restored);
        Assert.Equal(1, restored.Society.Inventory.Lots.Count(lot => lot.Id == "first-town-wooden-axe"));
        Assert.Equal(1, restored.Society.Inventory.Lots.Count(lot => lot.Id == "first-town-wooden-pickaxe"));
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "first_town_layout_redone");
        restored.Validate();
    }

    [Fact]
    public void RestoreRepairsOnlyRoadTilesInsideSavedBuildingFootprintsAndIsIdempotent()
    {
        var geography = new GeographyOptions("road-compatibility", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        world.AcceptFirstTownLayout(world.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position);
        var state = world.ExportState();
        var overlap = state.WorldSimulation!.Buildings[0].Position;
        var damaged = state with { RoadTiles = state.RoadTiles!.Append(overlap).Distinct().OrderBy(p => p.Y).ThenBy(p => p.X).ToArray() };
        var originalBytes = PrivateWorldRuntimeCodec.Encode(damaged);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(originalBytes));
        var repaired = restored.ExportState();
        Assert.Equal(state.RoadTiles, repaired.RoadTiles);
        Assert.Equal(state.WorldSimulation.Buildings, repaired.WorldSimulation!.Buildings);
        Assert.Equal(originalBytes, PrivateWorldRuntimeCodec.Encode(repaired with { RoadTiles = damaged.RoadTiles, Events = damaged.Events }));
        Assert.Equal(originalBytes, PrivateWorldRuntimeCodec.Encode(damaged));
        Assert.Single(repaired.Events, item => item.Kind == "saved_road_footprints_repaired");
        using var twice = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(repaired)));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(repaired), PrivateWorldRuntimeCodec.Encode(twice.ExportState()));
    }

    private static void AssertStarterStock(PrivateWorldRuntime world)
    {
        var stock = world.Society.Inventory;
        Assert.Equal("first-town-house-a", stock.GetLot("food:camp-alpha").StorageBuildingId);
        Assert.Equal("first-town-house-b", stock.GetLot("food:camp-beta").StorageBuildingId);
        Assert.True(stock.GetLot("food:camp-alpha").Quantity > 0);
        Assert.True(stock.GetLot("food:camp-beta").Quantity > 0);
        Assert.Equal("first-town-warehouse", stock.GetLot("first-town-wooden-axe").StorageBuildingId);
        Assert.Equal("first-town-warehouse", stock.GetLot("first-town-wooden-pickaxe").StorageBuildingId);
        Assert.Equal("town:first", stock.GetLot("first-town-wooden-axe").OwnerId);
        Assert.Equal("town:first", stock.GetLot("first-town-wooden-pickaxe").OwnerId);
        Assert.Equal("wooden_axe", stock.GetLot("first-town-wooden-axe").ItemKind);
        Assert.Equal("wooden_pickaxe", stock.GetLot("first-town-wooden-pickaxe").ItemKind);
        Assert.Equal(1, stock.GetLot("first-town-wooden-axe").Quantity);
        Assert.Equal(1, stock.GetLot("first-town-wooden-pickaxe").Quantity);
    }

    [Theory]
    [InlineData("starter-layout-one")]
    [InlineData("starter-layout-two")]
    [InlineData("starter-layout-three")]
    [InlineData("starter-layout-four")]
    [InlineData("starter-layout-five")]
    [InlineData("starter-layout-six")]
    public void StartingPlanLaysStreetsFirstWithEveryDoorFacingARoad(string seed)
    {
        var map = GeneratedCampMapGenerator.Generate(new GeographyOptions(seed, WorldSizePreset.Small));
        var roughSite = map.Resources.Single(item => item.Id == "berry-patch").Position;
        var plan = FirstTownLayoutPlanner.Plan(map, roughSite);
        Assert.NotNull(plan);
        var repeated = FirstTownLayoutPlanner.Plan(map, roughSite);
        Assert.NotNull(repeated);
        Assert.Equal(plan.Buildings, repeated.Buildings);
        Assert.Equal(plan.RoadTiles, repeated.RoadTiles);
        Assert.Equal(["warehouse", "house-a", "house-b", "farmhouse", "blacksmith"],
            plan.Buildings.Select(building => building.Role).ToArray());

        var blocked = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position)).ToHashSet();
        var footprints = new HashSet<GridPoint>();
        foreach (var building in plan.Buildings)
        {
            var own = Footprint(building).ToArray();
            // Footprints are clear, buildable and keep a one-tile gap from each other.
            Assert.All(own, tile => Assert.True(map.IsBuildable(tile) && !blocked.Contains(tile)));
            Assert.DoesNotContain(own.SelectMany(tile => TownStreets.Directions
                .Select(step => new GridPoint(tile.X + step.X, tile.Y + step.Y)).Append(tile)), footprints.Contains);
            footprints.UnionWith(own);
        }

        var roads = plan.RoadTiles.ToHashSet();
        Assert.NotEmpty(roads);
        Assert.Empty(roads.Intersect(footprints));
        Assert.All(plan.RoadTiles, point => Assert.True(map.IsBuildable(point) && !blocked.Contains(point)));

        // One connected network, where a diagonal step joins two Roads only
        // when both corner tiles are clear ground.
        var reachable = new HashSet<GridPoint> { plan.RoadTiles[0] };
        var pending = new Queue<GridPoint>(reachable);
        while (pending.TryDequeue(out var current))
        {
            foreach (var next in TownStreets.Linked(roads, current))
            {
                if (next.X != current.X && next.Y != current.Y)
                {
                    Assert.True(map.CanFootStep(current, next));
                    Assert.DoesNotContain(new GridPoint(next.X, current.Y), blocked);
                    Assert.DoesNotContain(new GridPoint(current.X, next.Y), blocked);
                }
                if (reachable.Add(next)) pending.Enqueue(next);
            }
        }
        Assert.True(roads.SetEquals(reachable));

        // Streets never run side by side: no two-by-two block of Road.
        Assert.DoesNotContain(roads, road => roads.Contains(new GridPoint(road.X + 1, road.Y)) &&
            roads.Contains(new GridPoint(road.X, road.Y + 1)) && roads.Contains(new GridPoint(road.X + 1, road.Y + 1)));

        // Each building's door faces a Road tile directly beside one edge.
        Assert.All(plan.Buildings, building =>
        {
            Assert.Contains(building.Entrance, roads);
            var besideColumn = building.Entrance.X >= building.Position.X &&
                building.Entrance.X < building.Position.X + building.Width;
            var besideRow = building.Entrance.Y >= building.Position.Y &&
                building.Entrance.Y < building.Position.Y + building.Height;
            Assert.True(besideColumn && (building.Entrance.Y == building.Position.Y - 1 ||
                    building.Entrance.Y == building.Position.Y + building.Height) ||
                besideRow && (building.Entrance.X == building.Position.X - 1 ||
                    building.Entrance.X == building.Position.X + building.Width));
        });

        // Every dead end runs on at most a few tiles past the nearest door.
        var entrances = plan.Buildings.Select(building => building.Entrance).ToHashSet();
        Assert.All(roads.Where(road => TownStreets.Linked(roads, road).Count() == 1 && !entrances.Contains(road)),
            end => Assert.InRange(StepsToDoor(roads, entrances, end), 1, TownStreets.RunOnTiles));
    }

    [Fact]
    public void AcceptedTownBorderKeepsThreeTilesOfLandAroundBuildingsAndRoads()
    {
        var geography = new GeographyOptions("starter-layout-border", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        var map = world.ExportState().Map;
        var plan = world.AcceptFirstTownLayout(map.Resources.Single(item => item.Id == "berry-patch").Position);
        var border = Assert.Single(world.Towns).BorderTiles.ToHashSet();
        var core = plan.Buildings.SelectMany(Footprint).Concat(plan.RoadTiles).ToArray();

        // Every land tile up to three tiles straight out from the Town is inside.
        foreach (var tile in core)
            for (var reach = -TownBorderRules.SpareTileMargin; reach <= TownBorderRules.SpareTileMargin; reach++)
                foreach (var near in new[] { new GridPoint(tile.X + reach, tile.Y), new GridPoint(tile.X, tile.Y + reach) })
                    if (map.IsLand(near)) Assert.Contains(near, border);
        // It follows the Town's shape: no water, and nothing far from a building or Road.
        Assert.All(border, tile =>
        {
            Assert.True(map.IsLand(tile));
            Assert.Contains(core, near => Math.Abs(near.X - tile.X) + Math.Abs(near.Y - tile.Y) <= TownBorderRules.SpareTileMargin + 1);
        });
        world.Validate();
    }

    private static IEnumerable<GridPoint> Footprint(FirstTownLayoutBuilding building) =>
        Enumerable.Range(0, building.Height).SelectMany(dy => Enumerable.Range(0, building.Width)
            .Select(dx => new GridPoint(building.Position.X + dx, building.Position.Y + dy)));

    private static int StepsToDoor(HashSet<GridPoint> roads, HashSet<GridPoint> entrances, GridPoint start)
    {
        var steps = new Dictionary<GridPoint, int> { [start] = 0 };
        var pending = new Queue<GridPoint>([start]);
        while (pending.TryDequeue(out var current))
        {
            if (entrances.Contains(current)) return steps[current];
            foreach (var next in TownStreets.Linked(roads, current))
                if (steps.TryAdd(next, steps[current] + 1)) pending.Enqueue(next);
        }
        return int.MaxValue;
    }
}
