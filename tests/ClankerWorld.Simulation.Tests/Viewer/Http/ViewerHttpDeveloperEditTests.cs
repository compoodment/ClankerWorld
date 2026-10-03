using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using ClientAction = ClankerWorld.GodotClient.UI.OwnerDeveloperEditAction;
using ClientPayload = ClankerWorld.GodotClient.UI.OwnerWorldActionPayload;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task DeveloperEditsBindEveryFieldAndPersistOnlyAuthenticatedCurrentWorldChanges()
    {
        var directory = Directory.CreateTempSubdirectory("developer-http-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            runtime.Pause();
            var command = DeveloperEditTests.Edit(runtime, "give_goods", "wood", 2);
            var action = new OwnerDeveloperEditAction(command.WorldId, command.ExpectedEventId, command.AgentId,
                command.Operation, command.Value, command.Amount, command.OtherAgentId);
            const string path = "/api/v1/owner/developer-edit";
            var before = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            foreach (var changed in new[]
            {
                action with { WorldId = "other" }, action with { ExpectedEventId = action.ExpectedEventId + 1 },
                action with { AgentId = "other" }, action with { Operation = "remove_goods" },
                action with { Value = "berries" }, action with { Amount = 1 }, action with { OtherAgentId = "other" },
            })
            {
                var envelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, action,
                    OwnerHttpBinding.DeveloperEditPayload(action));
                using var tampered = await client.PostAsJsonAsync(path, envelope with { Action = changed });
                Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
                Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            }
            var wrongWorld = action with { WorldId = "other" };
            using var refused = await SendSignedAsync(host, client, key, device.DeviceId, path, wrongWorld,
                OwnerHttpBinding.DeveloperEditPayload(wrongWorld));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            // Use the client's canonical payload to exercise the protocol boundary.
            var payload = ClientPayload.DeveloperEdit(new ClientAction(action.WorldId, action.ExpectedEventId,
                action.AgentId, action.Operation, action.Value, action.Amount, action.OtherAgentId));
            Assert.Equal(OwnerHttpBinding.DeveloperEditPayload(action), payload);
            using var accepted = await SendSignedAsync(host, client, key, device.DeviceId, path, action, payload);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.True((await accepted.Content.ReadFromJsonAsync<OwnerControlReceipt>())!.Changed);
            var bytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            Assert.Equal(bytes, File.ReadAllBytes(file.Path));
            using var retry = await SendSignedAsync(host, client, key, device.DeviceId, path, action, payload);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.False((await retry.Content.ReadFromJsonAsync<OwnerControlReceipt>())!.Changed);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Single(runtime.ExportState().Events, item => item.Kind == "developer_edit");
        }
        finally { directory.Delete(true); }
    }
}
