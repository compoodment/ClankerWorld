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

internal enum TownCivicTransitionKind
{
    CouncilChanged,
    ElectionOpened,
    RunoffOpened,
    ProposalOpened,
    DecisionRecorded,
    ElectionCancelled,
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

    public static void Civic(ILogger logger, long worldTick, string townId, TownCivicTransitionKind transition,
        bool representative, int members, string status, int yes, int no, int ballots) =>
        LogTownCivic(logger, worldTick, townId, transition, representative, members, status, yes, no, ballots);

    [LoggerMessage(EventId = 2290, Level = LogLevel.Information,
        Message = "town_civic tick={WorldTick} town={TownId} transition={Transition} representative={Representative} members={Members} status={Status} yes={Yes} no={No} ballots={Ballots}")]
    private static partial void LogTownCivic(ILogger logger, long worldTick, string townId, TownCivicTransitionKind transition,
        bool representative, int members, string status, int yes, int no, int ballots);

    /// <summary>A passed admission's outcome. IDs and counts only; never names or proposal text.</summary>
    public static void Admission(ILogger logger, long worldTick, string townId, string outcome, string previousTownId,
        int members, int residents) =>
        LogTownAdmission(logger, worldTick, townId, outcome, previousTownId, members, residents);

    [LoggerMessage(EventId = 2295, Level = LogLevel.Information,
        Message = "town_admission tick={WorldTick} town={TownId} outcome={Outcome} previous_town={PreviousTownId} members={Members} residents={Residents}")]
    private static partial void LogTownAdmission(ILogger logger, long worldTick, string townId, string outcome,
        string previousTownId, int members, int residents);

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

    public static void Bridge(ILogger logger, long worldTick, string bridgeId, string trigger,
        string outcome, string reason) =>
        LogBridge(logger, worldTick, bridgeId, trigger, outcome, reason);

    [LoggerMessage(EventId = 2272, Level = LogLevel.Information,
        Message = "bridge outcome={Outcome} world_tick={WorldTick} bridge={BridgeId} trigger={Trigger} reason={Reason}")]
    private static partial void LogBridge(ILogger logger, long worldTick, string bridgeId, string trigger,
        string outcome, string reason);

    public static void RoadUnconnected(ILogger logger, long worldTick, string townId, string buildingId, string reason) =>
        LogRoadUnconnected(logger, worldTick, townId, buildingId, reason);

    [LoggerMessage(EventId = 2273, Level = LogLevel.Information,
        Message = "road_route outcome=unconnected world_tick={WorldTick} town={TownId} building={BuildingId} reason={Reason}")]
    private static partial void LogRoadUnconnected(ILogger logger, long worldTick, string townId, string buildingId,
        string reason);

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
