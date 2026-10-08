using System.Globalization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Exact noticed assets and personal consent, separate from response waivers.</summary>
public static class TownPropertyRules
{
    public static bool IsValid(TownPropertyRequest request) => request is not null &&
        TownLandHearingRules.ValidText(request.BuildingId, 128) &&
        (request.SourceHouseholdId is not null ^ request.TargetHouseholdId is not null) &&
        TownLandHearingRules.ValidText(request.SourceHouseholdId ?? request.TargetHouseholdId, 128);

    public static TownLandRequestedOutcome Outcome(TownPropertyRequest request) =>
        request.SourceHouseholdId is { } source ? new("reclaim", source) : new("grant", request.TargetHouseholdId);

    public static string RecordId(TownLandCase item, int revision) => item.Id + ":property:" + revision.ToString(CultureInfo.InvariantCulture);

    // Freshness and condition age naturally while a hearing is pending. Changes to stock quantity,
    // custody, the actual building or the people whose consent is needed require a fresh notice.
    public static string MaterialVersion(TownPropertySnapshot snapshot) => TownLandHearingRules.RecordVersion(new
    {
        snapshot.Building,
        Lots = snapshot.SharedLots.Select(lot => new
        { lot.Id, lot.ItemKind, lot.OwnerId, lot.Quantity, lot.ProvenanceLotId, lot.StorageBuildingId,
            lot.DeliveryBuildingId, lot.ContainerLotId, lot.GroundPosition, lot.CarrierId }),
        snapshot.LivingFormerMemberIds,
        snapshot.RecipientAdultIds
    });

    public static string[] FormerMembers(SocietyCheckpoint society, string household, long tick) =>
        society.Relationships.Where(edge => edge.Type == SocietyRelationshipType.HouseholdMembership &&
                edge.HouseholdId == household && edge.ProposedTick <= tick &&
                (edge.Consent == SocietyConsentState.ProtectedLifecycle || edge.AcceptedParties.Contains(edge.TargetId, StringComparer.Ordinal)) &&
                edge.State is SocietyRelationshipState.Accepted or SocietyRelationshipState.Revoked or
                    SocietyRelationshipState.Dissolved or SocietyRelationshipState.EndedByDeath)
            .Select(edge => edge.TargetId)
            .Concat(society.Births.Where(birth => birth.HouseholdId == household && birth.CommittedTick <= tick).Select(birth => birth.ChildId))
            .Concat(society.Inhabitants.Where(person => person.HouseholdId == household && person.BirthTick <= tick).Select(person => person.Id))
            .Distinct(StringComparer.Ordinal).Where(id => society.Inhabitants.Any(person => person.Id == id &&
                (person.DeathTick is null || person.DeathTick > tick))).Order(StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<string> RequiredConsent(TownPropertySnapshot snapshot) =>
        snapshot.LivingFormerMemberIds.Concat(snapshot.RecipientAdultIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static TownLandCaseParty[] Parties(IEnumerable<TownLandCaseParty> parties, TownPropertyCase property,
        string townId, IReadOnlyList<SocietyInhabitant> inhabitants)
    {
        var household = property.Request.SourceHouseholdId ?? property.Request.TargetHouseholdId!;
        var snapshot = property.Snapshots[^1];
        var owners = property.Request.SourceHouseholdId is not null ? snapshot.LivingFormerMemberIds : snapshot.RecipientAdultIds;
        var adults = owners.Where(id => inhabitants.Any(person => person.Id == id && person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)).Order(StringComparer.Ordinal).ToArray();
        return parties.Where(party => party.HouseholdId != household)
            .Append(new("household:" + household, "household", household, townId, adults))
            .OrderBy(party => party.Id, StringComparer.Ordinal).ToArray();
    }

    public static bool HasAllConsent(TownLandCase item, long tick)
    {
        if (item.Property is not { Transfer: null } property) return false;
        var revision = TownLandHearingRules.CurrentRevision(item);
        var snapshot = property.Snapshots.SingleOrDefault(snapshot => snapshot.Revision == revision.Number);
        return snapshot is not null && RequiredConsent(snapshot).All(actor =>
            property.Consents.LastOrDefault(consent => consent.Revision == revision.Number && consent.AgentId == actor && consent.Tick <= tick) is { Agreed: true });
    }

    public static TownLandHearingState Consent(TownLandHearingState state, string caseId, string actor, bool agreed,
        long tick, IReadOnlyList<TownCivicReceipt> receipts, IReadOnlyList<TownLandCaseParty> parties)
    {
        var item = state.Cases.Single(item => item.Id == caseId);
        var revision = TownLandHearingRules.CurrentRevision(item);
        if (item.Status != "pending" || item.Property is not { Transfer: null } property ||
            !RequiredConsent(property.Snapshots.Single(snapshot => snapshot.Revision == revision.Number)).Contains(actor, StringComparer.Ordinal) ||
            !TownLandHearingRules.HasNoticeReceipt(revision, actor, tick, receipts) ||
            !item.Reads.Any(read => read.Revision == revision.Number && read.AgentId == actor && read.ReadTick <= tick &&
                read.EvidenceIds.Any(id => item.Evidence.Any(evidence => evidence.Id == id && evidence.Kind == "record" &&
                    evidence.SourceRecordId == RecordId(item, revision.Number) && evidence.SourceVersion ==
                    TownLandHearingRules.RecordVersion(property.Snapshots.Single(snapshot => snapshot.Revision == revision.Number))))))
            throw new InvalidOperationException("Property consent must be the noticed person's own informed choice.");
        var party = parties.Single(party => party.Kind == "household" && party.AdultIds.Contains(actor, StringComparer.Ordinal));
        state = TownLandHearingRules.Respond(state, caseId, revision.Number, actor, "answer",
            agreed ? "I personally agree to this exact property transfer." : "I refuse this property transfer.", tick, receipts, parties, party.Id);
        item = state.Cases.Single(item => item.Id == caseId);
        return Replace(state, item with { Property = property with { Consents = property.Consents.Append(new(revision.Number, actor, agreed, tick)).ToArray() } });
    }

    public static string? FilingRefusal(TownLandHearingState state, IReadOnlyList<GridPoint> tiles, TownPropertyCase property)
    {
        if (state.Cases.Any(item => item.Status == "pending" && TownLandHearingRules.CurrentRevision(item).Tiles.Any(tiles.Contains)))
            return "An existing hearing on this plot must finish first.";
        return state.Cases.Any(item => item.Status == "settled" && item.Property is { } old && old.Request == property.Request &&
            MaterialVersion(old.Snapshots[^1]) == MaterialVersion(property.Snapshots[^1]))
            ? "This property request was already heard; changed assets or new case evidence are needed." : null;
    }

    public static IReadOnlyList<HouseholdLandUseRight> BoundedRights(SeededMap map, IReadOnlyList<HouseholdLandUseRight> prior,
        IReadOnlyList<GridPoint> tiles, TownPropertyRequest request, string rulingId, long tick, string townId)
    {
        if (prior.Any(right => right.Tiles.Any(tiles.Contains) && right.HouseholdId != request.SourceHouseholdId))
            throw new InvalidOperationException("A property transfer cannot take another household's use permission.");
        var result = new List<HouseholdLandUseRight>();
        var sequence = 0;
        string NextId() => "household-use:hearing:" + TownLandHearingRules.Digest(rulingId + ":property:" + sequence++)[..24];
        foreach (var right in prior.OrderBy(right => right.Id, StringComparer.Ordinal))
            foreach (var piece in TownLandRightsRules.ConnectedPlots(map, right.Tiles.Where(tile => !tiles.Contains(tile))))
                result.Add(right with { Id = NextId(), Tiles = piece });
        if (request.TargetHouseholdId is { } target)
            result.Add(new(NextId(), townId, target, tiles, tick, "hearing:" + TownLandHearingRules.Digest(rulingId)[..32]));
        return result.OrderBy(right => right.Id, StringComparer.Ordinal).ToArray();
    }

    internal static TownLandHearingState Replace(TownLandHearingState state, TownLandCase item) =>
        state with { Cases = state.Cases.Select(current => current.Id == item.Id ? item : current).ToArray() };
}
