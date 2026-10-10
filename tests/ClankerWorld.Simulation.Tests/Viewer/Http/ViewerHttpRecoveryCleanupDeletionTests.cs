using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task SignedLoadingCreatesCleanupEligibleRecoveryBeforeDeletion()
    {
        var directory = Directory.CreateTempSubdirectory("recovery-load-http-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var stateFile = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var named = saves.Create("Named", runtime, []);
            var originalEnabled = runtime.JevEnabled;
            for (var index = 0; index < 3; index++)
            {
                runtime.SetJevEnabled(!originalEnabled);
                var load = new OwnerManualSaveAction("load", named.Id);
                using var response = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/saves/load", load, OwnerHttpBinding.ManualSavePayload(load));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(originalEnabled, runtime.JevEnabled);
            }
            var preview = saves.PreviewRecoveryCleanup(runtime.Society.WorldId, 1, stateFile);
            Assert.Equal(2, preview.Remove.Count);
            Assert.All(preview.Remove, save => Assert.Equal("Before loading", save.Name));
            Assert.Equal(2, preview.Keep.Count);
            Assert.Contains(preview.Keep, save => save.Id == named.Id);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task RecoveryCleanupDeletionRequiresExactSignedPreviewAndPreservesManualSaves()
    {
        var directory = Directory.CreateTempSubdirectory("recovery-cleanup-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var stateFile = host.Services.GetRequiredService<PrivateWorldStateFile>();
            stateFile.Save(runtime);
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var selected = saves.Create("Selected", runtime, []);
            var deceptive = saves.Create("Before overwriting: Selected", runtime, []);
            var selectedBytes = PrivateWorldRuntimeCodec.Encode(saves.Read(selected.Id));
            var first = saves.Overwrite(selected.Id, runtime, []).BackupId;
            var second = saves.Overwrite(selected.Id, runtime, []).BackupId;
            var latest = saves.Overwrite(selected.Id, runtime, []).BackupId;
            var previewAction = new OwnerRecoveryCleanupAction("preview", runtime.Society.WorldId, 1);
            Assert.Equal(OwnerHttpBinding.RecoveryCleanup(previewAction),
                ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.RecoveryCleanup(
                    new("preview", previewAction.WorldId, 1)));
            using var response = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/recovery-cleanup", previewAction, OwnerHttpBinding.RecoveryCleanup(previewAction));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var preview = (await response.Content.ReadFromJsonAsync<RecoveryCleanupPreview>())!;
            Assert.Equal(new[] { first, second }.Order(StringComparer.Ordinal), preview.Remove.Select(save => save.Id));
            Assert.Contains(preview.Keep, save => save.Id == latest);
            Assert.Contains(preview.Keep, save => save.Id == selected.Id);
            Assert.Contains(preview.Keep, save => save.Id == deceptive.Id);
            var activeBytes = File.ReadAllBytes(stateFile.Path);
            var filesBeforeCancel = Directory.GetFiles(stateFile.Path + ".manual")
                .ToDictionary(path => path, File.ReadAllBytes);
            // Cancelling means no apply request. Reopening the preview is read-only.
            Assert.Equal(preview.Digest, saves.PreviewRecoveryCleanup(preview.WorldId, 1, stateFile).Digest);
            foreach (var (path, bytes) in filesBeforeCancel) Assert.Equal(bytes, File.ReadAllBytes(path));
            var apply = new OwnerRecoveryCleanupAction("apply", preview.WorldId, 1, preview.Digest);
            var signed = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/recovery-cleanup", apply, OwnerHttpBinding.RecoveryCleanup(apply));
            using var tampered = await client.PostAsJsonAsync("/api/v1/owner/saves/recovery-cleanup",
                signed with { Action = apply with { KeepCount = 2 } });
            Assert.False(tampered.IsSuccessStatusCode);
            var newer = saves.Overwrite(selected.Id, runtime, []).BackupId;
            var beforeStale = Directory.GetFiles(stateFile.Path + ".manual").ToDictionary(path => path, File.ReadAllBytes);
            using var stale = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/recovery-cleanup", apply, OwnerHttpBinding.RecoveryCleanup(apply));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            foreach (var (path, bytes) in beforeStale) Assert.Equal(bytes, File.ReadAllBytes(path));
            var fresh = saves.PreviewRecoveryCleanup(preview.WorldId, 1, stateFile);
            apply = apply with { ExpectedDigest = fresh.Digest };
            runtime.Resume();
            using var running = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/recovery-cleanup", apply, OwnerHttpBinding.RecoveryCleanup(apply));
            Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
            runtime.Pause();
            using var deleted = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/recovery-cleanup", apply, OwnerHttpBinding.RecoveryCleanup(apply));
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            var receipt = (await deleted.Content.ReadFromJsonAsync<OwnerRecoveryCleanupReceipt>())!;
            Assert.Equal(fresh.Remove.Select(save => save.Id), receipt.RemovedIds);
            Assert.True(receipt.CleanupComplete);
            Assert.Equal(selectedBytes, PrivateWorldRuntimeCodec.Encode(saves.Read(selected.Id)));
            Assert.NotNull(saves.Read(deceptive.Id));
            Assert.NotNull(saves.Read(newer));
            Assert.Equal(activeBytes, File.ReadAllBytes(stateFile.Path));
            foreach (var old in fresh.Remove) Assert.Empty(Directory.GetFiles(stateFile.Path + ".manual", old.Id + ".*"));
        }
        finally { directory.Delete(recursive: true); }
    }
}
