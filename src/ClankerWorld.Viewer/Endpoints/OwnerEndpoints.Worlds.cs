using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapWorlds(WebApplication app, bool isPrivateWorld)
    {
        app.MapPost("/api/v1/owner/worlds/list", (
            OwnerSignedHttpRequest<OwnerControlAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (!IsControl(request, "list-worlds"))
                return Results.BadRequest(new { error = "A world-list action is required." });
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/worlds/list",
                OwnerHttpBinding.EmptyPayload("list-worlds"));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "World selection requires a private world." });
            return Results.Ok(services.GetRequiredService<WorldSelectionCoordinator>().List());
        });

        app.MapPost("/api/v1/owner/worlds/create", (
            OwnerSignedHttpRequest<OwnerWorldCreationAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (request?.Action is not { } action)
                return Results.BadRequest(new { error = "World options are required." });
            string creationPayload;
            try { creationPayload = OwnerHttpBinding.WorldCreationPayload(action); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "World name, seed, and size are required." }); }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/worlds/create",
                creationPayload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "World creation requires a private world." });
            if (!TryWorldOptions(action, out var options))
                return Results.BadRequest(new { error = "World seed, size, or water choice is invalid." });
            try
            {
                var entry = services.GetRequiredService<WorldSelectionCoordinator>().Create(action.Name, options!);
                return Results.Ok(entry);
            }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
            catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
        });

        app.MapPost("/api/v1/owner/worlds/preview", (
            OwnerSignedHttpRequest<OwnerWorldCreationAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (request?.Action is not { } action)
                return Results.BadRequest(new { error = "World options are required." });
            string payload;
            try { payload = OwnerHttpBinding.WorldCreationPayload(action); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "World name, seed, and size are required." }); }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/worlds/preview", payload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "World preview requires a private world." });
            if (!TryWorldOptions(action, out var options))
                return Results.BadRequest(new { error = "World seed, size, or water choice is invalid." });
            try { return Results.Ok(services.GetRequiredService<WorldSelectionCoordinator>().Preview(options!)); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
            catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
        });

        app.MapPost("/api/v1/owner/worlds/select", (
            OwnerSignedHttpRequest<OwnerManualSaveAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (request?.Action is not { Operation: "select-world" } action)
                return Results.BadRequest(new { error = "A world selection is required." });
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/worlds/select",
                OwnerHttpBinding.ManualSavePayload(action));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "World selection requires a private world." });
            try { return Results.Ok(services.GetRequiredService<WorldSelectionCoordinator>().Select(action.Value)); }
            catch (FileNotFoundException) { return Results.NotFound(new { error = "The selected world does not exist." }); }
            catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
            catch (InvalidDataException) { return Results.Conflict(new { error = "The selected world is invalid." }); }
            catch (ArgumentException) { return Results.Conflict(new { error = "This world's model configuration is unavailable." }); }
        });
    }
}
