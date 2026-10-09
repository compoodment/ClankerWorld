using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Retrieves owner-private source records for a personal-model decision and
/// builds bounded Jev salience batches. Jev ranks existing records; the raw
/// source text and evidence links remain unchanged.
/// </summary>
internal static class PrivateWorldMemoryRetrieval
{
    private const int MaximumScanned = 256;
    private const int MaximumReturned = 4;
    private const int MaximumSummaryLength = 160;
    private const int MaximumCompactionCandidates = 12;
    private const int MinimumCompactionBatch = 4;

    internal static IReadOnlyList<CognitionMemoryExcerpt> Retrieve(
        IEnumerable<SocietySocialMemory> socialMemories,
        IEnumerable<SocietyAgentBelief> beliefs,
        SocietyAgentMemoryCompaction? compaction,
        string ownerId,
        long worldTick,
        IReadOnlyList<CognitionCandidate> candidates,
        SocietyCheckpoint? checkpoint = null)
    {
        var contextTerms = Terms(string.Join(' ', candidates.Select(candidate =>
            $"{candidate.Id} {candidate.Description} {candidate.DestinationId}")));
        var importance = (compaction?.Sources ?? [])
            .ToDictionary(item => $"{ToWireKind(item.Kind)}:{item.SourceId}", StringComparer.Ordinal);
        var summaryRecords = (checkpoint?.MemorySummaries ?? []).Where(item => item.OwnerId == ownerId)
            .Select(item => new SourceRecord(item.Id, ownerId, ownerId, item.Text, item.CreatedTick,
                "summary", "private", null, null, null, null,
                Attribution(checkpoint!, ownerId, item.Sources).Any(source => source.IsCorrected)));
        return summaryRecords.Concat(SourceRecords(socialMemories, beliefs, ownerId, worldTick))
            .Take(MaximumScanned)
            .Select(source =>
            {
                var assessment = importance.GetValueOrDefault($"{source.Kind}:{source.Id}");
                var overlap = Terms(source.Summary).Count(contextTerms.Contains);
                return new
                {
                    Source = source,
                    Importance = assessment?.ImportanceBasisPoints ?? 0,
                    ImportanceConfidence = assessment?.ImportanceConfidenceBasisPoints ?? 0,
                    Score = overlap * 3_000 + (assessment?.ImportanceBasisPoints ?? 0) / 5,
                };
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Importance)
            .ThenByDescending(item => item.Source.SourceTick)
            .ThenBy(item => item.Source.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Source.Id, StringComparer.Ordinal)
            .Take(MaximumReturned)
            .Select(item => new CognitionMemoryExcerpt(
                item.Source.Id,
                item.Source.OwnerId,
                item.Source.SubjectId,
                item.Source.Summary,
                item.Source.SourceTick,
                item.Source.Kind,
                item.Source.Visibility,
                item.Source.Provenance,
                item.Source.ConfidenceBasisPoints,
                item.Source.SourceAgentId,
                item.Source.SourceEventId,
                item.Source.IsCorrected,
                item.Importance,
                item.ImportanceConfidence,
                item.Source.Kind == "summary" ? Attribution(checkpoint!, ownerId,
                    checkpoint!.MemorySummaries.Single(summary => summary.Id == item.Source.Id).Sources) : null))
            .ToArray();
    }

    internal static IReadOnlyList<CognitionMemorySummaryOption> SummaryOptions(SocietyCheckpoint checkpoint, string ownerId) =>
        SocietyMemorySummaryRules.Options(checkpoint, ownerId).Select(option => new CognitionMemorySummaryOption(
            ownerId, option.Choice, option.Text, Attribution(checkpoint, ownerId, option.Sources))).ToArray();

    private static CognitionMemorySummarySource[] Attribution(SocietyCheckpoint checkpoint, string ownerId,
        IReadOnlyList<SocietyMemorySummarySource> sources)
    {
        var records = SourceRecords(checkpoint.AllMemories(), checkpoint.AllBeliefs(), ownerId, checkpoint.WorldTick)
            .ToDictionary(item => item.Kind + ":" + item.Id, StringComparer.Ordinal);
        // Tombstoned experiences remain evidence but must be marked corrected in later recall.
        return sources.Select(source =>
        {
            var kind = ToWireKind(source.Kind);
            if (records.TryGetValue(kind + ":" + source.Id, out var record))
                return new CognitionMemorySummarySource(source.Id, kind, source.SourceTick, record.SubjectId,
                    record.Provenance, record.ConfidenceBasisPoints, record.SourceAgentId, record.SourceEventId, record.IsCorrected);
            var memory = checkpoint.AllMemories().Single(item => item.OwnerId == ownerId && item.Id == source.Id);
            return new CognitionMemorySummarySource(source.Id, kind, source.SourceTick, CognitionAgentId(memory.SubjectId),
                null, null, null, null, true);
        }).ToArray();
    }

    internal static IReadOnlyList<CognitionMemoryCompactionCandidate> Unassessed(
        IEnumerable<SocietySocialMemory> socialMemories,
        IEnumerable<SocietyAgentBelief> beliefs,
        SocietyAgentMemoryCompaction? compaction,
        string ownerId,
        long worldTick)
    {
        var assessed = (compaction?.Sources ?? [])
            .Select(item => $"{ToWireKind(item.Kind)}:{item.SourceId}")
            .ToHashSet(StringComparer.Ordinal);
        var batch = SourceRecords(socialMemories, beliefs, ownerId, worldTick)
            // The persisted salience index is bounded to the latest 256
            // sources. Keep the candidate domain aligned with that retention
            // window so entries it cannot retain are not scored forever.
            .Take(MaximumScanned)
            .Where(source => !assessed.Contains($"{source.Kind}:{source.Id}"))
            .OrderBy(item => item.SourceTick)
            .ThenBy(item => KindOrder(item.Kind))
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(MaximumCompactionCandidates)
            .ToArray();
        if (batch.Length < MinimumCompactionBatch) return [];

        return batch.Select(source => new CognitionMemoryCompactionCandidate(
            source.Id,
            ownerId,
            source.Kind,
            source.SubjectId,
            source.Summary,
            source.SourceTick,
            source.Visibility,
            source.Provenance,
            source.ConfidenceBasisPoints,
            source.SourceAgentId,
            source.SourceEventId,
            source.IsCorrected)).ToArray();
    }

    private static IEnumerable<SourceRecord> SourceRecords(
        IEnumerable<SocietySocialMemory> socialMemories,
        IEnumerable<SocietyAgentBelief> beliefs,
        string ownerId,
        long worldTick)
    {
        var experiences = socialMemories
            .Where(memory => string.Equals(memory.OwnerId, ownerId, StringComparison.Ordinal) &&
                memory.TombstonedTick is null && memory.SourceTick >= 0 && memory.SourceTick <= worldTick &&
                IsValidId(memory.Id) && IsCanonicalId(memory.SubjectId) && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(memory => new SourceRecord(
                memory.Id,
                ownerId,
                CognitionAgentId(memory.SubjectId),
                Bounded(memory.Summary, MaximumSummaryLength),
                memory.SourceTick,
                "experience",
                BoundedOptional(memory.Visibility, 32),
                null,
                null,
                null,
                null,
                false));

        var privateBeliefs = beliefs
            .Where(belief => string.Equals(belief.OwnerId, ownerId, StringComparison.Ordinal) &&
                belief.FormedTick >= 0 && belief.FormedTick <= worldTick &&
                IsValidId(belief.Id) && !string.IsNullOrWhiteSpace(belief.Statement))
            .Select(belief => new SourceRecord(
                belief.Id,
                ownerId,
                CognitionAgentId(belief.AboutInhabitantId ?? ownerId),
                Bounded(belief.Statement, MaximumSummaryLength),
                belief.FormedTick,
                "belief",
                null,
                belief.Provenance.ToString().ToLowerInvariant(),
                belief.ConfidenceBasisPoints,
                belief.SourceAgentId is { } sourceAgentId ? CognitionAgentId(sourceAgentId) : null,
                belief.SourceEventId,
                belief.SupersededByBeliefId is not null));

        return experiences.Concat(privateBeliefs)
            .Where(item => item.Summary.Length > 0)
            .OrderByDescending(item => item.SourceTick)
            .ThenBy(item => KindOrder(item.Kind))
            .ThenBy(item => item.Id, StringComparer.Ordinal);
    }

    private static int KindOrder(string kind) => kind switch
    {
        "experience" => (int)SocietyMemorySourceKind.Experience,
        "belief" => (int)SocietyMemorySourceKind.Belief,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ToWireKind(SocietyMemorySourceKind kind) => kind switch
    {
        SocietyMemorySourceKind.Experience => "experience",
        SocietyMemorySourceKind.Belief => "belief",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool IsValidId(string? value) => value is { Length: <= 128 } && IsCanonicalId(value);

    private static bool IsCanonicalId(string? value) => value is { Length: > 0 } &&
        value == value.Trim() && !value.Any(char.IsControl);

    private static string CognitionAgentId(string agentId) => agentId.Length <= 128
        ? agentId
        : "agent-sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(agentId)));

    private static string? BoundedOptional(string? value, int limit) => value is null
        ? null
        : Bounded(value, limit);

    private static string Bounded(string value, int limit) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) is { } normalized
            ? normalized[..Math.Min(limit, normalized.Length)]
            : string.Empty;

    private static HashSet<string> Terms(string text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var part in term.Split([':', '_', '-', '.', ',', ';', '(', ')', '/', '\\'],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 3) words.Add(part.ToLowerInvariant());
            }
        }
        return words;
    }

    private sealed record SourceRecord(
        string Id,
        string OwnerId,
        string SubjectId,
        string Summary,
        long SourceTick,
        string Kind,
        string? Visibility,
        string? Provenance,
        int? ConfidenceBasisPoints,
        string? SourceAgentId,
        long? SourceEventId,
        bool IsCorrected);
}
