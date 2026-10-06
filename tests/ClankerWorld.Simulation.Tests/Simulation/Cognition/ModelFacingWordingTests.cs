namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Finds words the game no longer uses for its agents and places. A whole-word
/// match keeps legitimate words such as "campfire" from tripping a check.
/// </summary>
internal static class RetiredWording
{
    private static readonly string[] Words = ["inhabitants", "inhabitant", "settlements", "settlement", "camps", "camp"];

    public static string? Find(string text)
    {
        foreach (var word in Words)
        {
            for (var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); index >= 0;
                 index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase))
            {
                var end = index + word.Length;
                var startsWord = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                var endsWord = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (startsWord && endsWord)
                {
                    return word;
                }
            }
        }

        return null;
    }
}
