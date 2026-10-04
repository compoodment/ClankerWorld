using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Fact]
    public async Task OwnerReconnectShowsTheActualBlockedToolRequestAtItsBlacksmithAndOnlyItsParties()
    {
        var (state, buyer, seller, shop) = await ToolMakingRequestTests.BlockedAcceptedRequest();
        var request = Assert.Single(state.ToolMakingRequests!);
        var directory = Directory.CreateTempSubdirectory("clankerworld-request-http-");
        try
        {
            using (var world = PrivateWorldRuntime.Restore(state))
                _ = new PrivateWorldStateFile(Path.Combine(directory.FullName, "runtime.json")).Save(world);
            Assert.True(File.Exists(Path.Combine(directory.FullName, "runtime.json")));
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, client, key);
            var action = new OwnerReconnectAction(0);
            using var response = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/reconnect", action, OwnerHttpBinding.ReconnectPayload(action));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var view = (await response.Content.ReadFromJsonAsync<ViewerOwnerReconnect>())!;
            var building = Assert.Single(view.Baseline.Snapshot.PlacedBuildings, building => building.InstanceId == shop.InstanceId);
            var visible = Assert.Single(building.ToolMakingRequests);
            Assert.Equal(request.Id, visible.Id);
            Assert.Equal(state.Society.Society.GetInhabitant(buyer).Name, visible.RequesterName);
            Assert.Equal(request.RecipeId, visible.RecipeId);
            Assert.Equal(state.WorldContent!.Recipes.Single(recipe => recipe.CanonicalId == request.RecipeId).DisplayName, visible.RecipeName);
            Assert.Equal("wooden_axe", visible.ItemKind);
            Assert.Equal("accepted", visible.Status);
            Assert.Equal(request.Blocker, visible.Blocker);
            Assert.Null(visible.OfferId);
            Assert.Contains("wood", visible.Blocker!, StringComparison.Ordinal);
            Assert.Equal(ToolMakingRequestRules.Note(state.ToolMakingRequests!, buyer, state.Society.Society.GetInhabitant(buyer).HouseholdId),
                Assert.Single(view.Baseline.Snapshot.Inhabitants, person => person.Id == buyer).ToolMakingRequestNote);
            Assert.NotNull(Assert.Single(view.Baseline.Snapshot.Inhabitants, person => person.Id == seller).ToolMakingRequestNote);
            Assert.All(view.Baseline.Snapshot.Inhabitants.Where(person => person.Id != buyer &&
                state.Society.Society.GetInhabitant(person.Id).HouseholdId != shop.HouseholdId), person => Assert.Null(person.ToolMakingRequestNote));
        }
        finally { directory.Delete(recursive: true); }
    }
}
