using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapRuntimeControl(WebApplication app, bool isPrivateWorld)
    {
        app.MapPost("/api/v1/owner/control/life-pace", (
            OwnerSignedHttpRequest<OwnerLifePaceAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services,
            OwnerWorldObservationStore observations,
            ILogger<PrivateWorldRuntimeService> logger) =>
        {
            if (request.Action is null || request.Action.Rate is not (1 or 365 or 1_460))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["action.rate"] = ["Life pace must be 1, 365 or 1460."] });
            }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/control/life-pace", OwnerHttpBinding.LifePacePayload(request.Action));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Life pacing requires a private world." });
            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            bool changed;
            try
            {
                changed = runtime.SetLifePace(request.Action.Rate);
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict(new { error = "Pause the world before changing life pace." });
            }
            // A retry must also persist a prior in-memory change whose first save failed.
            services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
            if (changed) OwnerLifePaceTelemetry.Changed(logger, runtime.WorldTick, request.Action.Rate);
            return Results.Ok(OwnerControlReceipt.From("life_pace", changed, observations.GetSnapshot()));
        });

        app.MapPost("/api/v1/owner/control/jev-assistance", (
            OwnerSignedHttpRequest<OwnerJevAssistanceAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services,
            OwnerWorldObservationStore observations,
            WorldJevPolicy jevPolicy,
            ILogger<PrivateWorldRuntimeService> logger) =>
        {
            if (request?.Action is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = ["A Jev setting is required."] });
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/control/jev-assistance",
                OwnerHttpBinding.JevAssistancePayload(request.Action));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Jev assistance requires a private world." });
            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            bool changed;
            try
            {
                changed = runtime.SetJevEnabled(request.Action.Enabled);
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict(new { error = "Pause the world before changing Jev assistance." });
            }
            jevPolicy.Set(runtime.JevEnabled, runtime.JevPolicyRevision);
            services.GetRequiredService<PrivateWorldStateFile>().Save(runtime);
            if (changed) OwnerJevAssistanceTelemetry.Changed(logger, runtime.WorldTick, runtime.JevEnabled);
            return Results.Ok(OwnerControlReceipt.From("jev_assistance", changed, observations.GetSnapshot()));
        });

        app.MapPost("/api/v1/owner/control/pause", (
            OwnerSignedHttpRequest<OwnerControlAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (!IsControl(request, "pause"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action.operation"] = ["This endpoint only accepts the pause operation."],
                });
            }

            var authorization = authorizer.Authorize(
                request,
                "POST",
                "/api/v1/owner/control/pause",
                OwnerHttpBinding.EmptyPayload("pause"));
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            if (isPrivateWorld)
            {
                var privateRuntime = services.GetRequiredService<PrivateWorldRuntime>();
                var privateStateFile = services.GetRequiredService<PrivateWorldStateFile>();
                var wasPaused = privateRuntime.Society.IsPaused;
                privateRuntime.Pause();
                var changed = !wasPaused && privateRuntime.Society.IsPaused;
                // A prior failed write may already have changed memory. Even a no-op
                // retry must durably acknowledge the requested pause.
                privateStateFile.Save(privateRuntime);

                return Results.Ok(OwnerControlReceipt.From("pause", changed, privateRuntime.ExportState()));
            }

            var runtime = services.GetRequiredService<OwnerWorldRuntime>();
            var stateFile = services.GetRequiredService<OwnerWorldStateFile>();
            var legacyChanged = runtime.Pause($"owner-device:{authorization.Value!.DeviceId}");
            if (legacyChanged)
            {
                stateFile.Save(runtime);
            }

            return Results.Ok(OwnerControlReceipt.From("pause", legacyChanged, runtime.Capture().Snapshot));
        });

        app.MapPost("/api/v1/owner/control/resume", (
            OwnerSignedHttpRequest<OwnerControlAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (!IsControl(request, "resume"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action.operation"] = ["This endpoint only accepts the resume operation."],
                });
            }

            var authorization = authorizer.Authorize(
                request,
                "POST",
                "/api/v1/owner/control/resume",
                OwnerHttpBinding.EmptyPayload("resume"));
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            if (isPrivateWorld)
            {
                var privateRuntime = services.GetRequiredService<PrivateWorldRuntime>();
                if (privateRuntime.FounderSetup is { Started: false })
                    return Results.Conflict(new { message = "Place four configured founders, then select Start World." });
                if (services.GetRequiredService<ProviderUsageStore>().Capture().AccountingError is { } accountingError)
                    return Results.Conflict(new { message = accountingError });
                if (services.GetRequiredService<ProviderUsageStore>().Capture().LimitReached)
                    return Results.Conflict(new { message = "The paid-call limit is reached. Grant more calls or turn off the limit in World Settings before resuming." });
                var privateStateFile = services.GetRequiredService<PrivateWorldStateFile>();
                var wasPaused = privateRuntime.Society.IsPaused;
                privateRuntime.Resume();
                var changed = wasPaused && !privateRuntime.Society.IsPaused;
                if (changed)
                {
                    privateStateFile.Save(privateRuntime);
                }

                return Results.Ok(OwnerControlReceipt.From("resume", changed, privateRuntime.ExportState()));
            }

            var runtime = services.GetRequiredService<OwnerWorldRuntime>();
            var stateFile = services.GetRequiredService<OwnerWorldStateFile>();
            var legacyChanged = runtime.Resume($"owner-device:{authorization.Value!.DeviceId}");
            if (legacyChanged)
            {
                stateFile.Save(runtime);
            }

            return Results.Ok(OwnerControlReceipt.From("resume", legacyChanged, runtime.Capture().Snapshot));
        });
    }
}
