using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using System.Diagnostics.CodeAnalysis;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Durable ownership proof, noticed consent and exact physical transfer receipts.</summary>
public static class TownPropertyValidation
{
    public static void ValidateFiles(TownLandHearingState state, string townId, long tick,
        IReadOnlySet<string> agents, IReadOnlySet<string> households, TownGovernanceState? council, TownGovernmentState? government)
    {
        foreach (var item in state.Cases)
        {
            Check((item.Kind == "property") == (item.Property is not null), "A property case must retain its asset and consent file.");
            if (item.Property is not { } property)
            {
                Check(item.Filings.All(filing => filing.RequestedOutcome.Kind is not ("reclaim" or "grant")), "An ordinary hearing cannot transfer physical property.");
                continue;
            }
            Check(TownPropertyRules.IsValid(property.Request) && households.Contains(property.Request.SourceHouseholdId ?? property.Request.TargetHouseholdId!) &&
                property.Snapshots is { Count: > 0 } && property.Snapshots.All(snapshot => snapshot is not null && snapshot.Building is not null &&
                    snapshot.SharedLots is not null && snapshot.SharedLots.All(lot => lot is not null) &&
                    snapshot.LivingFormerMemberIds is not null && snapshot.RecipientAdultIds is not null) && property.Consents is not null &&
                property.Consents.All(consent => consent is not null), "A property file needs its complete exact request, snapshots and personal choices.");
            Check(property.Snapshots.Select(snapshot => snapshot.Revision).SequenceEqual(Enumerable.Range(1, property.Snapshots.Count)) &&
                (property.Transfer is not null || property.Snapshots.Count == item.Revisions.Count), "Every pending property notice needs its own asset snapshot.");
            foreach (var snapshot in property.Snapshots)
            {
                var revision = item.Revisions.SingleOrDefault(revision => revision.Number == snapshot.Revision);
                var owner = property.Request.SourceHouseholdId ?? townId;
                Check(revision is not null && snapshot.Tick == revision.PublishedTick && snapshot.Tick <= tick &&
                    snapshot.Building.InstanceId == property.Request.BuildingId && snapshot.Building.TownId == townId &&
                    snapshot.Building.HouseholdId == property.Request.SourceHouseholdId &&
                    Canonical(snapshot.LivingFormerMemberIds) && snapshot.LivingFormerMemberIds.All(agents.Contains) &&
                    Canonical(snapshot.RecipientAdultIds) && snapshot.RecipientAdultIds.All(agents.Contains) &&
                    (property.Request.SourceHouseholdId is not null ? snapshot.RecipientAdultIds.Count == 0 :
                        snapshot.LivingFormerMemberIds.Count == 0 && snapshot.RecipientAdultIds.Count > 0), "A noticed property snapshot has inconsistent owners or claimants.");
                Check(Canonical(snapshot.SharedLots.Select(lot => lot.Id).ToArray()) && snapshot.SharedLots.All(lot =>
                    lot.OwnerId == owner && lot.Quantity > 0 && lot.CarrierId is null && lot.DeliveryBuildingId is null &&
                    lot.LastProcessedTick <= snapshot.Tick && lot.ConditionBasisPoints is >= 0 and <= 10_000 && lot.FreshnessBasisPoints is >= 0 and <= 10_000 &&
                    (lot.ContainerLotId is { } container ? snapshot.SharedLots.Any(root => root.Id == container && root.ContainerLotId is null) :
                        lot.StorageBuildingId == property.Request.BuildingId || lot.GroundPosition is { } ground && revision.Tiles.Contains(new(ground.X, ground.Y)))),
                    "A property file cannot include carried, personal, invented or off-plot shared goods.");
                var household = property.Request.SourceHouseholdId ?? property.Request.TargetHouseholdId;
                Check(revision.Parties.Any(party => party.HouseholdId == household &&
                    party.AdultIds.All(id => TownPropertyRules.RequiredConsent(snapshot).Contains(id, StringComparer.Ordinal))) &&
                    revision.Parties.Any(party => party.Kind == "town" && party.TownId == townId), "Property owners and the Town must be noticed parties.");
            }
            foreach (var consent in property.Consents)
            {
                var snapshot = property.Snapshots.SingleOrDefault(snapshot => snapshot.Revision == consent.Revision);
                var revision = item.Revisions.SingleOrDefault(revision => revision.Number == consent.Revision);
                Check(snapshot is not null && revision is not null && consent.Tick <= tick &&
                    TownPropertyRules.RequiredConsent(snapshot).Contains(consent.AgentId, StringComparer.Ordinal) &&
                    revision.Parties.Any(party => party.Kind == "household" && party.AdultIds.Contains(consent.AgentId, StringComparer.Ordinal)) &&
                    TownLandHearingRules.HasNoticeReceipt(revision, consent.AgentId, consent.Tick, council!.Knowledge) &&
                    item.Reads.Any(read => read.AgentId == consent.AgentId && read.Revision == consent.Revision && read.ReadTick <= consent.Tick &&
                        read.EvidenceIds.Any(id => item.Evidence.Any(evidence => evidence.Id == id && evidence.Kind == "record" &&
                            evidence.SourceRecordId == TownPropertyRules.RecordId(item, consent.Revision) &&
                            evidence.SourceVersion == TownLandHearingRules.RecordVersion(snapshot)))), "Property consent must follow the person's actual noticed asset-file read.");
            }
            Check(property.Consents.Select(consent => consent.Tick).SequenceEqual(property.Consents.Select(consent => consent.Tick).Order()),
                "Property choices must retain their actual chronological order.");
            foreach (var filing in item.Filings)
            {
                Check(filing.Kind == "town" && filing.RequestedOutcome == TownPropertyRules.Outcome(property.Request) &&
                    (council?.Proposals.Any(proposal => proposal.Id == filing.AuthorityId && proposal.AuthorId == filing.AgentId && proposal.Status == "passed" &&
                        proposal.LandHearingRequest is { } request && request.Property == property.Request && request.RequestedOutcome == filing.RequestedOutcome &&
                        request.Tiles.SequenceEqual(item.Revisions[0].Tiles)) == true ||
                    government?.Offices.Any(office => office.Mandates == "ordinary" && office.HolderId == filing.AgentId && office.ElectionId == filing.AuthorityId &&
                        office.TermStartTick <= filing.Tick && office.TermEndTick > filing.Tick) == true ||
                    government?.OfficeHistory.Any(office => office.Mandates == "ordinary" && office.HolderId == filing.AgentId && office.ElectionId == filing.AuthorityId &&
                        office.StartTick <= filing.Tick && office.EndTick >= filing.Tick) == true), "A property case must keep the actual Town filing authority.");
            }
            var transfers = item.Rulings.Where(ruling => ruling.Outcome.Kind is "reclaim" or "grant").ToArray();
            Check(transfers.Length == (property.Transfer is null ? 0 : 1), "Every physical ruling needs exactly its saved transfer receipt.");
            if (property.Transfer is not { } transfer) continue;
            var ruling = transfers.Single();
            var noticed = property.Snapshots.SingleOrDefault(snapshot => snapshot.Revision == transfer.Revision);
            Check(noticed is not null && transfer.RulingId == ruling.Id && transfer.Revision == ruling.Revision && transfer.Tick == ruling.Tick &&
                ruling.Outcome == TownPropertyRules.Outcome(property.Request) && transfer.PriorBuilding is not null && transfer.ResultBuilding is not null &&
                transfer.PriorLots is not null && transfer.ResultLots is not null && transfer.PriorLots.All(lot => lot is not null) && transfer.ResultLots.All(lot => lot is not null) &&
                TownPropertyRules.MaterialVersion(noticed) == TownPropertyRules.MaterialVersion(noticed with { Building = transfer.PriorBuilding, SharedLots = transfer.PriorLots }) &&
                transfer.ResultBuilding == (transfer.PriorBuilding with { HouseholdId = property.Request.TargetHouseholdId }), "A physical transfer must reproduce the exact noticed building and stock.");
            Check(ruling.EvidenceIds.Any(id => item.Evidence.Any(evidence => evidence.Id == id && evidence.Kind == "record" &&
                evidence.SourceRecordId == TownPropertyRules.RecordId(item, ruling.Revision) && evidence.SourceVersion == TownLandHearingRules.RecordVersion(noticed))),
                "A physical ruling must cite its actually inspected noticed asset record.");
            Check(TownPropertyRules.RequiredConsent(noticed).All(actor => property.Consents.LastOrDefault(consent =>
                consent.Revision == transfer.Revision && consent.AgentId == actor && consent.Tick <= ruling.Tick) is { Agreed: true }),
                "A physical ruling cannot omit a living owner's or receiving adult's personal agreement.");
            var recipient = property.Request.TargetHouseholdId ?? townId;
            var expected = transfer.PriorLots.Select(lot =>
            {
                var root = lot.ContainerLotId is { } container ? transfer.PriorLots.Single(parent => parent.Id == container) : lot;
                return lot with
                {
                    OwnerId = recipient,
                    CarrierId = null,
                    StorageBuildingId = root.StorageBuildingId,
                    DeliveryBuildingId = null,
                    GroundPosition = lot.ContainerLotId is null ? lot.GroundPosition : null
                };
            }).ToArray();
            Check(TownLandHearingRules.RecordVersion(expected) == TownLandHearingRules.RecordVersion(transfer.ResultLots),
                "Property transfer receipts must preserve stock identity, quantity, condition, contents and physical location.");
        }
    }

    public static void ValidateWorld(SeededMap map, SocietyCheckpoint society, IReadOnlyList<TownRuntimeState> towns,
        WorldContentSimulationState simulation, DeclarativeWorldContentState content)
    {
        foreach (var town in towns)
            foreach (var item in town.LandHearings.Cases.Where(item => item.Property is not null))
            {
                var property = item.Property!;
                foreach (var snapshot in property.Snapshots)
                {
                    var definition = content.Buildings.SingleOrDefault(definition => definition.CanonicalId == snapshot.Building.DefinitionId);
                    Check(definition is not null && WorldContentSimulationRules.Footprint(definition, snapshot.Building).OrderBy(tile => tile.Y).ThenBy(tile => tile.X)
                        .SequenceEqual(item.Revisions.Single(revision => revision.Number == snapshot.Revision).Tiles), "A property notice must cover its whole existing building footprint.");
                    if (property.Request.SourceHouseholdId is { } source)
                        Check(!TownPropertyRules.HadMembers(society, source, snapshot.Tick) &&
                            snapshot.LivingFormerMemberIds.SequenceEqual(TownPropertyRules.FormerMembers(society, source, snapshot.Tick)),
                            "A property case cannot forget living former household members.");
                }
                if (property.Request.SourceHouseholdId is null)
                    Check(town.LandHearings.Cases.Any(previous => previous.Id != item.Id && previous.Property?.Transfer is { ResultBuilding.HouseholdId: null } receipt &&
                        receipt.ResultBuilding.InstanceId == property.Request.BuildingId && receipt.Tick <= item.FiledTick),
                        "An onward grant needs the Town's prior recovery ruling.");
            }
    }

    public static HashSet<string> RecoveredBuildings(IReadOnlyList<TownRuntimeState>? towns) =>
        (towns ?? []).SelectMany(town => town?.LandHearings?.Cases ?? []).Where(item => item?.Property is { Transfer: not null, Request: not null })
            .Select(item => item.Property!.Request.BuildingId).ToHashSet(StringComparer.Ordinal);

    private static bool Canonical(IReadOnlyList<string> values) => values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).SequenceEqual(values);
    private static void Check([DoesNotReturnIf(false)] bool valid, string message)
    {
        if (!valid) throw new InvalidDataException(message);
    }
}
