using System.Net;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProviderConfigurationStoreTests
{
    [WindowsCredentialFact]
    public void WindowsProtectionMigratesLegacyKeysAndPreservesUnreadableProtectedBytes()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-windows-protection-");
        try
        {
            var path = Path.Combine(directory.FullName, "providers.json");
            const string key = "test-only-legacy-windows-secret";
            var legacy = new ProviderConfigurationState(3, 0, "deterministic", "openai",
                new("jev-test", null), new("test-model", key), new("ollama-test", null), [], [], []);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(legacy));
            var migrated = new ProviderConfigurationStore(path, EmptySeed());
            Assert.Equal(key, migrated.CaptureRuntimeConfiguration().OpenAi.ApiKey);
            var log = new RecordingLogger<ProviderConfigurationStoreTests>();
            ProviderCredentialTelemetry.Ready(log, "windows_current_user");
            Assert.Contains(log.Messages, message => message.Contains("provider_credential_storage outcome=ready", StringComparison.Ordinal));
            Assert.DoesNotContain(log.Messages, message => message.Contains(key, StringComparison.Ordinal) || message.Contains(path, StringComparison.Ordinal));
            var encrypted = File.ReadAllText(path);
            Assert.DoesNotContain(key, encrypted, StringComparison.Ordinal);
            Assert.Equal(key, new ProviderConfigurationStore(path, EmptySeed()).CaptureRuntimeConfiguration().OpenAi.ApiKey);
            // Truncate the native protected blob, leaving its format identifier intact.
            var damaged = encrypted[..(encrypted.IndexOf('\n') + 1)] + "AAAA";
            File.WriteAllText(path, damaged);
            var error = Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
                new ProviderConfigurationStore(path, EmptySeed()));
            Assert.DoesNotContain(key, error.Message, StringComparison.Ordinal);
            Assert.Equal(damaged, File.ReadAllText(path));
            File.WriteAllText(path, encrypted);
            Assert.Equal(key, new ProviderConfigurationStore(path, EmptySeed()).CaptureRuntimeConfiguration().OpenAi.ApiKey);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task BornChildNeverUsesPaidWorldDefaultWithoutAnExplicitPersonalAssignment()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-child-provider-");
        try
        {
            var path = Path.Combine(directory.FullName, "providers.json");
            var store = new ProviderConfigurationStore(path, EmptySeed());
            _ = store.Configure(new("routine", "jev", "jev-test", "world-jev-secret", false));
            _ = store.Configure(new("planning", "ollama-cloud", "world-model", "world-paid-secret", false));
            var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler));
            var routine = Request(router.ProviderEpoch).Observation with { RequiresPersonalProvider = true };
            var planning = Request(router.ProviderEpoch, strategic: true).Observation with { RequiresPersonalProvider = true };
            Assert.Equal(DecisionProviderKind.Deterministic, router.KindFor(routine));
            Assert.Equal(DecisionProviderKind.Deterministic, router.KindFor(planning));
            Assert.Equal(DecisionProviderKind.Deterministic,
                (await router.DecideAsync(new("child-routine", router.ProviderEpoch, routine))).Provider);
            Assert.Equal(DecisionProviderKind.Deterministic,
                (await router.DecideAsync(new("child-planning", router.ProviderEpoch, planning))).Provider);
            Assert.Null(handler.LastUri);

            var slot = Guid.NewGuid().ToString("N");
            _ = store.Configure(new("personal", "openai", "child-model", "child-secret", false,
                "inhabitant-test", slot, "Child model"));
            var restoredStore = new ProviderConfigurationStore(path, EmptySeed());
            var restored = new ConfigurableDecisionProvider(restoredStore, new FixedHttpClientFactory(handler));
            Assert.Equal(DecisionProviderKind.LargeLanguageModel, restored.KindFor(planning));
            _ = await restored.DecideAsync(new("child-selected", restored.ProviderEpoch, planning));
            Assert.Equal("api.openai.com", handler.LastUri!.Host);
            Assert.Equal("child-model", handler.LastModel);
            Assert.Equal("Bearer child-secret", handler.LastAuthorization);

            _ = restoredStore.Configure(new("personal", "inherit", null, null, false, "inhabitant-test"));
            Assert.Equal(DecisionProviderKind.Deterministic, restored.KindFor(planning));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task PaidCallCapBlocksProviderBeforeHttpAndLocalDecisionsRemainFree()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-usage-router-");
        try
        {
            var configuration = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), EmptySeed());
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var handler = new ProviderResponseHandler();
            _ = configuration.Configure(new("planning", "openai", "gpt-test", "secret-usage-key", false));
            _ = usage.Configure(new ProviderUsageLimitAction(1));
            var router = new ConfigurableDecisionProvider(configuration, new FixedHttpClientFactory(handler),
                usageStore: usage);
            _ = await router.DecideAsync(Request(router.ProviderEpoch, strategic: true));
            Assert.Equal(1, usage.Capture().Attempts);
            await Assert.ThrowsAsync<ProviderUsageLimitReachedException>(async () =>
                await router.DecideAsync(Request(router.ProviderEpoch, strategic: true)));
            Assert.Equal(1, usage.Capture().Attempts);
            Assert.DoesNotContain("secret-usage-key", File.ReadAllText(Path.Combine(directory.FullName, "usage.json")));
            _ = configuration.Configure(new("planning", "deterministic", null, null, false));
            var local = await router.DecideAsync(Request(router.ProviderEpoch, strategic: true));
            Assert.Equal(DecisionProviderKind.Deterministic, local.Provider);
            Assert.Equal(1, usage.Capture().Attempts);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task JevMemoryScoringSharesItsRoutineAttemptAndConsumesOnlyOnePaidCall()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-memory-usage-");
        try
        {
            var configuration = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), EmptySeed());
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            var handler = new ProviderResponseHandler();
            _ = configuration.Configure(new("routine", "jev", "jev-test", "jev-memory-secret", false));
            _ = usage.Configure(new ProviderUsageLimitAction(1));
            var router = new ConfigurableDecisionProvider(configuration, new FixedHttpClientFactory(handler),
                usageStore: usage);
            var original = Request(router.ProviderEpoch);
            var request = original with
            {
                Observation = original.Observation with
                {
                    MemoryCompactionCandidates = Enumerable.Range(0, 4).Select(index =>
                        new CognitionMemoryCompactionCandidate(
                            $"experience-{index}", "inhabitant-test", "experience", "friend",
                            $"Existing owner-private event {index}.", 1)).ToArray(),
                },
            };

            var response = await router.DecideAsync(request);

            Assert.Equal(DecisionProviderKind.Jev, response.Provider);
            Assert.Equal("routine", response.Usage!.Role);
            Assert.Equal(1, usage.Capture().Attempts);
            Assert.Equal(1, usage.Capture().Completed);
            Assert.Contains("memory_salience_00", handler.LastBody!, StringComparison.Ordinal);
            await Assert.ThrowsAsync<ProviderUsageLimitReachedException>(async () =>
                await router.DecideAsync(request with { RequestId = "second-memory-request" }));
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(1, usage.Capture().Attempts);
            Assert.DoesNotContain("jev-memory-secret", File.ReadAllText(Path.Combine(directory.FullName, "usage.json")));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void SlotDeletionTelemetryReportsOutcomeWithoutCredentialMaterial()
    {
        var logger = new RecordingLogger<ProviderConfigurationStore>();
        var slotId = Guid.NewGuid().ToString("N");
        OwnerCredentialSlotTelemetry.Deleted(logger, "deleted", slotId);
        var message = Assert.Single(logger.Messages);
        Assert.Contains("outcome=deleted", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorldJevChangeLogReportsOnlyTickAndAvailability()
    {
        var logger = new RecordingLogger<PrivateWorldRuntimeService>();
        OwnerJevAssistanceTelemetry.Changed(logger, 123, false);
        Assert.Equal("world_jev_assistance tick=123 enabled=False", Assert.Single(logger.Messages));
    }

    [Fact]
    public async Task DisablingWorldJevRoutesRoutineWorkToTheAgentsPersonalModelAndInvalidatesOldRequests()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-jev-routing-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), EmptySeed());
            _ = store.Configure(new("routine", "jev", "jev-test", "routine-test-secret", false));
            _ = store.Configure(new("planning", "ollama-cloud", "world-model", "world-test-secret", false));
            _ = store.Configure(new("planning", "openai", "personal-model", "personal-test-secret", false, "inhabitant-test"));
            Assert.Throws<ArgumentException>(() => store.Configure(
                new("routine", "jev", null, null, false, "inhabitant-test")));
            var policy = new WorldJevPolicy();
            var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler), jevPolicy: policy);
            var oldRequest = Request(router.ProviderEpoch);
            Assert.Equal(DecisionProviderKind.Jev, router.KindFor(oldRequest.Observation));

            policy.Set(false, 1);
            Assert.NotEqual(oldRequest.ProviderEpoch, router.ProviderEpoch);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await router.DecideAsync(oldRequest));
            var response = await router.DecideAsync(Request(router.ProviderEpoch));
            Assert.Equal(DecisionProviderKind.LargeLanguageModel, response.Provider);
            Assert.Equal("api.openai.com", handler.LastUri!.Host);
            Assert.Equal("personal-model", handler.LastModel);

            _ = store.Configure(new("planning", "inherit", null, null, false, "inhabitant-test"));
            _ = await router.DecideAsync(Request(router.ProviderEpoch));
            Assert.Equal("ollama.com", handler.LastUri!.Host);

            _ = store.Configure(new("planning", "deterministic", null, null, false));
            var local = await router.DecideAsync(Request(router.ProviderEpoch));
            Assert.Equal(DecisionProviderKind.Deterministic, local.Provider);

            policy.Set(true, 2);
            _ = await router.DecideAsync(Request(router.ProviderEpoch));
            Assert.Equal("api.typesafe.ai", handler.LastUri!.Host);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InhabitantInventionUsesPlanningProvider()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-invention-routing-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), EmptySeed());
            _ = store.Configure(new("routine", "jev", "jev-test", "routine-test-secret", false));
            _ = store.Configure(new("planning", "ollama-cloud", "planning-test", "planning-test-secret", false));
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(new ProviderResponseHandler()));
            var observation = Request(router.ProviderEpoch).Observation with
            {
                Candidates = [new("invent:building:shelter", "Propose a shelter.", 35), new("safe_idle", "Wait safely.", 100)],
            };
            Assert.Equal(DecisionProviderKind.LargeLanguageModel, router.KindFor(observation));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("wear_clothing")]
    [InlineData("care:dependent-child")]
    public async Task ExposureActionsUseRoutineProviderInsteadOfPlanning(string candidateId)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-survival-routing-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), EmptySeed());
            _ = store.Configure(new("routine", "jev", "jev-test", "routine-test-secret", false));
            _ = store.Configure(new("planning", "ollama-cloud", "planning-test", "planning-test-secret", false));
            var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler));
            var request = Request(router.ProviderEpoch);
            request = request with
            {
                Observation = request.Observation with
                {
                    Candidates = [new(candidateId, "Survive exposure.", 20), new("safe_idle", "Wait safely.", 100)],
                },
            };
            Assert.Equal(DecisionProviderKind.Jev, router.KindFor(request.Observation));
            var response = await router.DecideAsync(request);
            Assert.Equal(DecisionProviderKind.Jev, response.Provider);
            Assert.Equal("api.typesafe.ai", handler.LastUri!.Host);
            Assert.Equal("routine", response.Usage!.Role);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PersonalAssignmentsRouteIndependentlySurviveRestartAndCanInheritAgain()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-assignment-");
        try
        {
            var path = Path.Combine(directory.FullName, "providers.json");
            var store = new ProviderConfigurationStore(path, EmptySeed());
            _ = store.Configure(new("planning", "ollama-cloud", "world-model", "cloud-test-secret", false));
            _ = store.Configure(new("planning", "openai", "personal-model", "openai-test-secret", false, "inhabitant-test"));
            Assert.Equal("ollama-cloud", store.CaptureStatus().PlanningProvider);
            Assert.Single(store.CaptureStatus().Assignments!);
            Assert.DoesNotContain("test-secret", System.Text.Json.JsonSerializer.Serialize(store.CaptureStatus()), StringComparison.Ordinal);

            store = new ProviderConfigurationStore(path, EmptySeed());
            var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler));
            var response = await router.DecideAsync(Request(router.ProviderEpoch, strategic: true));
            Assert.Equal("api.openai.com", handler.LastUri!.Host);
            Assert.Equal("personal-model", handler.LastModel);
            Assert.Equal("openai", response.Usage!.ProviderId);
            Assert.Equal("planning", response.Usage.Role);
            Assert.True(response.Usage.LatencyMilliseconds >= 0);
            var other = Request(router.ProviderEpoch, strategic: true);
            _ = await router.DecideAsync(other with { Observation = other.Observation with { InhabitantId = "other" } });
            Assert.Equal("ollama.com", handler.LastUri!.Host);
            Assert.Equal("world-model", handler.LastModel);

            _ = store.Configure(new("planning", "inherit", null, null, false, "inhabitant-test"));
            Assert.Empty(store.CaptureStatus().Assignments!);
            _ = await router.DecideAsync(Request(router.ProviderEpoch, strategic: true));
            Assert.Equal("ollama.com", handler.LastUri!.Host);

            _ = store.Configure(new("planning", "openai", "personal-model", null, false, "inhabitant-test"));
            _ = store.Configure(new("planning", "openai", null, null, true));
            Assert.Empty(store.CaptureStatus().Assignments!);
            Assert.Equal("ollama-cloud", store.CaptureStatus().PlanningProvider);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AgentsCanUseSeparateKeysForOneProviderWithoutExposingKeysOrDependingOnTheDefault()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-key-slots-");
        try
        {
            var path = Path.Combine(directory.FullName, "providers.json");
            var store = new ProviderConfigurationStore(path, EmptySeed());
            var firstSlot = Guid.NewGuid().ToString("N");
            var secondSlot = Guid.NewGuid().ToString("N");
            _ = store.Configure(new("planning", "openai", "model-one", "first-agent-secret", false,
                "inhabitant-test", firstSlot, "First account"));
            _ = store.Configure(new("planning", "openai", "model-two", "second-agent-secret", false,
                "other", secondSlot, "Second account"));
            Assert.Equal("deterministic", store.CaptureStatus().PlanningProvider);
            Assert.False(store.CaptureStatus().Providers.Single(item => item.Provider == "openai").HasCredential);
            Assert.Equal(2, store.CaptureStatus().CredentialSlots!.Count);
            Assert.DoesNotContain("first-agent-secret", System.Text.Json.JsonSerializer.Serialize(store.CaptureStatus()), StringComparison.Ordinal);
            Assert.DoesNotContain("second-agent-secret", System.Text.Json.JsonSerializer.Serialize(store.CaptureStatus()), StringComparison.Ordinal);

            store = new ProviderConfigurationStore(path, EmptySeed());
            var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler));
            _ = await router.DecideAsync(Request(router.ProviderEpoch, strategic: true));
            Assert.Equal("Bearer first-agent-secret", handler.LastAuthorization);
            Assert.Equal("model-one", handler.LastModel);
            var other = Request(router.ProviderEpoch, strategic: true);
            _ = await router.DecideAsync(other with { Observation = other.Observation with { InhabitantId = "other" } });
            Assert.Equal("Bearer second-agent-secret", handler.LastAuthorization);
            Assert.Equal("model-two", handler.LastModel);

            _ = store.Configure(new("planning", "openai", "shared-model", "default-secret", false));
            _ = store.Configure(new("planning", "openai", null, null, true));
            Assert.Equal(2, store.CaptureStatus().Assignments!.Count);
            _ = await router.DecideAsync(Request(router.ProviderEpoch, strategic: true));
            Assert.Equal("Bearer first-agent-secret", handler.LastAuthorization);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DeletingNamedKeyRequiresUnassignmentErasesPersistedKeyAndOldSavesFallBackSafely()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-key-delete-");
        try
        {
            var path = Path.Combine(directory.FullName, "providers.json");
            var store = new ProviderConfigurationStore(path, EmptySeed());
            var slotId = Guid.NewGuid().ToString("N");
            const string secret = "obsolete-slot-secret";
            _ = store.Configure(new("personal", "openai", "personal-model", secret, false,
                "inhabitant-test", slotId, "Obsolete"));
            var oldWorldAssignments = store.CaptureStatus().Assignments!.ToArray();
            var revision = store.CaptureStatus().Revision;

            var assigned = Assert.Throws<InvalidOperationException>(() => store.DeleteCredentialSlot(slotId));
            Assert.Contains("assigned", assigned.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(revision, store.CaptureStatus().Revision);
            Assert.Equal(secret, store.CaptureRuntimeConfiguration().CredentialSlots!.Single().ApiKey);
            if (OperatingSystem.IsWindows())
                Assert.DoesNotContain(secret, File.ReadAllText(path), StringComparison.Ordinal);

            _ = store.Configure(new("personal", "inherit", null, null, false, "inhabitant-test"));
            var deleted = store.DeleteCredentialSlot(slotId);
            Assert.Empty(deleted.CredentialSlots!);
            Assert.Empty(deleted.Assignments!);
            Assert.DoesNotContain(secret, File.ReadAllText(path), StringComparison.Ordinal);
            Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(deleted), StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => store.DeleteCredentialSlot(slotId));

            store = new ProviderConfigurationStore(path, EmptySeed());
            Assert.Empty(store.CaptureStatus().CredentialSlots!);
            store.RestoreWorldAssignments(oldWorldAssignments);
            Assert.Equal(2, store.CaptureStatus().Assignments!.Count);
            Assert.All(store.CaptureStatus().Assignments!, item =>
            {
                Assert.Equal("deterministic", item.Provider);
                Assert.Null(item.CredentialSlotId);
            });
            var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler));
            Assert.Equal(DecisionProviderKind.Deterministic, router.KindFor(Request(router.ProviderEpoch).Observation));
            var response = await router.DecideAsync(Request(router.ProviderEpoch, strategic: true));
            Assert.Equal(DecisionProviderKind.Deterministic, response.Provider);
            Assert.Null(handler.LastAuthorization);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void CredentialSlotDeletionPayloadIsBoundToTheSlotOnBothSides()
    {
        var action = new OwnerCredentialSlotDeletionAction(Guid.NewGuid().ToString("N"));
        var clientAction = new ClankerWorld.GodotClient.UI.OwnerCredentialSlotDeletionAction(action.CredentialSlotId);
        Assert.Equal(OwnerHttpBinding.CredentialSlotDeletionPayload(action),
            ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.CredentialSlotDeletion(clientAction));
        Assert.NotEqual(OwnerHttpBinding.CredentialSlotDeletionPayload(action),
            OwnerHttpBinding.CredentialSlotDeletionPayload(action with { CredentialSlotId = Guid.NewGuid().ToString("N") }));
    }

    [Fact]
    public async Task PersonalModelSelectionRoutesRoutineAndPlanningToTheSameAgentCredential()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-personal-model-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), EmptySeed());
            var slot = Guid.NewGuid().ToString("N");
            _ = store.Configure(new("personal", "openai", "chosen-model", "chosen-secret", false,
                "inhabitant-test", slot, "Personal"));
            Assert.Equal(2, store.CaptureStatus().Assignments!.Count);
            var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler));
            _ = await router.DecideAsync(Request(router.ProviderEpoch, strategic: false));
            Assert.Equal("Bearer chosen-secret", handler.LastAuthorization);
            Assert.Equal("chosen-model", handler.LastModel);
            _ = await router.DecideAsync(Request(router.ProviderEpoch, strategic: true));
            Assert.Equal("Bearer chosen-secret", handler.LastAuthorization);
            Assert.Equal("chosen-model", handler.LastModel);

            _ = store.Configure(new("personal", "inherit", null, null, false, "inhabitant-test"));
            Assert.Empty(store.CaptureStatus().Assignments!);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void PriorProviderConfigurationMigratesWithoutChangingItsSavedKey()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-key-migration-");
        try
        {
            var path = Path.Combine(directory.FullName, "providers.json");
            var store = new ProviderConfigurationStore(path, EmptySeed());
            _ = store.Configure(new("planning", "openai", "prior-model", "prior-secret", false));
            var current = store.CaptureRuntimeConfiguration();
            var prior = System.Text.Json.JsonSerializer.Serialize(new ProviderConfigurationState(2, current.Revision,
                current.RoutineProvider, current.PlanningProvider, current.Jev, current.OpenAi, current.OllamaCloud,
                current.Assignments));
            File.WriteAllText(path, prior);
            var migrated = new ProviderConfigurationStore(path, EmptySeed());
            Assert.Equal("prior-secret", migrated.CaptureRuntimeConfiguration().OpenAi.ApiKey);
            Assert.Equal("openai", migrated.CaptureStatus().PlanningProvider);
            Assert.Equal("prior-secret", new ProviderConfigurationStore(path, EmptySeed()).CaptureRuntimeConfiguration().OpenAi.ApiKey);
            if (OperatingSystem.IsWindows())
                Assert.DoesNotContain("prior-secret", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AssignmentTargetIsBoundByBothClientAndServerSignatures()
    {
        var action = new OwnerProviderConfigurationAction("planning", "deterministic", null, null, false, "mira");
        var clientAction = new ClankerWorld.GodotClient.UI.OwnerProviderConfigurationAction("planning", "deterministic", null, null, false, "mira");
        Assert.Equal(OwnerHttpBinding.ProviderConfigurationPayload(action),
            ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.ProviderConfiguration(clientAction));
        Assert.NotEqual(OwnerHttpBinding.ProviderConfigurationPayload(action),
            OwnerHttpBinding.ProviderConfigurationPayload(action with { InhabitantId = "rowan" }));
        Assert.NotEqual(OwnerHttpBinding.ProviderConfigurationPayload(action),
            OwnerHttpBinding.ProviderConfigurationPayload(action with { InhabitantId = null }));
        var slotAction = action with { Provider = "openai", CredentialSlotId = Guid.NewGuid().ToString("N"), NewCredentialLabel = "Personal" };
        var clientSlotAction = new ClankerWorld.GodotClient.UI.OwnerProviderConfigurationAction(
            slotAction.Role, slotAction.Provider, slotAction.Model, slotAction.ApiKey,
            slotAction.ForgetCredential, slotAction.InhabitantId, slotAction.CredentialSlotId, slotAction.NewCredentialLabel);
        Assert.Equal(OwnerHttpBinding.ProviderConfigurationPayload(slotAction),
            ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.ProviderConfiguration(clientSlotAction));
        Assert.NotEqual(OwnerHttpBinding.ProviderConfigurationPayload(slotAction),
            OwnerHttpBinding.ProviderConfigurationPayload(slotAction with { CredentialSlotId = Guid.NewGuid().ToString("N") }));
        Assert.NotEqual(OwnerHttpBinding.ProviderConfigurationPayload(slotAction),
            OwnerHttpBinding.ProviderConfigurationPayload(slotAction with { NewCredentialLabel = "Imposter" }));
    }

    [Fact]
    public async Task PlayerCanSwitchAmongEverySupportedProviderAndForgettingAnActiveKeyFallsBack()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-provider-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, "providers.json");
            var store = new ProviderConfigurationStore(path, EmptySeed());
            var handler = new ProviderResponseHandler();
            var logger = new RecordingLogger<ConfigurableDecisionProvider>();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler), logger);

            Assert.Equal("deterministic", store.CaptureStatus().RoutineProvider);
            Assert.Equal("deterministic", store.CaptureStatus().PlanningProvider);
            Assert.Equal(DecisionProviderKind.Deterministic, router.Kind);

            var jevStatus = store.Configure(new OwnerProviderConfigurationAction(
                "routine",
                "jev",
                "jev-test",
                "jev-secret-123",
                false));
            var jevResponse = await router.DecideAsync(Request(store.CaptureRuntimeConfiguration().Revision));
            Assert.Equal("jev", jevStatus.RoutineProvider);
            Assert.Equal(DecisionProviderKind.Jev, jevResponse.Provider);
            Assert.Equal(new Uri("https://api.typesafe.ai/v1/systemone"), handler.LastUri);
            Assert.Equal("Bearer jev-secret-123", handler.LastAuthorization);

            var openAiStatus = store.Configure(new OwnerProviderConfigurationAction(
                "planning",
                "openai",
                "openai-test",
                "openai-secret-123",
                false));
            var openAiResponse = await router.DecideAsync(Request(store.CaptureRuntimeConfiguration().Revision, strategic: true));
            Assert.Equal("jev", openAiStatus.RoutineProvider);
            Assert.Equal("openai", openAiStatus.PlanningProvider);
            Assert.Equal(DecisionProviderKind.Jev, router.KindFor(RequestObservation(strategic: false)));
            Assert.Equal(DecisionProviderKind.LargeLanguageModel, router.KindFor(RequestObservation(strategic: true)));
            Assert.Equal(DecisionProviderKind.LargeLanguageModel, openAiResponse.Provider);
            Assert.Equal(new Uri("https://api.openai.com/v1/chat/completions"), handler.LastUri);
            Assert.Equal("Bearer openai-secret-123", handler.LastAuthorization);

            var ollamaStatus = store.Configure(new OwnerProviderConfigurationAction(
                "planning",
                "ollama-cloud",
                "ollama-test:cloud",
                "ollama-secret-123",
                false));
            var ollamaResponse = await router.DecideAsync(Request(store.CaptureRuntimeConfiguration().Revision, strategic: true));
            Assert.Equal("jev", ollamaStatus.RoutineProvider);
            Assert.Equal("ollama-cloud", ollamaStatus.PlanningProvider);
            Assert.Equal(DecisionProviderKind.LargeLanguageModel, ollamaResponse.Provider);
            Assert.Equal(new Uri("https://ollama.com/v1/chat/completions"), handler.LastUri);
            Assert.Equal("Bearer ollama-secret-123", handler.LastAuthorization);
            Assert.Contains(logger.Messages, message =>
                message.Contains("cognition_provider_call status=completed", StringComparison.Ordinal) &&
                message.Contains("provider=ollama-cloud", StringComparison.Ordinal) &&
                message.Contains("role=planning", StringComparison.Ordinal) &&
                message.Contains("candidate=safe_idle", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message =>
                message.Contains("jev-secret-123", StringComparison.Ordinal) ||
                message.Contains("openai-secret-123", StringComparison.Ordinal) ||
                message.Contains("ollama-secret-123", StringComparison.Ordinal));

            var forgotten = store.Configure(new OwnerProviderConfigurationAction(
                "planning",
                "ollama-cloud",
                "ollama-test:cloud",
                null,
                true));
            Assert.Equal("jev", forgotten.RoutineProvider);
            Assert.Equal("deterministic", forgotten.PlanningProvider);
            Assert.False(forgotten.Providers.Single(item => item.Provider == "ollama-cloud").HasCredential);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void StatePersistsButStatusNeverReturnsSecretsAndExistingStateWinsOverEnvironmentSeed()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-provider-restart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        const string secret = "persisted-player-secret";
        try
        {
            var path = System.IO.Path.Combine(directory, "providers.json");
            var first = new ProviderConfigurationStore(path, EmptySeed());
            _ = first.Configure(new OwnerProviderConfigurationAction(
                "routine",
                "jev",
                "jev-player-model",
                secret,
                false));

            var restarted = new ProviderConfigurationStore(
                path,
                new ProviderConfigurationSeed(
                    "openai",
                    null,
                    "replacement-jev-secret",
                    "replacement-openai-model",
                    "replacement-openai-secret",
                    null,
                    null));
            var runtime = restarted.CaptureRuntimeConfiguration();
            var statusJson = System.Text.Json.JsonSerializer.Serialize(restarted.CaptureStatus());

            Assert.Equal("jev", runtime.RoutineProvider);
            Assert.Equal("deterministic", runtime.PlanningProvider);
            Assert.Equal("jev-player-model", runtime.Jev.Model);
            Assert.Equal(secret, runtime.Jev.ApiKey);
            Assert.DoesNotContain(secret, statusJson, StringComparison.Ordinal);
            Assert.DoesNotContain("replacement-openai-secret", File.ReadAllText(path), StringComparison.Ordinal);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(path));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProviderFailureLogKeepsExceptionAndCredentialDetailsOut()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-provider-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ProviderConfigurationStore(
                System.IO.Path.Combine(directory, "providers.json"),
                EmptySeed());
            const string apiKey = "failure-path-api-secret";
            const string providerBody = "failure-path-provider-body-secret";
            _ = store.Configure(new OwnerProviderConfigurationAction(
                "routine",
                "jev",
                "jev-test",
                apiKey,
                false));
            var logger = new RecordingLogger<ConfigurableDecisionProvider>();
            var router = new ConfigurableDecisionProvider(
                store,
                new FixedHttpClientFactory(new ThrowingProviderHandler(providerBody)),
                logger);

            _ = await Assert.ThrowsAsync<HttpRequestException>(async () =>
                await router.DecideAsync(Request(store.CaptureRuntimeConfiguration().Revision)));

            Assert.Contains(logger.Messages, message =>
                message.Contains("cognition_provider_call status=failed", StringComparison.Ordinal) &&
                message.Contains("error_type=HttpRequestException", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message =>
                message.Contains(apiKey, StringComparison.Ordinal) ||
                message.Contains(providerBody, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ProviderConfigurationSeed EmptySeed() => new(
        "deterministic",
        null,
        null,
        null,
        null,
        null,
        null);

    private static CognitionDecisionRequest Request(long epoch, bool strategic = false)
    {
        var observation = RequestObservation(strategic);
        return new CognitionDecisionRequest("request-provider-test", epoch, observation);
    }

    private static InhabitantObservation RequestObservation(bool strategic)
    {
        CognitionCandidate[] candidates = strategic
            ? [new CognitionCandidate("build:building:shelter", "Build a shelter.", 20), new CognitionCandidate("safe_idle", "Wait safely.", 100)]
            : [new CognitionCandidate("safe_idle", "Wait safely.", 100)];
        return new InhabitantObservation(
            "inhabitant-test",
            12,
            0,
            3,
            "sha256:provider-test",
            5_000,
            candidates);
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ProviderResponseHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastUri { get; private set; }

        public string? LastAuthorization { get; private set; }
        public string? LastModel { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            using var payload = System.Text.Json.JsonDocument.Parse(LastBody!);
            LastModel = payload.RootElement.TryGetProperty("model", out var model) ? model.GetString() : null;
            var isJev = string.Equals(request.RequestUri?.Host, "api.typesafe.ai", StringComparison.Ordinal);
            var body = isJev
                ? """
                  {"model":"jev-test","answers":{"selected_candidate":{"type":"choice","choice":"safe_idle","probabilities":{"safe_idle":1.0},"confidence":1.0},"memory_salience_00":{"type":"score","score":1.8,"confidence":0.75}}}
                  """
                : """
                  {"model":"hosted-test","choices":[{"message":{"role":"assistant","content":"{\"selected_candidate_id\":\"safe_idle\",\"confidence\":1.0,\"probabilities\":{\"safe_idle\":1.0}}"}}]}
                  """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            };
        }
    }

    private sealed class ThrowingProviderHandler(string message) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException(message);
    }
}

public sealed class WindowsCredentialFactAttribute : FactAttribute
{
    public WindowsCredentialFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires native Windows current-user protection.";
    }
}
