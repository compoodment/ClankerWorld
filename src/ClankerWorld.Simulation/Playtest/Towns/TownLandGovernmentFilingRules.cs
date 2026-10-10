using System.Globalization;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Exact Council authorization to file a public case on the Town's behalf.</summary>
public static class TownLandGovernmentFilingRules
{
    public static string Describe(TownLandFilingRequest request) =>
        "Exact hearing plot: " + TownLandClaimRules.DescribeTiles(request.Tiles) + ". " +
        "Disagreement: " + request.Statement + " Requested result: " + request.RequestedOutcome.Kind +
        (request.RequestedOutcome.HouseholdId is { } household ? " for household " + household : "") +
        (request.RequestedOutcome.Kind is "renew" or "amend"
            ? request.RequestedOutcome.AgreedEndTick is { } end ? "; agreed end tick " + end.ToString(CultureInfo.InvariantCulture) : "; no agreed end date"
            : "") + (request.Property is { } property ? "; building " + property.BuildingId + " and its recorded shared goods" : "") + ".";

    public static string RequestKey(TownLandFilingRequest request) => "land_hearing:" +
        TownLandHearingRules.Digest(TownLandClaimRules.DescribeTiles(request.Tiles) + "|" +
            request.RequestedOutcome.Kind + "|" + request.RequestedOutcome.HouseholdId + "|" +
            request.RequestedOutcome.AgreedEndTick?.ToString(CultureInfo.InvariantCulture) +
            (request.Property is null ? "" : "|" + TownLandHearingRules.RecordVersion(request.Property)));

    public static bool IsValid(TownLandFilingRequest? request, long tick) =>
        request is not null && request.Tiles is { Count: > 0 and <= 64 } &&
        request.Tiles.Distinct().Count() == request.Tiles.Count &&
        request.Tiles.SequenceEqual(TownLandRightsRules.OrderTiles(request.Tiles)) &&
        TownLandHearingRules.ValidText(request.Statement, 256) &&
        TownLandHearingRules.IsValidOutcome(request.RequestedOutcome, tick) &&
        (request.Property is null ? request.RequestedOutcome.Kind is not ("reclaim" or "grant") :
            TownPropertyRules.IsValid(request.Property) && request.RequestedOutcome == TownPropertyRules.Outcome(request.Property));

    public static void Validate(TownProposal proposal, TownRuntimeState town, SeededMap map,
        IReadOnlyList<TownLandTitleRecord> titles, IReadOnlySet<string> households)
    {
        var request = proposal.LandHearingRequest;
        if (!IsValid(request, proposal.OpenedTick) || proposal.SubjectId is not null ||
            proposal.RequestKey != RequestKey(request!) ||
            !TownLandRightsRules.IsValidPlot(map, request!.Tiles, proposal.OpenedTick, proposal.OpenedTick) ||
            request.Tiles.Any(tile => !TownLandRightsRules.IsCoveredByTownTitle(tile, town.Id, titles)) ||
            request.RequestedOutcome.HouseholdId is { } household && !households.Contains(household))
            throw new InvalidDataException("A saved Town hearing proposal must retain its exact plot, request and lawful scope.");
    }
}
