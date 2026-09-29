using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.GodotClient.Pairing;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Uses an owner device key to issue a one-use challenge and sign every
/// operation. It stores no bearer token and has no local mutation fallback.
/// </summary>
public sealed class OwnerWorldApi
{
    private readonly OwnerPairingClient pairing;

    public OwnerWorldApi(HttpClient httpClient)
    {
        pairing = new OwnerPairingClient(httpClient);
    }

    public Task<OwnerPairingStart> StartPairingAsync(
        Uri serverUri,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.StartPairingAsync(serverUri, deviceKey, cancellationToken);

    public Task<OwnerPairingStatus> GetPairingStatusAsync(
        Uri serverUri,
        string pairingId,
        CancellationToken cancellationToken) =>
        pairing.GetPairingStatusAsync(serverUri, pairingId, cancellationToken);

    public Task<OwnerDevice> ActivatePairingAsync(
        Uri serverUri,
        OwnerPairingStart pairingStart,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.ActivatePairingAsync(serverUri, pairingStart, deviceKey, cancellationToken);

    public async Task<OwnerWorldReconnect> ReconnectAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        long afterEventId,
        string? knownTerrainWorldId,
        string? knownTerrainDigest,
        string? knownMapLayersDigest,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        var action = new OwnerReconnectAction(afterEventId, knownTerrainWorldId, knownTerrainDigest,
            knownMapLayersDigest);
        return await pairing.SendSignedActionAsync<OwnerReconnectAction, OwnerWorldReconnect>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerReconnect,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.Reconnect(action),
            action,
            deviceKey,
            deadline.Token).ConfigureAwait(false);
    }

    public Task<OwnerControlReceipt> SetPausedAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        bool paused,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken)
    {
        var operation = paused ? "pause" : "resume";
        var action = new OwnerControlAction(operation);
        var path = paused ? OwnerPairingEndpoints.OwnerPause : OwnerPairingEndpoints.OwnerResume;
        return pairing.SendSignedActionAsync<OwnerControlAction, OwnerControlReceipt>(
            serverUri,
            authority,
            deviceId,
            path,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.Control(operation),
            action,
            deviceKey,
            cancellationToken);
    }

    public Task<OwnerControlReceipt> StartWorldAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerControlAction("start-world");
        return pairing.SendSignedActionAsync<OwnerControlAction, OwnerControlReceipt>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerStartWorld,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.Control("start-world"),
            action, deviceKey, cancellationToken);
    }

    public Task<ManualWorldSave[]> ListManualSavesAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerControlAction("list-saves");
        return pairing.SendSignedActionAsync<OwnerControlAction, ManualWorldSave[]>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerSaveList,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.Control("list-saves"),
            action, deviceKey, cancellationToken);
    }

    public Task<WorldAutosaveSettings> GetAutosaveSettingsAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerControlAction("autosave-status");
        return pairing.SendSignedActionAsync<OwnerControlAction, WorldAutosaveSettings>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerAutosaveStatus,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.Control("autosave-status"),
            action, deviceKey, cancellationToken);
    }

    public Task<WorldAutosaveSettings> ConfigureAutosaveAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        OwnerAutosaveConfigurationAction action, IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerAutosaveConfigurationAction, WorldAutosaveSettings>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerAutosaveConfigure,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.AutosaveConfiguration(action),
            action, deviceKey, cancellationToken);

    public Task<ManualWorldSave> CreateManualSaveAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        string name, IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerManualSaveAction("create", name);
        return pairing.SendSignedActionAsync<OwnerManualSaveAction, ManualWorldSave>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerSaveCreate,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.ManualSave(action),
            action, deviceKey, cancellationToken);
    }

    public Task<ManualSaveOverwriteReceipt> OverwriteManualSaveAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        string id, IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerManualSaveAction("overwrite", id);
        return pairing.SendSignedActionAsync<OwnerManualSaveAction, ManualSaveOverwriteReceipt>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerSaveOverwrite,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.ManualSave(action),
            action, deviceKey, cancellationToken);
    }

    public Task<WorldCatalogSnapshot> ListWorldsAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerControlAction("list-worlds");
        return pairing.SendSignedActionAsync<OwnerControlAction, WorldCatalogSnapshot>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerWorldList,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.Control("list-worlds"),
            action, deviceKey, cancellationToken);
    }

    public Task<CatalogWorld> CreateWorldAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        OwnerWorldCreationAction action, IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerWorldCreationAction, CatalogWorld>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerWorldCreate,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.WorldCreation(action),
            action, deviceKey, cancellationToken);

    public Task<OwnerWorldPreview> PreviewWorldAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        OwnerWorldCreationAction action, IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerWorldCreationAction, OwnerWorldPreview>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerWorldPreview,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.WorldCreation(action),
            action, deviceKey, cancellationToken);

    public Task<CatalogWorld> SelectWorldAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        string id, IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerManualSaveAction("select-world", id);
        return pairing.SendSignedActionAsync<OwnerManualSaveAction, CatalogWorld>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerWorldSelect,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.ManualSave(action),
            action, deviceKey, cancellationToken);
    }

    public Task<ManualSaveLoadReceipt> LoadManualSaveAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        string id, IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerManualSaveAction("load", id);
        return pairing.SendSignedActionAsync<OwnerManualSaveAction, ManualSaveLoadReceipt>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerSaveLoad,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.ManualSave(action),
            action, deviceKey, cancellationToken);
    }

    public Task<OwnerFounderPlacementReceipt> PlaceFounderAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        OwnerFounderPlacementAction action, IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerFounderPlacementAction, OwnerFounderPlacementReceipt>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerFounderPlace,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.FounderPlacement(action),
            action, deviceKey, cancellationToken);

    public Task<OwnerFirstTownLayoutReceipt> AcceptFirstTownLayoutAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        OwnerFirstTownLayoutAction action, IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerFirstTownLayoutAction, OwnerFirstTownLayoutReceipt>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerFirstTownLayout,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.FirstTownLayout(action),
            action, deviceKey, cancellationToken);

    public Task<OwnerFounderMoveReceipt> MoveFounderAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        OwnerFounderMoveAction action, IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerFounderMoveAction, OwnerFounderMoveReceipt>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerFounderMove,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.FounderMove(action),
            action, deviceKey, cancellationToken);

    public Task<OwnerFounderUndoReceipt> UndoFounderAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        OwnerFounderUndoAction action, IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerFounderUndoAction, OwnerFounderUndoReceipt>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerFounderUndo,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.FounderUndo(action),
            action, deviceKey, cancellationToken);

    public Task<OwnerAgentPlacementReceipt> PlaceAgentAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        OwnerAgentPlacementAction action, IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerAgentPlacementAction, OwnerAgentPlacementReceipt>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerAgentPlace,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.AgentPlacement(action),
            action, deviceKey, cancellationToken);

    public Task<OwnerAgentRenameReceipt> RenameAgentAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        OwnerAgentRenameAction action, IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerAgentRenameAction, OwnerAgentRenameReceipt>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerAgentRename,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.AgentRename(action),
            action, deviceKey, cancellationToken);

    public Task<OwnerControlReceipt> SetLifePaceAsync(Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        int rate, IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerLifePaceAction(rate);
        return pairing.SendSignedActionAsync<OwnerLifePaceAction, OwnerControlReceipt>(serverUri, authority, deviceId,
            OwnerPairingEndpoints.OwnerLifePace, OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.LifePace(action),
            action, deviceKey, cancellationToken);
    }

    public Task<OwnerControlReceipt> SetJevAssistanceAsync(Uri serverUri, OwnerAuthorityIdentity authority, string deviceId,
        bool enabled, IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerJevAssistanceAction(enabled);
        return pairing.SendSignedActionAsync<OwnerJevAssistanceAction, OwnerControlReceipt>(serverUri, authority, deviceId,
            OwnerPairingEndpoints.OwnerJevAssistance, OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.JevAssistance(action), action, deviceKey, cancellationToken);
    }

    public Task<OwnerInstructionReceipt> SubmitInstructionAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerInstructionAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerInstructionAction, OwnerInstructionReceipt>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerInstructions,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.Instruction(action),
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerAuthoringBatchReceipt> SubmitAuthoringAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerAuthoringBatchAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerAuthoringBatchAction, OwnerAuthoringBatchReceipt>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerAuthoring,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.Authoring(action),
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerBuildingPlacementResult> PlaceBuildingAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerBuildingPlacementAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerBuildingPlacementAction, OwnerBuildingPlacementResult>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerBuildingPlacement,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.BuildingPlacement(action),
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerProductionStartResult> StartProductionAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerProductionStartAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerProductionStartAction, OwnerProductionStartResult>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerProductionStart,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.ProductionStart(action),
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerBuildingDesignPreview> ReviewBuildingAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId, OwnerContentPackageIdAction action,
        IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerContentPackageIdAction, OwnerBuildingDesignPreview>(serverUri, authority, deviceId,
            "/api/v1/owner/content/building-review", OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.ContentPackageId("building-review", action), action, deviceKey, cancellationToken);

    public Task<OwnerBuildingDesignPreview> PreviewBuildingAsync(
        Uri serverUri, OwnerAuthorityIdentity authority, string deviceId, OwnerBuildingDesignAction action,
        IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerBuildingDesignAction, OwnerBuildingDesignPreview>(serverUri, authority, deviceId,
            "/api/v1/owner/content/building-preview", OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.BuildingDesign(action), action, deviceKey, cancellationToken);

    public Task<OwnerContentPackageReceipt> ProposeContentAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerContentPackageAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerContentPackageAction, OwnerContentPackageReceipt>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerContentPropose,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.ContentPropose(action),
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerContentPackageReceipt> ValidateContentAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerContentPackageIdAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        SendContentLifecycleAsync(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerContentValidate,
            "validate",
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerContentPackageReceipt> ApproveContentAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerContentPackageIdAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        SendContentLifecycleAsync(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerContentApprove,
            "approve",
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerContentPackageReceipt> StageContentAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerContentPackageIdAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        SendContentLifecycleAsync(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerContentStage,
            "stage",
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerContentPackageReceipt> RollbackContentAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerContentRollbackAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerContentRollbackAction, OwnerContentPackageReceipt>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerContentRollback,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.ContentRollback(action),
            action,
            deviceKey,
            cancellationToken);

    private Task<OwnerContentPackageReceipt> SendContentLifecycleAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        string path,
        string operation,
        OwnerContentPackageIdAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerContentPackageIdAction, OwnerContentPackageReceipt>(
            serverUri,
            authority,
            deviceId,
            path,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.ContentPackageId(operation, action),
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerPairingApproval> ApprovePairingAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerPairingApprovalAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerPairingApprovalAction, OwnerPairingApproval>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerPairingApproval,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.PairingApproval(action),
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerDevice> RevokeDeviceAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerDeviceManagementAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerDeviceManagementAction, OwnerDevice>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerDeviceRevoke,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.DeviceManagement(action),
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerDevice[]> ListDevicesAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken)
    {
        var action = new OwnerDeviceListAction();
        return pairing.SendSignedActionAsync<OwnerDeviceListAction, OwnerDevice[]>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerDeviceList,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.DeviceList(),
            action,
            deviceKey,
            cancellationToken);
    }

    public Task<OwnerProviderConfigurationStatus> GetProviderStatusAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken)
    {
        var action = new OwnerProviderStatusAction();
        return pairing.SendSignedActionAsync<OwnerProviderStatusAction, OwnerProviderConfigurationStatus>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerProviderStatus,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.ProviderStatus(),
            action,
            deviceKey,
            cancellationToken);
    }

    public Task<OwnerUsageStatus> GetUsageStatusAsync(Uri serverUri, OwnerAuthorityIdentity authority,
        string deviceId, IOwnerDeviceSigner deviceKey, CancellationToken cancellationToken)
    {
        var action = new OwnerUsageStatusAction();
        return pairing.SendSignedActionAsync<OwnerUsageStatusAction, OwnerUsageStatus>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerUsageStatus,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.UsageStatus(),
            action, deviceKey, cancellationToken);
    }

    public Task<OwnerUsageStatus> ConfigureUsageLimitAsync(Uri serverUri, OwnerAuthorityIdentity authority,
        string deviceId, OwnerUsageLimitAction action, IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerUsageLimitAction, OwnerUsageStatus>(
            serverUri, authority, deviceId, OwnerPairingEndpoints.OwnerUsageLimit,
            OwnerPairingProtocol.CreateRequestId(), OwnerWorldActionPayload.UsageLimit(action),
            action, deviceKey, cancellationToken);

    public Task<OwnerProviderConfigurationStatus> ConfigureProviderAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerProviderConfigurationAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken) =>
        pairing.SendSignedActionAsync<OwnerProviderConfigurationAction, OwnerProviderConfigurationStatus>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerProviderConfigure,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.ProviderConfiguration(action),
            action,
            deviceKey,
            cancellationToken);

    public Task<OwnerProviderConfigurationStatus> DeleteCredentialSlotAsync(
        Uri serverUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        string credentialSlotId,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken)
    {
        var action = new OwnerCredentialSlotDeletionAction(credentialSlotId);
        return pairing.SendSignedActionAsync<OwnerCredentialSlotDeletionAction, OwnerProviderConfigurationStatus>(
            serverUri,
            authority,
            deviceId,
            OwnerPairingEndpoints.OwnerCredentialSlotDelete,
            OwnerPairingProtocol.CreateRequestId(),
            OwnerWorldActionPayload.CredentialSlotDeletion(action),
            action,
            deviceKey,
            cancellationToken);
    }
}
