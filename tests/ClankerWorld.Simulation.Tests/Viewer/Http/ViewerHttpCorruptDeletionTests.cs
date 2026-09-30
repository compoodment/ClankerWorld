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
    public void PendingWorldDeletionWithCorruptOrphanDoesNotPreventHostRestart()
    {
        var directory = Directory.CreateTempSubdirectory("corrupt-deletion-restart-");
        try
        {
            string activeWorld;
            string targetId;
            string corruptPath;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = host.CreateClient())
            {
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                runtime.Pause();
                activeWorld = runtime.Society.WorldId;
                var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
                file.Save(runtime);
                var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
                using var other = new PrivateWorldRuntime("corrupt-deletion-other");
                other.Pause();
                targetId = catalog.Add("Delete", other.ExportState()).Id;
                corruptPath = WriteCorruptOrphan(file.Path);
                var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
                Assert.Throws<InvalidDataException>(() =>
                    catalog.Delete(targetId, other.Society.WorldId, saves.DeleteWorldSnapshots));
            }
            using (var restarted = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = restarted.CreateClient())
            {
                var catalog = restarted.Services.GetRequiredService<WorldCatalogStore>();
                Assert.Equal(activeWorld, catalog.Active().WorldId);
                Assert.DoesNotContain(catalog.Capture().Worlds, world => world.Id == targetId);
                Assert.Equal("{}", File.ReadAllText(corruptPath));
                var runtime = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
                Assert.Equal(activeWorld, runtime.Society.WorldId);
                runtime.Validate();
                restarted.Services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
            }
            // Repair only the disposable orphan, then retry recovery naturally.
            using var repaired = new PrivateWorldRuntime("unrelated-orphan");
            File.WriteAllBytes(corruptPath, PrivateWorldRuntimeCodec.Encode(repaired.ExportState()));
            using var recovered = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var recoveredClient = recovered.CreateClient();
            Assert.Equal(activeWorld, recovered.Services.GetRequiredService<WorldCatalogStore>().Active().WorldId);
            var activePath = recovered.Services.GetRequiredService<PrivateWorldStateFile>().Path;
            Assert.False(File.Exists(Path.Combine(activePath + ".worlds", targetId + ".save")));
            Assert.True(File.Exists(corruptPath));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("world")]
    [InlineData("save")]
    public async Task SignedDeletionReportsCorruptCleanupWithoutLosingOtherFiles(string kind)
    {
        var directory = Directory.CreateTempSubdirectory("corrupt-deletion-response-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            file.Save(runtime);
            var activeBytes = File.ReadAllBytes(file.Path);
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var keep = saves.Create("Keep", runtime, []);
            var keepPath = Path.Combine(file.Path + ".manual", keep.Id + ".save");
            var keepBytes = File.ReadAllBytes(keepPath);
            OwnerDeletionAction action;
            if (kind == "world")
            {
                using var other = new PrivateWorldRuntime("corrupt-delete-target");
                other.Pause();
                var entry = host.Services.GetRequiredService<WorldCatalogStore>().Add("Delete", other.ExportState());
                action = new("world", entry.Id, entry.WorldId);
            }
            else
            {
                var selected = saves.Create("Delete", runtime, []);
                action = new("save", selected.Id, runtime.Society.WorldId, selected.CreatedUtc);
                Directory.CreateDirectory(file.Path + ".history");
            }
            var corruptPath = WriteCorruptOrphan(file.Path);
            using var result = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/delete", action, OwnerHttpBinding.Deletion(action));
            Assert.Equal(kind == "world" ? HttpStatusCode.Conflict : HttpStatusCode.OK, result.StatusCode);
            if (kind == "save")
            {
                Assert.False((await result.Content.ReadFromJsonAsync<OwnerDeletionReceipt>())!.CleanupComplete);
                Assert.Throws<FileNotFoundException>(() => saves.Read(action.Id));
            }
            Assert.Equal("{}", File.ReadAllText(corruptPath));
            Assert.Equal(activeBytes, File.ReadAllBytes(file.Path));
            Assert.Equal(keepBytes, File.ReadAllBytes(keepPath));
            Assert.Equal(runtime.Society.WorldId, saves.Read(keep.Id).Society.Society.WorldId);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static string WriteCorruptOrphan(string activePath)
    {
        Directory.CreateDirectory(activePath + ".manual");
        var path = Path.Combine(activePath + ".manual", Guid.NewGuid().ToString("N") + ".save");
        File.WriteAllText(path, "{}");
        return path;
    }
}
