using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A durable public work plan, not hidden model reasoning.</summary>
public sealed record SettlementProject(
    string CandidateId,
    string Label,
    long StartedTick,
    string Stage,
    int WorkDone = 0,
    string? Blocker = null,
    string? JobId = null,
    long LastTransitionTick = 0);

public sealed partial class PrivateWorldRuntime
{
    private const int ProjectWorkTicks = 10;
    private const string WaitingForWorkSiteBlocker = "Waiting for a free work site";
    private const int BlockedProjectRetryDelayTicks = 60;
    // The pre-energy-removal settlement package included bedding. Its digest
    // remains a valid provenance marker for three staged map resources.
    internal const string LegacySettlementPackageDigest =
        "sha256:037c1b07a6a88989adfc6fe61fb03e7c625524dfbbb530b3e1b30701f66a21ac";

    private static bool IsCompatibleSavedMap(SeededMap generated, PrivateWorldRuntimeState state)
    {
        if (MapManifestCodec.Digest(state.Map) != state.Map.ManifestDigest)
        {
            return false;
        }
        var baseline = state.Geography is { } savedGeography && state.Map.CampObjects.Count > 0
            ? GeneratedCampMapGenerator.GenerateWithLegacyCamp(savedGeography,
                state.Map.CampObjects.Any(item => item.Id == "bedroll" && item.Kind == "bedroll"))
            : generated;
        if (baseline.ManifestDigest == state.Map.ManifestDigest)
            return true;
        var withoutNaturalDetails = baseline with
        {
            Resources = baseline.Resources.Select(resource => resource with { NaturalObjectKind = null }).ToArray(),
            ManifestDigest = string.Empty,
        };
        withoutNaturalDetails = withoutNaturalDetails with
        {
            ManifestDigest = MapManifestCodec.Digest(withoutNaturalDetails),
        };
        var withoutGeology = baseline with
        {
            Resources = baseline.Resources.Where(resource =>
                !resource.Id.StartsWith("geology-", StringComparison.Ordinal)).ToArray(),
            ManifestDigest = string.Empty,
        };
        withoutGeology = withoutGeology with { ManifestDigest = MapManifestCodec.Digest(withoutGeology) };
        var legacyNaturalDetails = withoutGeology with
        {
            Resources = withoutGeology.Resources.Select(resource => resource with { NaturalObjectKind = null }).ToArray(),
            ManifestDigest = string.Empty,
        };
        legacyNaturalDetails = legacyNaturalDetails with
        {
            ManifestDigest = MapManifestCodec.Digest(legacyNaturalDetails),
        };
        var previousTrees = legacyNaturalDetails with
        {
            Resources = legacyNaturalDetails.Resources.Where(resource =>
                !resource.Id.StartsWith("orchard-", StringComparison.Ordinal)).ToArray(),
            ManifestDigest = string.Empty,
        };
        previousTrees = previousTrees with { ManifestDigest = MapManifestCodec.Digest(previousTrees) };
        var previousVegetation = previousTrees with
        {
            Resources = previousTrees.Resources.Where(resource => !resource.Id.StartsWith("tree-", StringComparison.Ordinal))
                .Select(resource => resource with { TreeKind = null }).ToArray(),
            ManifestDigest = string.Empty,
        };
        previousVegetation = previousVegetation with
        {
            ManifestDigest = MapManifestCodec.Digest(previousVegetation),
        };
        foreach (var candidateBaseline in new[]
                 { baseline, withoutNaturalDetails, withoutGeology, legacyNaturalDetails, previousTrees, previousVegetation })
        {
            if (SavedMapMatchesBaseline(state, candidateBaseline)) return true;
        }
        // Before independent map layers, resource placement and clearing
        // selection used the flattened TerrainKind. Validate that historical
        // generator as a separate immutable lineage, including worlds that
        // were later resaved under a newer checkpoint schema.
        return state.Geography is { } geography &&
            SavedMapMatchesBaseline(state, GeneratedCampMapGenerator.GenerateLegacy(geography,
                state.Map.CampObjects.Any(item => item.Id == "bedroll" && item.Kind == "bedroll")));
    }

    private static bool SavedMapMatchesBaseline(PrivateWorldRuntimeState state, SeededMap baseline)
    {
        if (baseline.ManifestDigest == state.Map.ManifestDigest)
            return true;
        var baseIds = baseline.Resources.Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        var added = state.Map.Resources.Where(resource => !baseIds.Contains(resource.Id)).ToArray();
        // A saved map may add the settlement's staged sites and trees planted
        // on new tiles. Everything else must match deterministic regeneration.
        var planted = added.Where(IsPlantedTree).ToArray();
        var staged = added.Where(resource => !IsPlantedTree(resource)).ToArray();
        if (added.Length == 0 ||
            added.Select(resource => resource.Position).Distinct().Count() != added.Length ||
            added.Any(resource => baseline.CampObjects.Any(item => item.Position == resource.Position) ||
                baseline.Resources.Any(item => item.Position == resource.Position)))
            return false;
        if (staged.Length > 0 && (state.SchemaVersion < 5 || staged.Length > 3 ||
            state.Content?.Packages.Any(package => package.Manifest.PackageId == SettlementContent.PackageId &&
                (package.Manifest.PackageDigest == SettlementContent.Create().PackageDigest ||
                 package.Manifest.PackageDigest == LegacySettlementPackageDigest) &&
                package.ActivationTick is not null) != true ||
            staged.Any(resource => resource.Id != "settlement-" + resource.Kind ||
                resource.Kind is not ("stone" or "fiber" or "seed") ||
                resource.IsRenewable != (resource.Kind is "fiber" or "seed") ||
                !baseline.IsBuildable(resource.Position))))
            return false;
        if (planted.Length > 0 && (state.SchemaVersion < PlantedTreeSchemaVersion ||
            planted.Any(tree => !TreeGrowthRules.IsValidPlantedTree(baseline, tree))))
            return false;
        var original = state.Map with
        {
            Resources = state.Map.Resources.Where(resource => baseIds.Contains(resource.Id)).ToArray(),
        };
        return MapManifestCodec.Digest(original) == baseline.ManifestDigest;
    }

    private void StageSettlementContent()
    {
        var packages = contentRegistry.ExportState().Packages;
        if (packages.Any(package => package.Manifest.PackageId == SettlementContent.PackageId) ||
            !packages.Any(package => package.Manifest.PackageId == StarterContent.PackageId && package.Lifecycle == ContentPackageLifecycle.Active))
        {
            return;
        }
        var manifest = SettlementContent.Create();
        var resolution = ContentPackageResolver.Resolve(packages.Select(package => package.Manifest).Append(manifest), [manifest.PackageId]);
        contentRegistry.Propose(manifest, WorldTick);
        contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
        contentRegistry.Approve(manifest.PackageId, WorldTick);
        contentRegistry.Stage(manifest.PackageId, WorldTick);
        AppendEvent("settlement_content_staged", manifest.PackageId);
    }

    private void StageForestryContent()
    {
        var packages = contentRegistry.ExportState().Packages;
        if (packages.Any(package => package.Manifest.PackageId == ForestryContent.PackageId) ||
            !packages.Any(package => package.Manifest.PackageId == SettlementContent.PackageId && package.Lifecycle == ContentPackageLifecycle.Active))
            return;
        var manifest = ForestryContent.Create();
        var resolution = ContentPackageResolver.Resolve(packages.Select(package => package.Manifest).Append(manifest), [manifest.PackageId]);
        contentRegistry.Propose(manifest, WorldTick);
        contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
        contentRegistry.Approve(manifest.PackageId, WorldTick);
        contentRegistry.Stage(manifest.PackageId, WorldTick);
        AppendEvent("forestry_content_staged", manifest.PackageId);
    }

    private void StageHouseContent()
    {
        var packages = contentRegistry.ExportState().Packages;
        if (packages.Any(package => package.Manifest.PackageId == HouseContent.PackageId) ||
            !packages.Any(package => package.Manifest.PackageId == SettlementContent.PackageId &&
                package.Lifecycle == ContentPackageLifecycle.Active))
            return;
        var manifest = HouseContent.Create();
        var resolution = ContentPackageResolver.Resolve(packages.Select(package => package.Manifest).Append(manifest),
            [manifest.PackageId]);
        contentRegistry.Propose(manifest, WorldTick);
        contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
        contentRegistry.Approve(manifest.PackageId, WorldTick);
        contentRegistry.Stage(manifest.PackageId, WorldTick);
        AppendEvent("house_content_staged", manifest.PackageId);
    }

    private void StageWarehouseContent()
    {
        var packages = contentRegistry.ExportState().Packages;
        if (packages.Any(package => package.Manifest.PackageId == WarehouseContent.PackageId) ||
            !packages.Any(package => package.Manifest.PackageId == HouseContent.PackageId &&
                package.Lifecycle == ContentPackageLifecycle.Active))
            return;
        var manifest = WarehouseContent.Create();
        var resolution = ContentPackageResolver.Resolve(packages.Select(package => package.Manifest).Append(manifest),
            [manifest.PackageId]);
        contentRegistry.Propose(manifest, WorldTick);
        contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
        contentRegistry.Approve(manifest.PackageId, WorldTick);
        contentRegistry.Stage(manifest.PackageId, WorldTick);
        AppendEvent("warehouse_content_staged", manifest.PackageId);
    }

    private void StageFarmContent()
    {
        var packages = contentRegistry.ExportState().Packages;
        if (packages.Any(package => package.Manifest.PackageId == FarmContent.PackageId) ||
            !packages.Any(package => package.Manifest.PackageId == HouseContent.PackageId &&
                package.Lifecycle == ContentPackageLifecycle.Active))
            return;
        var manifest = FarmContent.Create();
        var resolution = ContentPackageResolver.Resolve(packages.Select(package => package.Manifest).Append(manifest),
            [manifest.PackageId]);
        contentRegistry.Propose(manifest, WorldTick);
        contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
        contentRegistry.Approve(manifest.PackageId, WorldTick);
        contentRegistry.Stage(manifest.PackageId, WorldTick);
        AppendEvent("farm_content_staged", manifest.PackageId);
    }

    private void StageBlacksmithContent()
    {
        var packages = contentRegistry.ExportState().Packages;
        if (packages.Any(package => package.Manifest.PackageId == BlacksmithContent.PackageId) ||
            !packages.Any(package => package.Manifest.PackageId == HouseContent.PackageId &&
                package.Lifecycle == ContentPackageLifecycle.Active))
            return;
        var manifest = BlacksmithContent.Create();
        var resolution = ContentPackageResolver.Resolve(packages.Select(package => package.Manifest).Append(manifest),
            [manifest.PackageId]);
        contentRegistry.Propose(manifest, WorldTick);
        contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
        contentRegistry.Approve(manifest.PackageId, WorldTick);
        contentRegistry.Stage(manifest.PackageId, WorldTick);
        AppendEvent("blacksmith_content_staged", manifest.PackageId);
    }

    private void StageHouseCookingContent()
    {
        var packages = contentRegistry.ExportState().Packages;
        if (packages.Any(package => package.Manifest.PackageId == HouseCookingContent.PackageId) ||
            !packages.Any(package => package.Manifest.PackageId == HouseContent.PackageId &&
                package.Lifecycle == ContentPackageLifecycle.Active))
            return;
        var manifest = HouseCookingContent.Create();
        var resolution = ContentPackageResolver.Resolve(packages.Select(package => package.Manifest).Append(manifest),
            [manifest.PackageId]);
        contentRegistry.Propose(manifest, WorldTick);
        contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
        contentRegistry.Approve(manifest.PackageId, WorldTick);
        contentRegistry.Stage(manifest.PackageId, WorldTick);
        AppendEvent("house_cooking_content_staged", manifest.PackageId);
    }

    private void StageSiloContent() =>
        StageBuiltInContent(SiloContent.PackageId, FarmContent.PackageId, SiloContent.Create, "silo_content_staged");

    private void StageHouseCraftingContent() =>
        StageBuiltInContent(HouseCraftingContent.PackageId, HouseContent.PackageId, HouseCraftingContent.Create, "house_crafting_content_staged");

    private void StageTailorContent() =>
        StageBuiltInContent(TailorContent.PackageId, HouseContent.PackageId, TailorContent.Create, "tailor_content_staged");

    /// <summary>
    /// Stages a shipped package once its dependency is active, so worlds that
    /// did not start with it still receive it. It activates on a later tick.
    /// </summary>
    private void StageBuiltInContent(string packageId, string requiredActivePackageId,
        Func<ContentPackageManifest> create, string eventKind)
    {
        var packages = contentRegistry.ExportState().Packages;
        if (packages.Any(package => package.Manifest.PackageId == packageId) ||
            !packages.Any(package => package.Manifest.PackageId == requiredActivePackageId &&
                package.Lifecycle == ContentPackageLifecycle.Active))
            return;
        var manifest = create();
        var resolution = ContentPackageResolver.Resolve(packages.Select(package => package.Manifest).Append(manifest),
            [manifest.PackageId]);
        contentRegistry.Propose(manifest, WorldTick);
        contentRegistry.Validate(manifest.PackageId, resolution, WorldTick);
        contentRegistry.Approve(manifest.PackageId, WorldTick);
        contentRegistry.Stage(manifest.PackageId, WorldTick);
        AppendEvent(eventKind, manifest.PackageId);
    }

    private void AddSettlementResources()
    {
        var occupied = map.CampObjects.Select(item => item.Position).Concat(map.Resources.Select(item => item.Position))
            .Concat(RoadAndBridgeTiles())
            .Concat(worldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)))
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.State == WorldProductionJobState.Running).SelectMany(ExpansionTiles))
            .ToHashSet();
        var additions = new List<MapResource>();
        var townStorage = SettlementStoragePosition;
        var campChunk = worldSystems.Chunks.Single(chunk => chunk.Coordinate ==
            ChunkRules.ToChunkCoordinate(townStorage, chunk.ChunkSize));
        var campOrigin = campChunk.Coordinate.Origin(campChunk.ChunkSize);
        foreach (var kind in new[] { "stone", "fiber", "seed" })
        {
            var id = "settlement-" + kind;
            if (map.Resources.Any(resource => resource.Id == id))
            {
                continue;
            }
            var tile = map.Tiles.FirstOrDefault(tile =>
                tile.Position.X >= Math.Max(campOrigin.X, townStorage.X - 1) &&
                tile.Position.X < Math.Min(campOrigin.X + campChunk.Width, townStorage.X + 6) &&
                tile.Position.Y >= Math.Max(campOrigin.Y, townStorage.Y - 1) &&
                tile.Position.Y < Math.Min(campOrigin.Y + campChunk.Height, townStorage.Y + 5) &&
                map.IsBuildable(tile.Position) && !occupied.Contains(tile.Position));
            if (tile is null)
            {
                AppendEvent("settlement_resource_blocked", kind);
                continue;
            }
            additions.Add(new MapResource(id, kind, tile.Position, kind is "fiber" or "seed"));
            occupied.Add(tile.Position);
        }
        if (additions.Count == 0)
        {
            return;
        }
        map = map with { Resources = map.Resources.Concat(additions).OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray() };
        map = map with { ManifestDigest = MapManifestCodec.Digest(map) };
        worldSystems = worldSystems with
        {
            Ecology = worldSystems.Ecology with
            {
                Resources = worldSystems.Ecology.Resources.Concat(additions.Select(resource => new EcologyResource(
                    resource.Id, resource.Kind, resource.Position, resource.IsRenewable, 16, 16,
                    resource.IsRenewable ? 4 : 0, resource.IsRenewable ? 1 : 0, SeasonKind.Spring,
                    WorldCalendarRules.FromTick(WorldTick, worldSystems.Config).DayIndex + 1, EcologyResourceState.Available)))
                    .OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray(),
            },
            Chunks = worldSystems.Chunks.Select(chunk => chunk.Coordinate != campChunk.Coordinate
                ? chunk
                : ChunkManifestCodec.WithDigest(chunk with
                {
                    Resources = map.Resources.Where(resource =>
                            resource.Position.X >= campOrigin.X && resource.Position.X < campOrigin.X + chunk.Width &&
                            resource.Position.Y >= campOrigin.Y && resource.Position.Y < campOrigin.Y + chunk.Height)
                        .Select(resource => new ChunkResourceMetadata(
                            resource.Id, resource.Kind,
                            new GridPoint(resource.Position.X - campOrigin.X,
                                resource.Position.Y - campOrigin.Y),
                            resource.IsRenewable)).ToArray(),
                })).ToArray(),
        };
        SyncEcologyResourceStates();
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("settlement_resources_added", string.Join(',', additions.Select(resource => resource.Kind)));
    }

    private static void ValidateProject(SettlementProject project, long worldTick)
    {
        if (string.IsNullOrWhiteSpace(project.CandidateId) || project.CandidateId.Length > 512 ||
            !TownConstructionCandidateIds.TryParse(project.CandidateId, out _) ||
            string.IsNullOrWhiteSpace(project.Label) || project.Label.Length > 256 ||
            project.StartedTick < 0 || project.StartedTick > worldTick ||
            project.LastTransitionTick < project.StartedTick || project.LastTransitionTick > worldTick ||
            project.WorkDone is < 0 or > ProjectWorkTicks ||
            project.Stage is not ("acquiring" or "gathering" or "delivering" or "travelling" or "working" or "waiting" or "blocked" or "paused" or "completed" or "cancelled"))
        {
            throw new InvalidDataException("The saved settlement project is invalid.");
        }
    }

    private bool CanContinueProject(PlaytestInhabitantState state) =>
        AdultResident(state.InhabitantId) &&
        state.Project is { Stage: not ("completed" or "cancelled") } project &&
        !(CarryingRoom(state.InhabitantId) <= 1 && PersonalGoodsForStorage(state.InhabitantId) is not null) &&
        (project.Stage != "blocked" || WorldTick - project.LastTransitionTick < BlockedProjectRetryDelayTicks) &&
        !NeedsUrgentFood(state) &&
        !HasTradeResponse(state.InhabitantId) &&
        !HasCouncilDecision(state.InhabitantId) &&
        !HasFamilyDecision(state.InhabitantId) &&
        !HasParenthoodDecision(state.InhabitantId) &&
        !HasDependentCareDecision(state.InhabitantId) &&
        !HasLearningDecision(state.InhabitantId) &&
        !inhabitants.Keys.Any(other => TradeOpportunity(state.InhabitantId, other) is not null) &&
        (!NeedsUrgentWarmth(state) || IsProtectiveProject(state.Project)) &&
        PendingInstructionFor(state.InhabitantId) is null;

    private void BeginProject(string inhabitantId, PlaytestInhabitantState state, string candidateId)
    {
        if (!TownConstructionCandidateIds.TryParse(candidateId, out var selected))
            return;

        if (state.Project is { Stage: not ("completed" or "cancelled") } existing &&
            TownConstructionCandidateIds.TryParse(existing.CandidateId, out var current) &&
            current.IsBuilding == selected.IsBuilding && current.DefinitionId == selected.DefinitionId)
        {
            if (existing.CandidateId != candidateId)
            {
                state = state with
                {
                    Project = existing with
                    {
                        CandidateId = candidateId,
                        Stage = "acquiring",
                        Blocker = null,
                        LastTransitionTick = WorldTick,
                    },
                };
                inhabitants[inhabitantId] = state;
                AppendEvent("project_site_reselected", $"{inhabitantId}:{selected.DefinitionId}");
            }
            else if (existing.Stage == "blocked")
            {
                state = state with { Project = existing with { LastTransitionTick = WorldTick } };
                inhabitants[inhabitantId] = state;
            }
            ContinueProject(inhabitantId, state);
            return;
        }

        var definitionId = selected.DefinitionId;
        // Two members can choose in the same tick; the household still plans one of each kind.
        if (selected.IsBuilding &&
            worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == definitionId) is { } planned &&
            HouseholdBuildingKind(planned) is { } plannedKind &&
            society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId is { } planningHousehold &&
            HouseholdBuildingProjectInProgress(planningHousehold, plannedKind))
        {
            AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:household_plan_in_progress");
            return;
        }
        var label = selected.IsBuilding
            ? worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == definitionId)?.DisplayName
            : worldContent.Recipes.FirstOrDefault(item => item.CanonicalId == definitionId)?.DisplayName;
        if (label is null)
            return;
        state = state with { Project = new SettlementProject(candidateId, label, WorldTick, "acquiring", LastTransitionTick: WorldTick) };
        inhabitants[inhabitantId] = state;
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("project_chosen", $"{inhabitantId}:{candidateId}");
        ContinueProject(inhabitantId, state);
    }

    private void ContinueProject(string inhabitantId, PlaytestInhabitantState state)
    {
        var project = state.Project!;
        if (project.JobId is not null)
        {
            var job = worldSimulation.ProductionJobs.Concat(worldSimulation.CropBuilds ?? [])
                .FirstOrDefault(item => item.JobId == project.JobId);
            SetProject(inhabitantId, project with
            {
                Stage = job?.State switch { WorldProductionJobState.Running => "waiting", WorldProductionJobState.Completed => "completed", _ => "cancelled" },
                Blocker = job is null || job.State == WorldProductionJobState.Cancelled ? "Production was removed or cancelled" : null,
            });
            return;
        }

        if (!TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection))
        {
            SetProject(inhabitantId, project with { Stage = "cancelled", Blocker = "The construction choice is invalid" });
            return;
        }
        var definitionId = selection.DefinitionId;
        var building = selection.IsBuilding ? worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == definitionId) : null;
        var recipe = selection.IsBuilding ? null : worldContent.Recipes.FirstOrDefault(item => item.CanonicalId == definitionId);
        if (building is null && recipe is null)
        {
            SetProject(inhabitantId, project with { Stage = "cancelled", Blocker = "Content is no longer active" });
            return;
        }
        if (building?.Tags.Contains("house", StringComparer.Ordinal) == true)
        {
            var householdId = society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId;
            if (householdId is null)
            {
                SetProject(inhabitantId, project with { Stage = "cancelled", Blocker = "A household is required to own a House." });
                return;
            }
            if (HouseForHousehold(householdId) is not null)
            {
                SetProject(inhabitantId, project with { Stage = "cancelled", Blocker = "This household already has a House." });
                return;
            }
        }
        if (building is not null && HouseholdBuildingKind(building) is { } kind && kind != "house")
        {
            var householdId = society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId;
            if (householdId is null)
            {
                SetProject(inhabitantId, project with { Stage = "cancelled", Blocker = "A household is required to hold this building." });
                return;
            }
            if (HouseholdBuildingWithTag(householdId, kind) is not null)
            {
                SetProject(inhabitantId, project with { Stage = "cancelled", Blocker = $"This household already holds a {building.DisplayName}." });
                return;
            }
            if (kind == "silo" && FarmhouseForHousehold(householdId) is null)
            {
                SetProject(inhabitantId, project with { Stage = "cancelled", Blocker = "Only the household holding a Farmhouse builds a Silo." });
                return;
            }
        }
        if (building?.Tags.Contains("warehouse", StringComparer.Ordinal) == true &&
            (TownForResident(inhabitantId) is not { } townId ||
             worldSimulation.Buildings.Any(placed => placed.TownId == townId &&
                 worldContent.Buildings.Any(definition => definition.CanonicalId == placed.DefinitionId &&
                     definition.Tags.Contains("warehouse", StringComparer.Ordinal)))))
        {
            SetProject(inhabitantId, project with
            {
                Stage = "cancelled",
                Blocker = "This Town already has a Warehouse, or the builder is no longer a resident.",
            });
            return;
        }
        var inputs = building?.BuildCosts ?? recipe!.Inputs;
        PlacedBuilding? recipeBuilding = null;
        if (recipe is not null)
        {
            if (!TryFindRecipeSite(recipe, out var recipeSite, out _, inhabitantId))
            {
                SetProject(inhabitantId, project with { Stage = "blocked", Blocker = WaitingForWorkSiteBlocker });
                return;
            }
            recipeBuilding = worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == recipeSite);
        }
        var constructionOwner = recipe is not null ? ProductionOwnerFor(recipeBuilding, inhabitantId)
            : BuildingConstructionOwner(inhabitantId, building!);
        if (recipe is not null && recipeBuilding?.HouseholdId is not null &&
            worldContent.Buildings.Any(definition => definition.CanonicalId == recipeBuilding.DefinitionId &&
                definition.Tags.Any(IsHouseholdBuildingTag)) &&
            !HasIngredientsAtBuilding(recipe.Inputs, constructionOwner, recipeBuilding.InstanceId))
        {
            SetProject(inhabitantId, project with
            {
                Stage = "blocked",
                Blocker =
                $"Waiting for {MissingWorkstationIngredients(recipe, constructionOwner, recipeBuilding.InstanceId)} at this household building"
            });
            return;
        }
        var missing = inputs.FirstOrDefault(input => !HasAvailableQuantities([input], constructionOwner));
        if (missing.Amount > 0)
        {
            AcquireProjectInput(inhabitantId, state, missing, constructionOwner);
            return;
        }
        if (survivalState is not null && !HasCarriedItem(inhabitantId, "tool") && SharedItem("tool", inhabitantId) is not null)
        {
            CollectEquipment(inhabitantId, state, "tool");
            return;
        }

        GridPoint position;
        if (building is not null)
        {
            var layout = CreateTownLayoutContext(inhabitantId, selection.SitePosition, building);
            if (selection.SitePosition is { } selectedSite)
            {
                if (!TownLayoutService.TryEvaluateConstructionSite(layout, building, selectedSite, out _))
                {
                    const string blocker = "The selected site is no longer legal; fresh ranked choices return after 60 ticks.";
                    var newlyRejected = project.Stage != "blocked" || project.Blocker != blocker;
                    SetProject(inhabitantId, project with
                    {
                        Stage = "blocked",
                        Blocker = blocker,
                    });
                    if (newlyRejected)
                    {
                        AppendEvent("town_layout_site_rejected",
                            $"{TownForResident(inhabitantId) ?? "none"}|{inhabitantId}|{building.CanonicalId}|{selectedSite.X}|{selectedSite.Y}|site_unavailable");
                    }
                    return;
                }
                position = selectedSite;
            }
            else if (TownLayoutService.RankConstructionSites(layout, building) is { Count: > 0 } rankedSites)
            {
                // Older saved projects accepted a building before the chooser
                // included a site. Preserve them by assigning the current top
                // legal option once, then persist that exact choice.
                project = project with { CandidateId = TownConstructionCandidateIds.Building(building.CanonicalId, rankedSites[0].Position) };
                SetProject(inhabitantId, project);
                position = rankedSites[0].Position;
            }
            else
            {
                SetProject(inhabitantId, project with { Stage = "blocked", Blocker = "No legal Town construction site is currently available." });
                return;
            }
        }
        else if (!TryFindRecipeSite(recipe!, out _, out position, inhabitantId))
        {
            SetProject(inhabitantId, project with { Stage = "blocked", Blocker = WaitingForWorkSiteBlocker });
            return;
        }
        if (state.Position != position)
        {
            SetProject(inhabitantId, project with { Stage = "travelling", Blocker = null });
            MoveToward(inhabitantId, inhabitants[inhabitantId], position, "project");
            return;
        }
        if (project.WorkDone < ProjectWorkTicks)
        {
            if (!SettlementIllnessRules.AllowsWork(inhabitantId, WorldTick,
                    state.Survival?.IllnessBasisPoints ?? 0))
            {
                SetProject(inhabitantId, project with { Stage = "working", Blocker = null });
                return;
            }
            var specialist = building is not null ? UseTool(inhabitantId, ToolKind.Hammer)
                : recipe?.IsCrop == true ? UseTool(inhabitantId, ToolKind.Hoe) : null;
            var work = (specialist?.WorkQuantity ?? (HasCarriedItem(inhabitantId, "tool") ? 2 : 1)) + ProjectPracticeBonus(state, project);
            SetProject(inhabitantId, project with { Stage = "working", WorkDone = Math.Min(ProjectWorkTicks, project.WorkDone + work), Blocker = null });
            return;
        }
        ApplyBuildDecision(inhabitantId, state, project.CandidateId);
        if (building is not null && worldSimulation.Buildings.Any(item => item.InstanceId == BuildInstanceId(inhabitantId, building)))
        {
            SetProject(inhabitantId, project with { Stage = "completed", Blocker = null });
        }
        else if (recipe is not null)
        {
            var job = worldSimulation.ProductionJobs.Concat(worldSimulation.CropBuilds ?? [])
                .FirstOrDefault(item => item.WorkerId == inhabitantId && item.StartedTick == WorldTick && item.RecipeId == definitionId);
            if (job is not null)
            {
                SetProject(inhabitantId, project with { Stage = "waiting", JobId = job.JobId, Blocker = null });
            }
            else
            {
                SetProject(inhabitantId, project with { Stage = "blocked", Blocker = "Production could not start; waiting to retry" });
            }
        }
    }

    /// <summary>
    /// Whether someone else is already queued for the kind of site this recipe
    /// needs: fertile land for crops, or the recipe's workstation design. They
    /// keep their turn instead of losing it to new starts every time it frees.
    /// </summary>
    private bool AnotherAgentWaitsForWorkSite(string actor, RecipeDefinition recipe) =>
        inhabitants.Values.Any(other => other.InhabitantId != actor &&
            other.Project is { Stage: "blocked", Blocker: WaitingForWorkSiteBlocker } waiting &&
            TownConstructionCandidateIds.TryParse(waiting.CandidateId, out var selection) && !selection.IsBuilding &&
            worldContent.Recipes.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId) is { } queued &&
            (queued.IsCrop ? recipe.IsCrop : !recipe.IsCrop && queued.WorkstationBuildingId == recipe.WorkstationBuildingId));

    private void AcquireProjectInput(string inhabitantId, PlaytestInhabitantState state,
        ContentQuantity input, string constructionOwner)
    {
        var project = state.Project!;
        var carried = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.OwnerId == inhabitantId &&
            lot.ItemKind == input.ResourceId && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0);
        if (carried is not null && constructionOwner != inhabitantId)
        {
            var house = society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId == constructionOwner
                ? HouseForHousehold(constructionOwner) : null;
            var store = house?.Position ?? SettlementStoragePosition;
            var interactionRange = house is null ? ResourceInteractionRange : 0;
            SetProject(inhabitantId, project with { Stage = "delivering", Blocker = $"Taking {input.ResourceId} to household storage" });
            if (!IsWithinInteractionRange(state.Position, store, interactionRange))
            {
                MoveToward(inhabitantId, inhabitants[inhabitantId], store, "deliver", interactionRange);
                return;
            }
            var deliveryQuantity = Math.Min(input.Amount, AvailableLotQuantity(carried));
            if (house is not null) deliveryQuantity = Math.Min(deliveryQuantity, StorageRoom(house.InstanceId));
            if (deliveryQuantity == 0)
            {
                SetProject(inhabitantId, project with { Stage = "blocked", Blocker = "House storage is full; expand it before delivering more." });
                return;
            }
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"project-delivery:{WorldTick}:{inhabitantId}", inhabitantId, constructionOwner, carried.Id,
                deliveryQuantity, "project_contribution", house?.InstanceId));
            AppendEvent("project_material_delivered", $"{inhabitantId}:{input.ResourceId}");
            return;
        }

        if (WarehouseForResident(inhabitantId) is { } warehouse &&
            society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
                lot.OwnerId == warehouse.TownId && lot.StorageBuildingId == warehouse.InstanceId &&
                lot.ItemKind == input.ResourceId && AvailableLotQuantity(lot) > 0) is { } communal)
        {
            SetProject(inhabitantId, project with { Stage = "gathering", Blocker = $"Collecting {input.ResourceId} from the Town Warehouse" });
            if (state.Position != warehouse.Position)
            {
                MoveToward(inhabitantId, inhabitants[inhabitantId], warehouse.Position, "warehouse_materials", 0);
                return;
            }
            var quantity = Math.Min(WarehouseLoadQuantity,
                Math.Min(input.Amount, AvailableLotQuantity(communal)));
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"warehouse-pickup:{WorldTick}:{inhabitantId}", warehouse.TownId!, inhabitantId,
                communal.Id, quantity, "town_resource_collected"));
            AppendEvent("town_resource_collected", $"{inhabitantId}:{communal.Id}:{quantity}:{warehouse.InstanceId}");
            return;
        }

        if (CollectGatheringTool(inhabitantId, state, input.ResourceId)) return;
        var source = MaterialSource(input.ResourceId, inhabitantId);
        if (source is null)
        {
            SetProject(inhabitantId, project with { Stage = "blocked", Blocker = $"No available source of {input.ResourceId}" });
            return;
        }
        SetProject(inhabitantId, project with { Stage = "gathering", Blocker = $"Need {input.Amount} {input.ResourceId}" });
        GatherProjectMaterial(inhabitantId, state, input.ResourceId, source);
    }

    private void GatherProjectMaterial(string inhabitantId, PlaytestInhabitantState state, string itemKind, MapResource source)
    {
        if (!IsWithinInteractionRange(state.Position, source.Position, ResourceInteractionRange))
        {
            MoveToward(inhabitantId, inhabitants[inhabitantId], source.Position, "materials", ResourceInteractionRange);
            return;
        }
        var miningTier = ToolCapabilities.RequiredMiningTier(itemKind);
        if (FoodItems.IsEdible(itemKind))
        {
            HarvestFoodAtSource(inhabitantId, state, source);
            return;
        }
        var neededTool = itemKind == "wood" && source.TreeKind is not null ? ToolKind.Axe : ToolKind.Pickaxe;
        var requiresTool = miningTier > 0 || itemKind == "wood" && source.TreeKind is not null;
        if (requiresTool && CarriedTool(inhabitantId, neededTool, Math.Max(1, miningTier)) is null)
        {
            AppendEvent("material_gathering_blocked", $"{inhabitantId}:{itemKind}:required_tool_tier_{Math.Max(1, miningTier)}");
            return;
        }
        if (CarryingRoom(inhabitantId) <= (TreeGrowthRules.IsWoodTree(source.TreeKind) ? TreeGrowthRules.TreeSeedsPerFelledTree : 0))
        {
            AppendEvent("carrying_full", inhabitantId);
            return;
        }
        var ecology = worldSystems.Ecology.GetResource(source.Id);
        var harvest = EcologyRules.Harvest(ecology, 1);
        if (!harvest.IsValid || harvest.Resource is null)
        {
            return;
        }
        var harvested = source.TreeKind is not null && harvest.Resource.Quantity == 0 && source.IsRenewable
            ? harvest.Resource with
            {
                NextRegenerationDay = WorldCalendarRules.FromTick(WorldTick, worldSystems.Config).DayIndex +
                    harvest.Resource.RegenerationIntervalDays,
            }
            : harvest.Resource;
        worldSystems = worldSystems with
        {
            Ecology = worldSystems.Ecology with
            {
                Resources = worldSystems.Ecology.Resources.Select(resource => resource.Id == source.Id ? harvested : resource).ToArray(),
            },
        };
        SyncEcologyResourceStates();
        var tool = requiresTool ? UseTool(inhabitantId, neededTool, Math.Max(1, miningTier))
            : itemKind == "wood" ? UseTool(inhabitantId, ToolKind.Axe) : null;
        var seedRoom = TreeGrowthRules.IsWoodTree(source.TreeKind) && harvested.Quantity == 0 ? TreeGrowthRules.TreeSeedsPerFelledTree : 0;
        var quantity = Math.Min(CarryingRoom(inhabitantId) - seedRoom, tool?.WorkQuantity ?? 4);
        ApplyInventoryTransition(inventory => InventoryFixture.AddLot(inventory, $"material:{WorldTick}:{inhabitantId}",
            itemKind, inhabitantId, quantity, WorldTick));
        AppendEvent("material_gathered", $"{inhabitantId}:{itemKind}:{quantity}");
        if (source.TreeKind is not null)
            AppendEvent("tree_harvested", $"{inhabitantId}:{source.Id}:{source.TreeKind}:stump");
        if (TreeGrowthRules.IsWoodTree(source.TreeKind) && harvested.Quantity == 0 &&
            TreeGrowthRules.TreeSeedsPerFelledTree > 0 && CarryingRoom(inhabitantId) > 0)
        {
            // A felled tree also gives a seed that can replant a stump or
            // start a new tree elsewhere.
            ApplyInventoryTransition(inventory => InventoryFixture.AddLot(inventory,
                $"tree-seed:{WorldTick}:{inhabitantId}", TreeGrowthRules.TreeSeedItem, inhabitantId,
                TreeGrowthRules.TreeSeedsPerFelledTree, WorldTick));
            AppendEvent("tree_seed_collected", $"{inhabitantId}:{source.Id}:{TreeGrowthRules.TreeSeedsPerFelledTree}");
        }
    }

    private IEnumerable<(string Requester, ContentQuantity Input, string OwnerId)> ProjectRequests(string helperId)
    {
        foreach (var person in inhabitants.Values.OrderBy(person => person.InhabitantId, StringComparer.Ordinal))
        {
            if (person.InhabitantId == helperId ||
                person.Project is not { Stage: not ("completed" or "cancelled" or "waiting") } project ||
                !TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection))
            {
                continue;
            }
            var building = selection.IsBuilding ? worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId) : null;
            if (selection.IsBuilding && building is null) continue;
            var inputs = selection.IsBuilding
                ? building!.BuildCosts
                : worldContent.Recipes.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId)?.Inputs;
            var constructionOwner = selection.IsBuilding
                ? BuildingConstructionOwner(person.InhabitantId, building!)
                : worldContent.Recipes.Any(item =>
                    item.CanonicalId == selection.DefinitionId &&
                    (item.Tags.Contains("grain", StringComparer.Ordinal) ||
                     item.WorkstationBuildingId is { } workstationId && worldContent.Buildings.Any(definition =>
                         definition.CanonicalId == workstationId &&
                         definition.Tags.Any(IsHouseholdBuildingTag))))
                ? HouseholdFor(person.InhabitantId) :
                    society.Checkpoint.GetInhabitant(person.InhabitantId).HouseholdId is null
                        ? person.InhabitantId : HouseholdId;
            foreach (var input in inputs ?? [])
            {
                if (!HasAvailableQuantities([input], constructionOwner))
                {
                    yield return (person.InhabitantId, input, constructionOwner);
                }
            }
        }
    }

    private MapResource? MaterialSource(string itemKind, string actor) => map.Resources
        .Where(resource =>
            (resource.Kind == itemKind || (itemKind == "wood" && resource.Kind == "construction")) &&
            (itemKind != "wood" || resource.TreeKind is null || CarriedTool(actor, ToolKind.Axe) is not null ||
                SharedTool(actor, ToolKind.Axe) is not null) &&
            (ToolCapabilities.RequiredMiningTier(itemKind) == 0 ||
                CarriedTool(actor, ToolKind.Pickaxe, ToolCapabilities.RequiredMiningTier(itemKind)) is not null ||
                SharedTool(actor, ToolKind.Pickaxe, ToolCapabilities.RequiredMiningTier(itemKind)) is not null) &&
            resources.GetValueOrDefault(resource.Id) == ResourceState.Available &&
            map.IsReachableOnFoot(inhabitants[actor].Position, resource.Position))
        .OrderBy(resource => map.FootDistance(inhabitants[actor].Position, resource.Position))
        .ThenBy(resource => resource.Id, StringComparer.Ordinal)
        .FirstOrDefault(resource => IsWithinInteractionRange(inhabitants[actor].Position, resource.Position, ResourceInteractionRange) ||
            FindUnoccupiedRoute(actor, inhabitants[actor].Position, resource.Position, ResourceInteractionRange).Count > 0);

    private bool CanAcquireProjectInputs(IReadOnlyList<ContentQuantity> inputs, string? ownerId = null,
        string? residentId = null) => inputs.All(input =>
    {
        var stored = society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == input.ResourceId &&
                (lot.OwnerId == (ownerId ?? HouseholdId) || inhabitants.ContainsKey(lot.OwnerId) &&
                    (ownerId is null || HouseholdFor(lot.OwnerId) == ownerId)))
            .Sum(lot => (long)AvailableLotQuantity(lot));
        var harvestable = worldSystems.Ecology.Resources.Where(resource =>
                resource.State == EcologyResourceState.Available &&
                (resource.Kind == input.ResourceId || (input.ResourceId == "wood" && resource.Kind == "construction")) &&
                map.IsReachableFromCampOnFoot(resource.Position))
            .Sum(resource => (long)resource.Quantity * 4);
        var warehouse = residentId is null ? null : WarehouseForResident(residentId);
        var communal = warehouse is null ? 0 : society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.OwnerId == warehouse.TownId && lot.StorageBuildingId == warehouse.InstanceId &&
                lot.ItemKind == input.ResourceId)
            .Sum(lot => (long)AvailableLotQuantity(lot));
        return stored + harvestable + communal >= input.Amount;
    });

    private void AddProjectAssistanceCandidates(List<CognitionCandidate> candidates, string helperId)
    {
        foreach (var request in ProjectRequests(helperId).DistinctBy(request => request.Input.ResourceId))
        {
            var itemKind = request.Input.ResourceId;
            if (MaterialSource(itemKind, helperId) is not null || society.Checkpoint.Inventory.Lots.Any(lot =>
                    lot.OwnerId == helperId && lot.ItemKind == itemKind && lot.DeliveryBuildingId is null &&
                    AvailableLotQuantity(lot) > 0))
            {
                candidates.Add(new CognitionCandidate("assist:" + itemKind,
                    $"Help {society.Checkpoint.GetInhabitant(request.Requester).Name}: gather and share {itemKind} for their project.", 15));
            }
        }
    }

    private void AssistProject(string helperId, PlaytestInhabitantState state, string itemKind)
    {
        var request = ProjectRequests(helperId).FirstOrDefault(request => request.Input.ResourceId == itemKind);
        if (request.Requester is null)
        {
            return;
        }
        // A load already on its way into a household building is not spare.
        var carried = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.OwnerId == helperId &&
            lot.ItemKind == itemKind && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0);
        if (carried is null)
        {
            if (MaterialSource(itemKind, helperId) is { } source)
            {
                GatherProjectMaterial(helperId, state, itemKind, source);
            }
            return;
        }
        var house = HouseForHousehold(request.OwnerId);
        // A helper may deliver supplies without gaining access to the recipient's stock.
        // Without a House, hand them to the requester to keep them physically carried.
        var recipient = house is null ? request.Requester : request.OwnerId;
        var store = house?.Position ?? inhabitants[request.Requester].Position;
        var interactionRange = society.Checkpoint.GetInhabitant(helperId).HouseholdId == request.OwnerId ? 0 : ResourceInteractionRange;
        if (!IsWithinInteractionRange(state.Position, store, interactionRange))
        {
            MoveToward(helperId, state, store, "share_materials", interactionRange);
            return;
        }
        var quantity = Math.Min(request.Input.Amount, AvailableLotQuantity(carried));
        if (house is not null) quantity = Math.Min(quantity, StorageRoom(house.InstanceId));
        if (quantity == 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"project-share:{WorldTick}:{helperId}",
            helperId, recipient, carried.Id, quantity, "project_request_fulfilled",
            house?.InstanceId));
        IncreaseTrust(request.Requester, helperId, 2, "material_help");
        var memoryId = $"project-gratitude:{request.Requester}:{helperId}";
        if (!society.Checkpoint.Memories.Any(memory => memory.Id == memoryId))
        {
            society.Apply(checkpoint => SocietyFixture.RecordSocialMemory(checkpoint, new SocietySocialMemory(
                memoryId, request.Requester, helperId,
                $"Grateful for {society.Checkpoint.GetInhabitant(helperId).Name}'s help with project materials.", "public", WorldTick)));
        }
        AppendEvent("project_request_fulfilled", $"{helperId}:{request.Requester}:{itemKind}:{quantity}");
    }

    private int AvailableLotQuantity(InventoryLot lot) => lot.FreshnessBasisPoints == 0 || lot.ConditionBasisPoints == 0 ? 0 : lot.Quantity - society.Checkpoint.Inventory.Reservations
        .Where(reservation => reservation.LotId == lot.Id && reservation.State is InventoryReservationState.Reserved or
            InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed).Sum(reservation => reservation.Quantity);

    private void SetProject(string inhabitantId, SettlementProject project)
    {
        var state = inhabitants[inhabitantId];
        if (state.Project?.Stage != project.Stage || state.Project?.Blocker != project.Blocker)
        {
            project = project with { LastTransitionTick = WorldTick };
            AppendEvent("project_progress", $"{inhabitantId}:{project.Stage}:{project.Blocker ?? project.Label}");
        }
        inhabitants[inhabitantId] = state with { Project = project };
    }
}
