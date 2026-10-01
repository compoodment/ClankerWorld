namespace ClankerWorld.Simulation.Society;

public static partial class SocietyFixture
{
    /// <summary>
    /// Records an adult's explicit acceptance of primary care for a dependent.
    /// The caller may provide the caregiver's household only after checking its
    /// completed House capacity; Town membership is deliberately unchanged.
    /// </summary>
    public static SocietyOperationResult AcceptDependentGuardianship(
        SocietyCheckpoint checkpoint,
        string adultId,
        string childId,
        string? destinationHouseholdId = null)
    {
        Validate(checkpoint);
        var adult = checkpoint.GetInhabitant(adultId);
        var child = checkpoint.GetInhabitant(childId);
        if (adult.Status != SocietyInhabitantStatus.Active ||
            adult.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
            child.Status != SocietyInhabitantStatus.Active || !IsYoungerDependent(child) ||
            HasActivePrimaryCaregiver(checkpoint, childId) ||
            destinationHouseholdId is not null &&
                (destinationHouseholdId != adult.HouseholdId ||
                 !checkpoint.Households.Any(household => household.Id == destinationHouseholdId)))
            return Reject(checkpoint, "care_assignment_rejected", "ineligible_guardian_or_existing_caregiver");

        var caregiver = checkpoint.Relationships.FirstOrDefault(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId == adultId && edge.TargetId == childId && edge.State == SocietyRelationshipState.Accepted);
        var relationships = checkpoint.Relationships;
        if (caregiver is null)
        {
            var relationshipId = $"guardian-care:{childId}:{adultId}:{checkpoint.WorldTick}";
            if (checkpoint.Relationships.Any(edge => edge.Id == relationshipId))
                return Reject(checkpoint, "care_assignment_rejected", "duplicate_guardian_acceptance");
            caregiver = new SocietyRelationship(relationshipId, 1, SocietyRelationshipType.Caregiver,
                adultId, childId, SocietyRelationshipState.Accepted, SocietyConsentState.Accepted,
                checkpoint.WorldTick, checkpoint.WorldTick, "public", adult.HouseholdId, [adultId]);
            relationships = checkpoint.Relationships.Append(caregiver)
                .OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray();
        }

        var next = ApplyRelationshipProjection(checkpoint with { Relationships = relationships }, caregiver);
        next = SetPrimaryCaregiver(next, adultId, childId);
        if (destinationHouseholdId is { } newHome && newHome != child.HouseholdId)
        {
            var oldHome = child.HouseholdId is { } oldId
                ? next.Households.FirstOrDefault(household => household.Id == oldId)
                : null;
            var newHomeRecord = next.Households.FirstOrDefault(household => household.Id == newHome);
            if (newHomeRecord is null || child.HouseholdId is not null && oldHome is null)
                return Reject(checkpoint, "care_assignment_rejected", "missing_dependent_household");

            var membershipId = $"{newHome}:guardian-membership:{childId}:{checkpoint.WorldTick}";
            var suffix = 0;
            while (next.Relationships.Any(edge => edge.Id == membershipId))
                membershipId = $"{newHome}:guardian-membership:{childId}:{checkpoint.WorldTick}:{++suffix}";
            var membership = new SocietyRelationship(membershipId, 1, SocietyRelationshipType.HouseholdMembership,
                newHome, childId, SocietyRelationshipState.Accepted, SocietyConsentState.Accepted,
                checkpoint.WorldTick, checkpoint.WorldTick, "household", newHome,
                [adultId]);
            var movedRelationships = next.Relationships.Select(edge =>
                    edge.Type == SocietyRelationshipType.HouseholdMembership && edge.TargetId == childId &&
                    edge.State == SocietyRelationshipState.Accepted && edge.HouseholdId == child.HouseholdId
                        ? edge with
                        {
                            State = SocietyRelationshipState.Revoked,
                            Consent = SocietyConsentState.Revoked,
                            EffectiveTick = checkpoint.WorldTick
                        }
                        : edge)
                .Append(membership).OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray();
            next = next with
            {
                Inhabitants = next.Inhabitants.Select(person => person.Id == childId
                    ? person with { HouseholdId = newHome }
                    : person).OrderBy(person => person.Id, StringComparer.Ordinal).ToArray(),
                Households = next.Households.Select(household => household.Id == newHome
                        ? household with
                        {
                            MemberIds = household.MemberIds.Append(childId).Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal).ToArray()
                        }
                        : oldHome is not null && household.Id == oldHome.Id
                            ? household with { MemberIds = household.MemberIds.Where(id => id != childId).ToArray() }
                            : household)
                    .OrderBy(household => household.Id, StringComparer.Ordinal).ToArray(),
                Relationships = movedRelationships,
            };
        }

        return Commit(next, "dependent_guardian_accepted", $"{adultId}:{childId}", caregiver.Id);
    }

    public static bool HasActivePrimaryCaregiver(SocietyCheckpoint checkpoint, string childId)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(childId);
        var child = checkpoint.GetInhabitant(childId);
        if (child.PrimaryCaregiverId is not { } primaryCaregiverId ||
            !checkpoint.Inhabitants.Any(person => person.Id == primaryCaregiverId && IsAdult(person)))
            return false;
        return checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.State == SocietyRelationshipState.Accepted && edge.ProposerId == primaryCaregiverId && edge.TargetId == childId);
    }

    /// <summary>Protected care assignment for an infant, never consent on behalf of a capable dependent.</summary>
    public static SocietyOperationResult AssumeInfantCare(SocietyCheckpoint checkpoint, string adultId, string childId)
    {
        Validate(checkpoint);
        var adult = checkpoint.GetInhabitant(adultId);
        var child = checkpoint.GetInhabitant(childId);
        if (adult.Status != SocietyInhabitantStatus.Active || child.Status != SocietyInhabitantStatus.Active ||
            adult.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) || child.AgeBand != SocietyAgeBand.Infant ||
            adult.HouseholdId is null || adult.HouseholdId != child.HouseholdId ||
            HasActivePrimaryCaregiver(checkpoint, childId))
        {
            return Reject(checkpoint, "care_assignment_rejected", "ineligible_or_existing_caregiver");
        }
        if (checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                edge.ProposerId == adultId && edge.TargetId == childId && edge.State == SocietyRelationshipState.Accepted))
            return AssumePrimaryCare(checkpoint, adultId, childId);

        var id = $"dependent-care:{adultId}:{childId}:{checkpoint.WorldTick}";
        if (checkpoint.Relationships.Any(edge => edge.Id == id))
            return Reject(checkpoint, "care_assignment_rejected", "duplicate_assignment");
        var edge = new SocietyRelationship(id, 1, SocietyRelationshipType.Caregiver, adultId, childId,
            SocietyRelationshipState.Accepted, SocietyConsentState.ProtectedLifecycle,
            checkpoint.WorldTick, checked(checkpoint.WorldTick + 1), "public", adult.HouseholdId, [adultId]);
        var next = checkpoint with
        {
            Relationships = checkpoint.Relationships.Append(edge).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        return Commit(ApplyRelationshipProjection(next, edge), "dependent_care_assigned", id, id);
    }

    /// <summary>Promotes an already accepted caregiver only when the recorded primary is unavailable.</summary>
    public static SocietyOperationResult AssumePrimaryCare(SocietyCheckpoint checkpoint, string adultId, string childId)
    {
        Validate(checkpoint);
        var adult = checkpoint.GetInhabitant(adultId);
        var child = checkpoint.GetInhabitant(childId);
        if (!IsAdult(adult) || child.Status != SocietyInhabitantStatus.Active || !IsYoungerDependent(child) ||
            adult.HouseholdId is null || adult.HouseholdId != child.HouseholdId ||
            HasActivePrimaryCaregiver(checkpoint, childId) ||
            !checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                edge.ProposerId == adultId && edge.TargetId == childId && edge.State == SocietyRelationshipState.Accepted))
            return Reject(checkpoint, "care_assignment_rejected", "primary_caregiver_not_available_or_not_accepted");

        var next = SetPrimaryCaregiver(checkpoint, adultId, childId);
        return Commit(next, "primary_caregiver_assumed", $"{childId}:{adultId}", childId);
    }

    private static SocietyCheckpoint SetPrimaryCaregiver(SocietyCheckpoint checkpoint, string adultId, string childId)
    {
        var unitId = checkpoint.GetInhabitant(adultId).DomesticFamilyUnitId
            ?? throw new InvalidDataException("A primary caregiver needs a recorded domestic family unit.");
        return checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == childId
                ? person with { PrimaryCaregiverId = adultId, DomesticFamilyUnitId = unitId }
                : person).ToArray(),
        };
    }
}
