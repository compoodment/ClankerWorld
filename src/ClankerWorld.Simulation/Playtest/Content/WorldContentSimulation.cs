using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// A building in the world. <see cref="Entrance"/> is the ground tile just
/// outside its door, beside one edge of the footprint; the door is on that
/// side. It is set when the building's Road is laid, and is null for a
/// building without a Road.
/// </summary>
public sealed record PlacedBuilding(
    string InstanceId,
    string DefinitionId,
    GridPoint Position,
    long PlacedTick,
    string? TownId = null,
    string? HouseholdId = null,
    GridPoint? Entrance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BuildingFootprintRevision? Footprint = null);

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
/// Crop jobs refer to an owned, tilled tile using a canonical field site ID.
/// </summary>
public static class WorldBuildSiteRules
{
    public static string FieldSiteId(GridPoint position) =>
        $"field:{position.X},{position.Y}";

    public static bool TryGetFieldPosition(string siteId, out GridPoint position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(siteId))
        {
            return false;
        }

        const string prefix = "field:";
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
        return string.Equals(siteId, FieldSiteId(position), StringComparison.Ordinal);
    }
}

public sealed record WorldContentSimulationState(
    IReadOnlyList<PlacedBuilding> Buildings,
    IReadOnlyList<WorldProductionJob> ProductionJobs,
    long NextProductionJobSequence,
    IReadOnlyList<WorldProductionJob>? CropBuilds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BuildingExpansionJob>? BuildingExpansions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<HouseGuestInvitation>? GuestInvitations = null,
    IReadOnlyList<FarmFieldTile>? Fields = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PhysicalCart>? Carts = null)
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

            if (building.Footprint is { } footprint && !BuildingStorageRules.IsSupported(definition, footprint))
                throw new InvalidDataException("The saved building footprint revision is not supported.");
            definition = BuildingStorageRules.EffectiveDefinition(definition, building);
            var isHouse = definition.Tags.Contains("house", StringComparer.Ordinal);
            var acceptsHouseholdOwner = definition.Tags.Any(HouseholdBuildingKinds.IsKindTag);
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

            if (building.Entrance is { } entrance &&
                (!map.IsBuildable(entrance) || !IsEntrance(definition, building.Position, entrance)))
            {
                throw new InvalidDataException($"Placed building '{building.InstanceId}' has an invalid entrance.");
            }
        }

        var jobIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in state.ProductionJobs)
        {
            ArgumentNullException.ThrowIfNull(job);
            ContentPackageRules.ValidateLocalId(job.JobId);
            if (!jobIds.Add(job.JobId) || !recipeDefinitions.TryGetValue(job.RecipeId, out var recipe) ||
                (!buildingIds.Contains(job.BuildingInstanceId) &&
                    !IsValidFieldJobSite(recipe, job.BuildingInstanceId, map)))
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
        var fields = state.Fields ?? [];
        if (fields.Select(field => field.Position).Distinct().Count() != fields.Count ||
            !fields.Select(field => field.Position).SequenceEqual(fields.OrderBy(field => field.Position.Y)
                .ThenBy(field => field.Position.X).Select(field => field.Position)))
            throw new InvalidDataException("Field tiles must be unique and in map order.");
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.HouseholdId) || !Enum.IsDefined(field.Stage) ||
                !LandFertilityRules.IsFarmable(map, field.Position) || field.PreparedTick < 0 ||
                field.PreparedTick > worldTick || field.LastWorkedTick < -1 || field.LastWorkedTick > worldTick ||
                field.WorkDone < 0 || field.WorkDone > (field.Stage == FarmFieldStage.Ready ? 3 : 8) ||
                field.TendingWork is < 0 or > 4 ||
                field.Stage is FarmFieldStage.Planted or FarmFieldStage.Growing or FarmFieldStage.Ready &&
                    (field.RecipeId is null || field.JobId is null) ||
                field.Harvest is not null && (field.Stage != FarmFieldStage.Ready ||
                    field.Harvest.Select(output => output.ResourceId).Distinct(StringComparer.Ordinal).Count() != field.Harvest.Count ||
                    field.Harvest.Any(output => output.Amount <= 0 || string.IsNullOrWhiteSpace(output.ResourceId) ||
                        field.RecipeId is null || !recipeDefinitions.TryGetValue(field.RecipeId, out var harvestRecipe) ||
                        !harvestRecipe.Outputs.Any(expected => expected.ResourceId == output.ResourceId))) ||
                field.Stage == FarmFieldStage.Ready && field.Harvest is null ||
                field.RecipeId is { } recipeId && !recipeDefinitions.ContainsKey(recipeId) ||
                field.JobId is { } jobId && !cropBuilds.Any(job => job.JobId == jobId &&
                    job.BuildingInstanceId == WorldBuildSiteRules.FieldSiteId(field.Position)) ||
                state.Buildings.Any(building => Footprint(buildingDefinitions[building.DefinitionId], building)
                    .Contains(field.Position)) || map.Resources.Any(resource => resource.Position == field.Position) ||
                map.CampObjects.Any(item => item.Position == field.Position))
                throw new InvalidDataException("A saved field has invalid ground, work or crop state.");
        }
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
                !WorldBuildSiteRules.TryGetFieldPosition(cropBuild.BuildingInstanceId, out var cropPosition) ||
                !IsFieldPosition(map, cropPosition) ||
                !fields.Any(field => field.Position == cropPosition) ||
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
        if (!PortNavigationRules.IsPort(definition) && footprint.Any(point => !map.IsBuildable(point)))
        {
            return false;
        }

        var occupied = map.CampObjects
            .Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .ToHashSet();
        foreach (var existing in existingBuildings)
        {
            foreach (var point in Footprint(existing.Definition, existing.Placement))
            {
                occupied.Add(point);
            }
            if (PortNavigationRules.IsPort(existing.Definition))
                occupied.UnionWith(PortNavigationRules.Geometry(map, existing.Definition, existing.Placement.Position).DockingTiles);
        }
        if (PortNavigationRules.IsPort(definition))
            return PortNavigationRules.Fits(map, definition, position, occupied, out _);
        return footprint.All(point => !occupied.Contains(point));
    }

    /// <summary>
    /// Whether a tile can be a building's entrance: just outside the footprint,
    /// directly beside one of its edges (not a corner).
    /// </summary>
    public static bool IsEntrance(BuildingDefinition definition, GridPoint position, GridPoint entrance)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var besideColumn = entrance.X >= position.X && entrance.X < position.X + definition.Width;
        var besideRow = entrance.Y >= position.Y && entrance.Y < position.Y + definition.Height;
        return besideColumn && (entrance.Y == position.Y - 1 || entrance.Y == position.Y + definition.Height) ||
            besideRow && (entrance.X == position.X - 1 || entrance.X == position.X + definition.Width);
    }

    public static IEnumerable<GridPoint> Footprint(BuildingDefinition definition, PlacedBuilding building) =>
        Footprint(BuildingStorageRules.EffectiveDefinition(definition, building), building.Position);

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

    public static bool IsFieldPosition(SeededMap map, GridPoint position) =>
        LandFertilityRules.IsFarmable(map, position);

    private static bool IsValidFieldJobSite(
        RecipeDefinition recipe,
        string siteId,
        SeededMap map) =>
        recipe.IsCrop &&
        recipe.WorkstationBuildingId is null &&
        WorldBuildSiteRules.TryGetFieldPosition(siteId, out var position) &&
        IsFieldPosition(map, position);
}
