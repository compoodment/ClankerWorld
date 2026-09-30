using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapSaves(WebApplication app, bool isPrivateWorld)
    {
        app.MapPost("/api/v1/owner/saves/list", (
            OwnerSignedHttpRequest<OwnerControlAction> request,
            OwnerRequestAuthorizer authorizer,
            ManualWorldSaveStore saves,
            PrivateWorldRuntime runtime) =>
        {
            if (!IsControl(request, "list-saves"))
                return Results.BadRequest(new { error = "A save-list action is required." });
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/list",
                OwnerHttpBinding.EmptyPayload("list-saves"));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Manual saves require a private world." });
            return Results.Ok(saves.List(runtime.Society.WorldId));
        });

        app.MapPost("/api/v1/owner/saves/autosave/status", (
            OwnerSignedHttpRequest<OwnerControlAction> request,
            OwnerRequestAuthorizer authorizer,
            WorldAutosaveStore autosave) =>
        {
            if (!IsControl(request, "autosave-status"))
                return Results.BadRequest(new { error = "An autosave-status action is required." });
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/autosave/status",
                OwnerHttpBinding.EmptyPayload("autosave-status"));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Autosave settings require a private world." });
            return Results.Ok(autosave.Capture());
        });

        app.MapPost("/api/v1/owner/saves/autosave/configure", (
            OwnerSignedHttpRequest<OwnerAutosaveConfigurationAction> request,
            OwnerRequestAuthorizer authorizer,
            WorldAutosaveStore autosave,
            ManualWorldSaveStore saves,
            PrivateWorldRuntime runtime,
            ILogger<PrivateWorldRuntimeService> logger) =>
        {
            if (request?.Action is not { } action)
                return Results.BadRequest(new { error = "Autosave settings are required." });
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/autosave/configure",
                OwnerHttpBinding.AutosaveConfigurationPayload(action));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Autosave settings require a private world." });
            if (!runtime.Society.IsPaused)
            {
                ManualWorldSaveTelemetry.Rejected(logger, "autosave_configure", "not_paused");
                return Results.Conflict(new { error = "Pause the world before changing autosave settings." });
            }
            try
            {
                var updated = autosave.Configure(action.Enabled, action.IntervalMinutes, action.RotationCount);
                saves.KeepNewestAutosaves(Math.Max(1, updated.RotationCount), worldId: updated.WorldId);
                ManualWorldSaveTelemetry.AutosaveConfigured(logger, updated.Enabled,
                    updated.IntervalMinutes, updated.RotationCount, runtime.WorldTick);
                return Results.Ok(updated);
            }
            catch (ArgumentException)
            {
                ManualWorldSaveTelemetry.Rejected(logger, "autosave_configure", "invalid_option");
                return Results.BadRequest(new { error = "Choose an offered interval and rotation count." });
            }
        });

        app.MapPost("/api/v1/owner/saves/create", (
            OwnerSignedHttpRequest<OwnerManualSaveAction> request,
            OwnerRequestAuthorizer authorizer,
            ManualWorldSaveStore saves,
            PrivateWorldRuntime runtime,
            ProviderConfigurationStore providers,
            WorldAutosaveStore autosave,
            ILogger<PrivateWorldRuntimeService> logger) =>
        {
            if (request?.Action is not { Operation: "create" } action)
                return Results.BadRequest(new { error = "A named save action is required." });
            string payload;
            try { payload = OwnerHttpBinding.ManualSavePayload(action); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "A save name is required." }); }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/create", payload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Manual saves require a private world." });
            try
            {
                ManualWorldSave saved;
                lock (providers.WorldMutationGate)
                    saved = saves.Create(action.Value, runtime,
                        providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture());
                ManualWorldSaveTelemetry.Created(logger, saved.Id, saved.WorldTick);
                return Results.Ok(saved);
            }
            catch (ArgumentException)
            {
                ManualWorldSaveTelemetry.Rejected(logger, "create", "invalid_name");
                return Results.BadRequest(new { error = "Save name must be 1–80 printable characters." });
            }
            catch (InvalidOperationException)
            {
                ManualWorldSaveTelemetry.Rejected(logger, "create", "not_paused");
                return Results.Conflict(new { error = "Pause the world before saving." });
            }
        });

        app.MapPost("/api/v1/owner/saves/overwrite", (
            OwnerSignedHttpRequest<OwnerManualSaveAction> request,
            OwnerRequestAuthorizer authorizer,
            ManualWorldSaveStore saves,
            PrivateWorldRuntime runtime,
            ProviderConfigurationStore providers,
            WorldAutosaveStore autosave,
            ILogger<PrivateWorldRuntimeService> logger) =>
        {
            if (request?.Action is not { Operation: "overwrite" } action)
                return Results.BadRequest(new { error = "A selected save ID is required." });
            string payload;
            try { payload = OwnerHttpBinding.ManualSavePayload(action); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "A selected save ID is required." }); }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/overwrite", payload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Manual saves require a private world." });
            try
            {
                ManualSaveOverwriteReceipt result;
                lock (providers.WorldMutationGate)
                    result = saves.Overwrite(action.Value, runtime,
                        providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture());
                ManualWorldSaveTelemetry.Overwritten(logger, result.Saved.Id, result.BackupId,
                    result.Saved.WorldTick);
                return Results.Ok(result);
            }
            catch (FileNotFoundException)
            {
                ManualWorldSaveTelemetry.Rejected(logger, "overwrite", "missing");
                return Results.NotFound(new { error = "The selected save no longer exists." });
            }
            catch (ArgumentException)
            {
                ManualWorldSaveTelemetry.Rejected(logger, "overwrite", "invalid_id");
                return Results.BadRequest(new { error = "Invalid save ID." });
            }
            catch (InvalidDataException)
            {
                ManualWorldSaveTelemetry.Rejected(logger, "overwrite", "invalid_checkpoint");
                return Results.Conflict(new { error = "The selected checkpoint is invalid and was preserved." });
            }
            catch (InvalidOperationException)
            {
                ManualWorldSaveTelemetry.Rejected(logger, "overwrite", "not_paused_or_wrong_world");
                return Results.Conflict(new { error = "Pause the world and select one of its named saves." });
            }
        });

        app.MapPost("/api/v1/owner/saves/load", (
            OwnerSignedHttpRequest<OwnerManualSaveAction> request,
            OwnerRequestAuthorizer authorizer,
            ManualWorldSaveStore saves,
            PrivateWorldRuntime runtime,
            PrivateWorldStateFile stateFile,
            ProviderConfigurationStore providers,
            WorldAutosaveStore autosave,
            WorldJevPolicy jevPolicy,
            ILogger<PrivateWorldRuntimeService> logger) =>
        {
            if (request?.Action is not { Operation: "load" } action)
                return Results.BadRequest(new { error = "A save-load action is required." });
            string payload;
            try { payload = OwnerHttpBinding.ManualSavePayload(action); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "A save ID is required." }); }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/saves/load", payload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Manual saves require a private world." });
            lock (providers.WorldMutationGate)
            {
                if (!runtime.Society.IsPaused)
                {
                    ManualWorldSaveTelemetry.Rejected(logger, "load", "not_paused");
                    return Results.Conflict(new { error = "Pause the world before loading." });
                }
                try
                {
                    var committed = saves.ReadCommitted(action.Value);
                    var checkpoint = committed.Checkpoint;
                    stateFile.VerifyRequiredHistory(checkpoint);
                    var assignments = committed.Assignments;
                    var autosaveSettings = committed.AutosaveSettings;
                    if (!string.Equals(checkpoint.WorldSeed, runtime.ExportState().WorldSeed, StringComparison.Ordinal))
                    {
                        ManualWorldSaveTelemetry.Rejected(logger, "load", "different_world");
                        return Results.Conflict(new { error = "This save belongs to a different world." });
                    }
                    // A rewind must never destroy the current timeline. The backup is a
                    // normal named checkpoint, visible in Load Saves immediately.
                    var backup = saves.Create("Before loading", runtime,
                        providers.CaptureRuntimeConfiguration().Assignments ?? [], autosave.Capture());
                    try
                    {
                        runtime.LoadPausedCheckpoint(checkpoint);
                        stateFile.Save(runtime);
                        providers.RestoreWorldAssignments(assignments);
                        if (autosaveSettings is not null) autosave.RestoreFromCheckpoint(autosaveSettings);
                        jevPolicy.Initialize(runtime.JevEnabled, runtime.JevPolicyRevision);
                    }
                    catch
                    {
                        runtime.LoadPausedCheckpoint(saves.Read(backup.Id));
                        stateFile.Save(runtime);
                        providers.RestoreWorldAssignments(saves.ReadAssignments(backup.Id));
                        if (saves.ReadAutosaveSettings(backup.Id) is { } previousAutosave)
                            autosave.RestoreFromCheckpoint(previousAutosave);
                        jevPolicy.Initialize(runtime.JevEnabled, runtime.JevPolicyRevision);
                        throw;
                    }
                    ManualWorldSaveTelemetry.Loaded(logger, action.Value, backup.Id, runtime.WorldTick);
                    return Results.Ok(new { loadedId = action.Value, backupId = backup.Id, worldTick = runtime.WorldTick });
                }
                catch (FileNotFoundException)
                {
                    ManualWorldSaveTelemetry.Rejected(logger, "load", "missing");
                    return Results.NotFound(new { error = "The manual save no longer exists." });
                }
                catch (ArgumentException)
                {
                    ManualWorldSaveTelemetry.Rejected(logger, "load", "invalid_id");
                    return Results.BadRequest(new { error = "Invalid save ID." });
                }
                catch (InvalidDataException)
                {
                    ManualWorldSaveTelemetry.Rejected(logger, "load", "invalid_checkpoint_or_credential");
                    return Results.Conflict(new { error = "The save is invalid or its credential slot is unavailable." });
                }
                catch (InvalidOperationException)
                {
                    ManualWorldSaveTelemetry.Rejected(logger, "load", "not_paused");
                    return Results.Conflict(new { error = "Pause the world before loading." });
                }
            }
        });
    }
}
