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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedSnapshotDeletionPreservesOtherSavesAndRejectsStaleTargets(bool automatic)
    {
        var directory = Directory.CreateTempSubdirectory("snapshot-delete-");
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
            var before = File.ReadAllBytes(stateFile.Path);
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var settings = host.Services.GetRequiredService<WorldAutosaveStore>().Capture();
            var selected = automatic ? saves.CreateAutosave(runtime, [], settings) : saves.Create("Selected", runtime, []);
            var keep = saves.Create("Keep", runtime, []);
            if (!automatic)
            {
                var old = selected;
                var overwrite = saves.Overwrite(selected.Id, runtime, []);
                selected = overwrite.Saved;
                using var stale = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/delete", new OwnerDeletionAction("save", old.Id, runtime.Society.WorldId, old.CreatedUtc),
                    OwnerHttpBinding.Deletion(new OwnerDeletionAction("save", old.Id, runtime.Society.WorldId, old.CreatedUtc)));
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                Assert.NotNull(saves.Read(overwrite.BackupId));
            }
            var action = new OwnerDeletionAction("save", selected.Id, runtime.Society.WorldId, selected.CreatedUtc);
            Assert.Equal(OwnerHttpBinding.Deletion(action), ClankerWorld.GodotClient.UI.OwnerWorldActionPayload.Deletion(
                new ClankerWorld.GodotClient.UI.OwnerDeletionAction(action.Kind, action.Id, action.WorldId, action.ExpectedCreatedUtc)));
            var signed = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/delete", action, OwnerHttpBinding.Deletion(action));
            using var tampered = await client.PostAsJsonAsync("/api/v1/owner/delete", signed with { Action = action with { Id = keep.Id } });
            Assert.False(tampered.IsSuccessStatusCode);
            var wrong = action with { WorldId = "another-world" };
            using var refused = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/delete", wrong, OwnerHttpBinding.Deletion(wrong));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            using var deleted = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/delete", action, OwnerHttpBinding.Deletion(action));
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            Assert.True((await deleted.Content.ReadFromJsonAsync<OwnerDeletionReceipt>())!.CleanupComplete);
            Assert.DoesNotContain(saves.List(), save => save.Id == selected.Id);
            Assert.Throws<FileNotFoundException>(() => saves.Read(selected.Id));
            Assert.Empty(Directory.GetFiles(stateFile.Path + ".manual", selected.Id + ".*"));
            Assert.NotNull(saves.Read(keep.Id));
            Assert.Equal(before, File.ReadAllBytes(stateFile.Path));
            Assert.Equal(settings, host.Services.GetRequiredService<WorldAutosaveStore>().Capture());
            Assert.NotNull(saves.CreateAutosave(runtime, [], settings));
            using var repeated = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/delete", action, OwnerHttpBinding.Deletion(action));
            Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task WorldDeletionRecoversInterruptedCleanupWithoutRevivingTarget()
    {
        var directory = Directory.CreateTempSubdirectory("world-delete-");
        try
        {
            string worldId;
            string catalogId;
            string snapshotId;
            string activeWorld;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = host.CreateClient())
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var device = await StartAndActivateAsync(host, client, key);
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                runtime.Pause();
                activeWorld = runtime.Society.WorldId;
                var stateFile = host.Services.GetRequiredService<PrivateWorldStateFile>();
                stateFile.Save(runtime);
                var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
                var active = catalog.Active();
                var block = new OwnerDeletionAction("world", active.Id, active.WorldId);
                using var blocked = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/delete", block, OwnerHttpBinding.Deletion(block));
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
                using var other = new PrivateWorldRuntime("delete-other");
                other.Pause();
                worldId = other.Society.WorldId;
                catalogId = catalog.Add("Remove", other.ExportState()).Id;
                var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
                snapshotId = saves.Create("Remove too", other, []).Id;
                var target = Path.Combine(stateFile.Path + ".manual", snapshotId + ".save");
                var bytes = File.ReadAllBytes(target);
                File.Delete(target);
                Directory.CreateDirectory(target);
                var action = new OwnerDeletionAction("world", catalogId, worldId);
                using var failed = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/delete", action, OwnerHttpBinding.Deletion(action));
                Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
                Assert.DoesNotContain(catalog.Capture().Worlds, world => world.Id == catalogId);
                Assert.DoesNotContain(saves.List(), save => save.Id == snapshotId);
                Assert.Throws<FileNotFoundException>(() => catalog.Read(catalogId));
                Assert.Throws<InvalidOperationException>(() => catalog.Add("Do not revive", other.ExportState()));
                Directory.Delete(target);
                File.WriteAllBytes(target, bytes);
            }
            using var restarted = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var restartedClient = restarted.CreateClient();
            var reopened = restarted.Services.GetRequiredService<WorldCatalogStore>();
            Assert.Single(reopened.Capture().Worlds);
            Assert.Equal(activeWorld, reopened.Active().WorldId);
            var path = restarted.Services.GetRequiredService<PrivateWorldStateFile>().Path;
            Assert.False(File.Exists(Path.Combine(path + ".worlds", catalogId + ".save")));
            Assert.Empty(Directory.GetFiles(path + ".manual", snapshotId + ".*"));
            Assert.Empty(restarted.Services.GetRequiredService<ManualWorldSaveStore>().List(worldId));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task DeletionReclaimsOnlyHistoryNotReferencedByAnyRemainingCheckpoint()
    {
        var directory = Directory.CreateTempSubdirectory("history-delete-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var stateFile = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            using var archived = PrivateWorldRuntime.Restore(runtime.ExportState());
            for (var n = 0; n < PrivateWorldHistory.CompactionThreshold + 2; n++) archived.SetJevEnabled(!archived.JevEnabled);
            stateFile.Save(archived);
            var head = archived.ExportState().HistoryArchiveHead;
            Assert.NotNull(head);
            var first = saves.Create("Shared one", archived, []);
            var second = saves.Create("Shared two", archived, []);
            stateFile.Save(runtime);
            var history = Path.Combine(stateFile.Path + ".history", head + ".json");
            foreach (var save in new[] { first, second })
            {
                var action = new OwnerDeletionAction("save", save.Id, runtime.Society.WorldId, save.CreatedUtc);
                using var result = await SendSignedAsync(host, client, key, device.DeviceId,
                    "/api/v1/owner/delete", action, OwnerHttpBinding.Deletion(action));
                Assert.Equal(HttpStatusCode.OK, result.StatusCode);
                Assert.Equal(save == first, File.Exists(history));
                if (save == first) stateFile.VerifyRequiredHistory(saves.Read(second.Id));
            }
            using var restored = stateFile.LoadOrCreate(runtime.ExportState().WorldSeed);
            Assert.Equal(runtime.Society.WorldId, restored.Society.WorldId);
        }
        finally { directory.Delete(recursive: true); }
    }
}
