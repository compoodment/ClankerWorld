using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void ArchiveOldAgentMemories(IReadOnlySet<string> activeHostedIds)
    {
        // Once per unpaused world day; loading never runs the rule or resets its deadline.
        if (WorldTick % society.Checkpoint.Config.TicksPerWorldDay != 0) return;
        var protectedOwners = activeHostedIds.Concat(instructionsByIdempotency.Values
                .Where(item => item.Order is { } order && IsActiveOrder(order.Status))
                .Select(item => item.TargetInhabitantId))
            .Concat(inhabitants.Values.Where(person =>
                person.Project is { Stage: not ("completed" or "cancelled") } || ActiveLesson(person.Lesson) ||
                person.Parenthood is { Stage: not ("born" or "refused" or "cancelled") })
                .Select(person => person.InhabitantId))
            .Concat(inhabitants.Values.Where(person => ActiveLesson(person.Lesson))
                .Select(person => person.Lesson!.TeacherId))
            .Concat(society.Checkpoint.Relationships.Where(item => item.State == SocietyRelationshipState.Proposed)
                .SelectMany(item => new[] { item.ProposerId, item.TargetId }))
            .Concat(society.Checkpoint.Inventory.Offers.Where(item => item.State == DirectBarterState.Open)
                .SelectMany(item => new[] { item.FirstPartyId, item.SecondPartyId }))
            .Concat(conversations.Where(item => item.Status != AgentConversationStatus.Closed)
                .SelectMany(item => new[] { item.InitiatorId, item.InviteeId }))
            .ToHashSet(StringComparer.Ordinal);
        var protectedTurns = conversations.Where(item => item.Status != AgentConversationStatus.Closed ||
                item.Kind != AgentConversationKind.Ordinary)
            .SelectMany(item => item.Turns.Select(turn => turn.Id)).ToHashSet(StringComparer.Ordinal);
        var before = society.Checkpoint;
        var archived = SocietyMemoryArchiveRules.Archive(before, protectedOwners, protectedTurns);
        if (ReferenceEquals(before, archived)) return;
        society.Apply(_ => new SocietyOperationResult(archived));
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("agent_memories_archived", $"{archived.ArchivedMemories.Count - before.ArchivedMemories.Count}:{archived.ArchivedBeliefs.Count - before.ArchivedBeliefs.Count}");
    }
}
