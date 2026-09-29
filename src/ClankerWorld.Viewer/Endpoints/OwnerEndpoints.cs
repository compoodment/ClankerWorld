using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Viewer.Endpoints;

internal static partial class OwnerEndpoints
{
    public static void MapOwnerEndpoints(this WebApplication app, bool isPrivateWorld)
    {
        MapPairing(app);
        MapObservation(app);
        MapRuntimeControl(app, isPrivateWorld);
        MapWorlds(app, isPrivateWorld);
        MapSaves(app, isPrivateWorld);
        MapSetup(app, isPrivateWorld);
        MapInstructions(app, isPrivateWorld);
        MapContent(app, isPrivateWorld);
        MapDevices(app);
        MapProviders(app);

        app.MapGet("/api/v1/world", OwnerFailures.UnpairedObservation);
        app.MapGet("/api/v1/events", OwnerFailures.UnpairedObservation);
        app.MapGet("/api/v1/reconnect", OwnerFailures.UnpairedObservation);
    }

    private static bool IsControl(OwnerSignedHttpRequest<OwnerControlAction>? request, string expectedOperation) =>
        request?.Action is not null &&
        string.Equals(request.Action.Operation, expectedOperation, StringComparison.Ordinal);

    private static bool TryWorldOptions(OwnerWorldCreationAction action, out GeographyOptions? options)
    {
        options = null;
        if (action.Name is null || action.Name.Length is < 1 or > 80 || action.Name.Any(char.IsControl) ||
            action.Seed is null || action.Seed.Length is < 1 or > 100 || action.Seed.Any(char.IsControl) ||
            !Enum.TryParse<WorldSizePreset>(action.Size, true, out var size) ||
            !Enum.IsDefined(size) || size is not (WorldSizePreset.Small or WorldSizePreset.Medium) ||
            !Enum.TryParse<ClimateMode>(action.ClimateMode, true, out var climateMode) ||
            !Enum.IsDefined(climateMode) ||
            !Enum.TryParse<ClimateZone>(action.SelectedClimate, true, out var selectedClimate) ||
            !Enum.IsDefined(selectedClimate) ||
            !Enum.TryParse<ResourceAbundance>(action.ResourceAbundance, true, out var abundance) ||
            !Enum.IsDefined(abundance) ||
            action.WaterPercent is < 10 or > 80)
            return false;
        options = new GeographyOptions(action.Seed, size, action.WrapEastWest, action.WaterPercent,
            climateMode, selectedClimate, action.LatitudeCooling, abundance);
        return true;
    }

    private static bool TryParseInstructionKind(string? value, out OwnerInstructionKind kind)
    {
        kind = value?.Trim().ToLowerInvariant() switch
        {
            "suggestive" => OwnerInstructionKind.Suggestive,
            "must_do" => OwnerInstructionKind.MustDo,
            _ => default,
        };
        return value is not null && (value.Trim().Equals("suggestive", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("must_do", StringComparison.OrdinalIgnoreCase));
    }
}
