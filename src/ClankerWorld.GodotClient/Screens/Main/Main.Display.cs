using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void SetFullscreen(bool enabled)
    {
        SaveDisplayPreferences(displayPreferences with { Fullscreen = enabled });
        DisplayServer.WindowSetMode(enabled
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        windowSizeChoice.Disabled = enabled;
        if (!enabled)
            GetWindow().Size = DisplaySizePresets[windowSizeChoice.Selected];
        RefreshAutomaticRenderResolution();
        RefreshRenderResolutionOptions();
    }

    private void ApplySavedDisplaySettings()
    {
        var window = GetWindow();
        DisplayServer.WindowSetMode(displayPreferences.UsesFullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        var windowSize = new Vector2I(displayPreferences.WindowWidth, displayPreferences.WindowHeight);
        var renderSize = new Vector2I(displayPreferences.RenderWidth, displayPreferences.RenderHeight);
        window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        if (!displayPreferences.UsesFullscreen)
            window.Size = DisplaySizePresets[DisplaySizeIndex(windowSize)];
        window.ContentScaleSize = displayPreferences.UsesAutomaticRenderResolution
            ? AutomaticRenderSize()
            : new DisplayDimensions(renderSize.X, renderSize.Y).IsReasonable
                ? renderSize : AutomaticRenderSize();
        ApplyUiScale(displayPreferences.UiScalePercent);
    }

    private void ApplyUiScale(int percent)
    {
        var area = Size.X > 0 && Size.Y > 0 ? Size : GetViewportRect().Size;
        var factor = DisplayUiScalePolicy.FittingFactor(percent, area.X, area.Y);
        uiLayer.Factor = factor;
        menuLayer.Factor = factor;
        ScaleWindows(factor);
        ScaleMapText(factor);
        // Automatic names the size it picked; sizes that would leave too little room are unavailable.
        for (var index = 0; index < uiScaleChoice.ItemCount; index++)
        {
            var choice = DisplayUiScalePolicy.SupportedPercentages[index];
            var fitting = DisplayUiScalePolicy.FittingFactor(choice, area.X, area.Y) * 100;
            if (choice == DisplayUiScalePolicy.Automatic)
                uiScaleChoice.SetItemText(index, $"Automatic ({fitting}%)");
            else
                uiScaleChoice.SetItemDisabled(index, fitting != choice);
        }
        ApplyResponsiveLayout();
    }

    /// <summary>Dialogs, drop-down lists and tooltips are separate windows, so they scale on their own.</summary>
    private void ScaleWindows(int factor)
    {
        foreach (var dialog in new[] { quitGameConfirmation, quitToMenuConfirmation, manualSaveLoadConfirmation, manualSaveOverwriteConfirmation, deletionConfirmation })
            UiTheme.ScaleDialog(dialog, factor);
        foreach (var node in FindChildren("*", nameof(OptionButton), recursive: true, owned: false))
            UiTheme.ScaleWindow(((OptionButton)node).GetPopup(), factor);
        UiTheme.ScaleTooltips(factor);
    }

    /// <summary>Names on the map grow with the interface; the map itself keeps its own zoom.</summary>
    private void ScaleMapText(int factor)
    {
        AgentMarker.TextScale = factor;
        foreach (var marker in inhabitantVisuals.Values)
            marker.QueueRedraw();
        foreach (var label in mapObjectVisuals.Values)
            label.AddThemeFontSizeOverride("font_size", UiFonts.Body * factor);
    }

    /// <summary>Opens a confirmation at its size in interface pixels.</summary>
    private void PopupDialog(ConfirmationDialog dialog, Vector2I size) =>
        dialog.PopupCentered(DialogSize(size));

    private Vector2I DialogSize(Vector2I size) => size * uiLayer.Factor;

    private static Vector2I CurrentMonitorSize() =>
        DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());

    private Vector2I AutomaticRenderSize()
    {
        var monitor = CurrentMonitorSize();
        var window = GetWindow().Size;
        var mode = DisplayServer.WindowGetMode();
        var target = DisplayResolutionPolicy.AutomaticRenderSize(
            new DisplayDimensions(monitor.X, monitor.Y),
            new DisplayDimensions(window.X, window.Y),
            mode is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen);
        return new Vector2I(target.Width, target.Height);
    }

    private void RefreshAutomaticRenderResolution()
    {
        if (!displayPreferences.UsesAutomaticRenderResolution) return;
        var target = AutomaticRenderSize();
        if (GetWindow().ContentScaleSize != target)
            GetWindow().ContentScaleSize = target;
        if (renderResolutionChoice.ItemCount > 0)
            renderResolutionChoice.SetItemText(0, $"Automatic ({target.X} × {target.Y})");
    }

    private void RefreshRenderResolutionOptions()
    {
        var monitor = CurrentMonitorSize();
        var saved = new DisplayDimensions(displayPreferences.RenderWidth, displayPreferences.RenderHeight);
        renderSizeOptions.Clear();
        renderSizeOptions.AddRange(DisplayResolutionPolicy.FixedRenderSizes(
            new DisplayDimensions(monitor.X, monitor.Y), saved)
            .Select(size => new Vector2I(size.Width, size.Height)));
        renderResolutionChoice.Clear();
        var automatic = AutomaticRenderSize();
        renderResolutionChoice.AddItem($"Automatic ({automatic.X} × {automatic.Y})");
        foreach (var size in renderSizeOptions)
            renderResolutionChoice.AddItem($"{size.X} × {size.Y}");
        renderResolutionChoice.Select(displayPreferences.UsesAutomaticRenderResolution ? 0 :
            Math.Max(0, renderSizeOptions.IndexOf(saved.IsReasonable
                ? new Vector2I(saved.Width, saved.Height) : automatic) + 1));
    }

    private static int DisplaySizeIndex(Vector2I size)
    {
        for (var index = 0; index < DisplaySizePresets.Length; index++)
            if (DisplaySizePresets[index] == size) return index;
        return 0;
    }

    // Every labelled settings row shares one caption column so the choices
    // line up; ApplyResponsiveLayout widens it with the caption text.
    /// <summary>A small heading that groups related settings.</summary>
    private static Label SettingsSection(string text)
    {
        return new Label { Text = text.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel" };
    }

    private HBoxContainer DisplaySettingRow(string label, Control choice)
    {
        var row = new HBoxContainer();
        var caption = new Label { Text = label, CustomMinimumSize = new Vector2(SettingCaptionWidth, 0) };
        settingCaptionLabels.Add(caption);
        row.AddChild(caption);
        if (choice is not CheckButton) choice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(choice);
        return row;
    }

    private void SetWindowSize(long index)
    {
        var size = DisplaySizePresets[(int)index];
        SaveDisplayPreferences(displayPreferences with { WindowWidth = size.X, WindowHeight = size.Y });
        if (!fullscreenToggle.ButtonPressed)
            GetWindow().Size = size;
        RefreshAutomaticRenderResolution();
    }

    private void SetRenderResolution(long index)
    {
        if (index == 0)
        {
            SaveDisplayPreferences(displayPreferences with { AutoRenderResolution = true });
            RefreshAutomaticRenderResolution();
            return;
        }
        var size = renderSizeOptions[(int)index - 1];
        SaveDisplayPreferences(displayPreferences with
        {
            RenderWidth = size.X,
            RenderHeight = size.Y,
            AutoRenderResolution = false,
        });
        GetWindow().ContentScaleSize = size;
    }

    private void SetUiScale(long index)
    {
        if (index < 0 || index >= DisplayUiScalePolicy.SupportedPercentages.Count) return;
        var percent = DisplayUiScalePolicy.SupportedPercentages[(int)index];
        SaveDisplayPreferences(displayPreferences with { UiScalePercent = percent });
        ApplyUiScale(percent);
    }

    private void SetCloudHaze(bool enabled)
    {
        SaveDisplayPreferences(displayPreferences with { CloudHaze = enabled });
        weatherLayer.CloudsEnabled = enabled;
        weatherLayer.QueueRedraw();
    }

    private void SetLightningFlashes(bool enabled)
    {
        SaveDisplayPreferences(displayPreferences with { LightningFlashes = enabled });
        weatherLayer.LightningEnabled = enabled;
        weatherLayer.QueueRedraw();
    }

    private void SetUiTheme(long index)
    {
        var choice = (UiThemeChoice)Math.Clamp((int)index, 0, 2);
        SaveDisplayPreferences(displayPreferences with { Theme = UiTheme.Key(choice) });
        UiTheme.Apply(GetTree().Root, UiTheme.Resolve(choice));
    }

    public override void _Notification(int what)
    {
        // Match system picks up a change made while the game was in the background.
        if (what == NotificationApplicationFocusIn && UiTheme.Parse(displayPreferences.Theme) == UiThemeChoice.System)
            UiTheme.Apply(GetTree().Root, UiTheme.Resolve(UiThemeChoice.System));
    }

    /// <summary>
    /// Colors drawn outside the Theme resource: backdrops, modal shades and
    /// rich text written with explicit colors, which are redrawn in the new
    /// palette.
    /// </summary>
    private void ApplyThemeColors()
    {
        var palette = UiTheme.Current;
        appBackdrop.Color = palette.Backdrop;
        worldBackdrop.Color = palette.Backdrop;
        mainMenuBackground.Color = palette.Backdrop;
        mainMenuBackdrop.Night = ReferenceEquals(palette, UiTheme.Dark);
        // Dialog frames are drawn per scale in the current palette.
        ScaleWindows(uiLayer.Factor);
        menuShade.Color = palette.Shade;
        familyTreeView.QueueRedraw();
        RefreshHudIcons();
        renderedTownList = null;
        renderedTownPanel = null;
        renderedEventLog = null;
        if (observationSession.Current is { } current)
            Render(current.Baseline.Snapshot, []);
    }

    private void SetClockFormat(long index)
    {
        SaveDisplayPreferences(displayPreferences with { UseTwelveHourClock = index == 1 });
        if (observationSession.Current is { } current)
            Render(current.Baseline.Snapshot, []);
    }

    private void SetDateFormat(long index)
    {
        var format = index switch { 1 => "mdy", 2 => "ymd", _ => "dmy" };
        SaveDisplayPreferences(displayPreferences with { DateFormat = format });
        if (observationSession.Current is { } current)
            Render(current.Baseline.Snapshot, []);
    }

    private void SaveDisplayPreferences(GameDisplayPreferences updated)
    {
        displayPreferences = updated with
        {
            UiScalePercent = DisplayUiScalePolicy.NormalizePercent(updated.UiScalePercent),
        };
        try
        {
            displayPreferencesStore.Save(displayPreferences);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not save your display settings.", good: false);
        }
    }

    private string DisplayWorldClock(long worldTick) =>
        GameUiText.FormatWorldClock(worldTick, displayPreferences.UseTwelveHourClock,
            observedCalendarPace, displayPreferences.DateFormat);

}
