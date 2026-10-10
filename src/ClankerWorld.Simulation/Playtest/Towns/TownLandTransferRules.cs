using System.Globalization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Consensual transfers change only existing permissions, after every current adult agrees.</summary>
public static class TownLandTransferRules
{
    public static string TermsToken(TownLandTransferRequest request) => request.Id + ":" + TownLandHearingRules.RecordVersion(new
    { request.TownId, request.TargetHouseholdId, request.Tiles, request.RightVersions, request.Price });

    public static string PaymentTransferId(TownLandTransferRequest request, string buyer, int index) =>
        "land-sale-payment:" + TermsToken(request) + ":" + buyer + ":" + index;

    public static IReadOnlyList<TownLandTransferParty> PartiesFor(IEnumerable<HouseholdLandUseRight> rights,
        IReadOnlyList<GridPoint> tiles, string targetHouseholdId, IReadOnlyDictionary<string, string?> adultHouseholds) =>
        rights.Where(right => right.Tiles.Any(tiles.Contains)).Select(right => right.HouseholdId).Append(targetHouseholdId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(household => new TownLandTransferParty(household, household == targetHouseholdId ? "beneficiary" : "source",
                adultHouseholds.Where(pair => pair.Value == household).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray())).ToArray();

    public static bool CanPropose(TownLandHearingState state, SeededMap map, string townId, string actor,
        IReadOnlyList<GridPoint> tiles, string targetHouseholdId, IReadOnlyList<HouseholdLandUseRight> currentRights,
        IReadOnlyList<TownLandTransferParty> parties, IReadOnlyList<HouseholdLandUseRequest> pendingRequests, long tick)
    {
        var rights = currentRights.Where(right => right.TownId == townId && right.Tiles.Any(tiles.Contains)).ToArray();
        return TownLandHearingRules.ValidText(townId, 128) && TownLandHearingRules.ValidText(targetHouseholdId, 128) &&
            TownLandRightsRules.IsValidPlot(map, tiles, tick, tick) && rights.Length > 0 &&
            rights.All(right => right.HouseholdId != targetHouseholdId && right.GrantedTick <= tick && (right.AgreedEndTick is null || right.AgreedEndTick > tick)) &&
            tiles.All(tile => rights.Count(right => right.Tiles.Contains(tile)) == 1 && !TownLandRightsRules.IsDisputed(tile, currentRights, pendingRequests)) &&
            ValidParties(rights, targetHouseholdId, parties) && parties.All(party => party.AdultIds.Count > 0) &&
            parties.Any(party => party.AdultIds.Contains(actor, StringComparer.Ordinal)) && !HasOpenHearing(state, townId, tiles);
    }

    public static TownLandHearingState Propose(TownLandHearingState state, SeededMap map, string townId, string filer,
        IReadOnlyList<GridPoint> tiles, string targetHouseholdId, IReadOnlyList<HouseholdLandUseRight> currentRights,
        IReadOnlyList<TownLandTransferParty> currentParties, IReadOnlyList<HouseholdLandUseRequest> pendingRequests,
        long tick, string noticeId, TownLandSalePrice? price = null)
    {
        if (!CanPropose(state, map, townId, filer, tiles, targetHouseholdId, currentRights, currentParties, pendingRequests, tick) ||
            !TownLandHearingRules.ValidText(noticeId, 128))
            throw new InvalidOperationException("A voluntary transfer needs one undisputed, unexpired existing plot, affected adults and a known beneficiary household.");
        var rights = CurrentSources(townId, tiles, currentRights).Select(TownLandHearingRules.Snapshot).ToArray();
        if (price is not null && (price.Quantity <= 0 || !TownLandHearingRules.ValidText(price.ItemKind, 128) ||
            price.ItemKind.Any(char.IsWhiteSpace) || InventoryContainerRules.IsContainer(price.ItemKind) || price.ItemKind == "handcart" ||
            rights.Any(version => version.Right.HouseholdId != price.SellerHouseholdId) ||
            !currentParties.Any(party => party.HouseholdId == price.SellerHouseholdId && party.AdultIds.Contains(filer))))
            throw new InvalidOperationException("A goods sale needs one actual source household and an exact positive goods price.");
        if (state.Transfers.Any(existing => existing.Status == "pending" && existing.TownId == townId && existing.TargetHouseholdId == targetHouseholdId &&
                existing.Tiles.SequenceEqual(tiles) && SameSources(existing.RightVersions, rights) && existing.Price == price)) return state;
        var request = new TownLandTransferRequest("land-transfer:" + townId + ":" + (state.Sequence + 1).ToString(CultureInfo.InvariantCulture),
            townId, filer, targetHouseholdId, tiles.ToArray(), rights, CopyParties(currentParties), noticeId, tick, [])
        { Price = price };
        return state with { Sequence = state.Sequence + 1, Transfers = state.Transfers.Append(request).ToArray() };
    }

    public static TownLandHearingState Respond(TownLandHearingState state, string requestId, string termsToken,
        string actor, string partyHouseholdId, string kind, IReadOnlyList<TownCivicReceipt> receipts,
        IReadOnlyList<HouseholdLandUseRight> currentRights, IReadOnlyList<TownLandTransferParty> currentParties,
        IReadOnlyList<HouseholdLandUseRequest> pendingRequests, long tick)
    {
        var request = state.Transfers.Single(item => item.Id == requestId && item.Status == "pending");
        var party = currentParties.SingleOrDefault(item => item.HouseholdId == partyHouseholdId);
        if (TermsToken(request) != termsToken || kind is not ("accept" or "decline") || party is null ||
            !party.AdultIds.Contains(actor, StringComparer.Ordinal) || !HasNotice(request, actor, tick, receipts) ||
            PendingFailure(state, request, currentRights, pendingRequests, tick) is not null ||
            !ValidParties(request.RightVersions.Select(version => version.Right), request.TargetHouseholdId, currentParties))
            throw new InvalidOperationException("A transfer response needs the exact current terms and the household adult's actual notice receipt.");
        var response = new TownLandTransferResponse(partyHouseholdId, actor, kind, tick, party.AdultIds.ToArray());
        request = request with { Responses = request.Responses.Append(response).ToArray() };
        if (kind == "decline") request = request with { Status = "rejected", SettledTick = tick, Reason = "adult_declined" };
        return Replace(state, request);
    }

    public static TownLandHearingState Withdraw(TownLandHearingState state, string requestId, string actor, long tick)
    {
        var request = state.Transfers.Single(item => item.Id == requestId && item.Status == "pending");
        if (request.FilerId != actor || tick < request.ProposedTick) throw new InvalidOperationException("Only the actual filer may withdraw a pending transfer.");
        return Replace(state, request with { Status = "withdrawn", SettledTick = tick, Reason = "filer_withdrew" });
    }

    public static IReadOnlyList<string> AcceptedAdults(TownLandTransferRequest request, TownLandTransferParty currentParty,
        IReadOnlyList<TownCivicReceipt> receipts, long tick) => currentParty.AdultIds.Where(actor =>
            request.Responses.LastOrDefault(response => response.HouseholdId == currentParty.HouseholdId && response.AgentId == actor && response.Tick <= tick)
                is { Kind: "accept" } acceptance && acceptance.PartyAdults.Contains(actor, StringComparer.Ordinal) &&
            HasNotice(request, actor, acceptance.Tick, receipts)).Order(StringComparer.Ordinal).ToArray();

    public static bool HasAllConsents(TownLandTransferRequest request, IReadOnlyList<TownLandTransferParty> currentParties,
        IReadOnlyList<TownCivicReceipt> receipts, long tick) =>
        ValidParties(request.RightVersions.Select(version => version.Right), request.TargetHouseholdId, currentParties) &&
        currentParties.All(party => party.AdultIds.Count > 0 && AcceptedAdults(request, party, receipts, tick).Count == party.AdultIds.Count);

    public static (TownLandHearingState State, IReadOnlyList<HouseholdLandUseRight> Rights) Advance(TownLandHearingState state,
        SeededMap map, IReadOnlyList<HouseholdLandUseRight> currentRights,
        IReadOnlyDictionary<string, IReadOnlyList<TownLandTransferParty>> partiesByRequest,
        IReadOnlyList<HouseholdLandUseRequest> pendingRequests, IReadOnlyList<TownCivicReceipt> receipts, long tick,
        IReadOnlyDictionary<string, TownLandSalePayment>? payments = null)
    {
        var rights = currentRights;
        foreach (var request in state.Transfers.Where(item => item.Status == "pending").ToArray())
        {
            var failure = PendingFailure(state, request, rights, pendingRequests, tick);
            if (failure is not null)
            {
                state = Replace(state, request with { Status = "invalidated", SettledTick = tick, Reason = failure });
                continue;
            }
            if (!partiesByRequest.TryGetValue(request.Id, out var parties) || !HasAllConsents(request, parties, receipts, tick)) continue;
            TownLandSalePayment? payment = null;
            if (request.Price is not null && (payments is null || !payments.TryGetValue(request.Id, out payment))) continue;
            var after = TownLandRightsRules.ReassignFootprintRights(map, rights, request.Tiles.ToHashSet(), request.TargetHouseholdId, tick);
            var prior = CurrentSources(request.TownId, request.Tiles, rights);
            var affectedTiles = prior.SelectMany(right => right.Tiles).ToHashSet();
            var result = after.Where(right => right.Tiles.Any(affectedTiles.Contains)).OrderBy(right => right.Id, StringComparer.Ordinal).ToArray();
            var adjustment = new TownLandRightAdjustment("land-adjustment:" + request.TownId + ":" + (state.Sequence + 1).ToString(CultureInfo.InvariantCulture),
                "voluntary_transfer", tick, prior.Select(TownLandHearingRules.Snapshot).ToArray(), result, request.Tiles,
                TargetHouseholdId: request.TargetHouseholdId, TransferId: request.Id);
            var produced = state.Adjustments.SelectMany(item => item.ResultRights).Select(right => right.Id).ToHashSet(StringComparer.Ordinal);
            var originals = state.OriginalRights.Concat(adjustment.PriorRights.Where(right => !produced.Contains(right.Id) &&
                !state.OriginalRights.Any(original => original.Id == right.Id))).OrderBy(right => right.Id, StringComparer.Ordinal).ToArray();
            state = state with { Sequence = state.Sequence + 1, OriginalRights = originals, Adjustments = state.Adjustments.Append(adjustment).ToArray() };
            state = Replace(state, request with
            {
                Status = "transferred",
                SettledTick = tick,
                Reason = null,
                Receipt = new(adjustment.Id, tick, CopyParties(parties)) { Payment = payment }
            });
            rights = after;
        }
        return (state, rights);
    }

    internal static string? PendingFailure(TownLandHearingState state, TownLandTransferRequest request,
        IReadOnlyList<HouseholdLandUseRight> currentRights, IReadOnlyList<HouseholdLandUseRequest> pendingRequests, long tick)
    {
        if (!SameSources(request.RightVersions, CurrentSources(request.TownId, request.Tiles, currentRights).Select(TownLandHearingRules.Snapshot))) return "rights_changed";
        if (request.RightVersions.Any(version => version.Right.AgreedEndTick <= tick)) return "permission_expired";
        if (request.Tiles.Any(tile => TownLandRightsRules.IsDisputed(tile, currentRights, pendingRequests))) return "plot_disputed";
        return HasOpenHearing(state, request.TownId, request.Tiles) ? "hearing_open" : null;
    }

    internal static bool HasNotice(TownLandTransferRequest request, string actor, long tick, IReadOnlyList<TownCivicReceipt> receipts) =>
        receipts.Any(receipt => receipt.AgentId == actor && receipt.NoticeId == request.NoticeId && receipt.LearnedTick >= request.ProposedTick && receipt.LearnedTick <= tick);
    internal static bool ValidParties(IEnumerable<HouseholdLandUseRight> rights, string target, IReadOnlyList<TownLandTransferParty> parties)
    {
        var required = rights.Select(right => right.HouseholdId).Append(target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return parties.Select(party => party.HouseholdId).SequenceEqual(required) && parties.All(party =>
            party.Kind == (party.HouseholdId == target ? "beneficiary" : "source") && party.AdultIds.SequenceEqual(TownLandHearingRules.Ordered(party.AdultIds))) &&
            parties.SelectMany(party => party.AdultIds).Distinct(StringComparer.Ordinal).Count() == parties.Sum(party => party.AdultIds.Count);
    }
    internal static bool SameSources(IEnumerable<TownLandRightVersion> left, IEnumerable<TownLandRightVersion> right) =>
        left.Select(version => version.Id + ":" + version.Version).SequenceEqual(right.Select(version => version.Id + ":" + version.Version));
    private static bool HasOpenHearing(TownLandHearingState state, string townId, IReadOnlyList<GridPoint> tiles) =>
        state.Cases.Any(item => item.TownId == townId && (item.Status == "pending" || item.ReopenRequests.Any(request => request.Status == "pending")) &&
            TownLandHearingRules.CurrentRevision(item).Tiles.Any(tiles.Contains));
    private static HouseholdLandUseRight[] CurrentSources(string townId, IReadOnlyList<GridPoint> tiles, IEnumerable<HouseholdLandUseRight> rights) =>
        rights.Where(right => right.TownId == townId && right.Tiles.Any(tiles.Contains)).OrderBy(right => right.Id, StringComparer.Ordinal).ToArray();
    private static TownLandTransferParty[] CopyParties(IEnumerable<TownLandTransferParty> parties) =>
        parties.Select(party => party with { AdultIds = party.AdultIds.ToArray() }).ToArray();
    private static TownLandHearingState Replace(TownLandHearingState state, TownLandTransferRequest request) =>
        state with { Transfers = state.Transfers.Select(item => item.Id == request.Id ? request : item).ToArray() };
}
