using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private bool LandHearingAdult(string actor) => society.Checkpoint.Inhabitants.Any(person =>
        person.Id == actor && person.Status == SocietyInhabitantStatus.Active &&
        person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder);

    private TownLandCaseParty[] LandHearingParties(TownRuntimeState town,
        IReadOnlyList<GridPoint> tiles, TownLandCase? item = null, string? filingHousehold = null,
        string? townRepresentative = null)
    {
        var householdIds = householdLandUseRights.Where(right => right.TownId == town.Id && right.Tiles.Any(tiles.Contains))
            .Select(right => right.HouseholdId)
            .Concat(householdLandUseRequests.Where(request => request.TownId == town.Id && request.Status == "pending" &&
                TownLandRightsRules.UnresolvedRequestTiles(request).Any(tiles.Contains)).Select(request => request.HouseholdId))
            .Concat(item is null ? [] : TownLandHearingRules.CurrentRevision(item).Parties
                .Where(party => party.HouseholdId is not null).Select(party => party.HouseholdId!))
            .Concat(filingHousehold is null ? [] : new[] { filingHousehold });
        var parties = householdIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => new TownLandCaseParty("household:" + id, "household", id, town.Id, HouseholdAdults(id)))
            .ToList();
        var townFiling = item?.Filings.LastOrDefault(filing => filing.AuthorityId is not null && filing.Kind != "expiry");
        if (townRepresentative is not null || townFiling is not null)
        {
            var representative = townRepresentative ?? (townFiling?.AgentId is { } filer &&
                LandHearingTownFilingAuthority(town, filer, townFiling.AuthorityId) ? filer : null);
            if (representative is null && town.Government is { Arrangement.Ordinary: TownArrangementRules.Mayor } government)
                representative = government.Offices.SingleOrDefault(office => office.Mandates == "ordinary" &&
                    office.HolderId is not null && office.TermEndTick > WorldTick)?.HolderId;
            parties.Add(new("town:" + town.Id, "town", null, town.Id, [], representative));
        }
        return parties.OrderBy(party => party.Id, StringComparer.Ordinal).ToArray();
    }

    private bool LandHearingTownFilingAuthority(TownRuntimeState town, string actor, string? authorityId)
    {
        if (!TownAdults(town).Contains(actor, StringComparer.Ordinal) || town.Government is not { } government || authorityId is null)
            return false;
        if (government.Arrangement.Ordinary == TownArrangementRules.Mayor)
            return government.Offices.Any(office => office.Mandates == "ordinary" && office.HolderId == actor &&
                office.TermEndTick > WorldTick && office.ElectionId == authorityId);
        return government.Arrangement.Ordinary is TownArrangementRules.Council or TownArrangementRules.ElectedCouncil or TownArrangementRules.AllAdultCouncil &&
            town.Governance is { } council && council.Members.Contains(actor, StringComparer.Ordinal) &&
            council.Proposals.Any(proposal => proposal.Id == authorityId && proposal.Kind == "land_hearing" &&
                proposal.Status == "passed" && proposal.AuthorId == actor);
    }

    private HashSet<string> LandHearingDirectStakes(TownLandCase item)
    {
        var tiles = TownLandHearingRules.CurrentRevision(item).Tiles;
        var agents = society.Checkpoint.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var buildings = worldSimulation.Buildings.Where(building => WorldContentSimulationRules.Footprint(
            worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building).Any(tiles.Contains)).ToArray();
        var buildingIds = buildings.Select(building => building.InstanceId).ToHashSet(StringComparer.Ordinal);
        var lots = society.Checkpoint.Inventory.Lots;
        var onsite = lots.Where(lot => lot.GroundPosition is { } ground && tiles.Contains(new GridPoint(ground.X, ground.Y)) ||
            lot.StorageBuildingId is { } storage && buildingIds.Contains(storage)).Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        while (lots.Any(lot => lot.ContainerLotId is { } parent && onsite.Contains(parent) && !onsite.Contains(lot.Id)))
            foreach (var lot in lots.Where(lot => lot.ContainerLotId is { } parent && onsite.Contains(parent))) onsite.Add(lot.Id);
        return item.Filings.Where(filing => filing.AgentId is not null).Select(filing => filing.AgentId!)
            .Concat(buildings.Where(building => building.HouseholdId is not null).SelectMany(building => HouseholdAdults(building.HouseholdId!)))
            .Concat(fields.Where(field => tiles.Contains(field.Position)).SelectMany(field => HouseholdAdults(field.HouseholdId)))
            .Concat(lots.Where(lot => onsite.Contains(lot.Id)).SelectMany(lot => agents.Contains(lot.OwnerId) ? new[] { lot.OwnerId } : HouseholdAdults(lot.OwnerId)))
            .ToHashSet(StringComparer.Ordinal);
    }

    private bool LandHearingJudgeConflict(TownRuntimeState town, TownLandCase item, string actor) =>
        TownLandHearingRules.JudgeConflict(actor, HouseholdFor(actor),
            LandHearingParties(town, TownLandHearingRules.CurrentRevision(item).Tiles, item), LandHearingDirectStakes(item));

    private bool LandHearingJudgeValid(TownRuntimeState town, TownLandCase item, TownLandCaseJudge judge)
    {
        if (!TownAdults(town).Contains(judge.AgentId, StringComparer.Ordinal) || LandHearingJudgeConflict(town, item, judge.AgentId) ||
            town.Government is not { } government || government.Arrangement.Land != TownArrangementRules.Mayor)
            return false;
        if (judge.Kind == "case_elected")
            return item.JudgeConsents.Any(consent => consent.AgentId == judge.AgentId && consent.WithdrawnTick is null) &&
                item.ContestHistory.Any(contest => contest.Id == judge.AuthorityId && contest.Stage == "completed" && contest.WinnerId == judge.AgentId);
        return judge.Kind == "land_mayor" && government.Offices.Any(office => office.Mandates == "land" &&
            office.HolderId == judge.AgentId && office.TermEndTick > WorldTick && office.ElectionId == judge.AuthorityId);
    }

    private bool LandHearingMayInspect(TownRuntimeState town, TownLandCase item, string actor) =>
        LandHearingAdult(actor) && (TownAdults(town).Contains(actor, StringComparer.Ordinal) || item.Judge?.AgentId == actor ||
            LandHearingParties(town, TownLandHearingRules.CurrentRevision(item).Tiles, item)
                .Any(party => party.AdultIds.Contains(actor, StringComparer.Ordinal) || party.RepresentativeId == actor));

    private bool LandHearingReadCurrent(TownLandCase item, string actor) => item.Reads.Any(read =>
        read.AgentId == actor && read.Revision == TownLandHearingRules.CurrentRevision(item).Number && read.ReadTick <= WorldTick &&
        item.Evidence.All(evidence => evidence.SubmittedTick <= read.ReadTick && read.EvidenceIds.Contains(evidence.Id, StringComparer.Ordinal)));

    private bool LandHearingReadRuling(TownLandCase item, string actor)
    {
        if (item.Rulings.Count == 0) return false;
        var ruling = item.Rulings[^1];
        return item.Reads.Any(read => read.AgentId == actor && read.Revision == TownLandHearingRules.CurrentRevision(item).Number &&
            read.ReadTick >= ruling.Tick && read.ReadTick <= WorldTick && item.Evidence.Any(evidence => evidence.Kind == "record" &&
                evidence.SourceRecordId == ruling.Id && read.EvidenceIds.Contains(evidence.Id, StringComparer.Ordinal)));
    }

    private bool MayVisitLandHearing(string actor, TownRuntimeState town) => LandHearingAdult(actor) &&
        town.Governance is { } council && CanWalkToCivicBoard(actor, town) && town.LandHearings.Cases.Any(item =>
            LandHearingMayInspect(town, item, actor) && TownLandHearingRules.HasNoticeReceipt(
                TownLandHearingRules.CurrentRevision(item), actor, WorldTick, council.Knowledge));

    private bool HasOpenLandHearingPlot(string townId, IEnumerable<GridPoint> tiles)
    {
        var plot = tiles.ToHashSet();
        return towns.SingleOrDefault(town => town.Id == townId)?.LandHearings?.Cases.Any(item =>
            item.Status == "pending" && TownLandHearingRules.CurrentRevision(item).Tiles.Any(plot.Contains)) == true;
    }

    private static bool TownLandHearingElectionBusy(TownRuntimeState town) =>
        town.LandHearings?.Cases.Any(item => item.Contest is { Stage: "voting" }) == true;
}
