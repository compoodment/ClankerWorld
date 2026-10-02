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
        RefreshRenderSize();
    }

    private void ApplySavedDisplaySettings()
    {
        var window = GetWindow();
        DisplayServer.WindowSetMode(displayPreferences.UsesFullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        var windowSize = new Vector2I(displayPreferences.WindowWidth, displayPreferences.WindowHeight);
        window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        if (!displayPreferences.UsesFullscreen)
            window.Size = DisplaySizePresets[DisplaySizeIndex(windowSize)];
        window.ContentScaleSize = AutomaticRenderSize();
        ApplyUiScale();
    }

    /// <summary>The interface size follows the screen in whole steps; there is no setting.</summary>
    private void ApplyUiScale()
    {
        var area = Size.X > 0 && Size.Y > 0 ? Size : GetViewportRect().Size;
        SetUiFactor(DisplayUiScalePolicy.FittingFactor(area.X, area.Y));
    }

    /// <summary>Magnifies the interface, its windows and the names on the map by a whole number.</summary>
    private void SetUiFactor(int factor)
    {
        uiLayer.Factor = factor;
        menuLayer.Factor = factor;
        ScaleWindows(factor);
        ScaleMapText(factor);
        ApplyResponsiveLayout();
    }

    /// <summary>Dialogs, drop-down lists and tooltips are separate windows, so they scale on their own.</summary>
    private void ScaleWindows(int factor)
    {
        foreach (var dialog in new[] { quitGameConfirmation, quitToMenuConfirmation, manualSaveLoadConfirmation, manualSaveOverwriteConfirmation, deletionConfirmation, buildingRemoveConfirmation })
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

    /// <summary>Opens a confirmation just large enough for its title, message and buttons.</summary>
    private void PopupDialog(ConfirmationDialog dialog) =>
        dialog.PopupCentered(DialogSize(FitDialog(dialog)));

    private Vector2I DialogSize(Vector2I size) => size * uiLayer.Factor;

    /// <summary>The widest a confirmation's message runs before it wraps, in interface pixels.</summary>
    private const int DialogTextWidth = 360;

    /// <summary>Room kept on each side of a dialog's centered title for the close button.</summary>
    private const int DialogTitleClearance = 30;

    /// <summary>
    /// A confirmation's size in interface pixels. Its message keeps its natural
    /// width up to <see cref="DialogTextWidth"/> and wraps beyond that; the
    /// dialog is no wider than its title, message or buttons need and no taller
    /// than the wrapped message above its buttons.
    /// </summary>
    private static Vector2I FitDialog(AcceptDialog dialog)
    {
        var label = dialog.GetLabel();
        var margins = dialog.GetThemeStylebox("panel").GetMinimumSize();
        var buttons = dialog.GetOkButton().GetParent<Control>().GetCombinedMinimumSize();
        var message = label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1,
            label.GetThemeFontSize("font_size")).X + 1;
        var title = UiFonts.Headings.GetStringSize(dialog.Title, HorizontalAlignment.Left, -1, UiFonts.Heading).X +
            2 * DialogTitleClearance;
        var width = Mathf.Ceil(Math.Max(Math.Min(message, DialogTextWidth), Math.Max(buttons.X, title) - margins.X));
        label.Size = new Vector2(width, label.Size.Y);
        var height = label.GetMinimumSize().Y + margins.Y + buttons.Y + dialog.GetThemeConstant("buttons_separation");
        return new Vector2I((int)(width + margins.X), (int)Mathf.Ceil(height));
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

    /// <summary>The game always draws at the window's or screen's own resolution.</summary>
    private void RefreshRenderSize()
    {
        var target = AutomaticRenderSize();
        if (GetWindow().ContentScaleSize != target)
            GetWindow().ContentScaleSize = target;
    }

    private static int DisplaySizeIndex(Vector2I size)
    {
        for (var index = 0; index < DisplaySizePresets.Length; index++)
            if (DisplaySizePresets[index] == size) return index;
        return 0;
    }

    /// <summary>A boxed group of settings under its own heading, like API keys.</summary>
    private static PanelContainer SettingsBox(string title, params Control[] rows)
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        foreach (var row in rows) body.AddChild(row);
        return NewPanel(title, body);
    }

    // Every labelled settings row shares one caption column so the choices
    // line up; ApplyResponsiveLayout widens it with the caption text.
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
        RefreshRenderSize();
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
        FillFamilyLegend();
        RefreshHudIcons();
        renderedTownList = null;
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

    /// <summary>The Date display choices in Game Settings, in list order: season first, then the numeric orders.</summary>
    private static readonly (string Style, string Label)[] DateStyles =
    [
        (GameUiText.SeasonDates, "Season (Autumn 2, Year 1)"),
        ("dmy", "DD-MM-YYYY"),
        ("mdy", "MM-DD-YYYY"),
        ("ymd", "YYYY-MM-DD"),
    ];

    private static int DateStyleIndex(string style) =>
        Math.Max(0, Array.FindIndex(DateStyles, choice => choice.Style == style));

    private void SetDateStyle(long index)
    {
        var style = DateStyles[Math.Clamp((int)index, 0, DateStyles.Length - 1)].Style;
        SaveDisplayPreferences(displayPreferences with { DateStyle = style });
        if (observationSession.Current is { } current)
            Render(current.Baseline.Snapshot, []);
    }

    private void SaveDisplayPreferences(GameDisplayPreferences updated)
    {
        displayPreferences = updated;
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
            observedCalendarPace, displayPreferences.DateStyle);

    /// <summary>When the date already names the season, the season is not repeated beside it.</summary>
    private bool DatesShowSeason =>
        GameUiText.ShowsSeasonDates(observedCalendarPace, displayPreferences.DateStyle);

}
