using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>Real key input browses the roster, keeps its row through refreshes and activates the chosen profile.</summary>
    private async Task VerifyRosterKeyboardAsync()
    {
        var original = renderedMapSnapshot ?? throw new InvalidOperationException("Roster input checks need a rendered world.");
        var previousReconnect = observationSession.Current;
        var previousSelection = selectedInhabitantId;
        var wasVisible = rosterPanel.Visible;
        string? restorationFailure = null;
        var people = Enumerable.Range(0, 3).Select(index =>
            PanelSmokeAgent($"roster-keyboard-{index}", $"Agent {index:D2}", new OwnerWorldPosition(index + 1, 1))).ToArray();
        var snapshot = original with { Inhabitants = people, LatestEventId = 0 };
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                "owner-control.request.v1", "paused-authoring.request.v1"], []);
        async Task LayoutAsync()
        {
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        async Task KeyAsync(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
            await LayoutAsync();
        }
        async Task ClickAsync(int row, bool doubleClick)
        {
            var card = rosterCards.GetChild(0).GetChildren().OfType<PanelContainer>().ElementAt(row);
            var position = card.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion { Position = position, GlobalPosition = position });
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                DoubleClick = doubleClick,
                Position = position,
                GlobalPosition = position,
            });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = position,
                GlobalPosition = position,
            });
            await LayoutAsync();
        }
        void ExpectBrowsing(string id, string reason)
        {
            var rows = rosterCards.GetSelectedItems();
            if (!rosterPanel.Visible || !rosterCards.HasFocus() || selectedInhabitantId != id ||
                rows.Length != 1 || rosterCardIds[rows[0]] != id)
                throw new InvalidOperationException($"{reason} must keep the focused Agents list open on {id}: " +
                    $"visible={rosterPanel.Visible}, focus={rosterCards.HasFocus()}, selected={selectedInhabitantId}.");
        }
        void AcceptSnapshot()
        {
            observationSession.ResetAfterLoad();
            if (!observationSession.TryAccept(new(handshake, new(snapshot, new(0, 0, []))), 0, out var failure))
                throw new InvalidOperationException("Roster input observation was refused: " + failure);
        }
        try
        {
            AcceptSnapshot();
            selectedInhabitantId = null;
            RenderMap(snapshot);
            RenderInhabitantList(snapshot);
            RenderSelectedInhabitantCard(snapshot);
            rosterPanel.Show();
            await LayoutAsync();
            rosterCards.GrabFocus();
            await KeyAsync(Key.Down);
            ExpectBrowsing(people[0].Id, "The first Down press");
            await KeyAsync(Key.Down);
            ExpectBrowsing(people[1].Id, "A second Down press");
            await KeyAsync(Key.Up);
            ExpectBrowsing(people[0].Id, "An Up press");
            await KeyAsync(Key.Up);
            ExpectBrowsing(people[0].Id, "An Up press at the first row");
            await KeyAsync(Key.Down);
            await KeyAsync(Key.Down);
            ExpectBrowsing(people[2].Id, "Browsing to the last row");
            await KeyAsync(Key.Down);
            ExpectBrowsing(people[2].Id, "A Down press at the last row");
            RenderInhabitantList(snapshot);
            await LayoutAsync();
            ExpectBrowsing(people[2].Id, "An ordinary refresh");
            snapshot = snapshot with
            {
                Inhabitants = [PanelSmokeAgent("roster-keyboard-new", "Agent -1", new(1, 2)), .. people],
            };
            AcceptSnapshot();
            RenderInhabitantList(snapshot);
            await LayoutAsync();
            ExpectBrowsing(people[2].Id, "A new row above the selection");
            await KeyAsync(Key.Enter);
            if (!agentProfilePanel.Visible || selectedInhabitantId != people[2].Id ||
                !selectedActorNameLabel.Text.Contains(people[2].DisplayName, StringComparison.Ordinal))
                throw new InvalidOperationException("Enter must open the keyboard-selected agent's Profile.");
            agentProfileRequested = false;
            RenderSelectedInhabitantCard(snapshot);
            rosterPanel.Show();
            await LayoutAsync();
            await ClickAsync(0, doubleClick: false);
            if (rosterPanel.Visible || selectedInhabitantId != "roster-keyboard-new")
                throw new InvalidOperationException("A mouse click must still close the Agents list and find its agent.");
            rosterPanel.Show();
            await LayoutAsync();
            await ClickAsync(1, doubleClick: true);
            if (!agentProfilePanel.Visible || selectedInhabitantId != people[0].Id ||
                !selectedActorNameLabel.Text.Contains(people[0].DisplayName, StringComparison.Ordinal))
                throw new InvalidOperationException("A double-click must still open the clicked agent's Profile.");
            agentProfileRequested = false;
            RenderSelectedInhabitantCard(snapshot);
            rosterPanel.Show();
            await LayoutAsync();
            rosterCards.GrabFocus();
            await KeyAsync(Key.Down);
            await KeyAsync(Key.Up);
            ExpectBrowsing(people[0].Id, "Returning to a row with the keyboard before a double-click");
            await ClickAsync(1, doubleClick: true);
            if (!agentProfilePanel.Visible || selectedInhabitantId != people[0].Id ||
                !selectedActorNameLabel.Text.Contains(people[0].DisplayName, StringComparison.Ordinal))
                throw new InvalidOperationException("Double-clicking the keyboard-selected row must open that agent's Profile.");
        }
        finally
        {
            observationSession.ResetAfterLoad();
            if (previousReconnect is not null)
                observationSession.TryAccept(previousReconnect, 0, out restorationFailure);
            selectedInhabitantId = previousSelection;
            RenderMap(original);
            RenderInhabitantList(original);
            RenderSelectedInhabitantCard(original);
            rosterPanel.Visible = wasVisible;
        }
        if (!string.IsNullOrEmpty(restorationFailure))
            throw new InvalidOperationException("The previous roster observation could not be restored: " + restorationFailure);
    }

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
