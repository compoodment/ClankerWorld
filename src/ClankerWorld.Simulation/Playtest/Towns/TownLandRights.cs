using System.Globalization;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>A saved Town's formal title to one connected group of land tiles.</summary>
public sealed record TownLandTitleRecord(
    string Id,
    string TownId,
    IReadOnlyList<GridPoint> Tiles,
    long RecordedTick);

/// <summary>A household's recorded permission to use one connected group of tiles.</summary>
public sealed record HouseholdLandUseRight(
    string Id,
    string TownId,
    string HouseholdId,
    IReadOnlyList<GridPoint> Tiles,
    long GrantedTick,
    string GrantSource,
    long? AgreedEndTick = null);

/// <summary>
/// A structured request for a household to use Town-titled land. Filing a
/// request does not grant a right or decide a disagreement.
/// </summary>
public sealed record HouseholdLandUseRequest(
    string Id,
    string TownId,
    string HouseholdId,
    string RequestedByAgentId,
    IReadOnlyList<GridPoint> Tiles,
    long RequestedTick,
    long? AgreedEndTick = null)
{
    public string Status { get; init; } = "pending";
    public long? SettledTick { get; init; }
    public string? CouncilProposalId { get; init; }
    public IReadOnlyList<HouseholdLandUseConsent> Consents { get; init; } = [];
    public IReadOnlyList<string> GrantAdults { get; init; } = [];
    [JsonRequired]
    public IReadOnlyList<TownLandRequestResolution> HearingResolutions { get; init; } = [];
}

public sealed record HouseholdLandUseConsent(string AgentId, bool Accepted, long Tick);

public sealed record HouseholdLandUseRequestResult(
    bool Applied,
    HouseholdLandUseRequest? Request,
    bool IsDisputed,
    bool IsDuplicate,
    string? Failure);

/// <summary>Canonical plot grouping and conflict checks for recorded land claims.</summary>
public static class TownLandRightsRules
{
    public const string StarterAllocationSource = "starter_allocation";

    public static IEnumerable<GridPoint> UnresolvedRequestTiles(HouseholdLandUseRequest request) =>
        request.Tiles.Where(tile => !request.HearingResolutions.Any(resolution => resolution.Tiles.Contains(tile)));

    public static GridPoint[] OrderTiles(IEnumerable<GridPoint> tiles) => tiles
        .OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();

    public static IReadOnlyList<GridPoint[]> ConnectedPlots(SeededMap map, IEnumerable<GridPoint> tiles)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tiles);
        var remaining = tiles.ToHashSet();
        if (remaining.Any(point => !map.IsLand(point)))
            throw new ArgumentException("A land plot must contain only land tiles on the map.", nameof(tiles));

        var plots = new List<GridPoint[]>();
        while (remaining.Count > 0)
        {
            var start = remaining.OrderBy(point => point.Y).ThenBy(point => point.X).First();
            var plot = new HashSet<GridPoint> { start };
            var pending = new Queue<GridPoint>();
            pending.Enqueue(start);
            remaining.Remove(start);
            while (pending.TryDequeue(out var current))
            {
                foreach (var next in CardinalNeighbors(map, current))
                {
                    if (remaining.Remove(next))
                    {
                        plot.Add(next);
                        pending.Enqueue(next);
                    }
                }
            }
            plots.Add(OrderTiles(plot));
        }

        return plots.OrderBy(plot => plot[0].Y).ThenBy(plot => plot[0].X).ToArray();
    }

    public static bool IsValidPlot(SeededMap map, IReadOnlyList<GridPoint>? tiles, long worldTick,
        long recordedTick, long? agreedEndTick = null)
    {
        if (tiles is null || tiles.Count == 0 || tiles.Any(point => !map.IsLand(point)) ||
            tiles.Distinct().Count() != tiles.Count ||
            !tiles.SequenceEqual(OrderTiles(tiles)) ||
            ConnectedPlots(map, tiles).Count != 1 || recordedTick < 0 || recordedTick > worldTick ||
            agreedEndTick is { } endTick && endTick < recordedTick)
            return false;
        return true;
    }

    public static IReadOnlyList<TownLandTitleRecord> InitialTitles(SeededMap map, TownRuntimeState town,
        long recordedTick)
    {
        ArgumentNullException.ThrowIfNull(town);
        return ConnectedPlots(map, town.BorderTiles).Select((plot, index) =>
            new TownLandTitleRecord(StableId("town-title", town.Id, index), town.Id, plot, recordedTick)).ToArray();
    }

    public static IReadOnlyList<HouseholdLandUseRight> InitialUseRights(SeededMap map, string townId,
        IEnumerable<(string HouseholdId, IEnumerable<GridPoint> Tiles)> householdPlots, long grantedTick)
    {
        ArgumentNullException.ThrowIfNull(householdPlots);
        var rights = new List<HouseholdLandUseRight>();
        foreach (var (householdId, tiles) in householdPlots
                     .OrderBy(item => item.HouseholdId, StringComparer.Ordinal))
        {
            var plots = ConnectedPlots(map, tiles);
            for (var index = 0; index < plots.Count; index++)
                rights.Add(new HouseholdLandUseRight(
                    StableId("household-use", townId + ":" + householdId, index),
                    townId,
                    householdId,
                    plots[index],
                    grantedTick,
                    StarterAllocationSource));
        }
        return rights.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    public static bool IsDisputed(GridPoint tile, IReadOnlyList<HouseholdLandUseRight> rights,
        IReadOnlyList<HouseholdLandUseRequest> requests) =>
        ClaimantsAt(tile, rights, requests).Count > 1;

    /// <summary>Partitions existing grants after an authorized building transfer; grants no new land.</summary>
    public static IReadOnlyList<HouseholdLandUseRight> ReassignFootprintRights(SeededMap map,
        IReadOnlyList<HouseholdLandUseRight> rights, IReadOnlySet<GridPoint> footprint,
        string targetHouseholdId, long worldTick)
    {
        var usedIds = rights.Select(right => right.Id).ToHashSet(StringComparer.Ordinal);
        var sequence = 0;
        string NextId()
        {
            string id;
            do
            {
                id = $"household-use:reassigned:{worldTick.ToString(CultureInfo.InvariantCulture)}:{(sequence++).ToString(CultureInfo.InvariantCulture)}";
            } while (!usedIds.Add(id));
            return id;
        }

        var result = new List<HouseholdLandUseRight>();
        foreach (var right in rights.OrderBy(right => right.Id, StringComparer.Ordinal))
        {
            var transferred = right.Tiles.Where(footprint.Contains).ToArray();
            if (transferred.Length == 0)
            {
                result.Add(right);
                continue;
            }
            // Removing a footprint can split a plot. Keep every remaining tile
            // with its owner and retain the original grant terms on each piece.
            var originalIdAvailable = true;
            foreach (var (tiles, owner) in ConnectedPlots(map, right.Tiles.Where(tile => !footprint.Contains(tile)))
                         .Select(plot => (plot, right.HouseholdId))
                         .Concat(ConnectedPlots(map, transferred).Select(plot => (plot, targetHouseholdId))))
            {
                result.Add(right with
                {
                    Id = originalIdAvailable ? right.Id : NextId(),
                    HouseholdId = owner,
                    Tiles = tiles,
                });
                originalIdAvailable = false;
            }
        }
        return result.OrderBy(right => right.Id, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> ClaimantsAt(GridPoint tile,
        IReadOnlyList<HouseholdLandUseRight> rights, IReadOnlyList<HouseholdLandUseRequest> requests) =>
        rights.Where(right => right.Tiles.Contains(tile)).Select(right => right.HouseholdId)
            .Concat(requests.Where(request => request.Status == "pending" && UnresolvedRequestTiles(request).Contains(tile)).Select(request => request.HouseholdId))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static bool IsCoveredByTownTitle(GridPoint tile, string townId,
        IReadOnlyList<TownLandTitleRecord> titles) =>
        titles.Any(title => title.TownId == townId && title.Tiles.Contains(tile));

    public static void ValidateRecords(SeededMap map, long worldTick,
        IReadOnlyList<TownRuntimeState>? towns,
        IReadOnlyList<TownLandTitleRecord>? titles,
        IReadOnlyList<HouseholdLandUseRight>? rights,
        IReadOnlyList<HouseholdLandUseRequest>? requests,
        SocietyCheckpoint society)
    {
        if (towns is null || titles is null || rights is null || requests is null ||
            titles.Any(item => item is null) || rights.Any(item => item is null) || requests.Any(item => item is null))
            throw new InvalidDataException("Saved Town land titles, use rights and requests must be present and cannot contain null entries.");

        var townsById = towns.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var households = society.Households.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var agents = society.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (titles.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != titles.Count ||
            rights.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != rights.Count ||
            requests.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != requests.Count)
            throw new InvalidDataException("Saved Town land record identities must be unique within each record type.");

        var titledTiles = new HashSet<GridPoint>();
        foreach (var title in titles)
        {
            if (!ValidId(title.Id) || !ValidId(title.TownId) ||
                !townsById.TryGetValue(title.TownId, out var town) || town.OriginSite is null ||
                !IsValidPlot(map, title.Tiles, worldTick, title.RecordedTick) ||
                title.Tiles.Any(tile => !town.BorderTiles.Contains(tile) || !titledTiles.Add(tile)))
                throw new InvalidDataException("A saved Town title is invalid or outside its recorded Town border.");
        }
        TownLandClaimRules.Validate(map, worldTick, towns, titles);

        if (titles.Count > 0 && towns.All(town => town.OriginSite is null))
            throw new InvalidDataException("Town title may only be created from an accepted first-Town layout.");

        var grantedUseTiles = new HashSet<GridPoint>();
        foreach (var right in rights)
        {
            if (!ValidId(right.Id) || !ValidId(right.TownId) || !townsById.ContainsKey(right.TownId) ||
                !households.Contains(right.HouseholdId) || !ValidText(right.HouseholdId, 256) ||
                !ValidText(right.GrantSource, 64) ||
                !IsValidPlot(map, right.Tiles, worldTick, right.GrantedTick, right.AgreedEndTick) ||
                right.Tiles.Any(tile => !IsCoveredByTownTitle(tile, right.TownId, titles) ||
                    !grantedUseTiles.Add(tile)))
                throw new InvalidDataException("A saved household land-use right is invalid or not covered by Town title.");
        }

        foreach (var request in requests)
        {
            if (!ValidId(request.Id) || !ValidId(request.TownId) || !townsById.ContainsKey(request.TownId) ||
                !households.Contains(request.HouseholdId) || !ValidText(request.HouseholdId, 256) ||
                !TownHearingProcedure.Id(request.RequestedByAgentId) || !agents.Contains(request.RequestedByAgentId) ||
                request.Tiles is not { Count: <= CognitionDecisionResponse.MaximumCivicLandTiles } ||
                !IsValidPlot(map, request.Tiles, worldTick, request.RequestedTick, request.AgreedEndTick) ||
                request.Tiles.Any(tile => !IsCoveredByTownTitle(tile, request.TownId, titles)))
                throw new InvalidDataException("A saved household land-use request is invalid or not covered by Town title.");
        }
        HouseholdLandGrantRules.Validate(worldTick, towns, rights, requests, agents);
    }

    private static IEnumerable<GridPoint> CardinalNeighbors(SeededMap map, GridPoint point)
    {
        foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
        {
            var neighbor = map.WrapColumn(new GridPoint(point.X + dx, point.Y + dy));
            if (map.Contains(neighbor))
                yield return neighbor;
        }
    }

    private static string StableId(string prefix, string scope, int index) =>
        $"{prefix}:{scope}:{index.ToString(CultureInfo.InvariantCulture)}";

    private static bool ValidId(string? value) => ValidText(value, 128);

    private static bool ValidText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximumLength &&
        !value.Any(char.IsControl);
}

public sealed partial class PrivateWorldRuntime
{
    /// <summary>
    /// Files a structured household use request for Council consideration.
    /// Filing supplies no household acceptance and cannot settle a dispute.
    /// </summary>
    public HouseholdLandUseRequestResult RequestHouseholdLandUse(string requestId,
        string requestedByAgentId, string townId, IReadOnlyList<GridPoint> requestedTiles,
        long? agreedEndTick = null)
    {
        gate.Wait();
        try
        {
            var town = towns.SingleOrDefault(t => t.Id == townId);
            if (town?.Governance is not { } governance)
                return RejectedLandRequest("The Town needs a Council before a use request can be filed.");
            var (result, updated) = FileHouseholdLandUse(requestId, requestedByAgentId, townId,
                requestedTiles, agreedEndTick, governance);
            if (result.Applied && !result.IsDuplicate)
            {
                SaveTownGovernance(town, updated);
                MaintainTownProjects();
            }
            return result;
        }
        finally { gate.Release(); }
    }

    private (HouseholdLandUseRequestResult Result, TownGovernanceState State) FileHouseholdLandUse(
        string requestId, string requestedByAgentId, string townId, IReadOnlyList<GridPoint> requestedTiles,
        long? agreedEndTick, TownGovernanceState governance)
    {
        var priorNoticeCount = governance.Notices.Count;
        if (!ValidLandRequestText(requestId, 128) || !TownHearingProcedure.Id(requestedByAgentId) ||
            !ValidLandRequestText(townId, 128) || requestedTiles is not { Count: > 0 and <= CognitionDecisionResponse.MaximumCivicLandTiles } ||
            requestedTiles.Distinct().Count() != requestedTiles.Count ||
            requestedTiles.Any(tile => !map.IsLand(tile)))
            return (RejectedLandRequest("Choose a request ID, an adult Town resident, and one connected land plot."), governance);

        if (founderSetup is not { Started: true } ||
            !society.Checkpoint.Inhabitants.Any(person => person.Id == requestedByAgentId &&
                person.Status == SocietyInhabitantStatus.Active &&
                (person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder) && person.HouseholdId is not null) ||
            !towns.Any(town => town.Id == townId &&
                town.ResidentIds.Contains(requestedByAgentId, StringComparer.Ordinal)))
            return (RejectedLandRequest("Only an adult household member of this Town can file a use request."), governance);

        var householdId = society.Checkpoint.GetInhabitant(requestedByAgentId).HouseholdId!;
        var tiles = TownLandRightsRules.OrderTiles(requestedTiles);
        if (TownLandRightsRules.ConnectedPlots(map, tiles).Count != 1 ||
            tiles.Any(tile => !TownLandRightsRules.IsCoveredByTownTitle(tile, townId, townLandTitles)))
            return (RejectedLandRequest("The requested plot must be connected land covered by this Town's title."), governance);
        if (agreedEndTick is { } endTick && endTick < WorldTick)
            return (RejectedLandRequest("The optional end date cannot be earlier than today."), governance);
        if (householdLandUseRights.Any(right => right.TownId == townId && right.HouseholdId == householdId &&
                tiles.Any(right.Tiles.Contains)))
            return (RejectedLandRequest("This household already has a recorded use right for part of that plot."), governance);
        // Another household's recorded right is contested as a dispute; a building or running expansion without one is not free land.
        var foreignBuildings = BuildingFootprintTiles(building => building.HouseholdId != householdId);
        foreignBuildings.UnionWith(ExpansionWorkTiles(building => building?.HouseholdId != householdId));
        foreignBuildings.UnionWith(MarketSiteTiles());
        if (tiles.Any(tile => foreignBuildings.Contains(tile) && !householdLandUseRights.Any(right => right.Tiles.Contains(tile))))
            return (RejectedLandRequest("That plot includes another household's or the Town's building or expansion work."), governance);
        if (IncludesForeignFieldWithoutUseRight(householdId, tiles))
            return (RejectedLandRequest("That plot includes another household's field without a recorded use right."), governance);

        var existingId = householdLandUseRequests.SingleOrDefault(item => item.Id == requestId);
        var proposed = new HouseholdLandUseRequest(requestId, townId, householdId,
            requestedByAgentId, tiles, WorldTick, agreedEndTick);
        if (existingId is not null)
        {
            if (SameLandRequest(existingId, proposed))
                return (ExistingLandRequestResult(existingId, isDuplicate: true), governance);
            return (RejectedLandRequest("That request ID has already been used for a different request."), governance);
        }

        var duplicate = householdLandUseRequests.FirstOrDefault(item =>
            item.Status == "pending" && item.TownId == proposed.TownId && item.HouseholdId == proposed.HouseholdId &&
            item.AgreedEndTick == proposed.AgreedEndTick &&
            item.Tiles.SequenceEqual(proposed.Tiles));
        if (duplicate is not null)
            return (ExistingLandRequestResult(duplicate, isDuplicate: true), governance);

        try
        {
            governance = OpenLandUseProposal(towns.Single(t => t.Id == townId), governance, ref proposed);
        }
        catch (InvalidOperationException exception) { return (RejectedLandRequest(exception.Message), governance); }
        governance = TownGovernanceRules.LandUseNotice(governance, proposed.Id,
            $"{requestedByAgentId} requests household use of {TownLandClaimRules.DescribeTiles(tiles)}. " +
            "Every current adult in the household must explicitly accept; filing and Council votes supply no acceptance.", WorldTick);
        governance = TownGovernanceRules.LearnNotices(governance, requestedByAgentId,
            governance.Notices.Skip(priorNoticeCount).Select(n => n.Id), WorldTick);
        householdLandUseRequests.Add(proposed);
        householdLandUseRequests = householdLandUseRequests.OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("land_use_requested", $"{requestId}:{townId}:{householdId}:{tiles.Length}");
        return (ExistingLandRequestResult(proposed, isDuplicate: false), governance);
    }

    private HouseholdLandUseRequestResult ExistingLandRequestResult(HouseholdLandUseRequest request,
        bool isDuplicate) => new(true, request,
        request.Tiles.Any(tile => TownLandRightsRules.IsDisputed(tile, householdLandUseRights, householdLandUseRequests)),
        isDuplicate, null);

    private static HouseholdLandUseRequestResult RejectedLandRequest(string failure) =>
        new(false, null, false, false, failure);

    private static bool ValidLandRequestText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximumLength &&
        !value.Any(char.IsControl);

    private static bool SameLandRequest(HouseholdLandUseRequest left, HouseholdLandUseRequest right) =>
        left.TownId == right.TownId && left.HouseholdId == right.HouseholdId &&
        left.RequestedByAgentId == right.RequestedByAgentId && left.AgreedEndTick == right.AgreedEndTick &&
        left.Tiles.SequenceEqual(right.Tiles);
}
