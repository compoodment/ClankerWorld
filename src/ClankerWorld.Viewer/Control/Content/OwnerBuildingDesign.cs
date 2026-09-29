using System.Globalization;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Control;

public sealed record OwnerBuildingDesignAction(string Name, string Purpose, int WoodCost);

public sealed record OwnerBuildingDesignPreview(OwnerBuildingDesignAction Design, OwnerContentPackageAction Package, string ManifestDigest,
    string Summary, bool ConstructionPassed, int WoodConsumed);

public static partial class OwnerBuildingDesign
{
    public static string Payload(OwnerBuildingDesignAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(action.Name);
        ArgumentNullException.ThrowIfNull(action.Purpose);
        return string.Join('\n', "clankerworld.owner-building-design.v1",
            "name=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(action.Name)),
            "purpose=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(action.Purpose)),
            "wood-cost=" + action.WoodCost.ToString(CultureInfo.InvariantCulture));
    }

    public static void MapBuildingDesign(this WebApplication app, bool isPrivateWorld)
    {
        app.MapPost("/api/v1/owner/content/building-preview", async (
            OwnerSignedHttpRequest<OwnerBuildingDesignAction> request, OwnerRequestAuthorizer authorizer,
            ILogger<PrivateWorldRuntimeService> logger, CancellationToken cancellationToken) =>
        {
            string payload;
            try { payload = Payload(request.Action); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "A building design is required." }); }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/building-preview", payload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Building design requires a private world." });
            try
            {
                var preview = await PreviewAsync(request.Action, cancellationToken);
                LogPreview(logger, preview.Package.PackageDigest, preview.ConstructionPassed);
                return Results.Ok(preview);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPost("/api/v1/owner/content/building-review", async (
            OwnerSignedHttpRequest<OwnerContentPackageIdAction> request, OwnerRequestAuthorizer authorizer,
            IServiceProvider services, ILogger<PrivateWorldRuntimeService> logger, CancellationToken cancellationToken) =>
        {
            if (request.Action is null) return Results.BadRequest(new { error = "A package is required." });
            string payload;
            try { payload = OwnerContentBinding.PackageIdPayload("building-review", request.Action); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "A package ID is required." }); }
            var authorization = authorizer.Authorize(request, "POST", "/api/v1/owner/content/building-review", payload);
            if (!authorization.IsSuccess) return OwnerFailures.ToHttpResult(authorization.Failure);
            if (!isPrivateWorld) return Results.Conflict(new { error = "Building design requires a private world." });
            var record = services.GetRequiredService<PrivateWorldRuntime>().ExportState().Content?.Packages
                .SingleOrDefault(package => package.Manifest.PackageId == request.Action.PackageId);
            if (record is null) return Results.NotFound(new { error = "The design was not found." });
            try
            {
                var design = BuildingDesign.Read(record.Manifest);
                var preview = await PreviewAsync(new(design.Name, design.Purpose, design.WoodCost), cancellationToken);
                LogPreview(logger, preview.Package.PackageDigest, preview.ConstructionPassed);
                return Results.Ok(preview);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException or FormatException)
            {
                LogPreview(logger, record.Manifest.PackageDigest, false);
                return Results.BadRequest(new { error = "The saved package is not a valid workbench building design." });
            }
        });
    }

    public static async Task<OwnerBuildingDesignPreview> PreviewAsync(OwnerBuildingDesignAction action, CancellationToken cancellationToken)
    {
        var manifest = BuildingDesign.Create(action.Name, action.Purpose, action.WoodCost);
        var preview = PrivateWorldRuntime.PreviewWorldContent([manifest], [manifest.PackageId]);
        if (!preview.IsValid) throw new InvalidOperationException("The generated design failed typed content validation.");
        // Independent fixture: no live save, provider registry, credentials or file writes.
        using var testWorld = new PrivateWorldRuntime("building-design-preview", _ => new PreviewIdleProvider());
        testWorld.ProposeContent(manifest);
        testWorld.ValidateContent(manifest.PackageId, preview.Resolution);
        testWorld.ApproveContent(manifest.PackageId);
        testWorld.StageContent(manifest.PackageId);
        await testWorld.AdvanceOneTickAsync(cancellationToken);
        var state = testWorld.ExportState();
        var building = preview.WorldContent.Buildings.Single();
        var worker = state.Inhabitants.First(person => state.Map.IsPassable(person.Position) &&
            !state.Map.Resources.Any(resource => resource.Position == person.Position) &&
            !state.Map.CampObjects.Any(item => item.Position == person.Position));
        var woodBefore = testWorld.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var placement = testWorld.PlaceBuilding("preview-building", building.CanonicalId, worker.Position);
        var woodAfter = testWorld.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        testWorld.Validate();
        var package = new OwnerContentPackageAction(manifest.PackageId, manifest.Version.ToString(), manifest.PackageDigest,
            [], manifest.Definitions.Select(definition => new OwnerContentDefinitionAction(definition.Kind, definition.LocalId,
                definition.Version.ToString(), definition.DisplayName, definition.PayloadDigest, definition.PayloadJson)).ToArray(), []);
        return new(action, package, ContentPackageManifestCodec.ComputeManifestDigest(manifest),
            BuildingDesign.Describe(action.Purpose) + " Construction tested in a separate fixture; your world still needs materials, space and labour.",
            placement.Applied && woodBefore - woodAfter == action.WoodCost, woodBefore - woodAfter);
    }

    [LoggerMessage(EventId = 2214, Level = LogLevel.Information,
        Message = "content_building_preview digest={PackageDigest} construction_passed={Passed}")]
    private static partial void LogPreview(ILogger logger, string packageDigest, bool passed);

    private sealed class PreviewIdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == "safe_idle" ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}
