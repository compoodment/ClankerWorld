using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private bool AdultResident(string actor) => inhabitants.ContainsKey(actor) &&
        society.Checkpoint.GetInhabitant(actor).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder;

    private IEnumerable<SocietyRelationship> Partnerships(string actor) => society.Checkpoint.Relationships.Where(item =>
        item.Type == SocietyRelationshipType.Partnership && (item.ProposerId == actor || item.TargetId == actor));

    private bool AvailablePartner(string actor) => AdultResident(actor) &&
        !Partnerships(actor).Any(item => item.State is SocietyRelationshipState.Proposed or SocietyRelationshipState.Accepted);

    private bool HasFamilyDecision(string actor) => ReadyForLesson(actor) && Partnerships(actor).Any(item =>
        item.State == SocietyRelationshipState.Proposed && item.TargetId == actor);

    private bool CloseKin(string actor, string other)
    {
        var parentage = society.Checkpoint.Relationships.Where(item =>
            item.Type == SocietyRelationshipType.BiologicalParentage &&
            item.State is not (SocietyRelationshipState.Proposed or SocietyRelationshipState.Rejected)).ToArray();
        static bool HasAncestor(IReadOnlyList<SocietyRelationship> edges, string descendant, string ancestor)
        {
            var pending = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            pending.Push(descendant);
            while (pending.TryPop(out var current))
            {
                if (!visited.Add(current)) continue;
                foreach (var edge in edges.Where(edge => edge.TargetId == current))
                {
                    if (edge.ProposerId == ancestor) return true;
                    pending.Push(edge.ProposerId);
                }
            }
            return false;
        }
        return HasAncestor(parentage, actor, other) || HasAncestor(parentage, other, actor) ||
            parentage.Where(item => item.TargetId == actor).Select(item => item.ProposerId)
                .Intersect(parentage.Where(item => item.TargetId == other).Select(item => item.ProposerId), StringComparer.Ordinal).Any();
    }

    private bool CanProposePartnership(string actor, string other) => actor != other &&
        AvailablePartner(actor) && AvailablePartner(other) &&
        !CloseKin(actor, other) &&
        society.Checkpoint.GetInhabitant(actor).HouseholdId is { } household &&
        society.Checkpoint.GetInhabitant(other).HouseholdId == household &&
        !Partnerships(actor).Any(item => (item.ProposerId == other || item.TargetId == other) &&
            WorldTick - Math.Max(item.ProposedTick, item.EffectiveTick) < worldSystems.Config.TicksPerDay) &&
        TrustScore(actor, other) > 0;

    private void MaintainPartnerships()
    {
        foreach (var relationship in society.Checkpoint.Relationships.Where(item =>
                     item.Type == SocietyRelationshipType.Partnership && item.State == SocietyRelationshipState.Proposed).ToArray())
        {
            if (!AdultResident(relationship.ProposerId) || !AdultResident(relationship.TargetId) ||
                WorldTick - relationship.ProposedTick > 120)
            {
                society.Apply(checkpoint => SocietyFixture.RevokeRelationship(checkpoint, relationship.Id, relationship.ProposerId));
                AppendEvent("partnership_expired", relationship.ProposerId);
            }
        }
    }

    private void AddFamilyCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (survivalState is null || !AdultResident(actor) || !ReadyForLesson(actor))
        {
            return;
        }
        foreach (var relationship in Partnerships(actor).Where(item => item.State is SocietyRelationshipState.Proposed or SocietyRelationshipState.Accepted))
        {
            var other = relationship.ProposerId == actor ? relationship.TargetId : relationship.ProposerId;
            var name = society.Checkpoint.GetInhabitant(other).Name;
            if (relationship.State == SocietyRelationshipState.Proposed && relationship.TargetId == actor)
            {
                candidates.Add(new("partner_accept:" + relationship.Id, $"Accept {name}'s partnership proposal. This does not consent to parenthood.", 19));
                candidates.Add(new("partner_refuse:" + relationship.Id, $"Refuse {name}'s partnership proposal.", 70));
            }
            else
            {
                candidates.Add(new("partner_leave:" + relationship.Id, $"Withdraw from the partnership or proposal with {name}.", 110));
            }
        }
        foreach (var other in inhabitants.Keys.Order(StringComparer.Ordinal).Where(other => CanProposePartnership(actor, other)))
        {
            candidates.Add(new("partner_propose:" + other,
                $"Propose a partnership with {society.Checkpoint.GetInhabitant(other).Name} after cooperating; they may refuse.", 65));
        }
    }

    private void ApplyFamilyCandidate(string actor, string candidate)
    {
        var separator = candidate.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0 || !AdultResident(actor))
        {
            return;
        }
        var target = candidate[(separator + 1)..];
        if (candidate.StartsWith("partner_propose:", StringComparison.Ordinal))
        {
            if (CanProposePartnership(actor, target))
            {
                var id = $"settlement-partnership:{actor}:{target}:{WorldTick}";
                society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
                    new(id, 1, SocietyRelationshipType.Partnership, actor, target, WorldTick, PrivacyClass: "public")));
                AppendEvent("partnership_proposed", actor);
            }
            return;
        }
        var relationship = Partnerships(actor).FirstOrDefault(item => item.Id == target);
        if (relationship is null)
        {
            return;
        }
        if (candidate.StartsWith("partner_accept:", StringComparison.Ordinal) &&
            relationship.State == SocietyRelationshipState.Proposed && relationship.TargetId == actor &&
            AdultResident(relationship.ProposerId) && !CloseKin(actor, relationship.ProposerId))
        {
            society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint, target, relationship.Revision, actor));
            if (society.Checkpoint.GetRelationship(target).State == SocietyRelationshipState.Accepted)
            {
                AppendEvent("partnership_accepted", actor);
            }
        }
        else if (candidate.StartsWith("partner_refuse:", StringComparison.Ordinal) &&
            relationship.State == SocietyRelationshipState.Proposed && relationship.TargetId == actor)
        {
            society.Apply(checkpoint => SocietyFixture.RefuseRelationship(checkpoint, target, relationship.Revision, actor));
            AppendEvent("partnership_refused", actor);
        }
        else if (candidate.StartsWith("partner_leave:", StringComparison.Ordinal) &&
            relationship.State is SocietyRelationshipState.Proposed or SocietyRelationshipState.Accepted)
        {
            society.Apply(checkpoint => SocietyFixture.RevokeRelationship(checkpoint, target, actor));
            AppendEvent("partnership_ended", actor);
        }
    }
}
