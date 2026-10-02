using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// The body font draws the ellipsis character at mid-height, as Chinese
    /// text does, so everything on screen spells an ellipsis as three full stops.
    /// </summary>
    private void VerifyPlainEllipses(string when)
    {
        var ellipsis = GameUiText.Ellipsis;
        if (GameUiText.PlainEllipses($"Well{ellipsis} maybe{ellipsis}") != "Well... maybe...")
            throw new InvalidOperationException("Text from agents and the host must spell an ellipsis as three full stops.");

        // Agents' thoughts and speech reach the text panels as the model wrote them.
        var history = conversationHistoryText.Text;
        SetPanelText(conversationHistoryText, $"Ash · 08:00\nWell{ellipsis} maybe");
        var shown = conversationHistoryText.Text;
        SetPanelText(conversationHistoryText, history);
        if (shown != "Ash · 08:00\nWell... maybe")
            throw new InvalidOperationException($"Text panels must spell an agent's ellipsis as three full stops: {shown}.");

        // Long world and save names are shortened with three full stops, not Godot's ellipsis.
        if (FittedLabel.Shorten("A world with quite a long name", 60, text => text.Length * 6) != "A world..." ||
            FittedLabel.Shorten("Willowmere", 60, text => text.Length * 6) != "Willowmere")
            throw new InvalidOperationException("Names must be shortened to what fits, followed by three full stops.");
        var fitted = new FittedLabel { ThemeTypeVariation = "DimLabel" };
        AddChild(fitted);
        fitted.Size = new Vector2(120, 20);
        fitted.FullText = "A save with a name much too long for its card";
        var fittedWidth = fitted.GetThemeFont("font").GetStringSize(fitted.Text, HorizontalAlignment.Left, -1, fitted.GetThemeFontSize("font_size")).X;
        var fittedText = fitted.Text;
        RemoveChild(fitted);
        fitted.QueueFree();
        if (!fittedText.EndsWith("...", StringComparison.Ordinal) || fittedText.Length < 8 || fittedWidth > 120)
            throw new InvalidOperationException($"A long card name must be shortened to fit its width: '{fittedText}' is {fittedWidth} wide.");

        var found = new List<string>();
        foreach (var control in FindChildren("*", nameof(Control), recursive: true, owned: false).OfType<Control>())
        {
            IEnumerable<string> texts = control switch
            {
                Label label => [label.Text],
                RichTextLabel rich => [rich.GetParsedText()],
                LineEdit line => [line.Text, line.PlaceholderText],
                OptionButton option => Enumerable.Range(0, option.ItemCount).Select(option.GetItemText).Append(option.Text),
                Button button => [button.Text],
                ItemList list => Enumerable.Range(0, list.ItemCount).Select(list.GetItemText),
                _ => [],
            };
            foreach (var text in texts.Append(control.TooltipText))
                if (text.Contains(ellipsis, StringComparison.Ordinal))
                    found.Add($"{control.Name}: {text}");
            if (control is Label { TextOverrunBehavior: TextServer.OverrunBehavior.TrimEllipsis or TextServer.OverrunBehavior.TrimWordEllipsis })
                found.Add($"{control.Name} is shortened with Godot's ellipsis");
        }
        if (found.Count > 0)
            throw new InvalidOperationException($"Text must spell an ellipsis as three full stops {when}: {string.Join(" / ", found.Take(5))}.");
    }
}
