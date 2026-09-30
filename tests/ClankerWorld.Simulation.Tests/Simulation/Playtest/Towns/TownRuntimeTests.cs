using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Viewer.Observation;
using System.Text.Json;
using GodotOwnerWorldSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownRuntimeTests
{
    private static readonly JsonSerializerOptions GodotJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AddedIslandAdultCanHarvestAdjacentFoodOutsideTheCampComponent()
    {
        var geography = new GeographyOptions("island-food-review-0", WorldSizePreset.Small);
        using var setup = new PrivateWorldRuntime(geography.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        PlaceFourFounders(setup);
        setup.StartWorld();
        var position = new GridPoint(22, 13);
        setup.AddAgent("agent:00000000000000000000000000000099", position);
        var before = setup.ExportState();
        // Exercise reachable food while the island resident actually needs a food errand.
        using var world = PrivateWorldRuntime.Restore(before with
        {
            Inhabitants = before.Inhabitants.Select(person => person with { HungerBasisPoints = 4_000 }).ToArray(),
        });
        var food = before.Map.Resources.Single(item => item.Id == "wild-16-0");
        Assert.False(before.Map.IsReachableFromCampOnFoot(food.Position));
        Assert.True(before.Map.IsReachableOnFoot(position, food.Position));
        Assert.False(before.Map.IsReachableOnFoot(position, before.Map.Resources.Single(item => item.Id == "berry-patch").Position));
        for (var tick = 0; tick < 10; tick++) _ = await world.AdvanceOneTickAsync();
        Assert.Contains(world.ExportState().Events, item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith("agent:00000000000000000000000000000099:", StringComparison.Ordinal));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "movement_blocked" &&
            item.Detail.StartsWith("agent:00000000000000000000000000000099:no_route", StringComparison.Ordinal));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.True(restored.ExportState().Map.IsReachableOnFoot(position, food.Position));
    }

    [Fact]
    public void OlderCampWorldWithoutSavedTownRebuildsItsResidentBorder()
    {
        using var world = new PrivateWorldRuntime("older-camp-town", startPace: WorldStartPace.FounderSetup);
        var founderIds = new[]
        {
            "founder:00000000000000000000000000000001",
            "founder:00000000000000000000000000000002",
            "founder:00000000000000000000000000000003",
            "founder:00000000000000000000000000000004",
        };
        var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < founderIds.Length; index++) world.PlaceFounder(founderIds[index], positions[index]);
        var expected = Assert.Single(world.Towns);
        var earlierCheckpoint = world.ExportState() with { SchemaVersion = 20, Towns = null };

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(earlierCheckpoint)));
        var town = Assert.Single(restored.Towns);
        Assert.Equal(expected.ResidentIds, town.ResidentIds);
        Assert.Equal(expected.BorderTiles, town.BorderTiles);
    }

    [Fact]
    public async Task PausedFounderTownMembershipAndBordersSurviveSaveLoadAndProjectToOwnerAndTelemetry()
    {
        var geography = new GeographyOptions("first-town-persistence", WorldSizePreset.Small);
        var directory = Directory.CreateTempSubdirectory("clankerworld-town-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"),
                newWorldPace: WorldStartPace.FounderSetup, newWorldGeography: geography);
            using var world = file.LoadOrCreate(geography.Seed);
            Assert.Empty(world.Towns);
            world.InitializeFirstTownContent();
            world.AcceptFirstTownLayout(world.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position);
            var foundingTown = Assert.Single(world.Towns);
            Assert.Equal(TownBorderRules.FirstTownId, foundingTown.Id);
            Assert.Equal(TownBorderRules.FirstTownName, foundingTown.Name);
            Assert.Equal("founding", foundingTown.FoundingState);
            Assert.Empty(foundingTown.ResidentIds);
            Assert.Equal(5, foundingTown.AssignedBuildingIds.Count);
            Assert.NotEmpty(foundingTown.BorderTiles);
            Assert.True(world.Society.IsPaused);

            var founderIds = new List<string>();
            var founderPositions = FounderPositions(world);
            for (var index = 0; index < founderPositions.Length; index++)
            {
                var founderId = "founder:" + Guid.NewGuid().ToString("N");
                founderIds.Add(founderId);
                world.PlaceFounder(founderId, founderPositions[index]);
            }

            Assert.True(world.Society.IsPaused);
            var populatedTown = Assert.Single(world.Towns);
            Assert.Equal("founding", populatedTown.FoundingState);
            Assert.Equal(founderIds.Order(StringComparer.Ordinal), populatedTown.ResidentIds);
            var ownerSnapshot = new OwnerWorldObservationStore(world).GetSnapshot();
            var projected = Assert.Single(ownerSnapshot.Towns);
            Assert.Equal(populatedTown.Id, projected.Id);
            Assert.Equal(populatedTown.Name, projected.Name);
            Assert.Equal("founding", projected.FoundingState);
            Assert.Equal(populatedTown.ResidentIds, projected.ResidentIds);
            Assert.Equal(populatedTown.BorderTiles.Select(point => (point.X, point.Y)),
                projected.BorderTiles.Select(point => (point.X, point.Y)));
            var godotSnapshot = JsonSerializer.Deserialize<GodotOwnerWorldSnapshot>(
                JsonSerializer.Serialize(ownerSnapshot, GodotJsonOptions), GodotJsonOptions);
            var godotTown = Assert.Single(godotSnapshot!.Towns);
            Assert.Equal(populatedTown.Name, godotTown.Name);
            Assert.Equal(populatedTown.ResidentIds, godotTown.ResidentIds);
            Assert.Equal(populatedTown.BorderTiles.Select(point => (point.X, point.Y)),
                godotTown.BorderTiles.Select(point => (point.X, point.Y)));

            Assert.True(world.RenameAgent(founderIds[0], "credential-secret-token"));
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using (var service = new PrivateWorldRuntimeService(world, file,
                       new OwnerClientPresenceLease(TimeSpan.FromSeconds(30)), logger))
            {
                await service.StartAsync(CancellationToken.None);
                await service.StopAsync(CancellationToken.None);
            }
            Assert.Contains(logger.Messages, message => message.Contains(
                "town_transition tick=0 town=town:first transition=StateLoaded residents=4 buildings=5 border_tiles=",
                StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("credential-secret-token", StringComparison.Ordinal));

            file.Save(world);
            using var reloaded = file.LoadOrCreate(geography.Seed);
            var restoredTown = Assert.Single(reloaded.Towns);
            Assert.Equal(populatedTown.Id, restoredTown.Id);
            Assert.Equal(populatedTown.Name, restoredTown.Name);
            Assert.Equal(populatedTown.FoundingState, restoredTown.FoundingState);
            Assert.Equal(populatedTown.ResidentIds, restoredTown.ResidentIds);
            Assert.Equal(populatedTown.BorderTiles, restoredTown.BorderTiles);

        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TownAssignedBuildingExpandsTheBorderAndSurvivesOwnerProjectionAndReload()
    {
        var geography = new GeographyOptions("first-town-growth", WorldSizePreset.Small);
        var directory = Directory.CreateTempSubdirectory("clankerworld-town-growth-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"),
                newWorldPace: WorldStartPace.FounderSetup, newWorldGeography: geography);
            using var world = file.LoadOrCreate(geography.Seed);
            PlaceFourFounders(world);
            world.StartWorld();
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var starterRoads = world.RoadTiles.ToHashSet();
            Assert.NotEmpty(starterRoads);

            var definition = world.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
            var town = Assert.Single(world.Towns);
            var map = world.ExportState().Map;
            var occupied = map.CampObjects.Select(item => item.Position)
                .Concat(map.Resources.Select(item => item.Position))
                .Concat(world.WorldSimulation.Buildings.SelectMany(building =>
                {
                    var size = world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
                    return WorldContentSimulationRules.Footprint(size, building.Position);
                }))
                .ToHashSet();
            var position = map.Tiles.Select(tile => tile.Position).First(point =>
                map.IsBuildable(point) && !occupied.Contains(point) && !starterRoads.Contains(point) &&
                !town.BorderTiles.Contains(point) &&
                TownBorderRules.IsWithinOrAdjacent(town, point, definition.Width, definition.Height) &&
                TownBorderRules.ExpandForBuilding(map, town, point, definition.Width, definition.Height).Count > town.BorderTiles.Count);

            var result = world.PlaceBuilding("town-border-test", definition.CanonicalId, position);
            Assert.True(result.Applied, result.Failure);
            var placed = Assert.Single(world.WorldSimulation.Buildings, item => item.InstanceId == result.InstanceId);
            Assert.Equal(TownBorderRules.FirstTownId, placed.TownId);
            var grownTown = Assert.Single(world.Towns);
            Assert.Contains(placed.InstanceId, grownTown.AssignedBuildingIds);
            Assert.True(grownTown.BorderTiles.Count > town.BorderTiles.Count);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "town_building_assigned");
            Assert.Contains(world.ExportState().Events, item => item.Kind == "town_border_expanded");
            Assert.NotEmpty(world.RoadTiles);
            Assert.DoesNotContain(position, world.RoadTiles);
            Assert.Contains(world.RoadTiles, road => map.FootNeighbors(position).Contains(road) &&
                !map.IsDiagonalFootStep(position, road));
            Assert.Empty(world.RoadTiles.Intersect(world.WorldSimulation.Buildings.SelectMany(building =>
            {
                var size = world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
                return WorldContentSimulationRules.Footprint(size, building.Position);
            })));
            Assert.All(world.RoadTiles, point => Assert.True(map.IsBuildable(point)));
            Assert.Contains(world.ExportState().Events, item => item.Kind == "town_road_generated");
            var roadOverlap = world.PlaceBuilding("road-overlap-test", definition.CanonicalId, world.RoadTiles[0]);
            Assert.False(roadOverlap.Applied);
            Assert.Contains("Road", roadOverlap.Failure);

            var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
            var projectedTown = Assert.Single(snapshot.Towns);
            Assert.Equal(grownTown.BorderTiles.Select(point => (point.X, point.Y)),
                projectedTown.BorderTiles.Select(point => (point.X, point.Y)));
            Assert.Contains(placed.InstanceId, projectedTown.AssignedBuildingIds);
            Assert.Equal(world.RoadTiles.Select(point => (point.X, point.Y)),
                snapshot.RoadTiles.Select(point => (point.X, point.Y)));
            Assert.Equal(TownBorderRules.FirstTownId,
                Assert.Single(snapshot.PlacedBuildings, item => item.InstanceId == placed.InstanceId).TownId);
            var godotSnapshot = JsonSerializer.Deserialize<GodotOwnerWorldSnapshot>(
                JsonSerializer.Serialize(snapshot, GodotJsonOptions), GodotJsonOptions);
            var godotTown = Assert.Single(godotSnapshot!.Towns);
            Assert.Contains(placed.InstanceId, godotTown.AssignedBuildingIds);
            Assert.Equal(world.RoadTiles.Select(point => (point.X, point.Y)),
                godotSnapshot.RoadTiles.Select(point => (point.X, point.Y)));
            Assert.Equal(TownBorderRules.FirstTownId,
                Assert.Single(godotSnapshot.PlacedBuildings, item => item.InstanceId == placed.InstanceId).TownId);

            file.Save(world);
            using var reloaded = file.LoadOrCreate(geography.Seed);
            var restoredTown = Assert.Single(reloaded.Towns);
            Assert.Equal(grownTown.BorderTiles, restoredTown.BorderTiles);
            Assert.Equal(grownTown.AssignedBuildingIds, restoredTown.AssignedBuildingIds);
            Assert.Equal(world.RoadTiles, reloaded.RoadTiles);
            Assert.Equal(TownBorderRules.FirstTownId,
                Assert.Single(reloaded.WorldSimulation.Buildings, item => item.InstanceId == placed.InstanceId).TownId);
            var invalidRoads = reloaded.ExportState() with
            {
                RoadTiles = reloaded.RoadTiles.Append(reloaded.RoadTiles[0]).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalidRoads));
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(reloaded.ExportState() with
            {
                RoadTiles = null,
            }));
            using var beforeRoadSchema = PrivateWorldRuntime.Restore(reloaded.ExportState() with
            {
                SchemaVersion = 23,
                RoadTiles = null,
            });
            Assert.Empty(beforeRoadSchema.RoadTiles);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void PlaceFourFounders(PrivateWorldRuntime world)
    {
        if (world.Towns.Count == 0)
        {
            world.InitializeFirstTownContent();
            world.AcceptFirstTownLayout(world.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position);
        }
        var positions = FounderPositions(world);
        for (var index = 0; index < positions.Length; index++)
            world.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), positions[index]);
    }

    private static GridPoint[] FounderPositions(PrivateWorldRuntime world)
    {
        var map = world.ExportState().Map;
        var storage = Assert.Single(world.Towns).OriginSite!.Value;
        var buildingTiles = world.WorldSimulation.Buildings.SelectMany(building =>
        {
            var definition = world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            return WorldContentSimulationRules.Footprint(definition, building.Position);
        }).ToHashSet();
        var positions = map.Tiles.Where(tile =>
                Math.Abs(tile.Position.X - storage.X) <= 5 &&
                Math.Abs(tile.Position.Y - storage.Y) <= 5 &&
                map.IsBuildable(tile.Position) &&
                !buildingTiles.Contains(tile.Position) &&
                !map.Resources.Any(item => item.Position == tile.Position))
            .Take(PrivateWorldRuntime.RequiredFounders)
            .Select(tile => tile.Position)
            .ToArray();
        Assert.Equal(PrivateWorldRuntime.RequiredFounders, positions.Length);
        return positions;
    }
}
