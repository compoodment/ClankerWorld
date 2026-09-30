using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData("society", false)]
    [InlineData("society", true)]
    [InlineData("society.society", false)]
    [InlineData("society.society", true)]
    [InlineData("society.cognition", false)]
    [InlineData("society.cognition", true)]
    [InlineData("society.society.inventory", false)]
    [InlineData("society.society.inventory", true)]
    public async Task MissingCheckpointStructureDoesNotHideHealthyWorldsOrReplaceActiveWorld(string member, bool omitted)
    {
        var directory = Directory.CreateTempSubdirectory("damaged-world-structure-");
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
            var runtimeBytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var catalog = host.Services.GetRequiredService<WorldCatalogStore>();
            using var other = new PrivateWorldRuntime("damaged-structure-other");
            other.Pause();
            var entry = catalog.Add("Recoverable", other.ExportState());
            var path = Path.Combine(file.Path + ".worlds", entry.Id + ".save");
            var healthy = File.ReadAllBytes(path);
            var document = JsonNode.Parse(healthy)!.AsObject();
            var parts = member.Split('.');
            var parent = document["state"]!.AsObject();
            foreach (var part in parts[..^1]) parent = parent[part]!.AsObject();
            if (omitted) parent.Remove(parts[^1]);
            else parent[parts[^1]] = null;
            var damaged = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString());
            File.WriteAllBytes(path, damaged);
            using var listed = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/worlds/list", new OwnerControlAction("list-worlds"), OwnerHttpBinding.EmptyPayload("list-worlds"));
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var snapshot = (await listed.Content.ReadFromJsonAsync<WorldCatalogSnapshot>())!;
            Assert.Equal("compatible", snapshot.Worlds.Single(world => world.Id == snapshot.ActiveId).Compatibility);
            Assert.Equal("incompatible", snapshot.Worlds.Single(world => world.Id == entry.Id).Compatibility);
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(damaged));
            var select = new OwnerManualSaveAction("select-world", entry.Id);
            using var rejected = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/worlds/select", select, OwnerHttpBinding.ManualSavePayload(select));
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal(activeBytes, File.ReadAllBytes(file.Path));
            Assert.Equal(runtimeBytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(damaged, File.ReadAllBytes(path));
            Assert.Equal(snapshot.ActiveId, catalog.Capture().ActiveId);
            // Repair only the disposable inactive file; the existing catalog ID remains usable.
            File.WriteAllBytes(path, healthy);
            using var accepted = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/worlds/select", select, OwnerHttpBinding.ManualSavePayload(select));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal(entry.WorldId, runtime.Society.WorldId);
            runtime.Validate();
        }
        finally { directory.Delete(recursive: true); }
    }
}
