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
    private readonly Button profileFindButton = new();
    private readonly Button profileCloseButton = new();
    private readonly GridContainer profileMeters = new() { Columns = 2 };
    private readonly PixelMeter profileFullnessMeter = new() { Kind = MeterKind.Fullness, Caption = "Fullness", CaptionWidth = 54 };
    private readonly PixelMeter profileWarmthMeter = new() { Kind = MeterKind.Warmth, Caption = "Warmth", CaptionWidth = 50 };
    private readonly PixelMeter profileDietMeter = new() { Kind = MeterKind.Diet, Caption = "Diet", CaptionWidth = 54 };
    private readonly PixelMeter profileIllnessMeter = new() { Kind = MeterKind.Illness, Caption = "Illness", CaptionWidth = 50 };
    private readonly Label thoughtsHeading = new() { ThemeTypeVariation = "SectionLabel" };
    private readonly VBoxContainer speakSection = new();
    private readonly Button instructionSuggestButton = new();
    private readonly Button instructionOrderButton = new();
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
        uiLayer.AddChild(selectedInhabitantCard);
        uiLayer.AddChild(agentProfilePanel);
        uiLayer.AddChild(thoughtsPanel);
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

        renameAgentInput.PlaceholderText = "Agent name";
        renameAgentInput.MaxLength = 48;
        renameAgentInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
        ConfigureTextPanel(privateThoughtHistory, 180);
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
        ConfigureTextPanel(inhabitantSocialDetails, 90);
        selectedAgentOverview.AddChild(inhabitantSocialDetails);

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
        var speakHeading = new HBoxContainer();
        speakHeading.AddChild(new Label
        {
            Text = "SPEAK TO THEM",
            ThemeTypeVariation = "SectionLabel",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var kind = new ButtonGroup();
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
            speakHeading.AddChild(button);
        }
        instructionSuggestButton.ButtonPressed = true;
        speakSection.AddChild(speakHeading);
        var speakRow = new HBoxContainer();
        speakRow.AddThemeConstantOverride("separation", 4);
        instructionText.PlaceholderText = "Say something…";
        instructionText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        instructionText.TextSubmitted += submitted => _ = SubmitInstructionAsync();
        speakRow.AddChild(instructionText);
        submitInstructionButton.Text = "Send";
        StyleButton(submitInstructionButton, primary: true);
        submitInstructionButton.Pressed += () => _ = SubmitInstructionAsync();
        speakRow.AddChild(submitInstructionButton);
        speakSection.AddChild(speakRow);
        selectedAgentOverview.AddChild(speakSection);
        body.AddChild(selectedAgentOverview);

        selectedAgentModelScroll.CustomMinimumSize = new Vector2(0, 300);
        selectedAgentModelScroll.AddChild(selectedAgentModelContent);
        selectedAgentModelScroll.Hide();
        body.AddChild(selectedAgentModelScroll);

        AddPanelContents(agentProfilePanel, body);
        agentProfilePanel.CustomMinimumSize = new Vector2(AgentProfileWidth, 0);
        // Top-bar panels such as Filters open over the Profile while in use.
        agentProfilePanel.ZIndex = 75;
        agentProfilePanel.Hide();
    }

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
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
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
            thoughtsReaderText.AddText(text);
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
            instructionText.CallDeferred(Control.MethodName.GrabFocus);
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
            renameRow.Hide();
            quickCardNameLabel.Text = string.Empty;
            selectedActorNameLabel.Text = string.Empty;
            selectedActorSummaryLabel.Text = string.Empty;
            selectedActorConditionLabel.Text = string.Empty;
            SetPanelText(inhabitantDetails, string.Empty);
            SetPanelText(inhabitantSocialDetails, string.Empty);
            SetPanelText(privateThoughtHistory, string.Empty);
            SetPanelText(memoryHistory, string.Empty);
            memoriesPanel.Hide();
            thoughtsPanel.Hide();
            selectedInhabitantCard.Hide();
            agentProfilePanel.Hide();
            return;
        }

        string? Factor(string key) => inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == key)?.Detail;
        var ageBand = Factor("age-band");
        var ageYears = Factor("age-years");
        var ageDays = Factor("age-days");
        var deathTick = Factor("death-tick");
        var deathCause = Factor("death-cause");
        var willStatus = Factor("will-status");
        var willHeir = Factor("will-heir");
        var role = Factor("role");
        var isDeceased = IsDeceased(inhabitant);
        var waitingForDecision = inhabitant.DecisionFactors.Any(factor => factor.Key == "decision-pending");
        var decision = snapshot.Cognition?.Decisions?.FirstOrDefault(item => item.InhabitantId == inhabitant.Id);
        if (selectedAgentModelScroll.Visible && SelectedCognitionTarget() != inhabitant.Id)
            CloseAgentModelEditor();

        // Name, age and one plain sentence for what they are doing, shared by both cards.
        quickCardNameLabel.Text = inhabitant.DisplayName;
        selectedActorNameLabel.Text = inhabitant.DisplayName;
        if (renamingAgentId != inhabitant.Id || !renameAgentInput.HasFocus())
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
        quickCardActivityLabel.Text = activity;
        profileActivityLabel.Text = activity;

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
            : "Carrying " + string.Join(", ", inhabitant.Inventory.Select(item => $"{Pretty(item.Kind).ToLowerInvariant()} ({item.Quantity})"));
        selectedActorConditionLabel.Text = isDeceased
            ? survival is null ? "Historical record" :
                $"At death · Warmth {survival.WarmthBasisPoints / 100}% · Illness {survival.IllnessBasisPoints / 100}% · Diet {survival.NutritionBasisPoints / 100}%"
            : survival is null ? carrying :
                $"{carrying} · {(survival.HasClothing ? "Clothed" : "No warm clothing")} · {(survival.HasTool ? "Has a tool" : "Working by hand")}";

        // What they are working on, learning and who chose their action.
        var details = new List<string>();
        if (inhabitant.Project is { } project)
        {
            details.Add($"{project.Label} · {Pretty(project.Stage)} · {project.WorkDone}/{project.WorkRequired}");
            if (project.Blocker is not null) details.Add(project.Blocker);
        }
        if (role is not null and not "unassigned") details.Add($"Role: {Pretty(role)}");
        if (inhabitant.Lesson is { } lesson)
            details.Add($"Learning {Pretty(lesson.Role)} with {lesson.TeacherName} · {Pretty(lesson.Stage)} · {lesson.Progress}/{lesson.Required}");
        if (inhabitant.Proficiency is { } practice)
            details.Add($"Practice · Building {practice.Building}/30 · Farming {practice.Farming}/30 · Crafting {practice.Crafting}/30");
        if (isDeceased)
        {
            details.Add(willStatus switch
            {
                "accepted" => $"Final will: personal estate to {willHeir}.",
                "pending" => "Final will pending.",
                "default" => "Personal estate follows household inheritance.",
                _ => "No current thoughts or activity.",
            });
        }
        else if (!waitingForDecision)
        {
            details.Add(decision is null ? "No decision yet"
                : decision.FellBack ? "The model gave no usable choice, so built-in rules chose this."
                : $"Chosen by {ProviderDisplayName(decision.Provider)}");
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
        SetPanelText(privateThoughtHistory, inhabitant.RecentPrivateThoughts.Count == 0
            ? "None recorded yet."
            : string.Join("\n", inhabitant.RecentPrivateThoughts.Reverse()
                .Select(thought => $"{ThoughtTime(thought.WorldTick, snapshot.WorldTick)}  {thought.Text}")));

        var people = inhabitant.Relationships.Select(relationship => GameUiText.RelationshipSummary(
                relationship.Type, relationship.State, GameUiText.PartyName(snapshot, relationship.OtherPartyId), relationship.Direction))
            .Concat(inhabitant.SocialStanding.Select(item => $"Trusts {item.SubjectName} · {item.Trust} of 10"))
            .Concat(inhabitant.SocialNotes)
            .ToArray();
        SetPanelText(inhabitantSocialDetails, people.Length == 0 ? "No close relationships yet." : string.Join("\n", people));
        RenderMemoryHistory(snapshot, inhabitant);
        RenderThoughtsReader(snapshot, inhabitant);

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

    /// <summary>What this agent remembers, believes and has mapped, newest first, for the Memories panel.</summary>
    private void RenderMemoryHistory(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant inhabitant)
    {
        var memoryRows = new List<(long WorldTick, int Kind, string Text)>();
        memoryRows.AddRange(inhabitant.RecentBeliefs.Select(belief =>
        {
            var evidence = belief.Provenance switch
            {
                "firsthand" => "witnessed",
                "hearsay" when belief.SourceAgentName is { } source => $"heard from {source}",
                "hearsay" => "heard from someone",
                _ => "inferred",
            };
            var subject = belief.AboutInhabitantId is { } subjectId
                ? snapshot.Inhabitants.FirstOrDefault(person => person.Id == subjectId)?.DisplayName
                : null;
            var context = $"Belief · {evidence} · {belief.ConfidenceBasisPoints / 100}% sure" +
                (subject is null ? "" : $" · about {subject}") +
                (belief.IsCorrected
                    ? $" · corrected{(belief.CorrectedTick is { } correctedTick ? $" at {DisplayWorldClock(correctedTick)}" : "")}" : "");
            return (belief.WorldTick, 0,
                $"{DisplayWorldClock(belief.WorldTick)} · {context}\n{belief.Statement}");
        }));
        memoryRows.AddRange(inhabitant.RecentMemories.Select(memory =>
            (memory.WorldTick, 1,
                $"{DisplayWorldClock(memory.WorldTick)} · {Pretty(memory.Visibility)} · about {memory.SubjectName}\n{memory.Summary}")));
        memoryRows.AddRange(inhabitant.RecentKnowledgeFacts.Select(fact =>
        {
            var acquisition = fact.Acquisition == "firsthand"
                ? $"discovered by {fact.DiscovererName}"
                : $"{Pretty(fact.Acquisition)} from {fact.SourceAgentName ?? "another agent"}; discovered by {fact.DiscovererName}";
            var resources = fact.ResourceKinds.Count == 0 ? "no recorded resource site" :
                "resources · " + string.Join(", ", fact.ResourceKinds.Select(Pretty));
            return (fact.WorldTick, 2,
                $"{DisplayWorldClock(fact.WorldTick)} · Map fact · {acquisition}\n" +
                $"{Pretty(fact.Terrain)} at ({fact.X}, {fact.Y}) · {resources}");
        }));
        memoryRows.AddRange(inhabitant.KnowledgeArtifacts.Select(artifact =>
        {
            var sites = string.Join("\n", artifact.Sites.Select(site =>
                $"  {Pretty(site.Terrain)} at ({site.X}, {site.Y})" +
                (site.ResourceKinds.Count == 0 ? "" : " · " + string.Join(", ", site.ResourceKinds.Select(Pretty)))));
            return (artifact.CreatedTick, 3,
                $"{DisplayWorldClock(artifact.CreatedTick)} · {Pretty(artifact.Kind)} · {artifact.Title} · by {artifact.CreatorName}\n{sites}");
        }));
        SetPanelText(memoryHistory, memoryRows.Count == 0
            ? "No saved memories, beliefs, or map records for this agent yet."
            : string.Join("\n\n", memoryRows.OrderByDescending(item => item.WorldTick)
                .ThenBy(item => item.Kind).Select(item => item.Text)));
    }

    /// <summary>The Profile docks on the left, just below the top bar, and fits its contents.</summary>
    private void PositionAgentProfile()
    {
        if (!agentProfilePanel.Visible) return;
        agentProfilePanel.CustomMinimumSize = new Vector2(Math.Min(AgentProfileWidth, Math.Max(1, UiSize.X - 28)), 0);
        agentProfilePanel.Size = agentProfilePanel.GetCombinedMinimumSize();
        agentProfilePanel.Position = new Vector2(14, HudTop);
    }
}
