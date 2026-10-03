using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// The Profile names whose model made the latest choice without "chosen
    /// by", previews only the newest thought in two lines, lists people with
    /// an icon per tie and joins Suggest and Order into one switch. Memories
    /// are tabbed cards, and the Family Tree uses portrait boxes, a heart and
    /// a key, sized to the tree beside the Profile.
    /// </summary>
    private async Task VerifyAgentPanelsAsync()
    {
        if (renderedMapSnapshot is not { } shown)
            throw new InvalidOperationException("The agent panel checks need a world on screen.");
        var rowan = PanelSmokeAgent("agent-panels-rowan", "Rowan", new OwnerWorldPosition(1, 1));
        var pip = PanelSmokeAgent("agent-panels-pip", "Pip", new OwnerWorldPosition(2, 1)) with { Lifecycle = "dead" };
        var mira = PanelSmokeAgent("agent-panels-mira", "Mira", new OwnerWorldPosition(1, 2)) with
        {
            Relationships =
            [
                new OwnerWorldInhabitantRelationship("partner:mira-rowan", rowan.Id, "partnership", "accepted", "family", 1),
                new OwnerWorldInhabitantRelationship("birth:pip", pip.Id, "biological_parentage", "accepted", "family", 1, "parent"),
            ],
            SocialStanding = [new OwnerWorldSocialStanding(rowan.Id, "Rowan", 8)],
            RecentPrivateThoughts =
            [
                new OwnerWorldPrivateThought(1, "An older thought about the weather."),
                new OwnerWorldPrivateThought(2, "The river bank has good clay. If I carry some back before dusk, Ash can fire the kiln " +
                    "tomorrow and we will finally have pots to store the harvest in before the first frost arrives."),
            ],
            RecentMemories = [new OwnerWorldAgentMemory(2, rowan.Id, "Rowan", "Rowan shared the last of the bread with me.", "private")],
            RecentBeliefs = [new OwnerWorldAgentBelief(3, "Wren is saving seed for spring.", "hearsay", 6_000, "ash", "Ash", null, null, false, null)],
            RecentKnowledgeFacts = [new OwnerWorldKnowledgeFact(1, 0, 0, "meadow", ["clay"], "Mira", "firsthand", null)],
        };
        rowan = rowan with { Relationships = [new OwnerWorldInhabitantRelationship("partner:mira-rowan", mira.Id, "partnership", "accepted", "family", 1)] };
        pip = pip with { Relationships = [new OwnerWorldInhabitantRelationship("birth:pip", mira.Id, "biological_parentage", "accepted", "family", 1, "child")] };
        OwnerWorldCognition Decided(string provider) => new(provider, false, null, null, null, [],
            [new OwnerWorldInhabitantDecision(mira.Id, provider, "gather", 2, 0.9, "test-model", null, null)]);
        var snapshot = shown with { Inhabitants = [mira, rowan, pip], Cognition = Decided("openai") };
        RenderMap(snapshot);
        selectedInhabitantId = mira.Id;
        RenderSelectedInhabitantCard(snapshot);
        OpenAgentProfile(speak: false);
        try
        {
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!profileActivityLabel.Text.Contains("Model: OpenAI · Ready", StringComparison.Ordinal) ||
                (profileActivityLabel.Text + inhabitantDetails.Text).Contains("hosen by", StringComparison.Ordinal))
                throw new InvalidOperationException($"The Profile must name the model's provider on the Model line instead of \"chosen by\": {profileActivityLabel.Text}");
            RenderSelectedInhabitantCard(snapshot with { Cognition = Decided("jev") });
            if (!profileActivityLabel.Text.Contains("Model: Ready", StringComparison.Ordinal) ||
                !inhabitantDetails.Text.Contains("Jev made their latest choice.", StringComparison.Ordinal))
                throw new InvalidOperationException($"A choice Jev made must be said plainly: {profileActivityLabel.Text} / {inhabitantDetails.Text}");
            // Reuse the displayed agent to prove that projected care notes reach
            // the actual Profile controls and clear when the move is no longer pending.
            const string guardianNote = "Accepted care; waiting for a place in the guardian's House.";
            var pendingGuardian = mira with
            {
                DecisionFactors = [.. mira.DecisionFactors, new("guardian-care", guardianNote)],
                SocialNotes = [guardianNote],
            };
            RenderSelectedInhabitantCard(snapshot with { Inhabitants = [pendingGuardian, rowan, pip] });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!agentProfilePanel.IsVisibleInTree() || !inhabitantDetails.IsVisibleInTree() ||
                !inhabitantDetails.GetParsedText().Contains(guardianNote, StringComparison.Ordinal) ||
                !profilePeople.GetChildren().OfType<Label>().Any(label => label.IsVisibleInTree() &&
                    label.Text.Contains(guardianNote, StringComparison.Ordinal)))
                throw new InvalidOperationException("Pending guardian care must reach the visible Profile details and People section from the observation.");
            RenderSelectedInhabitantCard(snapshot);
            if (inhabitantDetails.GetParsedText().Contains(guardianNote, StringComparison.Ordinal) ||
                ProfilePeopleText().Contains(guardianNote, StringComparison.Ordinal))
                throw new InvalidOperationException("Refreshing the same agent without pending guardian care must clear its old note from both Profile sections.");
            // At 200% on this small window, four messages make the Profile taller than the screen:
            // its details scroll inside it and the panel stays on screen.
            var factor = uiLayer.Factor;
            SetUiFactor(2);
            try
            {
                RenderSelectedInhabitantCard(snapshot with
                {
                    Instructions = [.. Enumerable.Range(1, 4).Select(index => new OwnerWorldInstruction($"agent-panels-message-{index}", mira.Id,
                        "suggestive", $"Message {index}: please carry clay from the river bank to the kiln before dusk, and store the pots.",
                        "completed", 0, 0, index, index, "I will do that."))],
                });
                for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                PositionAgentProfile();
                var profileBottom = agentProfilePanel.Position.Y + agentProfilePanel.Size.Y;
                if (profileBottom > UiSize.Y + 0.5f || !agentOverviewScroll.Visible ||
                    selectedAgentOverview.GetCombinedMinimumSize().Y <= agentOverviewScroll.Size.Y ||
                    agentOverviewGap.GetThemeConstant("margin_right") != SettingsScrollGap)
                    throw new InvalidOperationException($"A tall Profile must scroll its details and stay on screen: bottom {profileBottom} of {UiSize.Y}.");
            }
            finally
            {
                SetUiFactor(factor);
            }
            RenderSelectedInhabitantCard(snapshot);

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!privateThoughtHistory.Text.Contains("The river bank has good clay.", StringComparison.Ordinal) ||
                privateThoughtHistory.Text.Contains("older thought", StringComparison.Ordinal) ||
                privateThoughtHistory.MaxLinesVisible != 2 || privateThoughtHistory.GetVisibleLineCount() > 2 ||
                privateThoughtHistory.TextOverrunBehavior != TextServer.OverrunBehavior.TrimWord)
                throw new InvalidOperationException($"The Profile must preview only the newest thought, in two lines: {privateThoughtHistory.Text}");
            var people = ProfilePeopleText();
            var partnerRow = profilePeople.GetChildren().OfType<HBoxContainer>().FirstOrDefault(row => row.GetChildren().OfType<Label>().Any(label => label.Text == "Partner"));
            if (!people.Contains("Partner Rowan", StringComparison.Ordinal) || !people.Contains("Parent of Pip", StringComparison.Ordinal) ||
                !people.Contains("Trusts Rowan", StringComparison.Ordinal) ||
                partnerRow?.GetChild(0) is not TextureRect { Texture: var heart } ||
                heart != PixelIcons.Texture(PixelGlyph.Heart, UiTheme.Current.Partner, UiTheme.Current.Partner, 1))
                throw new InvalidOperationException($"People must be rows with an icon for each tie, a heart for a partner: {people.ReplaceLineEndings(" / ")}");
            if (instructionSuggestButton.GetParent()?.GetParent() is not PanelContainer speakSwitch || speakSwitch.ThemeTypeVariation != "SegmentedPanel" ||
                instructionOrderButton.GetParent()?.GetParent() != speakSwitch || instructionText.PlaceholderText.Contains('…'))
                throw new InvalidOperationException("Suggest and Order must share one switch, and the message box must not use the mid-height ellipsis.");
            // A chosen toggle takes the new palette's pressed look when the theme changes.
            var themeBefore = displayPreferences.Theme;
            var switchTo = UiTheme.Current == UiTheme.Dark ? UiThemeChoice.Light : UiThemeChoice.Dark;
            SetUiTheme((int)switchTo);
            var restyled = instructionSuggestButton.GetThemeStylebox("pressed") is StyleBoxTexture pressed &&
                pressed.Texture == ((StyleBoxTexture)UiTheme.Theme.GetStylebox("pressed", "TabButton")).Texture;
            SetUiTheme((int)UiTheme.Parse(themeBefore));
            if (!restyled)
                throw new InvalidOperationException("Suggest and Order must take the new theme's pressed style when the theme changes.");
            // An order's Queue and Cancel task buttons fit beside the switch without widening the Profile.
            var profileWidth = agentProfilePanel.Size.X;
            instructionOrderButton.ButtonPressed = true;
            instructionCancelButton.Visible = true;
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var widened = agentProfilePanel.Size.X;
            var buttonsInside = new Control[] { instructionQueueToggle, instructionCancelButton }
                .All(button => agentProfilePanel.GetGlobalRect().Grow(1).Encloses(button.GetGlobalRect()));
            instructionSuggestButton.ButtonPressed = true;
            instructionCancelButton.Visible = false;
            if (widened > profileWidth + 1 || !buttonsInside)
                throw new InvalidOperationException($"Queue and Cancel task must wrap inside the Profile instead of widening it: before={profileWidth} after={widened}.");

            OpenMemories();
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var tabs = memoryTabs.FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>().Select(button => button.Text).ToArray();
            var memories = MemoryCardsText();
            if (!tabs.SequenceEqual(["All", "Memories 1", "Beliefs 1", "Maps 1"]) || memoryCards.GetChildCount() != 3 ||
                !memories.Contains("PRIVATE", StringComparison.Ordinal) || !memories.Contains("Heard from Ash", StringComparison.Ordinal) ||
                !memories.Contains("60% sure", StringComparison.Ordinal) || !memories.Contains("Clay near the meadow", StringComparison.Ordinal) ||
                !memories.Contains("Meadow at 0, 0", StringComparison.Ordinal))
                throw new InvalidOperationException($"Memories must be tabbed cards with sureness, a Private tag and map places: tabs={string.Join(",", tabs)} text={memories.ReplaceLineEndings(" / ")}");
            Button TabButton(int index) => memoryTabs.FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>().ElementAt(index);
            TabButton(memoryTabs.GetItemIndex((int)MemoryKind.Belief)).EmitSignal(BaseButton.SignalName.Pressed);
            if (memoryCards.GetChildCount() != 1 || !MemoryCardsText().Contains("Wren is saving seed", StringComparison.Ordinal))
                throw new InvalidOperationException("A Memories tab must show only its own kind.");
            TabButton(0).EmitSignal(BaseButton.SignalName.Pressed);
            // A belief names its subject as they are called now, even after a rename.
            var believer = mira with
            {
                RecentBeliefs = [.. mira.RecentBeliefs, new OwnerWorldAgentBelief(4, "Keeps the spare axe by the door.", "firsthand", 9_000, null, null, null, rowan.Id, false, null)],
            };
            RenderSelectedInhabitantCard(snapshot with { Inhabitants = [believer, rowan, pip] });
            RenderSelectedInhabitantCard(snapshot with { Inhabitants = [believer, rowan with { DisplayName = "Rowan Ash" }, pip] });
            if (!MemoryCardsText().Contains("about Rowan Ash", StringComparison.Ordinal))
                throw new InvalidOperationException($"A belief card must follow its subject's new name: {MemoryCardsText().ReplaceLineEndings(" / ")}");
            RenderSelectedInhabitantCard(snapshot);
            if (!GetViewportRect().Encloses(memoriesPanel.GetGlobalRect()))
                throw new InvalidOperationException($"Memories must stay on screen: {memoriesPanel.GetGlobalRect()}");

            ShowFamilyTree(snapshot, mira.Id);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var boxes = familyTreeView.GetChildren().OfType<Button>().ToArray();
            if (boxes.Length != 3 || boxes.Any(box => box.Icon is null) ||
                !boxes.Any(box => box.Text == "Pip · died") || boxes.Any(box => box.Text.Contains("Living", StringComparison.Ordinal)) ||
                familyTreeView.PartnerEdgeCount != 1 || !familyLegend.Visible || familyTreeStatus.Visible)
                throw new InvalidOperationException($"The Family Tree must use portrait boxes, mark only the dead and show its key: {string.Join(", ", boxes.Select(box => box.Text))}");
            var profileRight = agentProfilePanel.GetGlobalRect().End.X;
            if (familyTreePanel.GetGlobalRect().Position.X < profileRight || familyTreePanel.Size.X > 640 ||
                !GetViewportRect().Encloses(familyTreePanel.GetGlobalRect()))
                throw new InvalidOperationException($"A small Family Tree must fit its tree and open beside the Profile: tree={familyTreePanel.GetGlobalRect()} profile right={profileRight}.");
        }
        finally
        {
            familyTreePanel.Hide();
            memoriesPanel.Hide();
            agentProfileRequested = false;
            selectedInhabitantId = null;
            RenderSelectedInhabitantCard(shown);
            RenderMap(shown);
        }
    }
}
