using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Fact]
    public async Task NewbornKeepsInitiatingParentsModelAcrossSaveAndUsesItOnlyAfterInfancy()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-child-model-birth-");
        try
        {
            var state = await PreparedState();
            var initiatorId = state.Inhabitants[0].InhabitantId;
            var partnerId = state.Inhabitants[1].InhabitantId;
            var callCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            IDecisionProvider ProviderFor(string id) => id == initiatorId
                ? new ParentProvider("parent_propose:")
                : id == partnerId
                    ? new ParentProvider("parent_accept:")
                    : new CountingProvider(id, callCounts);

            var providerDirectory = Path.Combine(directory.FullName, "providers");
            var providers = new ProviderConfigurationStore(Path.Combine(providerDirectory, "providers.json"),
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            _ = providers.Configure(new("personal", "openai", "initiating-parent-model", "initiating-parent-secret",
                false, initiatorId));
            _ = providers.Configure(new("personal", "ollama-cloud", "other-parent-model", "other-parent-secret",
                false, partnerId));

            using var world = PrivateWorldRuntime.Restore(state, ProviderFor);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var plan = world.Inhabitants.Single(person => person.InhabitantId == initiatorId).Parenthood!;
            Assert.Equal("preparing", plan.Stage);
            PositionFamilyFixtureAt(world, plan.LastTransitionTick + 598);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Empty(world.Society.Births);

            var stateFile = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), ProviderFor);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromHours(1));
            presence.RecordAuthenticatedReconnect("owner");
            using var service = new PrivateWorldRuntimeService(world, stateFile, presence, providers: providers);
            Directory.Delete(providerDirectory, recursive: true);
            File.WriteAllText(providerDirectory, "block the provider configuration directory");
            Assert.False(await service.TryAdvanceOnceAsync());

            var birth = Assert.Single(world.Society.Births);
            var childId = birth.ChildId;
            var child = world.Inhabitants.Single(item => item.InhabitantId == childId);
            Assert.Equal(SocietyAgeBand.Infant, world.Society.GetInhabitant(childId).AgeBand);
            Assert.Equal("initiating-parent-model", child.ChildModelSelection!.ModelId);
            Assert.Equal("openai", child.ChildModelSelection.Provider);
            Assert.Equal(0, callCounts.GetValueOrDefault(childId));
            Assert.DoesNotContain(providers.CaptureStatus().Assignments ?? [], item => item.InhabitantId == childId);
            var shown = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(item => item.Id == childId);
            Assert.Contains(shown.DecisionFactors, factor => factor.Key == "birth-model-provider" && factor.Detail == "openai");
            Assert.Contains(shown.DecisionFactors, factor => factor.Key == "birth-model-id" && factor.Detail == "initiating-parent-model");

            var birthTick = world.WorldTick;
            var savedText = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(stateFile.Path));
            Assert.False(savedText.Contains("initiating-parent-secret", StringComparison.Ordinal),
                "World checkpoints must never contain provider credentials.");
            Assert.False(savedText.Contains("other-parent-secret", StringComparison.Ordinal),
                "World checkpoints must never contain provider credentials.");
            Assert.Contains("initiating-parent-model", savedText, StringComparison.Ordinal);

            var manualSaves = new ManualWorldSaveStore(stateFile.Path, mutationGate: providers.WorldMutationGate);
            var manual = manualSaves.Create("Birth waiting for provider storage", world,
                providers.CaptureRuntimeConfiguration().Assignments ?? []);
            var savedManual = manualSaves.ReadCommitted(manual.Id);
            Assert.Equal(child.ChildModelSelection,
                savedManual.Checkpoint.Inhabitants.Single(item => item.InhabitantId == childId).ChildModelSelection);
            Assert.DoesNotContain(savedManual.Assignments, item => item.InhabitantId == childId);

            File.Delete(providerDirectory);
            Directory.CreateDirectory(providerDirectory);

            var autosave = new WorldAutosaveStore(stateFile.Path, world.Society.WorldId,
                mutationGate: providers.WorldMutationGate);
            var catalog = new WorldCatalogStore(stateFile.Path, world.ExportState(),
                providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture(),
                providers.WorldMutationGate);
            var originalWorldId = catalog.Active().Id;
            var selection = new WorldSelectionCoordinator(catalog, world, stateFile, providers, autosave,
                new WorldJevPolicy(), NullLogger<WorldSelectionCoordinator>.Instance, ProviderFor);
            var switchOptions = new GeographyOptions("child-binding-switch", WorldSizePreset.Small,
                WaterPercent: 50, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);
            var switchPreview = selection.Preview(switchOptions);
            Assert.NotNull(switchPreview.MapLayersDigest);
            _ = selection.Create("Child binding check", switchOptions, switchPreview.Coverage!.Attempt,
                switchPreview.ManifestDigest, switchPreview.MapLayersDigest, acceptUnmetTargets: true);
            _ = selection.Select(originalWorldId);

            _ = providers.Configure(new("personal", "ollama-cloud", "later-parent-model", "later-parent-secret",
                false, initiatorId));
            Assert.False(await service.TryAdvanceOnceAsync());

            child = world.Inhabitants.Single(item => item.InhabitantId == childId);
            Assert.Equal(birthTick, world.WorldTick);
            Assert.Equal(SocietyAgeBand.Infant, world.Society.GetInhabitant(childId).AgeBand);
            Assert.Equal("initiating-parent-model", child.ChildModelSelection!.ModelId);
            Assert.Equal("openai", child.ChildModelSelection.Provider);
            Assert.Equal(PrivateWorldRuntime.OpenAiModelEndpointIdentity, child.ChildModelSelection.EndpointIdentity);
            Assert.False(string.IsNullOrWhiteSpace(child.ChildModelSelection.CredentialSlotId));
            Assert.Equal(PrivateWorldRuntime.ChildModelChoiceInitiatingParent, child.ChildModelSelection.ChoiceReason);
            Assert.Equal(0, callCounts.GetValueOrDefault(childId));
            Assert.Equal("ollama-cloud", providers.CaptureRuntimeConfiguration().Assignments!
                .Single(item => item.InhabitantId == initiatorId && item.Role == PlayerDecisionProviders.PlanningRole).Provider);

            var childAssignments = providers.CaptureStatus().Assignments!
                .Where(item => item.InhabitantId == childId).ToArray();
            Assert.Equal(2, childAssignments.Length);
            Assert.All(childAssignments, assignment =>
            {
                Assert.Equal("openai", assignment.Provider);
                Assert.Equal("initiating-parent-model", assignment.Model);
                Assert.Equal(child.ChildModelSelection.CredentialSlotId, assignment.CredentialSlotId);
                Assert.Equal(PrivateWorldRuntime.ChildModelChoiceInitiatingParent, assignment.SelectionReason);
            });
            var childSlot = Assert.Single(providers.CaptureRuntimeConfiguration().CredentialSlots!, slot =>
                slot.Id == child.ChildModelSelection.CredentialSlotId && slot.Provider == "openai");
            Assert.Equal(SecretDigest("initiating-parent-secret"), SecretDigest(childSlot.ApiKey));

            savedText = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(stateFile.Path));
            Assert.False(savedText.Contains("initiating-parent-secret", StringComparison.Ordinal),
                "World checkpoints must never contain provider credentials.");
            Assert.False(savedText.Contains("other-parent-secret", StringComparison.Ordinal),
                "World checkpoints must never contain provider credentials.");

            using var restored = stateFile.LoadOrCreate("settlement-parenthood");
            Assert.Equal(child.ChildModelSelection,
                restored.Inhabitants.Single(item => item.InhabitantId == childId).ChildModelSelection);
            var restoredProviders = new ProviderConfigurationStore(providers.Path,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            Assert.Equal("initiating-parent-model", Assert.Single(restoredProviders.CaptureStatus().Assignments!,
                item => item.InhabitantId == childId && item.Role == "planning").Model);

            var saved = restored.ExportState();
            var death = SocietyFixture.Kill(saved.Society.Society, childId, SocietyDeathCause.Accident).Checkpoint;
            var archivedChild = new PlaytestDeceasedInhabitantState(childId, death.WorldTick,
                death.AgeAt(death.GetInhabitant(childId), death.WorldTick), child);
            var archived = saved with
            {
                Society = saved.Society with { Society = death },
                Inhabitants = saved.Inhabitants.Where(item => item.InhabitantId != childId).ToArray(),
                DeceasedInhabitants = [.. saved.DeceasedInhabitants ?? [], archivedChild],
            };
            Assert.Equal(child.ChildModelSelection, Assert.Single(PrivateWorldRuntimeCodec.Decode(
                PrivateWorldRuntimeCodec.Encode(archived)).DeceasedInhabitants!).LastPhysical.ChildModelSelection);
            var olderArchive = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(
                archived with { SchemaVersion = 33 }));
            Assert.Contains($"minimum supported schema {PrivateWorldRuntime.StateSchemaVersion}", olderArchive.Message,
                StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(
                archived with { SchemaVersion = PrivateWorldRuntime.ChildModelSelectionSchemaVersion - 1 }));
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(archived with
            {
                DeceasedInhabitants = [archivedChild with
                {
                    LastPhysical = child with { ChildModelSelection = child.ChildModelSelection! with { Role = "invalid" } },
                }],
            }));
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(
                saved with { SchemaVersion = PrivateWorldRuntime.ChildModelSelectionSchemaVersion - 1 }));
            var olderLiving = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(
                saved with { SchemaVersion = 33 }));
            Assert.Contains($"minimum supported schema {PrivateWorldRuntime.StateSchemaVersion}", olderLiving.Message,
                StringComparison.Ordinal);
            Assert.Equal(child.ChildModelSelection, PrivateWorldRuntimeCodec.Decode(
                PrivateWorldRuntimeCodec.Encode(saved)).Inhabitants.Single(person => person.InhabitantId == childId).ChildModelSelection);
            var hungryChildState = saved with
            {
                Inhabitants = saved.Inhabitants.Select(item => item.InhabitantId == childId
                    ? item with { HungerBasisPoints = 500 } : item).ToArray(),
            };
            using var agingWorld = PrivateWorldRuntime.Restore(hungryChildState, ProviderFor);
            agingWorld.Pause();
            Assert.True(agingWorld.SetLifePace(1_460));
            PositionChildBeforeAge(agingWorld, childId, agingWorld.Society.Config.InfantYears);
            agingWorld.Resume();
            Assert.True((await agingWorld.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(SocietyAgeBand.Infant, agingWorld.Society.GetInhabitant(childId).AgeBand);
            Assert.Equal(0, callCounts.GetValueOrDefault(childId));
            var maturityTicks = agingWorld.Society.Config.TicksPerWorldDay;
            for (var tick = 0; tick < maturityTicks && callCounts.GetValueOrDefault(childId) == 0; tick++)
                Assert.True((await agingWorld.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(SocietyAgeBand.Child, agingWorld.Society.GetInhabitant(childId).AgeBand);
            Assert.True(callCounts.GetValueOrDefault(childId) > 0);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PendingChildModelChoiceSurvivesServiceShutdownAndDoesNotAdoptLaterParentKey()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-child-model-restart-");
        try
        {
            var state = await PreparedState();
            var initiatorId = state.Inhabitants[0].InhabitantId;
            var partnerId = state.Inhabitants[1].InhabitantId;
            IDecisionProvider ProviderFor(string id) => id == initiatorId
                ? new ParentProvider("parent_propose:")
                : id == partnerId
                    ? new ParentProvider("parent_accept:")
                    : new DeterministicDecisionProvider();

            var providerDirectory = Path.Combine(directory.FullName, "providers");
            var providerPath = Path.Combine(providerDirectory, "providers.json");
            var providers = new ProviderConfigurationStore(providerPath,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            _ = providers.Configure(new("personal", "openai", "birth-model", "birth-only-test-key", false, initiatorId));
            _ = providers.Configure(new("personal", "ollama-cloud", "partner-model", "partner-only-test-key", false, partnerId));
            var providerFileBeforeFailure = File.ReadAllBytes(providerPath);

            using var world = PrivateWorldRuntime.Restore(state, ProviderFor);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            for (var tick = 0; tick < 599; tick++)
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);

            var stateFile = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), ProviderFor);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromHours(1));
            presence.RecordAuthenticatedReconnect("owner");
            var service = new PrivateWorldRuntimeService(world, stateFile, presence, providers: providers);
            Directory.Delete(providerDirectory, recursive: true);
            File.WriteAllText(providerDirectory, "block the provider configuration directory");
            Assert.False(await service.TryAdvanceOnceAsync());

            var childId = Assert.Single(world.Society.Births).ChildId;
            var birthTick = world.WorldTick;
            var birthChoice = world.Inhabitants.Single(item => item.InhabitantId == childId).ChildModelSelection!;
            Assert.Equal("birth-model", birthChoice.ModelId);
            Assert.Equal("openai", birthChoice.Provider);
            var slotId = birthChoice.CredentialSlotId;
            Assert.False(string.IsNullOrWhiteSpace(slotId));
            var checkpointText = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(stateFile.Path));
            Assert.Contains("birth-model", checkpointText, StringComparison.Ordinal);
            Assert.False(checkpointText.Contains("birth-only-test-key", StringComparison.Ordinal),
                "World checkpoints must never contain provider credentials.");

            await service.StopAsync(CancellationToken.None);
            service.Dispose();

            // Restore the last durable provider file, then create fresh host
            // objects as a process restart would. The active checkpoint must
            // supply the child choice because the in-memory key draft is gone.
            File.Delete(providerDirectory);
            Directory.CreateDirectory(providerDirectory);
            File.WriteAllBytes(providerPath, providerFileBeforeFailure);
            var restartedProviders = new ProviderConfigurationStore(providerPath,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            _ = restartedProviders.Configure(new("personal", "ollama-cloud", "later-parent-model", "later-parent-key",
                false, initiatorId));
            using var restartedWorld = stateFile.LoadOrCreate("settlement-parenthood");
            Assert.Equal(birthChoice, restartedWorld.Inhabitants.Single(item => item.InhabitantId == childId).ChildModelSelection);
            using var restartedService = new PrivateWorldRuntimeService(restartedWorld, stateFile, presence,
                providers: restartedProviders);
            Assert.True(await restartedService.TryAdvanceOnceAsync());

            Assert.Equal(birthTick + 1, restartedWorld.WorldTick);
            var child = restartedWorld.Inhabitants.Single(item => item.InhabitantId == childId);
            Assert.Equal(birthChoice, child.ChildModelSelection);
            Assert.Equal(SocietyAgeBand.Infant, restartedWorld.Society.GetInhabitant(childId).AgeBand);
            var childAssignments = restartedProviders.CaptureRuntimeConfiguration().Assignments!
                .Where(item => item.InhabitantId == childId).ToArray();
            Assert.Equal(2, childAssignments.Length);
            Assert.All(childAssignments, assignment =>
            {
                Assert.Equal("openai", assignment.Provider);
                Assert.Equal("birth-model", assignment.Model);
                Assert.Equal(slotId, assignment.CredentialSlotId);
            });
            Assert.DoesNotContain(restartedProviders.CaptureRuntimeConfiguration().CredentialSlots!, slot =>
                slot.Id == slotId);
            var reloadedProviderStore = new ProviderConfigurationStore(providerPath,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            Assert.Equal("openai", reloadedProviderStore.CaptureRuntimeConfiguration().Assignments!
                .Single(item => item.InhabitantId == childId && item.Role == PlayerDecisionProviders.PlanningRole).Provider);
            Assert.DoesNotContain(reloadedProviderStore.CaptureRuntimeConfiguration().CredentialSlots!, slot =>
                slot.Id == slotId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task OwnerChildModelChoiceSurvivesLaterTicksAndRestart()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-child-model-owner-choice-");
        try
        {
            var state = await PreparedState();
            var initiatorId = state.Inhabitants[0].InhabitantId;
            var partnerId = state.Inhabitants[1].InhabitantId;
            IDecisionProvider ProviderFor(string id) => id == initiatorId
                ? new ParentProvider("parent_propose:")
                : id == partnerId
                    ? new ParentProvider("parent_accept:")
                    : new DeterministicDecisionProvider();

            var providerDirectory = Path.Combine(directory.FullName, "providers");
            var providerPath = Path.Combine(providerDirectory, "providers.json");
            var providers = new ProviderConfigurationStore(providerPath,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            _ = providers.Configure(new("personal", "openai", "parent-model", "parent-only-test-key",
                false, initiatorId));

            using var world = PrivateWorldRuntime.Restore(state, ProviderFor);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            for (var tick = 0; tick < 599; tick++)
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);

            var stateFile = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), ProviderFor);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromHours(1));
            presence.RecordAuthenticatedReconnect("owner");
            using var service = new PrivateWorldRuntimeService(world, stateFile, presence, providers: providers);
            Directory.Delete(providerDirectory, recursive: true);
            File.WriteAllText(providerDirectory, "block the provider configuration directory");
            Assert.False(await service.TryAdvanceOnceAsync());

            var child = Assert.Single(world.Inhabitants, item => item.ChildModelSelection is not null);
            var birthChoice = child.ChildModelSelection!;
            File.Delete(providerDirectory);
            Directory.CreateDirectory(providerDirectory);
            var pendingOwnerSlotId = Guid.NewGuid().ToString("N");
            _ = providers.Configure(new(PlayerDecisionProviders.PlanningRole, "ollama-cloud",
                "owner-pending-model", "owner-pending-test-key", false, child.InhabitantId,
                pendingOwnerSlotId, "Owner model set during recovery"));
            Assert.False(await service.TryAdvanceOnceAsync(),
                "Persisting the missing birth route must leave the world paused until the owner resumes it.");

            var birthAssignments = providers.CaptureRuntimeConfiguration().Assignments!
                .Where(item => item.InhabitantId == child.InhabitantId).ToArray();
            Assert.Equal(2, birthAssignments.Length);
            var recoveredBirthRoute = Assert.Single(birthAssignments,
                assignment => assignment.Role == PlayerDecisionProviders.RoutineRole);
            Assert.Equal(birthChoice.Provider, recoveredBirthRoute.Provider);
            Assert.Equal(birthChoice.ModelId, recoveredBirthRoute.Model);
            Assert.Equal(birthChoice.CredentialSlotId, recoveredBirthRoute.CredentialSlotId);
            Assert.Equal(birthChoice.ChoiceReason, recoveredBirthRoute.SelectionReason);
            var pendingOwnerRoute = Assert.Single(birthAssignments,
                assignment => assignment.Role == PlayerDecisionProviders.PlanningRole);
            Assert.Equal("ollama-cloud", pendingOwnerRoute.Provider);
            Assert.Equal("owner-pending-model", pendingOwnerRoute.Model);
            Assert.Equal(pendingOwnerSlotId, pendingOwnerRoute.CredentialSlotId);
            Assert.Null(pendingOwnerRoute.SelectionReason);

            world.Resume();
            var ownerSlotId = Guid.NewGuid().ToString("N");
            _ = providers.Configure(new(PlayerDecisionProviders.PlanningRole, "ollama-cloud",
                "owner-selected-model", "owner-only-test-key", false, child.InhabitantId,
                ownerSlotId, "Owner-selected model"));

            var ownerAssignments = providers.CaptureRuntimeConfiguration().Assignments!
                .Where(item => item.InhabitantId == child.InhabitantId).ToArray();
            Assert.Equal(2, ownerAssignments.Length);
            var unchangedBirthRoute = Assert.Single(ownerAssignments,
                item => item.Role == PlayerDecisionProviders.RoutineRole);
            var ownerRoute = Assert.Single(ownerAssignments,
                item => item.Role == PlayerDecisionProviders.PlanningRole);
            Assert.Equal(birthChoice.Provider, unchangedBirthRoute.Provider);
            Assert.Equal(birthChoice.ModelId, unchangedBirthRoute.Model);
            Assert.Equal(birthChoice.CredentialSlotId, unchangedBirthRoute.CredentialSlotId);
            Assert.Equal(birthChoice.ChoiceReason, unchangedBirthRoute.SelectionReason);
            Assert.Equal("ollama-cloud", ownerRoute.Provider);
            Assert.Equal("owner-selected-model", ownerRoute.Model);
            Assert.Equal(ownerSlotId, ownerRoute.CredentialSlotId);
            Assert.Null(ownerRoute.SelectionReason);
            Assert.True(await service.TryAdvanceOnceAsync());

            using var restartedWorld = stateFile.LoadOrCreate("settlement-parenthood");
            var restartedProviders = new ProviderConfigurationStore(providerPath,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            using var restartedService = new PrivateWorldRuntimeService(restartedWorld, stateFile, presence,
                providers: restartedProviders);
            Assert.True(await restartedService.TryAdvanceOnceAsync());

            Assert.Equal(birthChoice, restartedWorld.Inhabitants.Single(item => item.InhabitantId == child.InhabitantId)
                .ChildModelSelection);
            var restartedAssignments = restartedProviders.CaptureRuntimeConfiguration().Assignments!
                .Where(item => item.InhabitantId == child.InhabitantId).ToArray();
            Assert.Equal(2, restartedAssignments.Length);
            var restartedBirthRoute = Assert.Single(restartedAssignments,
                item => item.Role == PlayerDecisionProviders.RoutineRole);
            var restartedOwnerRoute = Assert.Single(restartedAssignments,
                item => item.Role == PlayerDecisionProviders.PlanningRole);
            Assert.Equal(birthChoice.Provider, restartedBirthRoute.Provider);
            Assert.Equal(birthChoice.ModelId, restartedBirthRoute.Model);
            Assert.Equal(birthChoice.CredentialSlotId, restartedBirthRoute.CredentialSlotId);
            Assert.Equal(birthChoice.ChoiceReason, restartedBirthRoute.SelectionReason);
            Assert.Equal("ollama-cloud", restartedOwnerRoute.Provider);
            Assert.Equal("owner-selected-model", restartedOwnerRoute.Model);
            Assert.Equal(ownerSlotId, restartedOwnerRoute.CredentialSlotId);
            Assert.Null(restartedOwnerRoute.SelectionReason);

            _ = restartedProviders.Configure(new(PlayerDecisionProviders.RoutineRole, PlayerDecisionProviders.Inherit,
                null, null, false, child.InhabitantId));
            var partiallyUnconfigured = restartedProviders.CaptureRuntimeConfiguration().Assignments!
                .Where(item => item.InhabitantId == child.InhabitantId).ToArray();
            Assert.Equal(2, partiallyUnconfigured.Length);
            var inheritedRoutine = Assert.Single(partiallyUnconfigured,
                assignment => assignment.Role == PlayerDecisionProviders.RoutineRole);
            Assert.Equal(PlayerDecisionProviders.Inherit, inheritedRoutine.Provider);
            Assert.Null(inheritedRoutine.SelectionReason);
            var retainedPlanning = Assert.Single(partiallyUnconfigured,
                assignment => assignment.Role == PlayerDecisionProviders.PlanningRole);
            Assert.Equal("ollama-cloud", retainedPlanning.Provider);
            Assert.Equal("owner-selected-model", retainedPlanning.Model);
            Assert.Null(retainedPlanning.SelectionReason);
            Assert.True(await restartedService.TryAdvanceOnceAsync());

            using var partiallyUnconfiguredWorld = stateFile.LoadOrCreate("settlement-parenthood");
            var partiallyUnconfiguredProviders = new ProviderConfigurationStore(providerPath,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            using var partiallyUnconfiguredService = new PrivateWorldRuntimeService(
                partiallyUnconfiguredWorld, stateFile, presence, providers: partiallyUnconfiguredProviders);
            Assert.True(await partiallyUnconfiguredService.TryAdvanceOnceAsync());
            Assert.Equal(birthChoice, partiallyUnconfiguredWorld.Inhabitants
                .Single(item => item.InhabitantId == child.InhabitantId).ChildModelSelection);
            var reloadedPartialChoice = partiallyUnconfiguredProviders.CaptureRuntimeConfiguration().Assignments!
                .Where(item => item.InhabitantId == child.InhabitantId).ToArray();
            Assert.Equal(2, reloadedPartialChoice.Length);
            Assert.Equal(PlayerDecisionProviders.Inherit,
                Assert.Single(reloadedPartialChoice, item => item.Role == PlayerDecisionProviders.RoutineRole).Provider);
            Assert.Equal("owner-selected-model",
                Assert.Single(reloadedPartialChoice, item => item.Role == PlayerDecisionProviders.PlanningRole).Model);

            _ = partiallyUnconfiguredProviders.Configure(new("personal", PlayerDecisionProviders.Inherit,
                null, null, false, child.InhabitantId));
            var explicitlyUnconfigured = partiallyUnconfiguredProviders.CaptureRuntimeConfiguration().Assignments!
                .Where(item => item.InhabitantId == child.InhabitantId).ToArray();
            Assert.Equal(2, explicitlyUnconfigured.Length);
            Assert.All(explicitlyUnconfigured, assignment =>
            {
                Assert.Equal(PlayerDecisionProviders.Inherit, assignment.Provider);
                Assert.Null(assignment.SelectionReason);
            });
            Assert.True(await partiallyUnconfiguredService.TryAdvanceOnceAsync());

            using var explicitlyUnconfiguredWorld = stateFile.LoadOrCreate("settlement-parenthood");
            var explicitlyUnconfiguredProviders = new ProviderConfigurationStore(providerPath,
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            using var explicitlyUnconfiguredService = new PrivateWorldRuntimeService(
                explicitlyUnconfiguredWorld, stateFile, presence, providers: explicitlyUnconfiguredProviders);
            Assert.True(await explicitlyUnconfiguredService.TryAdvanceOnceAsync());
            Assert.Equal(birthChoice, explicitlyUnconfiguredWorld.Inhabitants
                .Single(item => item.InhabitantId == child.InhabitantId).ChildModelSelection);
            var reloadedExplicitChoice = explicitlyUnconfiguredProviders.CaptureRuntimeConfiguration().Assignments!
                .Where(item => item.InhabitantId == child.InhabitantId).ToArray();
            Assert.Equal(2, reloadedExplicitChoice.Length);
            Assert.All(reloadedExplicitChoice, assignment =>
            {
                Assert.Equal(PlayerDecisionProviders.Inherit, assignment.Provider);
                Assert.Null(assignment.SelectionReason);
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class CountingProvider(string inhabitantId, Dictionary<string, int> callCounts) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            callCounts[inhabitantId] = callCounts.GetValueOrDefault(inhabitantId) + 1;
            return new DeterministicDecisionProvider().DecideAsync(request, cancellationToken);
        }
    }

    private static string SecretDigest(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
