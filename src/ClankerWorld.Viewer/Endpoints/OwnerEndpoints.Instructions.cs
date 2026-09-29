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
                    var receipt = privateRuntime.SubmitInstruction(new OwnerInstructionRequest(
                        request.Action.IdempotencyKey,
                        $"owner-device:{authorization.Value!.DeviceId}",
                        request.Action.TargetInhabitantId,
                        kind,
                        request.Action.Text));
                    privateStateFile.Save(privateRuntime);
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
            }

            try
            {
                var runtime = services.GetRequiredService<OwnerWorldRuntime>();
                var stateFile = services.GetRequiredService<OwnerWorldStateFile>();
                // The transport has no issuer field. This value is minted from the
                // authenticated server-side device identity rather than accepted from
                // a client payload.
                var receipt = runtime.SubmitInstruction(new OwnerInstructionRequest(
                    request.Action.IdempotencyKey,
                    $"owner-device:{authorization.Value!.DeviceId}",
                    request.Action.TargetInhabitantId,
                    kind,
                    request.Action.Text));
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
    }
}
