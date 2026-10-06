using System.Text;

namespace ClankerWorld.Simulation.Society;

public sealed class InhabitantNameTakenException : InvalidOperationException
{
    public InhabitantNameTakenException()
        : base("That first name belongs to another agent. Choose a different first name.") { }
}

/// <summary>The shared name comparisons for player and model choices.</summary>
public static class InhabitantNameRules
{
    public static bool IsTaken(SocietyCheckpoint checkpoint, string inhabitantId, string name)
    {
        var chosenKey = FirstNameKey(name);
        return chosenKey is not null && checkpoint.Inhabitants
            .Where(other => other.Id != inhabitantId && other.HasChosenName)
            .Select(other => FirstNameKey(other.Name))
            .Any(otherKey => otherKey is not null &&
                StringComparer.OrdinalIgnoreCase.Equals(otherKey, chosenKey));
    }

    public static string? FirstNameKey(string name)
    {
        var canonical = CanonicalKey(name);
        if (canonical is null) return null;
        var separator = canonical.IndexOf(' ', StringComparison.Ordinal);
        return separator < 0 ? canonical : canonical[..separator];
    }

    public static string? SurnameKey(string name)
    {
        var canonical = CanonicalKey(name);
        if (canonical is null) return null;
        var separator = canonical.LastIndexOf(' ');
        return separator < 0 ? null : canonical[(separator + 1)..];
    }

    public static bool RequiresParentSurname(SocietyCheckpoint checkpoint, string inhabitantId) =>
        checkpoint.GetInhabitant(inhabitantId).AgeBand is
            SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent &&
        BiologicalParents(checkpoint, inhabitantId).Any();

    public static IReadOnlyList<string> AllowedChildSurnames(SocietyCheckpoint checkpoint, string inhabitantId) =>
        ParentSurnames(BiologicalParents(checkpoint, inhabitantId));

    public static bool IsAllowedChildName(SocietyCheckpoint checkpoint, string inhabitantId, string name) =>
        !RequiresParentSurname(checkpoint, inhabitantId) ||
        SurnameKey(name) is { } surname &&
        AllowedChildSurnames(checkpoint, inhabitantId).Contains(surname, StringComparer.OrdinalIgnoreCase);

    public static bool HasParentSurname(string name, IReadOnlyList<SocietyInhabitant> parents) =>
        SurnameKey(name) is { } surname &&
        ParentSurnames(parents).Contains(surname, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<SocietyInhabitant> BiologicalParents(SocietyCheckpoint checkpoint, string inhabitantId) =>
        checkpoint.Relationships.Where(relationship =>
                relationship.Type == SocietyRelationshipType.BiologicalParentage &&
                relationship.State is SocietyRelationshipState.Accepted or SocietyRelationshipState.EndedByDeath &&
                relationship.TargetId == inhabitantId)
            .Select(relationship => checkpoint.GetInhabitant(relationship.ProposerId));

    private static string[] ParentSurnames(IEnumerable<SocietyInhabitant> parents) => parents
        .Where(parent => parent.HasChosenName)
        .Select(parent => SurnameKey(parent.Name))
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string? CanonicalKey(string name)
    {
        string normalized;
        try
        {
            normalized = name.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var result = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(rune.ToString());
        }

        return result.Length == 0 ? null : result.ToString();
    }
}
