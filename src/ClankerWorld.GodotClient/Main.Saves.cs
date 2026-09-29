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
    private readonly ItemList manualSaveList = new();
    private readonly Button manualSaveLoadButton = new();
    private readonly Button manualSaveOverwriteButton = new();
    private readonly ConfirmationDialog manualSaveLoadConfirmation = new();
    private readonly ConfirmationDialog manualSaveOverwriteConfirmation = new();
    private string? pendingOverwriteSaveId;
    private ManualWorldSave[] listedManualSaves = [];
    private bool manualSaveLoadMode;
    private readonly CheckBox autosaveEnabledToggle = new();
    private readonly OptionButton autosaveIntervalChoice = new();
    private readonly OptionButton autosaveRotationChoice = new();
    private readonly Button autosaveApplyButton = new();
    private readonly Label autosaveSettingsStatus = new();
    private bool autosaveSettingsLoaded;

    private void BuildAutosaveSettings()
    {
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

    private async Task RefreshAutosaveSettingsAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        autosaveSettingsLoaded = false;
        autosaveSettingsStatus.Text = "Loading this world's autosave settings…";
        RefreshControlAvailability();
        try
        {
            var saved = await ownerApi.GetAutosaveSettingsAsync(ResolveWorldUri(), authority,
                deviceId, signer, CancellationToken.None);
            autosaveEnabledToggle.ButtonPressed = saved.Enabled;
            autosaveIntervalChoice.Select(autosaveIntervalChoice.GetItemIndex(saved.IntervalMinutes));
            autosaveRotationChoice.Select(autosaveRotationChoice.GetItemIndex(saved.RotationCount));
            autosaveSettingsLoaded = true;
            autosaveSettingsStatus.Text = saved.LastWorldTick < 0
                ? "No rotating snapshot yet. The active world is still saved after committed changes."
                : $"Last rotating snapshot: tick {saved.LastWorldTick}. The active world is saved after committed changes.";
        }
        catch (Exception exception)
        {
            autosaveSettingsStatus.Text = "Could not read autosave settings: " + FriendlyFailure(exception);
        }
        RefreshControlAvailability();
    }

    private async Task ApplyAutosaveSettingsAsync()
    {
        if (!autosaveSettingsLoaded || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var action = new OwnerAutosaveConfigurationAction(autosaveEnabledToggle.ButtonPressed,
            autosaveIntervalChoice.GetSelectedId(), autosaveRotationChoice.GetSelectedId());
        await RunOwnerActionAsync(async () =>
        {
            var updated = await ownerApi.ConfigureAutosaveAsync(ResolveWorldUri(), authority,
                deviceId, action, signer, CancellationToken.None);
            autosaveSettingsStatus.Text = updated.Enabled
                ? $"Autosave every {updated.IntervalMinutes} minutes; " +
                  (updated.RotationCount == 0 ? "keep latest only." : $"keep {updated.RotationCount} copies.")
                : "Autosave off. The active recovery save still updates after committed changes.";
            return "Autosave settings saved for this world.";
        });
    }

    private void BuildManualSavesPanel()
    {
        manualSaveOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        manualSaveOverlay.MouseFilter = MouseFilterEnum.Stop;
        manualSaveOverlay.ZIndex = 220;
        AddChild(manualSaveOverlay);
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.78f), MouseFilter = MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        manualSaveOverlay.AddChild(shade);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        manualSaveOverlay.AddChild(center);
        center.AddChild(manualSaveCard);
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
        body.AddThemeConstantOverride("separation", 10);
        manualSaveHeading.AddThemeFontSizeOverride("font_size", 24);
        body.AddChild(manualSaveHeading);
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
        manualSaveList.ItemSelected += index =>
        {
            manualSaveLoadButton.Disabled = false;
            manualSaveOverwriteButton.Disabled = (int)index >= listedManualSaves.Length ||
                listedManualSaves[(int)index].IsAutosave;
        };
        body.AddChild(manualSaveList);
        manualSaveLoadButton.Text = "Load selected save";
        StyleButton(manualSaveLoadButton, primary: true);
        manualSaveLoadButton.Pressed += ConfirmManualSaveLoad;
        body.AddChild(manualSaveLoadButton);
        manualSaveOverwriteButton.Text = "Overwrite selected save";
        StyleButton(manualSaveOverwriteButton);
        manualSaveOverwriteButton.Pressed += ConfirmManualSaveOverwrite;
        body.AddChild(manualSaveOverwriteButton);
        var close = new Button { Text = "Back" };
        StyleButton(close);
        close.Pressed += () => manualSaveOverlay.Hide();
        body.AddChild(close);
        AddPanelContents(manualSaveCard, body);
        manualSaveCard.CustomMinimumSize = new Vector2(470, 0);
        manualSaveLoadConfirmation.Title = "Load this save?";
        manualSaveLoadConfirmation.Confirmed += () => _ = LoadSelectedManualSaveAsync();
        AddChild(manualSaveLoadConfirmation);
        manualSaveOverwriteConfirmation.Title = "Overwrite this save?";
        manualSaveOverwriteConfirmation.Confirmed += () => _ = OverwriteSelectedManualSaveAsync();
        AddChild(manualSaveOverwriteConfirmation);
        manualSaveOverlay.Hide();
    }

    private async Task OpenManualSavesAsync(bool loadMode)
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        if (!loadMode && observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused != true)
        {
            SetStatus("Wait for the world to pause before saving.", good: false);
            return;
        }
        manualSaveLoadMode = loadMode;
        manualSaveHeading.Text = loadMode ? "Load Save" : "Save World";
        manualSaveStatus.Text = loadMode
            ? "Choose a named checkpoint. Your current state will be saved before loading it."
            : "Create a new checkpoint, or select one by name to overwrite. A recovery copy of the old checkpoint is kept.";
        manualSaveName.Visible = !loadMode;
        manualSaveCreateButton.Visible = !loadMode;
        manualSaveList.Visible = true;
        manualSaveLoadButton.Visible = loadMode;
        manualSaveOverwriteButton.Visible = !loadMode;
        manualSaveLoadButton.Disabled = true;
        manualSaveOverwriteButton.Disabled = true;
        manualSaveOverlay.Show();
        try
        {
            listedManualSaves = (await ownerApi.ListManualSavesAsync(ResolveWorldUri(), authority,
                deviceId, signer, CancellationToken.None))
                .Where(save => loadMode || !save.IsAutosave).ToArray();
            manualSaveList.Clear();
            foreach (var save in listedManualSaves)
                manualSaveList.AddItem($"{(save.IsAutosave ? "Autosave" : save.Name)} · tick {save.WorldTick} · {save.CreatedUtc.ToLocalTime():g}");
            if (listedManualSaves.Length == 0)
                manualSaveStatus.Text = loadMode
                    ? "No saves yet. Continue the world and use Pause Menu → Save World."
                    : "No named saves yet. Create New Save to make the first one.";
        }
        catch (Exception exception)
        {
            manualSaveStatus.Text = "Could not list saves: " + FriendlyFailure(exception);
        }
    }

    private async Task CreateManualSaveAsync()
    {
        var name = manualSaveName.Text.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
        {
            manualSaveStatus.Text = "Choose a name of 1–80 printable characters.";
            return;
        }
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            var saved = await ownerApi.CreateManualSaveAsync(ResolveWorldUri(), authority,
                deviceId, name, signer, CancellationToken.None);
            manualSaveOverlay.Hide();
            manualSaveName.Text = string.Empty;
            return $"Saved world at tick {saved.WorldTick}.";
        });
    }

    private void ConfirmManualSaveOverwrite()
    {
        if (manualSaveLoadMode || manualSaveList.GetSelectedItems() is not { Length: 1 } selected ||
            selected[0] < 0 || selected[0] >= listedManualSaves.Length ||
            listedManualSaves[selected[0]].IsAutosave) return;
        var save = listedManualSaves[selected[0]];
        pendingOverwriteSaveId = save.Id;
        manualSaveOverwriteConfirmation.DialogText = $"Replace only ‘{save.Name}’ (tick {save.WorldTick}) with the current paused world? A separate ‘Before overwriting: {save.Name}’ recovery save will keep its old state.";
        manualSaveOverwriteConfirmation.PopupCentered(new Vector2I(520, 190));
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
            return $"Overwrote {receipt.Saved.Name} at tick {receipt.Saved.WorldTick}; its prior state is in a Before overwriting recovery save.";
        });
    }

    private void ConfirmManualSaveLoad()
    {
        if (!manualSaveLoadMode || manualSaveList.GetSelectedItems() is not { Length: 1 } selected ||
            selected[0] < 0 || selected[0] >= listedManualSaves.Length) return;
        manualSaveLoadConfirmation.DialogText = $"Load ‘{listedManualSaves[selected[0]].Name}’? The current world will be saved first, and the loaded world will remain paused.";
        manualSaveLoadConfirmation.PopupCentered(new Vector2I(480, 180));
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
            return $"Loaded {save.Name} at tick {loaded.WorldTick}; the previous state is saved too.";
        });
    }
}
