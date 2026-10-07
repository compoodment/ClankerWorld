using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The selected agent in two steps: a quick card beside them on the map with
/// what they are doing and how they are, and a Profile docked on the left
/// with everything else. Historical profiles open straight to the Profile,
/// because the dead have no place on the map.
/// </summary>
public partial class Main
{
    private const int QuickCardWidth = 206;
    private const int AgentProfileWidth = 300;
    private const int ReaderWidth = 560;

    private readonly PanelContainer agentProfilePanel = new();
    private readonly Label quickCardNameLabel = new();
    private readonly Label quickCardActivityLabel = new();
    private readonly Label quickCardOrderLabel = new();
    private readonly VBoxContainer quickCardMeters = new();
    private readonly PixelMeter quickFullnessMeter = new() { Kind = MeterKind.Fullness, Caption = "Fullness", CaptionWidth = 54 };
    private readonly PixelMeter quickWarmthMeter = new() { Kind = MeterKind.Warmth, Caption = "Warmth", CaptionWidth = 54 };
    private readonly PixelMeter quickIllnessMeter = new() { Kind = MeterKind.Illness, Caption = "Illness", CaptionWidth = 54 };
    private readonly Button quickCardProfileButton = new();
    private readonly Button quickCardSpeakButton = new();
    private readonly PanelContainer agentPortraitFrame = new();
    private readonly AtlasTexture agentPortraitTexture = new();
    private readonly Button renameToggleButton = new();
    private readonly HBoxContainer renameRow = new();
    private readonly Label profileActivityLabel = new();
    private readonly Label profileOrderLabel = new();
    private readonly Button profileFindButton = new();
    private readonly Button profileCloseButton = new();
    private readonly GridContainer profileMeters = new() { Columns = 2 };
    private readonly PixelMeter profileFullnessMeter = new() { Kind = MeterKind.Fullness, Caption = "Fullness", CaptionWidth = 54 };
    private readonly PixelMeter profileWarmthMeter = new() { Kind = MeterKind.Warmth, Caption = "Warmth", CaptionWidth = 50 };
    private readonly PixelMeter profileDietMeter = new() { Kind = MeterKind.Diet, Caption = "Diet", CaptionWidth = 54 };
    private readonly PixelMeter profileIllnessMeter = new() { Kind = MeterKind.Illness, Caption = "Illness", CaptionWidth = 50 };
    private readonly Label thoughtsHeading = new() { ThemeTypeVariation = "SectionLabel" };
    private readonly VBoxContainer speakSection = new();
    private readonly RichTextLabel instructionHistory = new();
    private readonly ScrollContainer agentOverviewScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
    private readonly MarginContainer agentOverviewGap = new();
    private bool agentProfileFitQueued;
    private readonly Button instructionSuggestButton = new();
    private readonly Button instructionOrderButton = new();
    private readonly CheckButton instructionQueueToggle = new();
    private readonly Button instructionCancelButton = new();
    private readonly PanelContainer thoughtsInset = new() { ThemeTypeVariation = "InsetPanel" };
    private readonly Button readThoughtsButton = new();
    private readonly PanelContainer thoughtsPanel = new();
    private readonly Label thoughtsReaderTitle = new();
    private readonly RichTextLabel thoughtsReaderText = new();
    private string? renderedThoughtsReader;
    private bool agentProfileRequested;
    private OwnerWorldSnapshot? agentCardSnapshot;

    private void BuildAgentCards()
    {
        BuildQuickCard();
        BuildAgentProfile();
        BuildThoughtsReader();
        BuildOrdersReader();
        BuildConversationReader();
        uiLayer.AddChild(selectedInhabitantCard);
        uiLayer.AddChild(agentProfilePanel);
        uiLayer.AddChild(thoughtsPanel);
        uiLayer.AddChild(ordersPanel);
        uiLayer.AddChild(conversationPanel);
    }

    private void BuildQuickCard()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 5);
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 4);
        quickCardNameLabel.ThemeTypeVariation = "HeadingLabel";
        quickCardNameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        quickCardNameLabel.ClipText = true;
        heading.AddChild(quickCardNameLabel);
        StyleIconButton(findAgentButton, PixelGlyph.Find);
        findAgentButton.TooltipText = "Center the map on this agent (C)";
        findAgentButton.Pressed += CenterOnSelectedAgent;
        heading.AddChild(findAgentButton);
        StyleIconButton(clearSelectionButton, PixelGlyph.Close);
        clearSelectionButton.TooltipText = "Close (Esc)";
        clearSelectionButton.Pressed += ClearInhabitantSelection;
        heading.AddChild(clearSelectionButton);
        body.AddChild(heading);

        quickCardActivityLabel.ThemeTypeVariation = "SoftLabel";
        quickCardActivityLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(quickCardActivityLabel);
        ConfigureOrderLabel(quickCardOrderLabel);
        body.AddChild(quickCardOrderLabel);
        quickCardMeters.AddThemeConstantOverride("separation", 3);
        foreach (var meter in new[] { quickFullnessMeter, quickWarmthMeter, quickIllnessMeter })
            quickCardMeters.AddChild(meter);
        body.AddChild(quickCardMeters);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 4);
        quickCardProfileButton.Text = "Profile";
        quickCardProfileButton.TooltipText = "Open everything about this agent.";
        StyleButton(quickCardProfileButton);
        quickCardProfileButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        quickCardProfileButton.Pressed += () => OpenAgentProfile(speak: false);
        actions.AddChild(quickCardProfileButton);
        quickCardSpeakButton.Text = "Speak";
        quickCardSpeakButton.TooltipText = "Say something to this agent.";
        StyleButton(quickCardSpeakButton);
        quickCardSpeakButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        quickCardSpeakButton.Pressed += () => OpenAgentProfile(speak: true);
        actions.AddChild(quickCardSpeakButton);
        body.AddChild(actions);

        AddPanelContents(selectedInhabitantCard, body);
        selectedInhabitantCard.ThemeTypeVariation = "HudPanel";
        selectedInhabitantCard.CustomMinimumSize = new Vector2(QuickCardWidth, 0);
        selectedInhabitantCard.ZIndex = 70;
        selectedInhabitantCard.Hide();
    }

    private void BuildAgentProfile()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 5);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        agentPortraitFrame.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        agentPortraitFrame.AddChild(new TextureRect
        {
            Texture = agentPortraitTexture,
            CustomMinimumSize = new Vector2(32, 32),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        });
        header.AddChild(agentPortraitFrame);
        var identity = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        identity.AddThemeConstantOverride("separation", 2);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 2);
        selectedActorNameLabel.ThemeTypeVariation = "HeadingLabel";
        nameRow.AddChild(selectedActorNameLabel);
        // A small pencil beside the name renames; it opens a field only when wanted.
        StyleIconButton(renameToggleButton, PixelGlyph.Pencil);
        renameToggleButton.Flat = true;
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            renameToggleButton.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginLeft = 3, ContentMarginRight = 3, ContentMarginTop = 3, ContentMarginBottom = 3 });
        renameToggleButton.TooltipText = "Rename";
        renameToggleButton.Pressed += ToggleRenameRow;
        nameRow.AddChild(renameToggleButton);
        identity.AddChild(nameRow);
        selectedActorSummaryLabel.ThemeTypeVariation = "DimLabel";
        selectedActorSummaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        identity.AddChild(selectedActorSummaryLabel);
        header.AddChild(identity);
        var tools = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        tools.AddThemeConstantOverride("separation", 4);
        StyleIconButton(profileFindButton, PixelGlyph.Find);
        profileFindButton.TooltipText = "Center the map on this agent (C)";
        profileFindButton.Pressed += CenterOnSelectedAgent;
        tools.AddChild(profileFindButton);
        UpdateAgentProfileCloseButton(living: true);
        profileCloseButton.Pressed += AgentProfileBack;
        tools.AddChild(profileCloseButton);
        header.AddChild(tools);
        body.AddChild(header);
        // Full width under the portrait, so what they are doing rarely wraps.
        profileActivityLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(profileActivityLabel);
        ConfigureOrderLabel(profileOrderLabel);
        body.AddChild(profileOrderLabel);

        renameAgentInput.PlaceholderText = "Agent name";
        renameAgentInput.TooltipText = "Changing a married agent's surname also updates their spouse.";
        renameAgentInput.MaxLength = 48;
        renameAgentInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        renameAgentInput.TextChanged += _ => refusedAgentRename.Forget();
        renameAgentInput.TextSubmitted += submitted => _ = RenameSelectedAgentAsync();
        renameRow.AddChild(renameAgentInput);
        renameAgentButton.Text = "Rename";
        StyleButton(renameAgentButton);
        renameAgentButton.Pressed += () => _ = RenameSelectedAgentAsync();
        renameRow.AddChild(renameAgentButton);
        renameRow.Hide();
        body.AddChild(renameRow);

        selectedAgentOverview.AddThemeConstantOverride("separation", 5);
        selectedActorConditionLabel.ThemeTypeVariation = "DimLabel";
        selectedActorConditionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        selectedAgentOverview.AddChild(selectedActorConditionLabel);
        profileMeters.AddThemeConstantOverride("h_separation", 14);
        profileMeters.AddThemeConstantOverride("v_separation", 4);
        foreach (var meter in new[] { profileFullnessMeter, profileWarmthMeter, profileDietMeter, profileIllnessMeter })
            profileMeters.AddChild(meter);
        selectedAgentOverview.AddChild(profileMeters);
        ConfigureTextPanel(inhabitantDetails, 90);
        selectedAgentOverview.AddChild(inhabitantDetails);

        // The Profile shows the latest thoughts; clicking them, or Read all,
        // opens every recent one in a larger reader beside it.
        var thoughtsRow = new HBoxContainer();
        thoughtsHeading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        thoughtsHeading.VerticalAlignment = VerticalAlignment.Center;
        thoughtsRow.AddChild(thoughtsHeading);
        readThoughtsButton.Text = "Read all";
        readThoughtsButton.TooltipText = "Open all of their recent thoughts in a larger reader.";
        StyleCompactToggle(readThoughtsButton);
        readThoughtsButton.Pressed += OpenThoughtsReader;
        thoughtsRow.AddChild(readThoughtsButton);
        selectedAgentOverview.AddChild(thoughtsRow);
        // The newest thought in two lines; clicking it, or Read all, opens the rest.
        privateThoughtHistory.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        privateThoughtHistory.MaxLinesVisible = 2;
        // A long thought stops at a whole word; the body font draws every
        // ellipsis character at mid-height, so none is added.
        privateThoughtHistory.TextOverrunBehavior = TextServer.OverrunBehavior.TrimWord;
        privateThoughtHistory.MouseFilter = Control.MouseFilterEnum.Stop;
        privateThoughtHistory.TooltipText = "Click to read all of their thoughts. Only you can see these; other agents don't know them unless they are told.";
        privateThoughtHistory.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        privateThoughtHistory.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                OpenThoughtsReader();
        };
        privateThoughtHistory.MouseEntered += () => thoughtsInset.ThemeTypeVariation = "InsetPanelHover";
        privateThoughtHistory.MouseExited += () => thoughtsInset.ThemeTypeVariation = "InsetPanel";
        thoughtsInset.AddChild(privateThoughtHistory);
        selectedAgentOverview.AddChild(thoughtsInset);

        selectedAgentOverview.AddChild(new Label { Text = "PEOPLE", ThemeTypeVariation = "SectionLabel" });
        BuildProfilePeople(selectedAgentOverview);

        var profileActions = new HBoxContainer();
        profileActions.AddThemeConstantOverride("separation", 4);
        memoriesButton.Text = "Memories";
        memoriesButton.TooltipText = "See what this agent remembers and the maps they know, including private memories.";
        memoriesButton.Pressed += OpenMemories;
        familyTreeButton.Text = "Family";
        familyTreeButton.TooltipText = "See their family tree, including those who have passed.";
        familyTreeButton.Pressed += OpenFamilyTree;
        modelSettingsButton.Text = "Model";
        modelSettingsButton.TooltipText = "Choose this agent's model and API key.";
        modelSettingsButton.Pressed += OpenAgentModelEditor;
        foreach (var button in new[] { memoriesButton, familyTreeButton, modelSettingsButton })
        {
            StyleButton(button);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            profileActions.AddChild(button);
        }
        selectedAgentOverview.AddChild(profileActions);

        // Speak to them: suggest or order beside the heading, then the message.
        speakSection.AddThemeConstantOverride("separation", 4);
        // Queue and Cancel task wrap onto a second line instead of widening the Profile.
        var speakHeading = new HFlowContainer();
        speakHeading.AddThemeConstantOverride("h_separation", 4);
        speakHeading.AddThemeConstantOverride("v_separation", 4);
        speakHeading.AddChild(new Label
        {
            Text = "SPEAK TO THEM",
            ThemeTypeVariation = "SectionLabel",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var kind = new ButtonGroup();
        // One joined switch, like the other two-way choices.
        var speakSwitch = new PanelContainer { ThemeTypeVariation = "SegmentedPanel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var speakSwitchRow = new HBoxContainer();
        speakSwitchRow.AddThemeConstantOverride("separation", 0);
        speakSwitch.AddChild(speakSwitchRow);
        foreach (var (button, text, tip) in new[]
        {
            (instructionSuggestButton, "Suggest", "They weigh it against their own plans."),
            (instructionOrderButton, "Order", "They try to do it before anything else."),
        })
        {
            button.Text = text;
            button.TooltipText = tip;
            button.ToggleMode = true;
            button.ButtonGroup = kind;
            StyleCompactToggle(button);
            speakSwitchRow.AddChild(button);
        }
        speakHeading.AddChild(speakSwitch);
        instructionSuggestButton.ButtonPressed = true;
        instructionOrderButton.Toggled += pressed => instructionQueueToggle.Visible = pressed;
        instructionQueueToggle.Text = "Queue";
        instructionQueueToggle.TooltipText = "Add this order after the current task instead of replacing it.";
        instructionQueueToggle.Visible = false;
        StyleCompactToggle(instructionQueueToggle);
        speakHeading.AddChild(instructionQueueToggle);
        instructionCancelButton.Text = "Cancel task";
        instructionCancelButton.TooltipText = "Cancel the selected agent’s waiting or active order.";
        StyleCompactToggle(instructionCancelButton);
        instructionCancelButton.Pressed += () => _ = CancelSelectedOrderAsync();
        instructionCancelButton.Hide();
        speakHeading.AddChild(instructionCancelButton);
        speakSection.AddChild(speakHeading);
        var speakRow = new HBoxContainer();
        speakRow.AddThemeConstantOverride("separation", 4);
        instructionText.PlaceholderText = "Say something...";
        instructionText.MaxLength = 512;
        instructionText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        instructionText.TextSubmitted += submitted => _ = SubmitInstructionAsync();
        speakRow.AddChild(instructionText);
        submitInstructionButton.Text = "Send";
        StyleButton(submitInstructionButton, primary: true);
        submitInstructionButton.Pressed += () => _ = SubmitInstructionAsync();
        speakRow.AddChild(submitInstructionButton);
        speakSection.AddChild(speakRow);
        // The four newest messages here; every order the world keeps opens in a reader beside the Profile.
        var messagesRow = new HBoxContainer();
        messagesRow.AddChild(new Label
        {
            Text = "YOUR MESSAGES",
            ThemeTypeVariation = "SectionLabel",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        });
        allOrdersButton.Text = "All orders";
        allOrdersButton.TooltipText = "See all of their current and queued orders and how their latest ones ended.";
        StyleCompactToggle(allOrdersButton);
        allOrdersButton.Pressed += OpenOrdersReader;
        allOrdersButton.Hide();
        messagesRow.AddChild(allOrdersButton);
        speakSection.AddChild(messagesRow);
        ConfigureTextPanel(instructionHistory, 105);
        speakSection.AddChild(instructionHistory);
        selectedAgentOverview.AddChild(speakSection);
        // The details scroll once the Profile would pass the bottom of the screen.
        selectedAgentOverview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        agentOverviewGap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        agentOverviewGap.AddChild(selectedAgentOverview);
        agentOverviewScroll.AddChild(agentOverviewGap);
        selectedAgentOverview.VisibilityChanged += () => agentOverviewScroll.Visible = selectedAgentOverview.Visible;
        // Wrapped text only knows its height once laid out at its width, so measure again then.
        selectedAgentOverview.MinimumSizeChanged += QueueAgentProfileFit;
        body.AddChild(agentOverviewScroll);

        BuildAgentModelScroll();
        selectedAgentModelScroll.Hide();
        body.AddChild(selectedAgentModelScroll);

        AddPanelContents(agentProfilePanel, body);
        agentProfilePanel.CustomMinimumSize = new Vector2(AgentProfileWidth, 0);
        // Top-bar panels such as Filters open over the Profile while in use.
        agentProfilePanel.ZIndex = 75;
        agentProfilePanel.Hide();
    }

    /// <summary>Whether a decision came from an agent's own model rather than a routine helper or the built-in rules.</summary>
    private static bool IsModelProvider(string provider) => provider is "openai" or "ollama-cloud";

    /// <summary>A flat, short tab-style button that sits beside a section heading.</summary>
    private static void StyleCompactToggle(Button button)
    {
        button.ThemeTypeVariation = "TabButton";
        button.FocusMode = Control.FocusModeEnum.All;
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            var box = (StyleBox)UiTheme.Theme.GetStylebox(state, "TabButton").Duplicate();
            box.ContentMarginTop = box.ContentMarginBottom = 3;
            button.AddThemeStyleboxOverride(state, box);
        }
    }

    private void BuildThoughtsReader()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        var heading = new HBoxContainer();
        thoughtsReaderTitle.ThemeTypeVariation = "HeadingLabel";
        thoughtsReaderTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        heading.AddChild(thoughtsReaderTitle);
        var close = CloseButton("Close thoughts (Esc)");
        close.Pressed += () => thoughtsPanel.Hide();
        heading.AddChild(close);
        body.AddChild(heading);
        body.AddChild(new Label
        {
            Text = "Only you can read these. Other agents don't know them unless they are told.",
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        ConfigureTextPanel(thoughtsReaderText, 1000);
        thoughtsReaderText.AddThemeConstantOverride("paragraph_separation", 4);
        body.AddChild(thoughtsReaderText);
        AddPanelContents(thoughtsPanel, body);
        thoughtsPanel.ZIndex = 85;
        thoughtsPanel.Resized += () => PlaceReaderPanel(thoughtsPanel);
        thoughtsPanel.Hide();
    }

    private void OpenThoughtsReader()
    {
        if (SelectedInhabitant() is not { } inhabitant || agentCardSnapshot is not { } snapshot) return;
        memoriesPanel.Hide();
        familyTreePanel.Hide();
        ordersPanel.Hide();
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        conversationPanel.Hide();
        RenderThoughtsReader(snapshot, inhabitant);
        thoughtsPanel.Show();
        ApplyResponsiveLayout();
    }

    /// <summary>Every recent private thought, newest first, under a heading for each day.</summary>
    private void RenderThoughtsReader(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant inhabitant)
    {
        thoughtsReaderTitle.Text = IsDeceased(inhabitant) ? $"{inhabitant.DisplayName} · thoughts, historical" : $"{inhabitant.DisplayName} · thoughts";
        var thoughts = inhabitant.RecentPrivateThoughts.Reverse()
            .Select(thought => (Clock: SplitClock(DisplayWorldClock(thought.WorldTick)), thought.Text)).ToArray();
        var signature = inhabitant.Id + "|" + UiTheme.Current.Name + "\n" +
            string.Join("\n", thoughts.Select(item => $"{item.Clock.Date}|{item.Clock.Time}|{item.Text}"));
        if (renderedThoughtsReader == signature) return;
        renderedThoughtsReader = signature;
        thoughtsReaderText.Clear();
        if (thoughts.Length == 0)
        {
            thoughtsReaderText.PushColor(DimText);
            thoughtsReaderText.AddText("None recorded yet.");
            thoughtsReaderText.Pop();
        }
        string? day = null;
        foreach (var (clock, text) in thoughts)
        {
            if (day is not null) thoughtsReaderText.Newline();
            if (clock.Date != day)
            {
                if (day is not null) thoughtsReaderText.Newline();
                thoughtsReaderText.PushFont(UiFonts.Headings, thoughtsReaderText.GetThemeFontSize("normal_font_size"));
                thoughtsReaderText.PushColor(HeadingText);
                thoughtsReaderText.AddText(clock.Date);
                thoughtsReaderText.Pop();
                thoughtsReaderText.Pop();
                thoughtsReaderText.Newline();
                day = clock.Date;
            }
            thoughtsReaderText.PushColor(DimText);
            thoughtsReaderText.AddText(clock.Time + "  ");
            thoughtsReaderText.Pop();
            thoughtsReaderText.AddText(GameUiText.PlainEllipses(text));
        }
        FitTextPanel(thoughtsReaderText);
    }

    /// <summary>
    /// Readers such as Memories and thoughts open beside the Profile when there
    /// is room, so both stay in view, and in the middle of the screen otherwise.
    /// </summary>
    private void PlaceReaderPanel(PanelContainer panel)
    {
        var size = panel.GetCombinedMinimumSize();
        panel.Size = size;
        var beside = agentProfilePanel.Position.X + agentProfilePanel.Size.X + 12;
        panel.Position = agentProfilePanel.Visible && beside + size.X <= UiSize.X - 14
            ? new Vector2(beside, HudTop)
            : new Vector2(Math.Max(14, (UiSize.X - size.X) / 2), Math.Max(HudTop, (UiSize.Y - size.Y) / 2));
    }

    /// <summary>Profile, Speak and card buttons carry pixel icons in the current theme.</summary>
    private void RefreshAgentCardIcons()
    {
        var palette = UiTheme.Current;
        var dark = palette.Name == "dark";
        var wood = dark ? new Color("C99A62") : new Color("9C6C42");
        var green = dark ? new Color("8DBA6A") : palette.Primary;
        var gold = dark ? new Color("E8B04A") : new Color("B77C10");
        quickCardProfileButton.Icon = PixelIcons.Themed(PixelGlyph.Person, wood, 1);
        quickCardSpeakButton.Icon = PixelIcons.Themed(PixelGlyph.Speech, palette.Paper, 1);
        memoriesButton.Icon = PixelIcons.Themed(PixelGlyph.Book, wood, 1);
        familyTreeButton.Icon = PixelIcons.Themed(PixelGlyph.Tree, green, 1);
        modelSettingsButton.Icon = PixelIcons.Themed(PixelGlyph.Key, gold, 1);
        // Compact toggles copy the theme's tab styles, so copy them again for the new palette.
        foreach (var toggle in new Button[] { readThoughtsButton, instructionSuggestButton, instructionOrderButton,
                     instructionQueueToggle, instructionCancelButton, allOrdersButton, conversationHistoryButton })
            StyleCompactToggle(toggle);
        agentPortraitFrame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = dark ? new Color("3E5A2E") : new Color("8FB06A"),
            BorderColor = palette.WoodEdge,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 2,
            ContentMarginTop = 2,
            ContentMarginRight = 2,
            ContentMarginBottom = 2,
        });
    }

    private void CenterOnSelectedAgent()
    {
        if (selectedInhabitantId is { } id) CenterOnInhabitant(id);
    }

    /// <summary>Opens the Profile from the quick card; Speak also puts the cursor in the message box.</summary>
    private void OpenAgentProfile(bool speak)
    {
        agentProfileRequested = true;
        if (agentCardSnapshot is { } snapshot)
            RenderSelectedInhabitantCard(snapshot);
        if (speak && instructionText.Editable)
        {
            instructionText.CallDeferred(Control.MethodName.GrabFocus);
            QueueAgentProfileFit();
        }
    }

    /// <summary>
    /// Steps back one level: from model settings to the Profile, from the
    /// Profile to the quick card beside the agent. A historical profile, which
    /// has no quick card, simply closes.
    /// </summary>
    private void AgentProfileBack()
    {
        if (selectedAgentModelScroll.Visible)
            CloseAgentModelEditor();
        else if (SelectedInhabitant() is { } selected && !IsDeceased(selected))
            agentProfileRequested = false;
        else
        {
            ClearInhabitantSelection();
            return;
        }
        renameRow.Hide();
        if (agentCardSnapshot is { } snapshot)
            RenderSelectedInhabitantCard(snapshot);
    }

    private OwnerWorldInhabitant? SelectedInhabitant() =>
        agentCardSnapshot?.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));

    private void ToggleRenameRow()
    {
        refusedAgentRename.Forget();
        renameRow.Visible = !renameRow.Visible;
        if (!renameRow.Visible) return;
        renameAgentInput.Text = selectedActorNameLabel.Text;
        renameAgentInput.CallDeferred(Control.MethodName.GrabFocus);
        renameAgentInput.CallDeferred(LineEdit.MethodName.SelectAll);
    }

    /// <summary>The Profile's corner button goes back while there is somewhere to go back to, and closes otherwise.</summary>
    private void UpdateAgentProfileCloseButton(bool living)
    {
        var back = selectedAgentModelScroll.Visible || living;
        StyleIconButton(profileCloseButton, back ? PixelGlyph.Back : PixelGlyph.Close);
        profileCloseButton.TooltipText = back ? "Back (Esc)" : "Close (Esc)";
    }

    private static bool IsDeceased(OwnerWorldInhabitant inhabitant) =>
        string.Equals(inhabitant.Lifecycle, "dead", StringComparison.OrdinalIgnoreCase);

    private static string Sentence(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];

    /// <summary>A thought from today shows its time; older ones show the full date too.</summary>
    private string ThoughtTime(long tick, long now)
    {
        var (date, time) = SplitClock(DisplayWorldClock(tick));
        return date == SplitClock(DisplayWorldClock(now)).Date ? time : $"{date} · {time}";
    }

    private void RenderSelectedInhabitantCard(OwnerWorldSnapshot snapshot)
    {
        agentCardSnapshot = snapshot;
        var inhabitant = snapshot.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (inhabitant is null)
        {
            CloseAgentModelEditor();
            agentProfileRequested = false;
            renamingAgentId = null;
            refusedAgentRename.Forget();
            renameRow.Hide();
            quickCardNameLabel.Text = string.Empty;
            selectedActorNameLabel.Text = string.Empty;
            selectedActorSummaryLabel.Text = string.Empty;
            selectedActorConditionLabel.Text = string.Empty;
            SetPanelText(inhabitantDetails, string.Empty);
            privateThoughtHistory.Text = string.Empty;
            SetPanelText(instructionHistory, string.Empty);
            memoriesPanel.Hide();
            thoughtsPanel.Hide();
            ordersPanel.Hide();
            selectedInhabitantCard.Hide();
            agentProfilePanel.Hide();
            return;
        }

        // One quick card at a time: choosing an agent closes a building's card.
        if (selectedBuildingId is not null) ClearBuildingSelection();
        string? Factor(string key) => inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == key)?.Detail;
        var ageBand = Factor("age-band");
        var ageYears = Factor("age-years");
        var ageDays = Factor("age-days");
        var deathTick = Factor("death-tick");
        var deathCause = Factor("death-cause");
        var willStatus = Factor("will-status");
        var role = Factor("role");
        var isDeceased = IsDeceased(inhabitant);
        var waitingForDecision = inhabitant.DecisionFactors.Any(factor => factor.Key == "decision-pending");
        var decision = snapshot.Cognition?.Decisions?.FirstOrDefault(item => item.InhabitantId == inhabitant.Id);
        if (selectedAgentModelScroll.Visible && SelectedCognitionTarget() != inhabitant.Id)
            CloseAgentModelEditor();

        // Name, age and one plain sentence for what they are doing, shared by both cards.
        quickCardNameLabel.Text = inhabitant.DisplayName;
        selectedActorNameLabel.Text = inhabitant.DisplayName;
        // Draft and refused names stay in the open field for the player to change,
        // while the labels above keep showing the name the host holds.
        if (!renameRow.Visible || renamingAgentId != inhabitant.Id) refusedAgentRename.Forget();
        if (!refusedAgentRename.Keeps(snapshot.WorldId, inhabitant.Id, renameAgentInput.Text) &&
            (renamingAgentId != inhabitant.Id || !renameRow.Visible))
        {
            renameAgentInput.Text = inhabitant.DisplayName;
            renamingAgentId = inhabitant.Id;
        }
        agentPortraitTexture.Atlas = AgentSprites.Atlas(32);
        agentPortraitTexture.Region = AgentSprites.Region(AgentSprites.VariantFor(inhabitant.Id), AgentSprites.StageIndex(ageBand), 32);
        selectedActorSummaryLabel.Text = string.Join(" · ", new[]
        {
            isDeceased ? "Dead" : null,
            ageBand is null ? null : Pretty(ageBand),
            ageYears is null ? null : ageYears + " years",
            ageDays is null ? null : ageDays + " days",
            deathTick is not null && long.TryParse(deathTick, CultureInfo.InvariantCulture, out var finalTick) ? DisplayWorldClock(finalTick) : null,
        }.Where(part => part is not null));
        var activity = isDeceased
            ? $"Life ended{(deathCause is null ? "" : " · " + Pretty(deathCause))}."
            : waitingForDecision
            ? "Deciding what to do next"
            : Sentence(GameUiText.ActivityPhrase(inhabitant.PublicIntention?.CandidateId, inhabitant.PublicIntention?.Summary));
        // The Model line names whose model made their latest choice, such as OpenAI.
        var modelStatus = GameUiText.ModelStatus(Factor("model-status"));
        var modelProvider = decision is { } latest && IsModelProvider(latest.Provider) ? ProviderDisplayName(latest.Provider) + " · " : string.Empty;
        quickCardActivityLabel.Text = isDeceased ? activity : $"{activity}\nModel: {modelProvider}{modelStatus}";
        profileActivityLabel.Text = quickCardActivityLabel.Text;

        // How they are: bars where the host reports a value, and plain facts beside them.
        var fullness = NeedPercent(inhabitant.HungerBasisPoints);
        var survival = inhabitant.Survival;
        quickFullnessMeter.Percent = fullness;
        profileFullnessMeter.Percent = fullness;
        foreach (var meter in new[] { quickWarmthMeter, quickIllnessMeter, profileWarmthMeter, profileDietMeter, profileIllnessMeter })
            meter.Visible = survival is not null;
        if (survival is not null)
        {
            quickWarmthMeter.Percent = profileWarmthMeter.Percent = survival.WarmthBasisPoints / 100;
            quickIllnessMeter.Percent = profileIllnessMeter.Percent = survival.IllnessBasisPoints / 100;
            profileDietMeter.Percent = survival.NutritionBasisPoints / 100;
        }
        quickCardMeters.Visible = !isDeceased;
        profileMeters.Visible = !isDeceased;
        var carrying = inhabitant.Inventory.Count == 0
            ? "Carrying nothing"
            : "Carrying " + string.Join(", ", inhabitant.Inventory.Select(item => $"{GameUiText.ItemName(item.Kind).ToLowerInvariant()} ({item.Quantity})"));
        selectedActorConditionLabel.Text = isDeceased
            ? survival is null ? "Historical record" :
                $"At death · Warmth {survival.WarmthBasisPoints / 100}% · Illness {survival.IllnessBasisPoints / 100}% · Diet {survival.NutritionBasisPoints / 100}%"
            : survival is null ? carrying :
                $"{carrying} · {(survival.HasClothing ? "Clothed" : "No warm clothing")} · {(survival.HasTool ? "Has a tool" : "Working by hand")}";

        // What they are working on, learning and who chose their action.
        var details = new List<string>();
        if (Factor("personality") is { } personality) details.Add("Personality: " + personality);
        if (Factor("aspiration") is { } aspiration) details.Add("Aspiration: " + aspiration);
        details.AddRange(inhabitant.DecisionFactors.Where(factor => factor.Key == "identity-change")
            .Select(factor => factor.Detail));
        if (!isDeceased && !string.IsNullOrWhiteSpace(inhabitant.MedicalCareNote))
            details.Add(inhabitant.MedicalCareNote);
        if (!isDeceased && Factor("knowledge-writing") is { } writingProgress)
            details.Add(writingProgress);
        if (!isDeceased && !string.IsNullOrWhiteSpace(inhabitant.ToolMakingRequestNote))
            details.Add(inhabitant.ToolMakingRequestNote);
        if (!isDeceased && inhabitant.Equipment is { } equipment)
        {
            details.Add($"Cargo: {equipment.CarriedQuantity}/{equipment.Capacity}" +
                (equipment.CarriedQuantity > equipment.Capacity ? " · Full; store or set down a load before picking up more." : ""));
            if (equipment.ClothingKind is { } garment)
                details.Add($"Wearing {Pretty(garment).ToLowerInvariant()} · Condition {equipment.ClothingConditionPercent}%");
            if (equipment.CarryAidKind is { } aid)
                details.Add($"Equipped {Pretty(aid).ToLowerInvariant()} · Condition {equipment.CarryAidConditionPercent}%");
            if (equipment.OrnamentKind is { } ornament)
                details.Add($"Wearing {GameUiText.ItemName(ornament).ToLowerInvariant()}");
            if (equipment.RepairItemKind is { } repairItem)
                details.Add($"Repairing {Pretty(repairItem).ToLowerInvariant()} · {equipment.RepairWorkDone}/{equipment.RepairWorkRequired}");
        }
        foreach (var cart in snapshot.Handcarts.Where(cart => cart.OwnerId == inhabitant.Id || cart.PullerId == inhabitant.Id))
            details.Add(GameUiText.HandcartDescription(cart));
        foreach (var animal in snapshot.Animals.Where(animal => animal.RiderId == inhabitant.Id || animal.LeaderId == inhabitant.Id))
            details.Add(GameUiText.AnimalDescription(animal));
        if (!isDeceased && Factor("last-model-choice") is { } lastModelChoice)
            details.Add("Last model choice: " + Sentence(GameUiText.ActivityPhrase(lastModelChoice, null)));
        if (!isDeceased && Factor("model-setup-blocker") == "unsupported_request")
            details.Add("This model rejected the request format. Choose a compatible model in Model settings.");
        if (inhabitant.Project is { } project)
        {
            details.Add($"{project.Label} · {Pretty(project.Stage)} · {project.WorkDone}/{project.WorkRequired}");
            if (project.Blocker is not null) details.Add(project.Blocker);
        }
        if (role is not null and not "unassigned") details.Add($"Role: {Pretty(role)}");
        if (!isDeceased)
            details.AddRange(inhabitant.DecisionFactors.Where(factor => factor.Key == "guardian-care")
                .Select(factor => factor.Detail));
        if (Factor("housing") is { } housing && !isDeceased) details.Add(housing);
        if (!isDeceased && inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "town-membership") is { } townMembership)
        {
            details.Add(townMembership.Detail);
            if (townMembership.AcceptanceDeadlineTick is { } deadline)
                details.Add($"Acceptance deadline: {DisplayWorldClock(deadline)} · Paused time does not count.");
        }
        if (inhabitant.Lesson is { } lesson)
            details.Add($"Learning {Pretty(lesson.Skill)} with {lesson.TeacherName} · {Pretty(lesson.Stage)} · {lesson.Progress}/{lesson.Required}");
        foreach (var skill in inhabitant.Skills ?? [])
            details.Add($"{Pretty(skill.Kind)} skill · {(skill.TeacherName is { } teacher ? "taught by " + teacher : "learned by doing")} · {DisplayWorldClock(skill.LearnedTick)}");
        if (inhabitant.Proficiency is { } practice)
            details.Add($"Practice · Building {practice.Building}/30 · Farming {practice.Farming}/30 · Crafting {practice.Crafting}/30");
        if (isDeceased)
        {
            details.AddRange(GameUiText.FinalWillLines(willStatus, inhabitant.FinalWill));
        }
        else if (!waitingForDecision)
        {
            // A choice their own model made is named on the Model line; say so when something else made it.
            if (decision is null) details.Add("No decision yet");
            else if (decision.FellBack) details.Add("The model gave no usable choice, so built-in rules chose this.");
            else if (!IsModelProvider(decision.Provider)) details.Add($"{ProviderDisplayName(decision.Provider)} made their latest choice.");
        }
        SetPanelText(inhabitantDetails, string.Join("\n", details));
        inhabitantDetails.Visible = details.Count > 0;
        inhabitantDetails.TooltipText = decision is null ? "" :
            $"Last accepted decision\nRole: {decision.Role ?? "not reported"}\nModel: {decision.Model ?? "not reported"}\nConfidence: {decision.Confidence:P0}\n" +
            $"Latency: {decision.LatencyMilliseconds?.ToString(CultureInfo.CurrentCulture) ?? "—"} ms\n" +
            $"Tokens in/out: {decision.InputTokens?.ToString(CultureInfo.CurrentCulture) ?? "—"}/{decision.OutputTokens?.ToString(CultureInfo.CurrentCulture) ?? "—"}";
        if (inhabitant.Proficiency is not null)
            inhabitantDetails.TooltipText += (inhabitantDetails.TooltipText.Length == 0 ? "" : "\n") +
                "Practice: each completed project earns one point in its domain, up to 30. Every 10 points adds one work per preparation step. Materials, permissions and crop growth time are unchanged.";

        thoughtsHeading.Text = isDeceased ? "THOUGHTS · HISTORICAL" : "THOUGHTS";
        privateThoughtHistory.Text = inhabitant.RecentPrivateThoughts.Count == 0
            ? "None recorded yet."
            : $"{ThoughtTime(inhabitant.RecentPrivateThoughts[^1].WorldTick, snapshot.WorldTick)}  {GameUiText.PlainEllipses(inhabitant.RecentPrivateThoughts[^1].Text)}";
        // Keep the task that Cancel task targets, then the newest open messages
        // before closed ones, within the same four-message history.
        var pendingOrder = PendingOrderToCancel(snapshot, inhabitant.Id);
        var recentInstructions = snapshot.Instructions
            .Where(item => item.TargetInhabitantId == inhabitant.Id)
            .OrderBy(item => item.InstructionId == pendingOrder?.InstructionId ? 0 : item.State == "completed" ? 2 : 1)
            .ThenByDescending(item => item.SubmissionSequence)
            .Take(4)
            .OrderBy(item => item.SubmissionSequence)
            .Select(item =>
            {
                var status = item.Kind == "must_do"
                    ? InstructionOrderSummary(item)
                    : item.ObservedTick is null ? "Suggestion waiting for their personal model" : "Suggestion heard by their personal model";
                var reply = item.ObserverReply is null ? string.Empty : $"\nAgent reply: “{item.ObserverReply}”";
                return $"{status}\n“You said: {item.Text}”{reply}";
            })
            .ToArray();
        instructionCancelButton.Visible = !isDeceased && pendingOrder is not null;
        instructionCancelButton.Disabled = isOwnerAction || pendingSubmission is not null || registration is null || deviceKey is null;
        SetPanelText(instructionHistory, recentInstructions.Length == 0
            ? "No messages yet."
            : string.Join("\n\n", recentInstructions));
        RenderOrderSummary(snapshot, inhabitant, isDeceased);

        RenderProfilePeople(snapshot, inhabitant);
        RenderMemoryCards(snapshot, inhabitant);
        RenderThoughtsReader(snapshot, inhabitant);
        RenderOrdersReader(snapshot, inhabitant);

        modelSettingsButton.Disabled = isDeceased || registration is null;
        findAgentButton.Visible = !isDeceased;
        profileFindButton.Visible = !isDeceased;
        speakSection.Visible = !isDeceased;
        UpdateAgentProfileCloseButton(living: !isDeceased);

        // The dead have no place on the map, so their profile opens directly.
        var showProfile = agentProfileRequested || isDeceased;
        agentProfilePanel.Visible = showProfile;
        selectedInhabitantCard.Visible = !showProfile;
        PositionSelectedInhabitantCard(snapshot);
        PositionAgentProfile();
    }

    private static OwnerWorldInstruction? PendingOrderToCancel(OwnerWorldSnapshot snapshot, string? inhabitantId) =>
        snapshot.Instructions.Where(item => item.TargetInhabitantId == inhabitantId &&
                item.Order is { Status: "queued" or "waiting" or "doing" or "interrupted" or "blocked" })
            .OrderBy(item => item.SubmissionSequence).FirstOrDefault();

    /// <summary>
    /// One order's state as the host reports it, such as "Blocked · Eating food ·
    /// 0/3 food items" followed by why it is held up or was replaced.
    /// </summary>
    private static string InstructionOrderSummary(OwnerWorldInstruction instruction, bool includeHeard = true)
    {
        var order = instruction.Order;
        if (order is null)
            return instruction.State == "completed"
                ? instruction.ObservedTick is null ? "Order closed · not reported as heard" : "Heard by their personal model · order closed"
                : instruction.ObservedTick is null ? "Order pending · waiting for their personal model" : "Heard by their personal model · order pending";

        var task = order.Action switch
        {
            "consume_food" => "Eating food",
            "harvest_food" => "Gathering food",
            "gather_material" => "Gathering " + (order.TargetMaterialKind?.Replace('_', ' ') ?? "materials"),
            "till_field" => "Tilling household fields",
            "plant_field" => "Planting " + (order.TargetCropKind?.Replace('_', ' ') ?? "crops"),
            "tend_field" => "Tending " + (order.TargetCropKind?.Replace('_', ' ') ?? "household fields"),
            "harvest_field" => "Harvesting " + (order.TargetCropKind?.Replace('_', ' ') ?? "household fields"),
            "repair_equipment" or "repair_tool" => "Repairing " + (order.TargetEquipmentKind?.Replace('_', ' ') ?? "equipment"),
            "collect_material" => "Collecting " + (order.TargetMaterialKind?.Replace('_', ' ') ?? "materials"),
            "collect_food" => "Collecting " + (order.TargetFoodKind?.Replace('_', ' ') ?? "food"),
            "collect_equipment" => "Collecting " + (order.TargetEquipmentKind?.Replace('_', ' ') ?? "equipment"),
            "collect_goods" => "Collecting " + OrderItemName(order.TargetItemKind),
            "store_material" => "Storing " + (order.TargetMaterialKind?.Replace('_', ' ') ?? "materials"),
            "store_equipment" => "Storing " + (order.TargetEquipmentKind?.Replace('_', ' ') ?? "equipment"),
            "store_goods" => "Storing " + OrderItemName(order.TargetItemKind),
            "return_borrowed" => "Returning borrowed " + OrderItemName(order.TargetItemKind),
            "deliver_stock" => "Delivering " + OrderItemName(order.TargetItemKind) + " to " + OrderBuildingName(order.TargetBuildingKind),
            "construct_building" => "Building " + OrderBuildingName(order.TargetBuildingKind),
            "expand_building" => "Expanding " + OrderBuildingName(order.TargetBuildingKind),
            "seek_shelter" => order.TargetBuildingKind == "house" ? "Seeking shelter in their House" : "Seeking shelter",
            "tend_fire" => order.TargetBuildingKind == "house" ? "Lighting a fire in their House" : "Lighting a fire",
            "produce_item" => "Making " + (order.TargetOutputKind is { } output
                ? GameUiText.ItemName(output).ToLowerInvariant() : "goods"),
            "seek_food" => "Going to a food site",
            "move_to" => "Going to a tile",
            "accept_guardianship" => "Becoming a guardian",
            "animal_care" => "Caring for an animal",
            "animal_collect" => "Collecting animal products",
            "animal_tame" => "Taming an animal",
            "animal_lead_home" => "Leading an animal home",
            "animal_saddle" => "Fitting a horse's saddle",
            "animal_mount" => "Mounting a horse",
            "animal_dismount" => "Dismounting a horse",
            _ => "Order",
        };
        var units = order.RepeatUntilCancelled
            ? $" · {order.CompletedUnits} {ProgressUnitLabel(order.ProgressUnit)} so far, repeats until cancelled"
            : order.Status is "doing" or "interrupted" or "blocked" or "finished"
                ? $" · {Math.Min(order.CompletedUnits, order.RequestedUnits)}/{order.RequestedUnits} {ProgressUnitLabel(order.ProgressUnit)}"
                : string.Empty;
        // Blocked and interrupted orders say why; a cancelled one says if a newer order replaced it.
        var reason = order.Status is "blocked" or "interrupted" or "cancelled" && !string.IsNullOrWhiteSpace(order.BlockedReason)
            ? $" · {order.BlockedReason}"
            : string.Empty;
        var heard = !includeHeard || instruction.ObservedTick is null ? string.Empty : " · Heard by their personal model";
        var state = order.Status switch
        {
            "queued" => "Queued",
            "waiting" => "Waiting",
            "doing" => "Doing",
            "interrupted" => "Interrupted",
            "blocked" => "Blocked",
            "finished" => "Finished",
            "cancelled" => "Cancelled",
            "not_understood" => "Not understood",
            _ => "Waiting",
        };
        // The game understood no task in an order it could not act on, so there is none to name.
        return order.Status == "not_understood" && order.Action == "unknown"
            ? $"{state}{heard}"
            : $"{state} · {task}{units}{reason}{heard}";
    }

    private static string OrderItemName(string? kind) => kind switch
    {
        null => "goods",
        "iron" => "refined iron",
        "tool" => "workshop tool",
        _ => GameUiText.ItemName(kind).ToLowerInvariant(),
    };

    private static string ProgressUnitLabel(string unit) => unit switch
    {
        "food_items" => "food items",
        "material_items" => "items",
        "equipment_items" => "equipment items",
        "goods_items" => "items",
        "output_items" => "items made",
        "production_batches" => "batches completed",
        "repairs" => "items repaired",
        "fields" => "fields completed",
        "collection_loads" => "loads collected",
        "storage_loads" => "loads stored",
        "return_loads" => "loads returned",
        "delivery_loads" => "loads delivered",
        "buildings" => "buildings finished",
        "expansions" => "expansions finished",
        "shelters" => "shelters reached",
        "fires" => "fires lit",
        "arrivals" => "sites reached",
        "harvests" => "harvest batches",
        "guardianships" => "care assignments",
        "animal_tasks" => "animal tasks completed",
        _ => unit,
    };

    private static string OrderBuildingName(string? kind) => kind switch
    {
        "house" => "House",
        "farmhouse" => "Farmhouse",
        "silo" => "Silo",
        "blacksmith" => "Blacksmith",
        "tailor" => "Tailor Shop",
        "clinic" => "Clinic",
        "store" => "Store",
        "warehouse" => "Town Warehouse",
        _ => "the requested building",
    };

    /// <summary>The Profile docks on the left, just below the top bar, and fits its contents.</summary>
    private void PositionAgentProfile()
    {
        if (!agentProfilePanel.Visible) return;
        if (selectedAgentModelScroll.Visible) FitAgentModelScroll();
        agentProfilePanel.CustomMinimumSize = new Vector2(Math.Min(AgentProfileWidth, Math.Max(1, UiSize.X - 28)), 0);
        FitAgentOverviewScroll();
        agentProfilePanel.Size = agentProfilePanel.GetCombinedMinimumSize();
        agentProfilePanel.Position = new Vector2(14, HudTop);
    }

    private void QueueAgentProfileFit()
    {
        if (agentProfileFitQueued) return;
        agentProfileFitQueued = true;
        Callable.From(() =>
        {
            agentProfileFitQueued = false;
            PositionAgentProfile();
            // Keep the message box in view while typing, once the scroll has its new height.
            if (instructionText.HasFocus())
                Callable.From(() => agentOverviewScroll.EnsureControlVisible(instructionText)).CallDeferred();
        }).CallDeferred();
    }

    /// <summary>The Profile's details take the room they need and scroll once the panel would pass the bottom of the screen.</summary>
    private void FitAgentOverviewScroll()
    {
        if (!agentOverviewScroll.Visible) return;
        var content = selectedAgentOverview.GetCombinedMinimumSize().Y;
        var rest = agentProfilePanel.GetCombinedMinimumSize().Y - agentOverviewScroll.CustomMinimumSize.Y;
        var room = Math.Max(120, UiSize.Y - HudTop - 14 - rest);
        var scrolls = content > room;
        agentOverviewGap.AddThemeConstantOverride("margin_right", scrolls ? SettingsScrollGap : 0);
        agentOverviewScroll.CustomMinimumSize = new Vector2(0, scrolls ? room : content);
    }
}
