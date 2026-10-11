using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using Microsoft.AspNetCore.Http;

namespace ClankerWorld.Viewer.Control;

public sealed record LocalPairingApprovalHttpRequest(string PairingCode);

public sealed record LocalDeviceRevokeHttpRequest(string? Reason);

public sealed record OwnerControlReceipt(
    string Operation,
    bool Changed,
    bool IsPaused,
    long RunEpoch,
    long Revision,
    long LatestEventId)
{
    public static OwnerControlReceipt From(string operation, bool changed, OwnerWorldSnapshot snapshot) => new(
        operation,
        changed,
        snapshot.IsPaused,
        snapshot.RunEpoch,
        snapshot.Revision,
        snapshot.LatestGlobalEventId);

    public static OwnerControlReceipt From(string operation, bool changed, ViewerWorldSnapshot snapshot)
    {
        var authoring = snapshot.Authoring;
        return new OwnerControlReceipt(
            operation,
            changed,
            authoring?.IsPaused ?? snapshot.Cognition?.IsPaused ?? false,
            authoring?.RunEpoch ?? 0,
            authoring?.Revision ?? snapshot.WorldTick,
            snapshot.LatestEventId);
    }

    // Control acknowledgements must not depend on the optional world-view
    // projection. A projection defect must never turn an accepted pause into
    // an HTTP 500 that leaves the client unsure whether the world stopped.
    public static OwnerControlReceipt From(string operation, bool changed, PrivateWorldRuntimeState state)
    {
        var revision = state.EventHistoryFloor + state.Events.Count;
        return new OwnerControlReceipt(operation, changed, state.Society.Society.IsPaused,
            state.Society.Society.RunEpoch, revision, revision);
    }
}

public sealed record OwnerControlFailure(string Code, string Detail);

/// <summary>
/// The normal HTTPS/Tailnet listener never has this port. Only a separately
/// bound loopback listener can approve the first pending device.
/// </summary>
/// <remarks>
/// A host the game started on the player's own PC also has a companion secret.
/// The game wrote it to a file in the player's own data folder before starting
/// the host, so every program on the PC can reach the loopback listener but only
/// the game that started it can approve its pairing or stop it.
/// </remarks>
public sealed record OwnerPairingHostOptions(int LocalApprovalPort, string? CompanionSecret = null)
{
    public const string CompanionSecretHeader = "X-ClankerWorld-Companion-Secret";
    public const int MinimumCompanionSecretLength = 32;

    public bool IsCompanionHost => CompanionSecret is not null;

    public bool IsLocalApprovalRequest(HttpContext context) =>
        LocalApprovalPort > 0 &&
        context.Connection.LocalPort == LocalApprovalPort &&
        context.Connection.RemoteIpAddress is { } address &&
        IPAddress.IsLoopback(address) &&
        (CompanionSecret is null || HasCompanionSecret(context));

    /// <summary>Only a companion host can be stopped over HTTP, and only by the game that started it.</summary>
    public bool IsCompanionShutdownRequest(HttpContext context) => IsCompanionHost && IsLocalApprovalRequest(context);

    private bool HasCompanionSecret(HttpContext context)
    {
        var supplied = context.Request.Headers[CompanionSecretHeader].ToString();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(CompanionSecret!));
    }

    /// <summary>
    /// Reads the secret the starting game wrote. The value is never logged or
    /// echoed; a missing, short or unreadable file stops startup.
    /// </summary>
    public static string ReadCompanionSecret(string path)
    {
        var secret = File.ReadAllText(path).Trim();
        if (secret.Length < MinimumCompanionSecretLength || secret.Any(char.IsWhiteSpace))
            throw new InvalidOperationException(
                $"The companion secret file must hold one value of at least {MinimumCompanionSecretLength} characters.");
        return secret;
    }
}

public static class OwnerFailures
{
    public static IResult UnpairedObservation() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Owner pairing required",
        detail: "Pair a device and use a signed owner reconnect request to inspect this world.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "owner_pairing_required",
        });

    public static IResult ToHttpResult(OwnerAuthorityFailure failure)
    {
        var (statusCode, code, title) = failure switch
        {
            OwnerAuthorityFailure.InvalidRequest => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid owner request"),
            OwnerAuthorityFailure.InvalidPublicKey => (StatusCodes.Status400BadRequest, "invalid_public_key", "Invalid owner key"),
            OwnerAuthorityFailure.PairingNotFound or OwnerAuthorityFailure.DeviceNotFound or OwnerAuthorityFailure.ChallengeNotFound =>
                (StatusCodes.Status404NotFound, "owner_record_not_found", "Owner record not found"),
            OwnerAuthorityFailure.PublicKeyAlreadyRegistered or OwnerAuthorityFailure.PairingNotPending or OwnerAuthorityFailure.PairingAlreadyActive =>
                (StatusCodes.Status409Conflict, "owner_state_conflict", "Owner state conflict"),
            OwnerAuthorityFailure.PairingCapacityExceeded or OwnerAuthorityFailure.ChallengeCapacityExceeded =>
                (StatusCodes.Status429TooManyRequests, "owner_capacity_reached", "Owner authority capacity reached"),
            OwnerAuthorityFailure.ChallengeConsumed =>
                (StatusCodes.Status409Conflict, "challenge_already_consumed", "Challenge already consumed"),
            OwnerAuthorityFailure.DeviceRevoked =>
                (StatusCodes.Status403Forbidden, "device_revoked", "Device revoked"),
            OwnerAuthorityFailure.PairingExpired or OwnerAuthorityFailure.PairingNotApproved or OwnerAuthorityFailure.PairingCodeMismatch or
            OwnerAuthorityFailure.InvalidCanonicalProof or OwnerAuthorityFailure.InvalidSignature or OwnerAuthorityFailure.ChallengeExpired or
            OwnerAuthorityFailure.ChallengeDeviceMismatch or OwnerAuthorityFailure.ChallengeNonceMismatch =>
                (StatusCodes.Status401Unauthorized, "owner_proof_rejected", "Owner proof rejected"),
            _ => (StatusCodes.Status400BadRequest, "owner_request_rejected", "Owner request rejected"),
        };
        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: $"Owner authority rejected this request: {failure}.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["authorityFailure"] = failure.ToString(),
            });
    }
}
