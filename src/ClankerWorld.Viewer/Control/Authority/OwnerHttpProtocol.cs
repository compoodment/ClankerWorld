using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// Transport DTOs and canonical bytes for the owner-device HTTP boundary.
/// These types intentionally carry no bearer token. Every protected operation
/// is authorized by consuming a short-lived, one-use signed challenge.
/// </summary>
public sealed record OwnerDeletionAction(string Kind, string Id, string WorldId, DateTimeOffset? ExpectedCreatedUtc = null);
public sealed record OwnerDeletionReceipt(string Id, bool CleanupComplete);

public sealed record StartOwnerPairingHttpRequest(string PublicKeySpkiBase64);

public sealed record ActivateOwnerPairingHttpRequest(
    string PairingId,
    string CanonicalProof,
    string SignatureBase64);

public sealed record IssueOwnerChallengeHttpRequest(
    string DeviceId,
    string RequestId,
    string CanonicalProof,
    string SignatureBase64);

public sealed record OwnerSignedHttpRequest<TAction>(
    string DeviceId,
    string ChallengeId,
    string Nonce,
    string Binding,
    string CanonicalProof,
    string SignatureBase64,
    string RequestId,
    TAction Action)
    where TAction : class;

public sealed record OwnerReconnectAction(long AfterEventId,
    string? KnownTerrainWorldId = null, string? KnownTerrainDigest = null,
    string? KnownMapLayersDigest = null);

public sealed record OwnerControlAction(string Operation);
public sealed record OwnerManualSaveAction(string Operation, string Value);
public sealed record OwnerWorldCreationAction(string Name, string Seed, string Size,
    int WaterPercent, bool WrapEastWest, string ClimateMode = "Balanced",
    string SelectedClimate = "Temperate", bool LatitudeCooling = true,
    string ResourceAbundance = "Normal", string ForestCover = "Normal",
    string MountainRelief = "Normal", string RiverAbundance = "Normal");
public sealed record OwnerAutosaveConfigurationAction(bool Enabled, int IntervalMinutes, int RotationCount);
public sealed record OwnerLifePaceAction(int Rate);
public sealed record OwnerJevAssistanceAction(bool Enabled);

public sealed record OwnerPairingApprovalAction(string PairingId, string PairingCode);

public sealed record OwnerDeviceManagementAction(string DeviceId);

/// <summary>
/// Deliberately empty action body for a signed paired-device registry query.
/// The endpoint and its fixed canonical payload make this read just as
/// challenge-bound and one-use as a world-changing owner request.
/// </summary>
public sealed record OwnerDeviceListAction;

/// <summary>
/// Empty signed action used to read only non-secret provider status. API keys
/// are never represented in the response contract.
/// </summary>
public sealed record OwnerProviderStatusAction;

public sealed record OwnerUsageStatusAction;

public sealed record OwnerCredentialSlotDeletionAction(string CredentialSlotId);

/// <summary>
/// Asks the host for the game's model list for a provider. With
/// <see cref="CheckKey"/>, the host also asks the provider which of those
/// models a key can use. <see cref="ApiKey"/> is a key the owner has just
/// pasted and not saved yet; it is used for this check only. Without it, the
/// named key slot or the provider's saved key is used.
/// </summary>
public sealed record OwnerProviderModelListAction(
    string Provider, string? CredentialSlotId = null, string? ApiKey = null, bool CheckKey = true);

/// <summary>One listed model, and whether the checked key can use it.</summary>
public sealed record OwnerProviderModelChoice(string Model, bool Available);

/// <summary>
/// The game's models for a provider, in display order. <see cref="DefaultModel"/>
/// is the game's default for this provider, chosen for a new agent when the
/// key can use it. <see cref="Error"/> explains, in plain words, a key that
/// couldn't be checked; the models are then all shown as usable.
/// </summary>
public sealed record OwnerProviderModelList(
    string Provider, IReadOnlyList<OwnerProviderModelChoice> Models, string DefaultModel, string? Error);

public sealed record OwnerProviderConfigurationAction(
    string Role,
    string Provider,
    string? Model,
    string? ApiKey,
    bool ForgetCredential,
    string? InhabitantId = null,
    string? CredentialSlotId = null,
    string? NewCredentialLabel = null);

public sealed record InhabitantProviderAssignment(string InhabitantId, string Role, string Provider, string? Model = null, string? CredentialSlotId = null);

public sealed record OwnerProviderCredentialStatus(string Id, string Provider, string Label);

public sealed record OwnerFounderPlacementAction(
    string FounderId, int X, int Y, OwnerProviderConfigurationAction Cognition);

public sealed record OwnerFounderPlacementReceipt(string FounderId, string HouseholdId, int Placed, int Required);
public sealed record OwnerFounderMoveAction(string FounderId, int X, int Y);
public sealed record OwnerFounderMoveReceipt(string FounderId, int X, int Y, bool Changed);
public sealed record OwnerFounderUndoAction(string FounderId);
public sealed record OwnerFounderUndoReceipt(string FounderId, int Placed, int Required);
public sealed record OwnerFirstTownLayoutAction(int X, int Y);
public sealed record OwnerFirstTownLayoutReceipt(int X, int Y, int Buildings, int RoadTiles);

public sealed record OwnerAgentPlacementAction(
    string AgentId, int X, int Y, OwnerProviderConfigurationAction Cognition);

public sealed record OwnerAgentPlacementReceipt(string AgentId, string? HouseholdId);

public sealed record OwnerAgentRenameAction(string AgentId, string Name);

public sealed record OwnerAgentRenameReceipt(string AgentId, string Name, bool Changed);

public sealed record OwnerProviderOptionStatus(
    string Provider,
    string Model,
    bool HasCredential);

public sealed record OwnerProviderConfigurationStatus(
    string RoutineProvider,
    string PlanningProvider,
    long Revision,
    IReadOnlyList<OwnerProviderOptionStatus> Providers,
    IReadOnlyList<InhabitantProviderAssignment>? Assignments = null,
    IReadOnlyList<OwnerProviderCredentialStatus>? CredentialSlots = null);

public sealed record OwnerInstructionAction(
    string IdempotencyKey,
    string TargetInhabitantId,
    string Kind,
    string Text);

/// <summary>
/// A stable scalar representation keeps authoring requests independent of a
/// client JSON serializer's ordering or polymorphism behavior. The server
/// maps it to a typed simulation operation only after signed authorization.
/// </summary>
public sealed record OwnerAuthoringOperationAction(
    string Kind,
    string? Id,
    string? Value,
    string? SecondaryValue,
    int? X,
    int? Y,
    bool? IsRenewable);

public sealed record OwnerAuthoringBatchAction(
    string BatchId,
    IReadOnlyList<OwnerAuthoringOperationAction> Operations);

public static class OwnerHttpBinding
{
    public const string Domain = "clankerworld.owner-http-binding.v1";

    public static string Create(
        string method,
        string path,
        string requestId,
        string canonicalPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(canonicalPayload);
        if (!path.StartsWith('/') || path.Contains('?'))
        {
            throw new ArgumentException("A binding path must be an absolute path without a query string.", nameof(path));
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload));
        return string.Join(
            '\n',
            Domain,
            $"method={ToBase64Url(Encoding.UTF8.GetBytes(method.ToUpperInvariant()))}",
            $"path={ToBase64Url(Encoding.UTF8.GetBytes(path))}",
            $"request-id={ToBase64Url(Encoding.UTF8.GetBytes(requestId))}",
            $"payload-sha256={ToBase64Url(digest)}");
    }

    public static string ReconnectPayload(OwnerReconnectAction action) =>
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

    public static string EmptyPayload(string operation) => string.Join(
        '\n',
        "clankerworld.owner-control.v1",
        $"operation={EncodeRequired(operation, nameof(operation))}");

    public static string Deletion(OwnerDeletionAction action) => string.Join(
        '\n', "clankerworld.owner-deletion.v1",
        $"kind={EncodeRequired(action.Kind, nameof(action.Kind))}",
        $"id={EncodeRequired(action.Id, nameof(action.Id))}",
        $"world-id={EncodeRequired(action.WorldId, nameof(action.WorldId))}",
        $"created-utc={action.ExpectedCreatedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "-"}");

    public static string ManualSavePayload(OwnerManualSaveAction action) => string.Join(
        '\n',
        "clankerworld.owner-manual-save.v1",
        $"operation={EncodeRequired(action.Operation, nameof(action.Operation))}",
        $"value={EncodeRequired(action.Value, nameof(action.Value))}");

    public const string WorldCreationPayloadDomain = "clankerworld.owner-world-creation.v2";

    public static string WorldCreationPayload(OwnerWorldCreationAction action) => string.Join(
        '\n',
        WorldCreationPayloadDomain,
        $"name={EncodeRequired(action.Name, nameof(action.Name))}",
        $"seed={EncodeRequired(action.Seed, nameof(action.Seed))}",
        $"size={EncodeRequired(action.Size, nameof(action.Size))}",
        $"water-percent={action.WaterPercent.ToString(CultureInfo.InvariantCulture)}",
        $"wrap-east-west={action.WrapEastWest.ToString().ToLowerInvariant()}",
        $"climate-mode={EncodeRequired(action.ClimateMode, nameof(action.ClimateMode))}",
        $"selected-climate={EncodeRequired(action.SelectedClimate, nameof(action.SelectedClimate))}",
        $"latitude-cooling={action.LatitudeCooling.ToString().ToLowerInvariant()}",
        $"resource-abundance={EncodeRequired(action.ResourceAbundance, nameof(action.ResourceAbundance))}",
        $"forest-cover={EncodeRequired(action.ForestCover, nameof(action.ForestCover))}",
        $"mountain-relief={EncodeRequired(action.MountainRelief, nameof(action.MountainRelief))}",
        $"river-abundance={EncodeRequired(action.RiverAbundance, nameof(action.RiverAbundance))}");

    public static string AutosaveConfigurationPayload(OwnerAutosaveConfigurationAction action) => string.Join(
        '\n',
        "clankerworld.owner-autosave-configuration.v1",
        $"enabled={action.Enabled.ToString().ToLowerInvariant()}",
        $"interval-minutes={action.IntervalMinutes.ToString(CultureInfo.InvariantCulture)}",
        $"rotation-count={action.RotationCount.ToString(CultureInfo.InvariantCulture)}");

    public static string LifePacePayload(OwnerLifePaceAction action) =>
        "clankerworld.owner-life-pace.v1\nrate=" + action.Rate.ToString(CultureInfo.InvariantCulture);

    public static string JevAssistancePayload(OwnerJevAssistanceAction action) =>
        "clankerworld.owner-jev-assistance.v1\nenabled=" + action.Enabled.ToString().ToLowerInvariant();

    public static string PairingApprovalPayload(OwnerPairingApprovalAction action) => string.Join(
        '\n',
        "clankerworld.owner-pairing-approval.v1",
        $"pairing-id={EncodeRequired(action.PairingId, nameof(action.PairingId))}",
        $"pairing-code={EncodeRequired(action.PairingCode, nameof(action.PairingCode))}");

    public static string DeviceManagementPayload(OwnerDeviceManagementAction action) => string.Join(
        '\n',
        "clankerworld.owner-device-management.v1",
        $"device-id={EncodeRequired(action.DeviceId, nameof(action.DeviceId))}");

    public static string DeviceListPayload() => EmptyPayload("list_devices");

    public static string ProviderStatusPayload() => EmptyPayload("provider_status");

    public static string UsageStatusPayload() => EmptyPayload("usage_status");

    public static string UsageLimitPayload(ProviderUsageLimitAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.AttemptLimit is < 1 or > 1_000_000 || action.AdditionalCalls is < 0 or > 1_000_000 ||
            action.AdditionalCalls > 0 && action.AttemptLimit is not null)
            throw new ArgumentOutOfRangeException(nameof(action));
        return string.Join('\n', "clankerworld.owner-usage-limit.v1",
            $"attempt-limit={action.AttemptLimit?.ToString(CultureInfo.InvariantCulture) ?? "off"}",
            $"additional-calls={action.AdditionalCalls.ToString(CultureInfo.InvariantCulture)}");
    }

    public static string CredentialSlotDeletionPayload(OwnerCredentialSlotDeletionAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return string.Join('\n', "clankerworld.owner-credential-slot-deletion.v1",
            $"credential-slot={EncodeRequired(action.CredentialSlotId, nameof(action.CredentialSlotId))}");
    }

    public static string ProviderModelListPayload(OwnerProviderModelListAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var apiKeyDigest = action.ApiKey is null
            ? "-"
            : ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(action.ApiKey)));
        return string.Join('\n', "clankerworld.owner-provider-models.v1",
            $"provider={EncodeRequired(action.Provider, nameof(action.Provider))}",
            $"credential-slot={EncodeOptional(action.CredentialSlotId)}",
            $"api-key-sha256={apiKeyDigest}",
            $"check-key={action.CheckKey.ToString().ToLowerInvariant()}");
    }

    public static string ProviderConfigurationPayload(OwnerProviderConfigurationAction action)
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

    public static string FounderPlacementPayload(OwnerFounderPlacementAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var cognition = ProviderConfigurationPayload(action.Cognition);
        var digest = ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(cognition)));
        return string.Join('\n',
            "clankerworld.owner-founder-placement.v1",
            $"founder={EncodeRequired(action.FounderId, nameof(action.FounderId))}",
            $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
            $"y={action.Y.ToString(CultureInfo.InvariantCulture)}",
            $"cognition-sha256={digest}");
    }

    public static string FirstTownLayoutPayload(OwnerFirstTownLayoutAction action) => string.Join('\n',
        "clankerworld.owner-first-town-layout.v1",
        $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
        $"y={action.Y.ToString(CultureInfo.InvariantCulture)}");

    public static string FounderMovePayload(OwnerFounderMoveAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return string.Join('\n',
            "clankerworld.owner-founder-move.v1",
            $"founder={EncodeRequired(action.FounderId, nameof(action.FounderId))}",
            $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
            $"y={action.Y.ToString(CultureInfo.InvariantCulture)}");
    }

    public static string FounderUndoPayload(OwnerFounderUndoAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return string.Join('\n', "clankerworld.owner-founder-undo.v1",
            $"founder={EncodeRequired(action.FounderId, nameof(action.FounderId))}");
    }

    public static string AgentPlacementPayload(OwnerAgentPlacementAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var cognition = ProviderConfigurationPayload(action.Cognition);
        var digest = ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(cognition)));
        return string.Join('\n',
            "clankerworld.owner-agent-placement.v1",
            $"agent={EncodeRequired(action.AgentId, nameof(action.AgentId))}",
            $"x={action.X.ToString(CultureInfo.InvariantCulture)}",
            $"y={action.Y.ToString(CultureInfo.InvariantCulture)}",
            $"cognition-sha256={digest}");
    }

    public static string AgentRenamePayload(OwnerAgentRenameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var name = EncodeRequired(action.Name, nameof(action.Name));
        return string.Join('\n',
            "clankerworld.owner-agent-rename.v1",
            $"agent={EncodeRequired(action.AgentId, nameof(action.AgentId))}",
            $"name-sha256={ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(name)))}");
    }

    public static string InstructionPayload(OwnerInstructionAction action) => string.Join(
        '\n',
        "clankerworld.owner-instruction.v1",
        $"idempotency-key={EncodeRequired(action.IdempotencyKey, nameof(action.IdempotencyKey))}",
        $"target-inhabitant-id={EncodeRequired(action.TargetInhabitantId, nameof(action.TargetInhabitantId))}",
        $"kind={EncodeRequired(action.Kind, nameof(action.Kind))}",
        $"text={EncodeRequired(action.Text, nameof(action.Text))}");

    public static string AuthoringPayload(OwnerAuthoringBatchAction action)
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
            var operation = action.Operations[index] ?? throw new ArgumentException("Authoring operations cannot contain null.", nameof(action));
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

    private static string EncodeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return ToBase64Url(Encoding.UTF8.GetBytes(value));
    }

    private static string EncodeOptional(string? value) => value is null
        ? "-"
        : ToBase64Url(Encoding.UTF8.GetBytes(value));

    private static string EncodeOptionalInteger(int? value) => value is null
        ? "-"
        : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string EncodeOptionalBoolean(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "-",
    };

    private static string ToBase64Url(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

public sealed record OwnerRequestAuthorization(
    string DeviceId,
    string PublicKeyFingerprint,
    string RequestId);

public sealed record OwnerRequestAuthorizationResult(
    OwnerAuthorityFailure Failure,
    OwnerRequestAuthorization? Value)
{
    public bool IsSuccess => Failure == OwnerAuthorityFailure.None && Value is not null;
}

/// <summary>
/// Single ingress guard for signed owner operations. It compares the server's
/// reconstructed binding before consuming a process-local one-use challenge.
/// Restart invalidates all old challenges; only durable pairing/device changes
/// require a file rewrite before a caller asks the simulation to commit.
/// </summary>
public sealed class OwnerRequestAuthorizer(
    OwnerAuthorityStore authority,
    OwnerAuthorityStateFile stateFile)
{
    public OwnerRequestAuthorizationResult Authorize<TAction>(
        OwnerSignedHttpRequest<TAction>? request,
        string method,
        string path,
        string canonicalPayload)
        where TAction : class
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.DeviceId) ||
            string.IsNullOrWhiteSpace(request.ChallengeId) ||
            string.IsNullOrWhiteSpace(request.Nonce) ||
            string.IsNullOrWhiteSpace(request.CanonicalProof) ||
            string.IsNullOrWhiteSpace(request.SignatureBase64) ||
            string.IsNullOrWhiteSpace(request.RequestId) ||
            request.Action is null)
        {
            return new OwnerRequestAuthorizationResult(OwnerAuthorityFailure.InvalidRequest, null);
        }

        var expectedBinding = OwnerHttpBinding.Create(method, path, request.RequestId, canonicalPayload);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedBinding),
                Encoding.UTF8.GetBytes(request.Binding ?? string.Empty)))
        {
            return new OwnerRequestAuthorizationResult(OwnerAuthorityFailure.InvalidCanonicalProof, null);
        }

        var consumed = authority.ConsumeChallenge(new OwnerChallengeConsumeRequest(
            request.DeviceId,
            request.ChallengeId,
            request.Nonce,
            expectedBinding,
            request.CanonicalProof,
            request.SignatureBase64));
        // Persist any pairing/device cleanup, but not process-local challenges.
        // A restart discards all pending and consumed challenges, failing closed.
        stateFile.Save(authority);
        if (!consumed.IsSuccess)
        {
            return new OwnerRequestAuthorizationResult(consumed.Failure, null);
        }

        return new OwnerRequestAuthorizationResult(
            OwnerAuthorityFailure.None,
            new OwnerRequestAuthorization(
                consumed.Value!.DeviceId,
                consumed.Value.PublicKeyFingerprint,
                request.RequestId));
    }
}
