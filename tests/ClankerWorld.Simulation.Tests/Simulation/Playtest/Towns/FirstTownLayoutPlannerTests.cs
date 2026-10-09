using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

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
        AssertStartingPlan(world.ExportState().Map, initialSite);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void RedoAfterRemovingEmptyStarterBuildingsReplacesLayoutAndPreservesStock(bool removeFarmhouse, bool reload)
    {
        var geography = new GeographyOptions("audit-town-invariants", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        var map = world.ExportState().Map;
        var initialSite = map.Resources.Single(item => item.Id == "berry-patch").Position;
        var first = world.AcceptFirstTownLayout(initialSite);
        var owners = world.WorldSimulation.Buildings.ToDictionary(building => building.InstanceId,
            building => (building.TownId, building.HouseholdId), StringComparer.Ordinal);
        var stock = InventoryCheckpointCodec.Encode(world.Society.Inventory);
        string[] removedIds = removeFarmhouse ? ["first-town-blacksmith", "first-town-farmhouse"] : ["first-town-blacksmith"];
        foreach (var id in removedIds)
        {
            var building = world.WorldSimulation.Buildings.Single(item => item.InstanceId == id);
            var removed = world.RemoveBuilding(id, building.TownId, building.HouseholdId);
            Assert.True(removed.Applied, removed.Failure);
        }
        Assert.Equal(5 - removedIds.Length, world.WorldSimulation.Buildings.Count);
        world.Validate();
        using var restored = reload ? PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()))) : null;
        var target = restored ?? world;
        Assert.True(new OwnerWorldObservationStore(target).GetSnapshot().FounderSetup?.CanChooseTownSite);
        // A neighbouring site whose plan really differs, so the redo is visible.
        var newSite = map.FootNeighbors(initialSite).First(point => point != initialSite &&
            FirstTownLayoutPlanner.Plan(map, point) is { } plan && !plan.RoadTiles.SequenceEqual(first.RoadTiles));

        var second = target.AcceptFirstTownLayout(newSite);

        Assert.Equal(stock, InventoryCheckpointCodec.Encode(target.Society.Inventory));
        Assert.False(first.RoadTiles.SequenceEqual(second.RoadTiles));
        Assert.Equal(second.RoadTiles, target.RoadTiles);
        Assert.Equal(5, target.WorldSimulation.Buildings.Count);
        foreach (var planned in second.Buildings)
        {
            var actual = target.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-" + planned.Role);
            Assert.Equal(planned.DefinitionId, actual.DefinitionId);
            Assert.Equal(planned.Position, actual.Position);
            Assert.Equal(planned.Entrance, actual.Entrance);
            Assert.Equal(owners[actual.InstanceId], (actual.TownId, actual.HouseholdId));
        }
        var town = Assert.Single(target.Towns);
        Assert.Equal(newSite, town.OriginSite);
        Assert.Equal(target.WorldSimulation.Buildings.Select(building => building.InstanceId), town.AssignedBuildingIds);
        Assert.True(town.BorderTiles.ToHashSet().SetEquals(target.TownLandTitles.SelectMany(title => title.Tiles)));
        Assert.All(target.TownLandTitles, title => Assert.Equal(town.Id, title.TownId));
        foreach (var household in target.WorldSimulation.Buildings.Where(building => building.HouseholdId is not null)
                     .GroupBy(building => building.HouseholdId, StringComparer.Ordinal))
        {
            var footprints = household.SelectMany(building => WorldContentSimulationRules.Footprint(
                target.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
            var rights = target.HouseholdLandUseRights.Where(right => right.HouseholdId == household.Key).ToArray();
            Assert.True(footprints.SetEquals(rights.SelectMany(right => right.Tiles)));
            Assert.All(rights, right =>
            {
                Assert.Equal(town.Id, right.TownId);
                Assert.Equal(TownLandRightsRules.StarterAllocationSource, right.GrantSource);
            });
        }
        Assert.Empty(target.HouseholdLandUseRequests);
        Assert.Single(target.ExportState().Events, item => item.Kind == "first_town_layout_redone");
        Assert.True(target.Society.IsPaused);
        Assert.Equal(0, target.WorldTick);
        Assert.Empty(target.FounderSetup!.FounderIds);
        target.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(target.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Theory]
    [InlineData("owner-added-blacksmith")]
    [InlineData("first-town-custom-blacksmith")]
    public void RedoDoesNotReplaceUnrelatedBuildingWork(string unrelatedId)
    {
        var geography = new GeographyOptions("audit-town-invariants", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        var site = world.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position;
        world.AcceptFirstTownLayout(site);
        var state = world.ExportState();
        const string originalId = "first-town-blacksmith";
        state = state with
        {
            WorldSimulation = state.WorldSimulation! with
            {
                Buildings = state.WorldSimulation.Buildings.Select(building => building.InstanceId == originalId
                    ? building with { InstanceId = unrelatedId } : building).OrderBy(building => building.InstanceId, StringComparer.Ordinal).ToArray(),
            },
            Towns = state.Towns!.Select(town => town with
            {
                AssignedBuildingIds = town.AssignedBuildingIds.Select(id => id == originalId ? unrelatedId : id)
                    .Order(StringComparer.Ordinal).ToArray(),
            }).ToArray(),
        };
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        var bytes = PrivateWorldRuntimeCodec.Encode(restored.ExportState());

        Assert.Throws<InvalidOperationException>(() => restored.AcceptFirstTownLayout(site));

        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Contains(restored.WorldSimulation.Buildings, building => building.InstanceId == unrelatedId);
    }

    [Fact]
    public void RedoStillResetsStarterOwnershipAfterAnEmptyBuildingIsReassigned()
    {
        var geography = new GeographyOptions("audit-town-invariants", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        var site = world.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position;
        world.AcceptFirstTownLayout(site);
        var blacksmith = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-blacksmith");
        var reassigned = world.ReassignBuilding(blacksmith.InstanceId, blacksmith.TownId, blacksmith.HouseholdId,
            targetTownId: null, targetHouseholdId: "household:camp-alpha");
        Assert.True(reassigned.Applied, reassigned.Failure);
        Assert.Equal("household:camp-alpha", world.WorldSimulation.Buildings.Single(item => item.InstanceId == blacksmith.InstanceId).HouseholdId);
        var stock = InventoryCheckpointCodec.Encode(world.Society.Inventory);

        world.AcceptFirstTownLayout(site);

        Assert.Equal(blacksmith.HouseholdId, world.WorldSimulation.Buildings.Single(item => item.InstanceId == blacksmith.InstanceId).HouseholdId);
        Assert.Equal(stock, InventoryCheckpointCodec.Encode(world.Society.Inventory));
        world.Validate();
    }

    [Fact]
    public void RedoAfterRemovalStillRequiresNoPlacedFounders()
    {
        var geography = new GeographyOptions("audit-town-invariants", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        var map = world.ExportState().Map;
        var site = map.Resources.Single(item => item.Id == "berry-patch").Position;
        var layout = world.AcceptFirstTownLayout(site);
        var blacksmith = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-blacksmith");
        Assert.True(world.RemoveBuilding(blacksmith.InstanceId, blacksmith.TownId, blacksmith.HouseholdId).Applied);
        var occupied = layout.Buildings.SelectMany(Footprint).Concat(layout.RoadTiles)
            .Concat(map.CampObjects.Select(item => item.Position)).Concat(map.Resources.Select(item => item.Position)).ToHashSet();
        var founderSite = Assert.Single(world.Towns).BorderTiles.First(point => map.IsBuildable(point) && !occupied.Contains(point));
        world.PlaceFounder("founder:00000000000000000000000000000001", founderSite);
        Assert.Single(world.FounderSetup!.FounderIds);
        Assert.False(new OwnerWorldObservationStore(world).GetSnapshot().FounderSetup?.CanChooseTownSite);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());

        Assert.Throws<InvalidOperationException>(() => world.AcceptFirstTownLayout(site));

        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Validate();
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

    private static void AssertStartingPlan(SeededMap map, GridPoint roughSite)
    {
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
            foreach (var next in TownStreets.Linked(map, roads, current))
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
        Assert.All(roads.Where(road => TownStreets.Linked(map, roads, road).Count() == 1 && !entrances.Contains(road)),
            end => Assert.InRange(StepsToDoor(map, roads, entrances, end), 1, TownStreets.RunOnTiles));
    }

    private static IEnumerable<GridPoint> Footprint(FirstTownLayoutBuilding building) =>
        Enumerable.Range(0, building.Height).SelectMany(dy => Enumerable.Range(0, building.Width)
            .Select(dx => new GridPoint(building.Position.X + dx, building.Position.Y + dy)));

    private static int StepsToDoor(SeededMap map, HashSet<GridPoint> roads, HashSet<GridPoint> entrances, GridPoint start)
    {
        var steps = new Dictionary<GridPoint, int> { [start] = 0 };
        var pending = new Queue<GridPoint>([start]);
        while (pending.TryDequeue(out var current))
        {
            if (entrances.Contains(current)) return steps[current];
            foreach (var next in TownStreets.Linked(map, roads, current))
                if (steps.TryAdd(next, steps[current] + 1)) pending.Enqueue(next);
        }
        return int.MaxValue;
    }
}
