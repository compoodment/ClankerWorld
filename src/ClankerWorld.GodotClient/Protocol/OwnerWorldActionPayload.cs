using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.GodotClient.Pairing;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Canonical scalar payloads for every currently exposed owner operation.
/// They mirror the server contract rather than trusting JSON field order.
/// </summary>
public static class OwnerWorldActionPayload
{
    public static string Reconnect(OwnerReconnectAction action) =>
        action.KnownMapLayersDigest is not null
            ? string.Join('\n', "clankerworld.owner-reconnect.v3",
                $"after-event-id={action.AfterEventId.ToString(CultureInfo.InvariantCulture)}",
                $"terrain-world-id={EncodeRequired(action.KnownTerrainWorldId!, nameof(action.KnownTerrainWorldId))}",
                $"terrain-digest={EncodeRequired(action.KnownTerrainDigest!, nameof(action.KnownTerrainDigest))}",
                $"map-layers-digest={EncodeRequired(action.KnownMapLayersDigest, nameof(action.KnownMapLayersDigest))}")
            : action.KnownTerrainWorldId is null && action.KnownTerrainDigest is null
                ? string.Join('\n', "clankerworld.owner-reconnect.v1",
                    $"after-event-id={action.AfterEventId.ToString(CultureInfo.InvariantCulture)}")
                : string.Join('\n', "clankerworld.owner-reconnect.v2",
                    $"after-event-id={action.AfterEventId.ToString(CultureInfo.InvariantCulture)}",
                    $"terrain-world-id={EncodeRequired(action.KnownTerrainWorldId!, nameof(action.KnownTerrainWorldId))}",
                    $"terrain-digest={EncodeRequired(action.KnownTerrainDigest!, nameof(action.KnownTerrainDigest))}");

    public static string Control(string operation) => string.Join(
        '\n',
        "clankerworld.owner-control.v1",
        $"operation={EncodeRequired(operation, nameof(operation))}");

    public static string ManualSave(OwnerManualSaveAction action) => string.Join(
        '\n',
        "clankerworld.owner-manual-save.v1",
        $"operation={EncodeRequired(action.Operation, nameof(action.Operation))}",
        $"value={EncodeRequired(action.Value, nameof(action.Value))}");

    public static string WorldCreation(OwnerWorldCreationAction action) => string.Join(
        '\n',
        "clankerworld.owner-world-creation.v1",
        $"name={EncodeRequired(action.Name, nameof(action.Name))}",
        $"seed={EncodeRequired(action.Seed, nameof(action.Seed))}",
        $"size={EncodeRequired(action.Size, nameof(action.Size))}",
        $"water-percent={action.WaterPercent.ToString(CultureInfo.InvariantCulture)}",
        $"wrap-east-west={action.WrapEastWest.ToString().ToLowerInvariant()}",
        $"climate-mode={EncodeRequired(action.ClimateMode, nameof(action.ClimateMode))}",
        $"selected-climate={EncodeRequired(action.SelectedClimate, nameof(action.SelectedClimate))}",
        $"latitude-cooling={action.LatitudeCooling.ToString().ToLowerInvariant()}",
        $"resource-abundance={EncodeRequired(action.ResourceAbundance, nameof(action.ResourceAbundance))}");

    public static string AutosaveConfiguration(OwnerAutosaveConfigurationAction action) => string.Join(
        '\n',
        "clankerworld.owner-autosave-configuration.v1",
        $"enabled={action.Enabled.ToString().ToLowerInvariant()}",
        $"interval-minutes={action.IntervalMinutes.ToString(CultureInfo.InvariantCulture)}",
        $"rotation-count={action.RotationCount.ToString(CultureInfo.InvariantCulture)}");

    public static string LifePace(OwnerLifePaceAction action) =>
        "clankerworld.owner-life-pace.v1\nrate=" + action.Rate.ToString(CultureInfo.InvariantCulture);

    public static string JevAssistance(OwnerJevAssistanceAction action) =>
        "clankerworld.owner-jev-assistance.v1\nenabled=" + action.Enabled.ToString().ToLowerInvariant();

    public static string PairingApproval(OwnerPairingApprovalAction action) => string.Join(
        '\n',
        "clankerworld.owner-pairing-approval.v1",
        $"pairing-id={EncodeRequired(action.PairingId, nameof(action.PairingId))}",
        $"pairing-code={EncodeRequired(action.PairingCode, nameof(action.PairingCode))}");

    public static string DeviceManagement(OwnerDeviceManagementAction action) => string.Join(
        '\n',
        "clankerworld.owner-device-management.v1",
        $"device-id={EncodeRequired(action.DeviceId, nameof(action.DeviceId))}");

    public static string DeviceList() => Control("list_devices");

    public static string ProviderStatus() => Control("provider_status");

    public static string UsageStatus() => Control("usage_status");

    public static string UsageLimit(OwnerUsageLimitAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.AttemptLimit is < 1 or > 1_000_000 || action.AdditionalCalls is < 0 or > 1_000_000 ||
            action.AdditionalCalls > 0 && action.AttemptLimit is not null)
            throw new ArgumentOutOfRangeException(nameof(action));
        return string.Join('\n', "clankerworld.owner-usage-limit.v1",
            $"attempt-limit={action.AttemptLimit?.ToString(CultureInfo.InvariantCulture) ?? "off"}",
            $"additional-calls={action.AdditionalCalls.ToString(CultureInfo.InvariantCulture)}");
    }

    public static string CredentialSlotDeletion(OwnerCredentialSlotDeletionAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return string.Join('\n', "clankerworld.owner-credential-slot-deletion.v1",
            $"credential-slot={EncodeRequired(action.CredentialSlotId, nameof(action.CredentialSlotId))}");
    }

    public static string ProviderConfiguration(OwnerProviderConfigurationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var apiKeyDigest = action.ApiKey is null
            ? "-"
            : ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(action.ApiKey)));
        var payload = string.Join(
            '\n',
            "clankerworld.owner-provider-configuration.v1",
            $"role={EncodeRequired(action.Role, nameof(action.Role))}",
            $"provider={EncodeRequired(action.Provider, nameof(action.Provider))}",
            $"model={EncodeOptional(action.Model)}",
            $"api-key-sha256={apiKeyDigest}",
            $"forget-credential={action.ForgetCredential.ToString().ToLowerInvariant()}");
        if (action.InhabitantId is not null)
            payload += "\ninhabitant=" + EncodeRequired(action.InhabitantId, nameof(action.InhabitantId));
        if (action.CredentialSlotId is not null)
            payload += "\ncredential-slot=" + EncodeRequired(action.CredentialSlotId, nameof(action.CredentialSlotId));
        if (action.NewCredentialLabel is not null)
            payload += "\ncredential-label=" + EncodeRequired(action.NewCredentialLabel, nameof(action.NewCredentialLabel));
        return payload;
    }

    public static string FounderPlacement(OwnerFounderPlacementAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var cognition = ProviderConfiguration(action.Cognition);
        var digest = ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(cognition)));
        return string.Join('\n',
            "clankerworld.owner-founder-placement.v1",
            $"founder={EncodeRequired(action.FounderId, nameof(action.FounderId))}",
            $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
            $"y={action.Y.ToString(CultureInfo.InvariantCulture)}",
            $"cognition-sha256={digest}");
    }

    public static string FirstTownLayout(OwnerFirstTownLayoutAction action) => string.Join('\n',
        "clankerworld.owner-first-town-layout.v1",
        $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
        $"y={action.Y.ToString(CultureInfo.InvariantCulture)}");

    public static string FounderMove(OwnerFounderMoveAction action) => string.Join('\n',
        "clankerworld.owner-founder-move.v1",
        $"founder={EncodeRequired(action.FounderId, nameof(action.FounderId))}",
        $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
        $"y={action.Y.ToString(CultureInfo.InvariantCulture)}");

    public static string FounderUndo(OwnerFounderUndoAction action) => string.Join('\n',
        "clankerworld.owner-founder-undo.v1",
        $"founder={EncodeRequired(action.FounderId, nameof(action.FounderId))}");

    public static string AgentPlacement(OwnerAgentPlacementAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var cognition = ProviderConfiguration(action.Cognition);
        var digest = ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(cognition)));
        return string.Join('\n',
            "clankerworld.owner-agent-placement.v1",
            $"agent={EncodeRequired(action.AgentId, nameof(action.AgentId))}",
            $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
            $"y={action.Y.ToString(CultureInfo.InvariantCulture)}",
            $"cognition-sha256={digest}");
    }

    public static string AgentRename(OwnerAgentRenameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var name = EncodeRequired(action.Name, nameof(action.Name));
        return string.Join('\n',
            "clankerworld.owner-agent-rename.v1",
            $"agent={EncodeRequired(action.AgentId, nameof(action.AgentId))}",
            $"name-sha256={ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(name)))}");
    }

    public static string Instruction(OwnerInstructionAction action) => string.Join(
        '\n',
        "clankerworld.owner-instruction.v1",
        $"idempotency-key={EncodeRequired(action.IdempotencyKey, nameof(action.IdempotencyKey))}",
        $"target-inhabitant-id={EncodeRequired(action.TargetInhabitantId, nameof(action.TargetInhabitantId))}",
        $"kind={EncodeRequired(action.Kind, nameof(action.Kind))}",
        $"text={EncodeRequired(action.Text, nameof(action.Text))}");

    public static string Authoring(OwnerAuthoringBatchAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(action.Operations);
        var lines = new List<string>
        {
            "clankerworld.owner-authoring.v1",
            $"batch-id={EncodeRequired(action.BatchId, nameof(action.BatchId))}",
            $"operation-count={action.Operations.Count.ToString(CultureInfo.InvariantCulture)}",
        };

        for (var index = 0; index < action.Operations.Count; index++)
        {
            var operation = action.Operations[index] ?? throw new ArgumentException("Authoring operations cannot be null.", nameof(action));
            var prefix = $"op-{index.ToString(CultureInfo.InvariantCulture)}";
            lines.Add($"{prefix}.kind={EncodeRequired(operation.Kind, nameof(operation.Kind))}");
            lines.Add($"{prefix}.id={EncodeOptional(operation.Id)}");
            lines.Add($"{prefix}.value={EncodeOptional(operation.Value)}");
            lines.Add($"{prefix}.secondary-value={EncodeOptional(operation.SecondaryValue)}");
            lines.Add($"{prefix}.x={EncodeOptionalInteger(operation.X)}");
            lines.Add($"{prefix}.y={EncodeOptionalInteger(operation.Y)}");
            lines.Add($"{prefix}.is-renewable={EncodeOptionalBoolean(operation.IsRenewable)}");
        }

        return string.Join('\n', lines);
    }

    public static string BuildingDesign(OwnerBuildingDesignAction action) => string.Join('\n',
        "clankerworld.owner-building-design.v1",
        "name=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(action.Name)),
        "purpose=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(action.Purpose)),
        "wood-cost=" + action.WoodCost.ToString(CultureInfo.InvariantCulture));

    public static string ContentPropose(OwnerContentPackageAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(action.Dependencies);
        ArgumentNullException.ThrowIfNull(action.Definitions);
        ArgumentNullException.ThrowIfNull(action.DeclaredCapabilities);
        var assets = action.Assets ?? [];
        var lines = new List<string>
        {
            "clankerworld.owner-content-propose.v1",
            $"package-id={EncodeRequired(action.PackageId, nameof(action.PackageId))}",
            $"version={EncodeRequired(action.Version, nameof(action.Version))}",
            $"package-digest={EncodeRequired(action.PackageDigest, nameof(action.PackageDigest))}",
            $"dependency-count={action.Dependencies.Count.ToString(CultureInfo.InvariantCulture)}",
            $"definition-count={action.Definitions.Count.ToString(CultureInfo.InvariantCulture)}",
            $"asset-count={assets.Count.ToString(CultureInfo.InvariantCulture)}",
            $"capability-count={action.DeclaredCapabilities.Count.ToString(CultureInfo.InvariantCulture)}",
        };

        for (var index = 0; index < action.Dependencies.Count; index++)
        {
            var dependency = action.Dependencies[index] ?? throw new ArgumentException(
                "Content dependencies cannot contain null.",
                nameof(action));
            var prefix = $"dependency-{index.ToString(CultureInfo.InvariantCulture)}";
            lines.Add($"{prefix}.package-id={EncodeRequired(dependency.PackageId, nameof(dependency.PackageId))}");
            lines.Add($"{prefix}.minimum={EncodeRequired(dependency.MinimumVersion, nameof(dependency.MinimumVersion))}");
            lines.Add($"{prefix}.maximum={EncodeRequired(dependency.MaximumExclusiveVersion, nameof(dependency.MaximumExclusiveVersion))}");
            lines.Add($"{prefix}.optional={dependency.Optional.ToString().ToLowerInvariant()}");
        }

        for (var index = 0; index < action.Definitions.Count; index++)
        {
            var definition = action.Definitions[index] ?? throw new ArgumentException(
                "Content definitions cannot contain null.",
                nameof(action));
            var prefix = $"definition-{index.ToString(CultureInfo.InvariantCulture)}";
            lines.Add($"{prefix}.kind={EncodeRequired(definition.Kind, nameof(definition.Kind))}");
            lines.Add($"{prefix}.local-id={EncodeRequired(definition.LocalId, nameof(definition.LocalId))}");
            lines.Add($"{prefix}.version={EncodeRequired(definition.Version, nameof(definition.Version))}");
            lines.Add($"{prefix}.display-name={EncodeRequired(definition.DisplayName, nameof(definition.DisplayName))}");
            lines.Add($"{prefix}.payload-digest={EncodeRequired(definition.PayloadDigest, nameof(definition.PayloadDigest))}");
            lines.Add($"{prefix}.payload-json={EncodeOptional(definition.PayloadJson)}");
        }

        for (var index = 0; index < assets.Count; index++)
        {
            var asset = assets[index] ?? throw new ArgumentException(
                "Content asset reservations cannot contain null.",
                nameof(action));
            var prefix = $"asset-{index.ToString(CultureInfo.InvariantCulture)}";
            lines.Add($"{prefix}.asset-id={EncodeRequired(asset.AssetId, nameof(asset.AssetId))}");
            lines.Add($"{prefix}.normalized-digest={EncodeRequired(asset.NormalizedDigest, nameof(asset.NormalizedDigest))}");
            lines.Add($"{prefix}.decode-profile={EncodeRequired(asset.DecodeProfile, nameof(asset.DecodeProfile))}");
            lines.Add($"{prefix}.durable-storage={asset.DurableStorageBytes.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{prefix}.decoded-cache={asset.DecodedCacheBytes.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{prefix}.gpu-bytes={asset.GpuBytes.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{prefix}.render-units={asset.RenderUnits.ToString(CultureInfo.InvariantCulture)}");
        }

        for (var index = 0; index < action.DeclaredCapabilities.Count; index++)
        {
            lines.Add($"capability-{index.ToString(CultureInfo.InvariantCulture)}={EncodeRequired(
                action.DeclaredCapabilities[index],
                nameof(action.DeclaredCapabilities))}");
        }

        return string.Join('\n', lines);
    }

    public static string ContentPackageId(string operation, OwnerContentPackageIdAction action) => string.Join(
        '\n',
        "clankerworld.owner-content-lifecycle.v1",
        $"operation={EncodeRequired(operation, nameof(operation))}",
        $"package-id={EncodeRequired(action.PackageId, nameof(action.PackageId))}");

    public static string ContentRollback(OwnerContentRollbackAction action) => string.Join(
        '\n',
        "clankerworld.owner-content-rollback.v1",
        $"package-id={EncodeRequired(action.PackageId, nameof(action.PackageId))}",
        $"reason={EncodeRequired(action.Reason, nameof(action.Reason))}");

    public static string BuildingPlacement(OwnerBuildingPlacementAction action) => string.Join(
        '\n',
        "clankerworld.owner-building-placement.v1",
        $"instance-id={EncodeRequired(action.InstanceId, nameof(action.InstanceId))}",
        $"definition-id={EncodeRequired(action.DefinitionId, nameof(action.DefinitionId))}",
        $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
        $"y={action.Y.ToString(CultureInfo.InvariantCulture)}");

    public static string ProductionStart(OwnerProductionStartAction action) => string.Join(
        '\n',
        "clankerworld.owner-production-start.v1",
        $"recipe-id={EncodeRequired(action.RecipeId, nameof(action.RecipeId))}",
        $"building-instance-id={EncodeRequired(action.BuildingInstanceId, nameof(action.BuildingInstanceId))}",
        $"worker-id={EncodeRequired(action.WorkerId, nameof(action.WorkerId))}");

    private static string EncodeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return ToBase64Url(Encoding.UTF8.GetBytes(value));
    }

    private static string EncodeOptional(string? value) => value is null ? "-" : ToBase64Url(Encoding.UTF8.GetBytes(value));

    private static string EncodeOptionalInteger(int? value) => value is null
        ? "-"
        : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string EncodeOptionalBoolean(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "-",
    };

    private static string ToBase64Url(ReadOnlySpan<byte> value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
