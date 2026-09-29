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
        var factor = DisplayUiScalePolicy.ScaleFactor(percent);
        // The fallback base scale helps theme-aware controls, while this
        // client's explicit font-size overrides also need direct scaling.
        ThemeDB.FallbackBaseScale = factor;
        RefreshHudIcons();
        if (uiScaleTreeReady)
        {
            ApplyUiScaleFontOverrides(this, factor);
            ApplyResponsiveLayout();
        }
    }

    private void WatchUiScaleTree(Node node)
    {
        if (IsMapRenderNode(node) || !uiScaleWatchedNodes.Add(node.GetInstanceId())) return;
        node.ChildEnteredTree += OnUiScaleChildEnteredTree;
        foreach (var child in node.GetChildren())
            WatchUiScaleTree(child);
    }

    private void OnUiScaleChildEnteredTree(Node child)
    {
        if (IsMapRenderNode(child)) return;
        WatchUiScaleTree(child);
        ApplyUiScaleFontOverrides(child, DisplayUiScalePolicy.ScaleFactor(displayPreferences.UiScalePercent));
    }

    private bool IsMapRenderNode(Node node) =>
        node.GetInstanceId() == mapStage.GetInstanceId() || mapStage.IsAncestorOf(node);

    private void ApplyUiScaleFontOverrides(Node node, float factor)
    {
        // Map terrain, object labels, and fixed-size agent hit targets stay in
        // their native map-space geometry; only the surrounding GUI is scaled.
        if (IsMapRenderNode(node)) return;
        if (node is Control control)
        {
            // RichTextLabel has separate sizes for each style; ordinary controls use font_size.
            var themeFontSizeItems = control is RichTextLabel
                ? UiScaleRichTextFontSizeThemeItems
                : UiScaleFontSizeThemeItems;
            foreach (var themeFontSizeItem in themeFontSizeItems)
            {
                // Cache each original resolved size so changes never compound.
                var metadataKey = UiScaleBaseFontSizeMetaPrefix + themeFontSizeItem;
                var baseFontSize = control.HasMeta(metadataKey)
                    ? (int)control.GetMeta(metadataKey)
                    : control.GetThemeFontSize(themeFontSizeItem);
                if (!control.HasMeta(metadataKey))
                    control.SetMeta(metadataKey, baseFontSize);
                var scaledFontSize = Math.Max(1, (int)Math.Round(baseFontSize * factor, MidpointRounding.AwayFromZero));
                control.AddThemeFontSizeOverride(themeFontSizeItem, scaledFontSize);
            }
        }

        foreach (var child in node.GetChildren())
            ApplyUiScaleFontOverrides(child, factor);
    }

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
        var heading = new Label { Text = text.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel" };
        heading.AddThemeFontSizeOverride("font_size", 12);
        return heading;
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
