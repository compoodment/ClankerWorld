using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClankerWorld.GodotClient.Pairing;

/// <summary>A host has not advertised the format required for this action.</summary>
public sealed class OwnerActionCompatibilityException : InvalidOperationException
{
    public OwnerActionCompatibilityException()
        : base("The host and client need matching updates for this action.") { }
}

/// <summary>
/// Default absolute-path endpoints for the owner pairing and signed-action
/// protocol. These paths are relative to the supplied ClankerWorld server URI.
/// </summary>
public static class OwnerPairingEndpoints
{
    public const string Pairings = "/api/v1/pairings";
    public const string PairingActivation = "/api/v1/pairings/activate";
    public const string ChallengeIssue = "/api/v1/owner/challenges";
    public const string OwnerReconnect = "/api/v1/owner/reconnect";
    public const string OwnerPause = "/api/v1/owner/control/pause";
    public const string OwnerLifePace = "/api/v1/owner/control/life-pace";
    public const string OwnerJevAssistance = "/api/v1/owner/control/jev-assistance";
    public const string OwnerResume = "/api/v1/owner/control/resume";
    public const string OwnerStartWorld = "/api/v1/owner/control/start-world";
    public const string OwnerSaveList = "/api/v1/owner/saves/list";
    public const string OwnerSaveCreate = "/api/v1/owner/saves/create";
    public const string OwnerSaveOverwrite = "/api/v1/owner/saves/overwrite";
    public const string OwnerSaveLoad = "/api/v1/owner/saves/load";
    public const string OwnerWorldList = "/api/v1/owner/worlds/list";
    public const string OwnerWorldCreate = "/api/v1/owner/worlds/create";
    public const string OwnerWorldPreview = "/api/v1/owner/worlds/preview";
    public const string OwnerWorldSelect = "/api/v1/owner/worlds/select";
    public const string OwnerAutosaveStatus = "/api/v1/owner/saves/autosave/status";
    public const string OwnerAutosaveConfigure = "/api/v1/owner/saves/autosave/configure";
    public const string OwnerFounderPlace = "/api/v1/owner/founders/place";
    public const string OwnerFounderMove = "/api/v1/owner/founders/move";
    public const string OwnerFounderUndo = "/api/v1/owner/founders/undo";
    public const string OwnerFirstTownLayout = "/api/v1/owner/town/first-layout";
    public const string OwnerAgentPlace = "/api/v1/owner/agents/place";
    public const string OwnerAgentRename = "/api/v1/owner/agents/rename";
    public const string OwnerInstructions = "/api/v1/owner/instructions";
    public const string OwnerAuthoring = "/api/v1/owner/authoring";
    public const string OwnerContentPropose = "/api/v1/owner/content/propose";
    public const string OwnerContentValidate = "/api/v1/owner/content/validate";
    public const string OwnerContentApprove = "/api/v1/owner/content/approve";
    public const string OwnerContentStage = "/api/v1/owner/content/stage";
    public const string OwnerContentRollback = "/api/v1/owner/content/rollback";
    public const string OwnerBuildingPlacement = "/api/v1/owner/buildings/place";
    public const string OwnerProductionStart = "/api/v1/owner/production/start";
    public const string OwnerPairingApproval = "/api/v1/owner/pairings/approve";
    public const string OwnerDeviceRevoke = "/api/v1/owner/devices/revoke";
    public const string OwnerDeviceList = "/api/v1/owner/devices/list";
    public const string OwnerProviderStatus = "/api/v1/owner/providers/status";
    public const string OwnerUsageStatus = "/api/v1/owner/usage/status";
    public const string OwnerUsageLimit = "/api/v1/owner/usage/limit";
    public const string OwnerProviderConfigure = "/api/v1/owner/providers/configure";
    public const string OwnerCredentialSlotDelete = "/api/v1/owner/providers/slots/delete";
}

/// <summary>
/// Allows an embedding client to override pairing endpoint paths while retaining
/// the protocol defaults.
/// </summary>
public sealed record OwnerPairingEndpointPaths(
    string PairingsPath = OwnerPairingEndpoints.Pairings,
    string PairingActivationPath = OwnerPairingEndpoints.PairingActivation,
    string ChallengeIssuePath = OwnerPairingEndpoints.ChallengeIssue,
    string OwnerReconnectPath = OwnerPairingEndpoints.OwnerReconnect)
{
    internal void Validate()
    {
        OwnerPairingProtocol.ValidateRelativeAbsolutePath(PairingsPath);
        OwnerPairingProtocol.ValidateRelativeAbsolutePath(PairingActivationPath);
        OwnerPairingProtocol.ValidateRelativeAbsolutePath(ChallengeIssuePath);
        OwnerPairingProtocol.ValidateRelativeAbsolutePath(OwnerReconnectPath);
    }
}

/// <summary>
/// HTTP adapter for owner-device pairing and signed actions. It never creates
/// or transmits a bearer token: pairing is public-key registration, and each
/// protected action proves possession of the device key against a one-use
/// server challenge.
/// </summary>
public sealed class OwnerPairingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    private readonly HttpClient httpClient;
    private readonly OwnerPairingEndpointPaths endpoints;

    /// <summary>
    /// Creates a reusable client. The caller owns the supplied
    /// <see cref="HttpClient"/> and is responsible for its lifetime.
    /// </summary>
    public OwnerPairingClient(HttpClient httpClient, OwnerPairingEndpointPaths? endpoints = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.endpoints = endpoints ?? new OwnerPairingEndpointPaths();
        this.endpoints.Validate();
    }

    /// <summary>
    /// Registers a device's public P-256 SPKI and validates that the returned
    /// activation proof is bound to that exact public key before returning it.
    /// </summary>
    public async Task<OwnerPairingStart> StartPairingAsync(
        Uri serverBaseUri,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);
        var pairing = await SendJsonAsync<OwnerPairingStartRequest, OwnerPairingStart>(
            HttpMethod.Post,
            BuildEndpointUri(serverBaseUri, endpoints.PairingsPath),
            new OwnerPairingStartRequest(deviceKey.PublicKeySpkiBase64),
            cancellationToken).ConfigureAwait(false);

        ValidatePairingStart(pairing, deviceKey);
        return pairing;
    }

    /// <summary>
    /// Polls a pairing status. This endpoint has no bearer credential because
    /// the response excludes the pairing code, private key, and signatures.
    /// </summary>
    public async Task<OwnerPairingStatus> GetPairingStatusAsync(
        Uri serverBaseUri,
        string pairingId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingId);
        var status = await GetJsonAsync<OwnerPairingStatus>(
            BuildEndpointUri(serverBaseUri, $"{endpoints.PairingsPath}/{Uri.EscapeDataString(pairingId)}"),
            cancellationToken).ConfigureAwait(false);

        if (!string.Equals(status.PairingId, pairingId, StringComparison.Ordinal) || status.Authority is null)
        {
            throw new InvalidDataException("Server returned a pairing status for a different or malformed pairing.");
        }

        return status;
    }

    /// <summary>
    /// Builds a proof-of-possession activation request after independently
    /// reconstructing the canonical proof the server must have returned.
    /// </summary>
    public static OwnerPairingActivationRequest CreateActivationRequest(
        OwnerPairingStart pairing,
        IOwnerDeviceSigner deviceKey)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        ArgumentNullException.ThrowIfNull(deviceKey);
        ValidatePairingStart(pairing, deviceKey);
        return new OwnerPairingActivationRequest(
            pairing.PairingId,
            pairing.ActivationCanonicalProof,
            deviceKey.SignCanonicalProof(pairing.ActivationCanonicalProof));
    }

    /// <summary>
    /// Activates an already locally approved pairing using proof of possession
    /// of the private key that supplied its public SPKI.
    /// </summary>
    public async Task<OwnerDevice> ActivatePairingAsync(
        Uri serverBaseUri,
        OwnerPairingStart pairing,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken)
    {
        var activation = CreateActivationRequest(pairing, deviceKey);
        var device = await SendJsonAsync<OwnerPairingActivationRequest, OwnerDevice>(
            HttpMethod.Post,
            BuildEndpointUri(serverBaseUri, endpoints.PairingActivationPath),
            activation,
            cancellationToken).ConfigureAwait(false);

        if (!string.Equals(device.DeviceId, pairing.DeviceId, StringComparison.Ordinal) ||
            !string.Equals(device.PublicKeyFingerprint, deviceKey.PublicKeyFingerprint, StringComparison.Ordinal) ||
            device.State != OwnerDeviceState.Active)
        {
            throw new InvalidDataException("Server returned a malformed or mismatched activated device.");
        }

        return device;
    }

    /// <summary>
    /// Builds a P-256 proof for a challenge issuance request.
    /// </summary>
    public static OwnerChallengeIssueRequest CreateChallengeIssueRequest(
        OwnerAuthorityIdentity authority,
        string deviceId,
        string requestId,
        IOwnerDeviceSigner deviceKey)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(deviceKey);

        var canonicalProof = OwnerPairingProtocol.CreateChallengeIssueCanonicalProof(
            authority,
            deviceId,
            requestId);
        return new OwnerChallengeIssueRequest(
            deviceId,
            requestId,
            canonicalProof,
            deviceKey.SignCanonicalProof(canonicalProof));
    }

    /// <summary>
    /// Requests a short-lived one-use challenge after signing the issuance
    /// request. The response is checked against the expected authority and
    /// device before it can be used for an action.
    /// </summary>
    public async Task<OwnerChallenge> IssueChallengeAsync(
        Uri serverBaseUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        string requestId,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken)
    {
        var issueRequest = CreateChallengeIssueRequest(authority, deviceId, requestId, deviceKey);
        var challenge = await SendJsonAsync<OwnerChallengeIssueRequest, OwnerChallenge>(
            HttpMethod.Post,
            BuildEndpointUri(serverBaseUri, endpoints.ChallengeIssuePath),
            issueRequest,
            cancellationToken).ConfigureAwait(false);

        if (challenge.Authority is null ||
            !Equals(challenge.Authority, authority) ||
            !string.Equals(challenge.DeviceId, deviceId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(challenge.ChallengeId) ||
            string.IsNullOrWhiteSpace(challenge.Nonce))
        {
            throw new InvalidDataException("Server returned a malformed or mismatched owner challenge.");
        }

        return challenge;
    }

    /// <summary>
    /// Builds the flattened signed-action envelope expected by owner-only
    /// endpoints. The canonical payload is never sent as an authority claim;
    /// it is hashed into the binding and must be independently reconstructed by
    /// the server from the typed <paramref name="action"/> payload.
    /// </summary>
    public static OwnerSignedActionRequest<TAction> CreateSignedActionRequest<TAction>(
        OwnerAuthorityIdentity authority,
        string deviceId,
        OwnerChallenge challenge,
        HttpMethod method,
        string relativeAbsolutePath,
        string requestId,
        string canonicalPayload,
        TAction action,
        IOwnerDeviceSigner deviceKey)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(canonicalPayload);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(deviceKey);

        if (challenge.Authority is null ||
            !Equals(challenge.Authority, authority) ||
            !string.Equals(challenge.DeviceId, deviceId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(challenge.ChallengeId) ||
            string.IsNullOrWhiteSpace(challenge.Nonce))
        {
            throw new ArgumentException("The challenge is not bound to the supplied authority and device.", nameof(challenge));
        }

        var binding = OwnerPairingProtocol.CreateHttpActionBinding(
            method.Method,
            relativeAbsolutePath,
            requestId,
            canonicalPayload);
        var canonicalProof = OwnerPairingProtocol.CreateChallengeConsumeCanonicalProof(
            authority,
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding);
        return new OwnerSignedActionRequest<TAction>(
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding,
            canonicalProof,
            deviceKey.SignCanonicalProof(canonicalProof),
            requestId,
            action);
    }

    /// <summary>
    /// Posts a prebuilt signed action envelope. This is intentionally POST-only
    /// because the current owner action contract is signed POST endpoints.
    /// </summary>
    public Task<TResponse> PostSignedActionAsync<TAction, TResponse>(
        Uri serverBaseUri,
        string relativeAbsolutePath,
        OwnerSignedActionRequest<TAction> request,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(request);
        OwnerPairingProtocol.ValidateRelativeAbsolutePath(relativeAbsolutePath);
        return SendJsonAsync<OwnerSignedActionRequest<TAction>, TResponse>(
            HttpMethod.Post,
            BuildEndpointUri(serverBaseUri, relativeAbsolutePath),
            request,
            cancellationToken);
    }

    /// <summary>
    /// Convenience flow for a protected action: sign a challenge issuance
    /// request, receive its one-use challenge, build the action binding and
    /// consume proof, then POST the typed action envelope. It does not retain a
    /// token or reusable credential between calls.
    /// </summary>
    public async Task<TResponse> SendSignedActionAsync<TAction, TResponse>(
        Uri serverBaseUri,
        OwnerAuthorityIdentity authority,
        string deviceId,
        string relativeAbsolutePath,
        string requestId,
        string canonicalPayload,
        TAction action,
        IOwnerDeviceSigner deviceKey,
        CancellationToken cancellationToken,
        string? requiredPayloadDomain = null)
        where TResponse : class
    {
        // ResponseHeadersRead ends HttpClient's timeout at the headers. Keep
        // a deadline alive across both JSON bodies and the complete signed flow.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var maximum = TimeSpan.FromSeconds(15);
        deadline.CancelAfter(httpClient.Timeout == Timeout.InfiniteTimeSpan || httpClient.Timeout > maximum
            ? maximum : httpClient.Timeout);
        cancellationToken = deadline.Token;
        var challenge = await IssueChallengeAsync(
            serverBaseUri,
            authority,
            deviceId,
            requestId,
            deviceKey,
            cancellationToken).ConfigureAwait(false);
        if (requiredPayloadDomain is not null &&
            !(challenge.SupportedActionPayloads ?? []).Contains(requiredPayloadDomain, StringComparer.Ordinal))
        {
            throw new OwnerActionCompatibilityException();
        }
        var request = CreateSignedActionRequest(
            authority,
            deviceId,
            challenge,
            HttpMethod.Post,
            relativeAbsolutePath,
            requestId,
            canonicalPayload,
            action,
            deviceKey);
        return await PostSignedActionAsync<TAction, TResponse>(
            serverBaseUri,
            relativeAbsolutePath,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts a prebuilt signed reconnect action using the configured default
    /// reconnect path.
    /// </summary>
    public Task<TResponse> PostOwnerReconnectAsync<TAction, TResponse>(
        Uri serverBaseUri,
        OwnerSignedActionRequest<TAction> request,
        CancellationToken cancellationToken)
        where TResponse : class =>
        PostSignedActionAsync<TAction, TResponse>(
            serverBaseUri,
            endpoints.OwnerReconnectPath,
            request,
            cancellationToken);

    private static void ValidatePairingStart(OwnerPairingStart pairing, IOwnerDeviceSigner deviceKey)
    {
        if (pairing.Authority is null ||
            string.IsNullOrWhiteSpace(pairing.Authority.ServerAuthorityId) ||
            string.IsNullOrWhiteSpace(pairing.Authority.WorldId) ||
            string.IsNullOrWhiteSpace(pairing.PairingId) ||
            string.IsNullOrWhiteSpace(pairing.DeviceId) ||
            !string.Equals(pairing.PublicKeyFingerprint, deviceKey.PublicKeyFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Server returned a malformed or mismatched pairing start response.");
        }

        var expectedCanonicalProof = OwnerPairingProtocol.CreatePairingActivationCanonicalProof(
            pairing.Authority,
            pairing.PairingId,
            pairing.DeviceId,
            pairing.PublicKeyFingerprint);
        if (!string.Equals(pairing.ActivationCanonicalProof, expectedCanonicalProof, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Server returned an activation proof that does not bind this pairing response.");
        }
    }

    private async Task<TResponse> GetJsonAsync<TResponse>(Uri endpointUri, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpointUri);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredJsonAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        Uri endpointUri,
        TRequest requestBody,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, endpointUri)
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredJsonAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TResponse> ReadRequiredJsonAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var parsed = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return parsed ?? throw new InvalidDataException("Server returned an empty JSON response.");
    }

    private static Uri BuildEndpointUri(Uri serverBaseUri, string relativeAbsolutePath)
    {
        ArgumentNullException.ThrowIfNull(serverBaseUri);
        if (!serverBaseUri.IsAbsoluteUri ||
            (!string.Equals(serverBaseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(serverBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The ClankerWorld server URI must be an absolute HTTP(S) URI.", nameof(serverBaseUri));
        }

        // Pairing and owner proofs must not cross plaintext LAN/Internet
        // transport. A literal loopback IP remains available for an explicit
        // local-development host; names such as localhost are not accepted
        // because their resolver configuration is outside this client.
        if (string.Equals(serverBaseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            (!IPAddress.TryParse(serverBaseUri.Host, out var address) || !IPAddress.IsLoopback(address)))
        {
            throw new ArgumentException(
                "Owner pairing requires HTTPS unless the server is a literal loopback IP used for local development.",
                nameof(serverBaseUri));
        }

        OwnerPairingProtocol.ValidateRelativeAbsolutePath(relativeAbsolutePath);
        return new Uri(serverBaseUri, relativeAbsolutePath);
    }
}
