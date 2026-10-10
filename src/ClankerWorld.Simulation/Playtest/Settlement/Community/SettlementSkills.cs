using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

public enum SettlementSkillKind
{
    Building,
    Farming,
    Crafting,
    Smithing,
}

/// <summary>A first learning event, independent of roles and work proficiency.</summary>
public sealed record SettlementSkill(SettlementSkillKind Kind, long LearnedTick, string? TeacherId = null);

public sealed partial class PrivateWorldRuntime
{
    private const int SkillsSchemaVersion = 31;
    // Provisional for playtesting. Round down to whole ticks, with at least one.
    private const int SkillWorkReductionPercent = 20;

    private int SkilledWorkTicks(string actor, SettlementSkillKind skill, int ticks) =>
        HasSkill(actor, skill) ? Math.Max(1, (int)((long)ticks * (100 - SkillWorkReductionPercent) / 100)) : ticks;

    private int SkilledWorkProgress(string actor, SettlementSkillKind skill, int done, int work)
    {
        if (!HasSkill(actor, skill)) return done + work;
        // Keep progress in its existing full-work units, including completion
        // receipts. Convert back with a ceiling so a whole tick always advances.
        var rate = 100 - SkillWorkReductionPercent;
        var elapsed = ((long)done * rate + 99) / 100;
        return (int)((elapsed + work) * 100 / rate);
    }

    private bool HasSkill(string actor, SettlementSkillKind skill) =>
        inhabitants.TryGetValue(actor, out var person) && person.Skills?.Any(item => item.Kind == skill) == true;

    private void GainSkill(string actor, SettlementSkillKind skill, string? teacher = null)
    {
        if (!inhabitants.TryGetValue(actor, out var person) || HasSkill(actor, skill)) return;
        inhabitants[actor] = person with
        {
            Skills = (person.Skills ?? []).Append(new SettlementSkill(skill, WorldTick, teacher))
                .OrderBy(item => item.Kind).ToArray(),
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("skill_learned", $"{actor}|{skill.ToString().ToLowerInvariant()}|{teacher ?? "work"}");
    }

    private SettlementSkillKind SkillForRecipe(RecipeDefinition recipe)
    {
        var workstation = worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == recipe.WorkstationBuildingId);
        if (recipe.IsCrop || workstation?.Tags.Contains("farmhouse", StringComparer.Ordinal) == true)
            return SettlementSkillKind.Farming;
        return workstation?.Tags.Contains("blacksmith", StringComparer.Ordinal) == true
            ? SettlementSkillKind.Smithing : SettlementSkillKind.Crafting;
    }

    private static void ValidateSkills(PlaytestInhabitantState person, int schema, long latestTick,
        HashSet<string> known)
    {
        if (person.Skills is not { } skills) return;
        if (schema < SkillsSchemaVersion || skills.Count > Enum.GetValues<SettlementSkillKind>().Length ||
            skills.Any(item => item is null) ||
            skills.Select(item => item.Kind).Distinct().Count() != skills.Count ||
            skills.Any(item => !Enum.IsDefined(item.Kind) || item.LearnedTick < 0 || item.LearnedTick > latestTick ||
                item.TeacherId is { } teacher && (teacher == person.InhabitantId || !known.Contains(teacher))))
            throw new InvalidDataException("The saved skills are invalid.");
    }
}
