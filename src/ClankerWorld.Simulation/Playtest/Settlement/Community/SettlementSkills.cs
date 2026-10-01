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
            skills.Select(item => item.Kind).Distinct().Count() != skills.Count ||
            skills.Any(item => !Enum.IsDefined(item.Kind) || item.LearnedTick < 0 || item.LearnedTick > latestTick ||
                item.TeacherId is { } teacher && (teacher == person.InhabitantId || !known.Contains(teacher))))
            throw new InvalidDataException("The saved skills are invalid.");
    }
}
