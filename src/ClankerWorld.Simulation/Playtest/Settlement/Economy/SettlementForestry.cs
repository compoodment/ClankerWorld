using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    // How far from an agent the built-in choice looks for open planting ground.
    private const int PlantingSearchRadius = 10;

    private static bool IsPlantedTree(MapResource resource) =>
        resource.Id.StartsWith(TreeGrowthRules.PlantedTreeIdPrefix, StringComparison.Ordinal);

    private void AddForestryCandidates(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        if (!HasCarriedItem(actor, TreeGrowthRules.TreeSeedItem) &&
            SharedItem(TreeGrowthRules.TreeSeedItem, actor) is null)
            return;
        if (ReplantableTree(state.Position) is { } tree)
            candidates.Add(new CognitionCandidate("replant_tree",
                "Carry a tree seed to a stump and replant it.", 32, tree.Id));
        if (PlantingSite(actor, state.Position) is not null)
            candidates.Add(new CognitionCandidate("plant_tree",
                "Plant a tree seed on open ground outside the Town.", 34));
    }

    private MapResource? ReplantableTree(GridPoint origin)
    {
        var depleted = worldSystems.Ecology.Resources.Where(ecology =>
                ecology.Quantity == 0 && !ecology.IsPlanted)
            .Select(ecology => ecology.Id).ToHashSet(StringComparer.Ordinal);
        return map.Resources
            .Where(resource => TreeGrowthRules.IsWoodTree(resource.TreeKind) && resource.IsRenewable &&
                depleted.Contains(resource.Id) && map.IsReachableFromCampOnFoot(resource.Position))
            .OrderBy(resource => map.FootDistance(origin, resource.Position))
            .ThenBy(resource => resource.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private void ReplantTree(string actor, PlaytestInhabitantState state)
    {
        var tree = ReplantableTree(state.Position);
        if (tree is null) return;
        if (!HasCarriedItem(actor, TreeGrowthRules.TreeSeedItem))
        {
            CollectEquipment(actor, state, TreeGrowthRules.TreeSeedItem);
            return;
        }
        if (!IsWithinInteractionRange(state.Position, tree.Position, ResourceInteractionRange))
        {
            MoveToward(actor, state, tree.Position, "replant_tree", ResourceInteractionRange);
            return;
        }

        var seed = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
            lot.OwnerId == actor && lot.ItemKind == TreeGrowthRules.TreeSeedItem && AvailableLotQuantity(lot) > 0);
        if (seed is null) return;
        var ecology = worldSystems.Ecology.GetResource(tree.Id);
        if (ecology.Quantity != 0 || ecology.IsPlanted) return;
        society.Apply(checkpoint => SocietyFixture.ConsumeInventory(checkpoint,
            actor, seed.Id, 1, "tree_replanting"));
        var day = WorldCalendarRules.FromTick(WorldTick, worldSystems.Config).DayIndex;
        var planted = ecology with
        {
            State = EcologyResourceState.Regenerating,
            IsPlanted = true,
            NextRegenerationDay = day + TreeGrowthRules.SaplingGrowthDays,
        };
        worldSystems = worldSystems with
        {
            Ecology = worldSystems.Ecology with
            {
                Resources = worldSystems.Ecology.Resources.Select(resource =>
                    resource.Id == tree.Id ? planted : resource).ToArray(),
            },
        };
        SyncEcologyResourceStates();
        AppendEvent("tree_replanted", $"{actor}:{tree.Id}:{tree.TreeKind}");
    }

    /// <summary>
    /// Plants one tree seed from the planter's own lot on an empty tile next
    /// to them. The seed is consumed exactly once and a saved sapling is
    /// created; a refused request changes nothing and keeps the seed.
    /// </summary>
    public TreePlantingResult PlantTree(string planterId, string species, string seedLotId, GridPoint destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(species);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedLotId);
        gate.Wait();
        try
        {
            return PlantTreeCore(planterId.Trim(), species.Trim(), seedLotId.Trim(), destination);
        }
        finally
        {
            gate.Release();
        }
    }

    private TreePlantingResult PlantTreeCore(string planterId, string species, string seedLotId, GridPoint destination)
    {
        if (!TreeGrowthRules.IsPlantableSpecies(species))
            return TreePlantingResult.Refused(TreePlantingRefusal.UnsupportedSpecies);
        if (!inhabitants.TryGetValue(planterId, out var planter) || !AdultResident(planterId))
            return TreePlantingResult.Refused(TreePlantingRefusal.PlanterUnavailable);
        var seed = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == seedLotId);
        if (seed is null)
            return TreePlantingResult.Refused(TreePlantingRefusal.NoSeedLeft);
        if (seed.ItemKind != TreeGrowthRules.TreeSeedItem)
            return TreePlantingResult.Refused(TreePlantingRefusal.NotATreeSeed);
        if (seed.OwnerId != planterId)
            return TreePlantingResult.Refused(TreePlantingRefusal.SeedNotOwned);
        if (AvailableLotQuantity(seed) < 1)
            return TreePlantingResult.Refused(TreePlantingRefusal.NoSeedLeft);
        var obstacles = PlantingObstacles();
        if (PlantingSiteRefusal(destination, obstacles) is { } site)
            return TreePlantingResult.Refused(site);
        if (!IsWithinInteractionRange(planter.Position, destination, ResourceInteractionRange))
            return TreePlantingResult.Refused(TreePlantingRefusal.TooFar);
        if (TreeAreaFull(destination))
            return TreePlantingResult.Refused(TreePlantingRefusal.AreaFull);

        society.Apply(checkpoint => SocietyFixture.ConsumeInventory(checkpoint,
            planterId, seed.Id, 1, "tree_planting"));
        var tree = new MapResource(TreeGrowthRules.PlantedTreeId(destination), "construction", destination, true,
            species);
        map = map with
        {
            Resources = map.Resources.Append(tree).OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray(),
        };
        map = map with { ManifestDigest = MapManifestCodec.Digest(map) };
        var day = WorldCalendarRules.FromTick(WorldTick, worldSystems.Config).DayIndex;
        var chunk = worldSystems.Chunks.Single(item =>
            item.Coordinate == ChunkRules.ToChunkCoordinate(destination, item.ChunkSize));
        var origin = chunk.Coordinate.Origin(chunk.ChunkSize);
        worldSystems = worldSystems with
        {
            Ecology = worldSystems.Ecology with
            {
                Resources = worldSystems.Ecology.Resources.Append(TreeGrowthRules.PlantedSapling(tree, day))
                    .OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray(),
            },
            Chunks = worldSystems.Chunks.Select(item => item.Coordinate != chunk.Coordinate
                ? item
                : ChunkManifestCodec.WithDigest(item with
                {
                    Resources = item.Resources.Append(new ChunkResourceMetadata(tree.Id, tree.Kind,
                            new GridPoint(destination.X - origin.X, destination.Y - origin.Y), true))
                        .OrderBy(resource => resource.ResourceId, StringComparer.Ordinal).ToArray(),
                })).ToArray(),
        };
        SyncEcologyResourceStates();
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("tree_planted", $"{planterId}:{tree.Id}:{species}");
        return TreePlantingResult.Success(tree.Id, species);
    }

    private void PlantTreeNearby(string actor, PlaytestInhabitantState state)
    {
        if (PlantingSite(actor, state.Position) is not { } site) return;
        if (!HasCarriedItem(actor, TreeGrowthRules.TreeSeedItem))
        {
            CollectEquipment(actor, state, TreeGrowthRules.TreeSeedItem);
            return;
        }
        if (!IsWithinInteractionRange(state.Position, site, ResourceInteractionRange))
        {
            MoveToward(actor, state, site, "plant_tree", ResourceInteractionRange);
            return;
        }
        var seed = society.Checkpoint.Inventory.Lots
            .Where(lot => lot.OwnerId == actor && lot.ItemKind == TreeGrowthRules.TreeSeedItem &&
                AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).First();
        var result = PlantTreeCore(actor, PlantingSpecies(site), seed.Id, site);
        if (!result.Planted)
            AppendEvent("tree_planting_refused", $"{actor}:{result.Refusal}");
    }

    /// <summary>
    /// The built-in planting choice: the nearest reachable open tile outside
    /// every Town border, so new trees do not block Town building sites.
    /// </summary>
    private GridPoint? PlantingSite(string actor, GridPoint origin)
    {
        var obstacles = PlantingObstacles();
        var townTiles = towns.SelectMany(town => town.BorderTiles ?? []).ToHashSet();
        var sites = new List<GridPoint>();
        for (var dy = -PlantingSearchRadius; dy <= PlantingSearchRadius; dy++)
            for (var dx = -PlantingSearchRadius; dx <= PlantingSearchRadius; dx++)
            {
                var x = origin.X + dx;
                if (map.WrapsEastWest) x = (x % map.Width + map.Width) % map.Width;
                var point = new GridPoint(x, origin.Y + dy);
                if (!map.Contains(point) || townTiles.Contains(point) ||
                    PlantingSiteRefusal(point, obstacles) is not null || !map.IsReachableOnFoot(origin, point))
                    continue;
                sites.Add(point);
            }
        return sites
            .OrderBy(point => map.FootDistance(origin, point))
            .ThenBy(point => point.Y)
            .ThenBy(point => point.X)
            .Where(point => !TreeAreaFull(point))
            .Select(point => (GridPoint?)point)
            .FirstOrDefault(point => IsWithinInteractionRange(origin, point!.Value, ResourceInteractionRange) ||
                FindUnoccupiedRoute(actor, origin, point.Value, ResourceInteractionRange).Count > 0);
    }

    /// <summary>A tree seed grows into the species of the nearest wood tree, or by climate when none is near.</summary>
    private string PlantingSpecies(GridPoint site) =>
        map.Resources.Where(resource => TreeGrowthRules.IsWoodTree(resource.TreeKind) &&
                map.FootDistance(site, resource.Position) <= PlantingSearchRadius)
            .OrderBy(resource => map.FootDistance(site, resource.Position))
            .ThenBy(resource => resource.Id, StringComparer.Ordinal)
            .Select(resource => resource.TreeKind!)
            .FirstOrDefault() ??
        (map.ClimateAt(site) is ClimateZone.Cold or ClimateZone.Polar
            ? TreeGrowthRules.Conifer : TreeGrowthRules.Broadleaf);

    private (HashSet<GridPoint> Buildings, HashSet<GridPoint> Occupied) PlantingObstacles()
    {
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var buildings = worldSimulation.Buildings.SelectMany(building =>
                definitions.TryGetValue(building.DefinitionId, out var definition)
                    ? WorldContentSimulationRules.Footprint(definition, building)
                    : [building.Position])
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.State == WorldProductionJobState.Running).SelectMany(ExpansionTiles))
            .ToHashSet();
        var occupied = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .ToHashSet();
        return (buildings, occupied);
    }

    private TreePlantingRefusal? PlantingSiteRefusal(GridPoint point,
        (HashSet<GridPoint> Buildings, HashSet<GridPoint> Occupied) obstacles)
    {
        if (TreeGrowthRules.GroundRefusal(map, point) is { } ground) return ground;
        if (obstacles.Buildings.Contains(point)) return TreePlantingRefusal.Building;
        if (RoadAndBridgeTiles().Contains(point)) return TreePlantingRefusal.Road;
        if (obstacles.Occupied.Contains(point)) return TreePlantingRefusal.Occupied;
        return null;
    }

    // Planted trees share the bounded per-chunk resource budget with
    // generated sites, so a crowded area cannot grow without limit.
    private bool TreeAreaFull(GridPoint point)
    {
        var config = worldSystems.Config;
        var chunk = worldSystems.Chunks.FirstOrDefault(item =>
            item.Coordinate == ChunkRules.ToChunkCoordinate(point, item.ChunkSize));
        return chunk is null || chunk.Resources.Count >= config.MaxResourcesPerChunk ||
            worldSystems.Ecology.Resources.Count >= config.MaxResourcesPerChunk * config.MaxChunkCount;
    }

    private void ValidatePlantedTrees()
    {
        var obstacles = PlantingObstacles();
        var ecology = worldSystems.Ecology.Resources.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var tree in map.Resources.Where(IsPlantedTree))
        {
            if (!TreeGrowthRules.IsValidPlantedTree(map, tree) || obstacles.Buildings.Contains(tree.Position) ||
                RoadAndBridgeTiles().Contains(tree.Position) ||
                map.CampObjects.Any(item => item.Position == tree.Position) ||
                !ecology.TryGetValue(tree.Id, out var growth) || growth.Kind != tree.Kind ||
                growth.Position != tree.Position || !growth.IsRenewable)
                throw new InvalidDataException($"The planted tree '{tree.Id}' is invalid.");
        }
    }
}
