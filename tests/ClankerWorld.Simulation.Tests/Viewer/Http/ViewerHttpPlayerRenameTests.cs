using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using Client = ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData("Élodie Vale")]
    [InlineData("  e\u0301LODIE\u00a0  Vale  ")]
    public async Task SignedPlayerRenameExplainsCollisionAndAllowsAnotherName(string duplicate)
    {
        var directory = Directory.CreateTempSubdirectory("player-rename-http-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var signer = new ActionCompatibilitySigner(key);
            var device = await StartAndActivateAsync(host, client, key);
            var identity = host.Services.GetRequiredService<OwnerAuthorityStore>().Identity;
            var authority = new ClankerWorld.GodotClient.Pairing.OwnerAuthorityIdentity(identity.ServerAuthorityId, identity.WorldId);
            var api = new Client.OwnerWorldApi(client);
            var uri = new UriBuilder(client.BaseAddress!) { Host = "127.0.0.1" }.Uri;
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            const string first = "founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            const string second = "founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            runtime.PlaceFounder(first, new(0, 0));
            runtime.PlaceFounder(second, new(1, 2));
            await api.RenameAgentAsync(uri, authority, device.DeviceId,
                new Client.OwnerAgentRenameAction(first, "Élodie Vale"), signer, default);
            var before = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            var file = host.Services.GetRequiredService<PrivateWorldStateFile>();
            var diskBefore = File.ReadAllBytes(file.Path);
            var failure = await Assert.ThrowsAsync<OwnerAgentNameTakenException>(() => api.RenameAgentAsync(
                uri, authority, device.DeviceId, new Client.OwnerAgentRenameAction(second, duplicate), signer, default));
            Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
            Assert.Contains("another agent", Client.GameUiText.FriendlyFailure(failure), StringComparison.Ordinal);
            Assert.Contains("Choose a different name", Client.GameUiText.FriendlyFailure(failure), StringComparison.Ordinal);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Equal(diskBefore, File.ReadAllBytes(file.Path));
            var renamed = await api.RenameAgentAsync(uri, authority, device.DeviceId,
                new Client.OwnerAgentRenameAction(second, "Élodie Lake"), signer, default);
            Assert.True(renamed.Changed);
            var retry = await api.RenameAgentAsync(uri, authority, device.DeviceId,
                new Client.OwnerAgentRenameAction(second, "Élodie Lake"), signer, default);
            Assert.False(retry.Changed);
            using var restored = file.LoadOrCreate(runtime.ExportState().WorldSeed);
            Assert.Equal("Élodie Lake", restored.Society.GetInhabitant(second).Name);
            Assert.Equal(second, restored.Society.GetInhabitant(second).Id);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task ConcurrentSignedRenamesCannotClaimTheSameFullName()
    {
        var directory = Directory.CreateTempSubdirectory("player-rename-race-");
        try
        {
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
            using var client = host.CreateClient();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            var ids = new[] { "founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" };
            runtime.PlaceFounder(ids[0], new(0, 0));
            runtime.PlaceFounder(ids[1], new(1, 2));
            const string path = "/api/v1/owner/agents/rename";
            var envelopes = new List<OwnerSignedHttpRequest<OwnerAgentRenameAction>>();
            foreach (var id in ids)
            {
                var action = new OwnerAgentRenameAction(id, "Shared Name");
                envelopes.Add(await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                    path, action, OwnerHttpBinding.AgentRenamePayload(action)));
            }
            var responses = await Task.WhenAll(envelopes.Select(envelope => client.PostAsJsonAsync(path, envelope)));
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
                var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
                Assert.Equal("name_taken", (await rejected.Content.ReadFromJsonAsync<OwnerControlFailure>())!.Code);
                Assert.Single(runtime.Society.Inhabitants, person => person.Name == "Shared Name");
                using var restored = host.Services.GetRequiredService<PrivateWorldStateFile>()
                    .LoadOrCreate(runtime.ExportState().WorldSeed);
                Assert.Single(restored.Society.Inhabitants, person => person.Name == "Shared Name");
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally { directory.Delete(recursive: true); }
    }
}
