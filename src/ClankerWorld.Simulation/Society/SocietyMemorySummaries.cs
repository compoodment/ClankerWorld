using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClankerWorld.Simulation.Society;

public sealed record SocietyMemorySummarySource(string Id, SocietyMemorySourceKind Kind, long SourceTick);
public sealed record SocietyAgentMemorySummary(string Id, string OwnerId, string Choice, string Text,
    long CreatedTick, IReadOnlyList<SocietyMemorySummarySource> Sources);
public sealed record SocietyMemorySummaryOption(string Choice, string Text, IReadOnlyList<SocietyMemorySummarySource> Sources);

/// <summary>Extractive helper choices retain the complete private sources without adding world facts.</summary>
public static class SocietyMemorySummaryRules
{
    public const int MinimumSources = 4;
    public const int MaximumSources = 12;
    public const int MaximumTextLength = 160;
    private static readonly string[] Choices = ["recent", "earliest", "varied"];

    public static IReadOnlyList<SocietyMemorySummaryOption> Options(SocietyCheckpoint checkpoint, string owner)
    {
        var covered = checkpoint.MemorySummaries.Where(item => item.OwnerId == owner)
            .SelectMany(item => item.Sources).Select(Key).ToHashSet(StringComparer.Ordinal);
        var importance = (checkpoint.MemoryCompactions ?? []).SingleOrDefault(item => item.OwnerId == owner)?.Sources ?? [];
        var before = checkpoint.WorldTick - (long)SocietyMemoryArchiveRules.MinimumAgeDays * checkpoint.Config.TicksPerWorldDay;
        var archived = checkpoint.ArchivedMemories.Select(item => Key(new(item.Memory.Id, SocietyMemorySourceKind.Experience, item.Memory.SourceTick)))
            .Concat(checkpoint.ArchivedBeliefs.Select(item => Key(new(item.Belief.Id, SocietyMemorySourceKind.Belief, item.Belief.FormedTick))))
            .ToHashSet(StringComparer.Ordinal);
        var sources = Records(checkpoint, owner).Where(item => archived.Contains(Key(item.Source)) && item.Source.SourceTick <= before &&
                !covered.Contains(Key(item.Source)) && !item.Corrected &&
                (importance.SingleOrDefault(score => score.SourceId == item.Source.Id && score.Kind == item.Source.Kind)?.ImportanceBasisPoints ?? 0) <
                    SocietyMemoryArchiveRules.ImportanceCutoffBasisPoints)
            .OrderBy(item => item.Source.SourceTick).ThenBy(item => item.Source.Kind)
            .ThenBy(item => item.Source.Id, StringComparer.Ordinal).Take(MaximumSources).ToArray();
        if (sources.Length < MinimumSources) return [];
        var links = sources.Select(item => item.Source).OrderBy(item => item.Kind).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        return Choices.Select(choice =>
                new SocietyMemorySummaryOption(choice, Text(sources, choice), links))
            .Where(option => option.Text.Length < sources.Sum(source => source.Text.Length))
            .DistinctBy(option => option.Text, StringComparer.Ordinal).ToArray();
    }

    internal static string Id(string owner, IReadOnlyList<SocietyMemorySummarySource> sources) =>
        "memory-summary:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { owner, sources }))));

    internal static string Key(SocietyMemorySummarySource source) => $"{source.Kind}:{source.Id}";

    internal static string Text(IReadOnlyList<SourceRecord> sources, string choice)
    {
        var ordered = sources.OrderBy(source => source.Source.SourceTick).ThenBy(source => source.Source.Kind)
            .ThenBy(source => source.Source.Id, StringComparer.Ordinal).ToArray();
        var selected = choice switch
        {
            "recent" => ordered.TakeLast(2),
            "earliest" => ordered.Take(2),
            "varied" => new[] { ordered[0], ordered[^1] },
            _ => throw new InvalidDataException("Unknown private memory summary choice."),
        };
        return string.Join("; ", selected.Select(source => source.Label + ": " + Excerpt(source.Text)));
    }

    private static string Excerpt(string text)
    {
        var clean = string.Join(' ', new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length <= 48) return clean;
        var length = char.IsHighSurrogate(clean[46]) && char.IsLowSurrogate(clean[47]) ? 46 : 47;
        return clean[..length] + "…";
    }

    internal sealed record SourceRecord(SocietyMemorySummarySource Source, string Text, string Label, bool Corrected);

    internal static IEnumerable<SourceRecord> Records(SocietyCheckpoint checkpoint, string owner) =>
        checkpoint.AllMemories().Where(memory => memory.OwnerId == owner && !SocietyMemoryArchiveRules.IsPermanent(memory) &&
                memory.Id.Length <= 128 && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(memory => new SourceRecord(new(memory.Id, SocietyMemorySourceKind.Experience, memory.SourceTick), memory.Summary,
                "Memory", memory.TombstonedTick is not null))
            .Concat(checkpoint.AllBeliefs().Where(belief => belief.OwnerId == owner && belief.Kind == SocietyMemoryKind.Experience &&
                    belief.SourceEventId is null && belief.Id.Length <= 128 && !string.IsNullOrWhiteSpace(belief.Statement))
                .Select(belief => new SourceRecord(new(belief.Id, SocietyMemorySourceKind.Belief, belief.FormedTick), belief.Statement,
                    belief.Provenance switch { SocietyBeliefProvenance.Hearsay => "Hearsay", SocietyBeliefProvenance.Inference => "Inference", _ => "Firsthand belief" },
                    belief.SupersededByBeliefId is not null)));
}

public static partial class SocietyFixture
{
    public static SocietyCheckpoint RecordAgentMemorySummary(SocietyCheckpoint checkpoint, string owner, SocietyMemorySummaryOption option)
    {
        Validate(checkpoint);
        if (!checkpoint.Inhabitants.Any(person => person.Id == owner && person.Status == SocietyInhabitantStatus.Active) ||
            !SocietyMemorySummaryRules.Options(checkpoint, owner).Any(candidate => candidate.Choice == option.Choice &&
                candidate.Text == option.Text && candidate.Sources.SequenceEqual(option.Sources)))
            throw new InvalidOperationException("A helper may summarize only its requested current private source batch.");
        var summary = new SocietyAgentMemorySummary(SocietyMemorySummaryRules.Id(owner, option.Sources), owner,
            option.Choice, option.Text, checkpoint.WorldTick, option.Sources.ToArray());
        var result = checkpoint with
        {
            MemorySummaries = checkpoint.MemorySummaries.Append(summary)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()
        };
        Validate(result);
        return result;
    }

    private static void ValidateMemorySummaries(SocietyCheckpoint checkpoint)
    {
        if (checkpoint.MemorySummaries is null || checkpoint.MemorySummaries.Any(item => item is null))
            throw new InvalidDataException("Private memory summaries are missing or incomplete.");
        EnsureCanonicalIds(checkpoint.MemorySummaries.Select(item => item.Id), "private memory summaries");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var summary in checkpoint.MemorySummaries)
        {
            if (!checkpoint.Inhabitants.Any(person => person.Id == summary.OwnerId) || summary.Sources is null ||
                summary.Sources.Count is < SocietyMemorySummaryRules.MinimumSources or > SocietyMemorySummaryRules.MaximumSources ||
                summary.CreatedTick < 0 || summary.CreatedTick > checkpoint.WorldTick || summary.Sources.Any(source => source is null) ||
                summary.Id != SocietyMemorySummaryRules.Id(summary.OwnerId, summary.Sources) ||
                !summary.Sources.SequenceEqual(summary.Sources.OrderBy(source => source.Kind).ThenBy(source => source.Id, StringComparer.Ordinal)))
                throw new InvalidDataException("A private summary needs its canonical owner, time and exact sources.");
            var records = SocietyMemorySummaryRules.Records(checkpoint, summary.OwnerId).ToDictionary(source => SocietyMemorySummaryRules.Key(source.Source), StringComparer.Ordinal);
            var sources = new List<SocietyMemorySummaryRules.SourceRecord>();
            foreach (var source in summary.Sources)
            {
                if (!used.Add(summary.OwnerId + ":" + SocietyMemorySummaryRules.Key(source)) ||
                    !records.TryGetValue(SocietyMemorySummaryRules.Key(source), out var record) || record.Source != source ||
                    source.SourceTick > summary.CreatedTick - (long)SocietyMemoryArchiveRules.MinimumAgeDays * checkpoint.Config.TicksPerWorldDay)
                    throw new InvalidDataException("A private summary cannot invent, repeat or borrow another owner's source.");
                sources.Add(record);
            }
            if (summary.Text != SocietyMemorySummaryRules.Text(sources, summary.Choice) || summary.Text.Length > SocietyMemorySummaryRules.MaximumTextLength ||
                summary.Text.Length >= sources.Sum(source => source.Text.Length))
                throw new InvalidDataException("A private summary must preserve its shorter source-derived text.");
        }
    }
}
