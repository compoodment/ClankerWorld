using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Bounded directed trust earned from committed cooperation.</summary>
public sealed record SettlementSocialStanding(string SubjectId, int Trust, long LastChangedTick);

public sealed partial class PrivateWorldRuntime
{
    private int TrustScore(string owner, string subject)
    {
        var saved = inhabitants[owner].SocialStanding?.FirstOrDefault(item => item.SubjectId == subject);
        if (saved is not null)
        {
            return saved.Trust;
        }

        // Older saves already contain durable cooperation memories. Preserve
        // their meaning until the next trusted event materializes schema 12.
        return Math.Min(10, society.Checkpoint.Memories.Where(memory =>
                memory.OwnerId == owner && memory.SubjectId == subject && memory.TombstonedTick is null)
            .Sum(memory => memory.Id.StartsWith("project-gratitude:", StringComparison.Ordinal) ? 2
                : memory.Id.StartsWith("lesson-gratitude:", StringComparison.Ordinal) ? 2
                : memory.Id.StartsWith("settlement-trust:", StringComparison.Ordinal) ? 1 : 0));
    }

    private void IncreaseTrust(string owner, string subject, int amount, string reason)
    {
        if (owner == subject || amount <= 0 || !inhabitants.TryGetValue(owner, out var person) || !inhabitants.ContainsKey(subject))
        {
            return;
        }

        var previous = TrustScore(owner, subject);
        var next = Math.Min(10, checked(previous + amount));
        if (next == previous)
        {
            return;
        }

        var standing = (person.SocialStanding ?? []).Where(item => item.SubjectId != subject)
            .Append(new SettlementSocialStanding(subject, next, WorldTick))
            .OrderBy(item => item.SubjectId, StringComparer.Ordinal).ToArray();
        inhabitants[owner] = person with { SocialStanding = standing };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("social_standing_changed", $"{owner}:{subject}:{reason}");
    }

    private static void ValidateSocialStanding(
        PlaytestInhabitantState person,
        IEnumerable<string> knownInhabitants,
        int schema,
        long worldTick)
    {
        if (person.SocialStanding is null)
        {
            return;
        }
        if (schema < 12)
        {
            throw new InvalidDataException("Social standing requires private-world schema 12.");
        }

        var known = knownInhabitants.ToHashSet(StringComparer.Ordinal);
        if (person.SocialStanding.Select(item => item.SubjectId).Distinct(StringComparer.Ordinal).Count() != person.SocialStanding.Count ||
            person.SocialStanding.Any(item => item.SubjectId == person.InhabitantId || !known.Contains(item.SubjectId) ||
                item.Trust is < 1 or > 10 || item.LastChangedTick < 0 || item.LastChangedTick > worldTick))
        {
            throw new InvalidDataException("The saved social standing is invalid.");
        }
    }
}
