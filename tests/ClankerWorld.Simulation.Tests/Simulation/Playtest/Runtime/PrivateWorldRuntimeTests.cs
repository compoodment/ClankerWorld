using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Fact]
    public async Task UnavailableMustDoIsNotCompletedByAcceptedIdleAndSurvivesReload()
    {
        using var world = new PrivateWorldRuntime("must-do-illegal-review", _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var receipt = world.SubmitInstruction(new OwnerInstructionRequest("must-eat-with-no-food", "owner:test",
            "founder-ilya", OwnerInstructionKind.MustDo, "eat food"));
        _ = await world.AdvanceOneTickAsync();
        var state = world.ExportState();
        Assert.DoesNotContain(receipt.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.DoesNotContain(state.Events, item => item.Kind == "instruction_applied" && item.Detail.StartsWith(receipt.InstructionId + ":", StringComparison.Ordinal));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        Assert.Contains(restored.ExportState().Instructions!, instruction => instruction.InstructionId == receipt.InstructionId);
        Assert.DoesNotContain(receipt.InstructionId, restored.ExportState().CompletedInstructionIds ?? []);
    }

    [Fact]
    public void PrivateWorldStartsWithAnActiveSettlementInsteadOfAuthoringDrafts()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");

        Assert.Equal(4, runtime.Society.Inhabitants.Count);
        Assert.All(runtime.Society.Inhabitants, inhabitant =>
            Assert.Equal(ClankerWorld.Simulation.Society.SocietyInhabitantStatus.Active, inhabitant.Status));
        Assert.Single(runtime.Society.Households);
        Assert.Equal(4, runtime.Inhabitants.Count);
        Assert.Contains(runtime.Inhabitants, inhabitant => inhabitant.Personality == "curious");
        Assert.Contains(runtime.Inhabitants, inhabitant => inhabitant.Aspiration == "build something lasting");
    }

    [Fact]
    public async Task PrivateWorldCarriesRicherSystemsThroughTheAuthoritativeTickAndCheckpoint()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");

        Assert.Equal("playtest-alpha", runtime.WorldSystems.WorldSeed);
        Assert.Equal(SeasonKind.Spring, runtime.WorldSystems.Climate.Season);
        Assert.Single(runtime.WorldSystems.Factions.Factions);
        Assert.Single(runtime.WorldSystems.Currency.Currencies);
        Assert.Single(runtime.WorldSystems.Culture.Cultures);
        Assert.Single(runtime.WorldSystems.Chunks);

        _ = await runtime.AdvanceOneTickAsync();

        Assert.Equal(runtime.WorldTick, runtime.WorldSystems.WorldTick);
        Assert.Equal(3, runtime.WorldSystems.Ecology.Resources.Count);
        var restoredState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(restoredState);

        Assert.Equal(
            WorldSystemsCodec.Encode(runtime.WorldSystems),
            WorldSystemsCodec.Encode(restored.WorldSystems));
    }

    [Fact]
    public async Task PrivateWorldAdvancesAllFoundersThroughBoundedCognition()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");

        var result = await runtime.AdvanceOneTickAsync();

        Assert.True(result.Advanced);
        Assert.Equal(1, result.WorldTick);
        Assert.Equal(4, result.Decisions.Count);
        Assert.All(result.Decisions, decision => Assert.True(decision.Admission.Accepted));
        Assert.Contains(result.Events, worldEvent => worldEvent.Kind == "inhabitant_moved");
        Assert.All(runtime.Inhabitants, inhabitant => Assert.True(inhabitant.HungerBasisPoints < 6_500));
    }

    [Fact]
    public async Task PrivateWorldCheckpointRoundTripsWithTheSamePopulationAndTick()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        _ = await runtime.AdvanceOneTickAsync();

        var encoded = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
        var restoredState = PrivateWorldRuntimeCodec.Decode(encoded);
        using var restored = PrivateWorldRuntime.Restore(restoredState);

        Assert.Equal(runtime.WorldTick, restored.WorldTick);
        Assert.Equal(
            runtime.Society.Inhabitants.Select(item => item.Id),
            restored.Society.Inhabitants.Select(item => item.Id));
        // Canonical bytes below compare nested collection contents, not array/list identity.
        Assert.Equal(
            encoded,
            PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task PrivateWorldPauseIsAnIdempotentBoundary()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        runtime.Pause();
        runtime.Pause();

        var paused = await runtime.AdvanceOneTickAsync();

        Assert.False(paused.Advanced);
        Assert.Equal("paused", paused.Outcome);
        Assert.Single(runtime.ExportState().Events, worldEvent => worldEvent.Kind == "paused");
    }

    [Fact]
    public async Task PrivateWorldInstructionsAreIdempotentAndReachCognition()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        var request = new OwnerInstructionRequest(
            "instruction-key-1",
            "owner-device:test",
            "founder-rowan",
            OwnerInstructionKind.MustDo,
            "travel to berry patch");

        var first = runtime.SubmitInstruction(request);
        var replay = runtime.SubmitInstruction(request);
        _ = await runtime.AdvanceOneTickAsync();

        Assert.Equal(first, replay);
        Assert.Contains(runtime.ExportState().CompletedInstructionIds!, id => id == first.InstructionId);
        Assert.Contains(
            runtime.ExportState().Events,
            worldEvent => worldEvent.Kind == "instruction_applied" && worldEvent.Detail.Contains(first.InstructionId, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OwnerInstructionKind.MustDo)]
    [InlineData(OwnerInstructionKind.Suggestive)]
    public async Task ProviderFailureDoesNotCompleteAnInstructionOrStopOtherAgents(OwnerInstructionKind kind)
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha", id =>
            id == "founder-rowan" ? new FailingDecisionProvider() : new DeterministicDecisionProvider());
        var instruction = runtime.SubmitInstruction(new OwnerInstructionRequest(
            "outage-instruction", "owner-device:test", "founder-rowan",
            kind, "gather food"));

        var step = await runtime.AdvanceOneTickAsync();

        Assert.True(step.Advanced);
        Assert.Equal(1, runtime.WorldTick);
        Assert.False(runtime.Society.IsPaused);
        Assert.Equal("safe_idle", step.Decisions.Single(item => item.InhabitantId == "founder-rowan")
            .Admission.Intention?.CandidateId);
        Assert.Contains(step.Decisions, item => item.InhabitantId != "founder-rowan" &&
            item.Admission.Intention?.CandidateId != "safe_idle");
        Assert.DoesNotContain(instruction.InstructionId, runtime.ExportState().CompletedInstructionIds ?? []);
        Assert.DoesNotContain(runtime.ExportState().Events, item =>
            item.Kind == "instruction_applied" && item.Detail.Contains(instruction.InstructionId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrivateWorldActivatesStagedContentOnTheNextTickAndCanQuarantineIt()
    {
        // Test unused-content rollback independently of the default gameplay policy.
        using var runtime = new PrivateWorldRuntime("playtest-alpha",
            _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var packageDigest = "sha256:" + new string('d', 64);
        var version = ContentVersion.Parse("1.0.0");
        var building = new BuildingDefinition(
            packageDigest,
            "camp-kitchen",
            version,
            "Camp kitchen",
            1,
            1,
            2,
            [new ContentQuantity("wood", 2)],
            ["camp"]);
        var recipe = new RecipeDefinition(
            packageDigest,
            "berry-stew",
            version,
            "Berry stew",
            [new ContentQuantity("wood", 1)],
            [new ContentQuantity("meal", 1)],
            10,
            building.CanonicalId,
            ["food"]);
        var package = new ContentPackageManifest(
            "camp-recipes",
            version,
            packageDigest,
            [],
            [
                new ContentDefinition(
                    BuildingDefinition.SchemaKind,
                    building.LocalId,
                    building.Version,
                    building.DisplayName,
                    building.PayloadDigest,
                    """{"schema":"building/v1","width":1,"height":1,"capacity":2,"buildCosts":[{"resourceId":"wood","amount":2}],"tags":["camp"]}"""),
                new ContentDefinition(
                    RecipeDefinition.SchemaKind,
                    recipe.LocalId,
                    recipe.Version,
                    recipe.DisplayName,
                    recipe.PayloadDigest,
                    $"{{\"schema\":\"recipe/v1\",\"inputs\":[{{\"resourceId\":\"wood\",\"amount\":1}}],\"outputs\":[{{\"resourceId\":\"meal\",\"amount\":1}}],\"durationTicks\":10,\"workstationBuildingId\":\"{building.CanonicalId}\",\"tags\":[\"food\"]}}")
            ],
            []);
        var resolution = PrivateWorldRuntime.PreviewContent([package], [package.PackageId]);

        runtime.ProposeContent(package);
        runtime.ValidateContent(package.PackageId, resolution);
        runtime.ApproveContent(package.PackageId);
        var staged = runtime.StageContent(package.PackageId);

        Assert.Equal(ContentPackageLifecycle.Staged, staged.Lifecycle);
        Assert.Equal(0, staged.StagedTick);
        Assert.DoesNotContain(runtime.Content.Packages, item => item.Lifecycle == ContentPackageLifecycle.Active);

        _ = await runtime.AdvanceOneTickAsync();

        var active = Assert.Single(runtime.Content.Packages);
        Assert.Equal(ContentPackageLifecycle.Active, active.Lifecycle);
        Assert.Equal(1, active.ActivationTick);
        Assert.Equal(building.CanonicalId, Assert.Single(runtime.WorldContent.Buildings).CanonicalId);
        Assert.Equal(recipe.CanonicalId, Assert.Single(runtime.WorldContent.Recipes).CanonicalId);
        Assert.Contains(runtime.ExportState().Events, item =>
            item.Kind == "content_definitions_activated" && item.Detail.Contains("buildings=1:recipes=1", StringComparison.Ordinal));

        var restoredState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(restoredState);
        var rolledBack = restored.RollbackContent(package.PackageId, "preview mismatch");

        Assert.Equal(ContentPackageLifecycle.Quarantined, rolledBack.Lifecycle);
        Assert.Empty(restored.WorldContent.Buildings);
        Assert.Empty(restored.WorldContent.Recipes);
        Assert.Contains(restored.Content.Events, item => item.Kind == "package_rolled_back");
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "content_rolled_back");
    }

    [Fact]
    public async Task PrivateWorldPlacesAContentBuildingConsumesCostsAndProducesRecipeOutputs()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        var (package, building, recipe) = MaterialPackage(durationTicks: 2);
        Activate(runtime, package);
        _ = await runtime.AdvanceOneTickAsync();

        var occupiedMapPositions = runtime.ExportState().Map.CampObjects.Select(item => item.Position)
            .Concat(runtime.ExportState().Map.Resources.Select(item => item.Position))
            .ToHashSet();
        var worker = runtime.Inhabitants.First(item => !occupiedMapPositions.Contains(item.Position));
        var rejected = runtime.PlaceBuilding("blocked-kitchen", building.CanonicalId, new GridPoint(0, 0));
        Assert.False(rejected.Applied);
        Assert.Equal(48, runtime.Society.Inventory.Lots.Single(item => item.Id == "wood:camp-alpha").Quantity);

        var mountain = runtime.ExportState().Map.Tiles.Single(tile => tile.Terrain == TerrainKind.Mountain).Position;
        var mountainPlacement = runtime.PlaceBuilding("mountain-kitchen", building.CanonicalId, mountain);
        Assert.False(mountainPlacement.Applied);
        Assert.Contains("buildable ground", mountainPlacement.Failure, StringComparison.Ordinal);
        Assert.Equal(48, runtime.Society.Inventory.Lots.Single(item => item.Id == "wood:camp-alpha").Quantity);

        var placement = runtime.PlaceBuilding("camp-kitchen-one", building.CanonicalId, worker.Position);
        Assert.True(placement.Applied, placement.Failure);
        Assert.Single(runtime.WorldSimulation.Buildings);
        Assert.Equal(46, runtime.Society.Inventory.Lots.Single(item => item.Id == "wood:camp-alpha").Quantity);

        var started = runtime.StartProduction(recipe.CanonicalId, placement.InstanceId, worker.InhabitantId);
        Assert.True(started.Applied, started.Failure);
        Assert.Equal(1, runtime.WorldSimulation.ProductionJobs.Count(item => item.State == WorldProductionJobState.Running));

        _ = await runtime.AdvanceOneTickAsync();
        Assert.DoesNotContain(runtime.Society.Inventory.Lots, item => item.ItemKind == "meal");
        _ = await runtime.AdvanceOneTickAsync();

        var completed = Assert.Single(runtime.WorldSimulation.ProductionJobs);
        Assert.Equal(WorldProductionJobState.Completed, completed.State);
        Assert.Equal(1, runtime.Society.Inventory.Lots.Single(item => item.ItemKind == "meal").Quantity);
        Assert.Contains(runtime.ExportState().Events, item => item.Kind == "recipe_completed");

        var restoredState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(restoredState);
        Assert.Equal(
            runtime.WorldSimulation.Buildings,
            restored.WorldSimulation.Buildings);
        var expectedJob = Assert.Single(runtime.WorldSimulation.ProductionJobs);
        var actualJob = Assert.Single(restored.WorldSimulation.ProductionJobs);
        Assert.Equal(expectedJob.JobId, actualJob.JobId);
        Assert.Equal(expectedJob.RecipeId, actualJob.RecipeId);
        Assert.Equal(expectedJob.BuildingInstanceId, actualJob.BuildingInstanceId);
        Assert.Equal(expectedJob.WorkerId, actualJob.WorkerId);
        Assert.Equal(expectedJob.StartedTick, actualJob.StartedTick);
        Assert.Equal(expectedJob.CompletionTick, actualJob.CompletionTick);
        Assert.Equal(expectedJob.State, actualJob.State);
        Assert.Equal(expectedJob.InputReservationIds, actualJob.InputReservationIds);
        Assert.Equal(
            runtime.WorldSimulation.NextProductionJobSequence,
            restored.WorldSimulation.NextProductionJobSequence);
        Assert.Equal(runtime.Society.Inventory.Lots, restored.Society.Inventory.Lots);
    }

    [Fact]
    public async Task InhabitantsChooseBuildForBuildingsAndRecipesWithoutOwnerCommands()
    {
        using var runtime = new PrivateWorldRuntime(
            "playtest-alpha",
            _ => new BuildSelectingProvider());
        var (package, building, recipe) = MaterialPackage(durationTicks: 2);
        Activate(runtime, package);

        var decisions = new List<SocietyCognitionDispatchResult>();
        for (var tick = 0; tick < 100 && !runtime.WorldSimulation.ProductionJobs.Any(job => job.State == WorldProductionJobState.Completed); tick++)
        {
            var result = await runtime.AdvanceOneTickAsync();
            decisions.AddRange(result.Decisions);
        }

        Assert.Contains(
            decisions,
            decision => decision.InhabitantId == "founder-rowan" &&
                decision.Admission.Intention?.CandidateId.StartsWith(
                    $"build:building:{building.CanonicalId}:site:", StringComparison.Ordinal) == true);
        Assert.Contains(runtime.WorldSimulation.Buildings, item => item.DefinitionId == building.CanonicalId);
        Assert.Contains(runtime.ExportState().Events, item => item.Kind == "build_completed");
        Assert.Contains(
            decisions,
            decision => decision.InhabitantId == "founder-rowan" &&
                decision.Admission.Intention?.CandidateId == $"build:recipe:{recipe.CanonicalId}");
        Assert.Contains(runtime.WorldSimulation.ProductionJobs, item =>
            item.RecipeId == recipe.CanonicalId && item.State == WorldProductionJobState.Completed);
        Assert.Contains(runtime.Society.Inventory.Lots, item => item.ItemKind == "meal" && item.Quantity > 0);
    }

    [Fact]
    public async Task InhabitantCanBuildAZeroInputCropOnGeneratedFertileLand()
    {
        using var runtime = new PrivateWorldRuntime(
            "playtest-alpha",
            _ => new BuildSelectingProvider());
        var (package, recipe) = CropPackage();
        Activate(runtime, package);

        var decisions = new List<SocietyCognitionDispatchResult>();
        for (var tick = 0; tick < 60 && !(runtime.WorldSimulation.CropBuilds ?? []).Any(job => job.State == WorldProductionJobState.Completed); tick++)
        {
            var result = await runtime.AdvanceOneTickAsync();
            decisions.AddRange(result.Decisions);
        }

        Assert.Equal(
            new GridPoint(2, 3),
            runtime.ExportState().Map.GetResource(SeededMapGenerator.FertileLandResourceId).Position);
        Assert.Contains(
            decisions,
            decision => decision.InhabitantId == "founder-mira" &&
                decision.Admission.Intention?.CandidateId == $"build:recipe:{recipe.CanonicalId}");
        var build = Assert.Single(
            runtime.WorldSimulation.CropBuilds ?? [],
            item => item.RecipeId == recipe.CanonicalId && item.State == WorldProductionJobState.Completed);
        Assert.Equal(
            WorldBuildSiteRules.FertileLandSiteId(new GridPoint(2, 3)),
            build.BuildingInstanceId);
        Assert.Contains(runtime.ExportState().Events, item => item.Kind == "build_completed");
        Assert.Contains(runtime.Society.Inventory.Lots, item => item.ItemKind == "carrot" && item.Quantity > 0);

        var restoredState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(restoredState);
        Assert.Equal(
            PrivateWorldRuntimeCodec.Encode(runtime.ExportState()),
            PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(runtime.Society.Inventory.Lots, restored.Society.Inventory.Lots);
    }

    [Fact]
    public async Task PrivateWorldRecoversFromTheLiveOccupiedTileStarvationDeadlock()
    {
        using var genesis = new PrivateWorldRuntime("playtest-alpha");
        var state = genesis.ExportState();
        var stuckPositions = new Dictionary<string, GridPoint>(StringComparer.Ordinal)
        {
            ["founder-ilya"] = new GridPoint(4, 1),
            ["founder-mira"] = new GridPoint(2, 1),
            ["founder-rowan"] = new GridPoint(3, 1),
            ["founder-scout"] = new GridPoint(4, 0),
        };
        state = state with
        {
            Inhabitants = state.Inhabitants
                .Select(inhabitant => inhabitant with
                {
                    Position = stuckPositions[inhabitant.InhabitantId],
                    HungerBasisPoints = 0,
                    MoveWaitTicks = 4_500,
                })
                .ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Lots = state.Society.Society.Inventory.Lots
                            .Where(lot => lot.Id != "food:camp-alpha")
                            .ToArray(),
                    },
                },
            },
        };
        var provider = new CountingSelectingProvider(DecisionProviderKind.Deterministic);
        using var runtime = PrivateWorldRuntime.Restore(state, _ => provider);

        for (var tick = 0; tick < 120; tick++)
        {
            _ = await runtime.AdvanceOneTickAsync();
        }

        var recovered = runtime.ExportState();
        Assert.Contains(recovered.Events, item => item.Kind == "food_harvested");
        Assert.Contains(recovered.Events, item => item.Kind == "food_consumed");
        Assert.Contains(recovered.Events, item => item.Kind == "inhabitant_moved" && item.WorldTick > 1);
        Assert.All(recovered.Inhabitants, inhabitant =>
        {
            Assert.True(
                inhabitant.HungerBasisPoints > 0,
                $"{inhabitant.InhabitantId}: " + string.Join(", ", recovered.Events
                    .Where(item => item.Detail.StartsWith(inhabitant.InhabitantId, StringComparison.Ordinal) &&
                        item.Kind is "food_harvested" or "food_consumed" or "harvest_failed")
                    .Select(item => $"{item.WorldTick}:{item.Kind}:{item.Detail}")));
        });
        Assert.Equal(4, recovered.Inhabitants.Select(item => item.Position).Distinct().Count());
        Assert.True(provider.CallCount < 100, $"Expected fewer than 100 cognition calls, got {provider.CallCount}.");
    }

    [Fact]
    public async Task PrivateWorldAcceptsJevIntentionsAndExecutesThemBetweenReevaluations()
    {
        var provider = new CountingSelectingProvider(DecisionProviderKind.Jev);
        using var runtime = new PrivateWorldRuntime("playtest-alpha", _ => provider);

        for (var tick = 0; tick < 10; tick++)
        {
            _ = await runtime.AdvanceOneTickAsync();
        }

        var cognition = runtime.ExportState().Society.Cognition;
        Assert.All(cognition.Runtimes, item =>
            Assert.Equal(DecisionProviderKind.Jev, item.CurrentIntention?.Provider));
        Assert.True(provider.CallCount >= 4);
        Assert.True(provider.CallCount < 40, $"Expected persistent intentions to avoid per-tick Jev calls, got {provider.CallCount}.");
        Assert.Contains(runtime.ExportState().Events, item => item.Kind == "inhabitant_moved");
    }

    [Fact]
    public void PrivateWorldCodecReadsLegacyCheckpointWithoutContentRegistry()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        var legacyState = runtime.ExportState() with { SchemaVersion = 1, Content = null };

        var decoded = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(legacyState));
        using var restored = PrivateWorldRuntime.Restore(decoded);

        Assert.Equal(1, decoded.SchemaVersion);
        Assert.Null(decoded.Content);
        Assert.Empty(restored.Content.Packages);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(
            runtime.ExportState() with { Content = null }));
    }

    private static ContentPackageManifest Package(string id, string version, char digestCharacter) => new(
        id,
        ContentVersion.Parse(version),
        "sha256:" + new string(digestCharacter, 64),
        [],
        [new ContentDefinition(
            "recipe",
            $"{id}-starter",
            ContentVersion.Parse("1.0.0"),
            "Starter recipe",
            "sha256:" + new string(digestCharacter, 64))],
        []);

    private static (ContentPackageManifest Package, BuildingDefinition Building, RecipeDefinition Recipe) MaterialPackage(
        int durationTicks)
    {
        var packageDigest = "sha256:" + new string('e', 64);
        var version = ContentVersion.Parse("1.0.0");
        var building = new BuildingDefinition(
            packageDigest,
            "camp-kitchen",
            version,
            "Camp kitchen",
            1,
            1,
            2,
            [new ContentQuantity("wood", 2)],
            ["camp"]);
        var recipe = new RecipeDefinition(
            packageDigest,
            "berry-meal",
            version,
            "Berry meal",
            [new ContentQuantity("wood", 1)],
            [new ContentQuantity("meal", 1)],
            durationTicks,
            building.CanonicalId,
            ["food"]);
        return (new ContentPackageManifest(
            "material-production",
            version,
            packageDigest,
            [],
            [
                new ContentDefinition(
                    BuildingDefinition.SchemaKind,
                    building.LocalId,
                    building.Version,
                    building.DisplayName,
                    building.PayloadDigest,
                    """{"schema":"building/v1","width":1,"height":1,"capacity":2,"buildCosts":[{"resourceId":"wood","amount":2}],"tags":["camp"]}"""),
                new ContentDefinition(
                    RecipeDefinition.SchemaKind,
                    recipe.LocalId,
                    recipe.Version,
                    recipe.DisplayName,
                    recipe.PayloadDigest,
                    $"{{\"schema\":\"recipe/v1\",\"inputs\":[{{\"resourceId\":\"wood\",\"amount\":1}}],\"outputs\":[{{\"resourceId\":\"meal\",\"amount\":1}}],\"durationTicks\":{durationTicks},\"workstationBuildingId\":\"{building.CanonicalId}\",\"tags\":[\"food\"]}}")
            ],
            []), building, recipe);
    }

    private static (ContentPackageManifest Package, RecipeDefinition Recipe) CropPackage()
    {
        var packageDigest = "sha256:" + new string('f', 64);
        var version = ContentVersion.Parse("1.0.0");
        var recipe = new RecipeDefinition(
            packageDigest,
            "carrots",
            version,
            "Carrots",
            [],
            [new ContentQuantity("carrot", 1)],
            2,
            null,
            ["crop"]);
        return (new ContentPackageManifest(
            "crop-content",
            version,
            packageDigest,
            [],
            [new ContentDefinition(
                RecipeDefinition.SchemaKind,
                recipe.LocalId,
                recipe.Version,
                recipe.DisplayName,
                recipe.PayloadDigest,
                """{"schema":"recipe/v1","inputs":[],"outputs":[{"resourceId":"carrot","amount":1}],"durationTicks":2,"workstationBuildingId":null,"tags":["crop"]}""")],
            []), recipe);
    }

    private static void Activate(PrivateWorldRuntime runtime, ContentPackageManifest package)
    {
        var resolution = PrivateWorldRuntime.PreviewContent([package], [package.PackageId]);
        runtime.ProposeContent(package);
        runtime.ValidateContent(package.PackageId, resolution);
        runtime.ApproveContent(package.PackageId);
        runtime.StageContent(package.PackageId);
    }

    private sealed class BuildSelectingProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;

        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            var selected = request.Observation.Candidates
                .FirstOrDefault(candidate => candidate.Id.StartsWith("build:", StringComparison.Ordinal))
                ?? request.Observation.Candidates
                    .OrderBy(candidate => candidate.DeterministicPriority)
                    .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                    .First();
            var probabilities = request.Observation.Candidates.ToDictionary(
                candidate => candidate.Id,
                candidate => candidate.Id == selected.Id ? 1d : 0d,
                StringComparer.Ordinal);
            return ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId,
                request.Observation.InhabitantId,
                Kind,
                ProviderEpoch,
                request.Observation.RunEpoch,
                request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest,
                selected.Id,
                1d,
                probabilities));
        }
    }

    [Fact]
    public async Task UnchangedIdleDecisionsAreNotRepurchasedEveryThirtyTicksOrOnReload()
    {
        var provider = new CountingSelectingProvider(DecisionProviderKind.Jev, chooseIdle: true);
        using var runtime = new PrivateWorldRuntime("playtest-alpha", _ => provider);
        _ = await runtime.AdvanceOneTickAsync();
        Assert.Equal(4, provider.CallCount);
        for (var tick = 0; tick < 60; tick++)
        {
            _ = await runtime.AdvanceOneTickAsync();
        }

        Assert.Equal(4, provider.CallCount);
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(runtime.ExportState())), _ => provider);
        _ = await restored.AdvanceOneTickAsync();
        Assert.Equal(4, provider.CallCount);

        var hungry = restored.ExportState();
        using var urgent = PrivateWorldRuntime.Restore(hungry with
        {
            Inhabitants = hungry.Inhabitants.Select(inhabitant => inhabitant with { HungerBasisPoints = 2_000 }).ToArray(),
        }, _ => provider);
        _ = await urgent.AdvanceOneTickAsync();
        Assert.Equal(8, provider.CallCount);
    }

    [Fact]
    public async Task StableAdultCanScoutLocalGroundReturnAndRememberVisitedTilesAfterReload()
    {
        using var initial = new PrivateWorldRuntime("exploration-prototype");
        var baseline = initial.ExportState();
        var target = baseline.Inhabitants[0];
        var provider = new ExplorationSelectingProvider(target.InhabitantId);
        using var runtime = PrivateWorldRuntime.Restore(baseline with
        {
            Inhabitants = baseline.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_500,
            }).ToArray(),
        }, _ => provider);

        for (var tick = 0; tick < 75; tick++)
            _ = await runtime.AdvanceOneTickAsync();

        var events = runtime.ExportState().Events;
        Assert.Contains(events, item => item.Kind == "exploration_started" && item.Detail.StartsWith(target.InhabitantId + ":", StringComparison.Ordinal));
        Assert.Contains(events, item => item.Kind == "exploration_discovered" && item.Detail.StartsWith(target.InhabitantId + ":", StringComparison.Ordinal));
        Assert.Contains(events, item => item.Kind == "exploration_completed" && item.Detail.StartsWith(target.InhabitantId + ":", StringComparison.Ordinal));
        var explorer = runtime.Inhabitants.Single(person => person.InhabitantId == target.InhabitantId);
        Assert.Equal(target.Position, explorer.Position);
        Assert.NotEmpty(explorer.Exploration!.VisitedTiles);
        Assert.Empty(explorer.Exploration.OutingPath);
        Assert.True(provider.CallCount < 30, "Exploration should reuse its intention rather than asking the model each step.");

        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(runtime.ExportState())), _ => provider);
        var restoredExploration = restored.Inhabitants.Single(person => person.InhabitantId == target.InhabitantId).Exploration!;
        Assert.Equal(explorer.Exploration.VisitedTiles, restoredExploration.VisitedTiles);
        Assert.Equal(explorer.Exploration.OutingPath, restoredExploration.OutingPath);
        Assert.Equal(explorer.Exploration.LastOutingTick, restoredExploration.LastOutingTick);
    }

    [Fact]
    public async Task UrgentFoodNeedDoesNotOfferCuriosityOuting()
    {
        using var initial = new PrivateWorldRuntime("exploration-hungry");
        var baseline = initial.ExportState();
        var target = baseline.Inhabitants[0];
        var provider = new ExplorationSelectingProvider(target.InhabitantId);
        using var runtime = PrivateWorldRuntime.Restore(baseline with
        {
            Inhabitants = baseline.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = person.InhabitantId == target.InhabitantId ? 2_000 : 9_500,
            }).ToArray(),
        }, _ => provider);

        _ = await runtime.AdvanceOneTickAsync();

        Assert.DoesNotContain("explore", provider.TargetCandidates);
        Assert.DoesNotContain(runtime.ExportState().Events, item => item.Kind == "exploration_started");
    }

    [Fact]
    public async Task ExplorationAfterLegalTravelInterruptionKeepsAValidSaveablePath()
    {
        var provider = new ExplorationSelectingProvider("founder-scout");
        using var world = new PrivateWorldRuntime("interrupted-exploration-repro", _ => provider);
        _ = await world.AdvanceOneTickAsync();
        var initial = world.Inhabitants.Single(person => person.InhabitantId == "founder-scout");
        Assert.NotEmpty(initial.Exploration!.OutingPath);
        world.SubmitInstruction(new OwnerInstructionRequest("interrupt-exploration", "owner:test",
            "founder-scout", OwnerInstructionKind.MustDo, "travel to berry patch"));
        for (var tick = 0; tick < 60; tick++)
        {
            _ = await world.AdvanceOneTickAsync();
            var state = world.ExportState();
            var bytes = PrivateWorldRuntimeCodec.Encode(state);
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
            var scout = state.Inhabitants.Single(person => person.InhabitantId == "founder-scout");
            var path = scout.Exploration!.OutingPath;
            Assert.All(path.Zip(path.Skip(1)), edge => Assert.True(state.Map.CanFootStep(edge.First, edge.Second)));
        }
        Assert.Contains(world.ExportState().Events, item => item.Kind == "exploration_aborted" &&
            item.Detail == "founder-scout:interrupted_movement");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "exploration_completed");
    }

    private sealed class ExplorationSelectingProvider(string targetId) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public int CallCount { get; private set; }
        public IReadOnlyList<string> TargetCandidates { get; private set; } = [];

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var options = request.Observation.Candidates;
            if (request.Observation.InhabitantId == targetId)
                TargetCandidates = options.Select(option => option.Id).ToArray();
            var selected = request.Observation.InhabitantId == targetId
                ? options.FirstOrDefault(option => option.Id == "explore")
                : null;
            selected ??= options.FirstOrDefault(option => option.Id == "safe_idle") ?? options[0];
            return ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1d,
                options.ToDictionary(option => option.Id, option => option.Id == selected.Id ? 1d : 0d,
                    StringComparer.Ordinal)));
        }
    }

    [Fact]
    public void LegacyInhabitantsDoNotAcquireNullDecisionCacheFieldsWhenSaved()
    {
        using var runtime = new PrivateWorldRuntime("playtest-alpha");
        Assert.DoesNotContain("lastDecisionContext", System.Text.Encoding.UTF8.GetString(PrivateWorldRuntimeCodec.Encode(runtime.ExportState())), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CountingSelectingProvider(DecisionProviderKind kind, bool chooseIdle = false) : IDecisionProvider
    {
        private int callCount;

        public DecisionProviderKind Kind => kind;

        public long ProviderEpoch => 1;

        public int CallCount => Volatile.Read(ref callCount);

        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref callCount);
            var selected = request.Observation.Candidates
                .OrderBy(candidate => chooseIdle && candidate.Id == "safe_idle" ? int.MinValue : candidate.DeterministicPriority)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .First();
            var probabilities = request.Observation.Candidates.ToDictionary(
                candidate => candidate.Id,
                candidate => candidate.Id == selected.Id ? 1d : 0d,
                StringComparer.Ordinal);
            return ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId,
                request.Observation.InhabitantId,
                Kind,
                ProviderEpoch,
                request.Observation.RunEpoch,
                request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest,
                selected.Id,
                1d,
                probabilities));
        }
    }

    private sealed class FailingDecisionProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(
            CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("provider unavailable");
    }
}
