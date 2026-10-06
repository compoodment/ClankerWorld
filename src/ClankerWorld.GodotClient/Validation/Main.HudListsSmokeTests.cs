using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>Refreshing the roster preserves browsing position; changing the selection still reveals its card.</summary>
    private async Task VerifyRosterRefreshScrollAsync()
    {
        var original = renderedMapSnapshot ?? throw new InvalidOperationException("Roster checks need a rendered world.");
        var originalSelection = selectedInhabitantId;
        var wasVisible = rosterPanel.Visible;
        var people = Enumerable.Range(0, 30).Select(index =>
            PanelSmokeAgent($"roster-scroll-{index}", $"Agent {index:D2}", new OwnerWorldPosition(1, 1))).ToArray();
        var snapshot = original with { Inhabitants = people };
        async Task LayoutAsync()
        {
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        async Task RefreshWithoutMovingAsync(OwnerWorldSnapshot refresh, string reason)
        {
            RenderInhabitantList(refresh);
            await LayoutAsync();
            if (Math.Abs(rosterCards.ScrollVertical - 500) > 1)
                throw new InvalidOperationException($"{reason} must preserve roster browsing position: expected 500, got {rosterCards.ScrollVertical}.");
        }
        try
        {
            rosterPanel.Show();
            selectedInhabitantId = people[0].Id;
            RenderInhabitantList(snapshot);
            await LayoutAsync();
            rosterCards.ScrollVertical = 500;
            await LayoutAsync();
            if (rosterCards.ScrollVertical != 500)
                throw new InvalidOperationException("The roster scroll check needs enough visible cards to browse lower rows.");
            await RefreshWithoutMovingAsync(snapshot, "An identical snapshot refresh");
            await RefreshWithoutMovingAsync(snapshot, "A repeated snapshot refresh");

            var newcomer = PanelSmokeAgent("roster-scroll-newcomer", "A new arrival", new OwnerWorldPosition(1, 1));
            snapshot = snapshot with { Inhabitants = [newcomer, .. people] };
            await RefreshWithoutMovingAsync(snapshot, "Adding an agent above the selection");
            snapshot = snapshot with
            {
                Inhabitants = snapshot.Inhabitants.Select(person => person.Id == people[0].Id
                    ? person with { DisplayName = "Zed" } : person).Reverse().ToArray(),
            };
            await RefreshWithoutMovingAsync(snapshot, "Sorting a renamed selected agent to the bottom");
            if (rosterCardIds[rosterCards.GetSelectedItems().Single()] != people[0].Id)
                throw new InvalidOperationException("Sorting must preserve the selected agent's identity.");
            snapshot = snapshot with { Inhabitants = snapshot.Inhabitants.Where(person => person.Id != people[1].Id).ToArray() };
            await RefreshWithoutMovingAsync(snapshot, "Removing another agent");
            snapshot = snapshot with
            {
                Inhabitants = snapshot.Inhabitants.Select(person => person.Id == people[2].Id
                    ? person with { IsDraft = true } : person).ToArray(),
            };
            await RefreshWithoutMovingAsync(snapshot, "Filtering out a draft agent");

            selectedInhabitantId = people[3].Id;
            RenderInhabitantList(snapshot);
            await LayoutAsync();
            if (rosterCards.ScrollVertical >= 500)
                throw new InvalidOperationException("Selecting another agent above the viewport must reveal that card.");
            rosterCards.ScrollVertical = 500;
            await LayoutAsync();
            snapshot = snapshot with { Inhabitants = snapshot.Inhabitants.Where(person => person.Id != people[3].Id).ToArray() };
            await RefreshWithoutMovingAsync(snapshot, "Removing the selected agent");
            if (selectedInhabitantId is not null || rosterCards.GetSelectedItems().Length != 0)
                throw new InvalidOperationException("Removing the selected agent must clear the selection.");
            await RefreshWithoutMovingAsync(snapshot, "Refreshing without a selection");
        }
        finally
        {
            selectedInhabitantId = originalSelection;
            RenderInhabitantList(original);
            rosterPanel.Visible = wasVisible;
        }
    }

    /// <summary>Every label on one Agents list card, joined, for checks.</summary>
    private string RosterCardText(int index)
    {
        var rows = rosterCards.GetChild(0).GetChildren().OfType<PanelContainer>().ToArray();
        return index < rows.Length
            ? string.Join(" ", rows[index].FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text))
            : string.Empty;
    }

    /// <summary>
    /// The Event Log has a heading per day, an icon on every row, a Find button
    /// for each located event kept clear of the scrollbar, a dot on the rows
    /// that were new, and it ends above the bottom of the screen.
    /// </summary>
    private void VerifyEventRows()
    {
        var bands = eventRows.GetChildren().OfType<HBoxContainer>().Where(row => row.GetChildCount() > 0 && row.GetChild(0) is Label label && label.ThemeTypeVariation == "SectionLabel").ToArray();
        var rows = eventRows.GetChildren().OfType<HBoxContainer>().Except(bands).Where(row => row.Name != "NewcomerOffer").ToArray();
        if (bands.Length == 0 || rows.Length == 0 || rows.Any(row => row.GetChildren().OfType<TextureRect>().FirstOrDefault()?.Texture is null))
            throw new InvalidOperationException("Every Event Log row needs a kind icon under a heading for its day.");
        if (!rows.Any(row => row.GetChildren().OfType<Button>().Any(button => ShowsFind(button))))
            throw new InvalidOperationException("Events with a place must have a Find button.");
        if (!rows.Any(row => row.GetChild(0) is ColorRect { Color.A: > 0 }))
            throw new InvalidOperationException("Events that arrived since the log was last opened must keep a dot.");
        if (eventRows.GetParent() is not MarginContainer gap || gap.GetThemeConstant("margin_right") != SettingsScrollGap)
            throw new InvalidOperationException("The Find buttons must keep a gap before the Event Log's scrollbar.");
        if (eventsPanel.GetGlobalRect().End.Y > GetViewport().GetVisibleRect().End.Y + 1)
            throw new InvalidOperationException($"The Event Log must end above the bottom of the screen: {eventsPanel.GetGlobalRect()}.");
    }

    private static bool ShowsFind(Button button) =>
        button.Icon is Texture2D icon && icon.GetWidth() == PixelIcons.Grid;

    /// <summary>The newcomer offer's button at the top of the Event Log, if shown.</summary>
    private Button? NewcomerOfferButton() =>
        eventRows.GetChildren().OfType<HBoxContainer>().FirstOrDefault(row => row.Name == "NewcomerOffer")?
            .GetChildren().OfType<Button>().FirstOrDefault(button => button.Text == "Add a newcomer");

    /// <summary>The controls list draws keys as keycaps in the current theme's colours, grouped by what they do.</summary>
    private void VerifyControlsKeycaps()
    {
        var texts = controlsPanel.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text).ToArray();
        foreach (var section in new[] { "MAP", "TIME AND AGENTS", "MOUSE", "PANELS" })
            if (!texts.Contains(section))
                throw new InvalidOperationException($"The controls list must group keys under {section}.");
        var caps = controlsPanel.FindChildren("*", nameof(PanelContainer), recursive: true, owned: false).OfType<PanelContainer>()
            .Select(panel => panel.GetThemeStylebox("panel")).OfType<StyleBoxFlat>().Where(style => style.BorderWidthBottom == 3).ToArray();
        if (caps.Length < 10 || caps.Any(style => style.BgColor != UiTheme.Current.Button))
            throw new InvalidOperationException("Keys must be drawn as keycaps in the current theme's button colour.");
    }
}
