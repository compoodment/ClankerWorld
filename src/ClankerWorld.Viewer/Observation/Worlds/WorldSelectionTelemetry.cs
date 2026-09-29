namespace ClankerWorld.Viewer.Observation;

public static partial class WorldSelectionTelemetry
{
    [LoggerMessage(EventId = 2260, Level = LogLevel.Information,
        Message = "world_selection outcome=created catalog_id={CatalogId} size={Size}")]
    public static partial void Created(ILogger logger, string catalogId, string size);

    [LoggerMessage(EventId = 2261, Level = LogLevel.Information,
        Message = "world_selection outcome=selected catalog_id={CatalogId}")]
    public static partial void Selected(ILogger logger, string catalogId);

    [LoggerMessage(EventId = 2262, Level = LogLevel.Warning,
        Message = "world_selection outcome=failed catalog_id={CatalogId} reason={Reason}")]
    public static partial void Failed(ILogger logger, string catalogId, string reason);

    [LoggerMessage(EventId = 2263, Level = LogLevel.Information,
        Message = "world_preview outcome=generated width={Width} height={Height}")]
    public static partial void Previewed(ILogger logger, int width, int height);
}
