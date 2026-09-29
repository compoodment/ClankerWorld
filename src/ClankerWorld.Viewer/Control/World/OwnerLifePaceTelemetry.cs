namespace ClankerWorld.Viewer.Control;

public static partial class OwnerLifePaceTelemetry
{
    [LoggerMessage(EventId = 2211, Level = LogLevel.Information,
        Message = "world_life_pace tick={WorldTick} rate={Rate}")]
    public static partial void Changed(ILogger logger, long worldTick, int rate);
}
