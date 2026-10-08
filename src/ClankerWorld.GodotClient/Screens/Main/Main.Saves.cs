using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.UI;
using System.Globalization;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Control manualSaveOverlay = new();
    private readonly PanelContainer manualSaveCard = new();
    private readonly Label manualSaveHeading = new();
    private readonly Label manualSaveStatus = new();
    private readonly LineEdit manualSaveName = new();
    private readonly Button manualSaveCreateButton = new();
    private readonly Button manualSaveBackButton = new();
    private readonly SlotList manualSaveList = new();
    private readonly PanelContainer manualSaveNewBox = new();
    private readonly Button manualSaveLoadButton = new();
    private readonly Button manualSaveOverwriteButton = new();
    private readonly ConfirmationDialog manualSaveLoadConfirmation = new();
    private readonly ConfirmationDialog manualSaveOverwriteConfirmation = new();
    private readonly Button manualSaveDeleteButton = new();
    private readonly ConfirmationDialog deletionConfirmation = new();
    private OwnerDeletionAction? pendingDeletion;
    private string? listedSaveWorldId;
    private string? pendingOverwriteSaveId;
    private CancellationTokenSource? manualSaveListCancellation;
    private ManualWorldSave[] listedManualSaves = [];
    private ManualWorldSave[] allListedManualSaves = [];
    private bool manualSaveLoadMode;
    private readonly SaveTimeline manualSaveTimeline = new();
    private readonly HBoxContainer manualSaveViewRow = new();
    private readonly HBoxContainer manualSaveKey = new();
    private readonly Label manualSaveViewLabel = new() { Text = "SAVES", ThemeTypeVariation = "SectionLabel" };
    private readonly SegmentedChoice manualSaveViewChoice = new();
    private readonly Label manualSaveSectionLabel = new() { Text = "SAVES", ThemeTypeVariation = "SectionLabel" };
    private readonly PanelContainer manualSaveDetails = new();
    private SaveTimelinePosition? listedTimelinePosition;
    private bool manualSaveShowsList;
    private readonly CheckBox autosaveEnabledToggle = new();
    private readonly OptionButton autosaveIntervalChoice = new();
    private readonly OptionButton autosaveRotationChoice = new();
    private readonly Button autosaveApplyButton = new();
    private readonly Label autosaveSettingsStatus = new();
    private bool autosaveSettingsLoaded;
    private string? autosaveSettingsWorldId;
    private OwnerDeviceRegistration? autosaveSettingsRegistration;
    private long autosaveSettingsGeneration;
    private CancellationTokenSource? autosaveSettingsCancellation;

    private void BuildAutosaveSettings()
    {
        void CancelHiddenSettings()
        {
            if (!settingsPanel.IsVisibleInTree() || !worldSettingsContent.IsVisibleInTree())
                CancelAutosaveSettingsRead();
        }
        settingsPanel.VisibilityChanged += CancelHiddenSettings;
        worldSettingsContent.VisibilityChanged += CancelHiddenSettings;
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 6);
        autosaveEnabledToggle.Text = "Autosave enabled";
        autosaveEnabledToggle.ButtonPressed = true;
        content.AddChild(autosaveEnabledToggle);
        foreach (var minutes in new[] { 1, 2, 5, 10, 15, 30 })
            autosaveIntervalChoice.AddItem(minutes + " minutes", minutes);
        autosaveIntervalChoice.Select(autosaveIntervalChoice.GetItemIndex(5));
        content.AddChild(DisplaySettingRow("Every", autosaveIntervalChoice));
        autosaveRotationChoice.AddItem("Off (keep latest)", 0);
        foreach (var count in new[] { 3, 5, 10 })
            autosaveRotationChoice.AddItem(count.ToString(CultureInfo.InvariantCulture), count);
        autosaveRotationChoice.Select(autosaveRotationChoice.GetItemIndex(5));
        content.AddChild(DisplaySettingRow("Rotating copies", autosaveRotationChoice));
        autosaveSettingsStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        autosaveSettingsStatus.Text = "Loading this world's autosave settings...";
        content.AddChild(autosaveSettingsStatus);
        autosaveApplyButton.Text = "Apply autosave settings";
        StyleButton(autosaveApplyButton);
        autosaveApplyButton.Pressed += () => _ = ApplyAutosaveSettingsAsync();
        content.AddChild(autosaveApplyButton);
        worldSettingsContent.AddChild(NewPanel("Autosave", content));
    }

    private bool IsCurrentAutosaveSettingsContext() =>
        settingsPanel.IsVisibleInTree() && worldSettingsContent.IsVisibleInTree() &&
        autosaveSettingsWorldId is not null &&
        IsCurrentWorldRequest(autosaveSettingsGeneration) &&
        autosaveSettingsWorldId == observationSession.Current?.Baseline.Snapshot.WorldId &&
        ReferenceEquals(autosaveSettingsRegistration, registration);

    private void CancelAutosaveSettingsRead()
    {
        var previous = autosaveSettingsCancellation;
        autosaveSettingsCancellation = null;
        autosaveSettingsLoaded = false;
        autosaveSettingsWorldId = null;
        autosaveSettingsRegistration = null;
        autosaveApplyButton.Disabled = true;
        previous?.Cancel();
    }

    private async Task RefreshAutosaveSettingsAsync(Func<CancellationToken, Task<WorldAutosaveSettings>>? fetch = null)
    {
        CancelAutosaveSettingsRead();
        if (!settingsPanel.IsVisibleInTree() || !worldSettingsContent.IsVisibleInTree() ||
            observationSession.Current?.Baseline.Snapshot.WorldId is not { } readWorldId ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        using var read = new CancellationTokenSource();
        autosaveSettingsCancellation = read;
        autosaveSettingsWorldId = readWorldId;
        autosaveSettingsRegistration = registration;
        autosaveSettingsGeneration = observationSession.RequestGeneration;
        autosaveSettingsStatus.Text = "Loading this world's autosave settings...";
        RefreshControlAvailability();
        bool IsCurrentRead() => ReferenceEquals(autosaveSettingsCancellation, read) &&
            IsCurrentAutosaveSettingsContext();
        try
        {
            var saved = await (fetch?.Invoke(read.Token) ?? ownerApi.GetAutosaveSettingsAsync(ResolveWorldUri(), authority,
                deviceId, signer, read.Token));
            if (!IsCurrentRead()) return;
            if (saved.WorldId != readWorldId)
            {
                autosaveSettingsStatus.Text = "The world changed. Reopen World Settings to read its autosaves.";
                return;
            }
            autosaveEnabledToggle.ButtonPressed = saved.Enabled;
            autosaveIntervalChoice.Select(autosaveIntervalChoice.GetItemIndex(saved.IntervalMinutes));
            autosaveRotationChoice.Select(autosaveRotationChoice.GetItemIndex(saved.RotationCount));
            autosaveSettingsLoaded = true;
            autosaveSettingsStatus.Text = saved.LastWorldTick < 0
                ? "No autosave copy yet. Your world is still saved as you play."
                : $"Last autosave copy: {DisplayWorldClock(saved.LastWorldTick)}. Your world is saved as you play.";
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentRead())
                autosaveSettingsStatus.Text = "Could not read autosave settings: " + FriendlyFailure(exception);
        }
        finally
        {
            if (ReferenceEquals(autosaveSettingsCancellation, read)) autosaveSettingsCancellation = null;
            RefreshControlAvailability();
        }
    }

    private async Task ApplyAutosaveSettingsAsync()
    {
        if (!autosaveSettingsLoaded || autosaveSettingsWorldId is not { } worldId || !IsCurrentAutosaveSettingsContext() ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var action = new OwnerAutosaveConfigurationAction(autosaveEnabledToggle.ButtonPressed,
            autosaveIntervalChoice.GetSelectedId(), autosaveRotationChoice.GetSelectedId(), worldId);
        await RunOwnerActionAsync(async () =>
        {
            var updated = await AwaitCurrentWorldResultAsync(ownerApi.ConfigureAutosaveAsync(ResolveWorldUri(), authority,
                deviceId, action, signer, CancellationToken.None));
            autosaveSettingsStatus.Text = updated.Enabled
                ? $"Autosave every {updated.IntervalMinutes} minutes; " +
                  (updated.RotationCount == 0 ? "keep latest only." : $"keep {updated.RotationCount} copies.")
                : "Autosave off. Your world is still saved as you play.";
            return "Autosave settings saved for this world.";
        });
    }

    private void BuildManualSavesPanel()
    {
        manualSaveOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        manualSaveOverlay.MouseFilter = MouseFilterEnum.Stop;
        manualSaveOverlay.VisibilityChanged += () =>
        {
            if (!manualSaveOverlay.Visible) CancelManualSaveListRead();
        };
        manualSaveOverlay.ZIndex = 220;
        menuLayer.AddChild(manualSaveOverlay);
        var shade = new ColorRect { Color = UiTheme.Current.Shade with { A = 0.78f }, MouseFilter = MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        manualSaveOverlay.AddChild(shade);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        manualSaveOverlay.AddChild(center);
        center.AddChild(manualSaveCard);
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
        body.AddThemeConstantOverride("separation", 10);
        var headingRow = new HBoxContainer();
        manualSaveHeading.ThemeTypeVariation = "TitleLabel";
        manualSaveHeading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headingRow.AddChild(manualSaveHeading);
        manualSaveBackButton.TooltipText = "Back to the Pause Menu";
        StyleIconButton(manualSaveBackButton, PixelGlyph.Back);
        manualSaveBackButton.Pressed += () => manualSaveOverlay.Hide();
        headingRow.AddChild(manualSaveBackButton);
        body.AddChild(headingRow);
        manualSaveStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(manualSaveStatus);
        // A new save opens already named after the world's date, so one click saves.
        var newSave = new HBoxContainer();
        newSave.AddThemeConstantOverride("separation", 8);
        manualSaveName.PlaceholderText = "Name this save";
        manualSaveName.MaxLength = 80;
        manualSaveName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        manualSaveName.TextSubmitted += name => _ = CreateManualSaveAsync();
        newSave.AddChild(manualSaveName);
        manualSaveCreateButton.Text = "Save";
        StyleButton(manualSaveCreateButton, primary: true);
        manualSaveCreateButton.CustomMinimumSize = new Vector2(110, 34);
        manualSaveCreateButton.Pressed += () => _ = CreateManualSaveAsync();
        newSave.AddChild(manualSaveCreateButton);
        AddPanelContents(manualSaveNewBox, "New save", newSave);
        manualSaveNewBox.ThemeTypeVariation = "InsetPanel";
        body.AddChild(manualSaveNewBox);
        BuildManualSaveTimeline(body);
        body.AddChild(manualSaveSectionLabel);
        manualSaveList.CustomMinimumSize = new Vector2(0, 250);
        manualSaveList.ItemSelected += index =>
        {
            manualSaveTimeline.Select(index >= 0 && index < listedManualSaves.Length ? listedManualSaves[index].Id : null);
            RenderManualSaveDetails();
            RefreshManualSaveAvailability();
        };
        manualSaveList.ItemActivated += _ =>
        {
            if (manualSaveLoadMode) ConfirmManualSaveLoad();
        };
        body.AddChild(manualSaveList);
        // Deleting sits apart on the left, away from the main action on the right.
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        manualSaveDeleteButton.Text = "Delete";
        StyleButton(manualSaveDeleteButton);
        manualSaveDeleteButton.Pressed += ConfirmSaveDeletion;
        actions.AddChild(manualSaveDeleteButton);
        actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        manualSaveOverwriteButton.Text = "Overwrite";
        manualSaveOverwriteButton.TooltipText = "Replace the chosen save with the world as it is now. The old version is kept as a recovery copy.";
        StyleButton(manualSaveOverwriteButton);
        manualSaveOverwriteButton.Pressed += ConfirmManualSaveOverwrite;
        actions.AddChild(manualSaveOverwriteButton);
        manualSaveLoadButton.Text = "Load";
        StyleButton(manualSaveLoadButton, primary: true);
        manualSaveLoadButton.CustomMinimumSize = new Vector2(110, 34);
        manualSaveLoadButton.Pressed += ConfirmManualSaveLoad;
        actions.AddChild(manualSaveLoadButton);
        body.AddChild(actions);
        StyleConfirmation(deletionConfirmation, "Permanently delete?", "Delete permanently");
        deletionConfirmation.GetOkButton().ThemeTypeVariation = "DangerButton";
        deletionConfirmation.Confirmed += () => _ = DeleteConfirmedAsync();
        deletionConfirmation.Canceled += () => pendingDeletion = null;
        AddChild(deletionConfirmation);
        AddPanelContents(manualSaveCard, body);
        manualSaveCard.CustomMinimumSize = new Vector2(520, 0);
        StyleConfirmation(manualSaveLoadConfirmation, "Load this save?", "Load Save");
        manualSaveLoadConfirmation.Confirmed += () => _ = LoadSelectedManualSaveAsync();
        AddChild(manualSaveLoadConfirmation);
        StyleConfirmation(manualSaveOverwriteConfirmation, "Overwrite this save?", "Overwrite");
        manualSaveOverwriteConfirmation.Confirmed += () => _ = OverwriteSelectedManualSaveAsync();
        AddChild(manualSaveOverwriteConfirmation);
        manualSaveOverlay.Resized += () =>
        {
            if (manualSaveOverlay.Visible) ApplyManualSaveView();
        };
        manualSaveOverlay.Hide();
    }

    /// <summary>
    /// Load Save's timeline: a key to its points beside the Timeline / List
    /// switch, the timeline, and a card describing the chosen save underneath.
    /// </summary>
    private void BuildManualSaveTimeline(VBoxContainer body)
    {
        manualSaveViewRow.AddThemeConstantOverride("separation", 6);
        manualSaveKey.AddThemeConstantOverride("separation", 6);
        foreach (var (kind, text) in new[]
        {
            (SaveTimelineKeyKind.Newest, "Newest on its branch"),
            (SaveTimelineKeyKind.Save, "Save"),
            (SaveTimelineKeyKind.Autosave, "Autosave"),
        })
        {
            if (kind != SaveTimelineKeyKind.Newest) manualSaveKey.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
            manualSaveKey.AddChild(new SaveTimelineKeyIcon { Kind = kind });
            manualSaveKey.AddChild(new Label { Text = text, ThemeTypeVariation = "DimLabel", SizeFlagsVertical = SizeFlags.ShrinkCenter });
        }
        manualSaveViewRow.AddChild(manualSaveKey);
        manualSaveViewLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        manualSaveViewRow.AddChild(manualSaveViewLabel);
        manualSaveViewRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        manualSaveViewChoice.AddItem("Timeline");
        manualSaveViewChoice.AddItem("List");
        manualSaveViewChoice.SetItemTooltip(0, "Show each branch of this world's history as a line over the seasons.");
        manualSaveViewChoice.SetItemTooltip(1, "Show the saves as a list, grouped by branch.");
        manualSaveViewChoice.Select(0);
        manualSaveViewChoice.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        manualSaveViewChoice.ItemSelected += index =>
        {
            manualSaveShowsList = index == 1;
            ApplyManualSaveView();
        };
        manualSaveViewRow.AddChild(manualSaveViewChoice);
        body.AddChild(manualSaveViewRow);
        manualSaveTimeline.SaveChosen += ChooseTimelineSave;
        // Double-click loads in Load Save, as in the list.
        manualSaveTimeline.SaveActivated += id =>
        {
            ChooseTimelineSave(id);
            if (manualSaveLoadMode) ConfirmManualSaveLoad();
        };
        body.AddChild(manualSaveTimeline);
        manualSaveDetails.ThemeTypeVariation = "InsetRow";
        // Wrapped text has its height only after layout, so the timeline refits then.
        manualSaveDetails.Resized += () => Callable.From(FitManualSaveTimeline).CallDeferred();
        body.AddChild(manualSaveDetails);
    }

    /// <summary>Whether Load Save or Save World is drawing its timeline rather than the list.</summary>
    private bool ManualSaveTimelineShown => !manualSaveShowsList && allListedManualSaves.Length > 0;

    /// <summary>
    /// Shows the timeline or the list. The panel is wide enough for the timeline,
    /// and the timeline gives up height so the panel stays on screen.
    /// </summary>
    private void ApplyManualSaveView()
    {
        var timeline = ManualSaveTimelineShown;
        var switchable = allListedManualSaves.Length > 0;
        manualSaveViewRow.Visible = switchable;
        manualSaveKey.Visible = timeline;
        manualSaveViewLabel.Visible = switchable && !timeline;
        manualSaveSectionLabel.Visible = !switchable;
        // The card under the timeline carries the status line's advice instead.
        manualSaveStatus.Visible = !timeline;
        manualSaveViewChoice.Select(manualSaveShowsList ? 1 : 0);
        manualSaveTimeline.Visible = timeline;
        manualSaveDetails.Visible = timeline;
        manualSaveList.Visible = !timeline;
        manualSaveCard.CustomMinimumSize = new Vector2(ManualSaveTimelineWidth(), 0);
        if (timeline) Callable.From(FitManualSaveTimeline).CallDeferred();
    }

    private float ManualSaveTimelineWidth() => MathF.Floor(Math.Clamp(manualSaveOverlay.Size.X - 40, 520, 900));

    // The card's frame and padding around its contents, which stay the same at any width.
    private float ManualSaveCardFrame() => manualSaveTimeline.GetParent() is Control body && body.Size.X > 0 && manualSaveCard.Size.X > body.Size.X
        ? manualSaveCard.Size.X - body.Size.X
        : 40;

    private void FitManualSaveTimeline()
    {
        if (!manualSaveTimeline.IsVisibleInTree()) return;
        var others = manualSaveCard.GetCombinedMinimumSize().Y - manualSaveTimeline.GetCombinedMinimumSize().Y;
        manualSaveTimeline.SetMaximumHeight(manualSaveOverlay.Size.Y - 16 - others);
    }

    private void ChooseTimelineSave(string id)
    {
        var index = Array.FindIndex(listedManualSaves, save => save.Id == id);
        if (index < 0) return;
        manualSaveList.Select(index);
        RenderManualSaveDetails();
        RefreshManualSaveAvailability();
    }

    private ManualWorldSave? SelectedManualSave() =>
        manualSaveList.GetSelectedItems() is [var index] && index >= 0 && index < listedManualSaves.Length
            ? listedManualSaves[index]
            : null;

    /// <summary>The chosen save, or where the running world is while no save is chosen.</summary>
    private void RenderManualSaveDetails()
    {
        foreach (var child in manualSaveDetails.GetChildren())
        {
            manualSaveDetails.RemoveChild(child);
            child.QueueFree();
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        var chosen = SelectedManualSave();
        var lane = chosen is null ? null : manualSaveTimeline.Lanes.FirstOrDefault(item => item.Points.Contains(chosen));
        Label Line(string value, string variation = "") => new()
        {
            Text = value,
            ThemeTypeVariation = variation,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(360, 0),
        };
        if (chosen is null || lane is null)
        {
            var readout = TimelineReadout();
            row.AddChild(new ColorRect { Color = readout is null ? UiTheme.Current.InkFaint : UiTheme.Current.Ember, CustomMinimumSize = new Vector2(4, 0) });
            if (readout is not null)
            {
                text.AddChild(new Label { Text = "You are here", ThemeTypeVariation = "HeadingLabel" });
                text.AddChild(Line(readout, "DimLabel"));
            }
            text.AddChild(Line(manualSaveLoadMode
                    ? "Choose a save to see it here, and double-click to load it. Your current world is saved first."
                    : "Name your save above and choose Save, or choose a save on the timeline to replace it.",
                readout is null ? "" : "DimLabel"));
        }
        else
        {
            var color = SaveTimelineLayout.BranchColor(lane.ColorNumber);
            var latest = lane.IsLatest(chosen);
            row.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(4, 0) });
            var titleRow = new HBoxContainer();
            titleRow.AddThemeConstantOverride("separation", 8);
            var title = new Label { ThemeTypeVariation = "HeadingLabel" };
            titleRow.AddChild(title);
            var branchTag = new Label
            {
                Text = ShortBranchTag(lane.Title),
                TooltipText = lane.Title,
                ThemeTypeVariation = "TagLabel",
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Pass,
            };
            branchTag.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = color,
                ContentMarginLeft = 5,
                ContentMarginRight = 5,
                ContentMarginTop = 2,
                ContentMarginBottom = 2,
            });
            branchTag.AddThemeColorOverride("font_color", UiTheme.Current.Paper);
            titleRow.AddChild(branchTag);
            if (latest) titleRow.AddChild(new Label { Text = "LATEST", ThemeTypeVariation = "TagNoteLabel", SizeFlagsVertical = SizeFlags.ShrinkCenter });
            if (chosen.IsAutosave) titleRow.AddChild(new Label { Text = "AUTOMATIC", ThemeTypeVariation = "TagNoteLabel", SizeFlagsVertical = SizeFlags.ShrinkCenter });
            text.AddChild(titleRow);
            var name = chosen.IsAutosave ? "Autosave" : chosen.Name;
            var font = title.GetThemeFont("font");
            var size = title.GetThemeFontSize("font_size");
            title.Text = SaveTimelineLayout.Shorten(name, Math.Max(120, manualSaveCard.CustomMinimumSize.X - 300),
                value => font.GetStringSize(value, HorizontalAlignment.Left, -1, size).X);
            if (title.Text != name) title.TooltipText = name;
            var began = lane.Branch?.StartedFromName ?? lane.ForkSave?.Name;
            var origin = began is null ? string.Empty : $" · {lane.Title} began at \"{began}\"";
            text.AddChild(Line($"{DisplayWorldClock(chosen.WorldTick)} · Saved {GameUiText.SavedAgo(chosen.CreatedUtc, DateTimeOffset.Now)}{origin}", "DimLabel"));
            var grown = SaveTimelineLayout.BranchesFrom(manualSaveTimeline.Lanes, chosen);
            var grew = grown switch
            {
                0 => string.Empty,
                1 => "Another branch grew from here. ",
                _ => $"{grown.ToString(CultureInfo.InvariantCulture)} other branches grew from here. ",
            };
            text.AddChild(Line(!manualSaveLoadMode
                ? $"{grew}Overwrite replaces it with the world as it is now and keeps the old version as a recovery copy."
                : latest
                ? $"{grew}Playing on from here continues {lane.Title}."
                : $"{grew}Playing on from it starts a new branch; your later saves stay as they are."));
        }
        row.AddChild(text);
        manualSaveDetails.AddChild(row);
        if (manualSaveTimeline.IsVisibleInTree()) Callable.From(FitManualSaveTimeline).CallDeferred();
    }

    /// <summary>Where the running world continues, in words, or null when the host did not say.</summary>
    private string? TimelineReadout()
    {
        if (listedTimelinePosition is not { } position || manualSaveTimeline.NowLane is not { } lane) return null;
        var from = position.ContinuedFromId is { } id ? allListedManualSaves.FirstOrDefault(save => save.Id == id) : null;
        var playing = from is null ? string.Empty : $"Playing on from \"{(from.IsAutosave ? "Autosave" : from.Name)}\". ";
        var next = manualSaveLoadMode ? "Your next save" : "Your new save";
        var newBranch = from is not null ? $"\"From {from.Name}\"" : position.NextBranchNumber is > 0
            ? $"Branch {position.NextBranchNumber.Value.ToString(CultureInfo.InvariantCulture)}"
            : "a new branch";
        return lane.IsUnsaved
            ? $"{playing}{next} starts {newBranch}, and your other saves stay as they are."
            : $"{playing}{next} continues {lane.Title}.";
    }

    /// <summary>Asks the host where the running world continues. An older host cannot say, so the timeline marks nothing.</summary>
    private static async Task<SaveTimelinePosition?> ReadTimelinePositionAsync(
        Func<CancellationToken, Task<SaveTimelinePosition?>> fetch, CancellationToken cancellationToken)
    {
        try
        {
            return await fetch(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"save_timeline_position_unavailable type={exception.GetType().Name}");
            return null;
        }
    }

    private void CancelManualSaveListRead()
    {
        var previous = manualSaveListCancellation;
        manualSaveListCancellation = null;
        previous?.Cancel();
    }

    private void RefreshManualSaveAvailability()
    {
        var selected = manualSaveList.GetSelectedItems();
        var valid = !isOwnerAction && !observationSession.AwaitingFreshBaseline &&
            selected.Length == 1 && selected[0] >= 0 && selected[0] < listedManualSaves.Length;
        if (selected.Length == 0 && manualSaveTimeline.SelectedId is not null)
        {
            manualSaveTimeline.Select(null);
            RenderManualSaveDetails();
        }
        manualSaveLoadButton.Disabled = !manualSaveLoadMode || !valid;
        manualSaveOverwriteButton.Disabled = manualSaveLoadMode || !valid || listedManualSaves[selected[0]].IsAutosave;
        manualSaveDeleteButton.Disabled = !valid || listedSaveWorldId is null;
        manualSaveCreateButton.Disabled = isOwnerAction || manualSaveLoadMode || observationSession.AwaitingFreshBaseline;
    }

    private string? CurrentManualSaveWorldId()
    {
        if (observationSession.Current is { } current) return current.Baseline.Snapshot.WorldId;
        var catalog = worldListRequest.Catalog;
        if (catalog is null) return null;
        return catalog.Worlds.FirstOrDefault(world => world.Id == catalog.ActiveId)?.WorldId;
    }

    /// <summary>
    /// Opens Save World or Load Save and reads the world's saves, and asks where
    /// the running world continues, for the timeline's You are here marker. A
    /// test that supplies the save list supplies that answer too, or none.
    /// </summary>
    private async Task OpenManualSavesAsync(bool loadMode, Func<CancellationToken, Task<ManualWorldSave[]>>? fetch = null,
        Func<CancellationToken, Task<SaveTimelinePosition?>>? fetchPosition = null)
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        if (!loadMode && observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused != true)
        {
            SetStatus("Wait for the world to pause before saving.", good: false);
            return;
        }
        CancelManualSaveListRead();
        using var read = new CancellationTokenSource();
        manualSaveListCancellation = read;
        var readWorldId = CurrentManualSaveWorldId();
        var readRegistration = registration;
        var readGeneration = observationSession.RequestGeneration;
        pendingDeletion = null;
        listedSaveWorldId = readWorldId;
        ShowManualSavePanel(loadMode);
        bool IsCurrentRead() => ReferenceEquals(manualSaveListCancellation, read) &&
            IsCurrentWorldRequest(readGeneration) &&
            manualSaveOverlay.Visible && ReferenceEquals(registration, readRegistration) &&
            readWorldId == CurrentManualSaveWorldId();
        fetchPosition ??= fetch is not null ? _ => Task.FromResult<SaveTimelinePosition?>(null)
            : async token => await ownerApi.GetSaveTimelinePositionAsync(ResolveWorldUri(), authority, deviceId, signer, token);
        var position = ReadTimelinePositionAsync(fetchPosition, read.Token);
        try
        {
            var saves = await (fetch?.Invoke(read.Token) ?? ownerApi.ListManualSavesAsync(ResolveWorldUri(), authority,
                deviceId, signer, read.Token));
            var timelinePosition = await position;
            if (!IsCurrentRead()) return;
            allListedManualSaves = saves;
            listedManualSaves = OrderSavesByBranch(saves.Where(save => loadMode || !save.IsAutosave));
            listedTimelinePosition = timelinePosition;
            RenderManualSaveList();
            RenderManualSaveTimeline();
            if (listedManualSaves.Length == 0)
                manualSaveStatus.Text = loadMode
                    ? "No saves yet. Continue the world and use Pause Menu → Save World."
                    : "No named saves yet. Name one above and choose Save.";
            RefreshManualSaveAvailability();
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentRead())
            {
                manualSaveStatus.Text = "Could not list saves: " + FriendlyFailure(exception);
                manualSaveList.Placeholder = "No saves to show.";
            }
        }
        finally
        {
            if (ReferenceEquals(manualSaveListCancellation, read)) manualSaveListCancellation = null;
        }
    }

    /// <summary>Opens Save World or Load Save with an empty list while the saves are read.</summary>
    private void ShowManualSavePanel(bool loadMode)
    {
        listedManualSaves = [];
        allListedManualSaves = [];
        listedTimelinePosition = null;
        manualSaveList.Clear();
        manualSaveDeleteButton.Disabled = true;
        manualSaveLoadMode = loadMode;
        manualSaveHeading.Text = loadMode ? "Load Save" : "Save World";
        manualSaveBackButton.TooltipText = loadMode ? "Back to Load World" : "Back to the Pause Menu";
        manualSaveStatus.Text = loadMode
            ? "Choose a save to load. Your current world is saved first."
            : "Save the world as it is now, or choose a save to replace.";
        manualSaveNewBox.Visible = !loadMode;
        if (!loadMode && observationSession.Current?.Baseline.Snapshot is { } current)
            manualSaveName.Text = DisplayWorldClock(current.WorldTick);
        manualSaveName.Visible = !loadMode;
        manualSaveCreateButton.Visible = !loadMode;
        manualSaveList.Placeholder = "Checking saves...";
        manualSaveList.Visible = true;
        manualSaveLoadButton.Visible = loadMode;
        manualSaveOverwriteButton.Visible = !loadMode;
        manualSaveLoadButton.Disabled = true;
        manualSaveOverwriteButton.Disabled = true;
        manualSaveOverlay.Show();
        ApplyManualSaveView();
        RefreshManualSaveAvailability();
    }

    /// <summary>
    /// Draws the world's saves on the timeline, nothing chosen yet. Save World
    /// shows autosaves too, for where the world has been, but they cannot be
    /// overwritten, so they cannot be chosen there.
    /// </summary>
    private void RenderManualSaveTimeline()
    {
        if (allListedManualSaves.Length > 0)
            manualSaveTimeline.Show(allListedManualSaves, listedTimelinePosition, SaveTimelineCalendar.From(observedCalendarPace),
                observationSession.Current?.Baseline.Snapshot.WorldTick ?? allListedManualSaves.Max(save => save.WorldTick),
                ManualSaveTimelineWidth() - ManualSaveCardFrame(), manualSaveLoadMode ? null : save => !save.IsAutosave);
        ApplyManualSaveView();
        RenderManualSaveDetails();
    }

    /// <summary>
    /// Each save is a card with its name, the world's date when it was made and how long ago that was.
    /// When a world has more than one branch, every card names its branch and each branch's newest point is marked.
    /// </summary>
    private void RenderManualSaveList()
    {
        manualSaveList.Clear();
        var icon = SlotIcon(PixelGlyph.Book);
        var showBranches = allListedManualSaves.Select(BranchKey).Distinct(StringComparer.Ordinal).Count() > 1;
        string? previousBranch = null;
        foreach (var save in listedManualSaves)
        {
            var detail = $"{DisplayWorldClock(save.WorldTick)} · Saved {GameUiText.SavedAgo(save.CreatedUtc, DateTimeOffset.Now)}";
            List<SlotTag> tags = [];
            if (showBranches)
            {
                var branch = BranchLabel(save.Branch);
                tags.Add(new SlotTag(ShortBranchTag(branch), Tooltip: branch));
                if (IsLatestInBranch(save, allListedManualSaves)) tags.Add(new SlotTag("Latest"));
                // The first card of each branch says where that branch began.
                if (BranchKey(save) != previousBranch && save.Branch?.StartedFromName is { } from)
                    detail += $" · Branched from \"{from}\"";
            }
            previousBranch = BranchKey(save);
            if (save.IsAutosave) tags.Add(new SlotTag("Automatic", Note: true));
            manualSaveList.AddItem(save.IsAutosave ? "Autosave" : save.Name, detail, icon, tags);
        }
        manualSaveList.Placeholder = manualSaveLoadMode ? "No saves yet." : "No named saves yet.";
    }

    private static string BranchKey(ManualWorldSave save) => save.Branch?.Id ?? string.Empty;

    internal static string BranchLabel(SaveBranch? branch) =>
        SaveTimelineLayout.BranchLabel(branch);

    private string ShortBranchTag(string label)
    {
        var font = manualSaveCard.GetThemeFont("font", "TagLabel");
        var size = manualSaveCard.GetThemeFontSize("font_size", "TagLabel");
        return SaveTimelineLayout.Shorten(label.ToUpperInvariant(), 160,
            value => font.GetStringSize(value, HorizontalAlignment.Left, -1, size).X);
    }

    /// <summary>
    /// Saves grouped by branch: the branch with the most recent save first, and
    /// each branch's history newest first, so its latest point leads the group.
    /// </summary>
    internal static ManualWorldSave[] OrderSavesByBranch(IEnumerable<ManualWorldSave> saves) =>
        saves.GroupBy(BranchKey, StringComparer.Ordinal)
            .OrderByDescending(branch => branch.Max(save => save.CreatedUtc))
            .ThenBy(branch => branch.Key, StringComparer.Ordinal)
            .SelectMany(branch => branch.OrderByDescending(save => save.BranchPosition)
                .ThenByDescending(save => save.WorldTick)
                .ThenByDescending(save => save.CreatedUtc)
                .ThenBy(save => save.Id, StringComparer.Ordinal))
            .ToArray();

    /// <summary>Whether playing on from this save continues its branch rather than starting a new one.</summary>
    internal static bool IsLatestInBranch(ManualWorldSave save, IEnumerable<ManualWorldSave> worldSaves) =>
        save.Branch is { } branch && !worldSaves.Any(other => other.Id != save.Id &&
            other.Branch?.Id == branch.Id &&
            other.BranchPosition > save.BranchPosition);

    private async Task CreateManualSaveAsync()
    {
        var name = manualSaveName.Text.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
        {
            SetStatus("Give the save a name (1–80 characters).", good: false);
            return;
        }
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            var saved = await AwaitCurrentWorldResultAsync(ownerApi.CreateManualSaveAsync(ResolveWorldUri(), authority,
                deviceId, name, signer, CancellationToken.None));
            manualSaveOverlay.Hide();
            manualSaveName.Text = string.Empty;
            return "World saved.";
        });
    }

    private void ConfirmManualSaveOverwrite()
    {
        if (manualSaveLoadMode || manualSaveList.GetSelectedItems() is not { Length: 1 } selected ||
            selected[0] < 0 || selected[0] >= listedManualSaves.Length ||
            listedManualSaves[selected[0]].IsAutosave) return;
        var save = listedManualSaves[selected[0]];
        pendingOverwriteSaveId = save.Id;
        manualSaveOverwriteConfirmation.DialogText = $"Replace \"{save.Name}\" with the current world? The old version is kept as \"Before overwriting: {save.Name}\".";
        PopupDialog(manualSaveOverwriteConfirmation);
    }

    private async Task OverwriteSelectedManualSaveAsync()
    {
        var id = pendingOverwriteSaveId;
        pendingOverwriteSaveId = null;
        if (id is null || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await AwaitCurrentWorldResultAsync(ownerApi.OverwriteManualSaveAsync(ResolveWorldUri(), authority,
                deviceId, id, signer, CancellationToken.None));
            manualSaveOverlay.Hide();
            return $"Saved over {receipt.Saved.Name}. The old version is kept as a recovery copy.";
        });
    }

    private void ConfirmManualSaveLoad()
    {
        if (!manualSaveLoadMode || manualSaveList.GetSelectedItems() is not { Length: 1 } selected ||
            selected[0] < 0 || selected[0] >= listedManualSaves.Length) return;
        var save = listedManualSaves[selected[0]];
        var branchNote = IsLatestInBranch(save, allListedManualSaves)
            ? string.Empty
            : " Playing on from it starts a new branch; your later saves stay as they are.";
        manualSaveLoadConfirmation.DialogText = $"Load \"{save.Name}\"? Your current world is saved first, and the loaded world starts paused.{branchNote}";
        PopupDialog(manualSaveLoadConfirmation);
    }

    private async Task LoadSelectedManualSaveAsync()
    {
        if (manualSaveList.GetSelectedItems() is not { Length: 1 } selected ||
            selected[0] < 0 || selected[0] >= listedManualSaves.Length ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var save = listedManualSaves[selected[0]];
        await RunOwnerActionAsync(async () =>
        {
            await AwaitCurrentWorldResultAsync(ownerApi.SetPausedAsync(ResolveWorldUri(), authority, deviceId, true,
                signer, CancellationToken.None));
            // A lost response cannot tell us whether the host committed the
            // rewind. Reconnect from zero either way, instead of rejecting a
            // valid older world as a regressing observation.
            // A pulse may accept the fresh baseline before the load receipt.
            // This explicit transition still needs to finish closing its menus.
            await observationSession.ChangeTimelineAsync(() => ownerApi.LoadManualSaveAsync(ResolveWorldUri(), authority,
                deviceId, save.Id, signer, CancellationToken.None));
            manualSaveOverlay.Hide();
            worldMenuOverlay.Hide();
            mainMenuOverlay.Hide();
            isInWorld = true;
            resumeWorldOnContinue = false;
            menuPausedWorld = false;
            return $"Loaded {save.Name}. Your previous world is saved too.";
        });
    }
    private void ConfirmSaveDeletion()
    {
        if (manualSaveList.GetSelectedItems() is not { Length: 1 } selected ||
            selected[0] < 0 || selected[0] >= listedManualSaves.Length || listedSaveWorldId is null) return;
        var save = listedManualSaves[selected[0]];
        pendingDeletion = new OwnerDeletionAction("save", save.Id, listedSaveWorldId, save.CreatedUtc);
        deletionConfirmation.DialogText = $"Permanently delete \"{save.Name}\" (saved {save.CreatedUtc.ToLocalTime():g})? This removes only this snapshot, not the world or its other saves. There is no undo.";
        PopupDialog(deletionConfirmation);
    }

    private void ConfirmWorldDeletion()
    {
        if (worldMenuBusy || isOwnerAction || worldSelectionList.GetSelectedItems() is not { Length: 1 } selected ||
            selected[0] < 0 || selected[0] >= listedWorlds.Length) return;
        var world = listedWorlds[selected[0]];
        if (world.Id == listedActiveWorldId)
        {
            worldMenuStatus.Text = "Open or create another world before deleting this one.";
            return;
        }
        pendingDeletion = new OwnerDeletionAction("world", world.Id, world.WorldId);
        deletionConfirmation.DialogText = $"Permanently delete \"{world.Name}\" and all of its manual saves and autosaves? Your other worlds and account settings stay unchanged. There is no undo.";
        PopupDialog(deletionConfirmation);
    }

    private async Task DeleteConfirmedAsync()
    {
        var generation = observationSession.RequestGeneration;
        var action = pendingDeletion;
        pendingDeletion = null;
        if (action is null || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        if (action.Kind == "world" && worldMenuBusy) return;
        manualSaveDeleteButton.Disabled = true;
        worldDeleteButton.Disabled = true;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await AwaitCurrentWorldResultAsync(ownerApi.DeleteAsync(ResolveWorldUri(), authority, deviceId,
                action, signer, CancellationToken.None));
            if (action.Kind == "save") await OpenManualSavesAsync(manualSaveLoadMode);
            else await RefreshWorldListAsync();
            if (!IsCurrentWorldRequest(generation)) throw new ObsoleteWorldRequestException();
            return receipt.CleanupComplete ? "Permanently deleted." :
                "Deleted. Some history could not be cleaned up; other saves were preserved.";
        });
    }

}
