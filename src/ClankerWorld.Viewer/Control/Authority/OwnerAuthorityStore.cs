using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// Supplies wall-clock time for operational authority state. Pairing and
/// challenge lifetimes intentionally do not use simulation ticks.
/// </summary>
public interface IOwnerAuthorityClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Supplies cryptographically strong random bytes in production and can be
/// replaced by deterministic bytes in focused tests.
/// </summary>
public interface IOwnerAuthorityRandom
{
    void Fill(Span<byte> destination);
}

public sealed class SystemOwnerAuthorityClock : IOwnerAuthorityClock
{
    public static SystemOwnerAuthorityClock Instance { get; } = new();

    private SystemOwnerAuthorityClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class CryptographicOwnerAuthorityRandom : IOwnerAuthorityRandom
{
    public static CryptographicOwnerAuthorityRandom Instance { get; } = new();

    private CryptographicOwnerAuthorityRandom()
    {
    }

    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}

/// <summary>
/// Stable identity that every owner proof binds to. A paired key is therefore
/// not silently valid against a different server or world.
/// </summary>
public sealed record OwnerAuthorityIdentity(string ServerAuthorityId, string WorldId);

/// <summary>
/// The only material a device contributes to pairing. It is a standard
/// SubjectPublicKeyInfo encoding of a named P-256 ECDSA public key, in base64.
/// </summary>
public sealed record OwnerPairingRequest(string PublicKeySpkiBase64);

public enum OwnerPairingState
{
    Pending,
    Approved,
    Active,
    Expired,
}

/// <summary>
/// Returned exactly when a pairing is created. The six-digit code is a human
/// confirmation value; the store retains only its SHA-256 hash.
/// </summary>
public sealed record OwnerPairingStart(
    OwnerAuthorityIdentity Authority,
    string PairingId,
    string DeviceId,
    string PairingCode,
    string PublicKeyFingerprint,
    DateTimeOffset ExpiresAtUtc,
    string ActivationCanonicalProof);

/// <summary>
/// Safe to expose to pairing status polling: it contains no code, private key,
/// bearer credential, or signature.
/// </summary>
public sealed record OwnerPairingStatus(
    OwnerAuthorityIdentity Authority,
    string PairingId,
    string DeviceId,
    string PublicKeyFingerprint,
    OwnerPairingState State,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Returned by the host-local approval path. This method is deliberately not
/// an HTTP endpoint; an integration host should expose it only through a local
/// administrator channel such as a service CLI or Unix socket.
/// </summary>
public sealed record OwnerPairingApproval(
    OwnerAuthorityIdentity Authority,
    string PairingId,
    string DeviceId,
    string PublicKeyFingerprint,
    string ActivationCanonicalProof);

/// <summary>
/// The device sends back the exact canonical proof it signed. The store
/// independently reconstructs it before verifying the P-256 SHA-256 proof.
/// </summary>
public sealed record OwnerPairingActivationRequest(
    string PairingId,
    string CanonicalProof,
    string SignatureBase64);

public enum OwnerDeviceState
{
    Active,
    Revoked,
}

/// <summary>
/// Public-key registration. The SPKI and its fingerprint are non-secret; no
/// private key or reusable bearer token is represented by this type.
/// </summary>
public sealed record OwnerDevice(
    string DeviceId,
    string PublicKeySpkiBase64,
    string PublicKeyFingerprint,
    OwnerDeviceState State,
    DateTimeOffset ActivatedAtUtc,
    DateTimeOffset? RevokedAtUtc);

/// <summary>
/// A signed request to mint a short-lived one-use challenge. RequestId is
/// caller-generated so a future HTTP/control layer can bind this proof to its
/// own idempotency and audit model.
/// </summary>
public sealed record OwnerChallengeIssueRequest(
    string DeviceId,
    string RequestId,
    string CanonicalProof,
    string SignatureBase64);

/// <summary>
/// The nonce is a one-use challenge, not a bearer credential. It must be
/// signed together with a later action binding before the store consumes it.
/// The store retains only the nonce hash.
/// </summary>
public sealed record OwnerChallenge(
    OwnerAuthorityIdentity Authority,
    string DeviceId,
    string ChallengeId,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Binding should be a deterministic identifier or canonical action digest
/// supplied by the future control ingress. It is included in the signed proof
/// so a challenge cannot be detached from the request it authorizes.
/// </summary>
public sealed record OwnerChallengeConsumeRequest(
    string DeviceId,
    string ChallengeId,
    string Nonce,
    string Binding,
    string CanonicalProof,
    string SignatureBase64);

public sealed record OwnerChallengeConsumption(
    OwnerAuthorityIdentity Authority,
    string DeviceId,
    string ChallengeId,
    string PublicKeyFingerprint,
    string Binding,
    DateTimeOffset ConsumedAtUtc);

/// <summary>
/// A durable, non-secret representation of authority state. It contains public
/// keys and one-way hashes only; it never contains a device private key, a raw
/// pairing code, a raw challenge nonce, or a bearer credential.
/// </summary>
public sealed record OwnerAuthorityState(
    int SchemaVersion,
    OwnerAuthorityIdentity Authority,
    IReadOnlyList<OwnerStoredPairing> Pairings,
    IReadOnlyList<OwnerStoredDevice> Devices,
    IReadOnlyList<OwnerStoredChallenge> Challenges);

public sealed record OwnerStoredPairing(
    string PairingId,
    string DeviceId,
    string PublicKeySpkiBase64,
    string PublicKeyFingerprint,
    string VisibleCodeHashBase64,
    DateTimeOffset ExpiresAtUtc,
    string ActivationCanonicalProof,
    OwnerPairingState State,
    int WrongVisibleCodeAttempts = 0);

public sealed record OwnerStoredDevice(
    string DeviceId,
    string PublicKeySpkiBase64,
    string PublicKeyFingerprint,
    OwnerDeviceState State,
    DateTimeOffset ActivatedAtUtc,
    DateTimeOffset? RevokedAtUtc);

public enum OwnerStoredChallengeState
{
    Pending,
    Consumed,
    Expired,
}

public sealed record OwnerStoredChallenge(
    string ChallengeId,
    string DeviceId,
    string NonceHashBase64,
    DateTimeOffset ExpiresAtUtc,
    OwnerStoredChallengeState State,
    DateTimeOffset? ConsumedAtUtc);

public enum OwnerAuthorityFailure
{
    None,
    InvalidRequest,
    InvalidPublicKey,
    PublicKeyAlreadyRegistered,
    PairingNotFound,
    PairingExpired,
    PairingNotPending,
    PairingNotApproved,
    PairingCodeMismatch,
    PairingCapacityExceeded,
    PairingAlreadyActive,
    InvalidCanonicalProof,
    InvalidSignature,
    DeviceNotFound,
    DeviceRevoked,
    ChallengeNotFound,
    ChallengeExpired,
    ChallengeConsumed,
    ChallengeDeviceMismatch,
    ChallengeNonceMismatch,
    ChallengeCapacityExceeded,
}

/// <summary>
/// A typed result keeps later HTTP mappings separate from authority policy.
/// No exception represents a normal authorization failure.
/// </summary>
public sealed record OwnerAuthorityResult<T>(OwnerAuthorityFailure Failure, T? Value)
    where T : class
{
    public bool IsSuccess => Failure == OwnerAuthorityFailure.None && Value is not null;

#pragma warning disable CA1000 // Typed result factories avoid a nullable default-value sentinel at every call site.
    public static OwnerAuthorityResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new OwnerAuthorityResult<T>(OwnerAuthorityFailure.None, value);
    }

    public static OwnerAuthorityResult<T> Fail(OwnerAuthorityFailure failure)
    {
        if (failure == OwnerAuthorityFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new OwnerAuthorityResult<T>(failure, null);
    }
#pragma warning restore CA1000
}

/// <summary>
/// In-memory server authority registry for owner-device pairing.
///
/// It has no HTTP knowledge and never mints a bearer token. Integrations pass
/// a typed request to this store, then map the result to their transport and
/// persistence mechanisms. All mutable state is guarded so a future host can
/// safely call it from concurrent endpoint handlers.
/// </summary>
public sealed class OwnerAuthorityStore
{
    public const string SignatureAlgorithm = "ecdsa-p256-sha256-p1363.v1";
    public const string PairingActivationProofDomain = "clankerworld.owner-pairing.activate.v1";
    public const string ChallengeIssueProofDomain = "clankerworld.owner-challenge.issue.v1";
    public const string ChallengeConsumeProofDomain = "clankerworld.owner-challenge.consume.v1";

    public static readonly TimeSpan PendingPairingLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(2);

    // These caps bound unauthenticated or paired-device operational state. Active
    // devices/pairings deliberately remain durable owner registrations and are
    // not evicted by this transient-state policy.
    public const int MaximumPendingPairings = 8;
    public const int MaximumRetainedExpiredPairings = 16;
    public const int MaximumWrongVisibleCodeAttempts = 5;
    public const int MaximumPendingChallenges = 32;
    public const int MaximumPendingChallengesPerDevice = 8;
    public const int MaximumRetainedTerminalChallenges = 64;

    private const int OpaqueIdentifierBytes = 32;
    private const int ChallengeNonceBytes = 32;
    private const int VisibleCodeLength = 6;
    private const int MaximumOpaqueInputLength = 512;
    private const int MaximumProofLength = 8192;
    private const int MaximumSignatureLength = 1024;
    private const int MaximumBindingLength = 4096;
    private const int MaximumSpkiBase64Length = 4096;
    private const int MaximumIdentifierAttempts = 32;

    private readonly IOwnerAuthorityClock clock;
    private readonly IOwnerAuthorityRandom random;
    private readonly object gate = new();
    private readonly Dictionary<string, PendingPairing> pairings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegisteredDevice> devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IssuedChallenge> challenges = new(StringComparer.Ordinal);

    public OwnerAuthorityStore(string serverAuthorityId, string worldId)
        : this(
            new OwnerAuthorityIdentity(serverAuthorityId, worldId),
            SystemOwnerAuthorityClock.Instance,
            CryptographicOwnerAuthorityRandom.Instance)
    {
    }

    public OwnerAuthorityStore(
        OwnerAuthorityIdentity identity,
        IOwnerAuthorityClock clock,
        IOwnerAuthorityRandom random)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(random);
        EnsureIdentity(identity);

        Identity = identity;
        this.clock = clock;
        this.random = random;
    }

    public OwnerAuthorityIdentity Identity { get; }

    /// <summary>
    /// Creates a durable non-secret state document. Operational expiries are
    /// applied before the copy is made, so restoring it cannot revive an
    /// expired pairing or challenge.
    /// </summary>
    public OwnerAuthorityState ExportState()
    {
        lock (gate)
        {
            ExpireOperationalStateUnsafe(UtcNow());
            return new OwnerAuthorityState(
                SchemaVersion: 1,
                Authority: Identity,
                Pairings: pairings.Values
                    .OrderBy(pairing => pairing.PairingId, StringComparer.Ordinal)
                    .Select(pairing => new OwnerStoredPairing(
                        pairing.PairingId,
                        pairing.DeviceId,
                        Convert.ToBase64String(pairing.PublicKeySpki),
                        pairing.PublicKeyFingerprint,
                        Convert.ToBase64String(pairing.VisibleCodeHash),
                        pairing.ExpiresAtUtc,
                        pairing.ActivationCanonicalProof,
                        pairing.State,
                        pairing.WrongVisibleCodeAttempts))
                    .ToArray(),
                Devices: devices.Values
                    .OrderBy(device => device.DeviceId, StringComparer.Ordinal)
                    .Select(device => new OwnerStoredDevice(
                        device.DeviceId,
                        Convert.ToBase64String(device.PublicKeySpki),
                        device.PublicKeyFingerprint,
                        device.State,
                        device.ActivatedAtUtc,
                        device.RevokedAtUtc))
                    .ToArray(),
                Challenges: challenges.Values
                    .OrderBy(challenge => challenge.ChallengeId, StringComparer.Ordinal)
                    .Select(challenge => new OwnerStoredChallenge(
                        challenge.ChallengeId,
                        challenge.DeviceId,
                        Convert.ToBase64String(challenge.NonceHash),
                        challenge.ExpiresAtUtc,
                        ToStoredChallengeState(challenge.State),
                        challenge.ConsumedAtUtc))
                    .ToArray());
        }
    }

    /// <summary>
    /// Restores state produced by <see cref="ExportState"/>. The input is
    /// revalidated rather than trusted merely because it came from local disk.
    /// A corrupt or mismatched authority file therefore refuses startup instead
    /// of granting a different key access to this world.
    /// </summary>
    public static OwnerAuthorityStore Restore(
        OwnerAuthorityState state,
        IOwnerAuthorityClock? clock = null,
        IOwnerAuthorityRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported owner-authority state schema '{state.SchemaVersion.ToString(CultureInfo.InvariantCulture)}'.");
        }

        var store = new OwnerAuthorityStore(
            state.Authority,
            clock ?? SystemOwnerAuthorityClock.Instance,
            random ?? CryptographicOwnerAuthorityRandom.Instance);
        store.ImportState(state);
        return store;
    }

    /// <summary>Host-local recovery can displace one unapproved request, never an approved or active device.</summary>
    public OwnerAuthorityResult<OwnerPairingStart> StartPairingLocally(OwnerPairingRequest request)
    {
        // Validate before displacing any existing request.
        if (request is null || !TryImportP256PublicKey(request.PublicKeySpkiBase64, out var publicKey))
            return OwnerAuthorityResult<OwnerPairingStart>.Fail(OwnerAuthorityFailure.InvalidPublicKey);
        lock (gate)
        {
            ExpireOperationalStateUnsafe(UtcNow());
            var fingerprint = CreatePublicKeyFingerprint(publicKey);
            if (devices.Values.Any(device => device.State == OwnerDeviceState.Active &&
                device.PublicKeyFingerprint == fingerprint))
                return OwnerAuthorityResult<OwnerPairingStart>.Fail(OwnerAuthorityFailure.PublicKeyAlreadyRegistered);
            var first = StartPairing(request);
            if (first.IsSuccess || first.Failure != OwnerAuthorityFailure.PairingCapacityExceeded) return first;
            var oldest = pairings.Values.Where(item => item.State == OwnerPairingState.Pending)
                .OrderBy(item => item.ExpiresAtUtc).ThenBy(item => item.PairingId, StringComparer.Ordinal).FirstOrDefault();
            if (oldest is null) return first;
            oldest.State = OwnerPairingState.Expired;
            return StartPairing(request);
        }
    }

    public OwnerAuthorityResult<OwnerPairingStart> StartPairing(OwnerPairingRequest request)
    {
        if (request is null || !TryImportP256PublicKey(request.PublicKeySpkiBase64, out var publicKeySpki))
        {
            return OwnerAuthorityResult<OwnerPairingStart>.Fail(
                request is null ? OwnerAuthorityFailure.InvalidRequest : OwnerAuthorityFailure.InvalidPublicKey);
        }

        var publicKeyFingerprint = CreatePublicKeyFingerprint(publicKeySpki);
        lock (gate)
        {
            ExpireOperationalStateUnsafe(UtcNow());
            if (pairings.Values.Count(pairing =>
                    pairing.State is OwnerPairingState.Pending or OwnerPairingState.Approved) >= MaximumPendingPairings)
            {
                return OwnerAuthorityResult<OwnerPairingStart>.Fail(OwnerAuthorityFailure.PairingCapacityExceeded);
            }

            if (devices.Values.Any(device =>
                device.State == OwnerDeviceState.Active &&
                string.Equals(device.PublicKeyFingerprint, publicKeyFingerprint, StringComparison.Ordinal)))
            {
                return OwnerAuthorityResult<OwnerPairingStart>.Fail(OwnerAuthorityFailure.PublicKeyAlreadyRegistered);
            }

            var pairingId = CreateOpaqueIdentifierUnsafe(
                "pairing",
                candidate => pairings.ContainsKey(candidate));
            var deviceId = CreateOpaqueIdentifierUnsafe(
                "device",
                candidate => devices.ContainsKey(candidate) || pairings.Values.Any(pairing => pairing.DeviceId == candidate));
            var pairingCode = CreateVisibleCodeUnsafe();
            var expiresAtUtc = UtcNow() + PendingPairingLifetime;
            var activationCanonicalProof = CreatePairingActivationCanonicalProof(
                Identity,
                pairingId,
                deviceId,
                publicKeyFingerprint);

            pairings.Add(
                pairingId,
                new PendingPairing(
                    pairingId,
                    deviceId,
                    publicKeySpki,
                    publicKeyFingerprint,
                    HashUtf8(pairingCode),
                    expiresAtUtc,
                    activationCanonicalProof));

            return OwnerAuthorityResult<OwnerPairingStart>.Success(
                new OwnerPairingStart(
                    Identity,
                    pairingId,
                    deviceId,
                    pairingCode,
                    publicKeyFingerprint,
                    expiresAtUtc,
                    activationCanonicalProof));
        }
    }

    public OwnerAuthorityResult<OwnerPairingStatus> GetPairingStatus(string pairingId)
    {
        if (!IsReasonableIdentifier(pairingId))
        {
            return OwnerAuthorityResult<OwnerPairingStatus>.Fail(OwnerAuthorityFailure.InvalidRequest);
        }

        lock (gate)
        {
            ExpireOperationalStateUnsafe(UtcNow());
            if (!pairings.TryGetValue(pairingId, out var pairing))
            {
                return OwnerAuthorityResult<OwnerPairingStatus>.Fail(OwnerAuthorityFailure.PairingNotFound);
            }

            return OwnerAuthorityResult<OwnerPairingStatus>.Success(ToPairingStatus(pairing));
        }
    }

    /// <summary>
    /// Host-local bootstrap/recovery authority. Do not map this method to a
    /// public HTTP route.
    /// </summary>
    public OwnerAuthorityResult<OwnerPairingApproval> ApprovePendingPairingLocally(
        string pairingId,
        string visibleCode) => ApprovePendingPairing(pairingId, visibleCode);

    /// <summary>
    /// Applies the pairing confirmation after an ingress layer has established
    /// either host-local bootstrap authority or an already paired owner device.
    /// This class deliberately does not decide which of those authorities was
    /// used; it only performs the one-way state transition.
    /// </summary>
    public OwnerAuthorityResult<OwnerPairingApproval> ApprovePendingPairing(
        string pairingId,
        string visibleCode)
    {
        if (!IsReasonableIdentifier(pairingId) || !IsVisibleCode(visibleCode))
        {
            return OwnerAuthorityResult<OwnerPairingApproval>.Fail(OwnerAuthorityFailure.InvalidRequest);
        }

        var presentedCodeHash = HashUtf8(visibleCode);
        lock (gate)
        {
            ExpireOperationalStateUnsafe(UtcNow());
            if (!pairings.TryGetValue(pairingId, out var pairing))
            {
                return OwnerAuthorityResult<OwnerPairingApproval>.Fail(OwnerAuthorityFailure.PairingNotFound);
            }

            if (pairing.State == OwnerPairingState.Expired)
            {
                return OwnerAuthorityResult<OwnerPairingApproval>.Fail(OwnerAuthorityFailure.PairingExpired);
            }

            if (pairing.State != OwnerPairingState.Pending)
            {
                return OwnerAuthorityResult<OwnerPairingApproval>.Fail(OwnerAuthorityFailure.PairingNotPending);
            }

            if (!CryptographicOperations.FixedTimeEquals(pairing.VisibleCodeHash, presentedCodeHash))
            {
                pairing.WrongVisibleCodeAttempts++;
                if (pairing.WrongVisibleCodeAttempts >= MaximumWrongVisibleCodeAttempts)
                {
                    // Do not disclose an attempt counter to a caller. The final
                    // wrong code receives the same rejection, but this pairing
                    // can no longer be brute-forced or revived after restart.
                    pairing.State = OwnerPairingState.Expired;
                }

                return OwnerAuthorityResult<OwnerPairingApproval>.Fail(OwnerAuthorityFailure.PairingCodeMismatch);
            }

            pairing.WrongVisibleCodeAttempts = 0;
            pairing.State = OwnerPairingState.Approved;
            return OwnerAuthorityResult<OwnerPairingApproval>.Success(
                new OwnerPairingApproval(
                    Identity,
                    pairing.PairingId,
                    pairing.DeviceId,
                    pairing.PublicKeyFingerprint,
                    pairing.ActivationCanonicalProof));
        }
    }

    public OwnerAuthorityResult<OwnerDevice> ActivatePairing(OwnerPairingActivationRequest request)
    {
        if (request is null ||
            !IsReasonableIdentifier(request.PairingId) ||
            !IsReasonableProof(request.CanonicalProof) ||
            !IsReasonableSignature(request.SignatureBase64))
        {
            return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.InvalidRequest);
        }

        lock (gate)
        {
            ExpireOperationalStateUnsafe(UtcNow());
            if (!pairings.TryGetValue(request.PairingId, out var pairing))
            {
                return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.PairingNotFound);
            }

            if (pairing.State == OwnerPairingState.Expired)
            {
                return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.PairingExpired);
            }

            if (!string.Equals(request.CanonicalProof, pairing.ActivationCanonicalProof, StringComparison.Ordinal))
            {
                return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.InvalidCanonicalProof);
            }

            if (!VerifyP256Sha256Signature(pairing.PublicKeySpki, request.CanonicalProof, request.SignatureBase64))
            {
                return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.InvalidSignature);
            }

            if (pairing.State == OwnerPairingState.Active)
            {
                if (!devices.TryGetValue(pairing.DeviceId, out var activeDevice))
                {
                    throw new InvalidOperationException("An active pairing must have a registered device.");
                }

                return activeDevice.State == OwnerDeviceState.Active
                    ? OwnerAuthorityResult<OwnerDevice>.Success(ToOwnerDevice(activeDevice))
                    : OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.DeviceRevoked);
            }

            if (pairing.State != OwnerPairingState.Approved)
            {
                return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.PairingNotApproved);
            }

            var registeredDevice = new RegisteredDevice(
                pairing.DeviceId,
                pairing.PublicKeySpki,
                pairing.PublicKeyFingerprint,
                UtcNow());
            devices.Add(registeredDevice.DeviceId, registeredDevice);
            pairing.State = OwnerPairingState.Active;

            return OwnerAuthorityResult<OwnerDevice>.Success(ToOwnerDevice(registeredDevice));
        }
    }

    public OwnerAuthorityResult<OwnerDevice> GetDevice(string deviceId)
    {
        if (!IsReasonableIdentifier(deviceId))
        {
            return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.InvalidRequest);
        }

        lock (gate)
        {
            if (!devices.TryGetValue(deviceId, out var device))
            {
                return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.DeviceNotFound);
            }

            return OwnerAuthorityResult<OwnerDevice>.Success(ToOwnerDevice(device));
        }
    }

    public IReadOnlyList<OwnerDevice> GetDevices()
    {
        lock (gate)
        {
            return devices.Values
                .OrderBy(device => device.DeviceId, StringComparer.Ordinal)
                .Select(ToOwnerDevice)
                .ToArray();
        }
    }

    /// <summary>
    /// Host-local recovery operation. A later signed owner-device-management
    /// route can call the same state transition after authenticating its caller.
    /// </summary>
    public OwnerAuthorityResult<OwnerDevice> RevokeDeviceLocally(string deviceId)
        => RevokeDevice(deviceId);

    /// <summary>
    /// Revokes an owner device after a host-local or authenticated owner
    /// management ingress has authorized the request.
    /// </summary>
    public OwnerAuthorityResult<OwnerDevice> RevokeDevice(string deviceId)
    {
        if (!IsReasonableIdentifier(deviceId))
        {
            return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.InvalidRequest);
        }

        lock (gate)
        {
            if (!devices.TryGetValue(deviceId, out var device))
            {
                return OwnerAuthorityResult<OwnerDevice>.Fail(OwnerAuthorityFailure.DeviceNotFound);
            }

            if (device.State == OwnerDeviceState.Active)
            {
                device.State = OwnerDeviceState.Revoked;
                device.RevokedAtUtc = UtcNow();
            }

            return OwnerAuthorityResult<OwnerDevice>.Success(ToOwnerDevice(device));
        }
    }

    public OwnerAuthorityResult<OwnerChallenge> IssueChallenge(OwnerChallengeIssueRequest request)
    {
        if (request is null ||
            !IsReasonableIdentifier(request.DeviceId) ||
            !IsReasonableIdentifier(request.RequestId) ||
            !IsReasonableProof(request.CanonicalProof) ||
            !IsReasonableSignature(request.SignatureBase64))
        {
            return OwnerAuthorityResult<OwnerChallenge>.Fail(OwnerAuthorityFailure.InvalidRequest);
        }

        lock (gate)
        {
            ExpireOperationalStateUnsafe(UtcNow());
            if (!devices.TryGetValue(request.DeviceId, out var device))
            {
                return OwnerAuthorityResult<OwnerChallenge>.Fail(OwnerAuthorityFailure.DeviceNotFound);
            }

            if (device.State == OwnerDeviceState.Revoked)
            {
                return OwnerAuthorityResult<OwnerChallenge>.Fail(OwnerAuthorityFailure.DeviceRevoked);
            }

            var expectedCanonicalProof = CreateChallengeIssueCanonicalProof(
                Identity,
                request.DeviceId,
                request.RequestId);
            if (!string.Equals(request.CanonicalProof, expectedCanonicalProof, StringComparison.Ordinal))
            {
                return OwnerAuthorityResult<OwnerChallenge>.Fail(OwnerAuthorityFailure.InvalidCanonicalProof);
            }

            if (!VerifyP256Sha256Signature(device.PublicKeySpki, request.CanonicalProof, request.SignatureBase64))
            {
                return OwnerAuthorityResult<OwnerChallenge>.Fail(OwnerAuthorityFailure.InvalidSignature);
            }

            var pendingChallengeCount = challenges.Values.Count(challenge => challenge.State == OwnerChallengeState.Pending);
            var pendingChallengesForDevice = challenges.Values.Count(challenge =>
                challenge.State == OwnerChallengeState.Pending &&
                string.Equals(challenge.DeviceId, device.DeviceId, StringComparison.Ordinal));
            if (pendingChallengeCount >= MaximumPendingChallenges ||
                pendingChallengesForDevice >= MaximumPendingChallengesPerDevice)
            {
                return OwnerAuthorityResult<OwnerChallenge>.Fail(OwnerAuthorityFailure.ChallengeCapacityExceeded);
            }

            var challengeId = CreateOpaqueIdentifierUnsafe(
                "challenge",
                candidate => challenges.ContainsKey(candidate));
            var nonce = CreateOpaqueValueUnsafe(ChallengeNonceBytes);
            var expiresAtUtc = UtcNow() + ChallengeLifetime;
            challenges.Add(
                challengeId,
                new IssuedChallenge(
                    challengeId,
                    device.DeviceId,
                    HashUtf8(nonce),
                    expiresAtUtc));

            return OwnerAuthorityResult<OwnerChallenge>.Success(
                new OwnerChallenge(Identity, device.DeviceId, challengeId, nonce, expiresAtUtc));
        }
    }

    public OwnerAuthorityResult<OwnerChallengeConsumption> ConsumeChallenge(OwnerChallengeConsumeRequest request)
    {
        if (request is null ||
            !IsReasonableIdentifier(request.DeviceId) ||
            !IsReasonableIdentifier(request.ChallengeId) ||
            !IsReasonableOpaqueValue(request.Nonce) ||
            !IsReasonableBinding(request.Binding) ||
            !IsReasonableProof(request.CanonicalProof) ||
            !IsReasonableSignature(request.SignatureBase64))
        {
            return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.InvalidRequest);
        }

        lock (gate)
        {
            ExpireOperationalStateUnsafe(UtcNow());
            if (!devices.TryGetValue(request.DeviceId, out var device))
            {
                return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.DeviceNotFound);
            }

            if (device.State == OwnerDeviceState.Revoked)
            {
                return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.DeviceRevoked);
            }

            if (!challenges.TryGetValue(request.ChallengeId, out var challenge))
            {
                return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.ChallengeNotFound);
            }

            if (!string.Equals(challenge.DeviceId, request.DeviceId, StringComparison.Ordinal))
            {
                return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.ChallengeDeviceMismatch);
            }

            if (challenge.State == OwnerChallengeState.Expired)
            {
                return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.ChallengeExpired);
            }

            if (challenge.State == OwnerChallengeState.Consumed)
            {
                return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.ChallengeConsumed);
            }

            var presentedNonceHash = HashUtf8(request.Nonce);
            if (!CryptographicOperations.FixedTimeEquals(challenge.NonceHash, presentedNonceHash))
            {
                return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.ChallengeNonceMismatch);
            }

            var expectedCanonicalProof = CreateChallengeConsumeCanonicalProof(
                Identity,
                request.DeviceId,
                request.ChallengeId,
                request.Nonce,
                request.Binding);
            if (!string.Equals(request.CanonicalProof, expectedCanonicalProof, StringComparison.Ordinal))
            {
                return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.InvalidCanonicalProof);
            }

            if (!VerifyP256Sha256Signature(device.PublicKeySpki, request.CanonicalProof, request.SignatureBase64))
            {
                return OwnerAuthorityResult<OwnerChallengeConsumption>.Fail(OwnerAuthorityFailure.InvalidSignature);
            }

            challenge.State = OwnerChallengeState.Consumed;
            challenge.ConsumedAtUtc = UtcNow();
            return OwnerAuthorityResult<OwnerChallengeConsumption>.Success(
                new OwnerChallengeConsumption(
                    Identity,
                    device.DeviceId,
                    challenge.ChallengeId,
                    device.PublicKeyFingerprint,
                    request.Binding,
                    challenge.ConsumedAtUtc.Value));
        }
    }

    /// <summary>
    /// Builds the exact versioned string that a pending device must sign to
    /// activate. Callers send this same string back in the activation request;
    /// the store compares it with its independently reconstructed value.
    /// </summary>
    public static string CreatePairingActivationCanonicalProof(
        OwnerAuthorityIdentity authority,
        string pairingId,
        string deviceId,
        string publicKeyFingerprint) => CreateCanonicalProof(
        PairingActivationProofDomain,
        ("signature-algorithm", SignatureAlgorithm),
        ("server-authority-id", authority.ServerAuthorityId),
        ("world-id", authority.WorldId),
        ("pairing-id", pairingId),
        ("device-id", deviceId),
        ("public-key-fingerprint", publicKeyFingerprint));

    /// <summary>
    /// Builds the signed proof required to issue a one-use challenge.
    /// </summary>
    public static string CreateChallengeIssueCanonicalProof(
        OwnerAuthorityIdentity authority,
        string deviceId,
        string requestId) => CreateCanonicalProof(
        ChallengeIssueProofDomain,
        ("signature-algorithm", SignatureAlgorithm),
        ("server-authority-id", authority.ServerAuthorityId),
        ("world-id", authority.WorldId),
        ("device-id", deviceId),
        ("challenge-request-id", requestId));

    /// <summary>
    /// Builds the signed proof required to consume a one-use challenge. Binding
    /// is deliberately included so later control ingress can use a deterministic
    /// action digest without changing the authority primitive.
    /// </summary>
    public static string CreateChallengeConsumeCanonicalProof(
        OwnerAuthorityIdentity authority,
        string deviceId,
        string challengeId,
        string nonce,
        string binding) => CreateCanonicalProof(
        ChallengeConsumeProofDomain,
        ("signature-algorithm", SignatureAlgorithm),
        ("server-authority-id", authority.ServerAuthorityId),
        ("world-id", authority.WorldId),
        ("device-id", deviceId),
        ("challenge-id", challengeId),
        ("challenge-nonce", nonce),
        ("binding", binding));

    /// <summary>
    /// Returns the stable non-secret SHA-256 fingerprint of a valid P-256 SPKI.
    /// </summary>
    public static string GetPublicKeyFingerprint(string publicKeySpkiBase64)
    {
        if (!TryImportP256PublicKey(publicKeySpkiBase64, out var publicKeySpki))
        {
            throw new ArgumentException("The key must be a base64 P-256 SubjectPublicKeyInfo value.", nameof(publicKeySpkiBase64));
        }

        return CreatePublicKeyFingerprint(publicKeySpki);
    }

    /// <summary>
    /// Verifies the explicitly specified P-256 / SHA-256 / IEEE-P1363 proof
    /// format. It is public so a future ingress adapter can perform preflight
    /// diagnostics without gaining access to mutable authority state.
    /// </summary>
    public static bool VerifyP256Sha256Signature(
        string publicKeySpkiBase64,
        string canonicalProof,
        string signatureBase64)
    {
        if (!TryImportP256PublicKey(publicKeySpkiBase64, out var publicKeySpki))
        {
            return false;
        }

        return VerifyP256Sha256Signature(publicKeySpki, canonicalProof, signatureBase64);
    }

    private void ImportState(OwnerAuthorityState state)
    {
        if (!Equals(Identity, state.Authority) ||
            state.Pairings is null ||
            state.Devices is null ||
            state.Challenges is null)
        {
            throw new InvalidDataException("The owner-authority state is incomplete or bound to another world.");
        }

        lock (gate)
        {
            foreach (var stored in state.Pairings.OrderBy(pairing => pairing.PairingId, StringComparer.Ordinal))
            {
                if (!IsReasonableIdentifier(stored.PairingId) ||
                    !IsReasonableIdentifier(stored.DeviceId) ||
                    !TryImportP256PublicKey(stored.PublicKeySpkiBase64, out var publicKeySpki) ||
                    !TryDecodeSha256(stored.VisibleCodeHashBase64, out var visibleCodeHash) ||
                    !IsReasonableProof(stored.ActivationCanonicalProof) ||
                    !Enum.IsDefined(stored.State) ||
                    stored.WrongVisibleCodeAttempts is < 0 or > MaximumWrongVisibleCodeAttempts ||
                    (stored.State == OwnerPairingState.Pending &&
                     stored.WrongVisibleCodeAttempts >= MaximumWrongVisibleCodeAttempts) ||
                    ((stored.State == OwnerPairingState.Approved || stored.State == OwnerPairingState.Active) &&
                     stored.WrongVisibleCodeAttempts != 0) ||
                    !string.Equals(
                        stored.PublicKeyFingerprint,
                        CreatePublicKeyFingerprint(publicKeySpki),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        stored.ActivationCanonicalProof,
                        CreatePairingActivationCanonicalProof(
                            Identity,
                            stored.PairingId,
                            stored.DeviceId,
                            stored.PublicKeyFingerprint),
                        StringComparison.Ordinal) ||
                    !pairings.TryAdd(
                        stored.PairingId,
                        new PendingPairing(
                            stored.PairingId,
                            stored.DeviceId,
                            publicKeySpki,
                            stored.PublicKeyFingerprint,
                            visibleCodeHash,
                            stored.ExpiresAtUtc.ToUniversalTime(),
                            stored.ActivationCanonicalProof,
                            stored.WrongVisibleCodeAttempts)
                        {
                            State = stored.State,
                        }))
                {
                    throw new InvalidDataException("The owner-authority pairing state is invalid.");
                }
            }

            foreach (var stored in state.Devices.OrderBy(device => device.DeviceId, StringComparer.Ordinal))
            {
                if (!IsReasonableIdentifier(stored.DeviceId) ||
                    !TryImportP256PublicKey(stored.PublicKeySpkiBase64, out var publicKeySpki) ||
                    !Enum.IsDefined(stored.State) ||
                    !string.Equals(
                        stored.PublicKeyFingerprint,
                        CreatePublicKeyFingerprint(publicKeySpki),
                        StringComparison.Ordinal) ||
                    !devices.TryAdd(
                        stored.DeviceId,
                        new RegisteredDevice(
                            stored.DeviceId,
                            publicKeySpki,
                            stored.PublicKeyFingerprint,
                            stored.ActivatedAtUtc.ToUniversalTime())
                        {
                            State = stored.State,
                            RevokedAtUtc = stored.RevokedAtUtc?.ToUniversalTime(),
                        }))
                {
                    throw new InvalidDataException("The owner-authority device state is invalid.");
                }
            }

            if (devices.Values
                .Where(device => device.State == OwnerDeviceState.Active)
                .GroupBy(device => device.PublicKeyFingerprint, StringComparer.Ordinal)
                .Any(group => group.Count() > 1) ||
                pairings.Values.Any(pairing =>
                    pairing.State == OwnerPairingState.Active && !devices.ContainsKey(pairing.DeviceId)) ||
                devices.Values.Any(device =>
                    pairings.Values.SingleOrDefault(pairing =>
                        string.Equals(pairing.DeviceId, device.DeviceId, StringComparison.Ordinal)) is not { } pairing ||
                    pairing.State != OwnerPairingState.Active ||
                    !string.Equals(
                        pairing.PublicKeyFingerprint,
                        device.PublicKeyFingerprint,
                        StringComparison.Ordinal)))
            {
                // A registered device can only originate from an approved,
                // activated pairing with the same public key. Do not let a
                // writable-but-corrupt state file smuggle in a new owner key.
                throw new InvalidDataException("The owner-authority device registrations are inconsistent.");
            }

            foreach (var stored in state.Challenges.OrderBy(challenge => challenge.ChallengeId, StringComparer.Ordinal))
            {
                if (!IsReasonableIdentifier(stored.ChallengeId) ||
                    !IsReasonableIdentifier(stored.DeviceId) ||
                    !devices.ContainsKey(stored.DeviceId) ||
                    !TryDecodeSha256(stored.NonceHashBase64, out var nonceHash) ||
                    !Enum.IsDefined(stored.State) ||
                    !challenges.TryAdd(
                        stored.ChallengeId,
                        new IssuedChallenge(
                            stored.ChallengeId,
                            stored.DeviceId,
                            nonceHash,
                            stored.ExpiresAtUtc.ToUniversalTime())
                        {
                            State = FromStoredChallengeState(stored.State),
                            ConsumedAtUtc = stored.ConsumedAtUtc?.ToUniversalTime(),
                        }))
                {
                    throw new InvalidDataException("The owner-authority challenge state is invalid.");
                }
            }

            ExpireOperationalStateUnsafe(UtcNow());
        }
    }

    private static OwnerStoredChallengeState ToStoredChallengeState(OwnerChallengeState state) => state switch
    {
        OwnerChallengeState.Pending => OwnerStoredChallengeState.Pending,
        OwnerChallengeState.Consumed => OwnerStoredChallengeState.Consumed,
        OwnerChallengeState.Expired => OwnerStoredChallengeState.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static OwnerChallengeState FromStoredChallengeState(OwnerStoredChallengeState state) => state switch
    {
        OwnerStoredChallengeState.Pending => OwnerChallengeState.Pending,
        OwnerStoredChallengeState.Consumed => OwnerChallengeState.Consumed,
        OwnerStoredChallengeState.Expired => OwnerChallengeState.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static bool TryDecodeSha256(string? value, out byte[] hash)
    {
        hash = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            hash = Convert.FromBase64String(value);
            return hash.Length == SHA256.HashSizeInBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private DateTimeOffset UtcNow() => clock.UtcNow.ToUniversalTime();

    private void ExpireOperationalStateUnsafe(DateTimeOffset now)
    {
        foreach (var pairing in pairings.Values)
        {
            if ((pairing.State == OwnerPairingState.Pending || pairing.State == OwnerPairingState.Approved) &&
                now >= pairing.ExpiresAtUtc)
            {
                pairing.State = OwnerPairingState.Expired;
            }
        }

        foreach (var challenge in challenges.Values)
        {
            if (challenge.State == OwnerChallengeState.Pending && now >= challenge.ExpiresAtUtc)
            {
                challenge.State = OwnerChallengeState.Expired;
            }
        }

        TrimLivePairingsUnsafe();
        TrimPendingChallengesUnsafe();
        TrimExpiredPairingsUnsafe();
        TrimTerminalChallengesUnsafe();
    }

    private void TrimLivePairingsUnsafe()
    {
        var livePairings = pairings.Values
            .Where(pairing => pairing.State is OwnerPairingState.Pending or OwnerPairingState.Approved)
            .OrderBy(pairing => pairing.ExpiresAtUtc)
            .ThenBy(pairing => pairing.PairingId, StringComparer.Ordinal)
            .ToArray();
        var removalCount = livePairings.Length - MaximumPendingPairings;
        if (removalCount <= 0)
        {
            return;
        }

        // A restored state file from before this bound may contain more live
        // pairings than the current policy permits. Expiring the oldest is
        // safer than silently preserving unbounded brute-force targets.
        foreach (var pairing in livePairings.Take(removalCount))
        {
            pairing.State = OwnerPairingState.Expired;
        }
    }

    private void TrimPendingChallengesUnsafe()
    {
        foreach (var deviceChallenges in challenges.Values
                     .Where(challenge => challenge.State == OwnerChallengeState.Pending)
                     .GroupBy(challenge => challenge.DeviceId, StringComparer.Ordinal))
        {
            var pendingForDevice = deviceChallenges
                .OrderBy(challenge => challenge.ExpiresAtUtc)
                .ThenBy(challenge => challenge.ChallengeId, StringComparer.Ordinal)
                .ToArray();
            var removalCount = pendingForDevice.Length - MaximumPendingChallengesPerDevice;
            foreach (var challenge in pendingForDevice.Take(Math.Max(0, removalCount)))
            {
                challenge.State = OwnerChallengeState.Expired;
            }
        }

        var pendingChallenges = challenges.Values
            .Where(challenge => challenge.State == OwnerChallengeState.Pending)
            .OrderBy(challenge => challenge.ExpiresAtUtc)
            .ThenBy(challenge => challenge.ChallengeId, StringComparer.Ordinal)
            .ToArray();
        var globalRemovalCount = pendingChallenges.Length - MaximumPendingChallenges;
        foreach (var challenge in pendingChallenges.Take(Math.Max(0, globalRemovalCount)))
        {
            challenge.State = OwnerChallengeState.Expired;
        }
    }

    private void TrimExpiredPairingsUnsafe()
    {
        var expiredPairingIds = pairings.Values
            .Where(pairing => pairing.State == OwnerPairingState.Expired)
            .OrderBy(pairing => pairing.ExpiresAtUtc)
            .ThenBy(pairing => pairing.PairingId, StringComparer.Ordinal)
            .Select(pairing => pairing.PairingId)
            .ToArray();
        var removalCount = expiredPairingIds.Length - MaximumRetainedExpiredPairings;
        if (removalCount <= 0)
        {
            return;
        }

        foreach (var pairingId in expiredPairingIds.Take(removalCount))
        {
            pairings.Remove(pairingId);
        }
    }

    private void TrimTerminalChallengesUnsafe()
    {
        var terminalChallengeIds = challenges.Values
            .Where(challenge => challenge.State != OwnerChallengeState.Pending)
            .OrderBy(challenge => challenge.ConsumedAtUtc ?? challenge.ExpiresAtUtc)
            .ThenBy(challenge => challenge.ChallengeId, StringComparer.Ordinal)
            .Select(challenge => challenge.ChallengeId)
            .ToArray();
        var removalCount = terminalChallengeIds.Length - MaximumRetainedTerminalChallenges;
        if (removalCount <= 0)
        {
            return;
        }

        foreach (var challengeId in terminalChallengeIds.Take(removalCount))
        {
            challenges.Remove(challengeId);
        }
    }

    private string CreateOpaqueIdentifierUnsafe(string prefix, Func<string, bool> isTaken)
    {
        for (var attempt = 0; attempt < MaximumIdentifierAttempts; attempt++)
        {
            var candidate = $"{prefix}_{CreateOpaqueValueUnsafe(OpaqueIdentifierBytes)}";
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new CryptographicException("Unable to mint a unique opaque authority identifier.");
    }

    private string CreateOpaqueValueUnsafe(int byteCount)
    {
        Span<byte> bytes = stackalloc byte[byteCount];
        random.Fill(bytes);
        return ToBase64Url(bytes);
    }

    private string CreateVisibleCodeUnsafe()
    {
        const uint exclusiveUpperBound = 1_000_000;
        const uint rejectionLimit = uint.MaxValue - (uint.MaxValue % exclusiveUpperBound);
        Span<byte> bytes = stackalloc byte[sizeof(uint)];

        while (true)
        {
            random.Fill(bytes);
            var value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            if (value < rejectionLimit)
            {
                return (value % exclusiveUpperBound).ToString($"D{VisibleCodeLength}", CultureInfo.InvariantCulture);
            }
        }
    }

    private OwnerPairingStatus ToPairingStatus(PendingPairing pairing) => new(
        Identity,
        pairing.PairingId,
        pairing.DeviceId,
        pairing.PublicKeyFingerprint,
        pairing.State,
        pairing.ExpiresAtUtc);

    private static OwnerDevice ToOwnerDevice(RegisteredDevice device) => new(
        device.DeviceId,
        Convert.ToBase64String(device.PublicKeySpki),
        device.PublicKeyFingerprint,
        device.State,
        device.ActivatedAtUtc,
        device.RevokedAtUtc);

    private static bool TryImportP256PublicKey(string? publicKeySpkiBase64, out byte[] publicKeySpki)
    {
        publicKeySpki = [];
        if (string.IsNullOrWhiteSpace(publicKeySpkiBase64) || publicKeySpkiBase64.Length > MaximumSpkiBase64Length)
        {
            return false;
        }

        try
        {
            var suppliedSpki = Convert.FromBase64String(publicKeySpkiBase64);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(suppliedSpki, out var bytesRead);
            if (bytesRead != suppliedSpki.Length)
            {
                return false;
            }

            var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
            if (!string.Equals(
                    parameters.Curve.Oid.Value,
                    ECCurve.NamedCurves.nistP256.Oid.Value,
                    StringComparison.Ordinal) ||
                parameters.Q.X is not { Length: 32 } ||
                parameters.Q.Y is not { Length: 32 })
            {
                return false;
            }

            publicKeySpki = ecdsa.ExportSubjectPublicKeyInfo();
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerifyP256Sha256Signature(
        byte[] publicKeySpki,
        string canonicalProof,
        string signatureBase64)
    {
        if (!IsReasonableProof(canonicalProof) || !IsReasonableSignature(signatureBase64))
        {
            return false;
        }

        try
        {
            var signature = Convert.FromBase64String(signatureBase64);
            if (signature.Length != 64)
            {
                return false;
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeySpki, out var bytesRead);
            if (bytesRead != publicKeySpki.Length)
            {
                return false;
            }

            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(canonicalProof),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string CreatePublicKeyFingerprint(ReadOnlySpan<byte> publicKeySpki) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(publicKeySpki)).ToLowerInvariant()}";

    private static byte[] HashUtf8(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string CreateCanonicalProof(
        string domain,
        params (string Name, string Value)[] fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        var builder = new StringBuilder(domain);
        foreach (var (name, value) in fields)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(value);
            builder.Append('\n');
            builder.Append(name);
            builder.Append('=');
            builder.Append(ToBase64Url(Encoding.UTF8.GetBytes(value)));
        }

        return builder.ToString();
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static void EnsureIdentity(OwnerAuthorityIdentity identity)
    {
        if (!IsReasonableIdentityValue(identity.ServerAuthorityId))
        {
            throw new ArgumentException("Server authority ID must be a non-empty bounded value.", nameof(identity));
        }

        if (!IsReasonableIdentityValue(identity.WorldId))
        {
            throw new ArgumentException("World ID must be a non-empty bounded value.", nameof(identity));
        }
    }

    private static bool IsVisibleCode(string? value) => value is { Length: VisibleCodeLength } && value.All(char.IsAsciiDigit);

    private static bool IsReasonableIdentityValue(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumOpaqueInputLength;

    private static bool IsReasonableIdentifier(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumOpaqueInputLength;

    private static bool IsReasonableOpaqueValue(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumOpaqueInputLength;

    private static bool IsReasonableBinding(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumBindingLength;

    private static bool IsReasonableProof(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumProofLength;

    private static bool IsReasonableSignature(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumSignatureLength;

    private sealed class PendingPairing(
        string pairingId,
        string deviceId,
        byte[] publicKeySpki,
        string publicKeyFingerprint,
        byte[] visibleCodeHash,
        DateTimeOffset expiresAtUtc,
        string activationCanonicalProof,
        int wrongVisibleCodeAttempts = 0)
    {
        public string PairingId { get; } = pairingId;

        public string DeviceId { get; } = deviceId;

        public byte[] PublicKeySpki { get; } = publicKeySpki;

        public string PublicKeyFingerprint { get; } = publicKeyFingerprint;

        public byte[] VisibleCodeHash { get; } = visibleCodeHash;

        public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;

        public string ActivationCanonicalProof { get; } = activationCanonicalProof;

        public int WrongVisibleCodeAttempts { get; set; } = wrongVisibleCodeAttempts;

        public OwnerPairingState State { get; set; } = OwnerPairingState.Pending;
    }

    private sealed class RegisteredDevice(
        string deviceId,
        byte[] publicKeySpki,
        string publicKeyFingerprint,
        DateTimeOffset activatedAtUtc)
    {
        public string DeviceId { get; } = deviceId;

        public byte[] PublicKeySpki { get; } = publicKeySpki;

        public string PublicKeyFingerprint { get; } = publicKeyFingerprint;

        public DateTimeOffset ActivatedAtUtc { get; } = activatedAtUtc;

        public OwnerDeviceState State { get; set; } = OwnerDeviceState.Active;

        public DateTimeOffset? RevokedAtUtc { get; set; }
    }

    private enum OwnerChallengeState
    {
        Pending,
        Consumed,
        Expired,
    }

    private sealed class IssuedChallenge(
        string challengeId,
        string deviceId,
        byte[] nonceHash,
        DateTimeOffset expiresAtUtc)
    {
        public string ChallengeId { get; } = challengeId;

        public string DeviceId { get; } = deviceId;

        public byte[] NonceHash { get; } = nonceHash;

        public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;

        public OwnerChallengeState State { get; set; } = OwnerChallengeState.Pending;

        public DateTimeOffset? ConsumedAtUtc { get; set; }
    }
}
