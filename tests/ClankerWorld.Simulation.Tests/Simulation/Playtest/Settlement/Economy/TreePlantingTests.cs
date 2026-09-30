using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class TreePlantingTests
{
    private const string SeedLotId = "test-tree-seed";

    [Fact]
    public async Task AgentWithATreeSeedPlantsOneSaplingOutsideTheTownThatSurvivesReload()
    {
        var initial = GeographyGeneratorTests.StartedGeneratedWorld(
            new GeographyOptions("tree-planting-agent", WorldSizePreset.Small));
        var actor = initial.Inhabitants[0].InhabitantId;
        var offered = new List<IReadOnlyList<string>>();

        // Households start with grain seed only; that is not a tree seed.
        Assert.DoesNotContain(initial.Society.Society.Inventory.Lots, lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem);
        using (var withoutSeed = PrivateWorldRuntime.Restore(Fed(initial, actor),
                   id => id == actor ? new ChooseProvider("plant_tree", offered) : new IdleProvider()))
            for (var tick = 0; tick < 3; tick++)
                Assert.True((await withoutSeed.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(offered);
        Assert.All(offered, candidates => Assert.DoesNotContain("plant_tree", candidates));
        offered.Clear();

        var state = WithTreeSeeds(Fed(initial, actor), actor, 2);
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? new ChooseProvider("plant_tree", offered) : new IdleProvider());
        for (var tick = 0; tick < 40 && !world.ExportState().Events.Any(item => item.Kind == "tree_planted"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(offered, candidates => candidates.Contains("plant_tree"));
        var planted = world.ExportState();
        var plantedEvent = Assert.Single(planted.Events, item => item.Kind == "tree_planted");
        Assert.StartsWith(actor + ":" + TreeGrowthRules.PlantedTreeIdPrefix, plantedEvent.Detail, StringComparison.Ordinal);
        var tree = Assert.Single(planted.Map.Resources, resource =>
            resource.Id.StartsWith(TreeGrowthRules.PlantedTreeIdPrefix, StringComparison.Ordinal));
        Assert.True(TreeGrowthRules.IsWoodTree(tree.TreeKind));
        Assert.Null(TreeGrowthRules.GroundRefusal(planted.Map, tree.Position));
        Assert.DoesNotContain(planted.Towns!.SelectMany(town => town.BorderTiles), point => point == tree.Position);
        Assert.Single(planted.Map.Resources, resource => resource.Position == tree.Position);
        Assert.Equal(1, SeedCount(planted, actor));

        var day = WorldCalendarRules.FromTick(plantedEvent.WorldTick, planted.WorldSystems!.Config).DayIndex;
        var sapling = world.WorldSystems.Ecology.GetResource(tree.Id);
        Assert.True(sapling.IsPlanted);
        Assert.Equal(0, sapling.Quantity);
        Assert.Equal(day + TreeGrowthRules.SaplingGrowthDays, sapling.NextRegenerationDay);
        var visible = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Resources,
            resource => resource.Id == tree.Id);
        Assert.Equal("sapling", visible.TreeStage);
        Assert.True(visible.IsPlanted);

        // Save and reload between planting and maturity: the consumed seed
        // stays consumed and the sapling keeps its growth day.
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(planted)),
            id => id == actor ? new ChooseProvider("plant_tree", offered) : new IdleProvider());
        restored.Validate();
        var reloaded = restored.ExportState();
        Assert.Equal(planted.Map.ManifestDigest, reloaded.Map.ManifestDigest);
        Assert.Equal(sapling, restored.WorldSystems.Ecology.GetResource(tree.Id));
        Assert.Equal(1, SeedCount(reloaded, actor));
        Assert.Single(reloaded.Map.Resources, resource =>
            resource.Id.StartsWith(TreeGrowthRules.PlantedTreeIdPrefix, StringComparison.Ordinal));
        Assert.Equal("sapling", Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Resources,
            resource => resource.Id == tree.Id).TreeStage);

        // The agent plants its remaining seed. Two seeds make exactly two
        // saplings on two tiles, however the work was split around the reload.
        for (var tick = 0; tick < 40 && SeedCount(restored.ExportState(), actor) > 0; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        for (var tick = 0; tick < 3; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var final = restored.ExportState();
        var saplings = final.Map.Resources.Where(resource =>
            resource.Id.StartsWith(TreeGrowthRules.PlantedTreeIdPrefix, StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, saplings.Length);
        Assert.Equal(2, saplings.Select(resource => resource.Position).Distinct().Count());
        Assert.Equal(0, SeedCount(final, actor));
        Assert.Equal(2, final.Events.Count(item => item.Kind == "tree_planted"));
        Assert.All(saplings, resource => Assert.True(restored.WorldSystems.Ecology.GetResource(resource.Id).IsPlanted));
        restored.Validate();
    }

    [Fact]
    public async Task PlantedSaplingMaturesOnceThroughTheTickAndKeepsOneYieldAcrossReload()
    {
        var (state, actor, site) = PlantingWorld("tree-planting-growth");
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var result = world.PlantTree(actor, TreeGrowthRules.Conifer, SeedLotId, site);
        Assert.True(result.Planted, result.Message);
        var treeId = result.TreeId!;

        // Pretend the growing days have passed, then let the ordinary tick grow it.
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var today = WorldCalendarRules.FromTick(saved.Society.Society.WorldTick, saved.WorldSystems!.Config).DayIndex;
        saved = saved with
        {
            WorldSystems = saved.WorldSystems with
            {
                Ecology = saved.WorldSystems.Ecology with
                {
                    Resources = saved.WorldSystems.Ecology.Resources.Select(resource => resource.Id == treeId
                        ? resource with { NextRegenerationDay = today } : resource).ToArray(),
                },
            },
        };
        using var growing = PrivateWorldRuntime.Restore(saved, _ => new IdleProvider());
        Assert.True((await growing.AdvanceOneTickAsync()).Advanced);
        var grown = growing.WorldSystems.Ecology.GetResource(treeId);
        Assert.False(grown.IsPlanted);
        Assert.Equal(1, grown.Quantity);
        Assert.Equal(ResourceState.Available, growing.ExportState().Resources.Single(item => item.ResourceId == treeId).State);
        Assert.Equal("mature", Assert.Single(new OwnerWorldObservationStore(growing).GetSnapshot().Resources,
            resource => resource.Id == treeId).TreeStage);

        using var reloaded = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(growing.ExportState())), _ => new IdleProvider());
        for (var tick = 0; tick < 3; tick++)
            Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, reloaded.WorldSystems.Ecology.GetResource(treeId).Quantity);
        Assert.Equal(0, SeedCount(reloaded.ExportState(), actor));
    }

    [Fact]
    public void PlantingRefusesIllegalTilesAndSeedsWithAReasonAndKeepsTheSeed()
    {
        var (state, actor, site) = PlantingWorld("tree-planting-refusals");
        var other = state.Inhabitants[1].InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "grain-seed", "seed", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "other-tree-seed", TreeGrowthRules.TreeSeedItem, other, 1);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var map = world.ExportState().Map;
        var buildingTiles = world.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(
                world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId),
                building.Position)).ToHashSet();
        var resourceTiles = map.Resources.Select(resource => resource.Position).ToHashSet();
        var roads = world.RoadTiles.ToHashSet();
        bool Free(GridPoint point) => !buildingTiles.Contains(point) && !roads.Contains(point) && !resourceTiles.Contains(point);
        var water = map.Tiles.First(tile => map.HydrologyAt(tile.Position) is not WaterKind.Land).Position;
        var sand = map.Tiles.First(tile => map.SurfaceAt(tile.Position) == SurfaceKind.Sand && Free(tile.Position)).Position;
        var rock = map.Tiles.First(tile => map.SurfaceAt(tile.Position) == SurfaceKind.Rock).Position;
        var building = buildingTiles.First(point => TreeGrowthRules.GroundRefusal(map, point) is null);
        var road = roads.First(point => TreeGrowthRules.GroundRefusal(map, point) is null && !buildingTiles.Contains(point));
        var tree = map.Resources.First(resource => TreeGrowthRules.IsWoodTree(resource.TreeKind) &&
            TreeGrowthRules.GroundRefusal(map, resource.Position) is null).Position;
        var orchard = map.Resources.First(resource => resource.TreeKind == TreeGrowthRules.Orchard &&
            TreeGrowthRules.GroundRefusal(map, resource.Position) is null).Position;
        var far = map.Tiles.Select(tile => tile.Position).First(point =>
            map.FootDistance(point, site) > 5 && TreeGrowthRules.GroundRefusal(map, point) is null && Free(point));
        var before = world.ExportState();

        foreach (var (species, lot, destination, planter, expected) in new[]
                 {
                     ("orchard", SeedLotId, site, actor, TreePlantingRefusal.UnsupportedSpecies),
                     ("palm", SeedLotId, site, actor, TreePlantingRefusal.UnsupportedSpecies),
                     (TreeGrowthRules.Broadleaf, SeedLotId, water, actor, TreePlantingRefusal.Water),
                     (TreeGrowthRules.Broadleaf, SeedLotId, sand, actor, TreePlantingRefusal.Sand),
                     (TreeGrowthRules.Broadleaf, SeedLotId, rock, actor, TreePlantingRefusal.UnsuitableGround),
                     (TreeGrowthRules.Broadleaf, SeedLotId, building, actor, TreePlantingRefusal.Building),
                     (TreeGrowthRules.Broadleaf, SeedLotId, road, actor, TreePlantingRefusal.Road),
                     (TreeGrowthRules.Broadleaf, SeedLotId, tree, actor, TreePlantingRefusal.Occupied),
                     (TreeGrowthRules.Broadleaf, SeedLotId, orchard, actor, TreePlantingRefusal.Occupied),
                     (TreeGrowthRules.Broadleaf, SeedLotId, new GridPoint(-1, 0), actor, TreePlantingRefusal.OutsideMap),
                     (TreeGrowthRules.Broadleaf, SeedLotId, far, actor, TreePlantingRefusal.TooFar),
                     (TreeGrowthRules.Broadleaf, "grain-seed", site, actor, TreePlantingRefusal.NotATreeSeed),
                     (TreeGrowthRules.Broadleaf, "other-tree-seed", site, actor, TreePlantingRefusal.SeedNotOwned),
                     (TreeGrowthRules.Broadleaf, "missing-seed", site, actor, TreePlantingRefusal.NoSeedLeft),
                     (TreeGrowthRules.Broadleaf, SeedLotId, site, "agent:nobody", TreePlantingRefusal.PlanterUnavailable),
                 })
        {
            var refused = world.PlantTree(planter, species, lot, destination);
            Assert.False(refused.Planted);
            Assert.Equal(expected, refused.Refusal);
            Assert.Equal(TreeGrowthRules.Reason(expected), refused.Message);
            Assert.False(string.IsNullOrWhiteSpace(refused.Message));
        }

        // Refusals change nothing: the seed, map, growth state and events are untouched.
        var after = world.ExportState();
        Assert.Equal(before.Map.ManifestDigest, after.Map.ManifestDigest);
        Assert.Equal(before.Society.Society.Inventory.Lots, after.Society.Society.Inventory.Lots);
        Assert.Equal(before.Society.Society.Inventory.Reservations, after.Society.Society.Inventory.Reservations);
        Assert.Equal(before.WorldSystems!.Ecology.Resources, after.WorldSystems!.Ecology.Resources);
        Assert.Equal(before.Events.Count, after.Events.Count);
        Assert.Equal(1, SeedCount(after, actor));

        var planted = world.PlantTree(actor, TreeGrowthRules.Broadleaf, SeedLotId, site);
        Assert.True(planted.Planted, planted.Message);
        Assert.Equal(TreeGrowthRules.PlantedTreeId(site), planted.TreeId);
        Assert.Equal(0, SeedCount(world.ExportState(), actor));
        // The seed is consumed exactly once, and the tile now holds a tree.
        Assert.Equal(TreePlantingRefusal.NoSeedLeft,
            world.PlantTree(actor, TreeGrowthRules.Broadleaf, SeedLotId, site).Refusal);
        world.Validate();
    }

    [Fact]
    public void SavedPlantedTreesMustBeLegalPlantings()
    {
        var (state, actor, site) = PlantingWorld("tree-planting-save-checks");
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        Assert.True(world.PlantTree(actor, TreeGrowthRules.Broadleaf, SeedLotId, site).Planted);
        var saved = world.ExportState();
        var treeId = TreeGrowthRules.PlantedTreeId(site);

        // An older schema cannot hold planted trees.
        Assert.Contains("schema 27", Assert.Throws<InvalidDataException>(() =>
            PrivateWorldRuntimeCodec.Encode(saved with { SchemaVersion = 26 })).Message, StringComparison.Ordinal);

        // A planted tree must be a plantable species on legal ground.
        var orchardMap = saved.Map with
        {
            Resources = saved.Map.Resources.Select(resource => resource.Id == treeId
                ? resource with { Kind = "fruit", TreeKind = TreeGrowthRules.Orchard } : resource).ToArray(),
        };
        orchardMap = orchardMap with { ManifestDigest = MapManifestCodec.Digest(orchardMap) };
        Assert.Contains("deterministic regeneration", Assert.Throws<InvalidDataException>(() =>
            PrivateWorldRuntime.Restore(saved with { Map = orchardMap })).Message, StringComparison.Ordinal);

        // A planted tree cannot share its tile with a Road.
        var onRoad = saved with { RoadTiles = saved.RoadTiles!.Append(site).ToArray() };
        Assert.Contains(treeId, Assert.Throws<InvalidDataException>(() =>
            PrivateWorldRuntime.Restore(onRoad)).Message, StringComparison.Ordinal);

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)));
        Assert.Equal(TreeGrowthRules.Broadleaf, restored.ExportState().Map.GetResource(treeId).TreeKind);
    }

    /// <summary>A started world where the first agent carries one tree seed beside an empty plantable tile.</summary>
    private static (PrivateWorldRuntimeState State, string Actor, GridPoint Site) PlantingWorld(string seed)
    {
        var initial = GeographyGeneratorTests.StartedGeneratedWorld(new GeographyOptions(seed, WorldSizePreset.Small));
        var actor = initial.Inhabitants[0].InhabitantId;
        var map = initial.Map;
        using var probe = PrivateWorldRuntime.Restore(initial);
        var blocked = probe.WorldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(
                    probe.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId),
                    building.Position))
            .Concat(probe.RoadTiles)
            .Concat(map.Resources.Select(resource => resource.Position))
            .Concat(map.CampObjects.Select(item => item.Position))
            .ToHashSet();
        var occupied = initial.Inhabitants.Skip(1).Select(person => person.Position).ToHashSet();
        var start = initial.Inhabitants[0].Position;
        var (site, stand) = map.Tiles.Select(tile => tile.Position)
            .Where(point => !blocked.Contains(point) && TreeGrowthRules.GroundRefusal(map, point) is null &&
                map.IsReachableOnFoot(start, point))
            .OrderBy(point => map.FootDistance(start, point))
            .Select(point => (Site: point, Stand: map.FootNeighbors(point)
                .Where(neighbor => !occupied.Contains(neighbor))
                .Select(neighbor => (GridPoint?)neighbor).FirstOrDefault()))
            .Where(item => item.Stand is not null)
            .Select(item => (item.Site, Stand: item.Stand!.Value))
            .First();
        var state = WithTreeSeeds(Fed(initial, actor), actor, 1) with
        {
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = stand, HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        return (state, actor, site);
    }

    private static PrivateWorldRuntimeState Fed(PrivateWorldRuntimeState state, string actor) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { HungerBasisPoints = 9_000 } : person).ToArray(),
    };

    private static PrivateWorldRuntimeState WithTreeSeeds(PrivateWorldRuntimeState state, string actor, int quantity) =>
        state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, SeedLotId,
                        TreeGrowthRules.TreeSeedItem, actor, quantity),
                },
            },
        };

    private static int SeedCount(PrivateWorldRuntimeState state, string actor) => state.Society.Society.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.ItemKind == TreeGrowthRules.TreeSeedItem).Sum(lot => lot.Quantity);

    private sealed class ChooseProvider(string candidateId, List<IReadOnlyList<string>> offered) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            offered.Add(request.Observation.Candidates.Select(candidate => candidate.Id).ToArray());
            var chosen = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == candidateId) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [chosen] },
            }, cancellationToken);
        }
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")],
                },
            }, cancellationToken);
    }
}
