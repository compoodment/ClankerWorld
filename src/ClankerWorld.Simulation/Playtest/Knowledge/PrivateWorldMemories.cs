using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Content-free telemetry for an accepted Jev memory-index update.</summary>
public sealed record PrivateWorldMemoryCompactionTransition(
    long WorldTick,
    string OwnerId,
    int AssessedCount,
    int IndexSize);

public sealed partial class PrivateWorldRuntime
{
    private void ApplyMemorySummary(string ownerId, CognitionMemorySummaryOption summary)
    {
        // Recheck the source batch after an awaited reply: corrections or another
        // accepted summary may have changed it while the helper was working.
        var current = PrivateWorldMemoryRetrieval.SummaryOptions(society.Checkpoint, ownerId)
            .SingleOrDefault(option => option.Choice == summary.Choice && option.Text == summary.Text &&
                option.OwnerId == summary.OwnerId && option.Sources.SequenceEqual(summary.Sources));
        if (current is null) return;
        var sources = summary.Sources.Select(source => new SocietyMemorySummarySource(source.Id,
            source.Kind == "experience" ? SocietyMemorySourceKind.Experience : SocietyMemorySourceKind.Belief, source.SourceTick)).ToArray();
        var next = SocietyFixture.RecordAgentMemorySummary(society.Checkpoint, ownerId,
            new SocietyMemorySummaryOption(summary.Choice, summary.Text, sources));
        society.Apply(_ => new SocietyOperationResult(next));
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("agent_memories_summarized", ownerId + ":" + sources.Length);
    }

    private void ApplyMemoryCompaction(
        string ownerId,
        IReadOnlyList<CognitionMemoryCompactionScore> scores)
    {
        if (scores.Count == 0) return;
        var indexed = scores.Select(score => new SocietyAgentMemoryImportance(
                score.Id,
                score.Kind switch
                {
                    "experience" => SocietyMemorySourceKind.Experience,
                    "belief" => SocietyMemorySourceKind.Belief,
                    _ => throw new InvalidDataException("A memory compaction returned an unsupported source kind."),
                },
                score.SourceTick,
                score.ImportanceBasisPoints,
                score.ConfidenceBasisPoints,
                WorldTick))
            .ToArray();
        var next = SocietyFixture.RecordAgentMemoryCompaction(society.Checkpoint, ownerId, indexed);
        society.Apply(_ => new SocietyOperationResult(next));
        checkpointSchemaVersion = StateSchemaVersion;
        var retained = (society.Checkpoint.MemoryCompactions ?? [])
            .Single(item => item.OwnerId == ownerId).Sources.Count;
        memoryCompactionTransitions.Add(new PrivateWorldMemoryCompactionTransition(
            WorldTick, ownerId, indexed.Length, retained));
    }
}
