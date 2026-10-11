using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;
using Microsoft.Extensions.DependencyInjection;
using ClientAction = ClankerWorld.GodotClient.UI.OwnerOrderCancelAction;
using ClientAuthority = ClankerWorld.GodotClient.Pairing.OwnerAuthorityIdentity;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ViewerHttpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeBornClientCancellationRetainsItsFullTargetThroughLostResponseAndRetry(bool bornAdult)
    {
        var state = await GrownAgentHelperMemoryTests.CancellationAdultStateAsync();
        var birth = Assert.Single(state.Society.Society.Births);
        var actor = bornAdult ? birth.ChildId : birth.PrimaryCaregiverId;
        Assert.True(birth.ChildId.Length > 128);
        Assert.InRange(birth.PrimaryCaregiverId.Length, 1, 128);
        var directory = Directory.CreateTempSubdirectory("native-client-cancel-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "runtime.json"), PrivateWorldRuntimeCodec.Encode(state));
            using var host = new ViewerWebApplicationFactory(directory.FullName, privateWorld: true, legacyPrivateWorld: false);
            using var pairingClient = host.CreateClient();
            pairingClient.BaseAddress = new Uri("http://127.0.0.1/");
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var device = await StartAndActivateAsync(host, pairingClient, key);
            var runtime = host.Services.GetRequiredService<PrivateWorldRuntime>();
            Assert.True(runtime.Society.IsPaused);
            var instruction = new ClankerWorld.Viewer.Control.OwnerInstructionAction(
                "native-client-order", actor, "must_do", "Eat 1 food", runtime.Society.WorldId);
            using var submitted = await SendSignedAsync(host, pairingClient, key, device.DeviceId,
                "/api/v1/owner/instructions", instruction, OwnerHttpBinding.InstructionPayload(instruction));
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
            var receipt = Assert.IsType<OwnerInstructionReceipt>(await submitted.Content.ReadFromJsonAsync<OwnerInstructionReceipt>());
            var order = Assert.Single(runtime.ExportState().Instructions!, item => item.InstructionId == receipt.InstructionId);
            Assert.Equal((actor, "consume_food", "waiting"), (order.TargetInhabitantId, order.Order!.Action, order.Order.Status));

            var identity = host.Services.GetRequiredService<OwnerAuthorityStore>().Identity;
            var authority = new ClientAuthority(identity.ServerAuthorityId, identity.WorldId);
            var timeline = Assert.IsType<ViewerObserverTimeline>(host.Services.GetRequiredService<OwnerWorldObservationStore>()
                .GetReconnectBaseline(0).Timeline);
            var binding = OwnerPendingSubmissionBinding.Create(authority, device.DeviceId,
                device.PublicKeyFingerprint, pairingClient.BaseAddress,
                new(timeline.InstanceId, timeline.Generation), runtime.Society.WorldId);
            var path = Path.Combine(directory.FullName, "pending.json");
            var store = new OwnerPendingSubmissionStore(path);
            var action = new ClientAction("native-client-cancel", actor, receipt.InstructionId, runtime.Society.WorldId);
            var pending = OwnerPendingSubmission.ForOrderCancel(binding, action);
            Assert.True(store.TrySave(pending));
            var pendingBytes = File.ReadAllBytes(path);

            using var handler = new LostCancellationReceiptHandler(host.Server.CreateHandler());
            using var client = new HttpClient(handler) { BaseAddress = pairingClient.BaseAddress };
            using var signer = new SettingsKeySigner(key);
            var api = new OwnerWorldApi(client);
            await Assert.ThrowsAsync<HttpRequestException>(() => api.CancelOrderAsync(client.BaseAddress,
                authority, device.DeviceId, action, signer, CancellationToken.None));
            Assert.Equal("cancelled", Assert.Single(runtime.ExportState().Instructions!, item => item.InstructionId == receipt.InstructionId).Order!.Status);
            var original = Assert.Single(runtime.ExportState().OrderCancellations!);
            Assert.Equal((actor, action.OrderId, action.WorldId), (original.TargetInhabitantId, original.OrderId, original.WorldId));
            var afterFirst = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            Assert.Equal(pendingBytes, File.ReadAllBytes(path));

            var restartedStore = new OwnerPendingSubmissionStore(path);
            var retained = Assert.IsType<OwnerPendingSubmission>(restartedStore.TryLoad(binding));
            Assert.Equal(action, retained.OrderCancel!.ToAction());
            Assert.True(retained.OrderCancel.CanRetryIn(runtime.Society.WorldId));
            Assert.Null(restartedStore.TryLoad(binding with { DeviceId = "other-device" }));
            Assert.Null(restartedStore.TryLoad(binding with { ObservedWorldId = "other-world" }));
            Assert.Null(restartedStore.TryLoad(binding with { Timeline = new(timeline.InstanceId, timeline.Generation + 1) }));
            var confirmed = await new OwnerWorldApi(client).CancelOrderAsync(client.BaseAddress,
                authority, device.DeviceId, retained.OrderCancel.ToAction(), signer, CancellationToken.None);
            Assert.Equal((original.Receipt.OrderId, original.Receipt.Status, original.Receipt.Changed),
                (confirmed.OrderId, confirmed.Status, confirmed.Changed));
            Assert.Equal(new[] { action, action }, handler.Actions);
            Assert.Equal(afterFirst, PrivateWorldRuntimeCodec.Encode(runtime.ExportState()));
            Assert.Single(runtime.ExportState().OrderCancellations!);
            using var reloaded = host.Services.GetRequiredService<PrivateWorldStateFile>().LoadOrCreate(state.WorldSeed);
            Assert.Equal(afterFirst, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
            Assert.Equal(actor, Assert.Single(reloaded.ExportState().OrderCancellations!).TargetInhabitantId);
            Assert.True(restartedStore.TryClear(retained));
            Assert.Null(restartedStore.TryLoad(binding));
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class LostCancellationReceiptHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private bool dropReceipt = true;
        public List<ClientAction> Actions { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cancellation = request.RequestUri!.AbsolutePath == "/api/v1/owner/orders/cancel";
            if (cancellation)
            {
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Actions.Add(JsonSerializer.Deserialize<ClientAction>(document.RootElement.GetProperty("action").GetRawText(), WebJsonOptions)!);
            }
            var response = await base.SendAsync(request, cancellationToken);
            if (cancellation && dropReceipt && response.IsSuccessStatusCode)
            {
                dropReceipt = false;
                response.Dispose();
                throw new HttpRequestException("The accepted cancellation receipt was lost.");
            }
            return response;
        }
    }
}
