using System.Text;

namespace ClankerWorld.Simulation.Society;

public sealed class InhabitantNameTakenException : InvalidOperationException
{
    public InhabitantNameTakenException()
        : base("That full name belongs to another agent. Choose a different name.") { }
}

/// <summary>The shared full-name comparison for player and model choices.</summary>
public static class InhabitantNameRules
{
    public static bool IsTaken(SocietyCheckpoint checkpoint, string inhabitantId, string name)
    {
        var chosenKey = CanonicalKey(name);
        return chosenKey is not null && checkpoint.Inhabitants
            .Where(other => other.Id != inhabitantId)
            .Select(other => CanonicalKey(other.Name))
            .Any(otherKey => otherKey is not null &&
                StringComparer.OrdinalIgnoreCase.Equals(otherKey, chosenKey));
    }

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
