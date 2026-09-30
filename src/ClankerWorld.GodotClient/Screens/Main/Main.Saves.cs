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
    private readonly ItemList manualSaveList = new();
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
    private bool manualSaveLoadMode;
    private readonly CheckBox autosaveEnabledToggle = new();
    private readonly OptionButton autosaveIntervalChoice = new();
    private readonly OptionButton autosaveRotationChoice = new();
    private readonly Button autosaveApplyButton = new();
    private readonly Label autosaveSettingsStatus = new();
    private bool autosaveSettingsLoaded;
    private string? autosaveSettingsWorldId;
    private OwnerDeviceRegistration? autosaveSettingsRegistration;
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
        autosaveSettingsStatus.Text = "Loading this world's autosave settings…";
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
        autosaveSettingsStatus.Text = "Loading this world's autosave settings…";
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
        if (!autosaveSettingsLoaded || !IsCurrentAutosaveSettingsContext() ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var action = new OwnerAutosaveConfigurationAction(autosaveEnabledToggle.ButtonPressed,
            autosaveIntervalChoice.GetSelectedId(), autosaveRotationChoice.GetSelectedId());
        await RunOwnerActionAsync(async () =>
        {
            var updated = await ownerApi.ConfigureAutosaveAsync(ResolveWorldUri(), authority,
                deviceId, action, signer, CancellationToken.None);
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
        manualSaveName.PlaceholderText = "Name this save";
        manualSaveName.MaxLength = 80;
        body.AddChild(manualSaveName);
        manualSaveCreateButton.Text = "Create New Save";
        StyleButton(manualSaveCreateButton, primary: true);
        manualSaveCreateButton.Pressed += () => _ = CreateManualSaveAsync();
        body.AddChild(manualSaveCreateButton);
        manualSaveList.CustomMinimumSize = new Vector2(0, 250);
        manualSaveList.ItemSelected += _ => RefreshManualSaveAvailability();
        body.AddChild(manualSaveList);
        manualSaveLoadButton.Text = "Load selected save";
        StyleButton(manualSaveLoadButton, primary: true);
        manualSaveLoadButton.Pressed += ConfirmManualSaveLoad;
        body.AddChild(manualSaveLoadButton);
        manualSaveOverwriteButton.Text = "Overwrite selected save";
        StyleButton(manualSaveOverwriteButton);
        manualSaveOverwriteButton.Pressed += ConfirmManualSaveOverwrite;
        body.AddChild(manualSaveOverwriteButton);
        manualSaveDeleteButton.Text = "Delete selected save";
        StyleButton(manualSaveDeleteButton);
        manualSaveDeleteButton.Pressed += ConfirmSaveDeletion;
        body.AddChild(manualSaveDeleteButton);
        StyleConfirmation(deletionConfirmation, "Permanently delete?", "Delete permanently");
        deletionConfirmation.GetOkButton().ThemeTypeVariation = "DangerButton";
        deletionConfirmation.Confirmed += () => _ = DeleteConfirmedAsync();
        deletionConfirmation.Canceled += () => pendingDeletion = null;
        AddChild(deletionConfirmation);
        AddPanelContents(manualSaveCard, body);
        manualSaveCard.CustomMinimumSize = new Vector2(470, 0);
        StyleConfirmation(manualSaveLoadConfirmation, "Load this save?", "Load Save");
        manualSaveLoadConfirmation.Confirmed += () => _ = LoadSelectedManualSaveAsync();
        AddChild(manualSaveLoadConfirmation);
        StyleConfirmation(manualSaveOverwriteConfirmation, "Overwrite this save?", "Overwrite");
        manualSaveOverwriteConfirmation.Confirmed += () => _ = OverwriteSelectedManualSaveAsync();
        AddChild(manualSaveOverwriteConfirmation);
        manualSaveOverlay.Hide();
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
        var valid = !isOwnerAction && selected.Length == 1 && selected[0] >= 0 && selected[0] < listedManualSaves.Length;
        manualSaveLoadButton.Disabled = !manualSaveLoadMode || !valid;
        manualSaveOverwriteButton.Disabled = manualSaveLoadMode || !valid || listedManualSaves[selected[0]].IsAutosave;
        manualSaveDeleteButton.Disabled = !valid;
        manualSaveCreateButton.Disabled = isOwnerAction || manualSaveLoadMode;
    }

    private async Task OpenManualSavesAsync(bool loadMode, Func<CancellationToken, Task<ManualWorldSave[]>>? fetch = null)
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
        var readWorldId = observationSession.Current?.Baseline.Snapshot.WorldId;
        var readRegistration = registration;
        pendingDeletion = null;
        listedSaveWorldId = readWorldId;
        listedManualSaves = [];
        manualSaveList.Clear();
        manualSaveDeleteButton.Disabled = true;
        manualSaveLoadMode = loadMode;
        manualSaveHeading.Text = loadMode ? "Load Save" : "Save World";
        manualSaveStatus.Text = loadMode
            ? "Choose a save to load. Your current world is saved first."
            : "Make a new save, or pick one to overwrite. The old version is kept as a recovery copy.";
        manualSaveName.Visible = !loadMode;
        manualSaveCreateButton.Visible = !loadMode;
        manualSaveList.Visible = true;
        manualSaveLoadButton.Visible = loadMode;
        manualSaveOverwriteButton.Visible = !loadMode;
        manualSaveLoadButton.Disabled = true;
        manualSaveOverwriteButton.Disabled = true;
        manualSaveOverlay.Show();
        RefreshManualSaveAvailability();
        bool IsCurrentRead() => ReferenceEquals(manualSaveListCancellation, read) &&
            manualSaveOverlay.Visible && ReferenceEquals(registration, readRegistration) &&
            readWorldId == observationSession.Current?.Baseline.Snapshot.WorldId;
        try
        {
            var saves = await (fetch?.Invoke(read.Token) ?? ownerApi.ListManualSavesAsync(ResolveWorldUri(), authority,
                deviceId, signer, read.Token));
            if (!IsCurrentRead()) return;
            listedManualSaves = saves.Where(save => loadMode || !save.IsAutosave).ToArray();
            manualSaveList.Clear();
            foreach (var save in listedManualSaves)
                manualSaveList.AddItem($"{(save.IsAutosave ? "Autosave" : save.Name)} · world {DisplayWorldClock(save.WorldTick)} · saved {save.CreatedUtc.ToLocalTime():g}");
            if (listedManualSaves.Length == 0)
                manualSaveStatus.Text = loadMode
                    ? "No saves yet. Continue the world and use Pause Menu → Save World."
                    : "No named saves yet. Create New Save to make the first one.";
            RefreshManualSaveAvailability();
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentRead()) manualSaveStatus.Text = "Could not list saves: " + FriendlyFailure(exception);
        }
        finally
        {
            if (ReferenceEquals(manualSaveListCancellation, read)) manualSaveListCancellation = null;
        }
    }

    private async Task CreateManualSaveAsync()
    {
        var name = manualSaveName.Text.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
        {
            manualSaveStatus.Text = "Give the save a name (1–80 characters).";
            return;
        }
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            var saved = await ownerApi.CreateManualSaveAsync(ResolveWorldUri(), authority,
                deviceId, name, signer, CancellationToken.None);
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
        manualSaveOverwriteConfirmation.DialogText = $"Replace ‘{save.Name}’ with the current world? The old version is kept as ‘Before overwriting: {save.Name}’.";
        PopupDialog(manualSaveOverwriteConfirmation);
    }

    private async Task OverwriteSelectedManualSaveAsync()
    {
        var id = pendingOverwriteSaveId;
        pendingOverwriteSaveId = null;
        if (id is null || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await ownerApi.OverwriteManualSaveAsync(ResolveWorldUri(), authority,
                deviceId, id, signer, CancellationToken.None);
            manualSaveOverlay.Hide();
            return $"Saved over {receipt.Saved.Name}. The old version is kept as a recovery copy.";
        });
    }

    private void ConfirmManualSaveLoad()
    {
        if (!manualSaveLoadMode || manualSaveList.GetSelectedItems() is not { Length: 1 } selected ||
            selected[0] < 0 || selected[0] >= listedManualSaves.Length) return;
        manualSaveLoadConfirmation.DialogText = $"Load ‘{listedManualSaves[selected[0]].Name}’? Your current world is saved first, and the loaded world starts paused.";
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
            await ownerApi.SetPausedAsync(ResolveWorldUri(), authority, deviceId, true,
                signer, CancellationToken.None);
            // A lost response cannot tell us whether the host committed the
            // rewind. Reconnect from zero either way, instead of rejecting a
            // valid older world as a regressing observation.
            observationSession.ResetAfterLoad();
            knownEvents.Clear();
            var loaded = await ownerApi.LoadManualSaveAsync(ResolveWorldUri(), authority,
                deviceId, save.Id, signer, CancellationToken.None);
            selectedInhabitantId = null;
            renderedMapSnapshot = null;
            manualSaveOverlay.Hide();
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
        deletionConfirmation.DialogText = $"Permanently delete ‘{save.Name}’ (saved {save.CreatedUtc.ToLocalTime():g})? This removes only this snapshot, not the world or its other saves. There is no undo.";
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
        deletionConfirmation.DialogText = $"Permanently delete ‘{world.Name}’ and all of its manual saves and autosaves? Your other worlds and account settings stay unchanged. There is no undo.";
        PopupDialog(deletionConfirmation);
    }

    private async Task DeleteConfirmedAsync()
    {
        var action = pendingDeletion;
        pendingDeletion = null;
        if (action is null || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        if (action.Kind == "world" && worldMenuBusy) return;
        manualSaveDeleteButton.Disabled = true;
        worldDeleteButton.Disabled = true;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await ownerApi.DeleteAsync(ResolveWorldUri(), authority, deviceId,
                action, signer, CancellationToken.None);
            if (action.Kind == "save") await OpenManualSavesAsync(manualSaveLoadMode);
            else await RefreshWorldListAsync();
            return receipt.CleanupComplete ? "Permanently deleted." :
                "Deleted. Some history could not be cleaned up; other saves were preserved.";
        });
    }

}
