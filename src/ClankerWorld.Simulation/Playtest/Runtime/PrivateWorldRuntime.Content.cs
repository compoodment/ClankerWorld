using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public static ContentResolutionResult PreviewContent(
        IEnumerable<ContentPackageManifest> availablePackages,
        IEnumerable<string> rootPackageIds) =>
        ContentPackageResolver.Resolve(availablePackages, rootPackageIds);

    public static ContentPreviewResult PreviewWorldContent(
        IEnumerable<ContentPackageManifest> availablePackages,
        IEnumerable<string> rootPackageIds,
        DeclarativeWorldContentState? baseWorldContent = null) =>
        ContentPackagePreview.Run(availablePackages, rootPackageIds, baseWorldContent);

    public ContentResolutionResult ResolveContent(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        gate.Wait();
        try
        {
            var available = contentRegistry.ExportState().Packages
                .Select(package => package.Manifest)
                .ToArray();
            return ContentPackageResolver.Resolve(available, [packageId]);
        }
        finally
        {
            gate.Release();
        }
    }

    public ContentPackageRecord ProposeContent(ContentPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        gate.Wait();
        try
        {
            var record = contentRegistry.Propose(manifest, WorldTick);
            AppendEvent("content_proposed", manifest.PackageId);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool StageStarterContent()
    {
        gate.Wait();
        try
        {
            if (society.Checkpoint.IsPaused || contentRegistry.ExportState().Packages
                .Any(package => package.Manifest.PackageId == StarterContent.PackageId))
            {
                // Never undo an owner's rollback or quarantine, or mutate a paused save.
                return false;
            }

            var manifest = StarterContent.Create();
            _ = ContentDefinitionPayloadCodec.ApplyPackage(worldContent, manifest);
            var resolution = ContentPackageResolver.Resolve([manifest], [manifest.PackageId]);
            contentRegistry.Propose(manifest, WorldTick);
            contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
            contentRegistry.Approve(manifest.PackageId, WorldTick);
            contentRegistry.Stage(manifest.PackageId, WorldTick);
            AppendEvent("starter_content_staged", manifest.PackageId);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Install trusted shipped content before the first paused Town layout is accepted.</summary>
    public void InitializeFirstTownContent()
    {
        gate.Wait();
        try
        {
            if (geographyOptions is null || founderSetup is not { Started: false } ||
                !society.Checkpoint.IsPaused || WorldTick != 0 ||
                contentRegistry.ExportState().Packages.Count != 0)
                throw new InvalidOperationException("Initial content is available only to a fresh paused generated world.");
            ContentPackageManifest[] manifests =
            [
                StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(),
                WarehouseContent.Create(), FarmContent.Create(), BlacksmithContent.Create(),
                HouseCookingContent.Create(),
            ];
            foreach (var manifest in manifests)
            {
                var packages = contentRegistry.ExportState().Packages;
                var resolution = ContentPackageResolver.Resolve(
                    packages.Select(package => package.Manifest).Append(manifest), [manifest.PackageId]);
                var definitions = ContentDefinitionPayloadCodec.ApplyPackage(worldContent, manifest);
                contentRegistry.Propose(manifest, 0);
                contentRegistry.Validate(manifest.PackageId, resolution, 0);
                contentRegistry.Approve(manifest.PackageId, 0);
                contentRegistry.Stage(manifest.PackageId, 0);
                var reservation = assetReservations.TryReservePackage(manifest.PackageId,
                    manifest.AssetReservations ?? [], 0);
                if (!reservation.IsSuccess)
                    throw new InvalidOperationException($"Initial content assets were rejected: {reservation.FailureCode}");
                contentRegistry.ActivateAtCreation(manifest.PackageId);
                worldContent = definitions;
                AppendEvent("initial_content_activated", manifest.PackageId);
            }
        }
        finally { gate.Release(); }
    }

    public ContentPackageRecord ValidateContent(
        string packageId,
        ContentResolutionResult resolution)
    {
        gate.Wait();
        try
        {
            var manifest = GetContentManifest(packageId);
            _ = ContentDefinitionPayloadCodec.ApplyPackage(worldContent, manifest);
            var record = contentRegistry.Validate(packageId, resolution, WorldTick);
            AppendEvent("content_validated", packageId);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public ContentPackageRecord ApproveContent(string packageId)
    {
        gate.Wait();
        try
        {
            var record = contentRegistry.Approve(packageId, WorldTick);
            AppendEvent("content_approved", packageId);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public ContentPackageRecord StageContent(string packageId)
    {
        gate.Wait();
        try
        {
            var manifest = GetContentManifest(packageId);
            _ = ContentDefinitionPayloadCodec.ApplyPackage(worldContent, manifest);
            var record = contentRegistry.Stage(packageId, WorldTick);
            AppendEvent("content_staged", packageId);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public BuildingPlacementResult PlaceBuilding(
        string instanceId,
        string definitionId,
        GridPoint position,
        string? householdId = null)
    {
        gate.Wait();
        try
        {
            var definition = worldContent.Buildings.SingleOrDefault(item => item.CanonicalId == definitionId);
            var assignedTown = definition is null ? null : TownForOwnerPlacement(position, definition);
            return PlaceBuildingCore(instanceId, definitionId, position, "building_placed", assignedTown?.Id, householdId);
        }
        finally
        {
            gate.Release();
        }
    }

    private BuildingPlacementResult PlaceBuildingCore(
        string instanceId,
        string definitionId,
        GridPoint position,
        string eventKind,
        string? assignedTownId = null,
        string? householdId = null,
        string? constructionOwnerId = null)
    {
        try
        {
            var normalizedInstanceId = NormalizeRequiredText(instanceId, nameof(instanceId));
            var normalizedDefinitionId = NormalizeRequiredText(definitionId, nameof(definitionId));
            ContentPackageRules.ValidateLocalId(normalizedInstanceId);
            var definition = worldContent.Buildings.SingleOrDefault(item => item.CanonicalId == normalizedDefinitionId);
            if (definition is null)
            {
                return BuildingPlacementResult.Rejected(
                    normalizedInstanceId,
                    normalizedDefinitionId,
                    position,
                    $"Building definition '{normalizedDefinitionId}' is not active.");
            }

            if (worldSimulation.Buildings.Any(item => item.InstanceId == normalizedInstanceId))
            {
                return BuildingPlacementResult.Rejected(
                    normalizedInstanceId,
                    normalizedDefinitionId,
                    position,
                    $"Building instance '{normalizedInstanceId}' already exists.");
            }

            if (assignedTownId is not null && !towns.Any(item => item.Id == assignedTownId))
                return BuildingPlacementResult.Rejected(normalizedInstanceId, normalizedDefinitionId, position,
                    "The assigned Town does not exist.");

            var isHouse = definition.Tags.Contains("house", StringComparer.Ordinal);
            var acceptsHouseholdOwner = definition.Tags.Any(IsHouseholdBuildingTag);
            if (isHouse && householdId is null || householdId is not null && !acceptsHouseholdOwner ||
                householdId is not null && !society.Checkpoint.Households.Any(item => item.Id == householdId))
                return BuildingPlacementResult.Rejected(normalizedInstanceId, normalizedDefinitionId, position,
                    "A House requires an existing household; only household work buildings may take household ownership.");

            if (definition.Tags.Contains("warehouse", StringComparer.Ordinal) &&
                (assignedTownId is null || worldSimulation.Buildings.Any(building =>
                    building.TownId == assignedTownId && worldContent.Buildings.Any(existing =>
                        existing.CanonicalId == building.DefinitionId &&
                        existing.Tags.Contains("warehouse", StringComparer.Ordinal)))))
                return BuildingPlacementResult.Rejected(normalizedInstanceId, normalizedDefinitionId, position,
                    "A Warehouse must join a Town that does not already have one.");

            if (!CanPlaceBuilding(definition, position, out var placementFailure))
            {
                return BuildingPlacementResult.Rejected(
                    normalizedInstanceId,
                    normalizedDefinitionId,
                    position,
                    placementFailure);
            }

            ApplyInventoryTransition(inventory => ConsumeQuantities(
                inventory,
                definition.BuildCosts,
                $"building:{normalizedInstanceId}",
                householdId ?? constructionOwnerId ?? HouseholdId));
            var placed = new PlacedBuilding(
                normalizedInstanceId,
                definition.CanonicalId,
                position,
                WorldTick,
                assignedTownId,
                householdId);
            worldSimulation = new WorldContentSimulationState(
                worldSimulation.Buildings
                    .Append(placed)
                    .OrderBy(item => item.InstanceId, StringComparer.Ordinal)
                    .ToArray(),
                worldSimulation.ProductionJobs,
                worldSimulation.NextProductionJobSequence,
                worldSimulation.CropBuilds);
            if (assignedTownId is not null)
                AssignBuildingToTown(placed, definition);
            AppendEvent(eventKind, $"{placed.InstanceId}:{placed.DefinitionId}:{position.X},{position.Y}" +
                (householdId is null ? string.Empty : $":household={householdId}"));
            return BuildingPlacementResult.Success(placed);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return BuildingPlacementResult.Rejected(
                instanceId?.Trim() ?? string.Empty,
                definitionId?.Trim() ?? string.Empty,
                position,
                exception.Message);
        }
    }

    public ProductionStartResult StartProduction(
        string recipeId,
        string buildingInstanceId,
        string workerId)
    {
        gate.Wait();
        try
        {
            return StartProductionCore(recipeId, buildingInstanceId, workerId, "recipe_started");
        }
        finally
        {
            gate.Release();
        }
    }

    private ProductionStartResult StartProductionCore(
        string recipeId,
        string buildingInstanceId,
        string workerId,
        string eventKind)
    {
        try
        {
            var normalizedRecipeId = NormalizeRequiredText(recipeId, nameof(recipeId));
            var normalizedBuildingId = NormalizeRequiredText(buildingInstanceId, nameof(buildingInstanceId));
            var normalizedWorkerId = NormalizeRequiredText(workerId, nameof(workerId));
            var recipe = worldContent.Recipes.SingleOrDefault(item => item.CanonicalId == normalizedRecipeId);
            if (recipe is null)
            {
                return ProductionStartResult.Rejected(normalizedRecipeId, "The recipe is not active.");
            }
            if (recipe.Outputs.Any(output => output.ResourceId == "bedding"))
                return ProductionStartResult.Rejected(normalizedRecipeId, "Bedding production was retired with sleep.");

            GridPoint workPosition;
            var isFertileLandBuild = recipe.IsCrop && recipe.WorkstationBuildingId is null;
            PlacedBuilding? placed = null;
            if (isFertileLandBuild)
            {
                if (!WorldBuildSiteRules.TryGetFertileLandPosition(normalizedBuildingId, out workPosition) ||
                    !WorldContentSimulationRules.IsFertileLandPosition(map, workPosition))
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The crop must use a generated fertile-land site.");
                }

                if ((worldSimulation.CropBuilds ?? []).Any(job =>
                        job.State == WorldProductionJobState.Running &&
                        job.BuildingInstanceId == normalizedBuildingId))
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The fertile-land site is already being used.");
                }
            }
            else
            {
                placed = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == normalizedBuildingId);
                if (placed is null)
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The workstation building is not placed.");
                }

                if (recipe.WorkstationBuildingId is not null && recipe.WorkstationBuildingId != placed.DefinitionId)
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The placed building is not a valid workstation for this recipe.");
                }

                var buildingDefinition = worldContent.Buildings.Single(item => item.CanonicalId == placed.DefinitionId);
                var activeJobs = worldSimulation.ProductionJobs.Count(item =>
                    item.BuildingInstanceId == placed.InstanceId && item.State == WorldProductionJobState.Running);
                if (activeJobs >= buildingDefinition.Capacity)
                {
                    return ProductionStartResult.Rejected(normalizedRecipeId, "The workstation has no free production capacity.");
                }

                workPosition = placed.Position;
            }

            var worker = society.Checkpoint.Inhabitants.SingleOrDefault(item => item.Id == normalizedWorkerId);
            if (worker is null || worker.Status != SocietyInhabitantStatus.Active)
            {
                return ProductionStartResult.Rejected(normalizedRecipeId, "The production worker is not active.");
            }

            var workstation = placed is null ? null : worldContent.Buildings.Single(item => item.CanonicalId == placed.DefinitionId);
            if (placed?.HouseholdId is { } workOwner && worker.HouseholdId != workOwner)
                return ProductionStartResult.Rejected(normalizedRecipeId,
                    "Only a member of the building's household can work there.");

            if (workstation?.Tags.Any(tag => tag is "farmhouse" or "blacksmith") == true && placed?.HouseholdId is null)
                return ProductionStartResult.Rejected(normalizedRecipeId,
                    "The household workshop must be claimed before production.");

            var onSiteHouseholdRecipe = placed?.HouseholdId is not null && workstation?.Tags.Any(IsHouseholdBuildingTag) == true;
            if (onSiteHouseholdRecipe && !HasIngredientsAtBuilding(recipe.Inputs, worker.HouseholdId!, placed!.InstanceId))
                return ProductionStartResult.Rejected(normalizedRecipeId,
                    "The household building lacks the required ingredients in its on-site stock.");

            if (!inhabitants.TryGetValue(normalizedWorkerId, out var physical) || physical.Position != workPosition)
            {
                return ProductionStartResult.Rejected(normalizedRecipeId, "The worker must be standing at the build site.");
            }

            var jobId = $"production-{worldSimulation.NextProductionJobSequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)}";
            var completionTick = checked(WorldTick + recipe.DurationTicks);
            IReadOnlyList<string> reservationIds = [];
            ApplyInventoryTransition(inventory =>
            {
                var reserved = ReserveQuantities(
                    inventory,
                    recipe.Inputs,
                    $"{jobId}:input",
                    completionTick,
                    ProductionOwnerFor(placed, normalizedWorkerId),
                    out reservationIds,
                    onSiteHouseholdRecipe ? placed!.InstanceId : null);
                return reserved;
            });

            var job = new WorldProductionJob(
                jobId,
                recipe.CanonicalId,
                normalizedBuildingId,
                normalizedWorkerId,
                WorldTick,
                completionTick,
                WorldProductionJobState.Running,
                reservationIds.ToArray());
            var productionJobs = isFertileLandBuild
                ? worldSimulation.ProductionJobs
                : worldSimulation.ProductionJobs.Append(job).OrderBy(item => item.JobId, StringComparer.Ordinal).ToArray();
            var cropBuilds = isFertileLandBuild
                ? (worldSimulation.CropBuilds ?? []).Append(job).OrderBy(item => item.JobId, StringComparer.Ordinal).ToArray()
                : worldSimulation.CropBuilds;
            worldSimulation = new WorldContentSimulationState(
                worldSimulation.Buildings,
                productionJobs,
                checked(worldSimulation.NextProductionJobSequence + 1),
                cropBuilds);
            AppendEvent(eventKind == "recipe_started" && isFertileLandBuild ? "build_started" : eventKind,
                $"{job.JobId}:{job.RecipeId}:{job.BuildingInstanceId}");
            return ProductionStartResult.Success(job);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return ProductionStartResult.Rejected(recipeId?.Trim() ?? string.Empty, exception.Message);
        }
    }

    public ContentPackageRecord RollbackContent(string packageId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        gate.Wait();
        try
        {
            var manifest = GetContentManifest(packageId);
            var remainingSimulation = WorldContentSimulationRules.RemovePackage(worldSimulation, manifest.PackageDigest);
            if (inhabitants.Values.Any(person => person.Project is { } project &&
                (project.CandidateId.StartsWith($"build:building:{manifest.PackageDigest}/", StringComparison.Ordinal) ||
                 project.CandidateId.StartsWith($"build:recipe:{manifest.PackageDigest}/", StringComparison.Ordinal))))
            {
                throw new InvalidOperationException("Content referenced by settlement projects requires an explicit migration before removal.");
            }
            var record = contentRegistry.Rollback(packageId, WorldTick, reason);
            worldContent = ContentDefinitionApplicator.RemovePackage(worldContent, record.Manifest.PackageDigest);
            worldSimulation = remainingSimulation;
            if (survivalState is not null)
            {
                survivalState = survivalState with
                {
                    Fires = survivalState.Fires.Where(fire =>
                    worldSimulation.Buildings.Any(building => building.InstanceId == fire.BuildingId)).ToArray()
                };
            }
            assetReservations.ReleasePackage(packageId, WorldTick);
            AppendEvent("content_rolled_back", $"{packageId}:{reason.Trim()}");
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    public DeclarativeWorldContentState WorldContent => worldContent;

    private ContentPackageManifest GetContentManifest(string packageId)
    {
        ContentPackageRules.ValidatePackageId(packageId);
        return contentRegistry.ExportState().Packages
            .SingleOrDefault(package => package.Manifest.PackageId == packageId)?.Manifest
            ?? throw new KeyNotFoundException($"Package '{packageId}' has no lifecycle record.");
    }

    private static DeclarativeWorldContentState RebuildWorldContent(ContentRegistryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = new DeclarativeWorldContentState([], []);
        var active = state.Packages.Where(package => package.Lifecycle == ContentPackageLifecycle.Active)
            .ToDictionary(package => package.Manifest.PackageId, StringComparer.Ordinal);
        var resolution = ContentPackageResolver.Resolve(active.Values.Select(package => package.Manifest), active.Keys);
        if (!resolution.IsSuccess)
        {
            throw new InvalidDataException("Active world content has an invalid dependency graph.");
        }
        foreach (var entry in resolution.Lock)
        {
            result = ContentDefinitionPayloadCodec.ApplyPackage(result, active[entry.PackageId].Manifest);
        }

        return result;
    }

    private void ValidateAssetReservationsAgainstActivePackages()
    {
        var expected = new WorldAssetReservationLedger(assetReservations.Policy);
        foreach (var package in contentRegistry.ExportState().Packages
                     .Where(item => item.Lifecycle == ContentPackageLifecycle.Active)
                     .OrderBy(item => item.ActivationTick ?? long.MaxValue)
                     .ThenBy(item => item.Manifest.PackageId, StringComparer.Ordinal))
        {
            var result = expected.TryReservePackage(
                package.Manifest.PackageId,
                package.Manifest.AssetReservations ?? [],
                package.ActivationTick ?? WorldTick);
            if (!result.IsSuccess)
            {
                throw new InvalidDataException(
                    $"Active package '{package.Manifest.PackageId}' cannot be reconstructed in the world asset reservation ledger: {result.Diagnostic}");
            }
        }

        var expectedReservations = expected.ExportState().Reservations;
        var actualReservations = assetReservations.ExportState().Reservations;
        if (!expectedReservations.SequenceEqual(actualReservations))
        {
            throw new InvalidDataException("The world asset reservation ledger does not match active package reservations.");
        }
    }

}
