namespace ClankerWorld.Viewer.Control;

/// <summary>Log lines for a host the game started on the player's own PC.</summary>
public static partial class CompanionHostTelemetry
{
    [LoggerMessage(EventId = 2321, Level = LogLevel.Information,
        Message = "companion_host outcome=shutdown_requested")]
    public static partial void ShutdownRequested(ILogger logger);
}
