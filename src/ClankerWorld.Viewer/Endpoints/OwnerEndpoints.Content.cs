using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapContent(WebApplication app, bool isPrivateWorld)
    {
        app.MapBuildingDesign(isPrivateWorld);

        app.MapPost("/api/v1/owner/content/propose", (
            OwnerSignedHttpRequest<OwnerContentPackageAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (!isPrivateWorld)
            {
                return Results.Conflict(new OwnerControlFailure(
                    "private_world_content_required",
                    "Data-only content governance is available only in the integrated private world."));
            }

            if (!OwnerContentBinding.TryMapManifest(request?.Action, out var manifest, out var failure))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [failure],
                });
            }

            string payload;
            try
            {
                payload = OwnerContentBinding.ProposePayload(request!.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/propose", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
            try
            {
                var record = runtime.ProposeContent(manifest!);
                stateFile.Save(runtime);
                return Results.Ok(OwnerContentPackageReceipt.From("propose", record));
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
                return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
            }
        });

        app.MapPost("/api/v1/owner/content/validate", (
            OwnerSignedHttpRequest<OwnerContentPackageIdAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (!isPrivateWorld)
            {
                return Results.Conflict(new OwnerControlFailure(
                    "private_world_content_required",
                    "Data-only content governance is available only in the integrated private world."));
            }

            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action.packageId"] = ["A package ID is required."],
                });
            }

            string payload;
            try
            {
                payload = OwnerContentBinding.PackageIdPayload("validate", request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/validate", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
            try
            {
                var resolution = runtime.ResolveContent(request.Action.PackageId);
                var record = runtime.ValidateContent(request.Action.PackageId, resolution);
                stateFile.Save(runtime);
                return Results.Ok(OwnerContentPackageReceipt.From("validate", record));
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
                return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new OwnerControlFailure("content_not_found", exception.Message));
            }
        });

        app.MapPost("/api/v1/owner/content/approve", (
            OwnerSignedHttpRequest<OwnerContentPackageIdAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (!isPrivateWorld)
            {
                return Results.Conflict(new OwnerControlFailure(
                    "private_world_content_required",
                    "Data-only content governance is available only in the integrated private world."));
            }

            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action.packageId"] = ["A package ID is required."],
                });
            }

            string payload;
            try
            {
                payload = OwnerContentBinding.PackageIdPayload("approve", request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/approve", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
            try
            {
                var record = runtime.ApproveContent(request.Action.PackageId);
                stateFile.Save(runtime);
                return Results.Ok(OwnerContentPackageReceipt.From("approve", record));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new OwnerControlFailure("content_not_found", exception.Message));
            }
        });

        app.MapPost("/api/v1/owner/content/stage", (
            OwnerSignedHttpRequest<OwnerContentPackageIdAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (!isPrivateWorld)
            {
                return Results.Conflict(new OwnerControlFailure(
                    "private_world_content_required",
                    "Data-only content governance is available only in the integrated private world."));
            }

            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action.packageId"] = ["A package ID is required."],
                });
            }

            string payload;
            try
            {
                payload = OwnerContentBinding.PackageIdPayload("stage", request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/stage", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
            try
            {
                var record = runtime.StageContent(request.Action.PackageId);
                stateFile.Save(runtime);
                return Results.Ok(OwnerContentPackageReceipt.From("stage", record));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new OwnerControlFailure("content_not_found", exception.Message));
            }
        });

        app.MapPost("/api/v1/owner/content/rollback", (
            OwnerSignedHttpRequest<OwnerContentRollbackAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services,
            ILogger<PrivateWorldRuntimeService> logger) =>
        {
            if (!isPrivateWorld)
            {
                return Results.Conflict(new OwnerControlFailure(
                    "private_world_content_required",
                    "Data-only content governance is available only in the integrated private world."));
            }

            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["A package ID and rollback reason are required."],
                });
            }

            string payload;
            try
            {
                payload = OwnerContentBinding.RollbackPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/rollback", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
            try
            {
                var record = runtime.RollbackContent(request.Action.PackageId, request.Action.Reason);
                stateFile.Save(runtime);
                OwnerContentTelemetry.Rollback(logger, runtime.WorldTick, record.Manifest.PackageId, "quarantined");
                return Results.Ok(OwnerContentPackageReceipt.From("rollback", record));
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
                OwnerContentTelemetry.Rollback(logger, runtime.WorldTick, request.Action.PackageId, "rejected");
                return Results.Conflict(new OwnerControlFailure("content_rejected", exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new OwnerControlFailure("content_not_found", exception.Message));
            }
        });

        app.MapPost("/api/v1/owner/buildings/place", (
            OwnerSignedHttpRequest<OwnerBuildingPlacementAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services,
            ILoggerFactory loggerFactory) =>
        {
            if (!isPrivateWorld)
            {
                return Results.Conflict(new OwnerControlFailure(
                    "private_world_required",
                    "Building placement is available only in the integrated private world."));
            }

            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["A building placement action is required."],
                });
            }

            string payload;
            try
            {
                payload = OwnerContentBinding.BuildingPlacementPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/buildings/place", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
            var previousBorderTiles = runtime.Towns.SingleOrDefault(item => item.Id == TownBorderRules.FirstTownId)?.BorderTiles.Count ?? 0;
            var result = runtime.PlaceBuilding(
                request.Action.InstanceId,
                request.Action.DefinitionId,
                new ClankerWorld.Simulation.Harness.GridPoint(request.Action.X, request.Action.Y));
            if (!result.Applied)
            {
                return Results.Conflict(new OwnerControlFailure("building_rejected", result.Failure ?? "Building placement was rejected."));
            }

            stateFile.Save(runtime);
            var placed = runtime.WorldSimulation.Buildings.Single(item => item.InstanceId == result.InstanceId);
            var town = placed.TownId is { } townId
                ? runtime.Towns.Single(item => item.Id == townId)
                : null;
            var telemetry = loggerFactory.CreateLogger("ClankerWorld.Town");
            TownTelemetry.Transition(telemetry, runtime.WorldTick, town?.Id ?? "none",
                town is null ? TownTransitionKind.BuildingUnassigned : TownTransitionKind.BuildingAssigned,
                town?.ResidentIds.Count ?? 0, town?.AssignedBuildingIds.Count ?? 0, town?.BorderTiles.Count ?? 0);
            if (town is not null && town.BorderTiles.Count != previousBorderTiles)
                TownTelemetry.Transition(telemetry, runtime.WorldTick, town.Id, TownTransitionKind.BorderExpanded,
                    town.ResidentIds.Count, town.AssignedBuildingIds.Count, town.BorderTiles.Count);
            return Results.Ok(result);
        });

        app.MapPost("/api/v1/owner/production/start", (
            OwnerSignedHttpRequest<OwnerProductionStartAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (!isPrivateWorld)
            {
                return Results.Conflict(new OwnerControlFailure(
                    "private_world_required",
                    "Recipe production is available only in the integrated private world."));
            }

            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["A production-start action is required."],
                });
            }

            string payload;
            try
            {
                payload = OwnerContentBinding.ProductionStartPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/production/start", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            var runtime = services.GetRequiredService<PrivateWorldRuntime>();
            var stateFile = services.GetRequiredService<PrivateWorldStateFile>();
            var result = runtime.StartProduction(
                request.Action.RecipeId,
                request.Action.BuildingInstanceId,
                request.Action.WorkerId);
            if (!result.Applied)
            {
                return Results.Conflict(new OwnerControlFailure("production_rejected", result.Failure ?? "Recipe production was rejected."));
            }

            stateFile.Save(runtime);
            return Results.Ok(result);
        });

        app.MapPost("/api/v1/owner/authoring", (
            OwnerSignedHttpRequest<OwnerAuthoringBatchAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (request?.Action is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["An authoring batch is required."],
                });
            }

            if (isPrivateWorld)
            {
                return Results.Conflict(new OwnerControlFailure(
                    "private_world_authoring_pending",
                    "Private-world authoring is not migrated yet; content activation remains disabled in the alpha runtime."));
            }

            string payload;
            try
            {
                payload = OwnerHttpBinding.AuthoringPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/authoring", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            if (!OwnerAuthoringMapper.TryMap(request.Action, out var batch, out var failure))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [failure],
                });
            }

            var runtime = services.GetRequiredService<OwnerWorldRuntime>();
            var stateFile = services.GetRequiredService<OwnerWorldStateFile>();
            var receipt = runtime.ApplyAuthoringBatch(batch! with
            {
                IssuerId = $"owner-device:{authorization.Value!.DeviceId}",
            });
            if (receipt.Applied)
            {
                stateFile.Save(runtime);
            }

            return Results.Ok(receipt);
        });
    }
}
