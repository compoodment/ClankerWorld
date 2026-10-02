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

    /// <summary>
    /// Agents' own words reach the drawn panels as their models wrote them:
    /// the Profile's newest thought and People notes, memory cards, World
    /// Info's council proposals, project blockers and social notes, Event Log
    /// rows, an agent-proposed add-on's name and status messages from the
    /// host. Each spells an ellipsis as three full stops.
    /// </summary>
    private void VerifyAgentTextEllipses()
    {
        if (renderedMapSnapshot is not { } shown)
            throw new InvalidOperationException("The agent text check needs a world on screen.");
        var ellipsis = GameUiText.Ellipsis;
        var ash = PanelSmokeAgent("ellipsis-ash", "Ash", new OwnerWorldPosition(1, 1)) with
        {
            RecentPrivateThoughts = [new OwnerWorldPrivateThought(1, $"Maybe{ellipsis} I should rest.")],
            RecentMemories = [new OwnerWorldAgentMemory(1, "ellipsis-ash", "Ash", $"The river was{ellipsis} cold.", "private")],
            SocialNotes = [$"Talked with Rowan{ellipsis} briefly."],
            Project = new OwnerWorldProject("Kiln", "building", 1, 10, $"Waiting for clay{ellipsis}", 0),
        };
        var snapshot = shown with { Inhabitants = [ash] };
        var council = new OwnerTownGovernance("assembly", "none", ["Ash"], null, 0, [],
            [new OwnerCivicProposal("ellipsis-proposal", "law", $"Share the harvest{ellipsis}", "passed", 1, 0, 1, 0)], null);
        var selected = selectedInhabitantId;
        // An Event Log row looks its event up for the icon; use an id no real event has.
        const long EventId = -2026;
        knownEvents[EventId] = new OwnerWorldEvent(EventId, 1, "inhabitant_spoke", ash.Id, null);
        try
        {
            selectedInhabitantId = ash.Id;
            RenderSelectedInhabitantCard(snapshot);
            RenderTownExtras(snapshot);
            RenderEventRows([(EventId, false, "08:00", $"Ash said{ellipsis} hello")], false);
            RenderModLibrary(snapshot with
            {
                ContentPackages = [new OwnerWorldContentPackage("ellipsis.pottery", "1.0.0", "sha256:package", "active",
                    null, null, null, 1, "sha256:manifest", $"River{ellipsis} pottery", ash.Id)],
            });
            SetStatus($"Saved{ellipsis} nearly", good: true);
            var texts = new Dictionary<string, string>
            {
                ["thought"] = privateThoughtHistory.Text,
                ["memory"] = MemoryCardsText(),
                ["People"] = ProfilePeopleText(),
                ["Town details"] = PageText(townExtras),
                ["Mod Library"] = ModLibraryText(),
                ["status"] = statusLabel.Text,
                ["Event Log"] = string.Join("\n", eventRows.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text)),
                ["council"] = TownCivicText(shown.Towns.Count > 0 ? shown.Towns[0] with { Governance = council } :
                    new OwnerWorldTown("town:ellipsis", "Ellipsis", "founded", 0, [], [], []) { Governance = council }, 0),
            };
            var wrong = texts.Where(pair => pair.Value.Contains(ellipsis, StringComparison.Ordinal) || !pair.Value.Contains("...", StringComparison.Ordinal))
                .Select(pair => $"{pair.Key}: {pair.Value.ReplaceLineEndings(" / ")}").ToArray();
            if (wrong.Length > 0)
                throw new InvalidOperationException($"Agents' words must spell an ellipsis as three full stops on every panel: {string.Join(" | ", wrong)}");
        }
        finally
        {
            knownEvents.Remove(EventId);
            selectedInhabitantId = selected;
            RenderSelectedInhabitantCard(shown);
            RenderTownExtras(shown);
            RenderModLibrary(shown);
            statusToast.Hide();
            renderedEventLog = null;
            RenderEventLog();
        }
    }
}
