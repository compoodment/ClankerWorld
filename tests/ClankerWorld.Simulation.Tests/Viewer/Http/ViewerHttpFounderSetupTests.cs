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
    public async Task NewPrivateWorldRequiresFourConfiguredFoundersAndAnExplicitSignedStart()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-founder-http-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var host = new ViewerWebApplicationFactory(directory.FullName, null, privateWorld: true,
                legacyPrivateWorld: false);
            using var client = host.CreateClient();
            var placementLog = new RecordingLogger<ViewerHttpTests>();
            host.Services.GetRequiredService<ILoggerFactory>().AddProvider(new RecordingLoggerProvider<ViewerHttpTests>(placementLog));
            var device = await StartAndActivateAsync(host, client, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.Empty(runtime.Inhabitants);
            using var premature = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/control/resume", new OwnerControlAction("resume"), OwnerHttpBinding.EmptyPayload("resume"));
            Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);

            var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
            var slotId = Guid.NewGuid().ToString("N");
            OwnerFounderMoveAction? firstFounderMove = null;
            for (var index = 0; index < positions.Length; index++)
            {
                var id = "founder:" + Guid.NewGuid().ToString("N");
                var cognition = new OwnerProviderConfigurationAction("personal", "openai", "gpt-5-mini",
                    index == 0 ? "test-secret-key" : null, false, id, slotId,
                    index == 0 ? "Test account" : null);
                var action = new OwnerFounderPlacementAction(id, positions[index].X, positions[index].Y, cognition);
                const string path = "/api/v1/owner/founders/place";
                if (index == 0)
                {
                    var envelope = await CreateSignedRequestAsync(host, client, key, device.DeviceId, path, action,
                        OwnerHttpBinding.FounderPlacementPayload(action));
                    using var tampered = await client.PostAsJsonAsync(path, envelope with
                    {
                        Action = action with { X = positions[index].X + 1 },
                    });
                    Assert.False(tampered.IsSuccessStatusCode);
                    Assert.Empty(runtime.Inhabitants);
                }
                using var placed = await SendSignedAsync(host, client, key, device.DeviceId, path, action,
                    OwnerHttpBinding.FounderPlacementPayload(action));
                Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
                var receipt = await placed.Content.ReadFromJsonAsync<OwnerFounderPlacementReceipt>();
                Assert.Equal(index + 1, receipt!.Placed);
                if (index == 0)
                {
                    var map = runtime.ExportState().Map;
                    var destination = map.Tiles.Select(tile => tile.Position).First(point =>
                        map.IsBuildable(point) && !positions.Contains(point) &&
                        !map.CampObjects.Any(item => item.Position == point) &&
                        !map.Resources.Any(item => item.Position == point));
                    firstFounderMove = new OwnerFounderMoveAction(id, destination.X, destination.Y);
                    const string movePath = "/api/v1/owner/founders/move";
                    var signedMove = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                        movePath, firstFounderMove, OwnerHttpBinding.FounderMovePayload(firstFounderMove));
                    using var tamperedMove = await client.PostAsJsonAsync(movePath, signedMove with
                    {
                        Action = firstFounderMove with { X = destination.X + 1 },
                    });
                    Assert.False(tamperedMove.IsSuccessStatusCode);
                    Assert.Equal(positions[0], runtime.Inhabitants.Single(person => person.InhabitantId == id).Position);
                    using var moved = await SendSignedAsync(host, client, key, device.DeviceId,
                        movePath, firstFounderMove, OwnerHttpBinding.FounderMovePayload(firstFounderMove));
                    Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
                    Assert.True((await moved.Content.ReadFromJsonAsync<OwnerFounderMoveReceipt>())!.Changed);
                    Assert.Equal(destination, runtime.Inhabitants.Single(person => person.InhabitantId == id).Position);
                    Assert.Contains(placementLog.Messages, message => message.Contains(
                        "founder_setup outcome=moved world_tick=0", StringComparison.Ordinal));
                }
            }

            Assert.True(runtime.Society.IsPaused);
            Assert.Equal(2, runtime.Society.Households.Single(item => item.Id == "household:camp-beta").MemberIds.Count);
            var lastFounder = runtime.FounderSetup!.FounderIds[^1];
            var undo = new OwnerFounderUndoAction(lastFounder);
            const string undoPath = "/api/v1/owner/founders/undo";
            var signedUndo = await CreateSignedRequestAsync(host, client, key, device.DeviceId,
                undoPath, undo, OwnerHttpBinding.FounderUndoPayload(undo));
            using var tamperedUndo = await client.PostAsJsonAsync(undoPath, signedUndo with
            {
                Action = undo with { FounderId = runtime.FounderSetup.FounderIds[0] },
            });
            Assert.False(tamperedUndo.IsSuccessStatusCode);
            Assert.Equal(4, runtime.FounderSetup.FounderIds.Count);
            using var undone = await SendSignedAsync(host, client, key, device.DeviceId,
                undoPath, undo, OwnerHttpBinding.FounderUndoPayload(undo));
            Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
            Assert.Equal(3, (await undone.Content.ReadFromJsonAsync<OwnerFounderUndoReceipt>())!.Placed);
            Assert.DoesNotContain(lastFounder, runtime.FounderSetup.FounderIds);
            Assert.Equal(runtime.FounderSetup.FounderIds[^1], host.Services
                .GetRequiredService<OwnerWorldObservationStore>().GetSnapshot().FounderSetup!.LastFounderId);
            var providerStatus = host.Services.GetRequiredService<ProviderConfigurationStore>().CaptureStatus();
            Assert.DoesNotContain(providerStatus.Assignments ?? [], item => item.InhabitantId == lastFounder);
            Assert.Contains(providerStatus.CredentialSlots ?? [], item => item.Id == slotId);
            Assert.Contains(placementLog.Messages, message => message.Contains(
                "founder_setup outcome=undone world_tick=0", StringComparison.Ordinal));
            var replacementId = "founder:" + Guid.NewGuid().ToString("N");
            var replacement = new OwnerFounderPlacementAction(replacementId, positions[3].X, positions[3].Y,
                new OwnerProviderConfigurationAction("personal", "openai", "gpt-5-mini", null,
                    false, replacementId, slotId));
            using var replaced = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/founders/place", replacement,
                OwnerHttpBinding.FounderPlacementPayload(replacement));
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            Assert.Equal(4, runtime.FounderSetup.FounderIds.Count);
            var start = new OwnerControlAction("start-world");
            using var started = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/control/start-world", start, OwnerHttpBinding.EmptyPayload("start-world"));
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            Assert.False(runtime.Society.IsPaused);
            Assert.True(runtime.FounderSetup!.Started);
            using var lateMove = await SendSignedAsync(host, client, key, device.DeviceId,
                "/api/v1/owner/founders/move", firstFounderMove!,
                OwnerHttpBinding.FounderMovePayload(firstFounderMove!));
            Assert.Equal(HttpStatusCode.Conflict, lateMove.StatusCode);
            using var lateUndo = await SendSignedAsync(host, client, key, device.DeviceId,
                undoPath, new OwnerFounderUndoAction(replacementId),
                OwnerHttpBinding.FounderUndoPayload(new OwnerFounderUndoAction(replacementId)));
            Assert.Equal(HttpStatusCode.Conflict, lateUndo.StatusCode);
            var agentId = "agent:" + Guid.NewGuid().ToString("N");
            var adult = new OwnerAgentPlacementAction(agentId, 4, 2,
                new OwnerProviderConfigurationAction("personal", "openai", "gpt-5-mini", null,
                    false, agentId, slotId));
            const string agentPath = "/api/v1/owner/agents/place";
            var signedAdult = await CreateSignedRequestAsync(host, client, key, device.DeviceId, agentPath,
                adult, OwnerHttpBinding.AgentPlacementPayload(adult));
            using var tamperedAdult = await client.PostAsJsonAsync(agentPath, signedAdult with
            {
                Action = adult with { X = 5 },
            });
            Assert.False(tamperedAdult.IsSuccessStatusCode);
            Assert.Equal(4, runtime.Inhabitants.Count);
            using var added = await SendSignedAsync(host, client, key, device.DeviceId, agentPath,
                adult, OwnerHttpBinding.AgentPlacementPayload(adult));
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            var addedReceipt = await added.Content.ReadFromJsonAsync<OwnerAgentPlacementReceipt>();
            Assert.Null(addedReceipt!.HouseholdId);
            Assert.Equal(5, runtime.Inhabitants.Count);
            Assert.Null(runtime.Society.GetInhabitant(agentId).HouseholdId);
            Assert.Contains(agentId, runtime.Towns.Single().ResidentIds);
            var observedAdult = host.Services.GetRequiredService<OwnerWorldObservationStore>()
                .GetSnapshot().Inhabitants.Single(person => person.Id == agentId);
            Assert.Equal("active", observedAdult.Lifecycle);
            Assert.Equal("unhoused", observedAdult.DecisionFactors.Single(factor => factor.Key == "household").Detail);
            using var duplicate = await SendSignedAsync(host, client, key, device.DeviceId, agentPath,
                adult, OwnerHttpBinding.AgentPlacementPayload(adult));
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            Assert.Contains(placementLog.Messages, message => message.Contains("AgentPlaced AgentId=" + agentId, StringComparison.Ordinal));
            var rename = new OwnerAgentRenameAction(agentId, "Nova");
            const string renamePath = "/api/v1/owner/agents/rename";
            var signedRename = await CreateSignedRequestAsync(host, client, key, device.DeviceId, renamePath,
                rename, OwnerHttpBinding.AgentRenamePayload(rename));
            using var tamperedRename = await client.PostAsJsonAsync(renamePath, signedRename with
            {
                Action = rename with { Name = "Someone else" },
            });
            Assert.False(tamperedRename.IsSuccessStatusCode);
            Assert.Equal("New agent", runtime.Society.GetInhabitant(agentId).Name);
            using var renamed = await SendSignedAsync(host, client, key, device.DeviceId, renamePath,
                rename, OwnerHttpBinding.AgentRenamePayload(rename));
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            Assert.Equal("Nova", host.Services.GetRequiredService<OwnerWorldObservationStore>()
                .GetSnapshot().Inhabitants.Single(person => person.Id == agentId).DisplayName);
            Assert.Contains(placementLog.Messages, message => message.Contains("AgentRenamed AgentId=" + agentId, StringComparison.Ordinal));
            Assert.DoesNotContain(placementLog.Messages, message => message.Contains("test-secret-key", StringComparison.Ordinal));
            Assert.DoesNotContain("test-secret-key", File.ReadAllText(host.Services.GetRequiredService<PrivateWorldStateFile>().Path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
