using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    private static void MapDeveloperEdits(WebApplication app, bool isPrivateWorld)
    {
        app.MapPost("/api/v1/owner/developer-edit", (
            OwnerSignedHttpRequest<OwnerDeveloperEditAction> request,
            OwnerRequestAuthorizer authorizer, IServiceProvider services) =>
        {
            if (request?.Action is not { } action || action.ExpectedEventId < 0 ||
                new[] { action.WorldId, action.AgentId, action.Operation, action.Value }
                    .Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl)) ||
                action.OtherAgentId is { } other && (other.Length > 512 || other.Any(char.IsControl)))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = ["Choose an agent and a valid developer edit."] });

            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/developer-edit",
                OwnerHttpBinding.DeveloperEditPayload(action));
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Developer edits require a private world." });
            lock (services.GetRequiredService<ProviderConfigurationStore>().WorldMutationGate)
            {
                var runtime = services.GetRequiredService<PrivateWorldRuntime>();
                var result = services.GetRequiredService<PrivateWorldStateFile>().ApplyDeveloperEdit(runtime,
                    new(action.WorldId, action.ExpectedEventId, action.AgentId, action.Operation, action.Value, action.Amount, action.OtherAgentId));
                if (result.Failure is { } failure) return Results.Conflict(new { error = failure });
                return Results.Ok(OwnerControlReceipt.From("developer_edit", result.Applied, runtime.ExportState()));
            }
        });
    }
}
