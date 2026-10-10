using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapRecoveryCleanup(WebApplication app, bool isPrivateWorld)
    {
        app.MapPost("/api/v1/owner/saves/recovery-cleanup", (
            OwnerSignedHttpRequest<OwnerRecoveryCleanupAction> request,
            OwnerRequestAuthorizer authorizer, ProviderConfigurationStore providers,
            PrivateWorldRuntime runtime, PrivateWorldStateFile stateFile, ManualWorldSaveStore saves) =>
        {
            if (request?.Action is not { } action || action.Operation is not ("preview" or "apply") ||
                action.KeepCount is < 1 or > 10 || string.IsNullOrWhiteSpace(action.WorldId) ||
                (action.Operation == "preview" ? action.ExpectedDigest is not null :
                    action.ExpectedDigest is not { Length: 64 } || !action.ExpectedDigest.All(char.IsAsciiHexDigit)))
                return Results.BadRequest(new { error = "Choose a recovery count and preview before confirming cleanup." });
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/recovery-cleanup",
                OwnerHttpBinding.RecoveryCleanup(action));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Recovery cleanup requires a private world." });
            lock (providers.WorldMutationGate)
            {
                if (!runtime.Society.IsPaused || runtime.Society.WorldId != action.WorldId)
                    return Results.Conflict(new { error = "Pause this world and reopen its recovery preview." });
                try
                {
                    if (action.Operation == "preview")
                        return Results.Ok(saves.PreviewRecoveryCleanup(action.WorldId, action.KeepCount, stateFile));
                    var removed = saves.CleanRecoveryHistory(action.WorldId, action.KeepCount, action.ExpectedDigest!, stateFile);
                    // Only the listed recovery checkpoints are removed. Shared
                    // archives, manual saves and migration originals stay in place.
                    return Results.Ok(new OwnerRecoveryCleanupReceipt(removed, true));
                }
                catch (InvalidOperationException)
                {
                    return Results.Conflict(new { error = "Recovery history changed. Preview it again before deleting." });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                {
                    return Results.Conflict(new { error = "Cleanup could not finish. Refresh the saves; pending deletion will be retried when the host starts." });
                }
            }
        });
    }
}
