using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData("durable-retry")]
    [InlineData(" durable-retry ")]
    public async Task SignedInstructionRetryReturnsItsReceiptAfterRecipientDeathAndHostRestart(string idempotencyKey)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-instruction-retry-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            const string path = "/api/v1/owner/instructions";
            OwnerInstructionAction action;
            string deviceId;
            OwnerInstructionReceipt receipt;
            using (var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = host.CreateClient())
            {
                deviceId = (await StartAndActivateAsync(host, client, key)).DeviceId;
                action = new OwnerInstructionAction(idempotencyKey, "founder-mira", "suggestive", "wait safely",
                    host.Services.GetRequiredService<PrivateWorldRuntime>().Society.WorldId);
                using var accepted = await SendSignedAsync(host, client, key, deviceId, path, action,
                    OwnerHttpBinding.InstructionPayload(action));
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
                receipt = (await accepted.Content.ReadFromJsonAsync<OwnerInstructionReceipt>())!;
                Assert.Equal("durable-retry", receipt.IdempotencyKey);
                using var exactRetry = await SendSignedAsync(host, client, key, deviceId, path, action,
                    OwnerHttpBinding.InstructionPayload(action));
                Assert.Equal(HttpStatusCode.OK, exactRetry.StatusCode);
                Assert.Equal(receipt, await exactRetry.Content.ReadFromJsonAsync<OwnerInstructionReceipt>());
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                using var deceased = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
                    PrivateWorldRuntimeCodec.Encode(InstructionRecipientFixture.AfterDeath(runtime.ExportState(), "founder-mira"))));
                deceased.Pause();
                host.Services.GetRequiredService<PrivateWorldStateFile>().Save(deceased);
            }

            for (var restart = 0; restart < 2; restart++)
            {
                using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true);
                using var client = host.CreateClient();
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                var before = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
                using var retried = await SendSignedAsync(host, client, key, deviceId, path, action,
                    OwnerHttpBinding.InstructionPayload(action));
                Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
                Assert.Equal(receipt, await retried.Content.ReadFromJsonAsync<OwnerInstructionReceipt>());
                var conflict = action with { Text = "gather food" };
                using var conflicting = await SendSignedAsync(host, client, key, deviceId, path, conflict,
                    OwnerHttpBinding.InstructionPayload(conflict));
                Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
                var newDead = action with { IdempotencyKey = "new-dead-request" };
                using var rejected = await SendSignedAsync(host, client, key, deviceId, path, newDead,
                    OwnerHttpBinding.InstructionPayload(newDead));
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
                Assert.Single(runtime.ExportState().Instructions!);
                Assert.Single(runtime.ExportState().Events, item => item.Kind == "instruction_queued");
                runtime.Validate();
            }
        }
        finally { directory.Delete(recursive: true); }
    }
}
