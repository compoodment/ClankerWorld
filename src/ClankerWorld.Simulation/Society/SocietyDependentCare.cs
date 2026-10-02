namespace ClankerWorld.Simulation.Society;

public static partial class SocietyFixture
{
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
