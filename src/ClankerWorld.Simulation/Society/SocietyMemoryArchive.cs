namespace ClankerWorld.Simulation.Society;

/// <summary>Fixed private-memory aging; it requires no helper or provider call.</summary>
public static class SocietyMemoryArchiveRules
{
    public const int MinimumAgeDays = 3;
    public const int ImportanceCutoffBasisPoints = 2_500;

    private static readonly string[] PermanentPrefixes = ["final-words:", "lesson-gratitude:", "project-gratitude:", "settlement-trust:", "rename:", "life-event:"];

    public static bool IsPermanent(SocietySocialMemory memory) => memory.Permanent || memory.Kind != SocietyMemoryKind.Experience ||
        PermanentPrefixes.Any(prefix => memory.Id.StartsWith(prefix, StringComparison.Ordinal));

    public static SocietyCheckpoint Archive(SocietyCheckpoint checkpoint, IReadOnlySet<string> protectedOwners,
        IReadOnlySet<string> protectedTurns)
    {
        SocietyFixture.Validate(checkpoint);
        var age = checked((long)MinimumAgeDays * checkpoint.Config.TicksPerWorldDay);
        var importance = (checkpoint.MemoryCompactions ?? []).SelectMany(owner => owner.Sources.Select(source =>
            (Key: (owner.OwnerId, source.Kind, source.SourceId), source.ImportanceBasisPoints)))
            .ToDictionary(item => item.Key, item => item.ImportanceBasisPoints);
        bool Eligible(string owner, string id, SocietyMemorySourceKind kind, long sourceTick) =>
            !protectedOwners.Contains(owner) && sourceTick >= 0 && sourceTick <= checkpoint.WorldTick - age &&
            importance.GetValueOrDefault((owner, kind, id)) < ImportanceCutoffBasisPoints;
        var owners = checkpoint.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var memories = checkpoint.Memories.Where(memory => !IsPermanent(memory) &&
                owners.Contains(memory.OwnerId) && owners.Contains(memory.SubjectId) &&
                !string.IsNullOrWhiteSpace(memory.Summary) && !string.IsNullOrWhiteSpace(memory.Visibility) &&
                Eligible(memory.OwnerId, memory.Id, SocietyMemorySourceKind.Experience, memory.SourceTick))
            .Select(memory => new SocietyArchivedMemory(memory, checkpoint.WorldTick)).ToArray();
        var beliefs = (checkpoint.Beliefs ?? []).Where(belief => belief.Kind == SocietyMemoryKind.Experience &&
                belief.SourceEventId is null && (belief.SourceTurnId is null || !protectedTurns.Contains(belief.SourceTurnId)) &&
                Eligible(belief.OwnerId, belief.Id, SocietyMemorySourceKind.Belief, belief.FormedTick))
            .Select(belief => new SocietyArchivedBelief(belief, checkpoint.WorldTick)).ToArray();
        if (memories.Length == 0 && beliefs.Length == 0) return checkpoint;
        var memoryIds = memories.Select(item => item.Memory.Id).ToHashSet(StringComparer.Ordinal);
        var beliefIds = beliefs.Select(item => item.Belief.Id).ToHashSet(StringComparer.Ordinal);
        var archived = checkpoint with
        {
            Memories = checkpoint.Memories.Where(item => !memoryIds.Contains(item.Id)).ToArray(),
            Beliefs = checkpoint.Beliefs is null ? null : checkpoint.Beliefs.Where(item => !beliefIds.Contains(item.Id)).ToArray(),
            ArchivedMemories = checkpoint.ArchivedMemories.Concat(memories).OrderBy(item => item.Memory.Id, StringComparer.Ordinal).ToArray(),
            ArchivedBeliefs = checkpoint.ArchivedBeliefs.Concat(beliefs).OrderBy(item => item.Belief.Id, StringComparer.Ordinal).ToArray(),
        };
        SocietyFixture.Validate(archived);
        return archived;
    }
}

public static partial class SocietyFixture
{
    private static void ValidateMemoryArchive(SocietyCheckpoint checkpoint)
    {
        if (checkpoint.ArchivedMemories is null || checkpoint.ArchivedBeliefs is null ||
            checkpoint.ArchivedMemories.Any(item => item is null || item.Memory is null) ||
            checkpoint.ArchivedBeliefs.Any(item => item is null || item.Belief is null))
            throw new InvalidDataException("An agent memory archive is missing or incomplete.");
        EnsureCanonicalIds(checkpoint.ArchivedMemories.Select(item => item.Memory.Id), "archived memories");
        EnsureCanonicalIds(checkpoint.ArchivedBeliefs.Select(item => item.Belief.Id), "archived beliefs");
        if (checkpoint.Memories.Any(item => !Enum.IsDefined(item.Kind)) ||
            checkpoint.AllMemories().Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() !=
                checkpoint.Memories.Count + checkpoint.ArchivedMemories.Count ||
            checkpoint.AllBeliefs().Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() !=
                (checkpoint.Beliefs?.Count ?? 0) + checkpoint.ArchivedBeliefs.Count)
            throw new InvalidDataException("An agent source must be stored exactly once with a known kind.");
        var owners = checkpoint.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in checkpoint.ArchivedMemories)
            if (SocietyMemoryArchiveRules.IsPermanent(item.Memory) || item.Memory.SourceTick < 0 ||
                item.ArchivedTick < item.Memory.SourceTick || item.ArchivedTick > checkpoint.WorldTick ||
                !owners.Contains(item.Memory.OwnerId) || !owners.Contains(item.Memory.SubjectId) ||
                string.IsNullOrWhiteSpace(item.Memory.Summary) || string.IsNullOrWhiteSpace(item.Memory.Visibility))
                throw new InvalidDataException("An archived experience is malformed or protected.");
        foreach (var item in checkpoint.ArchivedBeliefs)
            if (item.Belief.Kind != SocietyMemoryKind.Experience || item.Belief.SourceEventId is not null ||
                item.ArchivedTick < item.Belief.FormedTick || item.ArchivedTick > checkpoint.WorldTick)
                throw new InvalidDataException("An archived belief is malformed or protected.");
    }
}
