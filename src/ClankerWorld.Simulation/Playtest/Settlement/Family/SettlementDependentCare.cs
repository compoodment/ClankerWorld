using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private bool NeedsCaregiver(string child) => inhabitants.ContainsKey(child) &&
        society.Checkpoint.GetInhabitant(child).AgeBand is SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent &&
        !society.Checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.State == SocietyRelationshipState.Accepted && edge.TargetId == child && inhabitants.ContainsKey(edge.ProposerId));

    private bool EligibleCaregiver(string adult, string child) => AdultResident(adult) && NeedsCaregiver(child) &&
        society.Checkpoint.GetInhabitant(adult).HouseholdId is { } household &&
        society.Checkpoint.GetInhabitant(child).HouseholdId == household;

    private IEnumerable<SocietyRelationship> CareProposals() => society.Checkpoint.Relationships.Where(edge =>
        edge.Type == SocietyRelationshipType.Caregiver && edge.State == SocietyRelationshipState.Proposed &&
        edge.Id.StartsWith("settlement-care:", StringComparison.Ordinal));

    private IEnumerable<string> IllDependentsNeedingCare(string adult) => society.Checkpoint.Relationships
        .Where(edge => edge.Type == SocietyRelationshipType.Caregiver && edge.ProposerId == adult &&
            edge.State == SocietyRelationshipState.Accepted && edge.EffectiveTick <= WorldTick &&
            inhabitants.TryGetValue(edge.TargetId, out var recipient) &&
            society.Checkpoint.GetInhabitant(edge.TargetId).AgeBand != SocietyAgeBand.Infant &&
            recipient.Survival is { IllnessBasisPoints: >= 2_500 } && !RecentlyCaredForIllness(edge.TargetId))
        .Select(edge => edge.TargetId);

    private bool CanOfferCare(string adult, string child) => EligibleCaregiver(adult, child) &&
        !CareProposals().Any(edge => edge.TargetId == child) &&
        !society.Checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId == adult && edge.TargetId == child && WorldTick - Math.Max(edge.ProposedTick, edge.EffectiveTick) < worldSystems.Config.TicksPerDay);

    private bool HasDependentCareDecision(string actor) => ReadyForBriefInteraction(actor) &&
        (CareProposals().Any(edge => edge.TargetId == actor) ||
         inhabitants.Keys.Any(child => CanOfferCare(actor, child)));

    private void MaintainDependentCare()
    {
        foreach (var edge in CareProposals().ToArray())
        {
            if (!EligibleCaregiver(edge.ProposerId, edge.TargetId) || WorldTick - edge.ProposedTick > 120)
            {
                society.Apply(checkpoint => SocietyFixture.RevokeRelationship(checkpoint, edge.Id, edge.ProposerId));
                AppendEvent("caregiver_proposal_expired", edge.TargetId);
            }
        }
    }

    private void AddDependentCareCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!ReadyForBriefInteraction(actor)) return;
        foreach (var edge in society.Checkpoint.Relationships.Where(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                     (edge.ProposerId == actor || edge.TargetId == actor) &&
                     (edge.State == SocietyRelationshipState.Accepted || edge.State == SocietyRelationshipState.Proposed && edge.ProposerId == actor)))
        {
            candidates.Add(new("guardian_end:" + edge.Id, "Withdraw from this caregiving relationship or proposal.", 110));
        }
        foreach (var dependent in IllDependentsNeedingCare(actor).Where(dependent =>
                     !NeedsUrgentFood(inhabitants[actor]) || IsWithinInteractionRange(inhabitants[actor].Position, inhabitants[dependent].Position, ResourceInteractionRange)))
        {
            candidates.Add(new("guardian_tend:" + dependent,
                "Offer warmth and practical care to your ill dependent.", 75));
        }
        foreach (var edge in CareProposals().Where(edge => edge.TargetId == actor && EligibleCaregiver(edge.ProposerId, actor)))
        {
            candidates.Add(new("guardian_accept:" + edge.Id, $"Accept care from {society.Checkpoint.GetInhabitant(edge.ProposerId).Name}.", 3));
            candidates.Add(new("guardian_refuse:" + edge.Id, "Refuse this caregiver proposal.", 70));
        }
        foreach (var child in inhabitants.Keys.Order(StringComparer.Ordinal).Where(child => CanOfferCare(actor, child)))
        {
            candidates.Add(new("guardian_offer:" + child,
                $"Offer to care for {society.Checkpoint.GetInhabitant(child).Name}, who has no surviving active caregiver. Older dependents may refuse.", 4));
        }
    }

    private void ApplyDependentCareCandidate(string actor, string candidate)
    {
        var target = candidate[(candidate.IndexOf(':', StringComparison.Ordinal) + 1)..];
        if (candidate.StartsWith("guardian_tend:", StringComparison.Ordinal))
        {
            TendToIllDependent(actor, target);
            return;
        }
        if (candidate.StartsWith("guardian_end:", StringComparison.Ordinal))
        {
            var existing = society.Checkpoint.Relationships.FirstOrDefault(edge => edge.Id == target &&
                edge.Type == SocietyRelationshipType.Caregiver && (edge.ProposerId == actor || edge.TargetId == actor) &&
                (edge.State == SocietyRelationshipState.Accepted || edge.State == SocietyRelationshipState.Proposed && edge.ProposerId == actor));
            if (existing is null) return;
            society.Apply(checkpoint => SocietyFixture.RevokeRelationship(checkpoint, target, actor));
            AppendEvent("caregiver_ended", actor);
            return;
        }
        if (candidate.StartsWith("guardian_offer:", StringComparison.Ordinal))
        {
            if (!CanOfferCare(actor, target)) return;
            if (society.Checkpoint.GetInhabitant(target).AgeBand == SocietyAgeBand.Infant)
            {
                var result = society.Apply(checkpoint => SocietyFixture.AssumeInfantCare(checkpoint, actor, target));
                if (result.CreatedId is not null) AppendEvent("caregiver_assigned", target);
            }
            else
            {
                society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
                    new($"settlement-care:{actor}:{target}:{WorldTick}", 1, SocietyRelationshipType.Caregiver,
                        actor, target, WorldTick, PrivacyClass: "public", HouseholdId: HouseholdFor(actor))));
                AppendEvent("caregiver_proposed", target);
            }
            return;
        }
        var edge = CareProposals().FirstOrDefault(edge => edge.Id == target && edge.TargetId == actor);
        if (edge is null || !EligibleCaregiver(edge.ProposerId, actor)) return;
        if (candidate.StartsWith("guardian_accept:", StringComparison.Ordinal))
        {
            society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint, edge.Id, edge.Revision, actor));
            AppendEvent("caregiver_accepted", actor);
        }
        else if (candidate.StartsWith("guardian_refuse:", StringComparison.Ordinal))
        {
            society.Apply(checkpoint => SocietyFixture.RefuseRelationship(checkpoint, edge.Id, edge.Revision, actor));
            AppendEvent("caregiver_refused", actor);
        }
    }

    private bool CanTendToIllDependent(string adult, string dependent) => AdultResident(adult) &&
        IllDependentsNeedingCare(adult).Contains(dependent, StringComparer.Ordinal);

    private void TendToIllDependent(string adult, string dependent)
    {
        if (!ReadyForBriefInteraction(adult) || !CanTendToIllDependent(adult, dependent)) return;
        var caregiver = inhabitants[adult];
        var recipient = inhabitants[dependent];
        if (NeedsUrgentFood(caregiver) && !IsWithinInteractionRange(caregiver.Position, recipient.Position, ResourceInteractionRange)) return;
        if (!IsWithinInteractionRange(caregiver.Position, recipient.Position, ResourceInteractionRange))
        {
            MoveToward(adult, caregiver, recipient.Position, "care", ResourceInteractionRange);
            return;
        }
        recipient = recipient with
        {
            Survival = recipient.Survival! with
            {
                WarmthBasisPoints = Math.Min(10_000, recipient.Survival.WarmthBasisPoints + 1_000),
                IllnessBasisPoints = Math.Max(0, recipient.Survival.IllnessBasisPoints - IllnessCareReliefBasisPoints),
            },
        };
        inhabitants[dependent] = recipient;
        AppendEvent("dependent_cared_for", dependent);
    }
}
