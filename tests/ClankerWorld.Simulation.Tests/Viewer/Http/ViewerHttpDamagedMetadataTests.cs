using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData("load", "null-save")]
    [InlineData("overwrite", "null-save")]
    [InlineData("load", "missing-save")]
    [InlineData("overwrite", "missing-save")]
    [InlineData("load", "invalid-json")]
    [InlineData("overwrite", "invalid-json")]
    [InlineData("load", "null-assignments")]
    [InlineData("overwrite", "null-assignments")]
    [InlineData("load", "null-assignment")]
    [InlineData("overwrite", "null-assignment")]
    [InlineData("load", "null-name")]
    [InlineData("overwrite", "null-name")]
    public async Task DamagedMetadataIsRejectedBeforeCreatingBackupsOrChangingWorld(string operation, string damage)
    {
        var directory = Directory.CreateTempSubdirectory("damaged-manual-metadata-");
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
            var before = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var saves = host.Services.GetRequiredService<ManualWorldSaveStore>();
            var save = saves.Create("Retained selection", runtime, []);
            var root = file.Path + ".manual";
            var metadataPath = Path.Combine(root, save.Id + ".meta.json");
            var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
            var healthyMetadata = metadata.ToJsonString();
            switch (damage)
            {
                case "null-save": metadata["Save"] = null; break;
                case "missing-save": metadata.Remove("Save"); break;
                case "null-assignments": metadata["Assignments"] = null; break;
                case "null-assignment": metadata["Assignments"] = new JsonArray((JsonNode?)null); break;
                case "null-name": metadata["Save"]!["Name"] = null; break;
            }
            var damagedText = damage == "invalid-json" ? "{" : metadata.ToJsonString();
            File.WriteAllText(metadataPath, damagedText, new UTF8Encoding(false));
            var filesBefore = Directory.GetFiles(root).ToDictionary(path => path, File.ReadAllBytes);
            var action = new OwnerManualSaveAction(operation, save.Id);
            using var rejected = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/" + operation, action, OwnerHttpBinding.ManualSavePayload(action));
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(activeBytes, File.ReadAllBytes(file.Path));
            Assert.Equal(filesBefore.Keys.Order(), Directory.GetFiles(root).Order());
            foreach (var (path, bytes) in filesBefore) Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.DoesNotContain(saves.List(runtime.Society.WorldId), item => item.Id == save.Id);
            // Repair the disposable metadata and prove the retained ID still works.
            File.WriteAllText(metadataPath, healthyMetadata, new UTF8Encoding(false));
            using var accepted = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/saves/" + operation, action, OwnerHttpBinding.ManualSavePayload(action));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            runtime.Validate();
        }
        finally { directory.Delete(recursive: true); }
    }
}
