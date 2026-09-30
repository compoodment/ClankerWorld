using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task DamagedUsageMeterKeepsHostReachableAndReportsBlockedAccountingToOwner()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-meter-startup-");
        try
        {
            var path = Path.Combine(directory.FullName, "provider-usage.json");
            const string damaged = "{private sk-secret-accounting";
            File.WriteAllText(path, damaged);
            using var host = new ViewerWebApplicationFactory(directory.FullName,
                configureProviderUsagePath: false);
            var log = new RecordingLogger<ViewerHttpTests>();
            using var configured = host.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
                logging.AddProvider(new RecordingLoggerProvider<ViewerHttpTests>(log))));
            using var client = configured.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(configured, client, key);
            using var response = await SendSignedAsync(configured, client, key, device.DeviceId,
                "/api/v1/owner/usage/status", new OwnerUsageStatusAction(), OwnerHttpBinding.UsageStatusPayload());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var status = await response.Content.ReadFromJsonAsync<ProviderUsageStatus>();
            Assert.NotNull(status);
            Assert.True(status.LimitReached);
            Assert.Equal(ProviderUsageStore.RecoveryMessage, status.AccountingError);
            Assert.DoesNotContain("sk-secret", await response.Content.ReadAsStringAsync());
            Assert.Equal(damaged, File.ReadAllText(path));
            Assert.Contains(log.Messages, message => message.Contains("provider_usage_unavailable outcome=blocked", StringComparison.Ordinal));
            Assert.DoesNotContain(log.Messages, message => message.Contains("sk-secret", StringComparison.Ordinal) ||
                message.Contains(path, StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void DefaultProviderUsageMeterUsesTheConfiguredPrivateStateDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-provider-usage-path-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName,
                configureProviderUsagePath: false);
            using var client = host.CreateClient();
            var usage = host.Services.GetRequiredService<ProviderUsageStore>();
            var ticket = usage.Begin("ollama-cloud", "test-model", "planning");
            usage.Finish(ticket, "failed");
            Assert.True(File.Exists(Path.Combine(directory.FullName, "provider-usage.json")));
            Assert.Equal(1, usage.Capture().Failed);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task SignedUsageCapPausesWorldPersistsAndRequiresOwnerAllowanceBeforeResume()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-usage-http-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            OwnerDevice device;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true))
            using (var client = host.CreateClient())
            {
                device = await StartAndActivateAsync(host, client, key);
                var cap = new ProviderUsageLimitAction(1);
                using var configured = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/usage/limit", cap, OwnerHttpBinding.UsageLimitPayload(cap));
                Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
                var usage = host.Services.GetRequiredService<ProviderUsageStore>();
                var ticket = usage.Begin("openai", "test-model", "planning");
                usage.Finish(ticket, "completed", 4, 2);
                Assert.True(host.Services.GetRequiredService<PrivateWorldRuntime>().Society.IsPaused);
                var statusAction = new OwnerUsageStatusAction();
                using var observed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/usage/status", statusAction, OwnerHttpBinding.UsageStatusPayload());
                Assert.Equal(HttpStatusCode.OK, observed.StatusCode);
                var meter = await observed.Content.ReadFromJsonAsync<ProviderUsageStatus>();
                Assert.True(meter!.LimitReached);
                Assert.Equal(1, meter.Attempts);
                Assert.Equal(4, meter.InputTokens);
                using var refused = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/control/resume", new OwnerControlAction("resume"),
                    OwnerHttpBinding.EmptyPayload("resume"));
                Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            }
            using (var restarted = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true))
            using (var client = restarted.CreateClient())
            {
                Assert.True(restarted.Services.GetRequiredService<PrivateWorldRuntime>().Society.IsPaused);
                var usage = restarted.Services.GetRequiredService<ProviderUsageStore>();
                var statusAction = new OwnerUsageStatusAction();
                using var response = await SendSignedAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/usage/status", statusAction, OwnerHttpBinding.UsageStatusPayload());
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var meter = await response.Content.ReadFromJsonAsync<ProviderUsageStatus>();
                Assert.Equal(1, meter!.Attempts);
                Assert.Equal(1, meter.AttemptLimit);
                using var refused = await SendSignedAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/control/resume", new OwnerControlAction("resume"),
                    OwnerHttpBinding.EmptyPayload("resume"));
                Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
                var grant = new ProviderUsageLimitAction(null, AdditionalCalls: 2);
                var signed = await CreateSignedRequestAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/usage/limit", grant, OwnerHttpBinding.UsageLimitPayload(grant));
                using var tampered = await client.PostAsJsonAsync("/api/v1/owner/usage/limit",
                    signed with { Action = grant with { AdditionalCalls = 100 } });
                Assert.False(tampered.IsSuccessStatusCode);
                Assert.Equal(1, usage.Capture().AttemptLimit);
                using var consent = await SendSignedAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/usage/limit", grant, OwnerHttpBinding.UsageLimitPayload(grant));
                Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
                using var resumed = await SendSignedAsync(restarted, client, key, device.DeviceId,
                    "/api/v1/owner/control/resume", new OwnerControlAction("resume"),
                    OwnerHttpBinding.EmptyPayload("resume"));
                Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
                Assert.False(restarted.Services.GetRequiredService<PrivateWorldRuntime>().Society.IsPaused);
                Assert.Equal(3, usage.Capture().AttemptLimit);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task PairedOwnerCanConfigureHostedCognitionWithoutEchoingOrSavingTheKeyInTheWorld()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-viewer-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        const string secret = "openai-player-secret-that-must-not-echo";
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            OwnerDevice pairedDevice;
            using (var host = new ViewerWebApplicationFactory(directory, null, privateWorld: true))
            using (var client = host.CreateClient())
            {
                pairedDevice = await StartAndActivateAsync(host, client, key);
                var configureAction = new OwnerProviderConfigurationAction(
                    "planning",
                    "openai",
                    "test-openai-model",
                    secret,
                    ForgetCredential: false);
                using var configure = await SendSignedAsync(
                    host,
                    client,
                    key,
                    pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure",
                    configureAction,
                    OwnerHttpBinding.ProviderConfigurationPayload(configureAction));
                var responseText = await configure.Content.ReadAsStringAsync();
                var status = System.Text.Json.JsonSerializer.Deserialize<OwnerProviderConfigurationStatus>(
                    responseText,
                    WebJsonOptions);

                Assert.Equal(HttpStatusCode.OK, configure.StatusCode);
                Assert.NotNull(status);
                Assert.Equal("deterministic", status.RoutineProvider);
                Assert.Equal("openai", status.PlanningProvider);
                Assert.True(status.Providers.Single(item => item.Provider == "openai").HasCredential);
                Assert.DoesNotContain(secret, responseText, StringComparison.Ordinal);

                var personal = new OwnerProviderConfigurationAction("planning", "deterministic", null, null, false, "founder-scout");
                using var personalResponse = await SendSignedAsync(host, client, key, pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure", personal, OwnerHttpBinding.ProviderConfigurationPayload(personal));
                Assert.Equal(HttpStatusCode.OK, personalResponse.StatusCode);
                var personalStatus = await personalResponse.Content.ReadFromJsonAsync<OwnerProviderConfigurationStatus>();
                Assert.Equal("founder-scout", Assert.Single(personalStatus!.Assignments!).InhabitantId);
                Assert.Equal("openai", personalStatus.PlanningProvider);

                var tampered = personal with { InhabitantId = "founder-rowan" };
                using var tamperedResponse = await SendSignedAsync(host, client, key, pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure", tampered, OwnerHttpBinding.ProviderConfigurationPayload(personal));
                Assert.False(tamperedResponse.IsSuccessStatusCode);
                Assert.Equal(personalStatus.Revision, host.Services.GetRequiredService<ProviderConfigurationStore>().CaptureStatus().Revision);

                var missing = personal with { InhabitantId = "not-an-inhabitant" };
                using var missingResponse = await SendSignedAsync(host, client, key, pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure", missing, OwnerHttpBinding.ProviderConfigurationPayload(missing));
                Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
                Assert.Equal(personalStatus.Revision, host.Services.GetRequiredService<ProviderConfigurationStore>().CaptureStatus().Revision);

                var providerPath = host.Services.GetRequiredService<ProviderConfigurationStore>().Path;
                if (OperatingSystem.IsWindows())
                {
                    Assert.DoesNotContain(secret, File.ReadAllText(providerPath), StringComparison.Ordinal);
                }
                else
                {
                    Assert.Contains(secret, File.ReadAllText(providerPath), StringComparison.Ordinal);
                }
                var reloadedProviders = new ProviderConfigurationStore(providerPath,
                    new("deterministic", null, null, null, null, null, null));
                Assert.Equal(secret, reloadedProviders.CaptureRuntimeConfiguration().OpenAi.ApiKey);
                foreach (var file in Directory.EnumerateFiles(directory).Where(path => path != providerPath))
                {
                    Assert.DoesNotContain(secret, File.ReadAllText(file), StringComparison.Ordinal);
                }

                if (!OperatingSystem.IsWindows())
                {
                    var mode = File.GetUnixFileMode(providerPath);
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
                }
            }

            using (var restartedHost = new ViewerWebApplicationFactory(directory, null, privateWorld: true))
            using (var restartedClient = restartedHost.CreateClient())
            {
                var statusAction = new OwnerProviderStatusAction();
                using var statusResponse = await SendSignedAsync(
                    restartedHost,
                    restartedClient,
                    key,
                    pairedDevice.DeviceId,
                    "/api/v1/owner/providers/status",
                    statusAction,
                    OwnerHttpBinding.ProviderStatusPayload());
                var restored = await statusResponse.Content.ReadFromJsonAsync<OwnerProviderConfigurationStatus>();
                Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
                Assert.Equal("openai", restored!.PlanningProvider);

                var forgetAction = new OwnerProviderConfigurationAction(
                    "planning",
                    "openai",
                    "test-openai-model",
                    null,
                    ForgetCredential: true);
                using var forgottenResponse = await SendSignedAsync(
                    restartedHost,
                    restartedClient,
                    key,
                    pairedDevice.DeviceId,
                    "/api/v1/owner/providers/configure",
                    forgetAction,
                    OwnerHttpBinding.ProviderConfigurationPayload(forgetAction));
                var forgotten = await forgottenResponse.Content.ReadFromJsonAsync<OwnerProviderConfigurationStatus>();
                Assert.Equal(HttpStatusCode.OK, forgottenResponse.StatusCode);
                Assert.Equal("deterministic", forgotten!.PlanningProvider);
                Assert.False(forgotten.Providers.Single(item => item.Provider == "openai").HasCredential);
                Assert.DoesNotContain(
                    secret,
                    File.ReadAllText(restartedHost.Services.GetRequiredService<ProviderConfigurationStore>().Path),
                    StringComparison.Ordinal);
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
    public async Task OnlySignedOwnerCanDeleteUnusedNamedCredentialSlot()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-slot-http-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);
            var store = host.Services.GetRequiredService<ProviderConfigurationStore>();
            var slotId = Guid.NewGuid().ToString("N");
            const string secret = "delete-me-secret";
            _ = store.Configure(new("personal", "openai", "model", secret, false,
                "inhabitant-test", slotId, "Discard"));
            var action = new OwnerCredentialSlotDeletionAction(slotId);
            const string endpoint = "/api/v1/owner/providers/slots/delete";
            using var tampered = await SendSignedAsync(host, client, key, device.DeviceId,
                endpoint, action, OwnerHttpBinding.CredentialSlotDeletionPayload(
                    action with { CredentialSlotId = Guid.NewGuid().ToString("N") }));
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
            if (OperatingSystem.IsWindows())
            {
                Assert.DoesNotContain(secret, File.ReadAllText(store.Path), StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(secret, File.ReadAllText(store.Path), StringComparison.Ordinal);
            }
            var reloadedProviders = new ProviderConfigurationStore(store.Path,
                new("deterministic", null, null, null, null, null, null));
            Assert.Equal(secret, Assert.Single(reloadedProviders.CaptureRuntimeConfiguration().CredentialSlots!).ApiKey);

            using var assigned = await SendSignedAsync(host, client, key, device.DeviceId,
                endpoint, action, OwnerHttpBinding.CredentialSlotDeletionPayload(action));
            Assert.Equal(HttpStatusCode.Conflict, assigned.StatusCode);
            Assert.DoesNotContain(secret, await assigned.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            _ = store.Configure(new("personal", "inherit", null, null, false, "inhabitant-test"));
            using var removed = await SendSignedAsync(host, client, key, device.DeviceId,
                endpoint, action, OwnerHttpBinding.CredentialSlotDeletionPayload(action));
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            var status = await removed.Content.ReadFromJsonAsync<OwnerProviderConfigurationStatus>();
            Assert.Empty(status!.CredentialSlots!);
            Assert.DoesNotContain(secret, File.ReadAllText(store.Path), StringComparison.Ordinal);
            Assert.DoesNotContain(secret, await removed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            var afterDeletion = new ProviderConfigurationStore(store.Path,
                new("deterministic", null, null, null, null, null, null));
            Assert.Empty(afterDeletion.CaptureRuntimeConfiguration().CredentialSlots!);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task OnlySignedOwnerCanListProviderModelsAndThePastedKeyIsBoundByDigest()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-model-list-http-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true);
            using var client = host.CreateClient();
            var device = await StartAndActivateAsync(host, client, key);
            const string endpoint = "/api/v1/owner/providers/models";

            var pasted = new OwnerProviderModelListAction("openai", ApiKey: "pasted-list-secret");
            Assert.DoesNotContain("pasted-list-secret", OwnerHttpBinding.ProviderModelListPayload(pasted), StringComparison.Ordinal);
            using var tampered = await SendSignedAsync(host, client, key, device.DeviceId, endpoint, pasted,
                OwnerHttpBinding.ProviderModelListPayload(pasted with { ApiKey = "other-secret" }));
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);

            var saved = new OwnerProviderModelListAction("ollama-cloud");
            using var missing = await SendSignedAsync(host, client, key, device.DeviceId, endpoint, saved,
                OwnerHttpBinding.ProviderModelListPayload(saved));
            Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
            var list = await missing.Content.ReadFromJsonAsync<OwnerProviderModelList>();
            Assert.Equal("Add an API key for Ollama Cloud first.", list!.Error);
            Assert.Equal(PlayerDecisionProviders.DefaultOllamaCloudModel, list.DefaultModel);
            Assert.Equal(ProviderModelCatalog.Curated[PlayerDecisionProviders.OllamaCloud], list.Models.Select(item => item.Model));

            var listOnly = saved with { CheckKey = false };
            using var tamperedCheck = await SendSignedAsync(host, client, key, device.DeviceId, endpoint, listOnly,
                OwnerHttpBinding.ProviderModelListPayload(saved));
            Assert.Equal(HttpStatusCode.Unauthorized, tamperedCheck.StatusCode);

            var jev = new OwnerProviderModelListAction("jev");
            using var unsupported = await SendSignedAsync(host, client, key, device.DeviceId, endpoint, jev,
                OwnerHttpBinding.ProviderModelListPayload(jev));
            Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
