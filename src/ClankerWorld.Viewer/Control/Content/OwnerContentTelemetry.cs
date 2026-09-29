namespace ClankerWorld.Viewer.Control;

public static partial class OwnerContentTelemetry
{
    [LoggerMessage(EventId = 2213, Level = LogLevel.Information,
        Message = "content_rollback tick={WorldTick} package={PackageId} outcome={Outcome}")]
    public static partial void Rollback(ILogger logger, long worldTick, string packageId, string outcome);
}
