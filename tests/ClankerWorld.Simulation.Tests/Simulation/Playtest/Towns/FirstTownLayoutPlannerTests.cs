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
    public void StartingPlanHasFiveLegalBuildingsAndAConnectedRoadNetwork(string seed)
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

        var occupied = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position)).ToHashSet();
        foreach (var building in plan.Buildings)
        {
            for (var dy = 0; dy < building.Height; dy++)
            {
                for (var dx = 0; dx < building.Width; dx++)
                {
                    var tile = new GridPoint(building.Position.X + dx, building.Position.Y + dy);
                    Assert.True(map.IsBuildable(tile));
                    Assert.True(occupied.Add(tile));
                }
            }
        }

        var roads = plan.RoadTiles.ToHashSet();
        Assert.Empty(roads.Intersect(occupied));
        Assert.NotEmpty(plan.RoadTiles);
        var firstRoad = plan.RoadTiles[0];
        var reachable = new HashSet<GridPoint> { firstRoad };
        var pending = new Queue<GridPoint>();
        pending.Enqueue(firstRoad);
        while (pending.TryDequeue(out var current))
        {
            foreach (var next in map.FootNeighbors(current).Where(point => roads.Contains(point) &&
                         !map.IsDiagonalFootStep(current, point)))
                if (reachable.Add(next)) pending.Enqueue(next);
        }
        Assert.True(roads.SetEquals(reachable));
        Assert.All(plan.Buildings, building =>
            Assert.Contains(roads, road => Enumerable.Range(0, building.Height)
                .SelectMany(dy => Enumerable.Range(0, building.Width)
                    .Select(dx => new GridPoint(building.Position.X + dx, building.Position.Y + dy)))
                .Any(tile => Math.Abs(road.X - tile.X) + Math.Abs(road.Y - tile.Y) == 1)));
        Assert.All(plan.RoadTiles, point => Assert.True(map.IsBuildable(point)));
    }
}
