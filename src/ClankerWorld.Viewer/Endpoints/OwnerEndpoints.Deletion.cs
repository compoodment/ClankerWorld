using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapDeletion(WebApplication app, bool isPrivateWorld)
    {
        app.MapPost("/api/v1/owner/delete", (
            OwnerSignedHttpRequest<OwnerDeletionAction> request,
            OwnerRequestAuthorizer authorizer,
            ProviderConfigurationStore providers,
            PrivateWorldRuntime runtime,
            PrivateWorldStateFile stateFile,
            ManualWorldSaveStore saves,
            IServiceProvider services,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("WorldDeletion");
            if (request?.Action is not { } action || action.Kind is not ("save" or "world"))
                return Results.BadRequest(new { error = "Select a save or world to delete." });
            string payload;
            try { payload = OwnerHttpBinding.Deletion(action); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "The deletion target is invalid." }); }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/delete", payload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Deletion requires a private world." });
            lock (providers.WorldMutationGate)
            {
                if (!runtime.Society.IsPaused)
                    return Results.Conflict(new { error = "Pause the world before deleting saves or worlds." });
                try
                {
                    if (action.Kind == "save")
                    {
                        if (action.WorldId != runtime.Society.WorldId || action.ExpectedCreatedUtc is null)
                            return Results.Conflict(new { error = "The world changed. Reopen the save list before deleting." });
                        saves.Delete(action.Id, action.WorldId, action.ExpectedCreatedUtc.Value);
                    }
                    else
                    {
                        if (action.WorldId == runtime.Society.WorldId)
                            return Results.Conflict(new { error = "Open or create another world before deleting this one." });
                        services.GetRequiredService<WorldCatalogStore>().Delete(action.Id, action.WorldId,
                            saves.DeleteWorldSnapshots);
                    }
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound(new { error = "The selected item no longer exists. Refresh the list." });
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new { error = "The deletion target is invalid." });
                }
                catch (InvalidOperationException)
                {
                    return Results.Conflict(new { error = "The selection changed or is active. Reopen the list before deleting." });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
                {
                    // Do not claim success: the durable intent may have committed,
                    // but cleanup was interrupted. Repeating the same target is safe.
                    ManualWorldSaveTelemetry.DeletionPending(logger,
                        action.Kind, exception.GetType().Name);
                    return Results.Conflict(new { error = "Deletion could not finish. Refresh the list; any pending cleanup will be retried when the host starts." });
                }
                var complete = true;
                try { stateFile.ReclaimUnreferencedHistory(); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
                {
                    complete = false;
                    ManualWorldSaveTelemetry.DeletionPending(logger,
                        "history", exception.GetType().Name);
                }
                ManualWorldSaveTelemetry.Deleted(logger, action.Kind, complete);
                return Results.Ok(new OwnerDeletionReceipt(action.Id, complete));
            }
        });
    }
}
