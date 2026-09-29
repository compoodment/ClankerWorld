namespace ClankerWorld.Viewer.Control;

public static partial class OwnerJevAssistanceTelemetry
{
    [LoggerMessage(EventId = 2219, Level = LogLevel.Information,
        Message = "world_jev_assistance tick={WorldTick} enabled={Enabled}")]
    public static partial void Changed(ILogger logger, long worldTick, bool enabled);
}
