using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public static class TownLandTransferValidation
{
    public static void Validate(SeededMap map, long tick, string townId, TownLandHearingState state,
        IReadOnlyList<HouseholdLandUseRight> currentRights, IReadOnlyList<TownLandTitleRecord> titles,
        IReadOnlySet<string> agents, IReadOnlySet<string> households, TownGovernanceState? council)
    {
        Check(state.Transfers is not null && state.Transfers.All(request => request is not null), "Saved voluntary transfer records must be present.");
        Check(state.Transfers.Select(request => request.Id).Distinct(StringComparer.Ordinal).Count() == state.Transfers.Count,
            "Saved voluntary transfer identities must be unique.");
        if (state.Transfers.Count == 0) return;
        Check(state.OriginalRights.All(version => version.Right is not null) && state.Adjustments.All(adjustment =>
            adjustment.PriorRights is not null && adjustment.ResultRights is not null &&
            adjustment.PriorRights.All(version => version is not null && version.Right is not null) && adjustment.ResultRights.All(right => right is not null)),
            "Saved transfer provenance cannot contain incomplete permission history.");
        var recordedRights = currentRights.Concat(state.OriginalRights.Select(version => version.Right))
            .Concat(state.Adjustments.SelectMany(adjustment => adjustment.PriorRights.Select(version => version.Right).Concat(adjustment.ResultRights))).ToArray();
        foreach (var request in state.Transfers)
        {
            Check(TownGovernmentValidation.ValidId(request.Id, "land-transfer:" + townId + ":", state.Sequence) && request.TownId == townId && Id(request.FilerId) && agents.Contains(request.FilerId) &&
                Id(request.TargetHouseholdId) && households.Contains(request.TargetHouseholdId) && request.ProposedTick >= 0 && request.ProposedTick <= tick &&
                request.Status is "pending" or "transferred" or "rejected" or "withdrawn" or "invalidated" &&
                request.RightVersions is { Count: > 0 } && request.RightVersions.All(version => version is not null && version.Right is not null) &&
                request.Parties is { Count: > 0 } && request.Parties.All(party => party is not null) && request.Responses is not null &&
                request.Responses.All(response => response is not null) && TownLandRightsRules.IsValidPlot(map, request.Tiles, tick, request.ProposedTick) &&
                request.Tiles.All(tile => TownLandRightsRules.IsCoveredByTownTitle(tile, townId, titles)),
                "A saved transfer must retain its immutable known parties, connected titled plot and exact prior permissions.");
            Check(request.RightVersions.Select(version => version.Id).SequenceEqual(request.RightVersions.Select(version => version.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                "Transfer permission snapshots must be unique and canonical.");
            foreach (var version in request.RightVersions)
            {
                var right = version.Right;
                Check(version.Id == right.Id && Id(right.Id) && right.TownId == townId && households.Contains(right.HouseholdId) &&
                    right.HouseholdId != request.TargetHouseholdId && right.Tiles.Any(request.Tiles.Contains) &&
                    version.Version == TownLandHearingRules.Version(right) &&
                    TownLandRightsRules.IsValidPlot(map, right.Tiles, request.ProposedTick, right.GrantedTick, right.AgreedEndTick) &&
                    (right.AgreedEndTick is null || right.AgreedEndTick > request.ProposedTick) &&
                    recordedRights.Any(recorded => recorded.Id == version.Id && TownLandHearingRules.Version(recorded) == version.Version),
                    "A transfer cannot invent a prior permission, expiry term or source household.");
            }
            Check(request.Tiles.All(tile => request.RightVersions.Count(version => version.Right.Tiles.Contains(tile)) == 1),
                "A transfer plot must consist entirely of unambiguous existing permissions.");
            ValidateParties(request.Parties, request, agents, households, requireAdults: true);
            Check(request.Parties.Any(party => party.AdultIds.Contains(request.FilerId, StringComparer.Ordinal)), "A transfer filer must be an actual affected adult.");
            Check(council?.Notices.Any(notice => notice.Id == request.NoticeId && notice.Kind == "land_transfer" &&
                notice.SubjectId == TownLandTransferRules.TermsToken(request) && notice.PostedTick == request.ProposedTick) == true,
                "A transfer needs its actual public notice containing the exact immutable terms.");
            foreach (var response in request.Responses)
                Check(agents.Contains(response.AgentId) && request.Parties.Any(party => party.HouseholdId == response.HouseholdId) &&
                    response.Kind is "accept" or "decline" && response.Tick >= request.ProposedTick && response.Tick <= tick &&
                    (request.SettledTick is null || response.Tick <= request.SettledTick) && Canonical(response.PartyAdults) &&
                    response.PartyAdults.All(agents.Contains) && response.PartyAdults.Contains(response.AgentId, StringComparer.Ordinal) &&
                    TownLandTransferRules.HasNotice(request, response.AgentId, response.Tick, council!.Knowledge),
                    "Each transfer response needs the household adult's own actual notice receipt and contemporaneous roster.");
            Check(request.Responses.Select(response => response.Tick).SequenceEqual(request.Responses.Select(response => response.Tick).Order()),
                "Transfer responses must retain their actual chronological order.");
            Check(request.Status == "pending" ? request.SettledTick is null && request.Receipt is null && request.Reason is null :
                request.SettledTick >= request.ProposedTick && request.SettledTick <= tick, "A transfer closure must retain its actual settlement time.");
            if (request.Status == "transferred")
            {
                var receipt = request.Receipt;
                Check(receipt is not null && receipt.Tick == request.SettledTick && Id(receipt.AdjustmentId) && request.Reason is null,
                    "A completed transfer needs its exact durable permission-adjustment receipt.");
                ValidateParties(receipt.Parties, request, agents, households, requireAdults: true);
                Check(TownLandTransferRules.HasAllConsents(request, receipt.Parties, council!.Knowledge, receipt.Tick) &&
                    request.RightVersions.All(version => version.Right.AgreedEndTick is null || version.Right.AgreedEndTick > receipt.Tick) &&
                    request.Responses.All(response => response.Kind == "accept") &&
                    state.Adjustments.Count(adjustment => adjustment.Id == receipt.AdjustmentId && adjustment.TransferId == request.Id && adjustment.Kind == "voluntary_transfer") == 1,
                    "A completed transfer needs every current source and beneficiary adult's separate acceptance.");
            }
            else
            {
                Check(request.Receipt is null && !state.Adjustments.Any(adjustment => adjustment.TransferId == request.Id), "An unfinished or refused transfer cannot alter permissions.");
                Check(request.Status switch
                {
                    "pending" => request.Responses.All(response => response.Kind == "accept") &&
                        TownLandTransferRules.PendingFailure(state, request, currentRights, [], tick) is null,
                    "rejected" => request.Reason == "adult_declined" && request.Responses.Count > 0 &&
                        request.Responses[^1].Kind == "decline" && request.Responses[^1].Tick == request.SettledTick,
                    "withdrawn" => request.Reason == "filer_withdrew",
                    "invalidated" => request.Reason is "rights_changed" or "permission_expired" or "plot_disputed" or "hearing_open",
                    _ => false
                }, "An invalidated, declined or withdrawn transfer must retain its actual closure reason.");
            }
        }
    }

    public static void ValidateAdjustment(SeededMap map, TownLandHearingState state, TownLandRightAdjustment adjustment)
    {
        var request = state.Transfers.SingleOrDefault(item => item.Id == adjustment.TransferId);
        Check(request is { Status: "transferred", Receipt: { } } && request.Receipt.AdjustmentId == adjustment.Id &&
            request.SettledTick == adjustment.Tick && adjustment.CaseId is null && adjustment.RulingId is null && adjustment.BuildingId is null &&
            adjustment.TargetHouseholdId == request.TargetHouseholdId && adjustment.Tiles.SequenceEqual(request.Tiles) &&
            TownLandTransferRules.SameSources(request.RightVersions, adjustment.PriorRights), "A voluntary adjustment needs its exact completed transfer receipt and prior permission versions.");
        var expected = TownLandRightsRules.ReassignFootprintRights(map, adjustment.PriorRights.Select(version => version.Right).ToArray(),
            adjustment.Tiles.ToHashSet(), request.TargetHouseholdId, adjustment.Tick);
        Check(TileTerms(expected).SequenceEqual(TileTerms(adjustment.ResultRights)),
            "A voluntary transfer may reassign only the noticed existing permissions, preserving all original terms and outside pieces.");
    }

    private static void ValidateParties(IReadOnlyList<TownLandTransferParty> parties, TownLandTransferRequest request,
        IReadOnlySet<string> agents, IReadOnlySet<string> households, bool requireAdults) =>
        Check(parties is not null && parties.All(party => party is not null && households.Contains(party.HouseholdId) &&
            Canonical(party.AdultIds) && party.AdultIds.All(agents.Contains) && (!requireAdults || party.AdultIds.Count > 0)) &&
            TownLandTransferRules.ValidParties(request.RightVersions.Select(version => version.Right), request.TargetHouseholdId, parties),
            "A transfer must retain precisely its source and beneficiary households and actual adult signatories.");
    private static IEnumerable<string> TileTerms(IEnumerable<HouseholdLandUseRight> rights) => rights.SelectMany(right => right.Tiles.Select(tile =>
        JsonSerializer.Serialize(new { tile.X, tile.Y, right.TownId, right.HouseholdId, right.GrantedTick, right.GrantSource, right.AgreedEndTick }))).Order(StringComparer.Ordinal);
    private static bool Id(string? value) => TownLandHearingRules.ValidText(value, 256);
    private static bool Canonical(IReadOnlyList<string>? values) => values is not null && values.All(Id) && values.SequenceEqual(TownLandHearingRules.Ordered(values));
    private static void Check([DoesNotReturnIf(false)] bool valid, string reason) { if (!valid) throw new InvalidDataException(reason); }
}
