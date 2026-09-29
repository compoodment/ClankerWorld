using Microsoft.Extensions.Logging;

namespace ClankerWorld.Viewer.Control;

internal enum TownTransitionKind
{
    StateLoaded,
    FoundingStarted,
    ResidentJoined,
    ResidentLeft,
    ResidentUnaffiliated,
    Founded,
    BuildingAssigned,
    BuildingUnassigned,
    BorderExpanded,
}

/// <summary>Bounded operational outcomes for authoritative Town state changes.</summary>
internal static partial class TownTelemetry
{
    public static void Transition(ILogger logger, long worldTick, string townId,
        TownTransitionKind transition, int residentCount, int buildingCount, int borderTileCount)
    {
        LogTownTransition(logger, worldTick, townId, transition, residentCount, buildingCount, borderTileCount);
    }

    [LoggerMessage(EventId = 2265, Level = LogLevel.Information,
        Message = "town_transition tick={WorldTick} town={TownId} transition={Transition} residents={ResidentCount} buildings={BuildingCount} border_tiles={BorderTileCount}")]
    private static partial void LogTownTransition(ILogger logger, long worldTick, string townId, TownTransitionKind transition,
        int residentCount, int buildingCount, int borderTileCount);

    public static void SiteRejected(ILogger logger, long worldTick, string townId, string inhabitantId,
        string buildingId, int x, int y, string reason)
    {
        LogTownSiteRejected(logger, worldTick, townId, inhabitantId, buildingId, x, y, reason);
    }

    [LoggerMessage(EventId = 2266, Level = LogLevel.Information,
        Message = "town_site_rejected tick={WorldTick} town={TownId} inhabitant={InhabitantId} building={BuildingId} x={X} y={Y} reason={Reason}")]
    private static partial void LogTownSiteRejected(ILogger logger, long worldTick, string townId, string inhabitantId,
        string buildingId, int x, int y, string reason);

    public static void LayoutAccepted(ILogger logger, long worldTick, string outcome, int x, int y,
        int buildings, int roadTiles) =>
        LogTownLayoutAccepted(logger, worldTick, outcome, x, y, buildings, roadTiles);

    [LoggerMessage(EventId = 2267, Level = LogLevel.Information,
        Message = "first_town_layout outcome={Outcome} world_tick={WorldTick} x={X} y={Y} buildings={Buildings} roads={RoadTiles}")]
    private static partial void LogTownLayoutAccepted(ILogger logger, long worldTick, string outcome, int x, int y,
        int buildings, int roadTiles);

    public static void FounderMoved(ILogger logger, long worldTick, string founderId, int x, int y) =>
        LogFounderMoved(logger, worldTick, founderId, x, y);

    [LoggerMessage(EventId = 2268, Level = LogLevel.Information,
        Message = "founder_setup outcome=moved world_tick={WorldTick} founder={FounderId} x={X} y={Y}")]
    private static partial void LogFounderMoved(ILogger logger, long worldTick, string founderId, int x, int y);

    public static void FounderUndone(ILogger logger, long worldTick, string founderId, int placed) =>
        LogFounderUndone(logger, worldTick, founderId, placed);

    [LoggerMessage(EventId = 2269, Level = LogLevel.Information,
        Message = "founder_setup outcome=undone world_tick={WorldTick} founder={FounderId} placed={Placed}")]
    private static partial void LogFounderUndone(ILogger logger, long worldTick, string founderId, int placed);
}
