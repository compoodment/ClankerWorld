using Microsoft.Extensions.Logging;

namespace ClankerWorld.Viewer.Control;

internal static partial class AgentPlacementLog
{
    [LoggerMessage(EventId = 1710, Level = LogLevel.Information,
        Message = "AgentPlaced AgentId={AgentId} HouseholdId={HouseholdId} WorldTick={WorldTick} X={X} Y={Y}")]
    public static partial void Placed(ILogger logger, string agentId, string? householdId, long worldTick, int x, int y);

    [LoggerMessage(EventId = 1711, Level = LogLevel.Warning,
        Message = "AgentPlacementRejected AgentId={AgentId} Reason={Reason}")]
    public static partial void Rejected(ILogger logger, string agentId, string reason);

    [LoggerMessage(EventId = 1712, Level = LogLevel.Information,
        Message = "AgentRenamed AgentId={AgentId} WorldTick={WorldTick}")]
    public static partial void Renamed(ILogger logger, string agentId, long worldTick);
}
