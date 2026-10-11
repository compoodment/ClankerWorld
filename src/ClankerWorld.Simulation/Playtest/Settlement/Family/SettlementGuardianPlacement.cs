using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Accepted care whose dependent still needs a physical home and Town placement.</summary>
public sealed record SettlementGuardianPlacement(
    string CaregiverId,
    string CareRelationshipId,
    int CareRevision,
    long StartedTick,
    string Stage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DestinationHouseholdId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DestinationTownId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HouseId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HouseDefinitionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? HousePlacedTick = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GridPoint? HousePosition = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Blocker = null);

public sealed partial class PrivateWorldRuntime
{
    // Only the guardian-placement continuation consults this budget. Ordinary
    // movement retains its existing scheduling; even a cooldown wait spends
    // the pending dependent's movement opportunity for this tick.
    private readonly HashSet<string> guardianPlacementActions = new(StringComparer.Ordinal);

    private void BeginGuardianPlacement(string child, string caregiver, string careRelationshipId)
    {
        var person = society.Checkpoint.GetInhabitant(child);
        var adult = society.Checkpoint.GetInhabitant(caregiver);
        if (person.HouseholdId == adult.HouseholdId && TownForResident(child) == TownForResident(caregiver))
            return;
        var care = society.Checkpoint.GetRelationship(careRelationshipId);
        SetGuardianPlacement(child, new(caregiver, care.Id, care.Revision, WorldTick, "collecting"));
        RefreshGuardianPlacement(child);
        AppendEvent("guardian_placement_pending", child);
    }

    private void SetGuardianPlacement(string child, SettlementGuardianPlacement? placement)
    {
        if (!inhabitants.TryGetValue(child, out var physical) || physical.GuardianPlacement == placement) return;
        inhabitants[child] = physical with { GuardianPlacement = placement };
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private bool IsCurrentGuardianPlacement(string child, SettlementGuardianPlacement placement) =>
        inhabitants.ContainsKey(child) && inhabitants.ContainsKey(placement.CaregiverId) &&
        society.Checkpoint.Inhabitants.Any(person => person.Id == child &&
            person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand is SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent &&
            person.PrimaryCaregiverId == placement.CaregiverId) &&
        society.Checkpoint.Inhabitants.Any(person => person.Id == placement.CaregiverId &&
            person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder) &&
        society.Checkpoint.Relationships.Any(edge => edge.Id == placement.CareRelationshipId &&
            edge.Revision == placement.CareRevision && edge.Type == SocietyRelationshipType.Caregiver &&
            edge.State == SocietyRelationshipState.Accepted && edge.ProposerId == placement.CaregiverId && edge.TargetId == child);

    private void ReconcileGuardianPlacements()
    {
        foreach (var child in inhabitants.Values.Where(person => person.GuardianPlacement is not null)
                     .Select(person => person.InhabitantId).Order(StringComparer.Ordinal).ToArray())
            RefreshGuardianPlacement(child);
    }

    private void RefreshGuardianPlacement(string child)
    {
        if (!inhabitants.TryGetValue(child, out var physical) || physical.GuardianPlacement is not { } placement) return;
        if (!IsCurrentGuardianPlacement(child, placement))
        {
            SetGuardianPlacement(child, null);
            AppendEvent("guardian_placement_cancelled", child);
            return;
        }
        var household = society.Checkpoint.GetInhabitant(placement.CaregiverId).HouseholdId;
        var town = TownForResident(placement.CaregiverId);
        var house = household is null || town is null ? null : HouseForHousehold(household);
        var changedHome = placement.DestinationHouseholdId != household || placement.DestinationTownId != town ||
            placement.HouseId != house?.InstanceId || placement.HouseDefinitionId != house?.DefinitionId ||
            placement.HousePlacedTick != house?.PlacedTick || placement.HousePosition != house?.Position;
        var refreshed = placement with
        {
            DestinationHouseholdId = household,
            DestinationTownId = town,
            HouseId = house?.InstanceId,
            HouseDefinitionId = house?.DefinitionId,
            HousePlacedTick = house?.PlacedTick,
            HousePosition = house?.Position,
            Stage = changedHome || house is null ? "collecting" : placement.Stage,
        };
        var homeBlocker = GuardianPlacementHomeBlocker(child, refreshed);
        var activityBlocker = GuardianPlacementActivityBlocker(child, refreshed);
        refreshed = refreshed with
        {
            Blocker = homeBlocker ?? activityBlocker ??
                (changedHome || IsGuardianHomeBlocker(placement.Blocker) ? null : placement.Blocker),
        };
        SetGuardianPlacement(child, refreshed);
    }

    private static bool IsGuardianHomeBlocker(string? blocker) => blocker is not null &&
        (blocker.StartsWith("The guardian needs", StringComparison.Ordinal) ||
         blocker.StartsWith("The guardian's House", StringComparison.Ordinal) ||
         blocker.StartsWith("The guardian's home", StringComparison.Ordinal) ||
         blocker.StartsWith("The guardian is meeting", StringComparison.Ordinal) ||
         blocker.StartsWith("The guardian is finishing", StringComparison.Ordinal) ||
         blocker.StartsWith("The dependent is meeting", StringComparison.Ordinal));

    private string? GuardianPlacementActivityBlocker(string child, SettlementGuardianPlacement placement)
    {
        var adult = inhabitants[placement.CaregiverId];
        if (NeedsUrgentFood(adult) || NeedsUrgentWarmth(adult))
            return "The guardian is meeting urgent food or warmth needs.";
        if (PendingInstructionFor(placement.CaregiverId) is not null || IsConversationBusy(placement.CaregiverId))
            return "The guardian is finishing another activity.";
        var dependent = inhabitants[child];
        if (placement.Stage == "escorting" && (NeedsUrgentFood(dependent) || NeedsUrgentWarmth(dependent) ||
            PendingInstructionFor(child) is not null || IsConversationBusy(child)))
            return "The dependent is meeting needs or finishing another activity.";
        return null;
    }

    private string? GuardianPlacementHomeBlocker(string child, SettlementGuardianPlacement placement)
    {
        if (placement.DestinationHouseholdId is null) return "The guardian needs a household before the child can move in.";
        if (placement.DestinationTownId is null) return "The guardian needs a recorded Town membership before the child can move in.";
        if (placement.HouseId is null || placement.HousePosition is null)
            return "The guardian needs a completed House before the child can move in.";
        if (society.Checkpoint.GetInhabitant(placement.CaregiverId).HouseholdId != placement.DestinationHouseholdId ||
            TownForResident(placement.CaregiverId) != placement.DestinationTownId ||
            !worldSimulation.Buildings.Any(house => house.InstanceId == placement.HouseId &&
                house.DefinitionId == placement.HouseDefinitionId && house.HouseholdId == placement.DestinationHouseholdId &&
                house.PlacedTick == placement.HousePlacedTick && house.Position == placement.HousePosition))
            return "The guardian's home changed; the journey needs a current destination.";
        return CanFitGuardianHousehold(placement.DestinationHouseholdId, placement.CaregiverId, child)
            ? null : "The guardian's House needs a free resident place.";
    }

    private bool HasGuardianPlacementTask(string actor) =>
        inhabitants.TryGetValue(actor, out var person) && person.GuardianPlacement is not null ||
        inhabitants.Values.Any(person => person.GuardianPlacement?.CaregiverId == actor);

    private string? GuardianPlacementChildFor(string caregiver)
    {
        var placements = inhabitants.Values
            .Where(person => person.GuardianPlacement is { } placement && placement.CaregiverId == caregiver &&
                IsCurrentGuardianPlacement(person.InhabitantId, placement) && GuardianPlacementHomeBlocker(person.InhabitantId, placement) is null)
            .OrderBy(person => person.GuardianPlacement!.StartedTick).ThenBy(person => person.InhabitantId, StringComparer.Ordinal).ToArray();
        var origin = inhabitants[caregiver].Position;
        // A blocked collection must not starve another accepted child of a move.
        // Keep an escort together, and retry the oldest blocked collection when
        // no reachable collection remains so its normal blocker stays visible.
        return (placements.FirstOrDefault(person => person.GuardianPlacement!.Stage == "escorting") ??
            placements.FirstOrDefault(person => IsWithinInteractionRange(origin, person.Position, ResourceInteractionRange) ||
            FindUnoccupiedRoute(caregiver, origin, person.Position, ResourceInteractionRange).Count > 0) ??
            placements.FirstOrDefault())?.InhabitantId;
    }

    private CognitionCandidate? GuardianPlacementCandidate(string actor)
    {
        if (!inhabitants.TryGetValue(actor, out var person) || PendingInstructionFor(actor) is not null ||
            NeedsUrgentFood(person) || NeedsUrgentWarmth(person) || IsConversationBusy(actor)) return null;
        if (person.GuardianPlacement is { Stage: "escorting" } follow && IsCurrentGuardianPlacement(actor, follow) &&
            GuardianPlacementHomeBlocker(actor, follow) is null && PendingInstructionFor(follow.CaregiverId) is null &&
            !IsConversationBusy(follow.CaregiverId))
            return new("guardian_follow:" + follow.CaregiverId, "Follow your accepted guardian to their House.", 8, follow.HouseId);
        if (AdultResident(actor) && GuardianPlacementChildFor(actor) is { } child)
            return new("guardian_relocate:" + child,
                $"Collect {society.Checkpoint.GetInhabitant(child).Name} and accompany them to your House.", 8,
                inhabitants[child].GuardianPlacement!.HouseId);
        return null;
    }

    private bool TryApplyGuardianPlacementCandidate(string actor, string candidate)
    {
        if (candidate.StartsWith("guardian_relocate:", StringComparison.Ordinal))
        {
            var child = candidate[18..];
            RefreshGuardianPlacement(child);
            if (GuardianPlacementCandidate(actor)?.Id == candidate) EscortGuardianChild(actor, child);
            return true;
        }
        if (candidate.StartsWith("guardian_follow:", StringComparison.Ordinal))
        {
            RefreshGuardianPlacement(actor);
            if (GuardianPlacementCandidate(actor)?.Id == candidate) FollowPlacementGuardian(actor);
            return true;
        }
        return false;
    }

    private bool ContinueGuardianPlacement(string actor)
    {
        if (guardianPlacementActions.Contains(actor) || PendingInstructionFor(actor) is not null ||
            !HasGuardianPlacementTask(actor) || IsConversationBusy(actor)) return false;
        var person = inhabitants[actor];
        if (NeedsUrgentFood(person) || NeedsUrgentWarmth(person))
        {
            // Infants still depend on actual nearby care; never grant them an adult food action.
            if (society.Checkpoint.GetInhabitant(actor).AgeBand != SocietyAgeBand.Infant &&
                UrgentSurvivalCandidateFor(actor, person) is { } urgent)
                ApplyCandidate(actor, person, urgent.Id, reportIdle: false);
            return true;
        }
        if (GuardianPlacementCandidate(actor) is not { } candidate) return false;
        guardianPlacementActions.Add(actor);
        TryApplyGuardianPlacementCandidate(actor, candidate.Id);
        return true;
    }

    private void EscortGuardianChild(string caregiver, string child)
    {
        if (!inhabitants.TryGetValue(child, out var dependent) || dependent.GuardianPlacement is not { } placement ||
            placement.CaregiverId != caregiver || !IsCurrentGuardianPlacement(child, placement) ||
            GuardianPlacementHomeBlocker(child, placement) is not null) return;
        var adult = inhabitants[caregiver];
        if (placement.Stage == "collecting")
        {
            if (!IsWithinInteractionRange(adult.Position, dependent.Position, ResourceInteractionRange))
            {
                MoveForGuardianPlacement(caregiver, child, dependent.Position, "collect_guardian_child", ResourceInteractionRange);
                return;
            }
            placement = placement with { Stage = "escorting", Blocker = null };
            SetGuardianPlacement(child, placement);
        }
        if (PendingInstructionFor(child) is not null || IsConversationBusy(child) ||
            NeedsUrgentFood(dependent) || NeedsUrgentWarmth(dependent))
        {
            SetGuardianPlacement(child, placement with { Blocker = "The dependent is meeting needs or finishing another activity." });
            if (NeedsUrgentFood(dependent) || NeedsUrgentWarmth(dependent))
            {
                PauseGuardianPlacementWork(caregiver);
                if (dependent.HungerBasisPoints < 7_000 &&
                    CaregiverFoodCarryRequirement(caregiver, inhabitants[caregiver]) > FreeCarryCapacity(caregiver))
                    MakeRoomForFood(caregiver, inhabitants[caregiver]);
                else
                    CareForChild(caregiver, child);
            }
            return;
        }
        if (!IsWithinInteractionRange(adult.Position, dependent.Position, 2))
        {
            SetGuardianPlacement(child, placement with { Blocker = "The guardian is waiting for the child to catch up." });
            return;
        }
        MoveForGuardianPlacement(caregiver, child, placement.HousePosition!.Value, "escort_guardian_child");
    }

    private void MoveForGuardianPlacement(string actor, string child, GridPoint destination, string reason, int range = 0)
    {
        PauseGuardianPlacementWork(actor);
        var before = inhabitants[actor];
        MoveToward(actor, before, destination, reason, range);
        if (inhabitants[child].GuardianPlacement is { } placement)
            SetGuardianPlacement(child, placement with
            {
                Blocker = inhabitants[actor].MoveWaitTicks > before.MoveWaitTicks
                    ? "The journey is waiting for a clear walking route." : null,
            });
    }

    private void PauseGuardianPlacementWork(string actor)
    {
        // Release native field claims before leaving for the accepted care task.
        if (FarmWorkFor(actor) is { } field) CancelFarmWork(field);
        if (inhabitants[actor].Equipment?.Repair is not null) CancelEquipmentRepair(actor);
        if (inhabitants[actor].Project is { Stage: not ("completed" or "cancelled") } project)
            SetProject(actor, project with { Stage = "paused", Blocker = "Accompanying an accepted dependent." });
    }

    private void FollowPlacementGuardian(string child)
    {
        if (!inhabitants.TryGetValue(child, out var dependent) || dependent.GuardianPlacement is not { Stage: "escorting" } placement ||
            !IsCurrentGuardianPlacement(child, placement) || GuardianPlacementHomeBlocker(child, placement) is not null ||
            PendingInstructionFor(child) is not null || PendingInstructionFor(placement.CaregiverId) is not null ||
            IsConversationBusy(child) || IsConversationBusy(placement.CaregiverId) ||
            NeedsUrgentFood(dependent) || NeedsUrgentWarmth(dependent)) return;
        var adult = inhabitants[placement.CaregiverId];
        if (NeedsUrgentFood(adult) || NeedsUrgentWarmth(adult)) return;
        var home = placement.HousePosition!.Value;
        // Normal following stops beside the guardian; the final leg must enter
        // the actual House before household or Town membership changes.
        var destination = adult.Position == home ? home : adult.Position;
        MoveForGuardianPlacement(child, child, destination, "follow_guardian", adult.Position == home ? 0 : ResourceInteractionRange);
        CompleteGuardianPlacement(child);
    }

    private void AdvanceGuardianPlacementFollowers(IEnumerable<string> orderActors)
    {
        guardianPlacementActions.UnionWith(orderActors);
        foreach (var child in inhabitants.Values.Where(person => person.GuardianPlacement is { Stage: "escorting" })
                     .Select(person => person.InhabitantId).Order(StringComparer.Ordinal).ToArray())
        {
            if (!guardianPlacementActions.Contains(child)) FollowPlacementGuardian(child);
            CompleteGuardianPlacement(child);
        }
    }

    private void CompleteGuardianPlacement(string child)
    {
        if (!inhabitants.TryGetValue(child, out var dependent) || dependent.GuardianPlacement is not { Stage: "escorting" } placement ||
            !IsCurrentGuardianPlacement(child, placement) || GuardianPlacementHomeBlocker(child, placement) is not null ||
            dependent.Position != placement.HousePosition ||
            !IsWithinInteractionRange(dependent.Position, inhabitants[placement.CaregiverId].Position, ResourceInteractionRange)) return;
        var formerHousehold = society.Checkpoint.GetInhabitant(child).HouseholdId;
        var result = society.Apply(checkpoint => SocietyFixture.PlaceDependentWithGuardian(checkpoint,
            placement.CaregiverId, child, placement.CareRelationshipId, placement.CareRevision, placement.DestinationHouseholdId!));
        if (result.CreatedId != child) return;
        if (formerHousehold is not null && formerHousehold != placement.DestinationHouseholdId)
            RestoreHouseholdDeliveries(child, formerHousehold, "guardian-placement");
        if (!MoveDependentToGuardianTown(child, placement.CaregiverId))
            throw new InvalidOperationException("A validated guardian placement could not settle the child's Town.");
        SetGuardianPlacement(child, null);
        SetHousing(child, (inhabitants[child].Housing ?? new()) with { Blocker = HousingBlocker(child) });
        AppendEvent("guardian_placement_completed", $"{child}:{placement.CaregiverId}");
    }

    private bool CanEnterGuardianPlacementHouse(string child, PlacedBuilding house) =>
        inhabitants.TryGetValue(child, out var person) && person.GuardianPlacement is { Stage: "escorting" } placement &&
        IsCurrentGuardianPlacement(child, placement) && GuardianPlacementHomeBlocker(child, placement) is null &&
        house.InstanceId == placement.HouseId && house.DefinitionId == placement.HouseDefinitionId &&
        house.HouseholdId == placement.DestinationHouseholdId && house.PlacedTick == placement.HousePlacedTick &&
        house.Position == placement.HousePosition &&
        IsWithinInteractionRange(inhabitants[placement.CaregiverId].Position, house.Position, ResourceInteractionRange);

    private string? GuardianPlacementModelNote(string actor)
    {
        var child = inhabitants[actor].GuardianPlacement is not null ? actor : inhabitants.Values
            .Where(person => person.GuardianPlacement?.CaregiverId == actor)
            .OrderBy(person => person.InhabitantId, StringComparer.Ordinal).Select(person => person.InhabitantId).FirstOrDefault();
        if (child is null || inhabitants[child].GuardianPlacement is not { } placement) return HousingNote(actor);
        var stage = placement.Stage == "collecting" ? "collecting the child" : "travelling together";
        // The observation's housing slot is bounded. Keep the accepted task and
        // current blocker before ordinary housing detail and the repeated name.
        var note = $"Accepted guardian placement pending: {stage}. {placement.Blocker} {HousingNote(actor)} Child: {society.Checkpoint.GetInhabitant(child).Name}.".Trim();
        return NonviolentExcerpt(note, 256);
    }
}
