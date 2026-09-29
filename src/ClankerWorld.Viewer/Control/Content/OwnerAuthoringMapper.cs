using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Viewer.Control;

/// <summary>
/// Converts the deliberately scalar wire representation into the simulation's
/// typed paused-authoring operations. This is validation/translation only: the
/// runtime still performs its own all-or-nothing state validation.
/// </summary>
public static class OwnerAuthoringMapper
{
    public static bool TryMap(
        OwnerAuthoringBatchAction? action,
        out OwnerAuthoringBatch? batch,
        out string failure)
    {
        batch = null;
        failure = string.Empty;
        if (action is null || string.IsNullOrWhiteSpace(action.BatchId) || action.Operations is null || action.Operations.Count == 0)
        {
            failure = "An authoring batch ID and at least one operation are required.";
            return false;
        }

        var operations = new List<OwnerAuthoringOperation>(action.Operations.Count);
        try
        {
            for (var index = 0; index < action.Operations.Count; index++)
            {
                if (!TryMapOperation(action.Operations[index], out var operation, out failure))
                {
                    failure = $"Operation {index}: {failure}";
                    return false;
                }

                operations.Add(operation!);
            }
        }
        catch (ArgumentException exception)
        {
            failure = exception.Message;
            return false;
        }

        batch = new OwnerAuthoringBatch(action.BatchId.Trim(), operations);
        return true;
    }

    private static bool TryMapOperation(
        OwnerAuthoringOperationAction? source,
        out OwnerAuthoringOperation? operation,
        out string failure)
    {
        operation = null;
        failure = string.Empty;
        if (source is null)
        {
            failure = "An authoring operation is required.";
            return false;
        }

        var kind = source.Kind?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(kind))
        {
            failure = "An operation kind is required.";
            return false;
        }

        switch (kind)
        {
            case "set_terrain":
                if (!TryPosition(source, out var terrainPosition, out failure) ||
                    !TryTerrain(source.Value, out var terrain, out failure))
                {
                    return false;
                }

                operation = new SetTerrainOperation(terrainPosition, terrain);
                return true;

            case "place_resource":
                if (!TryPosition(source, out var resourcePosition, out failure) || source.IsRenewable is null)
                {
                    failure = source.IsRenewable is null ? "A resource requires isRenewable." : failure;
                    return false;
                }

                operation = new PlaceResourceOperation(Required(source.Id), Required(source.Value), resourcePosition, source.IsRenewable.Value);
                return true;

            case "remove_resource":
                operation = new RemoveResourceOperation(Required(source.Id));
                return true;

            case "place_object":
                if (!TryPosition(source, out var objectPosition, out failure))
                {
                    return false;
                }

                operation = new PlaceObjectOperation(Required(source.Id), Required(source.Value), objectPosition);
                return true;

            case "remove_object":
                operation = new RemoveObjectOperation(Required(source.Id));
                return true;

            case "place_building":
                if (!TryPosition(source, out var buildingPosition, out failure))
                {
                    return false;
                }

                operation = new PlaceBuildingOperation(Required(source.Id), Required(source.Value), buildingPosition);
                return true;

            case "remove_building":
                operation = new RemoveBuildingOperation(Required(source.Id));
                return true;

            case "place_plant":
                if (!TryPosition(source, out var plantPosition, out failure))
                {
                    return false;
                }

                operation = new PlacePlantOperation(Required(source.Id), Required(source.Value), plantPosition);
                return true;

            case "remove_plant":
                operation = new RemovePlantOperation(Required(source.Id));
                return true;

            case "create_founder_draft":
                if (!TryPosition(source, out var founderPosition, out failure))
                {
                    return false;
                }

                operation = new CreateFounderDraftOperation(Required(source.Id), Required(source.Value), founderPosition);
                return true;

            case "remove_founder_draft":
                operation = new RemoveFounderDraftOperation(Required(source.Id));
                return true;

            case "set_weather":
                operation = new SetWeatherOperation(Required(source.Value));
                return true;

            case "set_season":
                operation = new SetSeasonOperation(Required(source.Value));
                return true;

            case "set_weather_season":
                operation = new SetWeatherSeasonOperation(Required(source.Value), Required(source.SecondaryValue));
                return true;

            case "add_approved_asset_reference":
                operation = new AddApprovedAssetReferenceOperation(Required(source.Id), Required(source.Value));
                return true;

            case "remove_approved_asset_reference":
                operation = new RemoveApprovedAssetReferenceOperation(Required(source.Id));
                return true;

            default:
                failure = $"Unsupported authoring operation '{source.Kind}'.";
                return false;
        }
    }

    private static bool TryPosition(OwnerAuthoringOperationAction source, out GridPoint position, out string failure)
    {
        position = default;
        if (source.X is null || source.Y is null)
        {
            failure = "A map operation requires x and y coordinates.";
            return false;
        }

        position = new GridPoint(source.X.Value, source.Y.Value);
        failure = string.Empty;
        return true;
    }

    private static bool TryTerrain(string? value, out TerrainKind terrain, out string failure)
    {
        terrain = default;
        failure = string.Empty;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "meadow":
                terrain = TerrainKind.Meadow;
                return true;
            case "water":
                terrain = TerrainKind.Water;
                return true;
            case "mountain":
                terrain = TerrainKind.Mountain;
                return true;
            default:
                failure = "Terrain must be meadow, water, or mountain.";
                return false;
        }
    }

    private static string Required(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required authoring operation value is missing.", nameof(value));
        }

        return value.Trim();
    }
}
