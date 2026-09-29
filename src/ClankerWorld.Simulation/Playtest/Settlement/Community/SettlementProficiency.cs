namespace ClankerWorld.Simulation.Playtest;

/// <summary>Bounded experience earned by committed useful work, not model claims.</summary>
public sealed record SettlementProficiency(int Building = 0, int Farming = 0, int Crafting = 0)
{
    public static int Level(int experience) => experience / 10;
}

public sealed partial class PrivateWorldRuntime
{
    private int ProjectPracticeBonus(PlaytestInhabitantState person, SettlementProject project)
    {
        var practice = person.Proficiency ?? new();
        var experience = project.CandidateId.StartsWith("build:building:", StringComparison.Ordinal)
            ? practice.Building
            : worldContent.Recipes.FirstOrDefault(recipe => "build:recipe:" + recipe.CanonicalId == project.CandidateId)?.IsCrop == true
                ? practice.Farming : practice.Crafting;
        return SettlementProficiency.Level(experience);
    }

    private void CreditCompletedWork(string actor, string domain)
    {
        if (!inhabitants.TryGetValue(actor, out var person)) return;
        var previous = person.Proficiency ?? new();
        var next = domain switch
        {
            "building" => previous with { Building = Math.Min(30, previous.Building + 1) },
            "farming" => previous with { Farming = Math.Min(30, previous.Farming + 1) },
            "crafting" => previous with { Crafting = Math.Min(30, previous.Crafting + 1) },
            _ => throw new InvalidOperationException("Unknown practice domain."),
        };
        if (next == previous) return;
        inhabitants[actor] = person with { Proficiency = next };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("work_practice_earned", actor);
    }

    private static void ValidateProficiency(PlaytestInhabitantState person, int schema)
    {
        if (person.Proficiency is { } practice && (schema < 11 ||
            practice.Building is < 0 or > 30 || practice.Farming is < 0 or > 30 || practice.Crafting is < 0 or > 30))
            throw new InvalidDataException("Work proficiency requires schema 11 and bounded experience.");
    }
}
