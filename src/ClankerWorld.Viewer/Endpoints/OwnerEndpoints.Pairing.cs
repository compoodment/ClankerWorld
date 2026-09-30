using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapPairing(WebApplication app)
    {
        // This is deliberately discovery-only. Full owner capabilities are returned
        // only inside the authenticated reconnect baseline below.
        app.MapGet("/api/v1/handshake", () => Results.Ok(new ViewerHandshake(
            new ProtocolVersion(Major: 1, Minor: 1),
            ["owner-device-pairing.v1"],
            ["owner-device-pairing.v1"])));

        // Bound unauthenticated creation before JSON binding. Do not spend this budget
        // on signed owner challenges/reconnect/pause or host-local recovery.
        app.Use(async (context, next) =>
        {
            var pairingRoute = context.Request.Path.StartsWithSegments("/api/v1/pairings") ||
                context.Request.Path.StartsWithSegments("/api/v1/local/pairings");
            if (pairingRoute)
            {
                const long maximumPairingBytes = 16 * 1024;
                if (context.Request.ContentLength > maximumPairingBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = maximumPairingBytes;
            }
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/api/v1/pairings" &&
                !context.RequestServices.GetRequiredService<PairingRequestBudget>().TryAcquire())
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "60";
                return;
            }
            await next(context);
        });

        app.MapPost("/api/v1/local/pairings", (
            StartOwnerPairingHttpRequest request, HttpContext context, OwnerPairingHostOptions options,
            OwnerAuthorityStore authority, OwnerAuthorityStateFile stateFile) =>
        {
            if (!options.IsLocalApprovalRequest(context)) return Results.NotFound();
            var result = authority.StartPairingLocally(new OwnerPairingRequest(request?.PublicKeySpkiBase64 ?? string.Empty));
            stateFile.Save(authority);
            PairingRequestBudget.LogLocalRecovery(app.Logger, result.IsSuccess ? "created" : "refused");
            return result.IsSuccess ? Results.Ok(result.Value) : OwnerFailures.ToHttpResult(result.Failure);
        });

        app.MapPost("/api/v1/pairings", (
            StartOwnerPairingHttpRequest request,
            OwnerAuthorityStore authority,
            OwnerAuthorityStateFile stateFile) =>
        {
            var result = authority.StartPairing(new OwnerPairingRequest(request?.PublicKeySpkiBase64 ?? string.Empty));
            // Start/expiry trimming changes operational authority state even when a
            // new pairing is refused for capacity. Persist that bounded state before
            // returning either outcome.
            stateFile.Save(authority);
            if (!result.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(result.Failure);
            }

            return Results.Ok(result.Value);
        });

        app.MapGet("/api/v1/pairings/{pairingId}", (
            string pairingId,
            OwnerAuthorityStore authority,
            OwnerAuthorityStateFile stateFile) =>
        {
            var result = authority.GetPairingStatus(pairingId);
            // Polling can cause a pending record to become expired, so retain that
            // state before replying.
            stateFile.Save(authority);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : OwnerFailures.ToHttpResult(result.Failure);
        });

        app.MapPost("/api/v1/pairings/activate", (
            ActivateOwnerPairingHttpRequest request,
            OwnerAuthorityStore authority,
            OwnerAuthorityStateFile stateFile) =>
        {
            var result = authority.ActivatePairing(new OwnerPairingActivationRequest(
                request?.PairingId ?? string.Empty,
                request?.CanonicalProof ?? string.Empty,
                request?.SignatureBase64 ?? string.Empty));
            // Expiry is applied while evaluating activation, including rejected
            // activation requests, so persist before returning the result.
            stateFile.Save(authority);
            if (!result.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(result.Failure);
            }

            return Results.Ok(result.Value);
        });

        // This route is served only by the second loopback-only listener configured by
        // the deployment. Tailscale Serve forwards the normal viewer port, never this
        // approval port, so a remote client cannot bootstrap itself.
        app.MapPost("/api/v1/local/pairings/{pairingId}/approve", (
            string pairingId,
            LocalPairingApprovalHttpRequest request,
            HttpContext context,
            OwnerPairingHostOptions options,
            OwnerAuthorityStore authority,
            OwnerAuthorityStateFile stateFile) =>
        {
            if (!options.IsLocalApprovalRequest(context))
            {
                return Results.NotFound();
            }

            var result = authority.ApprovePendingPairingLocally(pairingId, request?.PairingCode ?? string.Empty);
            // A mismatch increments the durable bounded-attempt counter, so a restart
            // must not erase unsuccessful approval attempts.
            stateFile.Save(authority);
            if (!result.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(result.Failure);
            }

            return Results.Ok(result.Value);
        });

        // Recovery deliberately shares the separate host-local listener with first
        // pairing approval. If every paired Windows device is lost or revoked, the
        // host can still revoke a stale public key before pairing a replacement; this
        // path is not forwarded by Tailscale Serve.
        app.MapPost("/api/v1/local/devices/{deviceId}/revoke", (
            string deviceId,
            LocalDeviceRevokeHttpRequest _,
            HttpContext context,
            OwnerPairingHostOptions options,
            OwnerAuthorityStore authority,
            OwnerAuthorityStateFile stateFile) =>
        {
            if (!options.IsLocalApprovalRequest(context))
            {
                return Results.NotFound();
            }

            var result = authority.RevokeDeviceLocally(deviceId);
            stateFile.Save(authority);
            if (!result.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(result.Failure);
            }

            return Results.Ok(result.Value);
        });

        app.MapPost("/api/v1/owner/challenges", (
            IssueOwnerChallengeHttpRequest request,
            OwnerAuthorityStore authority,
            OwnerAuthorityStateFile stateFile) =>
        {
            var result = authority.IssueChallenge(new OwnerChallengeIssueRequest(
                request?.DeviceId ?? string.Empty,
                request?.RequestId ?? string.Empty,
                request?.CanonicalProof ?? string.Empty,
                request?.SignatureBase64 ?? string.Empty));
            // Challenge expiry/retention trimming also happens on rejected requests.
            stateFile.Save(authority);
            if (!result.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(result.Failure);
            }

            return Results.Ok(result.Value! with
            {
                SupportedActionPayloads = [OwnerHttpBinding.WorldCreationPayloadDomain],
            });
        });
    }
}
