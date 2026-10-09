using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed record BoatTripRequest(string Id, long Sequence, string PassengerId, string BoatTownId,
    string OriginPortId, string DestinationPortId, long RequestedTick, string Status = "waiting",
    string? BoatId = null, long? SettledTick = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OrderInstructionId { get; init; }
}

public sealed record BoatJourney(string RequestId, string PassengerId, string OriginPortId,
    string DestinationPortId, GridPoint ReservedDock, IReadOnlyList<GridPoint> WaterPath,
    int PathIndex, long StartedTick, long NextMoveTick, long? WaitingSinceTick = null,
    bool Returning = false);

/// <summary>One physical communal asset produced by one paid Council-approved project.</summary>
public sealed record BoatState(string Id, string TownId, string ProjectId, GridPoint Position,
    string? DockedPortId = null, BoatJourney? Journey = null,
    IReadOnlyList<string>? GroundCargoLotIds = null);

public sealed record RetiredBoatRequestRange(long FirstSequence, long LastSequence);

public sealed record BoatTransportState(long Sequence, IReadOnlyList<BoatState> Boats,
    IReadOnlyList<BoatTripRequest> Requests)
{
    [JsonRequired]
    public IReadOnlyList<RetiredBoatRequestRange> RetiredRequestRanges { get; init; } = [];

    public static BoatTransportState Empty() => new(0, [], []);
}
