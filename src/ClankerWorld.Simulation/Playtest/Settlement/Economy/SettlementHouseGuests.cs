using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Playtest;

public sealed record HouseGuestInvitation(
    string HouseInstanceId, string GuestId, string InvitedById, long ChangedTick, bool Active);

public sealed record BuildingAccessResult(bool Applied, string? Failure = null);

public sealed partial class PrivateWorldRuntime
{
    private const string InviteHouseGuestPrefix = "invite_house_guest:";
    private const string RevokeHouseGuestPrefix = "revoke_house_guest:";

    public BuildingAccessResult SetHouseGuestInvitation(string actor, string houseId, string guestId, bool invited)
    {
        gate.Wait();
        try { return SetHouseGuestInvitationCore(actor, houseId, guestId, invited); }
        finally { gate.Release(); }
    }

    private BuildingAccessResult SetHouseGuestInvitationCore(string actor, string houseId, string guestId, bool invited)
    {
        var house = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == houseId);
        if (!AdultResident(actor) || house?.HouseholdId is null ||
            house.HouseholdId != society.Checkpoint.GetInhabitant(actor).HouseholdId ||
            !worldContent.Buildings.Any(item => item.CanonicalId == house.DefinitionId &&
                item.Tags.Contains("house", StringComparer.Ordinal)))
            return new(false, "Only an adult member of the House's household can invite or revoke a guest.");
        if (!inhabitants.ContainsKey(guestId)) return new(false, "Choose a living named guest.");
        if (society.Checkpoint.GetInhabitant(guestId).HouseholdId == house.HouseholdId)
            return new(false, "Household members already have access to their own House.");
        var existing = (worldSimulation.GuestInvitations ?? []).SingleOrDefault(item =>
            item.HouseInstanceId == houseId && item.GuestId == guestId);
        if (existing?.Active == invited || existing is null && !invited) return new(true);
        var invitation = new HouseGuestInvitation(houseId, guestId, actor, WorldTick, invited);
        worldSimulation = worldSimulation with
        {
            GuestInvitations = (worldSimulation.GuestInvitations ?? []).Where(item =>
                    item.HouseInstanceId != houseId || item.GuestId != guestId).Append(invitation)
                .OrderBy(item => item.HouseInstanceId, StringComparer.Ordinal)
                .ThenBy(item => item.GuestId, StringComparer.Ordinal).ToArray(),
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent(invited ? "house_guest_invited" : "house_guest_revoked", $"{houseId}:{actor}:{guestId}");
        return new(true);
    }

    private bool HasHouseGuestInvitation(string actor, string houseId) =>
        (worldSimulation.GuestInvitations ?? []).Any(item =>
            item.HouseInstanceId == houseId && item.GuestId == actor && item.Active);

    private void AddHouseGuestCandidates(List<CognitionCandidate> candidates, string actor)
    {
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (household is null || HouseForHousehold(household) is not { } house) return;
        foreach (var guest in inhabitants.Values.Where(person => person.InhabitantId != actor &&
                     society.Checkpoint.GetInhabitant(person.InhabitantId).HouseholdId != household &&
                     !HasHouseGuestInvitation(person.InhabitantId, house.InstanceId) &&
                     IsWithinInteractionRange(inhabitants[actor].Position, person.Position, 2))
                 .OrderBy(person => person.InhabitantId, StringComparer.Ordinal).Take(4))
            candidates.Add(new(InviteHouseGuestPrefix + guest.InhabitantId,
                $"Invite {society.Checkpoint.GetInhabitant(guest.InhabitantId).Name} to shelter in the House during storms, without stock or cooking access.",
                110, guest.InhabitantId));
        foreach (var invitation in (worldSimulation.GuestInvitations ?? []).Where(item =>
                     item.HouseInstanceId == house.InstanceId && item.Active && inhabitants.ContainsKey(item.GuestId)))
            candidates.Add(new(RevokeHouseGuestPrefix + invitation.GuestId,
                $"End {society.Checkpoint.GetInhabitant(invitation.GuestId).Name}'s invitation to shelter in the House.", 110, invitation.GuestId));
    }
}
