using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed record PlacedBuilding(
    string InstanceId,
    string DefinitionId,
    GridPoint Position,
    long PlacedTick,
    string? TownId = null,
    string? HouseholdId = null);

public enum WorldProductionJobState
{
    Running,
    Completed,
    Cancelled,
}

public sealed record WorldProductionJob(
    string JobId,
    string RecipeId,
    string BuildingInstanceId,
    string WorkerId,
    long StartedTick,
    long CompletionTick,
    WorldProductionJobState State,
    IReadOnlyList<string> InputReservationIds);

/// <summary>
/// A production job can use a generated fertile-land site when its recipe is
/// tagged <c>crop</c>. The existing BuildingInstanceId field on
/// <see cref="WorldProductionJob"/> stores the canonical site ID for that
/// case, so the same deterministic completion and inventory path handles
/// both workstation production and cultivation.
/// </summary>
public static class WorldBuildSiteRules
{
    public static string FertileLandSiteId(GridPoint position) =>
        $"{SeededMapGenerator.FertileLandResourceId}:{position.X},{position.Y}";

    public static bool TryGetFertileLandPosition(string siteId, out GridPoint position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(siteId))
        {
            return false;
        }

        var prefix = SeededMapGenerator.FertileLandResourceId + ":";
        if (!siteId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var coordinates = siteId[prefix.Length..].Split(',', StringSplitOptions.None);
        if (coordinates.Length != 2 ||
            !int.TryParse(coordinates[0], out var x) ||
            !int.TryParse(coordinates[1], out var y))
        {
            return false;
        }

        position = new GridPoint(x, y);
        return string.Equals(siteId, FertileLandSiteId(position), StringComparison.Ordinal);
    }
}

public sealed record WorldContentSimulationState(
    IReadOnlyList<PlacedBuilding> Buildings,
    IReadOnlyList<WorldProductionJob> ProductionJobs,
    long NextProductionJobSequence,
    IReadOnlyList<WorldProductionJob>? CropBuilds = null)
{
    public static WorldContentSimulationState Empty { get; } = new([], [], 1, []);
}

public sealed record BuildingPlacementResult(
    bool Applied,
    string InstanceId,
    string DefinitionId,
    GridPoint Position,
    string? Failure)
{
    public static BuildingPlacementResult Success(
        PlacedBuilding building) => new(
            true,
            building.InstanceId,
            building.DefinitionId,
            building.Position,
            null);

    public static BuildingPlacementResult Rejected(
        string instanceId,
        string definitionId,
        GridPoint position,
        string failure) => new(false, instanceId, definitionId, position, failure);
}

public sealed record ProductionStartResult(
    bool Applied,
    string? JobId,
    string RecipeId,
    string? Failure)
{
    public static ProductionStartResult Success(WorldProductionJob job) =>
        new(true, job.JobId, job.RecipeId, null);

    public static ProductionStartResult Rejected(string recipeId, string failure) =>
        new(false, null, recipeId, failure);
}

public static class WorldContentSimulationRules
{
    public static void Validate(
        WorldContentSimulationState state,
        DeclarativeWorldContentState definitions,
        SeededMap map,
        long worldTick)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        definitions.Validate();
        if (state.NextProductionJobSequence <= 0)
        {
            throw new InvalidDataException("The next production job sequence must be positive.");
        }

        var buildingDefinitions = definitions.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var recipeDefinitions = definitions.Recipes.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var buildingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var building in state.Buildings)
        {
            ArgumentNullException.ThrowIfNull(building);
            ContentPackageRules.ValidateLocalId(building.InstanceId);
            if (!buildingIds.Add(building.InstanceId) || !buildingDefinitions.TryGetValue(building.DefinitionId, out var definition))
            {
                throw new InvalidDataException("Placed buildings must have unique IDs and registered definitions.");
            }

            var isHouse = definition.Tags.Contains("house", StringComparer.Ordinal);
            var acceptsHouseholdOwner = definition.Tags.Any(tag => tag is "house" or "farmhouse" or "blacksmith");
            if (isHouse && string.IsNullOrWhiteSpace(building.HouseholdId) ||
                building.HouseholdId is not null && string.IsNullOrWhiteSpace(building.HouseholdId) ||
                building.HouseholdId is not null && !acceptsHouseholdOwner ||
                building.HouseholdId is { } householdId && householdId != householdId.Trim())
            {
                throw new InvalidDataException($"Placed building '{building.InstanceId}' has invalid household ownership.");
            }

            var existing = state.Buildings
                .Where(item => item.InstanceId != building.InstanceId)
                .Select(item => (Placement: item, Definition: buildingDefinitions[item.DefinitionId]));
            if (building.PlacedTick < 0 || building.PlacedTick > worldTick ||
                !Fits(map, existing, definition, building.Position))
            {
                throw new InvalidDataException($"Placed building '{building.InstanceId}' has an invalid footprint.");
            }
        }

        var jobIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in state.ProductionJobs)
        {
            ArgumentNullException.ThrowIfNull(job);
            ContentPackageRules.ValidateLocalId(job.JobId);
            if (!jobIds.Add(job.JobId) || !recipeDefinitions.TryGetValue(job.RecipeId, out var recipe) ||
                (!buildingIds.Contains(job.BuildingInstanceId) &&
                    !IsValidFertileLandJobSite(recipe, job.BuildingInstanceId, map)))
            {
                throw new InvalidDataException("Production jobs must have unique IDs and registered references.");
            }

            if (string.IsNullOrWhiteSpace(job.WorkerId) || job.StartedTick < 0 ||
                job.CompletionTick <= job.StartedTick || job.CompletionTick < worldTick &&
                job.State == WorldProductionJobState.Running ||
                job.InputReservationIds is null ||
                job.InputReservationIds.Count != job.InputReservationIds.Distinct(StringComparer.Ordinal).Count())
            {
                throw new InvalidDataException($"Production job '{job.JobId}' is malformed.");
            }
        }

        var cropBuilds = state.CropBuilds ?? [];
        var cropJobIds = new HashSet<string>(StringComparer.Ordinal);
        var activeCropSites = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cropBuild in cropBuilds)
        {
            ArgumentNullException.ThrowIfNull(cropBuild);
            ContentPackageRules.ValidateLocalId(cropBuild.JobId);
            if (!cropJobIds.Add(cropBuild.JobId) ||
                jobIds.Contains(cropBuild.JobId) ||
                !recipeDefinitions.TryGetValue(cropBuild.RecipeId, out var recipe) ||
                !recipe.IsCrop ||
                cropBuild.State is not (WorldProductionJobState.Running or
                    WorldProductionJobState.Completed or WorldProductionJobState.Cancelled) ||
                string.IsNullOrWhiteSpace(cropBuild.WorkerId) ||
                cropBuild.StartedTick < 0 ||
                cropBuild.CompletionTick <= cropBuild.StartedTick ||
                cropBuild.CompletionTick < worldTick && cropBuild.State == WorldProductionJobState.Running ||
                cropBuild.InputReservationIds is null ||
                cropBuild.InputReservationIds.Count != cropBuild.InputReservationIds.Distinct(StringComparer.Ordinal).Count() ||
                !WorldBuildSiteRules.TryGetFertileLandPosition(cropBuild.BuildingInstanceId, out var cropPosition) ||
                !IsFertileLandPosition(map, cropPosition) ||
                cropBuild.State == WorldProductionJobState.Running &&
                    !activeCropSites.Add(cropBuild.BuildingInstanceId))
            {
                throw new InvalidDataException($"Crop build '{cropBuild.JobId}' is malformed.");
            }
        }

        if (!state.Buildings.Select(item => item.InstanceId).SequenceEqual(
                state.Buildings.Select(item => item.InstanceId).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            !state.ProductionJobs.Select(item => item.JobId).SequenceEqual(
                state.ProductionJobs.Select(item => item.JobId).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            !cropBuilds.Select(item => item.JobId).SequenceEqual(
                cropBuilds.Select(item => item.JobId).Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("World content simulation state is not in canonical order.");
        }
    }

    public static bool Fits(
        SeededMap map,
        IEnumerable<(PlacedBuilding Placement, BuildingDefinition Definition)> existingBuildings,
        BuildingDefinition definition,
        GridPoint position)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(existingBuildings);
        ArgumentNullException.ThrowIfNull(definition);
        var footprint = Footprint(definition, position).ToArray();
        if (footprint.Any(point => !map.IsBuildable(point)))
        {
            return false;
        }

        var occupied = map.CampObjects
            .Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .ToHashSet();
        foreach (var existing in existingBuildings)
        {
            foreach (var point in Footprint(existing.Definition, existing.Placement.Position))
            {
                occupied.Add(point);
            }
        }

        return footprint.All(point => !occupied.Contains(point));
    }

    public static IEnumerable<GridPoint> Footprint(BuildingDefinition definition, GridPoint position)
    {
        for (var y = 0; y < definition.Height; y++)
        {
            for (var x = 0; x < definition.Width; x++)
            {
                yield return new GridPoint(position.X + x, position.Y + y);
            }
        }
    }

    public static WorldContentSimulationState RemovePackage(
        WorldContentSimulationState state,
        string packageDigest)
    {
        ContentPackageRules.ValidateDigest(packageDigest, nameof(packageDigest));
        if (state.Buildings.Any(item => item.DefinitionId.StartsWith($"{packageDigest}/", StringComparison.Ordinal)) ||
            state.ProductionJobs.Concat(state.CropBuilds ?? []).Any(item =>
                item.RecipeId.StartsWith($"{packageDigest}/", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Content with committed buildings or production history requires an explicit migration before removal.");
        }
        // Withdrawal of unused definitions cannot mutate another package's jobs.
        return state;
    }

    public static bool IsFertileLandPosition(SeededMap map, GridPoint position) =>
        map.IsBuildable(position) && map.Resources.Any(resource =>
            resource.Id == SeededMapGenerator.FertileLandResourceId &&
            resource.Kind == "fertile_land" &&
            resource.Position == position);

    private static bool IsValidFertileLandJobSite(
        RecipeDefinition recipe,
        string siteId,
        SeededMap map) =>
        recipe.IsCrop &&
        recipe.WorkstationBuildingId is null &&
        WorldBuildSiteRules.TryGetFertileLandPosition(siteId, out var position) &&
        IsFertileLandPosition(map, position);
}
