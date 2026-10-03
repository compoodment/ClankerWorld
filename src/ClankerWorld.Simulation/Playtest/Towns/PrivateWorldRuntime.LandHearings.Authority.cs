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
        string? townRepresentative = null) =>
        TownLandCasePartyRules.CurrentParties(town, tiles, householdLandUseRights, householdLandUseRequests,
            society.Checkpoint.Inhabitants, WorldTick, item, filingHousehold, townRepresentative);

    private bool LandHearingTownFilingAuthority(TownRuntimeState town, string actor, string? authorityId)
        => TownLandCasePartyRules.TownFilingAuthority(town, actor, authorityId, society.Checkpoint.Inhabitants, WorldTick);

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
