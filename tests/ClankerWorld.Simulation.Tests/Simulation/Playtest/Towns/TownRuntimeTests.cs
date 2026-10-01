using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Simulation.Society;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Viewer.Observation;
using System.Text.Json;
using GodotOwnerWorldSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;
using GodotOwnerWorldEvent = ClankerWorld.GodotClient.UI.OwnerWorldEvent;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownRuntimeTests
{
    private static readonly JsonSerializerOptions GodotJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task TownDeathRetainsTheArchivedFounderNameAndCompleteTelemetryTownId()
    {
        const string founderId = "founder:00000000000000000000000000000001";
        using var setup = new PrivateWorldRuntime("town-event-names", startPace: WorldStartPace.FounderSetup);
        setup.PlaceFounder(founderId, new(0, 0));
        setup.PlaceFounder("founder:00000000000000000000000000000002", new(1, 2));
        setup.PlaceFounder("founder:00000000000000000000000000000003", new(2, 2));
        setup.PlaceFounder("founder:00000000000000000000000000000004", new(3, 2));
        setup.StartWorld();
        Assert.True(setup.RenameAgent(founderId, "Aster"));
        var state = setup.ExportState();
        var society = state.Society.Society;
        var maximumDay = society.Config.DayLifecycle!.MaximumDay;
        var birthTick = 1 - maximumDay * society.Config.TicksPerWorldDay;
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == founderId ? person with
                    {
                        BirthTick = birthTick,
                        BirthLifeTick = society.LifeClock is null ? null : birthTick,
                        AgeBand = SocietyAgeBand.Elder,
                        LastLifecycleYearChecked = maximumDay - 1,
                    } : person).ToArray(),
                },
            },
        });
        var directory = Directory.CreateTempSubdirectory("clankerworld-town-event-names-");
        try
        {
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(1));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains(
                "town=town:first transition=ResidentLeft residents=3", StringComparison.Ordinal));
            var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
            var store = new OwnerWorldObservationStore(restored);
            var snapshot = JsonSerializer.Deserialize<GodotOwnerWorldSnapshot>(
                JsonSerializer.Serialize(store.GetSnapshot(), GodotJsonOptions), GodotJsonOptions);
            Assert.Equal("dead", snapshot!.Inhabitants.Single(person => person.Id == founderId).Lifecycle);
            foreach (var (kind, expected) in new[]
                     { ("inhabitant_removed", "Aster died."), ("town_resident_left", "Aster left the first Town."),
                       ("town_resident_joined", "Aster joined the first Town.") })
            {
                var accepted = store.GetEventsAfter(0).Events.First(item => item.Kind == kind && item.Detail.Contains(founderId, StringComparison.Ordinal));
                var clientEvent = JsonSerializer.Deserialize<GodotOwnerWorldEvent>(JsonSerializer.Serialize(accepted, GodotJsonOptions), GodotJsonOptions);
                Assert.Equal(expected, WorldEventText.Describe(clientEvent!, snapshot));
                Assert.Equal(accepted.Detail, clientEvent!.Detail);
            }
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddedIslandAdultCanHarvestAdjacentFoodOutsideTheCampComponent()
    {
        var geography = new GeographyOptions("island-food-review-0", WorldSizePreset.Small);
        using var setup = new PrivateWorldRuntime(geography.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        PlaceFourFounders(setup);
        setup.StartWorld();
        var position = new GridPoint(22, 13);
        setup.AddAgent("agent:00000000000000000000000000000099", position);
        Assert.True(setup.RenameAgent("agent:00000000000000000000000000000099", "Aster"));
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
        var acceptedHistory = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        var store = new OwnerWorldObservationStore(restored);
        var godotSnapshot = JsonSerializer.Deserialize<GodotOwnerWorldSnapshot>(
            JsonSerializer.Serialize(store.GetSnapshot(), GodotJsonOptions), GodotJsonOptions);
        var harvest = store.GetEventsAfter(0).Events.First(item => item.Kind == "food_harvested" &&
            item.Detail.StartsWith("agent:00000000000000000000000000000099:", StringComparison.Ordinal));
        var godotEvent = JsonSerializer.Deserialize<GodotOwnerWorldEvent>(JsonSerializer.Serialize(harvest, GodotJsonOptions), GodotJsonOptions);
        Assert.Equal("Aster gathered food.", WorldEventText.Describe(godotEvent!, godotSnapshot));
        Assert.Equal(harvest.Detail, godotEvent!.Detail);
        Assert.Equal(acceptedHistory, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
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
            // Town Roads stay inside the border, which grows around the new Road too.
            Assert.All(world.RoadTiles, road => Assert.Contains(road, grownTown.BorderTiles));
            Assert.Contains(world.ExportState().Events, item => item.Kind == "town_building_assigned");
            Assert.Contains(world.ExportState().Events, item => item.Kind == "town_border_expanded");
            Assert.NotEmpty(world.RoadTiles);
            Assert.DoesNotContain(position, world.RoadTiles);
            Assert.Contains(world.RoadTiles, road => map.FootNeighbors(position).Contains(road) &&
                !map.IsDiagonalFootStep(position, road));
            // The door faces the Road the building was joined to.
            var entrance = Assert.Single(world.WorldSimulation.Buildings, item => item.InstanceId == result.InstanceId)
                .Entrance ?? throw new InvalidOperationException("The joined building has no entrance.");
            Assert.Contains(entrance, world.RoadTiles);
            Assert.True(WorldContentSimulationRules.IsEntrance(definition, position, entrance));
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
            Assert.Equal((entrance.X, entrance.Y), Assert.Single(snapshot.PlacedBuildings,
                item => item.InstanceId == placed.InstanceId).Entrance is { } shown ? (shown.X, shown.Y) : default);
            var godotSnapshot = JsonSerializer.Deserialize<GodotOwnerWorldSnapshot>(
                JsonSerializer.Serialize(snapshot, GodotJsonOptions), GodotJsonOptions);
            var godotTown = Assert.Single(godotSnapshot!.Towns);
            Assert.Contains(placed.InstanceId, godotTown.AssignedBuildingIds);
            Assert.Equal(world.RoadTiles.Select(point => (point.X, point.Y)),
                godotSnapshot.RoadTiles.Select(point => (point.X, point.Y)));
            Assert.Equal(TownBorderRules.FirstTownId,
                Assert.Single(godotSnapshot.PlacedBuildings, item => item.InstanceId == placed.InstanceId).TownId);
            Assert.Equal((entrance.X, entrance.Y), Assert.Single(godotSnapshot.PlacedBuildings,
                item => item.InstanceId == placed.InstanceId).Entrance is { } drawn ? (drawn.X, drawn.Y) : default);

            file.Save(world);
            using var reloaded = file.LoadOrCreate(geography.Seed);
            var restoredTown = Assert.Single(reloaded.Towns);
            Assert.Equal(grownTown.BorderTiles, restoredTown.BorderTiles);
            Assert.Equal(grownTown.AssignedBuildingIds, restoredTown.AssignedBuildingIds);
            Assert.Equal(world.RoadTiles, reloaded.RoadTiles);
            Assert.Equal(TownBorderRules.FirstTownId,
                Assert.Single(reloaded.WorldSimulation.Buildings, item => item.InstanceId == placed.InstanceId).TownId);
            Assert.Equal(entrance,
                Assert.Single(reloaded.WorldSimulation.Buildings, item => item.InstanceId == placed.InstanceId).Entrance);
            // A saved entrance must sit beside its building.
            var reloadedState = reloaded.ExportState();
            // A saved border must cover every assigned building.
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(reloadedState with
            {
                Towns = [restoredTown with { BorderTiles = restoredTown.BorderTiles.Where(tile => tile != position).ToArray() }],
            }));
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(reloadedState with
            {
                WorldSimulation = reloadedState.WorldSimulation! with
                {
                    Buildings = reloadedState.WorldSimulation.Buildings
                        .Select(item => item.InstanceId == placed.InstanceId
                            ? item with { Entrance = new GridPoint(position.X + definition.Width, position.Y + definition.Height) }
                            : item)
                        .ToArray(),
                },
            }));
            var invalidRoads = reloaded.ExportState() with
            {
                RoadTiles = reloaded.RoadTiles.Append(reloaded.RoadTiles[0]).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalidRoads));
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(reloaded.ExportState() with
            {
                RoadTiles = null,
            }));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BuildingsJoiningATownExtendItsStreetsAndBranchNewOnes()
    {
        var geography = new GeographyOptions("first-town-streets-grow", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        PlaceFourFounders(world);
        var workshop = world.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
        var map = world.ExportState().Map;
        HashSet<GridPoint> Footprints() => world.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(
                world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId),
                building.Position)).ToHashSet();

        // The starting wood pays for four workshops.
        for (var index = 0; index < 4; index++)
        {
            var town = Assert.Single(world.Towns);
            var roads = world.RoadTiles.ToHashSet();
            var taken = map.CampObjects.Select(item => item.Position).Concat(map.Resources.Select(item => item.Position))
                .Concat(roads).Concat(Footprints()).ToHashSet();
            var sites = map.Tiles.Select(tile => tile.Position).Where(point =>
                    WorldContentSimulationRules.Footprint(workshop, point).All(tile => map.IsBuildable(tile) && !taken.Contains(tile)) &&
                    TownBorderRules.IsWithinOrAdjacent(town, point, workshop.Width, workshop.Height))
                .Where(point => RoadRoutePlanner.Plan(new RoadRouteRequest(map,
                    WorldContentSimulationRules.Footprint(workshop, point).SelectMany(map.FootNeighbors)
                        .Where(entrance => WorldContentSimulationRules.IsEntrance(workshop, point, entrance)).ToArray(),
                    roads, taken.Concat(WorldContentSimulationRules.Footprint(workshop, point)).ToHashSet(),
                    world.Bridges, roads)).Proposal is not null)
                .ToArray();
            int NearestRoad(GridPoint point) => WorldContentSimulationRules.Footprint(workshop, point)
                .Min(tile => roads.Min(road => Math.Abs(road.X - tile.X) + Math.Abs(road.Y - tile.Y)));
            // Alternate a lot facing a street with one that needs a new side street.
            var facing = index % 2 == 0;
            var position = sites.First(point => facing ? NearestRoad(point) == 1 : NearestRoad(point) >= 3);

            var result = world.PlaceBuilding($"growth-{index}", workshop.CanonicalId, position);
            Assert.True(result.Applied, result.Failure);
            var entrance = Assert.Single(world.WorldSimulation.Buildings, item => item.InstanceId == result.InstanceId)
                .Entrance ?? throw new InvalidOperationException("The joined building has no entrance.");
            Assert.True(WorldContentSimulationRules.IsEntrance(workshop, position, entrance));
            Assert.Contains(entrance, world.RoadTiles);
            if (facing) Assert.Contains(entrance, roads);
            else
            {
                Assert.DoesNotContain(entrance, roads);
                Assert.True(world.RoadTiles.Count > roads.Count,
                    "A building without a pre-existing street entrance should add a connected entrance tile.");
            }
        }

        var network = world.RoadTiles.ToHashSet();
        var grownTown = Assert.Single(world.Towns);
        Assert.Empty(network.Intersect(Footprints()));
        Assert.All(network, road => Assert.Contains(road, grownTown.BorderTiles));

        // The Town stays one connected street network. A bridge with Road at
        // both ends joins the streets on its two banks.
        var bridgeEnds = world.Bridges.Where(bridge => bridge.Entrances.All(network.Contains))
            .SelectMany(bridge => new[] { (bridge.Entrances[0], bridge.Entrances[1]), (bridge.Entrances[1], bridge.Entrances[0]) })
            .ToLookup(pair => pair.Item1, pair => pair.Item2);
        IEnumerable<GridPoint> Linked(GridPoint tile) => TownStreets.Linked(network, tile).Concat(bridgeEnds[tile]);
        var reachable = new HashSet<GridPoint> { world.RoadTiles[0] };
        var pending = new Queue<GridPoint>(reachable);
        while (pending.TryDequeue(out var current))
            foreach (var next in Linked(current))
                if (reachable.Add(next)) pending.Enqueue(next);
        Assert.True(network.SetEquals(reachable));

        // Streets run on three tiles past the nearest door wherever the land ahead is clear.
        var doors = world.WorldSimulation.Buildings.Where(item => item.Entrance is not null)
            .Select(item => item.Entrance!.Value).ToHashSet();
        var blocked = map.CampObjects.Select(item => item.Position).Concat(map.Resources.Select(item => item.Position))
            .Concat(Footprints()).ToHashSet();
        foreach (var end in network.Where(road => Linked(road).Count() == 1))
        {
            if (StepsToDoor(Linked, doors, end) >= TownStreets.RunOnTiles) continue;
            var from = Linked(end).Single();
            var ahead = new GridPoint(end.X + (end.X - from.X), end.Y + (end.Y - from.Y));
            Assert.True(!map.IsBuildable(ahead) || blocked.Contains(ahead) || network.Contains(ahead) ||
                TownStreets.Directions.Any(step => new GridPoint(ahead.X + step.X, ahead.Y + step.Y) is var near &&
                    near != end && near != from && network.Contains(near)),
                $"The street ending at {end} could run on past its door.");
        }
    }

    private static int StepsToDoor(Func<GridPoint, IEnumerable<GridPoint>> linked, HashSet<GridPoint> doors, GridPoint start)
    {
        var steps = new Dictionary<GridPoint, int> { [start] = 0 };
        var pending = new Queue<GridPoint>([start]);
        while (pending.TryDequeue(out var current))
        {
            if (doors.Contains(current)) return steps[current];
            foreach (var next in linked(current))
                if (steps.TryAdd(next, steps[current] + 1)) pending.Enqueue(next);
        }
        return int.MaxValue;
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
