using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed record SettlementParenthood(string PartnerId, string Stage, long RequestedTick,
    long LastTransitionTick, string? ChildId = null);

public sealed partial class PrivateWorldRuntime
{
    private static readonly string[] ChildNames = ["Ari", "Neri", "Lio", "Sage"];
    private const int IllnessCareCooldownTicks = 8;
    private static bool ActiveParenthood(SettlementParenthood? plan) => plan?.Stage is "requested" or "preparing";

    private bool RecentlyCaredForIllness(string dependent) => events.Any(item =>
        (item.Kind is "child_cared_for" or "dependent_cared_for") && item.Detail == dependent &&
        item.WorldTick > WorldTick - IllnessCareCooldownTicks);

    private bool Partners(string actor, string other) => AdultResident(actor) && AdultResident(other) && !CloseKin(actor, other) &&
        society.Checkpoint.GetInhabitant(actor).HouseholdId is { } household && society.Checkpoint.GetInhabitant(other).HouseholdId == household &&
        Partnerships(actor).Any(item => item.State == SocietyRelationshipState.Accepted &&
            (item.ProposerId == other || item.TargetId == other));

    private bool HasParenthoodDecision(string actor) => ReadyForLesson(actor) &&
        (inhabitants.Values.Any(person => person.Parenthood is { Stage: "requested" } plan && plan.PartnerId == actor) ||
         ChildrenNeedingCare(actor).Any());

    private IEnumerable<PlaytestInhabitantState> ChildrenNeedingCare(string actor) => inhabitants.Values.Where(person =>
        society.Checkpoint.GetInhabitant(person.InhabitantId).AgeBand == SocietyAgeBand.Infant &&
        (person.HungerBasisPoints < 7_000 || person.Survival is { WarmthBasisPoints: < 6_000 } ||
         person.Survival is { IllnessBasisPoints: >= 2_500 } && !RecentlyCaredForIllness(person.InhabitantId)) &&
        society.Checkpoint.Relationships.Any(item => item.Type == SocietyRelationshipType.Caregiver &&
            item.State == SocietyRelationshipState.Accepted && item.EffectiveTick <= WorldTick &&
            item.ProposerId == actor && item.TargetId == person.InhabitantId));

    private bool FamilyResourcesReady(string actor) =>
        society.Checkpoint.GetInhabitant(actor).HouseholdId is not null &&
        AccessibleShelters(actor).Any() &&
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == HouseholdFor(actor) && IsEdibleFood(lot.ItemKind))
            .Sum(AvailableLotQuantity) >= society.Checkpoint.Inhabitants.Count(person => person.HouseholdId == HouseholdFor(actor) &&
                person.Status == SocietyInhabitantStatus.Active) * 2 + 4 &&
        BirthFood(actor) is not null;

    private InventoryLot? BirthFood(string actor) => society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
        lot.OwnerId == HouseholdFor(actor) && IsEdibleFood(lot.ItemKind) && AvailableLotQuantity(lot) >= 4);

    private void AddParenthoodCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor) || !ReadyForBriefInteraction(actor) || survivalState is null)
        {
            return;
        }
        foreach (var child in ChildrenNeedingCare(actor).Where(child =>
                     !NeedsUrgentFood(inhabitants[actor]) || IsWithinInteractionRange(inhabitants[actor].Position, child.Position, ResourceInteractionRange)))
        {
            candidates.Add(new("care:" + child.InhabitantId, "Bring food and warmth to your dependent child.", 2));
        }
        if (!ReadyForLesson(actor)) return;
        foreach (var person in inhabitants.Values.Where(person => ActiveParenthood(person.Parenthood) &&
                     (person.InhabitantId == actor || person.Parenthood!.PartnerId == actor)))
        {
            if (person.Parenthood is { Stage: "requested" } && person.InhabitantId != actor)
            {
                if (FamilyResourcesReady(actor))
                {
                    candidates.Add(new("parent_accept:" + person.InhabitantId, "Agree to parenthood and caregiving with your partner; preparation takes time and needs food and shelter.", 25));
                }
                candidates.Add(new("parent_decline:" + person.InhabitantId, "Decline the parenthood request.", 70));
            }
            else
            {
                candidates.Add(new("parent_cancel:" + person.InhabitantId, "Withdraw consent before the planned birth.", 110));
            }
        }
        if (!FamilyResourcesReady(actor) || inhabitants.Values.Any(person => ActiveParenthood(person.Parenthood) &&
                (person.InhabitantId == actor || person.Parenthood!.PartnerId == actor)) ||
            society.Checkpoint.Relationships.Any(item => item.Type == SocietyRelationshipType.Caregiver && item.ProposerId == actor &&
                item.State == SocietyRelationshipState.Accepted && inhabitants.ContainsKey(item.TargetId) &&
                society.Checkpoint.GetInhabitant(item.TargetId).AgeBand is SocietyAgeBand.Infant or SocietyAgeBand.Child) ||
            inhabitants.Values.Any(person => person.Parenthood is { } previous &&
                (person.InhabitantId == actor || previous.PartnerId == actor) && WorldTick - previous.LastTransitionTick < worldSystems.Config.TicksPerDay))
        {
            return;
        }
        var partner = inhabitants.Keys.Order(StringComparer.Ordinal).FirstOrDefault(other => other != actor && Partners(actor, other) &&
            !inhabitants.Values.Any(person => ActiveParenthood(person.Parenthood) &&
                (person.InhabitantId == other || person.Parenthood!.PartnerId == other)));
        if (partner is not null)
        {
            candidates.Add(new("parent_propose:" + partner, "Ask your partner whether to raise a child together. Their independent consent is required.", 75));
        }
    }

    private void ApplyParenthoodCandidate(string actor, string candidate)
    {
        var target = candidate[(candidate.IndexOf(':', StringComparison.Ordinal) + 1)..];
        if (candidate.StartsWith("care:", StringComparison.Ordinal))
        {
            CareForChild(actor, target);
            return;
        }
        if (candidate.StartsWith("parent_propose:", StringComparison.Ordinal))
        {
            if (Partners(actor, target) && FamilyResourcesReady(actor) &&
                !inhabitants.Values.Any(person => ActiveParenthood(person.Parenthood) &&
                    (person.InhabitantId == actor || person.InhabitantId == target || person.Parenthood!.PartnerId == actor || person.Parenthood.PartnerId == target)))
            {
                SetParenthood(actor, new(target, "requested", WorldTick, WorldTick));
                checkpointSchemaVersion = StateSchemaVersion;
            }
            return;
        }
        if (!inhabitants.TryGetValue(target, out var owner) || owner.Parenthood is not { } plan ||
            !ActiveParenthood(plan) || (actor != target && actor != plan.PartnerId))
        {
            return;
        }
        if (candidate.StartsWith("parent_accept:", StringComparison.Ordinal) && actor == plan.PartnerId &&
            plan.Stage == "requested" && Partners(target, actor) && FamilyResourcesReady(actor))
        {
            SetParenthood(target, plan with { Stage = "preparing" });
        }
        else if (candidate.StartsWith("parent_cancel:", StringComparison.Ordinal) || candidate.StartsWith("parent_decline:", StringComparison.Ordinal))
        {
            SetParenthood(target, plan with { Stage = "cancelled" });
        }
    }

    private void MaintainParenthood()
    {
        foreach (var person in inhabitants.Values.OrderBy(person => person.Parenthood?.RequestedTick ?? long.MaxValue)
                     .ThenBy(person => person.InhabitantId, StringComparer.Ordinal).ToArray())
        {
            if (person.Parenthood is not { } plan || !ActiveParenthood(plan))
            {
                continue;
            }
            if (!Partners(person.InhabitantId, plan.PartnerId) ||
                WorldTick - plan.LastTransitionTick > (plan.Stage == "requested" ? 120 : 2_400))
            {
                SetParenthood(person.InhabitantId, plan with { Stage = "cancelled" });
                continue;
            }
            if (plan.Stage != "preparing" || WorldTick - plan.LastTransitionTick < 600 || !FamilyResourcesReady(person.InhabitantId) ||
                !ReadyForLesson(person.InhabitantId) || !ReadyForLesson(plan.PartnerId))
            {
                continue;
            }
            var shelter = AccessibleShelters(person.InhabitantId).First();
            var site = map.Tiles.Where(tile => map.IsBuildable(tile.Position) &&
                IsWithinInteractionRange(tile.Position, shelter.Position, ResourceInteractionRange) &&
                !inhabitants.Values.Any(resident => resident.Position == tile.Position)).Select(tile => (GridPoint?)tile.Position).FirstOrDefault();
            if (site is null)
            {
                continue;
            }
            var requestId = $"family:{person.InhabitantId}:{plan.RequestedTick}";
            society.Apply(checkpoint => SocietyFixture.CommitBirth(checkpoint,
                new(requestId, 1, person.InhabitantId, plan.PartnerId, HouseholdFor(person.InhabitantId),
                    [person.InhabitantId, plan.PartnerId], [person.InhabitantId, plan.PartnerId], BirthFood(person.InhabitantId)!.Id, 4, WorldTick,
                    ChildName: $"{ChildNames[society.Checkpoint.Births.Count % ChildNames.Length]} {society.Checkpoint.Births.Count + 1}")));
            var birth = society.Checkpoint.Births.FirstOrDefault(item => item.RequestId == requestId);
            if (birth is null)
            {
                continue;
            }
            inhabitants.Add(birth.ChildId, new(birth.ChildId, site.Value, 8_000, 0, "curious", "grow with the household",
                Survival: new SurvivalCondition()));
            if (TownForResident(person.InhabitantId) is { } parentTownId)
                AddTownResident(parentTownId, birth.ChildId, "child_joined");
            SetParenthood(person.InhabitantId, plan with { Stage = "completed", ChildId = birth.ChildId });
            AppendEvent("child_born", birth.ChildId);
        }
    }

    private void CareForChild(string actor, string childId)
    {
        if (!ReadyForBriefInteraction(actor) || !ChildrenNeedingCare(actor).Any(child => child.InhabitantId == childId))
        {
            return;
        }
        var parent = inhabitants[actor];
        var child = inhabitants[childId];
        if (NeedsUrgentFood(parent) && !IsWithinInteractionRange(parent.Position, child.Position, ResourceInteractionRange)) return;
        if (child.HungerBasisPoints < 7_000 && PreferredFood(actor, actor).FirstOrDefault() is null)
        {
            if (NeedsUrgentFood(parent)) return;
            if (PreferredFood(HouseholdFor(actor), actor).FirstOrDefault(lot =>
                    (lot.StorageBuildingId is null ||
                     society.Checkpoint.GetInhabitant(actor).HouseholdId == lot.OwnerId) &&
                    FindUnoccupiedRoute(actor, parent.Position, HouseholdStockPosition(lot),
                        HouseholdStockInteractionRange(lot)).Count > 0) is { } sharedFood)
            {
                var camp = HouseholdStockPosition(sharedFood);
                var interactionRange = HouseholdStockInteractionRange(sharedFood);
                if (!IsWithinInteractionRange(parent.Position, camp, interactionRange))
                {
                    MoveToward(actor, parent, camp, "care_food", interactionRange);
                    return;
                }
                ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"care-food:{WorldTick}:{actor}",
                    HouseholdFor(actor), actor, sharedFood.Id, 1, "caregiver_food"));
            }
            else
            {
                if (AvailableFoodSource(actor, parent.Position) is { } source)
                {
                    if (IsWithinInteractionRange(parent.Position, source.Position, ResourceInteractionRange))
                        HarvestFood(actor, parent);
                    else
                        MoveToward(actor, parent, source.Position, "care_food", ResourceInteractionRange);
                }
            }
            return;
        }
        if (!IsWithinInteractionRange(parent.Position, child.Position, ResourceInteractionRange))
        {
            MoveToward(actor, parent, child.Position, "care", ResourceInteractionRange);
            return;
        }
        if (child.HungerBasisPoints < 7_000 && PreferredFood(actor, actor).FirstOrDefault() is { } serving)
        {
            society.Apply(checkpoint => SocietyFixture.ConsumeInventory(checkpoint, actor, serving.Id, 1));
            child = child with { HungerBasisPoints = Math.Min(10_000, child.HungerBasisPoints + 3_000), Survival = AfterMeal(child, serving) };
        }
        child = child with
        {
            Survival = (child.Survival ?? new SurvivalCondition()) with
            {
                WarmthBasisPoints = Math.Min(10_000, (child.Survival?.WarmthBasisPoints ?? 10_000) + 1_000),
                IllnessBasisPoints = Math.Max(0, (child.Survival?.IllnessBasisPoints ?? 0) - IllnessCareReliefBasisPoints),
            }
        };
        inhabitants[childId] = child;
        AppendEvent("child_cared_for", childId);
    }

    private void SetParenthood(string owner, SettlementParenthood plan)
    {
        if (inhabitants[owner].Parenthood?.Stage != plan.Stage)
        {
            plan = plan with { LastTransitionTick = WorldTick };
            AppendEvent("parenthood_" + plan.Stage, owner);
        }
        inhabitants[owner] = inhabitants[owner] with { Parenthood = plan };
    }

    private static void ValidateParenthood(PrivateWorldRuntimeState state)
    {
        var known = state.Society.Society.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var participants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var person in state.Inhabitants)
        {
            if (person.Parenthood is not { } plan) continue;
            if (state.SchemaVersion < 9 || !known.Contains(plan.PartnerId) || plan.PartnerId == person.InhabitantId ||
                plan.Stage is not ("requested" or "preparing" or "completed" or "cancelled") || plan.RequestedTick < 0 ||
                plan.LastTransitionTick < plan.RequestedTick || plan.LastTransitionTick > state.Society.Society.WorldTick ||
                plan.Stage == "completed" && (plan.ChildId is null || !known.Contains(plan.ChildId) ||
                    !state.Society.Society.Births.Any(birth => birth.ChildId == plan.ChildId &&
                        birth.RequestId == $"family:{person.InhabitantId}:{plan.RequestedTick}")) ||
                plan.Stage != "completed" && plan.ChildId is not null ||
                ActiveParenthood(plan) && state.Society.Society.Births.Any(birth =>
                    birth.RequestId == $"family:{person.InhabitantId}:{plan.RequestedTick}") ||
                ActiveParenthood(plan) && (!participants.Add(person.InhabitantId) || !participants.Add(plan.PartnerId)))
            {
                throw new InvalidDataException("The saved parenthood plan is invalid.");
            }
        }
    }
}
