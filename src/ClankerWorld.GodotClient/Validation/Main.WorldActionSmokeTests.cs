using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using Environment = System.Environment;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyWorldActionSelectionAsync()
    {
        var originalRegistration = registration;
        var originalKey = deviceKey;
        var originalUrl = worldUrlInput.Text;
        var originalCi = Environment.GetEnvironmentVariable("CI");
        Environment.SetEnvironmentVariable("CI", "true");
        using var key = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        try
        {
            deviceKey = key;
            // Test both reply orders, including the case where the old last row vanishes.
            foreach (var (refreshFirst, removeChosen) in new[] { (true, false), (false, false), (true, true) })
            {
                using var host = new WorldActionSmokeHost(key.PublicKeySpkiBase64);
                registration = new(host.Authority, "smoke-device", key.PublicKeyFingerprint, host.Address);
                worldUrlInput.Text = host.Address;
                ShowWorldActionSmokeMenu();
                host.Catalog = WorldActionSmokeCatalog(removeChosen ? ["A", "B", "C"] : ["A", "B", "C", "D"]);
                await RefreshWorldListAsync();
                ChooseWorldActionSmokeRow(2);
                var opening = SelectListedWorldAsync();
                await host.PauseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (!worldSelectButton.Disabled || !worldDeleteButton.Disabled)
                    throw new InvalidOperationException("Opening a world must disable both Open and Delete.");

                // A selected-row signal must not re-enable either action while opening.
                ChooseWorldActionSmokeRow(1);
                worldDeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (!worldSelectButton.Disabled || !worldDeleteButton.Disabled || pendingDeletion is not null)
                    throw new InvalidOperationException("Changing rows during Open must not admit a deletion.");
                pendingDeletion = new("world", "B", "world-B");
                await DeleteConfirmedAsync();
                if (host.DeleteCount != 0)
                    throw new InvalidOperationException("A previously confirmed deletion must not overlap Open.");

                if (!refreshFirst)
                {
                    host.ReleasePause.SetResult();
                    await host.SelectReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                host.Catalog = WorldActionSmokeCatalog(removeChosen ? ["A", "B"] : ["A", "C", "D"]);
                await RefreshWorldListAsync();
                if (refreshFirst) host.ReleasePause.SetResult();
                var signedId = await host.SelectReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (signedId != "C")
                    throw new InvalidOperationException($"A catalog refresh retargeted Open to {signedId} instead of C.");
                host.ReleaseSelect.SetResult();
                await opening.WaitAsync(TimeSpan.FromSeconds(5));
                if (worldMenuBusy || isOwnerAction || !worldSelectButton.Disabled || !worldDeleteButton.Disabled ||
                    !worldMenuStatus.Text.StartsWith("Could not open world:", StringComparison.Ordinal))
                    throw new InvalidOperationException("Open cleanup must safely handle a changed or removed selected row.");
            }

            using (var host = new WorldActionSmokeHost(key.PublicKeySpkiBase64))
            {
                registration = new(host.Authority, "smoke-device", key.PublicKeyFingerprint, host.Address);
                worldUrlInput.Text = host.Address;
                ShowWorldActionSmokeMenu();
                host.Catalog = WorldActionSmokeCatalog(["A", "B", "C", "D"]);
                await RefreshWorldListAsync();
                ChooseWorldActionSmokeRow(1);
                worldDeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (pendingDeletion?.Id != "B")
                    throw new InvalidOperationException("The Delete control must confirm the chosen world.");
                deletionConfirmation.Hide();
                var deleting = DeleteConfirmedAsync();
                await host.DeleteReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                ChooseWorldActionSmokeRow(2);
                worldSelectionList.EmitSignal(ItemList.SignalName.ItemActivated, 2L);
                worldSelectButton.EmitSignal(BaseButton.SignalName.Pressed);
                await SelectListedWorldAsync();
                if (!worldSelectButton.Disabled || !worldDeleteButton.Disabled || host.PauseCount != 0)
                    throw new InvalidOperationException("Delete must block Open through both the button and double-click paths.");
                host.Catalog = WorldActionSmokeCatalog(["A", "C", "D"]);
                host.ReleaseDelete.SetResult();
                await deleting.WaitAsync(TimeSpan.FromSeconds(5));
                if (listedWorlds.Length != 3 || worldMenuBusy || isOwnerAction)
                    throw new InvalidOperationException("Deletion must refresh its catalog and release the action gate.");
                ChooseWorldActionSmokeRow(1);
                var opening = SelectListedWorldAsync();
                await host.PauseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                host.ReleasePause.SetResult();
                if (await host.SelectReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)) != "C")
                    throw new InvalidOperationException("Opening after deletion must use C's new row.");
                host.ReleaseSelect.SetResult();
                await opening.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            worldListRequest.Cancel();
            worldMenuOverlay.Hide();
            deletionConfirmation.Hide();
            pendingDeletion = null;
            listedWorlds = [];
            worldSelectionList.Clear();
            registration = originalRegistration;
            deviceKey = originalKey;
            worldUrlInput.Text = originalUrl;
            Environment.SetEnvironmentVariable("CI", originalCi);
            RefreshControlAvailability();
            RefreshMainMenuAvailability();
            statusToast.Hide();
        }
    }

    private void ShowWorldActionSmokeMenu()
    {
        worldMenuColumns.Hide();
        worldSelectionList.Show();
        worldSelectButton.Show();
        worldDeleteButton.Show();
        worldMenuOverlay.Show();
    }

    private void ChooseWorldActionSmokeRow(int row)
    {
        worldSelectionList.Select(row);
        worldSelectionList.EmitSignal(ItemList.SignalName.ItemSelected, (long)row);
    }

    private static WorldCatalogSnapshot WorldActionSmokeCatalog(string[] ids) => new("A", ids.Select((id, index) =>
        new CatalogWorld(id, id, "world-" + id, "seed", DateTimeOffset.UnixEpoch.AddSeconds(-index), [], null, "compatible")).ToArray());

    /// <summary>Disposable loopback host controls signed HTTP timing without reading real saves or pairing files.</summary>
    private sealed class WorldActionSmokeHost : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly HttpListener listener = new();
        private readonly string publicKey;
        private int pauseCount;
        private int deleteCount;
        private int saveCreateCount;
        public OwnerAuthorityIdentity Authority { get; } = new("smoke-server", "smoke-authority");
        public string Address { get; }
        public WorldCatalogSnapshot Catalog { get; set; } = WorldActionSmokeCatalog([]);
        public TaskCompletionSource PauseReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> SelectReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DeleteReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePause { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSelect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PauseCount => Volatile.Read(ref pauseCount);
        public int DeleteCount => Volatile.Read(ref deleteCount);
        public int SaveCreateCount => Volatile.Read(ref saveCreateCount);
        public string AutosaveWorldId { get; set; } = "autosave-world-B";
        public List<OwnerAutosaveConfigurationAction> AutosaveConfigurations { get; } = [];

        public WorldActionSmokeHost(string publicKey)
        {
            this.publicKey = publicKey;
            var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();
            Address = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(Address);
            listener.Start();
            _ = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync().ConfigureAwait(false);
                    _ = ReplyAsync(context);
                }
            }
            catch (HttpListenerException) when (!listener.IsListening) { }
            catch (ObjectDisposedException) { }
        }

        private async Task ReplyAsync(HttpListenerContext context)
        {
            using var body = await JsonDocument.ParseAsync(context.Request.InputStream).ConfigureAwait(false);
            var envelope = body.RootElement;
            if (!OwnerPairingProtocol.VerifyP256Sha256P1363(publicKey,
                    envelope.GetProperty("canonicalProof").GetString()!, envelope.GetProperty("signatureBase64").GetString()!))
                throw new InvalidOperationException("The smoke host must receive an actual signed owner request.");
            object response;
            switch (context.Request.Url!.AbsolutePath)
            {
                case OwnerPairingEndpoints.ChallengeIssue:
                    response = new OwnerChallenge(Authority, "smoke-device", Guid.NewGuid().ToString("N"),
                        "smoke-nonce", DateTimeOffset.UtcNow.AddMinutes(1));
                    break;
                case OwnerPairingEndpoints.OwnerWorldList:
                    response = Catalog;
                    break;
                case OwnerPairingEndpoints.OwnerSaveCreate:
                    Interlocked.Increment(ref saveCreateCount);
                    response = new ManualWorldSave("new-save", envelope.GetProperty("action").GetProperty("value").GetString()!,
                        DateTimeOffset.UnixEpoch, 0);
                    break;
                case OwnerPairingEndpoints.OwnerAutosaveConfigure:
                    var configuration = envelope.GetProperty("action").Deserialize<OwnerAutosaveConfigurationAction>(JsonOptions)!;
                    AutosaveConfigurations.Add(configuration);
                    response = new WorldAutosaveSettings(AutosaveWorldId, configuration.Enabled,
                        configuration.IntervalMinutes, configuration.RotationCount, DateTimeOffset.UnixEpoch, -1);
                    break;
                case OwnerPairingEndpoints.OwnerPause:
                    Interlocked.Increment(ref pauseCount);
                    PauseReceived.TrySetResult();
                    await ReleasePause.Task.ConfigureAwait(false);
                    response = new OwnerControlReceipt("pause", true, true, 0, 0, 0);
                    break;
                case OwnerPairingEndpoints.OwnerWorldSelect:
                    SelectReceived.TrySetResult(envelope.GetProperty("action").GetProperty("value").GetString()!);
                    await ReleaseSelect.Task.ConfigureAwait(false);
                    // Stop after recording the signed ID; this check does not claim a live-host playtest.
                    context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                    response = new { error = "Controlled selection refusal." };
                    break;
                case "/api/v1/owner/delete":
                    Interlocked.Increment(ref deleteCount);
                    DeleteReceived.TrySetResult();
                    await ReleaseDelete.Task.ConfigureAwait(false);
                    response = new OwnerDeletionReceipt("B", true);
                    break;
                default:
                    // Deletion's observation refresh is outside this catalog/action check.
                    context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    response = new { error = "No observation fixture." };
                    break;
            }
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.OutputStream, response,
                response.GetType(), JsonOptions).ConfigureAwait(false);
            context.Response.Close();
        }

        public void Dispose() => listener.Close();
    }
}
