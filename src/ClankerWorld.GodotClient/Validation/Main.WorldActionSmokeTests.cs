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
                worldSelectionList.EmitSignal(SlotList.SignalName.ItemActivated, 2L);
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
        worldSelectionList.EmitSignal(SlotList.SignalName.ItemSelected, (long)row);
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
        public TaskCompletionSource<string> LoadReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ReleaseLoad { get; set; }
        public ManualSaveLoadReceipt? LoadReceipt { get; set; }
        public TaskCompletionSource<OwnerProviderModelListAction> ModelsReceived { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ReleaseModels { get; set; }
        public OwnerProviderModelList? Models { get; set; }
        public bool FailModels { get; set; }
        public TaskCompletionSource<OwnerDeveloperEditAction> DeveloperEditReceived { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ReleaseDeveloperEdit { get; set; }
        public OwnerControlReceipt? DeveloperEditReceipt { get; set; }
        public bool FailDeveloperEdit { get; set; }
        public Func<OwnerWorldCreationAction, Task<OwnerWorldPreview>>? PreviewHandler { get; set; }
        public Func<bool, Task<OwnerControlReceipt>>? ControlHandler { get; set; }
        public int PauseCount => Volatile.Read(ref pauseCount);
        public int DeleteCount => Volatile.Read(ref deleteCount);
        public int SaveCreateCount => Volatile.Read(ref saveCreateCount);
        public ManualWorldSave[]? ManualSaves { get; set; }
        public SaveDiskSpaceStatus DiskSpace { get; set; } = new("ok", 4L * 1024 * 1024 * 1024, 1024L * 1024 * 1024, DateTimeOffset.UtcNow);
        public StartupRecoveryStatus StartupRecovery { get; set; } = new(false, null, null);
        public TaskCompletionSource RecoveryReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ReleaseRecovery { get; set; }
        public bool LoseRecoveryReply { get; set; }
        public string AutosaveWorldId { get; set; } = "autosave-world-B";
        public List<OwnerAutosaveConfigurationAction> AutosaveConfigurations { get; } = [];
        /// <summary>The next signed refresh's world, or none to refuse refreshes.</summary>
        public OwnerWorldReconnect? Reconnect { get; set; }
        public Func<OwnerUsageLimitAction, Task<OwnerUsageStatus>>? UsageLimitHandler { get; set; }
        public Func<Task<OwnerUsageStatus?>>? UsageHandler { get; set; }
        public OwnerUsageStatus? Usage { get; set; }
        public System.Collections.Concurrent.ConcurrentQueue<OwnerUsageLimitAction> UsageLimits { get; } = new();
        public OwnerPairingStart? PairingStart { get; set; }
        public OwnerPairingStatus? PairingStatus { get; set; }
        public bool LoseActivationReply { get; set; }
        public TaskCompletionSource ReconnectReceived { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ReleaseReconnect { get; set; }
        public bool HostPaused { get; private set; } = true;
        public IReadOnlyList<string>? SupportedActionPayloads { get; set; }
        public OwnerWorldPreview? Preview { get; set; }
        public CatalogWorld? SelectedWorld { get; set; }
        public CatalogWorld? CreatedWorld { get; set; }
        public OwnerWorldReconnect? CreatedObservation { get; set; }
        public System.Collections.Concurrent.ConcurrentQueue<string> Requests { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<OwnerWorldCreationAction> WorldCreations { get; } = new();
        /// <summary>Exact rename attempts given a name_taken response by this fixture.</summary>
        public HashSet<string> TakenAgentNames { get; } = new(StringComparer.Ordinal);
        public System.Collections.Concurrent.ConcurrentQueue<OwnerAgentRenameAction> RenameRequests { get; } = new();
        public TaskCompletionSource RenameReceived { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // When set, the host holds its rename reply until the check releases it.
        public TaskCompletionSource? ReleaseRename { get; set; }
        public bool FailAgentPlacement { get; set; }
        public bool FailPlacementProviderStatus { get; set; }
        public System.Collections.Concurrent.ConcurrentQueue<OwnerAgentPlacementAction> AgentPlacements { get; } = new();

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
            var path = context.Request.Url!.AbsolutePath;
            if (PairingStatus is { } status && path == $"{OwnerPairingEndpoints.Pairings}/{status.PairingId}")
            {
                Requests.Enqueue(path);
                await WriteResponseAsync(context, status).ConfigureAwait(false);
                return;
            }
            using var body = await JsonDocument.ParseAsync(context.Request.InputStream).ConfigureAwait(false);
            var envelope = body.RootElement;
            if (PairingStart is { } start && path == OwnerPairingEndpoints.Pairings)
            {
                if (envelope.GetProperty("publicKeySpkiBase64").GetString() != publicKey)
                    throw new InvalidOperationException("Pairing must use the fixture's actual public key.");
                Requests.Enqueue(path);
                await WriteResponseAsync(context, start).ConfigureAwait(false);
                return;
            }
            if (!OwnerPairingProtocol.VerifyP256Sha256P1363(publicKey,
                    envelope.GetProperty("canonicalProof").GetString()!, envelope.GetProperty("signatureBase64").GetString()!))
                throw new InvalidOperationException("The smoke host must receive an actual signed owner request.");
            if (PairingStart is { } activation && path == OwnerPairingEndpoints.PairingActivation)
            {
                if (envelope.GetProperty("canonicalProof").GetString() != activation.ActivationCanonicalProof)
                    throw new InvalidOperationException("Activation must prove possession of the pending pairing key.");
                Requests.Enqueue(path);
                PairingStatus = new(activation.Authority, activation.PairingId, activation.DeviceId,
                    activation.PublicKeyFingerprint, OwnerPairingState.Active, activation.ExpiresAtUtc);
                if (LoseActivationReply)
                {
                    // The host committed activation, but the client cannot read its incomplete reply.
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync("{"u8.ToArray()).ConfigureAwait(false);
                    context.Response.Close();
                }
                else
                    await WriteResponseAsync(context, new OwnerDevice(activation.DeviceId, publicKey,
                        activation.PublicKeyFingerprint, OwnerDeviceState.Active, DateTimeOffset.UnixEpoch, null)).ConfigureAwait(false);
                return;
            }
            object response;
            Requests.Enqueue(context.Request.Url!.AbsolutePath);
            switch (context.Request.Url!.AbsolutePath)
            {
                case "/api/v1/owner/recovery/status":
                    response = StartupRecovery;
                    break;
                case "/api/v1/owner/recovery/restore":
                    var recoveryId = envelope.GetProperty("action").GetProperty("value").GetString()!;
                    RecoveryReceived.TrySetResult();
                    if (ReleaseRecovery is { } releaseRecovery) await releaseRecovery.Task.ConfigureAwait(false);
                    StartupRecovery = new(false, StartupRecovery.WorldId, null);
                    if (LoseRecoveryReply)
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.OutputStream.WriteAsync("{"u8.ToArray()).ConfigureAwait(false);
                        context.Response.Close();
                        return;
                    }
                    response = new StartupRecoveryReceipt(recoveryId, 0);
                    break;
                case OwnerPairingEndpoints.OwnerAgentPlace:
                    var placement = envelope.GetProperty("action").Deserialize<OwnerAgentPlacementAction>(JsonOptions)!;
                    AgentPlacements.Enqueue(placement);
                    if (FailAgentPlacement)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        response = new { error = "Controlled placement refusal." };
                    }
                    else response = new OwnerAgentPlacementReceipt(placement.AgentId, "household:" + placement.AgentId);
                    break;
                case OwnerPairingEndpoints.OwnerProviderStatus:
                    if (FailPlacementProviderStatus)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        response = new { error = "Controlled settings refresh failure." };
                    }
                    else response = new OwnerProviderConfigurationStatus("openai", "openai", 0,
                        [new("openai", "placement-smoke-model", true)]);
                    break;
                case OwnerPairingEndpoints.ChallengeIssue:
                    response = new OwnerChallenge(Authority, "smoke-device", Guid.NewGuid().ToString("N"),
                        "smoke-nonce", DateTimeOffset.UtcNow.AddMinutes(1),
                        SupportedActionPayloads ?? (PreviewHandler is null ? null : [OwnerWorldActionPayload.WorldCreationPayloadDomain]));
                    break;
                case OwnerPairingEndpoints.OwnerWorldPreview when PreviewHandler is not null:
                    response = await PreviewHandler(envelope.GetProperty("action").Deserialize<OwnerWorldCreationAction>(JsonOptions)!).ConfigureAwait(false);
                    break;
                case OwnerPairingEndpoints.OwnerWorldList:
                    response = Catalog;
                    break;
                case OwnerPairingEndpoints.OwnerSaveList when ManualSaves is { } saves:
                    response = saves;
                    break;
                case OwnerPairingEndpoints.OwnerSaveTimeline when ManualSaves is not null:
                    response = new SaveTimelinePosition(null, null, false);
                    break;
                case OwnerPairingEndpoints.OwnerSaveCreate:
                    Interlocked.Increment(ref saveCreateCount);
                    response = new ManualWorldSave("new-save", envelope.GetProperty("action").GetProperty("value").GetString()!,
                        DateTimeOffset.UnixEpoch, 0);
                    break;
                case "/api/v1/owner/saves/disk-status":
                    response = DiskSpace;
                    break;
                case OwnerPairingEndpoints.OwnerSaveLoad when LoadReceipt is { } loadReceipt:
                    LoadReceived.TrySetResult(envelope.GetProperty("action").GetProperty("value").GetString()!);
                    if (ReleaseLoad is { } releaseLoad) await releaseLoad.Task.ConfigureAwait(false);
                    response = loadReceipt;
                    break;
                case OwnerPairingEndpoints.OwnerProviderModels when Models is { } modelList:
                    var failModels = FailModels;
                    ModelsReceived.TrySetResult(envelope.GetProperty("action").Deserialize<OwnerProviderModelListAction>(JsonOptions)!);
                    if (ReleaseModels is { } releaseModels) await releaseModels.Task.ConfigureAwait(false);
                    if (failModels)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        response = new { error = "Controlled previous-timeline model-list failure." };
                    }
                    else response = modelList;
                    break;
                case OwnerPairingEndpoints.OwnerDeveloperEdit when DeveloperEditReceipt is { } editReceipt:
                    var failEdit = FailDeveloperEdit;
                    DeveloperEditReceived.TrySetResult(envelope.GetProperty("action").Deserialize<OwnerDeveloperEditAction>(JsonOptions)!);
                    if (ReleaseDeveloperEdit is { } releaseEdit) await releaseEdit.Task.ConfigureAwait(false);
                    if (failEdit)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        response = new { error = "Controlled previous-timeline developer-edit failure." };
                    }
                    else response = editReceipt;
                    break;
                case OwnerPairingEndpoints.OwnerAutosaveConfigure:
                    var configuration = envelope.GetProperty("action").Deserialize<OwnerAutosaveConfigurationAction>(JsonOptions)!;
                    AutosaveConfigurations.Add(configuration);
                    response = new WorldAutosaveSettings(AutosaveWorldId, configuration.Enabled,
                        configuration.IntervalMinutes, configuration.RotationCount, DateTimeOffset.UnixEpoch, -1);
                    break;
                case OwnerPairingEndpoints.OwnerPause:
                    HostPaused = true;
                    Interlocked.Increment(ref pauseCount);
                    PauseReceived.TrySetResult();
                    if (ControlHandler is { } pauseHandler)
                        response = await pauseHandler(true).ConfigureAwait(false);
                    else
                    {
                        await ReleasePause.Task.ConfigureAwait(false);
                        response = new OwnerControlReceipt("pause", true, true, 0, 0, 0);
                    }
                    break;
                case OwnerPairingEndpoints.OwnerWorldSelect:
                    SelectReceived.TrySetResult(envelope.GetProperty("action").GetProperty("value").GetString()!);
                    await ReleaseSelect.Task.ConfigureAwait(false);
                    if (SelectedWorld is { } selectedWorld)
                    {
                        response = selectedWorld;
                        break;
                    }
                    // Stop after recording the signed ID; this check does not claim a live-host playtest.
                    context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                    response = new { error = "Controlled selection refusal." };
                    break;
                case OwnerPairingEndpoints.OwnerWorldPreview when Preview is not null:
                    response = Preview;
                    break;
                case OwnerPairingEndpoints.OwnerWorldCreate when CreatedWorld is not null:
                    WorldCreations.Enqueue(envelope.GetProperty("action").Deserialize<OwnerWorldCreationAction>(JsonOptions)!);
                    Reconnect = CreatedObservation;
                    response = CreatedWorld;
                    break;
                case OwnerPairingEndpoints.OwnerResume:
                    HostPaused = false;
                    response = ControlHandler is { } resumeHandler
                        ? await resumeHandler(false).ConfigureAwait(false)
                        : new OwnerControlReceipt("resume", true, false, 0, 0, 0);
                    break;
                case OwnerPairingEndpoints.OwnerReconnect when Reconnect is not null:
                    ReconnectReceived.TrySetResult();
                    if (ReleaseReconnect is { } releaseReconnect) await releaseReconnect.Task.ConfigureAwait(false);
                    response = Reconnect;
                    break;
                case OwnerPairingEndpoints.OwnerUsageLimit when UsageLimitHandler is not null:
                    response = await UsageLimitHandler(envelope.GetProperty("action").Deserialize<OwnerUsageLimitAction>(JsonOptions)!).ConfigureAwait(false);
                    break;
                case OwnerPairingEndpoints.OwnerUsageStatus when UsageHandler is not null:
                    if (await UsageHandler().ConfigureAwait(false) is { } usage)
                        response = usage;
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        response = new { error = "Controlled usage read failure." };
                    }
                    break;
                case OwnerPairingEndpoints.OwnerUsageLimit when Usage is not null:
                    var limit = envelope.GetProperty("action").Deserialize<OwnerUsageLimitAction>(JsonOptions)!;
                    UsageLimits.Enqueue(limit);
                    Usage = Usage with
                    {
                        AttemptLimit = limit.AdditionalCalls > 0 ? Usage.Attempts + limit.AdditionalCalls : limit.AttemptLimit,
                        LimitReached = limit.AttemptLimit is { } cap && cap <= Usage.Attempts && limit.AdditionalCalls == 0,
                    };
                    response = Usage;
                    break;
                case OwnerPairingEndpoints.OwnerAgentRename:
                    var rename = envelope.GetProperty("action").Deserialize<OwnerAgentRenameAction>(JsonOptions)!;
                    RenameRequests.Enqueue(rename);
                    RenameReceived.TrySetResult();
                    if (ReleaseRename is { } releaseRename) await releaseRename.Task.ConfigureAwait(false);
                    if (TakenAgentNames.Contains(rename.Name))
                    {
                        // The same refusal the world host sends for a taken first name.
                        context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                        response = new { code = "name_taken", message = "That first name belongs to another agent." };
                    }
                    else response = new OwnerAgentRenameReceipt(rename.AgentId, rename.Name, true);
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
            await WriteResponseAsync(context, response).ConfigureAwait(false);
        }

        private static async Task WriteResponseAsync(HttpListenerContext context, object response)
        {
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.OutputStream, response,
                response.GetType(), JsonOptions).ConfigureAwait(false);
            context.Response.Close();
        }

        public void Dispose() => listener.Close();
    }
}
