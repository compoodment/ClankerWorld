using System.Globalization;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// The low-population continuity rule. <paramref name="Active"/> records whether the rule was on at the
/// last check, so turning on or off is logged once; each eligible couple keeps the tick at which its
/// "not yet" ends and the child plan goes ahead.
/// </summary>
public sealed record SettlementContinuity(bool Active, IReadOnlyList<SettlementContinuityCouple> Couples);

/// <summary>An eligible couple, with partner IDs in ordinal order, and when its child plan goes ahead.</summary>
public sealed record SettlementContinuityCouple(string FirstPartnerId, string SecondPartnerId, long DeadlineTick);

public sealed partial class PrivateWorldRuntime
{
    // Provisional: four eligible couples retain the old eight-adult-partner threshold (#1266).
    private const int ContinuityEligibleCoupleThreshold = 4;
    private const int ContinuityPostponeDays = 2;

    private SettlementContinuity continuity = new(false, []);

    private long ContinuityPostponeTicks => ContinuityPostponeDays * (long)worldSystems.Config.TicksPerDay;

    private static string ContinuityDetail(int eligibleCouples) =>
        "eligible_couples|" + eligibleCouples.ToString(CultureInfo.InvariantCulture) +
        "|threshold|" + ContinuityEligibleCoupleThreshold.ToString(CultureInfo.InvariantCulture);

    /// <summary>Only accepted eligible partnerships count; unrelated single adults do not turn the rule off.</summary>
    private void StartContinuityRule()
    {
        var couples = EligibleContinuityCouples().Count();
        if (couples >= ContinuityEligibleCoupleThreshold) return;
        continuity = new(true, []);
        AppendEvent("continuity_rule_on", ContinuityDetail(couples));
    }

    private bool HasInfant(string parent) => society.Checkpoint.Relationships.Any(item =>
        item.Type is SocietyRelationshipType.BiologicalParentage or SocietyRelationshipType.Caregiver &&
        item.State == SocietyRelationshipState.Accepted && item.ProposerId == parent &&
        inhabitants.ContainsKey(item.TargetId) &&
        society.Checkpoint.GetInhabitant(item.TargetId).AgeBand == SocietyAgeBand.Infant);

    /// <summary>
    /// Living adult couples who could have children. Having an infant does not remove a partnership
    /// from the risk count; the no-infant check applies separately to holding a child plan.
    /// The rule only reads accepted partnerships;
    /// it never proposes or accepts one.
    /// </summary>
    private IEnumerable<(string First, string Second)> EligibleContinuityCouples() => society.Checkpoint.Relationships
        .Where(item => item.Type == SocietyRelationshipType.Partnership && item.State == SocietyRelationshipState.Accepted)
        .Select(item => string.CompareOrdinal(item.ProposerId, item.TargetId) < 0
            ? (First: item.ProposerId, Second: item.TargetId)
            : (First: item.TargetId, Second: item.ProposerId))
        .Where(pair => society.Checkpoint.GetInhabitant(pair.First).Status == SocietyInhabitantStatus.Active &&
            society.Checkpoint.GetInhabitant(pair.Second).Status == SocietyInhabitantStatus.Active && Partners(pair.First, pair.Second))
        .Distinct()
        .OrderBy(pair => pair.First, StringComparer.Ordinal)
        .ThenBy(pair => pair.Second, StringComparer.Ordinal);

    private SettlementContinuityCouple? ContinuityCoupleOf(string actor) => continuity.Couples.FirstOrDefault(item =>
        item.FirstPartnerId == actor || item.SecondPartnerId == actor);

    private SettlementContinuityCouple? ContinuityCoupleFor(string owner, string partner) =>
        ContinuityCoupleOf(owner) is { } couple && (couple.FirstPartnerId == partner || couple.SecondPartnerId == partner)
            ? couple : null;

    /// <summary>The couple may still say "not yet" to a plan; refusal is never offered while the rule holds them.</summary>
    private bool ContinuityAllowsPostponement(SettlementContinuityCouple couple) => WorldTick < couple.DeadlineTick;

    /// <summary>The couple's two days are up, so their plan goes ahead rather than being put off or expiring.</summary>
    private bool ContinuityPlanDue(string owner, string partner) =>
        ContinuityCoupleFor(owner, partner) is { } couple && !ContinuityAllowsPostponement(couple);

    private void MaintainContinuity()
    {
        var eligible = EligibleContinuityCouples().ToArray();
        var active = eligible.Length < ContinuityEligibleCoupleThreshold;
        if (active != continuity.Active)
            AppendEvent(active ? "continuity_rule_on" : "continuity_rule_off", ContinuityDetail(eligible.Length));
        if (!active)
        {
            continuity = new(false, []);
            return;
        }
        var couples = new List<SettlementContinuityCouple>();
        foreach (var (first, second) in eligible.Where(pair => !HasInfant(pair.First) && !HasInfant(pair.Second)))
        {
            var deadline = continuity.Couples.FirstOrDefault(item =>
                item.FirstPartnerId == first && item.SecondPartnerId == second)?.DeadlineTick ?? WorldTick + ContinuityPostponeTicks;
            couples.Add(new(first, second, deadline));
            if (WorldTick >= deadline) ProceedWithContinuityPlan(first, second);
        }
        continuity = new(true, couples);
    }

    /// <summary>Once "not yet" has run out, the couple's plan goes ahead as if both had agreed.</summary>
    private void ProceedWithContinuityPlan(string first, string second)
    {
        var active = inhabitants.Values.Where(person => person.Parenthood is { } plan && ActiveParenthood(plan) &&
            (person.InhabitantId == first || person.InhabitantId == second || plan.PartnerId == first || plan.PartnerId == second)).ToArray();
        if (active.Length > 1) return;
        string owner;
        if (active.Length == 1)
        {
            var plan = active[0].Parenthood!;
            owner = active[0].InhabitantId;
            if (plan.Stage != "requested" || plan.PartnerId != (owner == first ? second : first)) return;
            SetParenthood(owner, plan with { Stage = "preparing" });
        }
        else
        {
            // The partner who asked most recently initiates; otherwise the first by ID, as a stable tie-break.
            owner = new[] { first, second }
                .OrderByDescending(id => inhabitants[id].Parenthood is { } previous &&
                    previous.PartnerId == (id == first ? second : first) ? previous.RequestedTick : -1)
                .ThenBy(id => id, StringComparer.Ordinal).First();
            var partner = owner == first ? second : first;
            var previous = inhabitants[owner].Parenthood;
            SetParenthood(owner, previous is { Stage: "postponed" } && previous.PartnerId == partner
                ? previous with { Stage = "preparing" }
                : new(partner, "preparing", WorldTick, WorldTick,
                    PrimaryCaregiverId: owner, IntendedHouseholdId: HouseholdFor(owner)));
            checkpointSchemaVersion = StateSchemaVersion;
        }
        AppendEvent("continuity_plan_proceeded", owner);
    }

    /// <summary>What a partner's own model is told about the rule; null when it does not apply to them.</summary>
    private string? ContinuityNote(string actor)
    {
        if (ContinuityCoupleOf(actor) is not { } couple) return null;
        var preparing = inhabitants.Values.FirstOrDefault(person => person.Parenthood is { Stage: "preparing" } plan &&
            (person.InhabitantId == actor || plan.PartnerId == actor));
        if (preparing?.Parenthood?.PrimaryCaregiverId is { } caregiver)
        {
            var deadline = !ContinuityAllowsPostponement(couple) ? "Your two days are up. "
                : $"Your two days end in about {((couple.DeadlineTick - WorldTick) * 24 + worldSystems.Config.TicksPerDay - 1) / worldSystems.Config.TicksPerDay} hours. ";
            return "Continuity rule: you may put off a child for two days but may not refuse. " + deadline +
                "Preparing for parenthood. " + ParenthoodFoodNote(society.Checkpoint, caregiver,
                    society.Checkpoint.GetInhabitant(actor).HouseholdId == society.Checkpoint.GetInhabitant(caregiver).HouseholdId);
        }
        var rule = $"Continuity rule: fewer than {ContinuityEligibleCoupleThreshold} adult couples who are not close relatives can have children. " +
            "You may put off having a child for up to two days but may not refuse.";
        if (!ContinuityAllowsPostponement(couple))
            return rule + " Your two days are up, so your child plan is going ahead; the birth still needs food.";
        var ticksPerDay = worldSystems.Config.TicksPerDay;
        var hours = ((couple.DeadlineTick - WorldTick) * 24 + ticksPerDay - 1) / ticksPerDay;
        return rule + $" Your two days end in about {hours.ToString(CultureInfo.InvariantCulture)} hours; then your child plan goes ahead.";
    }

    private static void ValidateContinuity(SettlementContinuity? saved, SocietyCheckpoint society, int schemaVersion)
    {
        if (schemaVersion < ContinuitySchemaVersion || saved?.Couples is null)
            throw new InvalidDataException($"Private-world schema {ContinuitySchemaVersion} requires the continuity rule state.");
        var known = society.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var postponeTicks = ContinuityPostponeDays * (long)society.Config.TicksPerWorldDay;
        SettlementContinuityCouple? previous = null;
        foreach (var couple in saved.Couples)
        {
            if (!saved.Active || couple is null || !known.Contains(couple.FirstPartnerId) || !known.Contains(couple.SecondPartnerId) ||
                string.CompareOrdinal(couple.FirstPartnerId, couple.SecondPartnerId) >= 0 ||
                previous is not null && (string.CompareOrdinal(previous.FirstPartnerId, couple.FirstPartnerId) > 0 ||
                    previous.FirstPartnerId == couple.FirstPartnerId &&
                    string.CompareOrdinal(previous.SecondPartnerId, couple.SecondPartnerId) >= 0) ||
                couple.DeadlineTick < postponeTicks || couple.DeadlineTick > society.WorldTick + postponeTicks)
                throw new InvalidDataException("The saved continuity rule state is invalid.");
            previous = couple;
        }
    }
}
