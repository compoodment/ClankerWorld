using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("world")]
    public void MissingDeletionIdentityDoesNotPreventHealthyHostRestart(string kind)
    {
        var directory = Directory.CreateTempSubdirectory("missing-deletion-identity-");
        try
        {
            string activeWorld;
            string activePath;
            string damagedPath;
            string statePath;
            string healthyMetadata;
            string damagedMetadata;
            string keepPath;
            byte[] keepBytes;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = host.CreateClient())
            {
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                runtime.Pause();
                activeWorld = runtime.Society.WorldId;
                var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
                file.Save(runtime);
                activePath = file.Path;
                var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
                var keep = saves.Create("Keep", runtime, []);
                keepPath = Path.Combine(file.Path + ".manual", keep.Id + ".save");
                keepBytes = File.ReadAllBytes(keepPath);
                using var other = new PrivateWorldRuntime("missing-delete-identity-other");
                other.Pause();
                var target = saves.Create("Delete", kind == "save" ? runtime : other, []);
                var metadataPath = Path.Combine(file.Path + ".manual", target.Id + ".meta.json");
                statePath = Path.Combine(file.Path + ".manual", target.Id + ".save");
                healthyMetadata = File.ReadAllText(metadataPath);
                if (kind == "save")
                {
                    // A disposable directory obstruction leaves the real deletion intent committed.
                    File.Move(statePath, statePath + ".held");
                    Directory.CreateDirectory(statePath);
                    var fault = Record.Exception(() => saves.Delete(target.Id, activeWorld, target.CreatedUtc));
                    Assert.True(fault is IOException or UnauthorizedAccessException);
                    damagedPath = Path.Combine(file.Path + ".manual", target.Id + ".deleting.json");
                    Assert.True(File.Exists(damagedPath));
                }
                else
                {
                    var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
                    var entry = catalog.Add("Delete", other.ExportState());
                    File.WriteAllText(metadataPath, "{");
                    Assert.Throws<JsonException>(() => catalog.Delete(entry.Id, other.Society.WorldId, saves.DeleteWorldSnapshots));
                    Assert.DoesNotContain(catalog.Capture().Worlds, world => world.Id == entry.Id);
                    damagedPath = metadataPath;
                }
                var metadata = JsonNode.Parse(healthyMetadata)!.AsObject();
                metadata["Save"] = null;
                damagedMetadata = metadata.ToJsonString();
                File.WriteAllText(damagedPath, damagedMetadata);
            }
            using (var restarted = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = restarted.CreateClient())
            {
                var runtime = restarted.Services.GetRequiredService<PrivateWorldRuntime>();
                Assert.Equal(activeWorld, runtime.Society.WorldId);
                runtime.Validate();
                Assert.Equal(damagedMetadata, File.ReadAllText(damagedPath));
                Assert.Equal(keepBytes, File.ReadAllBytes(keepPath));
                restarted.Services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
            }
            // Restore the identity, not playability: deletion needs neither a name nor model routing.
            var repaired = JsonNode.Parse(healthyMetadata)!.AsObject();
            repaired["Save"]!["Name"] = null;
            repaired["Assignments"] = null;
            File.WriteAllText(damagedPath, repaired.ToJsonString());
            if (kind == "save")
            {
                Directory.Delete(statePath);
                File.Move(statePath + ".held", statePath);
            }
            using var recovered = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var recoveredClient = recovered.CreateClient();
            Assert.Equal(activeWorld, recovered.Services.GetRequiredService<WorldCatalogStore>().Active().WorldId);
            Assert.False(File.Exists(statePath));
            Assert.False(File.Exists(damagedPath));
            Assert.Equal(keepBytes, File.ReadAllBytes(keepPath));
            Assert.Equal(activeWorld, PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(activePath)).Society.Society.WorldId);
        }
        finally { directory.Delete(recursive: true); }
    }
}
