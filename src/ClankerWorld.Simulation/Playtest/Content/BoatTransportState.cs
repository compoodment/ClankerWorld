using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed record BoatGuestAccess(string TownId, string VisitorId);

public sealed record BoatJourney(
    string PassengerId,
    string OriginPortId,
    string DestinationPortId,
    IReadOnlyList<GridPoint> WaterPath,
    int PathIndex,
    long StartedTick,
    long NextMoveTick,
    long? WaitingSinceTick = null,
    bool Returning = false);

/// <summary>A physical communal Town asset, tied to one completed Port build job.</summary>
public sealed record BoatState(
    string Id,
    string TownId,
    string BuildJobId,
    GridPoint Position,
    string? DockedPortId = null,
    BoatJourney? Journey = null,
    IReadOnlyList<string>? EstateCargoLotIds = null);

public sealed record BoatTransportState(
    int SchemaVersion,
    IReadOnlyList<BoatState> Boats,
    IReadOnlyList<BoatGuestAccess> GuestPermissions)
{
    public static BoatTransportState Empty() => new(1, [], []);
}

public sealed record BoatJourneyStartResult(bool Applied, string? Failure = null);
