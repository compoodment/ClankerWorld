using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapStartupRecovery(WebApplication app, bool isPrivateWorld)
    {
        app.MapPost("/api/v1/owner/recovery/status", (OwnerSignedHttpRequest<OwnerControlAction> request,
            OwnerRequestAuthorizer authorizer, IServiceProvider services) =>
        {
            if (!IsControl(request, "recovery-status")) return Results.BadRequest();
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/recovery/status",
                OwnerHttpBinding.EmptyPayload("recovery-status"));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            return isPrivateWorld
                ? Results.Ok(services.GetRequiredService<PrivateWorldStartupRecovery>().Capture())
                : Results.Ok(new StartupRecoveryStatus(false, null, null));
        });

        app.MapPost("/api/v1/owner/recovery/restore", (OwnerSignedHttpRequest<OwnerManualSaveAction> request,
            OwnerRequestAuthorizer authorizer, OwnerAuthorityStore authority, IServiceProvider services) =>
        {
            if (request?.Action is not { Operation: "recover", Value: { Length: 32 } id } action ||
                !id.All(char.IsAsciiHexDigit)) return Results.BadRequest();
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/recovery/restore",
                OwnerHttpBinding.ManualSavePayload(action));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict();
            try
            {
                var recovery = services.GetRequiredService<PrivateWorldStartupRecovery>();
                if (!recovery.Pending || recovery.Capture().Autosave?.Id != id)
                    return Results.Conflict(new { error = "Read recovery again and choose the offered autosave." });
                return Results.Ok(recovery.Recover(id, services.GetRequiredService<WorldAutosaveStore>(), authority));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or
                UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                return Results.Conflict(new { error = "Recovery could not finish. Your saved files are kept. Check storage and read recovery again." });
            }
        });
    }
}
