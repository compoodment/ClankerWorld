using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapDevices(WebApplication app)
    {
        app.MapPost("/api/v1/owner/pairings/approve", (
            OwnerSignedHttpRequest<OwnerPairingApprovalAction> request,
            OwnerRequestAuthorizer authorizer,
            OwnerAuthorityStore authority,
            OwnerAuthorityStateFile stateFile) =>
        {
            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["A pairing ID and comparison code are required."],
                });
            }

            string payload;
            try
            {
                payload = OwnerHttpBinding.PairingApprovalPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/pairings/approve", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            var approved = authority.ApprovePendingPairing(request.Action.PairingId, request.Action.PairingCode);
            // Treat paired-device approval exactly like local bootstrap approval: a
            // failed comparison changes the bounded durable attempt count.
            stateFile.Save(authority);
            if (!approved.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(approved.Failure);
            }

            return Results.Ok(approved.Value);
        });

        app.MapPost("/api/v1/owner/devices/revoke", (
            OwnerSignedHttpRequest<OwnerDeviceManagementAction> request,
            OwnerRequestAuthorizer authorizer,
            OwnerAuthorityStore authority,
            OwnerAuthorityStateFile stateFile) =>
        {
            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action.deviceId"] = ["A device ID is required."],
                });
            }

            string payload;
            try
            {
                payload = OwnerHttpBinding.DeviceManagementPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/devices/revoke", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            var revoked = authority.RevokeDevice(request.Action.DeviceId);
            stateFile.Save(authority);
            if (!revoked.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(revoked.Failure);
            }

            return Results.Ok(revoked.Value);
        });

        // Paired devices may inspect the public-key registry, but this is still a
        // signed, one-use request rather than a bearer-style read endpoint. The
        // result contains only OwnerDevice records: public SPKIs/fingerprints and
        // lifecycle timestamps, never pairing codes, challenge nonces, or secrets.
        app.MapPost("/api/v1/owner/devices/list", (
            OwnerSignedHttpRequest<OwnerDeviceListAction> request,
            OwnerRequestAuthorizer authorizer,
            OwnerAuthorityStore authority) =>
        {
            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["A device-list action is required."],
                });
            }

            var authorization = authorizer.Authorize(
                request,
                "POST",
                "/api/v1/owner/devices/list",
                OwnerHttpBinding.DeviceListPayload());
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            return Results.Ok(authority.GetDevices());
        });

        // Provider status is owner-only even though it contains no key material. It
        // reveals which hosted account integration is active and therefore uses the
        // same one-use signed request boundary as every other private-world control.
    }
}
