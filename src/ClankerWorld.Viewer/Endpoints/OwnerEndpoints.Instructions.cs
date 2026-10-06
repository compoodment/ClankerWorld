using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapInstructions(WebApplication app, bool isPrivateWorld)
    {
        app.MapPost("/api/v1/owner/instructions", (
            OwnerSignedHttpRequest<OwnerInstructionAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (request?.Action is null || !TryParseInstructionKind(request.Action.Kind, out var kind))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action.kind"] = ["Instruction kind must be suggestive or must_do."],
                });
            }

            string payload;
            try
            {
                payload = OwnerHttpBinding.InstructionPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/instructions", payload);
            if (!authorization.IsSuccess)
            {
                return OwnerFailures.ToHttpResult(authorization.Failure);
            }

            if (isPrivateWorld)
            {
                var privateRuntime = services.GetRequiredService<PrivateWorldRuntime>();
                var privateStateFile = services.GetRequiredService<PrivateWorldStateFile>();
                try
                {
                    // Selection and load use this same gate. Keep the world check,
                    // mutation and durable save together so selection cannot retarget a retry.
                    lock (services.GetRequiredService<ProviderConfigurationStore>().WorldMutationGate)
                    {
                        if (request.Action.WorldId != privateRuntime.Society.WorldId)
                            return Results.Conflict(new OwnerControlFailure("world_mismatch",
                                "Return to the world where this instruction was sent before retrying."));
                        var receipt = privateRuntime.SubmitInstruction(new OwnerInstructionRequest(
                            request.Action.IdempotencyKey,
                            $"owner-device:{authorization.Value!.DeviceId}",
                            request.Action.TargetInhabitantId,
                            kind,
                        request.Action.Text,
                        request.Action.Queue));
                        privateStateFile.Save(privateRuntime);
                        return Results.Ok(receipt);
                    }
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
                    return Results.Conflict(new OwnerControlFailure("idempotency_conflict", exception.Message));
                }
            }

            try
            {
                var runtime = services.GetRequiredService<OwnerWorldRuntime>();
                var stateFile = services.GetRequiredService<OwnerWorldStateFile>();
                if (request.Action.WorldId != runtime.Capture().Snapshot.World.Identity.WorldId)
                    return Results.Conflict(new OwnerControlFailure("world_mismatch",
                        "This instruction belongs to another world."));
                // The transport has no issuer field. This value is minted from the
                // authenticated server-side device identity rather than accepted from
                // a client payload.
                var receipt = runtime.SubmitInstruction(new OwnerInstructionRequest(
                    request.Action.IdempotencyKey,
                    $"owner-device:{authorization.Value!.DeviceId}",
                    request.Action.TargetInhabitantId,
                    kind,
                    request.Action.Text,
                    request.Action.Queue));
                stateFile.Save(runtime);
                return Results.Ok(receipt);
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
                return Results.Conflict(new OwnerControlFailure("idempotency_conflict", exception.Message));
            }
        });

        app.MapPost("/api/v1/owner/orders/cancel", (
            OwnerSignedHttpRequest<OwnerOrderCancelAction> request,
            OwnerRequestAuthorizer authorizer,
            IServiceProvider services) =>
        {
            if (!isPrivateWorld)
                return Results.NotFound();
            if (request?.Action is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = ["An order cancellation is required."],
                });

            string payload;
            try
            {
                payload = OwnerHttpBinding.OrderCancelPayload(request.Action);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["action"] = [exception.Message],
                });
            }

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/orders/cancel", payload);
            if (!authorization.IsSuccess)
                return OwnerFailures.ToHttpResult(authorization.Failure);

            var privateRuntime = services.GetRequiredService<PrivateWorldRuntime>();
            var privateStateFile = services.GetRequiredService<PrivateWorldStateFile>();
            try
            {
                lock (services.GetRequiredService<ProviderConfigurationStore>().WorldMutationGate)
                {
                    if (request.Action.WorldId != privateRuntime.Society.WorldId)
                        return Results.Conflict(new OwnerControlFailure("world_mismatch",
                            "Return to the world where this order was sent before cancelling it."));
                    var receipt = privateRuntime.CancelOrder(new OwnerOrderCancelRequest(
                        request.Action.IdempotencyKey,
                        $"owner-device:{authorization.Value!.DeviceId}",
                        request.Action.WorldId,
                        request.Action.TargetInhabitantId,
                        request.Action.OrderId));
                    privateStateFile.Save(privateRuntime);
                    return Results.Ok(receipt);
                }
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
                return Results.Conflict(new OwnerControlFailure("idempotency_conflict", exception.Message));
            }
        });
    }
}
