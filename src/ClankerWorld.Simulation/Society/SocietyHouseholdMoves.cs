namespace ClankerWorld.Simulation.Society;

public static partial class SocietyFixture
{
    public static IReadOnlyList<string> MovingCareGroup(SocietyCheckpoint checkpoint, string adultId) =>
        checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
            (person.Id == adultId || IsYoungerDependent(person) && person.PrimaryCaregiverId == adultId))
            .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Membership ends together; location, ancestry, property and care stay intact.</summary>
    public static SocietyOperationResult LeaveHousehold(SocietyCheckpoint checkpoint, string adultId)
    {
        Validate(checkpoint);
        var adult = checkpoint.GetInhabitant(adultId);
        if (!IsAdult(adult) || adult.HouseholdId is not { } home)
            return Reject(checkpoint, "household_departure_rejected", $"{adultId}:not_an_adult_member");
        var group = MovingCareGroup(checkpoint, adultId).ToHashSet(StringComparer.Ordinal);
        if (checkpoint.Inhabitants.Any(person => group.Contains(person.Id) && person.HouseholdId != home))
            return Reject(checkpoint, "household_departure_rejected", $"{adultId}:care_group_not_together");
        var next = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => group.Contains(person.Id)
                ? person with { HouseholdId = null } : person).ToArray(),
            Households = checkpoint.Households.Select(household => household.Id == home
                ? household with
                {
                    MemberIds = household.MemberIds.Where(id => !group.Contains(id)).ToArray(),
                    CaregiverIds = household.CaregiverIds.Where(id => !group.Contains(id)).ToArray(),
                } : household).ToArray(),
            Relationships = checkpoint.Relationships.Select(edge => edge.Type == SocietyRelationshipType.HouseholdMembership &&
                edge.HouseholdId == home && group.Contains(edge.TargetId) && edge.State == SocietyRelationshipState.Accepted
                ? edge with { State = SocietyRelationshipState.Revoked, EffectiveTick = checkpoint.WorldTick } : edge).ToArray(),
        };
        return Commit(next, "household_left", $"{adultId}:{home}", adultId);
    }

    /// <summary>Admission consent and completed House capacity are checked by the runtime for the complete group.</summary>
    public static SocietyOperationResult JoinHouseholdCareGroup(SocietyCheckpoint checkpoint, string adultId, string householdId)
    {
        Validate(checkpoint);
        if (!IsAdult(checkpoint.GetInhabitant(adultId)))
            return Reject(checkpoint, "household_join_rejected", $"{adultId}:not_an_adult");
        var group = MovingCareGroup(checkpoint, adultId);
        if (group.Any(id => checkpoint.GetInhabitant(id).HouseholdId is not null))
            return Reject(checkpoint, "household_join_rejected", $"{adultId}:existing_household");
        var next = checkpoint;
        foreach (var id in group)
            next = JoinHousehold(next, id, householdId).Checkpoint;
        return Commit(next, "care_group_joined", $"{adultId}:{householdId}", adultId);
    }

    /// <summary>The replacement adult explicitly takes responsibility while the old caregiver is still alive.</summary>
    public static SocietyOperationResult AcceptReplacementCare(SocietyCheckpoint checkpoint, string adultId, string childId)
    {
        Validate(checkpoint);
        var adult = checkpoint.GetInhabitant(adultId);
        var child = checkpoint.GetInhabitant(childId);
        if (!IsAdult(adult) || !IsYoungerDependent(child) || child.Status != SocietyInhabitantStatus.Active ||
            adult.HouseholdId is null || adult.HouseholdId != child.HouseholdId || child.PrimaryCaregiverId == adultId)
            return Reject(checkpoint, "care_assignment_rejected", "replacement_not_eligible");
        var edgeId = $"replacement-care:{adultId}:{childId}:{checkpoint.WorldTick}";
        if (checkpoint.Relationships.Any(edge => edge.Id == edgeId))
            return Reject(checkpoint, "care_assignment_rejected", "duplicate_assignment");
        var edge = new SocietyRelationship(edgeId, 1, SocietyRelationshipType.Caregiver, adultId, childId,
            SocietyRelationshipState.Accepted, SocietyConsentState.Accepted, checkpoint.WorldTick,
            checkpoint.WorldTick, "public", adult.HouseholdId, [adultId]);
        var next = checkpoint with
        {
            Relationships = checkpoint.Relationships.Append(edge).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        next = SetPrimaryCaregiver(ApplyRelationshipProjection(next, edge), adultId, childId);
        return Commit(next, "replacement_care_accepted", $"{childId}:{adultId}", childId);
    }
}
