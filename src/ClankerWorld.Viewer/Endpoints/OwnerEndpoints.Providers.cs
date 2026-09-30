using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapProviders(WebApplication app)
    {
        app.MapPost("/api/v1/owner/providers/status", (
            OwnerSignedHttpRequest<OwnerProviderStatusAction> request,
            OwnerRequestAuthorizer authorizer,
            ProviderConfigurationStore providers) =>
        {
            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["A provider-status action is required."],
                });
            }

            var authorization = authorizer.Authorize(
                request,
                "POST",
                "/api/v1/owner/providers/status",
                OwnerHttpBinding.ProviderStatusPayload());
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            return Results.Ok(providers.CaptureStatus());
        });

        // Lists the chat models a key can use so the owner can pick one. The
        // key never leaves the host, and a pasted key is used without saving it.
        app.MapPost("/api/v1/owner/providers/models", async (
            OwnerSignedHttpRequest<OwnerProviderModelListAction> request,
            OwnerRequestAuthorizer authorizer,
            ProviderModelCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            if (request?.Action is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["A model-list action is required."],
                });
            string payload;
            try { payload = OwnerHttpBinding.ProviderModelListPayload(request.Action); }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
            }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/providers/models", payload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            try
            {
                return Results.Ok(await catalog.ListAsync(request.Action, cancellationToken).ConfigureAwait(false));
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
            }
        });

        app.MapPost("/api/v1/owner/usage/status", (
            OwnerSignedHttpRequest<OwnerUsageStatusAction> request,
            OwnerRequestAuthorizer authorizer,
            ProviderUsageStore usage) =>
        {
            if (request?.Action is null)
                return Results.BadRequest(new { error = "A usage-status action is required." });
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/usage/status",
                OwnerHttpBinding.UsageStatusPayload());
            return authorization.IsSuccess ? Results.Ok(usage.Capture()) : OwnerFailures.ToHttpResult(authorization.Failure);
        });

        app.MapPost("/api/v1/owner/usage/limit", (
            OwnerSignedHttpRequest<ProviderUsageLimitAction> request,
            OwnerRequestAuthorizer authorizer,
            ProviderUsageStore usage) =>
        {
            if (request?.Action is null)
                return Results.BadRequest(new { error = "A usage-limit action is required." });
            string payload;
            try { payload = OwnerHttpBinding.UsageLimitPayload(request.Action); }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
            }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/usage/limit", payload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            try
            {
                var status = usage.Configure(request.Action);
                ProviderUsageTelemetry.LimitConfigured(app.Logger, status.AttemptLimit is not null,
                    status.AttemptLimit, status.Attempts);
                return Results.Ok(status);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = [exception.Message] });
            }
        });

        app.MapPost("/api/v1/owner/providers/slots/delete", (
            OwnerSignedHttpRequest<OwnerCredentialSlotDeletionAction> request,
            OwnerRequestAuthorizer authorizer,
            ProviderConfigurationStore providers,
            ILogger<ProviderConfigurationStore> logger) =>
        {
            if (request?.Action is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["A credential-slot deletion action is required."],
                });

            string payload;
            try
            {
                payload = OwnerHttpBinding.CredentialSlotDeletionPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(
                request, "POST", "/api/v1/owner/providers/slots/delete", payload);
            if (!authorization.IsSuccess)
                return OwnerFailures.ToHttpResult(authorization.Failure);

            try
            {
                var status = providers.DeleteCredentialSlot(request.Action.CredentialSlotId);
                OwnerCredentialSlotTelemetry.Deleted(logger, "deleted", request.Action.CredentialSlotId);
                return Results.Ok(status);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }
            catch (InvalidOperationException exception)
            {
                OwnerCredentialSlotTelemetry.Deleted(logger, "assigned", request.Action.CredentialSlotId);
                return Results.Conflict(new { error = exception.Message });
            }
        });

        app.MapPost("/api/v1/owner/providers/configure", (
            OwnerSignedHttpRequest<OwnerProviderConfigurationAction> request,
            OwnerRequestAuthorizer authorizer,
            ProviderConfigurationStore providers,
            OwnerWorldObservationStore observations) =>
        {
            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["A provider-configuration action is required."],
                });
            }

            string payload;
            try
            {
                payload = OwnerHttpBinding.ProviderConfigurationPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(
                request,
                "POST",
                "/api/v1/owner/providers/configure",
                payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            try
            {
                if (request.Action.InhabitantId is { } target &&
                    !observations.GetSnapshot().Inhabitants.Any(item => item.Id == target))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["inhabitantId"] = ["Choose an inhabitant in this world."],
                    });
                }
                return Results.Ok(providers.Configure(request.Action));
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }
        });

        // Legacy diagnostic routes deliberately remain present only to make their
        // read denial explicit (and to retain a 405 response for accidental POSTs).
    }
}
