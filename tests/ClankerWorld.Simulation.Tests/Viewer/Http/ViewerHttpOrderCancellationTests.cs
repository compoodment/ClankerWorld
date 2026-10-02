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
    [Fact]
    public async Task SignedOrderCancellationRejectsTamperingAndWrongWorldAndSurvivesRestart()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-order-cancellation-");
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            const string instructionPath = "/api/v1/owner/instructions";
            const string cancellationPath = "/api/v1/owner/orders/cancel";
            string deviceId;
            OwnerInstructionReceipt orderReceipt;
            OwnerOrderCancelAction cancellation;
            OwnerOrderControlReceipt cancellationReceipt;

            using (var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = host.CreateClient())
            {
                deviceId = (await StartAndActivateAsync(host, client, key)).DeviceId;
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                runtime.Pause();
                var instruction = new OwnerInstructionAction(
                    "order-submit-01", "founder-mira", "must_do", "gather berries", runtime.Society.WorldId);
                using var submitted = await SendSignedAsync(host, client, key, deviceId, instructionPath,
                    instruction, OwnerHttpBinding.InstructionPayload(instruction));
                Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
                orderReceipt = (await submitted.Content.ReadFromJsonAsync<OwnerInstructionReceipt>())!;
                var waiting = Assert.Single(runtime.ExportState().Instructions!,
                    item => item.InstructionId == orderReceipt.InstructionId);
                Assert.Equal("waiting", waiting.Order!.Status);

                cancellation = new OwnerOrderCancelAction(
                    "order-cancel-01", "founder-mira", orderReceipt.InstructionId, runtime.Society.WorldId);
                var beforeTamper = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
                var signed = await CreateSignedRequestAsync(host, client, key, deviceId, cancellationPath,
                    cancellation, OwnerHttpBinding.OrderCancelPayload(cancellation));
                using var tampered = await client.PostAsJsonAsync(cancellationPath,
                    signed with { Action = cancellation with { OrderId = "another-order" } });
                Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
                Assert.Equal(beforeTamper, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));

                var unauthenticatedEnvelope = await CreateSignedRequestAsync(host, client, key, deviceId,
                    cancellationPath, cancellation, OwnerHttpBinding.OrderCancelPayload(cancellation));
                using var unauthenticated = await client.PostAsJsonAsync(cancellationPath,
                    unauthenticatedEnvelope with { SignatureBase64 = Convert.ToBase64String(new byte[64]) });
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
                Assert.Equal(beforeTamper, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));

                foreach (var invalidIdentity in new[]
                {
                    cancellation with { IdempotencyKey = new string('k', 129) },
                    cancellation with { TargetInhabitantId = "founder-mira\u0001" },
                })
                {
                    using var invalid = await SendSignedAsync(host, client, key, deviceId, cancellationPath,
                        invalidIdentity, OwnerHttpBinding.OrderCancelPayload(invalidIdentity));
                    Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                    Assert.Equal(beforeTamper, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
                }

                var wrongWorld = cancellation with { WorldId = "another-world" };
                using var refused = await SendSignedAsync(host, client, key, deviceId, cancellationPath,
                    wrongWorld, OwnerHttpBinding.OrderCancelPayload(wrongWorld));
                Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
                Assert.Equal(beforeTamper, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));

                using var cancelled = await SendSignedAsync(host, client, key, deviceId, cancellationPath,
                    cancellation, OwnerHttpBinding.OrderCancelPayload(cancellation));
                Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
                cancellationReceipt = (await cancelled.Content.ReadFromJsonAsync<OwnerOrderControlReceipt>())!;
                Assert.Equal(orderReceipt.InstructionId, cancellationReceipt.OrderId);
                Assert.Equal("cancelled", cancellationReceipt.Status);
                Assert.True(cancellationReceipt.Changed);
                var message = Assert.Single(new OwnerWorldObservationStore(runtime).GetSnapshot().Instructions,
                    item => item.InstructionId == orderReceipt.InstructionId);
                Assert.Equal("completed", message.State);
                Assert.Equal("cancelled", message.Order!.Status);

                var stateFile = host.Services.GetRequiredService<PrivateWorldStateFile>();
                var saved = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(stateFile.Path));
                Assert.Contains(saved.OrderCancellations!, item =>
                    item.IdempotencyKey == cancellation.IdempotencyKey && item.Receipt == cancellationReceipt);
                Assert.Equal("cancelled", Assert.Single(saved.Instructions!,
                    item => item.InstructionId == orderReceipt.InstructionId).Order!.Status);
                var afterCancellation = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());

                using var exactRetry = await SendSignedAsync(host, client, key, deviceId, cancellationPath,
                    cancellation, OwnerHttpBinding.OrderCancelPayload(cancellation));
                Assert.Equal(HttpStatusCode.OK, exactRetry.StatusCode);
                Assert.Equal(cancellationReceipt,
                    await exactRetry.Content.ReadFromJsonAsync<OwnerOrderControlReceipt>());
                Assert.Equal(afterCancellation, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));

                var conflictingReuse = cancellation with { OrderId = "another-order" };
                using var conflict = await SendSignedAsync(host, client, key, deviceId, cancellationPath,
                    conflictingReuse, OwnerHttpBinding.OrderCancelPayload(conflictingReuse));
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
                Assert.Equal(afterCancellation, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
                runtime.Validate();
            }

            using (var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true))
            using (var client = host.CreateClient())
            {
                var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
                var beforeRetry = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
                using var afterRestart = await SendSignedAsync(host, client, key, deviceId, cancellationPath,
                    cancellation, OwnerHttpBinding.OrderCancelPayload(cancellation));
                Assert.Equal(HttpStatusCode.OK, afterRestart.StatusCode);
                Assert.Equal(cancellationReceipt,
                    await afterRestart.Content.ReadFromJsonAsync<OwnerOrderControlReceipt>());
                Assert.Equal(beforeRetry, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
                runtime.Validate();
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
