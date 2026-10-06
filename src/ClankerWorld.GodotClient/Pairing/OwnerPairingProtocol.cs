using System.Security.Cryptography;
using System.Text;

namespace ClankerWorld.GodotClient.Pairing;

/// <summary>
/// Stable identity that every owner proof binds to. A device key is therefore
/// not silently valid against a different ClankerWorld server or world.
/// </summary>
public sealed record OwnerAuthorityIdentity(string ServerAuthorityId, string WorldId);

/// <summary>
/// Lifecycle states returned while polling a pairing request.
/// Values intentionally mirror the server authority contract.
/// </summary>
public enum OwnerPairingState
{
    Pending,
    Approved,
    Active,
    Expired,
}

/// <summary>
/// Lifecycle states returned for a registered owner device.
/// </summary>
public enum OwnerDeviceState
{
    Active,
    Revoked,
}

/// <summary>
/// Public-key-only request used to start pairing. It contains a standard
/// P-256 SubjectPublicKeyInfo value encoded with ordinary base64.
/// </summary>
public sealed record OwnerPairingStartRequest(string PublicKeySpkiBase64);

/// <summary>
/// Server response returned when a new pairing is started. The pairing code is
/// a human comparison value, never an authentication token, and callers must
/// not write it to logs.
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
/// Safe pairing status response for polling. It intentionally contains no
/// private key, pairing code, bearer credential, or signature.
/// </summary>
public sealed record OwnerPairingStatus(
    OwnerAuthorityIdentity Authority,
    string PairingId,
    string DeviceId,
    string PublicKeyFingerprint,
    OwnerPairingState State,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Safe result of approving another device's pending pairing. The approving
/// device receives no private material; the candidate still has to prove its
/// own key before activation.
/// </summary>
public sealed record OwnerPairingApproval(
    OwnerAuthorityIdentity Authority,
    string PairingId,
    string DeviceId,
    string PublicKeyFingerprint,
    string ActivationCanonicalProof);

/// <summary>
/// Proof-of-possession request that activates an approved pairing.
/// </summary>
public sealed record OwnerPairingActivationRequest(
    string PairingId,
    string CanonicalProof,
    string SignatureBase64);

/// <summary>
/// Registered device data returned after a successful activation.
/// </summary>
public sealed record OwnerDevice(
    string DeviceId,
    string PublicKeySpkiBase64,
    string PublicKeyFingerprint,
    OwnerDeviceState State,
    DateTimeOffset ActivatedAtUtc,
    DateTimeOffset? RevokedAtUtc);

/// <summary>
/// Signed request to mint a short-lived, one-use challenge.
/// </summary>
public sealed record OwnerChallengeIssueRequest(
    string DeviceId,
    string RequestId,
    string CanonicalProof,
    string SignatureBase64);

/// <summary>
/// One-use server challenge. Its nonce is intentionally represented only for
/// immediate proof construction and must not be logged or persisted by the
/// client.
/// </summary>
public sealed record OwnerChallenge(
    OwnerAuthorityIdentity Authority,
    string DeviceId,
    string ChallengeId,
    string Nonce,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<string>? SupportedActionPayloads = null);

/// <summary>
/// Signed challenge-consumption proof. HTTP action envelopes flatten these
/// fields so transport adapters can validate an authorization before reading
/// the endpoint-specific action payload.
/// </summary>
public sealed record OwnerChallengeConsumeRequest(
    string DeviceId,
    string ChallengeId,
    string Nonce,
    string Binding,
    string CanonicalProof,
    string SignatureBase64);

/// <summary>
/// Signed action envelope sent to owner-only HTTP endpoints. <typeparamref
/// name="TAction"/> is endpoint-specific data; its canonical payload is used
/// to construct <see cref="Binding"/> but is deliberately not trusted from
/// this envelope by the server.
/// </summary>
public sealed record OwnerSignedActionRequest<TAction>(
    string DeviceId,
    string ChallengeId,
    string Nonce,
    string Binding,
    string CanonicalProof,
    string SignatureBase64,
    string RequestId,
    TAction Action);

/// <summary>
/// Canonical owner-pairing protocol primitives shared by all Godot-facing
/// client code. These strings and signature encodings intentionally match
/// <c>OwnerAuthorityStore</c> without taking a dependency on the server
/// project.
/// </summary>
public static class OwnerPairingProtocol
{
    public const string SignatureAlgorithm = "ecdsa-p256-sha256-p1363.v1";
    public const string PairingActivationProofDomain = "clankerworld.owner-pairing.activate.v1";
    public const string ChallengeIssueProofDomain = "clankerworld.owner-challenge.issue.v1";
    public const string ChallengeConsumeProofDomain = "clankerworld.owner-challenge.consume.v1";
    public const string HttpActionBindingDomain = "clankerworld.owner-http-binding.v1";

    /// <summary>
    /// Builds the exact proof an approved device signs to activate its pairing.
    /// </summary>
    public static string CreatePairingActivationCanonicalProof(
        OwnerAuthorityIdentity authority,
        string pairingId,
        string deviceId,
        string publicKeyFingerprint)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return CreateCanonicalDocument(
            PairingActivationProofDomain,
            ("signature-algorithm", SignatureAlgorithm),
            ("server-authority-id", authority.ServerAuthorityId),
            ("world-id", authority.WorldId),
            ("pairing-id", pairingId),
            ("device-id", deviceId),
            ("public-key-fingerprint", publicKeyFingerprint));
    }

    /// <summary>
    /// Builds the exact proof a device signs before the server issues a
    /// challenge.
    /// </summary>
    public static string CreateChallengeIssueCanonicalProof(
        OwnerAuthorityIdentity authority,
        string deviceId,
        string requestId)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return CreateCanonicalDocument(
            ChallengeIssueProofDomain,
            ("signature-algorithm", SignatureAlgorithm),
            ("server-authority-id", authority.ServerAuthorityId),
            ("world-id", authority.WorldId),
            ("device-id", deviceId),
            ("challenge-request-id", requestId));
    }

    /// <summary>
    /// Builds the exact proof a device signs to consume a one-use challenge.
    /// The action binding prevents the challenge from being detached from the
    /// request it authorizes.
    /// </summary>
    public static string CreateChallengeConsumeCanonicalProof(
        OwnerAuthorityIdentity authority,
        string deviceId,
        string challengeId,
        string nonce,
        string binding)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return CreateCanonicalDocument(
            ChallengeConsumeProofDomain,
            ("signature-algorithm", SignatureAlgorithm),
            ("server-authority-id", authority.ServerAuthorityId),
            ("world-id", authority.WorldId),
            ("device-id", deviceId),
            ("challenge-id", challengeId),
            ("challenge-nonce", nonce),
            ("binding", binding));
    }

    /// <summary>
    /// Builds the versioned action binding consumed by the owner HTTP ingress.
    /// <paramref name="relativeAbsolutePath"/> must be a path beginning with
    /// <c>/</c>, without a scheme, query, or fragment. The payload is
    /// endpoint-specific deterministic plain text, not raw JSON.
    /// </summary>
    public static string CreateHttpActionBinding(
        string method,
        string relativeAbsolutePath,
        string requestId,
        string canonicalPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(canonicalPayload);
        ValidateRelativeAbsolutePath(relativeAbsolutePath);

        var normalizedMethod = method.ToUpperInvariant();
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload));
        // This binding is deliberately not made with CreateCanonicalDocument:
        // payload-sha256 is already an encoded digest, whereas the other
        // fields are UTF-8 values that must be base64url encoded exactly once.
        // Keep this byte-for-byte aligned with Viewer.Control.OwnerHttpBinding.
        return string.Join(
            '\n',
            HttpActionBindingDomain,
            $"method={ToBase64Url(Encoding.UTF8.GetBytes(normalizedMethod))}",
            $"path={ToBase64Url(Encoding.UTF8.GetBytes(relativeAbsolutePath))}",
            $"request-id={ToBase64Url(Encoding.UTF8.GetBytes(requestId))}",
            $"payload-sha256={ToBase64Url(payloadHash)}");
    }

    /// <summary>
    /// Creates a cryptographically random opaque request identifier suitable
    /// for challenge issuance and action binding.
    /// </summary>
    public static string CreateRequestId()
    {
        Span<byte> randomBytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        return $"request_{ToBase64Url(randomBytes)}";
    }

    /// <summary>
    /// Exports a validated P-256 public key as standard SubjectPublicKeyInfo
    /// encoded with ordinary base64. Private key material is never exported.
    /// </summary>
    public static string ExportP256PublicKeySpkiBase64(ECDsa signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        EnsureP256(signingKey);
        return Convert.ToBase64String(signingKey.ExportSubjectPublicKeyInfo());
    }

    /// <summary>
    /// Computes the non-secret SHA-256 fingerprint used by the server for a
    /// public SPKI value emitted by <see cref="ExportP256PublicKeySpkiBase64"/>.
    /// </summary>
    public static string CreatePublicKeyFingerprint(string publicKeySpkiBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeySpkiBase64);
        try
        {
            var suppliedSpki = Convert.FromBase64String(publicKeySpkiBase64);
            using var signingKey = ECDsa.Create();
            signingKey.ImportSubjectPublicKeyInfo(suppliedSpki, out var bytesRead);
            if (bytesRead != suppliedSpki.Length || !IsP256(signingKey))
            {
                throw new ArgumentException(
                    "The key must be a base64 P-256 SubjectPublicKeyInfo value.",
                    nameof(publicKeySpkiBase64));
            }

            return CreatePublicKeyFingerprint(signingKey.ExportSubjectPublicKeyInfo());
        }
        catch (CryptographicException exception)
        {
            throw new ArgumentException(
                "The key must be a base64 P-256 SubjectPublicKeyInfo value.",
                nameof(publicKeySpkiBase64),
                exception);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "The key must be a base64 P-256 SubjectPublicKeyInfo value.",
                nameof(publicKeySpkiBase64),
                exception);
        }
    }

    /// <summary>
    /// Compatibility-named public fingerprint helper corresponding to the
    /// server authority primitive.
    /// </summary>
    public static string GetPublicKeyFingerprint(string publicKeySpkiBase64) =>
        CreatePublicKeyFingerprint(publicKeySpkiBase64);

    /// <summary>
    /// Computes the non-secret SHA-256 fingerprint over a canonical public
    /// SPKI byte sequence.
    /// </summary>
    public static string CreatePublicKeyFingerprint(ReadOnlySpan<byte> publicKeySpki) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(publicKeySpki)).ToLowerInvariant()}";

    /// <summary>
    /// Signs UTF-8 canonical proof data with P-256 ECDSA, SHA-256, and the
    /// fixed-width IEEE-P1363 <c>r || s</c> format required by the server.
    /// </summary>
    public static string SignP256Sha256P1363(ECDsa signingKey, string canonicalProof)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalProof);
        EnsureP256(signingKey);

        var signature = signingKey.SignData(
            Encoding.UTF8.GetBytes(canonicalProof),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (signature.Length != 64)
        {
            throw new CryptographicException("A P-256 IEEE-P1363 signature must be 64 bytes.");
        }

        return Convert.ToBase64String(signature);
    }

    /// <summary>
    /// Verifies a standard-base64 IEEE-P1363 P-256/SHA-256 proof. This is
    /// useful for diagnostics and tests; normal client flows only sign.
    /// </summary>
    public static bool VerifyP256Sha256P1363(
        string publicKeySpkiBase64,
        string canonicalProof,
        string signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(publicKeySpkiBase64) ||
            string.IsNullOrWhiteSpace(canonicalProof) ||
            string.IsNullOrWhiteSpace(signatureBase64))
        {
            return false;
        }

        try
        {
            var publicKeySpki = Convert.FromBase64String(publicKeySpkiBase64);
            var signature = Convert.FromBase64String(signatureBase64);
            if (signature.Length != 64)
            {
                return false;
            }

            using var signingKey = ECDsa.Create();
            signingKey.ImportSubjectPublicKeyInfo(publicKeySpki, out var bytesRead);
            if (bytesRead != publicKeySpki.Length || !IsP256(signingKey))
            {
                return false;
            }

            return signingKey.VerifyData(
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

    /// <summary>
    /// Compatibility-named verifier corresponding to the server authority
    /// primitive. Signatures are P-256, SHA-256, and IEEE-P1363.
    /// </summary>
    public static bool VerifyP256Sha256Signature(
        string publicKeySpkiBase64,
        string canonicalProof,
        string signatureBase64) =>
        VerifyP256Sha256P1363(publicKeySpkiBase64, canonicalProof, signatureBase64);

    internal static string ToBase64Url(ReadOnlySpan<byte> value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string CreateCanonicalDocument(
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

    private static void EnsureP256(ECDsa signingKey)
    {
        if (!IsP256(signingKey))
        {
            throw new CryptographicException("The owner device key must use the NIST P-256 curve.");
        }
    }

    private static bool IsP256(ECDsa signingKey)
    {
        var parameters = signingKey.ExportParameters(includePrivateParameters: false);
        return string.Equals(
                   parameters.Curve.Oid.Value,
                   ECCurve.NamedCurves.nistP256.Oid.Value,
                   StringComparison.Ordinal) &&
               parameters.Q.X is { Length: 32 } &&
               parameters.Q.Y is { Length: 32 };
    }

    internal static void ValidateRelativeAbsolutePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path[0] != '/' ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('?') ||
            path.Contains('#'))
        {
            throw new ArgumentException(
                "The action path must be an absolute path without a scheme, query, or fragment.",
                nameof(path));
        }
    }
}
