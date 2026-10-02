using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class GeographyGeneratorTests
{
    [Fact]
    public void VisibleNaturalObjectsCannotOverlapTreesOrEachOther()
    {
        var options = new GeographyOptions("natural-roster-visible-occupancy", WorldSizePreset.Small,
            ResourceAbundance: ResourceAbundance.Abundant);
        var map = GeneratedCampMapGenerator.Generate(options);
        var visibleObjects = map.Resources.Where(resource =>
            resource.TreeKind is not null || resource.NaturalObjectKind is not null).ToArray();
        Assert.Equal(visibleObjects.Length, visibleObjects.Select(resource => resource.Position).Distinct().Count());

        var tree = visibleObjects.First(resource => resource.TreeKind is not null);
        var overlappingForage = new MapResource("invalid-overlap-forage", "food", tree.Position, true,
            NaturalObjectKind: "berry_bush");
        var invalidMap = map with
        {
            Resources = map.Resources.Append(overlappingForage).ToArray(),
            ManifestDigest = string.Empty,
        };
        invalidMap = invalidMap with { ManifestDigest = MapManifestCodec.Digest(invalidMap) };

        Assert.False(MapAcceptance.Validate(invalidMap, allowEmptyCamp: true).IsValid);
    }

    [Fact]
    public void TreeStumpAndPlantedSaplingPersistAcrossGeneratedWorldReload()
    {
        var options = new GeographyOptions("saved-tree-stages", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(options.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        var tree = world.ExportState().Map.Resources.First(site => site.TreeKind is not null);
        var standing = world.WorldSystems.Ecology.GetResource(tree.Id);
        Assert.Equal(1, standing.Quantity);
        var cut = EcologyRules.Harvest(standing, 1);
        Assert.True(cut.IsValid);
        Assert.Equal(EcologyResourceState.Depleted, cut.Resource!.State);
        Assert.Equal(0, cut.Resource.Quantity);

        var state = world.ExportState();
        var planted = cut.Resource with
        {
            State = EcologyResourceState.Regenerating,
            IsPlanted = true,
            NextRegenerationDay = 3,
        };
        state = state with
        {
            Resources = state.Resources.Select(resource => resource.ResourceId == tree.Id
                ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource =>
                        resource.Id == tree.Id ? planted : resource).ToArray(),
                },
            },
        };
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));
        using var restored = PrivateWorldRuntime.Restore(saved);
        var projection = new OwnerWorldObservationStore(restored).GetSnapshot();
        var visible = Assert.Single(projection.Resources, resource => resource.Id == tree.Id);
        Assert.Equal(tree.TreeKind, visible.TreeKind);
        Assert.True(visible.IsPlanted);
        Assert.Equal(0, visible.Quantity);

        var beforeGrowth = EcologyRules.Regenerate(planted,
            WorldCalendarRules.FromTick(2 * restored.WorldSystems.Config.TicksPerDay, restored.WorldSystems.Config),
            restored.WorldSystems.Config);
        Assert.True(beforeGrowth.IsPlanted);
        var grown = EcologyRules.Regenerate(beforeGrowth,
            WorldCalendarRules.FromTick(3 * restored.WorldSystems.Config.TicksPerDay, restored.WorldSystems.Config),
            restored.WorldSystems.Config);
        Assert.False(grown.IsPlanted);
        Assert.Equal(1, grown.Quantity);
    }

    [Fact]
    public async Task AgentReplantsHarvestedTreeFromCarriedSeed()
    {
        var options = new GeographyOptions("agent-replanting", WorldSizePreset.Small);
        var initial = StartedGeneratedWorld(options);
        var map = initial.Map;
        var actor = initial.Inhabitants[0].InhabitantId;
        var occupied = initial.Inhabitants.Skip(1).Select(person => person.Position).ToHashSet();
        var treeSite = map.Resources.Where(site => site.TreeKind is "broadleaf" or "conifer" && site.IsRenewable &&
                map.IsReachableFromCampOnFoot(site.Position))
            .Select(site => new
            {
                Site = site,
                Stand = map.FootNeighbors(site.Position).FirstOrDefault(position =>
                    map.IsPassable(position) && !occupied.Contains(position) &&
                    !map.Resources.Any(resource => resource.Position == position)),
            })
            .First(item => map.Contains(item.Stand) && map.IsPassable(item.Stand));
        var stump = initial.WorldSystems!.Ecology.GetResource(treeSite.Site.Id) with
        {
            Quantity = 0,
            State = EcologyResourceState.Regenerating,
            NextRegenerationDay = 6,
        };
        var seededInventory = InventoryFixture.AddLot(initial.Society.Society.Inventory,
            "replant-test-seed", TreeGrowthRules.TreeSeedItem, actor, 1);
        var state = initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = treeSite.Stand, HungerBasisPoints = 9_000 }
                : person).ToArray(),
            Resources = initial.Resources.Select(resource => resource.ResourceId == treeSite.Site.Id
                ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            WorldSystems = initial.WorldSystems with
            {
                Ecology = initial.WorldSystems.Ecology with
                {
                    Resources = initial.WorldSystems.Ecology.Resources.Select(resource =>
                        resource.Id == stump.Id ? stump : resource).ToArray(),
                },
            },
            Society = initial.Society with
            {
                Society = initial.Society.Society with { Inventory = seededInventory },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? new ReplantProvider() : new DeterministicDecisionProvider());
        for (var tick = 0; tick < 12 && !world.ExportState().Events.Any(item => item.Kind == "tree_replanted"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(world.ExportState().Events, item => item.Kind == "tree_replanted" &&
            item.Detail.Contains(treeSite.Site.Id, StringComparison.Ordinal));
        Assert.True(world.WorldSystems.Ecology.GetResource(treeSite.Site.Id).IsPlanted);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "replant-test-seed" && lot.Quantity > 0);
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(saved);
        Assert.True(restored.WorldSystems.Ecology.GetResource(treeSite.Site.Id).IsPlanted);
        var visible = Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Resources,
            resource => resource.Id == treeSite.Site.Id);
        Assert.True(visible.IsPlanted);
        Assert.Equal("sapling", visible.TreeStage);
    }

    [Fact]
    public async Task AgentPicksOrchardFruitAndEatsTheDistinctItem()
    {
        var options = new GeographyOptions("agent-orchard-fruit", WorldSizePreset.Small);
        var initial = StartedGeneratedWorld(options);
        var map = initial.Map;
        var actor = initial.Inhabitants[0].InhabitantId;
        var occupied = initial.Inhabitants.Skip(1).Select(person => person.Position).ToHashSet();
        var orchard = map.Resources.Where(resource => resource.TreeKind == "orchard" &&
                map.IsReachableFromCampOnFoot(resource.Position))
            .Select(resource => new
            {
                Site = resource,
                Stand = map.FootNeighbors(resource.Position).FirstOrDefault(position =>
                    map.IsPassable(position) && !occupied.Contains(position) &&
                    !map.Resources.Any(other => other.Position == position)),
            })
            .First(item => map.Contains(item.Stand) && map.IsPassable(item.Stand));
        var otherFoodIds = map.Resources.Where(resource => resource.Kind == "food")
            .Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        var state = initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = orchard.Stand, HungerBasisPoints = 4_000 }
                : person).ToArray(),
            Resources = initial.Resources.Select(resource => otherFoodIds.Contains(resource.ResourceId)
                ? resource with { State = ResourceState.Depleted }
                : resource.ResourceId == orchard.Site.Id ? resource with { State = ResourceState.Available } : resource).ToArray(),
            WorldSystems = initial.WorldSystems! with
            {
                Ecology = initial.WorldSystems.Ecology with
                {
                    Resources = initial.WorldSystems.Ecology.Resources.Select(resource =>
                        otherFoodIds.Contains(resource.Id)
                            ? resource with { Quantity = 0, State = EcologyResourceState.Depleted }
                            : resource.Id == orchard.Site.Id ? TreeGrowthAndPlantingTests.InFruitingSeason(resource, initial) : resource).ToArray(),
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? new OrchardProvider() : new DeterministicDecisionProvider());
        for (var tick = 0; tick < 12 && !world.ExportState().Events.Any(item => item.Kind == "fruit_harvested"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(world.ExportState().Events, item => item.Kind == "fruit_harvested" &&
            item.Detail.Contains(orchard.Site.Id, StringComparison.Ordinal));
        Assert.Equal(0, world.WorldSystems.Ecology.GetResource(orchard.Site.Id).Quantity);
        Assert.Equal("picked", Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Resources,
            resource => resource.Id == orchard.Site.Id).TreeStage);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "fruit" && lot.Quantity == 4);
        for (var tick = 0; tick < 12 && !world.ExportState().Events.Any(item => item.Kind == "food_consumed" &&
                 item.Detail == actor); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == actor);
        Assert.True(world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints > 4_000);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Contains(restored.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "fruit" && lot.Quantity > 0);
        Assert.Equal("picked", Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Resources,
            resource => resource.Id == orchard.Site.Id).TreeStage);
        Assert.Equal(0, restored.WorldSystems.Ecology.GetResource(orchard.Site.Id).Quantity);
    }

    private sealed class OrchardProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var chosen = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id is "harvest_food" or "consume_food");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = chosen is null ? request.Observation.Candidates : [chosen],
                },
            }, cancellationToken);
        }
    }

    private sealed class ReplantProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var desired = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "replant_tree");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = desired is null ? request.Observation.Candidates : [desired],
                },
            }, cancellationToken);
        }
    }

    private sealed class SeekCoverProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var desired = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "seek_warmth");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = desired is null ? request.Observation.Candidates : [desired],
                },
            }, cancellationToken);
        }
    }

    internal static PrivateWorldRuntimeState StartedGeneratedWorld(GeographyOptions options)
    {
        using var setup = new PrivateWorldRuntime(options.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        var map = setup.ExportState().Map;
        var campAnchor = NormalPathWorld.FindStartingTownSite(map);
        setup.InitializeFirstTownContent();
        setup.AcceptFirstTownLayout(campAnchor);
        var buildingTiles = setup.WorldSimulation.Buildings.SelectMany(building =>
        {
            var definition = setup.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            return WorldContentSimulationRules.Footprint(definition, building.Position);
        }).ToHashSet();
        var startingTiles = map.Tiles.Where(tile =>
                Math.Abs(tile.Position.X - campAnchor.X) <= 5 &&
                Math.Abs(tile.Position.Y - campAnchor.Y) <= 5 &&
                map.IsBuildable(tile.Position) &&
                !buildingTiles.Contains(tile.Position) &&
                !setup.RoadTiles.Contains(tile.Position) &&
                !map.CampObjects.Any(item => item.Position == tile.Position) &&
                !map.Resources.Any(item => item.Position == tile.Position))
            .Take(4).Select(tile => tile.Position).ToArray();
        Assert.Equal(4, startingTiles.Length);
        // Founder IDs decide the order agents act in, so random IDs would give
        // each run a different world. Derive them from the seed instead.
        for (var index = 0; index < 4; index++)
            setup.PlaceFounder(TestFounderId(options.Seed, index), startingTiles[index]);
        setup.StartWorld();
        return setup.ExportState();
    }

    private static string TestFounderId(string seed, int index) => "founder:" + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{seed}:{index}")))[..32];

    [Fact]
    public async Task GeneratedWorldCanStartAdvanceAndRestoreWithItsOwnGeography()
    {
        var options = new GeographyOptions("generated-life", WorldSizePreset.Small, WrapEastWest: true);
        using var world = new PrivateWorldRuntime(options.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        var initial = world.ExportState();
        Assert.Equal(options, initial.Geography);
        Assert.True(initial.Map.WrapsEastWest);
        Assert.Empty(world.Inhabitants);
        Assert.True(world.Society.IsPaused);
        var projection = new OwnerWorldObservationStore(world).GetSnapshot();
        Assert.Empty(projection.Tiles);
        Assert.Equal((256, 128), (projection.PackedTerrain?.Width, projection.PackedTerrain?.Height));
        Assert.Equal("terrain-kind-v1", projection.PackedTerrain!.Encoding);
        Assert.True(projection.WrapsEastWest);
        Assert.Equal(WeatherRules.RegionSize, projection.WeatherRegionSize);
        Assert.Equal((256 / WeatherRules.RegionSize) * (128 / WeatherRules.RegionSize),
            projection.WeatherRegions.Count);
        Assert.True(projection.WeatherRegions.Select(region => region.Weather).Distinct().Count() > 1,
            "A generated world must not have one planet-wide weather condition.");
        Assert.All(projection.WeatherRegions,
            region => Assert.InRange(region.SoilMoisture ?? -1, 0, 100));
        var terrainBytes = Convert.FromBase64String(projection.PackedTerrain.Data);
        Assert.Equal(initial.Map.Tiles.Count, terrainBytes.Length);
        Assert.Equal((byte)initial.Map.Tiles[0].Terrain, terrainBytes[0]);
        var townStorage = initial.Map.Resources.Single(item => item.Id == "berry-patch").Position;
        Assert.Empty(initial.Map.CampObjects);
        Assert.Empty(world.Towns);
        world.InitializeFirstTownContent();
        world.AcceptFirstTownLayout(townStorage);
        var buildingTiles = world.WorldSimulation.Buildings.SelectMany(building =>
        {
            var definition = world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            return WorldContentSimulationRules.Footprint(definition, building.Position);
        }).ToHashSet();
        var positions = initial.Map.Tiles.Where(tile =>
                tile.Position.X >= townStorage.X - 1 && tile.Position.X < townStorage.X + 5 &&
                tile.Position.Y >= townStorage.Y - 1 && tile.Position.Y < townStorage.Y + 4 &&
                initial.Map.IsBuildable(tile.Position) &&
                !buildingTiles.Contains(tile.Position) &&
                !initial.Map.Resources.Any(item => item.Position == tile.Position))
            .Take(4).Select(tile => tile.Position).ToArray();
        Assert.Equal(4, positions.Length);
        for (var index = 0; index < positions.Length; index++)
            world.PlaceFounder("founder:" + Guid.NewGuid().ToString("N"), positions[index]);
        world.StartWorld();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(saved);
        Assert.Equal(1, restored.WorldTick);
        Assert.Equal(saved.Map.ManifestDigest, restored.ExportState().Map.ManifestDigest);
        Assert.Equal(options, restored.ExportState().Geography);
        Assert.True(restored.ExportState().Map.WrapsEastWest);
        Assert.True(new OwnerWorldObservationStore(restored).GetSnapshot().WrapsEastWest);
        Assert.Equal(4, restored.Inhabitants.Count);
        Assert.True(restored.WorldSystems.Chunks.Count > 1);
        Assert.All(restored.ExportState().Map.Resources, site =>
        {
            var chunk = Assert.Single(restored.WorldSystems.Chunks, item =>
                item.Coordinate == ChunkRules.ToChunkCoordinate(site.Position, item.ChunkSize));
            Assert.Contains(chunk.Resources, item => item.ResourceId == site.Id &&
                item.LocalPosition == ChunkRules.ToLocalPoint(site.Position, chunk.ChunkSize));
        });
    }

    [Theory]
    [InlineData("river-world-a", true, 1)]
    [InlineData("river-world-b", false, 1)]
    public void GeneratedRiversFollowAnAcyclicRouteToWater(string seed, bool wrap, int version)
    {
        var map = GeographyGenerator.Generate(new GeographyOptions(seed, WorldSizePreset.Small, wrap, HydrologyVersion: version));

        Assert.Equal(0, map.Width % GeographyGenerator.ChunkSize);
        Assert.Equal(0, map.Height % GeographyGenerator.ChunkSize);
        Assert.True(map.Count(WaterKind.Ocean) > 0);
        Assert.True(map.Count(WaterKind.River) > 0);

        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                if (map.At(x, y).Water != WaterKind.River) continue;
                var visited = new HashSet<(int X, int Y)>();
                var cursor = (X: x, Y: y);
                while (map.At(cursor.X, cursor.Y).Water is WaterKind.Land or WaterKind.River)
                {
                    Assert.True(visited.Add(cursor), $"Drainage cycle from {x},{y}");
                    var next = map.DownstreamAt(cursor.X, cursor.Y);
                    Assert.NotNull(next);
                    Assert.Equal(1, Math.Abs(next.Value.Y - cursor.Y) +
                        Math.Min(Math.Abs(next.Value.X - cursor.X),
                            wrap ? map.Width - Math.Abs(next.Value.X - cursor.X) : map.Width));
                    cursor = next.Value;
                }

                Assert.True(map.At(cursor.X, cursor.Y).Water is WaterKind.Ocean or WaterKind.Lake);
            }
    }

    [Fact]
    public async Task RegionalStormCutoffSurvivesSaveAndMatchesOwnerObservation()
    {
        var options = new GeographyOptions("storm-observation", WorldSizePreset.Small);
        var initial = StartedGeneratedWorld(options);
        var systems = initial.WorldSystems!;
        var profiles = Enum.GetValues<SeasonKind>()
            .Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray();
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with { WeatherProfiles = profiles },
                Climate = systems.Climate with { Weather = WeatherKind.Storm },
            },
        });
        while (world.WorldTick < 269)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.All(new OwnerWorldObservationStore(world).GetSnapshot().WeatherRegions,
            region => Assert.Equal("storm", region.Weather));

        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(saved);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(270, restored.WorldTick);
        Assert.All(new OwnerWorldObservationStore(restored).GetSnapshot().WeatherRegions,
            region => Assert.NotEqual("storm", region.Weather));
        Assert.Contains(restored.ExportState().Events,
            item => item.WorldTick == 270 && item.Kind == "weather_changed");
    }

    [Fact]
    public async Task AgentAwayFromTownCanReachNaturalCoverAndReduceStormExposure()
    {
        var options = new GeographyOptions("storm-cover", WorldSizePreset.Small);
        var initial = StartedGeneratedWorld(options);
        var map = initial.Map;
        var camp = map.Resources.Single(item => item.Id == "berry-patch").Position;
        var pair = map.Tiles.Where(tile => map.VegetationAt(tile.Position) == VegetationCover.Forest &&
                map.IsPassable(tile.Position) && map.FootDistance(camp, tile.Position) > 12)
            .SelectMany(tile => map.FootNeighbors(tile.Position).Where(neighbor =>
                    map.IsPassable(neighbor) && map.VegetationAt(neighbor) != VegetationCover.Forest &&
                    !map.Resources.Any(resource => resource.Position == neighbor))
                .Select(neighbor => (Cover: tile.Position, Open: neighbor)))
            .First();
        var actor = initial.Inhabitants[0];
        var systems = initial.WorldSystems!;
        var profiles = Enum.GetValues<SeasonKind>()
            .Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray();
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == actor.InhabitantId
                ? person with
                {
                    Position = pair.Open,
                    HungerBasisPoints = 9_000,
                    Survival = new SurvivalCondition(WarmthBasisPoints: 4_000)
                }
                : person).ToArray(),
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with { WeatherProfiles = profiles },
                Climate = systems.Climate with { Weather = WeatherKind.Storm },
            },
        }, _ => new SeekCoverProvider());
        for (var tick = 0; tick < 6 && !world.ExportState().Events.Any(item =>
                 item.Kind == "inhabitant_moved" && item.Detail.StartsWith(actor.InhabitantId + ":", StringComparison.Ordinal) &&
                 item.Detail.EndsWith(":storm_cover", StringComparison.Ordinal)); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var moved = world.Inhabitants.Single(person => person.InhabitantId == actor.InhabitantId);
        Assert.Equal(VegetationCover.Forest, map.VegetationAt(moved.Position));
        var previousWarmth = moved.Survival!.WarmthBasisPoints;
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var sheltered = world.Inhabitants.Single(person => person.InhabitantId == actor.InhabitantId);
        Assert.Equal(moved.Position, sheltered.Position);
        Assert.True(sheltered.Survival!.WarmthBasisPoints >= previousWarmth - 10,
            "Natural cover should reduce storm exposure at the agent's actual tile.");
    }

}
