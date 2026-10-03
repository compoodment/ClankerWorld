using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public static class HouseholdLandGrantRules
{
    private const string GrantPrefix = "council:";
    private static string Digest(string text) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string GrantSource(string requestId) => GrantPrefix + Digest(requestId);
    public static string RightId(string requestId) => "household-use:grant:" + Digest(requestId);
    public static string RequestKey(HouseholdLandUseRequest request) => "land_use:" + Digest(
        request.TownId + "|" + request.HouseholdId + "|" + request.AgreedEndTick?.ToString(CultureInfo.InvariantCulture) +
        "|" + TownLandClaimRules.DescribeTiles(request.Tiles));

    public static bool IsAvailable(HouseholdLandUseRequest request, IReadOnlyList<HouseholdLandUseRight> rights,
        IReadOnlyList<HouseholdLandUseRequest> requests) =>
        request.HearingResolutions.Count == 0 && !rights.Any(right => right.Tiles.Any(request.Tiles.Contains)) &&
        !requests.Any(other => other.Status == "pending" && other.HouseholdId != request.HouseholdId &&
            other.Tiles.Any(tile => request.Tiles.Contains(tile) && !other.HearingResolutions.Any(resolution => resolution.Tiles.Contains(tile))));

    public static void Validate(long tick, IReadOnlyList<TownRuntimeState> towns,
        IReadOnlyList<HouseholdLandUseRight> rights, IReadOnlyList<HouseholdLandUseRequest> requests,
        IReadOnlySet<string> knownAgents)
    {
        IReadOnlyList<HouseholdLandUseRight> receiptRights = rights;
        foreach (var town in towns)
        {
            if (town.LandHearings is null)
                throw new InvalidDataException("Saved Town hearing history must be present.");
            receiptRights = TownLandHearingRules.OriginalGrantRights(town.LandHearings, receiptRights);
        }
        foreach (var request in requests)
        {
            var council = towns.Single(t => t.Id == request.TownId).Governance;
            var proposal = council?.Proposals.SingleOrDefault(p => p.Id == request.CouncilProposalId);
            if (request.Status is not ("pending" or "granted" or "rejected" or "withdrawn" or "hearing_resolved") ||
                request.Status == "pending" && request.SettledTick is not null ||
                request.Status != "pending" && (request.SettledTick is null || request.SettledTick < request.RequestedTick || request.SettledTick > tick) ||
                request.Consents is null || request.GrantAdults is null || request.HearingResolutions is null ||
                request.Consents.Any(c => c is null || !knownAgents.Contains(c.AgentId) || c.Tick < request.RequestedTick ||
                    c.Tick > (request.SettledTick ?? tick) || council is null ||
                    !council.Knowledge.Any(k => k.AgentId == c.AgentId && k.LearnedTick <= c.Tick &&
                        council.Notices.Any(n => n.Id == k.NoticeId && n.Kind == "land_use" && n.SubjectId == request.Id))) ||
                request.Consents.Select(c => c.AgentId).Distinct(StringComparer.Ordinal).Count() != request.Consents.Count ||
                request.CouncilProposalId is not null && (proposal is null || proposal.Kind != "land_use" ||
                    proposal.SubjectId != request.Id || proposal.RequestKey != RequestKey(request) ||
                    proposal.AuthorId != request.RequestedByAgentId || proposal.OpenedTick < request.RequestedTick) ||
                request.Status != "granted" && request.GrantAdults.Count != 0)
                throw new InvalidDataException("A saved household land request has invalid approval or consent history.");
            var grants = receiptRights.Where(right => right.GrantSource == GrantSource(request.Id)).ToArray();
            if (request.Status == "granted")
            {
                if (proposal is not { Status: "passed" } || proposal.SettledTick > request.SettledTick ||
                    request.GrantAdults.Count == 0 || !request.GrantAdults.SequenceEqual(TownGovernanceRules.Ordered(request.GrantAdults)) ||
                    request.GrantAdults.Any(id => !knownAgents.Contains(id) || !request.Consents.Any(c => c.AgentId == id && c.Accepted)) ||
                    request.AgreedEndTick is { } end && end <= request.SettledTick ||
                    !TownLandRightsRules.OrderTiles(grants.SelectMany(right => right.Tiles)).SequenceEqual(request.Tiles) ||
                    grants.Any(right => right.TownId != request.TownId || right.GrantedTick != request.SettledTick ||
                        right.AgreedEndTick != request.AgreedEndTick))
                    throw new InvalidDataException("A household land grant must retain its Council approval, adult acceptance and recorded terms.");
            }
            else if (grants.Length != 0)
                throw new InvalidDataException("An unfinished or refused land request cannot supply a use right.");
        }
        TownLandHearingValidation.ValidateRequestResolutions(tick, towns.Select(town => town.LandHearings).ToArray(), requests);
        if (receiptRights.Any(right => right.GrantSource.StartsWith(GrantPrefix, StringComparison.Ordinal) &&
                !requests.Any(request => request.Status == "granted" && GrantSource(request.Id) == right.GrantSource)) ||
            towns.Any(town => town.Governance?.Proposals.Any(proposal => proposal.Kind == "land_use" &&
                !requests.Any(request => request.TownId == town.Id && request.CouncilProposalId == proposal.Id &&
                    request.Id == proposal.SubjectId)) == true))
            throw new InvalidDataException("A saved land approval or use right is missing its household request.");
    }
}

public sealed partial class PrivateWorldRuntime
{
    private TownGovernanceState OpenLandUseProposal(TownRuntimeState town, TownGovernanceState state,
        ref HouseholdLandUseRequest request)
    {
        if (request.CouncilProposalId is not null || HasOpenLandHearingPlot(town.Id, request.Tiles) ||
            !HouseholdLandGrantRules.IsAvailable(request, householdLandUseRights, householdLandUseRequests) ||
            !TownAdults(town).Contains(request.RequestedByAgentId, StringComparer.Ordinal)) return state;
        var text = $"Grant household use of {request.Tiles.Count} land tile(s); every current adult in the household must separately accept.";
        state = TownGovernanceRules.SubmitProposal(state, town.Id, request.RequestedByAgentId, "land_use", request.Id,
            text, "council:" + state.Revision, TownAdults(town), WorldTick, CivicDay, landUseRequest: request);
        var key = HouseholdLandGrantRules.RequestKey(request);
        request = request with { CouncilProposalId = state.Proposals.Single(p => p.RequestKey == key && p.Status == "pending").Id };
        return state;
    }

    private TownGovernanceState ResolveHouseholdLandRequests(TownRuntimeState town, TownGovernanceState state)
    {
        foreach (var saved in householdLandUseRequests.Where(r => r.TownId == town.Id && r.Status == "pending").ToArray())
        {
            var request = saved;
            if (HasOpenLandHearingPlot(town.Id, request.Tiles) || request.HearingResolutions.Count != 0) continue;
            var adults = HouseholdAdults(request.HouseholdId);
            var proposal = state.Proposals.SingleOrDefault(p => p.Id == request.CouncilProposalId);
            string? refusal = adults.Length == 0 ? "The household has no living adult signatory." :
                request.AgreedEndTick is { } end && end <= WorldTick ? "The requested end date has arrived." :
                request.Consents.Any(c => !c.Accepted && adults.Contains(c.AgentId, StringComparer.Ordinal)) ? "A current adult in the household declined." :
                proposal is { Status: "rejected" or "cancelled" or "withdrawn" } ? "The Council proposal did not pass." : null;
            if (refusal is not null)
            {
                request = request with { Status = "rejected", SettledTick = WorldTick };
                if (request.CouncilProposalId is { } proposalId)
                    state = TownGovernanceRules.CancelLandUseProposal(state, proposalId, WorldTick);
                state = TownGovernanceRules.LandUseNotice(state, request.Id, "Household land request refused: " + refusal, WorldTick);
            }
            else if (HouseholdLandGrantRules.IsAvailable(request, householdLandUseRights, householdLandUseRequests))
            {
                if (proposal is null)
                {
                    try { state = OpenLandUseProposal(town, state, ref request); }
                    catch (InvalidOperationException) { /* Ordinary Council retry rules still apply. */ }
                }
                else if (proposal.Status == "passed" && adults.All(id => request.Consents.Any(c => c.AgentId == id && c.Accepted)))
                {
                    request = request with { Status = "granted", SettledTick = WorldTick, GrantAdults = adults };
                    householdLandUseRights.Add(new(HouseholdLandGrantRules.RightId(request.Id), town.Id, request.HouseholdId,
                        request.Tiles, WorldTick, HouseholdLandGrantRules.GrantSource(request.Id), request.AgreedEndTick));
                    householdLandUseRights = householdLandUseRights.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
                    state = TownGovernanceRules.LandUseNotice(state, request.Id,
                        "Household land use granted for " + TownLandClaimRules.DescribeTiles(request.Tiles) + ".", WorldTick);
                    AppendEvent("land_use_granted", $"{request.Id}:{town.Id}:{request.HouseholdId}:{request.Tiles.Count}");
                }
            }
            if (request != saved)
                householdLandUseRequests = householdLandUseRequests.Select(r => r.Id == request.Id ? request : r).ToList();
        }
        return state;
    }

    private string LandUseTerms(HouseholdLandUseRequest request) =>
        "Exact tiles: " + TownLandClaimRules.DescribeTiles(request.Tiles) + ". " +
        (request.AgreedEndTick is { } end ? $"Agreed end: world day {end / CivicDay + 1}. " : "No agreed end date. ");

    private void AddHouseholdLandCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        if (TownAdults(town).Contains(actor, StringComparer.Ordinal))
            foreach (var building in worldSimulation.Buildings.Where(b => b.TownId == town.Id && b.HouseholdId == HouseholdFor(actor)))
                if (ExpansionLandRequestTiles(actor, building) is { Length: > 0 } extra)
                    candidates.Add(new(CivicAction(town.Id, "request_expansion_land", building.InstanceId, TownLandClaimRules.DescribeTiles(extra)),
                        "Request household use of the extra land needed to expand your House. Council approval and every current adult's acceptance are required. Exact tiles: " +
                        TownLandClaimRules.DescribeTiles(extra) + ".", 190));
        var history = CivicHistory(town);
        foreach (var request in householdLandUseRequests.Where(r => r.TownId == town.Id && r.Status == "pending" && history.Knows(actor, "land_use", r.Id)))
        {
            if (request.RequestedByAgentId == actor)
                candidates.Add(new(CivicAction(town.Id, "withdraw_land_use", request.Id),
                    "Withdraw your pending household land request. " + LandUseTerms(request), 190));
            if (!HouseholdAdults(request.HouseholdId).Contains(actor, StringComparer.Ordinal) || request.Consents.Any(c => c.AgentId == actor)) continue;
            candidates.Add(new(CivicAction(town.Id, "accept_land_use", request.Id),
                "Personally accept the requested use right for your household. Council approval and every current adult's acceptance are also required. " + LandUseTerms(request), 165));
            candidates.Add(new(CivicAction(town.Id, "decline_land_use", request.Id),
                "Personally decline the requested use right for your household. " + LandUseTerms(request), 166));
        }
    }

    private GridPoint[]? ExpansionLandRequestTiles(string actor, PlacedBuilding building)
    {
        if (building.HouseholdId is null || !MayExpandBuilding(actor, building, out _) ||
            ExpansionShapes(building).Any(shape => CanFitExpansion(building, shape.Position, shape.Footprint, out _))) return null;
        var definition = worldContent.Buildings.Single(d => d.CanonicalId == building.DefinitionId);
        foreach (var shape in ExpansionShapes(building))
        {
            if (!CanFitExpansion(building, shape.Position, shape.Footprint, out _, requireLandRights: false)) continue;
            var extra = TownLandRightsRules.OrderTiles(WorldContentSimulationRules.Footprint(
                BuildingStorageRules.WithSize(definition, shape.Footprint.Width, shape.Footprint.Height), shape.Position)
                .Except(WorldContentSimulationRules.Footprint(definition, building))
                .Where(tile => !householdLandUseRights.Any(right => right.HouseholdId == building.HouseholdId && right.Tiles.Contains(tile))));
            if (extra.Length == 0 || extra.Any(tile => !TownLandRightsRules.IsCoveredByTownTitle(tile, building.TownId!, townLandTitles)) ||
                householdLandUseRequests.Any(r => r.Status == "pending" && r.HouseholdId == building.HouseholdId && extra.All(r.Tiles.Contains))) continue;
            return extra;
        }
        return null;
    }

    private TownGovernanceState SubmitExpansionLandRequest(TownRuntimeState town, TownGovernanceState state, string actor, string buildingId)
    {
        var building = worldSimulation.Buildings.Single(b => b.InstanceId == buildingId);
        var tiles = ExpansionLandRequestTiles(actor, building) ?? throw new InvalidOperationException("The expansion's land need has changed.");
        return SubmitPersonalLandUseRequest(town, state, actor, tiles.Select(t => new CognitionLandTile(t.X, t.Y)).ToArray());
    }

    private TownGovernanceState DecideHouseholdLandUse(TownGovernanceState state, string actor, string requestId, string action)
    {
        var request = householdLandUseRequests.Single(r => r.Id == requestId && r.Status == "pending");
        if (action == "withdraw_land_use")
        {
            if (request.RequestedByAgentId != actor) throw new InvalidOperationException("Only the filer may withdraw this request.");
            request = request with { Status = "withdrawn", SettledTick = WorldTick };
            if (request.CouncilProposalId is { } proposalId)
                state = TownGovernanceRules.CancelLandUseProposal(state, proposalId, WorldTick);
            state = TownGovernanceRules.LandUseNotice(state, request.Id, "The household land request was withdrawn by its filer.", WorldTick);
        }
        else
        {
            if (!HouseholdAdults(request.HouseholdId).Contains(actor, StringComparer.Ordinal) || request.Consents.Any(c => c.AgentId == actor))
                throw new InvalidOperationException("Land acceptance requires an explicit choice from a current adult household member.");
            request = request with
            {
                Consents = request.Consents.Append(new(actor, action == "accept_land_use", WorldTick))
                .OrderBy(c => c.AgentId, StringComparer.Ordinal).ToArray()
            };
        }
        householdLandUseRequests = householdLandUseRequests.Select(r => r.Id == request.Id ? request : r).ToList();
        return state;
    }

    private TownGovernanceState SubmitPersonalLandUseRequest(TownRuntimeState town, TownGovernanceState state,
        string actor, IReadOnlyList<CognitionLandTile>? landTiles)
    {
        if (landTiles is not { Count: > 0 and <= CognitionDecisionResponse.MaximumCivicLandTiles })
            throw new InvalidOperationException("Choose exact land tiles for the household request.");
        var (result, updated) = FileHouseholdLandUse("land-request:" + town.Id + ":" + (state.Notices.Count + 1),
            actor, town.Id, landTiles.Select(t => new GridPoint(t.X, t.Y)).ToArray(), null, state);
        if (!result.Applied) throw new InvalidOperationException(result.Failure);
        return updated;
    }
}
