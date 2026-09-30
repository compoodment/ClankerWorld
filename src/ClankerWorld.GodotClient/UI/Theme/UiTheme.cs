using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Named colors for one interface theme.</summary>
public sealed record UiPalette(
    string Name,
    Color Backdrop,
    Color Shade,
    Color Wood,
    Color WoodLight,
    Color WoodDark,
    Color WoodEdge,
    Color Paper,
    Color PaperEdge,
    Color Ink,
    Color OnWood,
    Color OnWoodSoft,
    Color InkSoft,
    Color InkMuted,
    Color InkFaint,
    Color Button,
    Color ButtonLight,
    Color ButtonDark,
    Color ButtonEdge,
    Color ButtonHover,
    Color Pressed,
    Color PressedDark,
    Color PressedLight,
    Color Primary,
    Color PrimaryLight,
    Color PrimaryDark,
    Color PrimaryEdge,
    Color PrimaryInk,
    Color Ember,
    Color EmberLight,
    Color EmberDark,
    Color EmberEdge,
    Color EmberInk,
    Color Seal,
    Color SealRing,
    Color SealInk,
    Color Inset,
    Color InsetEdge,
    Color Field,
    Color FieldEdge,
    Color FieldDisabled,
    Color Separator,
    Color Section,
    Color Link,
    Color Warning,
    Color Good,
    Color Bad,
    Color Partner,
    Color Selection,
    Color MeterFullness,
    Color MeterWarmth,
    Color MeterDiet,
    Color MeterIllness);

/// <summary>Which palette the player chose in Settings.</summary>
public enum UiThemeChoice
{
    Light,
    Dark,
    System,
}

/// <summary>
/// The Timber &amp; Parchment interface: wooden frames around parchment
/// panels, bevelled pixel buttons and ink text, in a light and a dark
/// palette. Every frame, button face, field, checkbox and switch is a small
/// generated nine-slice image, so the look needs no bundled binary asset and
/// switching theme swaps one Theme resource for the whole window.
/// </summary>
public static class UiTheme
{
    public static readonly UiPalette Light = new(
        Name: "light",
        Backdrop: new Color("2B1D12"),
        Shade: new Color(0.08f, 0.05f, 0.02f, 0.58f),
        Wood: new Color("6B4428"),
        WoodLight: new Color("9C6C42"),
        WoodDark: new Color("3F2716"),
        WoodEdge: new Color("2E1C0F"),
        Paper: new Color("EBDDBB"),
        PaperEdge: new Color("D5C197"),
        Ink: new Color("33261A"),
        OnWood: new Color("F6EBCF"),
        OnWoodSoft: new Color("E2CC9E"),
        InkSoft: new Color("4A3A2A"),
        InkMuted: new Color("5E4B38"),
        InkFaint: new Color("84715A"),
        Button: new Color("E4D2A8"),
        ButtonLight: new Color("F6EBCF"),
        ButtonDark: new Color("BFA778"),
        ButtonEdge: new Color("4A2F1C"),
        ButtonHover: new Color("F0E2BE"),
        Pressed: new Color("D4BF8E"),
        PressedDark: new Color("B39D6C"),
        PressedLight: new Color("EFE2C2"),
        Primary: new Color("4A7033"),
        PrimaryLight: new Color("6F9C4F"),
        PrimaryDark: new Color("34511F"),
        PrimaryEdge: new Color("22341A"),
        PrimaryInk: new Color("F7F1E1"),
        Ember: new Color("D9822B"),
        EmberLight: new Color("F2A95A"),
        EmberDark: new Color("9E5A18"),
        EmberEdge: new Color("5A300C"),
        EmberInk: new Color("2A1705"),
        Seal: new Color("B63B26"),
        SealRing: new Color("E0664E"),
        SealInk: new Color("FFF6E0"),
        Inset: new Color("F4EAD2"),
        InsetEdge: new Color("D5C197"),
        Field: new Color("F6EEDA"),
        FieldEdge: new Color("9C8356"),
        FieldDisabled: new Color("E6D9BC"),
        Separator: new Color("C9B489"),
        Section: new Color("6B4428"),
        Link: new Color("386126"),
        Warning: new Color("A4451C"),
        Good: new Color("386126"),
        Bad: new Color("A4331F"),
        Partner: new Color("B8457A"),
        Selection: new Color(0.33f, 0.5f, 0.23f, 0.35f),
        MeterFullness: new Color("B77C10"),
        MeterWarmth: new Color("C8502A"),
        MeterDiet: new Color("4A7033"),
        MeterIllness: new Color("7A4A96"));

    public static readonly UiPalette Dark = new(
        Name: "dark",
        Backdrop: new Color("120C07"),
        Shade: new Color(0.03f, 0.02f, 0.01f, 0.66f),
        Wood: new Color("4A3020"),
        WoodLight: new Color("6E4A2E"),
        WoodDark: new Color("2A1A0E"),
        WoodEdge: new Color("120A05"),
        Paper: new Color("2A211A"),
        PaperEdge: new Color("3A2E23"),
        Ink: new Color("EADFC4"),
        OnWood: new Color("EADFC4"),
        OnWoodSoft: new Color("C9B48E"),
        InkSoft: new Color("D2C4A6"),
        InkMuted: new Color("B7A485"),
        InkFaint: new Color("8E7C63"),
        Button: new Color("3A2E23"),
        ButtonLight: new Color("54432F"),
        ButtonDark: new Color("241B13"),
        ButtonEdge: new Color("120A05"),
        ButtonHover: new Color("46382A"),
        Pressed: new Color("221A13"),
        PressedDark: new Color("15100B"),
        PressedLight: new Color("46372A"),
        Primary: new Color("4A7231"),
        PrimaryLight: new Color("6E9A4C"),
        PrimaryDark: new Color("34521F"),
        PrimaryEdge: new Color("182610"),
        PrimaryInk: new Color("F7F1E1"),
        Ember: new Color("D9822B"),
        EmberLight: new Color("F2A95A"),
        EmberDark: new Color("9E5A18"),
        EmberEdge: new Color("3A1E06"),
        EmberInk: new Color("2A1705"),
        Seal: new Color("C4452E"),
        SealRing: new Color("E87A62"),
        SealInk: new Color("FFF6E0"),
        Inset: new Color("1E1812"),
        InsetEdge: new Color("3A2E23"),
        Field: new Color("1E1812"),
        FieldEdge: new Color("5C4731"),
        FieldDisabled: new Color("262019"),
        Separator: new Color("4A3A2B"),
        Section: new Color("C99A62"),
        Link: new Color("9CCB7A"),
        Warning: new Color("E8A87C"),
        Good: new Color("9CCB7A"),
        Bad: new Color("F0A08C"),
        Partner: new Color("E88AAE"),
        Selection: new Color(0.55f, 0.73f, 0.42f, 0.35f),
        MeterFullness: new Color("E8B04A"),
        MeterWarmth: new Color("F08A5A"),
        MeterDiet: new Color("8DBA6A"),
        MeterIllness: new Color("C08AD8"));

    /// <summary>The palette currently applied to the window.</summary>
    public static UiPalette Current { get; private set; } = Light;

    /// <summary>The Theme resource for the current palette.</summary>
    public static Theme Theme { get; private set; } = Build(Light);

    /// <summary>Raised after the palette changes, for colors drawn outside the Theme.</summary>
    public static event Action? Changed;

    public static string Key(UiThemeChoice choice) => choice switch
    {
        UiThemeChoice.Dark => "dark",
        UiThemeChoice.System => "system",
        _ => "light",
    };

    public static UiThemeChoice Parse(string? key) => key?.ToLowerInvariant() switch
    {
        "dark" => UiThemeChoice.Dark,
        "system" => UiThemeChoice.System,
        _ => UiThemeChoice.Light,
    };

    /// <summary>Match system follows the operating system where it reports a dark mode, otherwise stays light.</summary>
    public static UiPalette Resolve(UiThemeChoice choice) => choice switch
    {
        UiThemeChoice.Dark => Dark,
        UiThemeChoice.System => DisplayServer.IsDarkModeSupported() && DisplayServer.IsDarkMode() ? Dark : Light,
        _ => Light,
    };

    /// <summary>Applies a palette to the whole window, including popups, tooltips and dialogs.</summary>
    public static void Apply(Window root, UiPalette palette)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(palette);
        if (!ReferenceEquals(palette, Current) || root.Theme != Theme)
        {
            Current = palette;
            Theme = Build(palette);
            root.Theme = Theme;
            Changed?.Invoke();
        }
    }

    /// <summary>WCAG contrast ratio between two opaque colors, for readability checks.</summary>
    public static float Contrast(Color a, Color b)
    {
        static float Luminance(Color color)
        {
            static float Channel(float value) => value <= 0.03928f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Channel(color.R) + 0.7152f * Channel(color.G) + 0.0722f * Channel(color.B);
        }
        var (light, dark) = (Luminance(a), Luminance(b));
        if (dark > light) (light, dark) = (dark, light);
        return (light + 0.05f) / (dark + 0.05f);
    }

    public static Theme Build(UiPalette p)
    {
        var theme = new Theme { DefaultFont = UiFonts.Text, DefaultFontSize = UiFonts.Body };

        // Frames and panels.
        var frame = Frame(p, 28, 2, 7);
        theme.SetStylebox("panel", "PanelContainer", frame);
        theme.SetStylebox("panel", "Panel", frame);
        theme.SetTypeVariation("HudPanel", "PanelContainer");
        theme.SetStylebox("panel", "HudPanel", Frame(p, 16, 2, 2, contentMargin: 7));
        theme.SetTypeVariation("InsetPanel", "PanelContainer");
        theme.SetStylebox("panel", "InsetPanel", Box(p.Inset, p.InsetEdge, 2, contentMargin: 8));
        // An inset that opens something when clicked, under the pointer.
        theme.SetTypeVariation("InsetPanelHover", "PanelContainer");
        theme.SetStylebox("panel", "InsetPanelHover", Box(p.Field, p.FieldEdge, 2, contentMargin: 8));
        theme.SetTypeVariation("TopBar", "PanelContainer");
        theme.SetStylebox("panel", "TopBar", TopBar(p));
        // A wax-seal count on a button's corner, and a small warning dot.
        theme.SetTypeVariation("Badge", "PanelContainer");
        theme.SetStylebox("panel", "Badge", Round(p.Seal, p.SealRing, 10, 2));
        theme.SetTypeVariation("WarningDot", "PanelContainer");
        theme.SetStylebox("panel", "WarningDot", Round(p.Ember, p.ButtonEdge, 4, 1));

        // Buttons.
        var button = Bevel(p.Button, p.ButtonLight, p.ButtonDark, p.ButtonEdge);
        var hover = Bevel(p.ButtonHover, p.ButtonLight, p.ButtonDark, p.ButtonEdge);
        var pressed = Bevel(p.Pressed, p.PressedDark, p.PressedLight, p.ButtonEdge);
        var disabled = Bevel(p.FieldDisabled, p.FieldDisabled, p.FieldDisabled, p.PaperEdge);
        SetButton(theme, "Button", button, hover, pressed, disabled, p.Ink, p.InkFaint, Focus(p));
        theme.SetTypeVariation("PrimaryButton", "Button");
        SetButton(theme, "PrimaryButton",
            Bevel(p.Primary, p.PrimaryLight, p.PrimaryDark, p.PrimaryEdge),
            Bevel(p.PrimaryLight, p.PrimaryLight, p.PrimaryDark, p.PrimaryEdge),
            Bevel(p.PrimaryDark, p.PrimaryEdge, p.Primary, p.PrimaryEdge),
            disabled, p.PrimaryInk, p.InkFaint, Focus(p));
        // Square icon buttons: close (×), back (‹) and other one-glyph actions.
        // Their white icons take the ink color, so they follow the theme.
        theme.SetTypeVariation("IconButton", "Button");
        SetButton(theme, "IconButton",
            Bevel(p.Button, p.ButtonLight, p.ButtonDark, p.ButtonEdge, IconButtonMargin, IconButtonMargin),
            Bevel(p.ButtonHover, p.ButtonLight, p.ButtonDark, p.ButtonEdge, IconButtonMargin, IconButtonMargin),
            Bevel(p.Pressed, p.PressedDark, p.PressedLight, p.ButtonEdge, IconButtonMargin, IconButtonMargin),
            Bevel(p.FieldDisabled, p.FieldDisabled, p.FieldDisabled, p.PaperEdge, IconButtonMargin, IconButtonMargin),
            p.Ink, p.InkFaint, Focus(p));
        foreach (var item in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color" })
            theme.SetColor(item, "IconButton", p.Ink);
        theme.SetColor("icon_disabled_color", "IconButton", p.InkFaint);
        // Actions that cannot be undone, such as permanent deletion.
        theme.SetTypeVariation("DangerButton", "Button");
        SetButton(theme, "DangerButton",
            Bevel(p.Seal, p.SealRing, p.Seal.Darkened(0.3f), p.ButtonEdge),
            Bevel(p.SealRing, p.SealRing, p.Seal.Darkened(0.3f), p.ButtonEdge),
            Bevel(p.Seal.Darkened(0.3f), p.ButtonEdge, p.Seal, p.ButtonEdge),
            disabled, p.SealInk, p.InkFaint, Focus(p));
        theme.SetTypeVariation("EmberButton", "Button");
        SetButton(theme, "EmberButton",
            Bevel(p.Ember, p.EmberLight, p.EmberDark, p.EmberEdge),
            Bevel(p.EmberLight, p.EmberLight, p.EmberDark, p.EmberEdge),
            Bevel(p.EmberDark, p.EmberEdge, p.Ember, p.EmberEdge),
            disabled, p.EmberInk, p.InkFaint, Focus(p));
        // Settings categories and other tab-like selectors: flat until chosen.
        theme.SetTypeVariation("TabButton", "Button");
        var tabIdle = new StyleBoxEmpty { ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 7, ContentMarginBottom = 7 };
        SetButton(theme, "TabButton", tabIdle, Flat(p.ButtonHover, 12, 7), pressed, tabIdle, p.InkMuted, p.InkFaint, Focus(p));
        theme.SetColor("font_hover_color", "TabButton", p.Ink);
        theme.SetColor("font_pressed_color", "TabButton", p.Ink);
        theme.SetColor("font_hover_pressed_color", "TabButton", p.Ink);
        theme.SetStylebox("hover_pressed", "TabButton", pressed);

        // Text.
        SetLabel(theme, "Label", p.Ink);
        SetLabel(theme, "HeadingLabel", p.Ink, "Label");
        SetLabel(theme, "SectionLabel", p.Section, "Label");
        SetLabel(theme, "SoftLabel", p.InkSoft, "Label");
        SetLabel(theme, "DimLabel", p.InkMuted, "Label");
        SetLabel(theme, "WarningLabel", p.Warning, "Label");
        SetLabel(theme, "GoodLabel", p.Good, "Label");
        SetLabel(theme, "BadLabel", p.Bad, "Label");
        SetLabel(theme, "KeyLabel", p.Section, "Label");
        SetLabel(theme, "BadgeLabel", p.SealInk, "Label");
        SetLabel(theme, "NewMarkLabel", p.Warning, "Label");
        // Text sitting straight on a wooden bar rather than on parchment.
        SetLabel(theme, "WoodLabel", p.OnWood, "Label");
        SetLabel(theme, "WoodSoftLabel", p.OnWoodSoft, "Label");
        // Titles, headings, section labels and counts use the Timber lettering.
        SetLabel(theme, "TitleLabel", p.Ink, "Label");
        foreach (var (type, size) in new[] { ("TitleLabel", UiFonts.Title), ("HeadingLabel", UiFonts.Heading),
            ("SectionLabel", UiFonts.Body), ("BadgeLabel", UiFonts.Body) })
        {
            theme.SetFont("font", type, UiFonts.Headings);
            theme.SetFontSize("font_size", type, size);
        }
        theme.SetColor("default_color", "RichTextLabel", p.Ink);
        theme.SetColor("font_selected_color", "RichTextLabel", p.Ink);
        theme.SetColor("selection_color", "RichTextLabel", p.Selection);
        theme.SetStylebox("normal", "RichTextLabel", new StyleBoxEmpty());
        theme.SetStylebox("focus", "RichTextLabel", new StyleBoxEmpty());

        // Fields and choosers.
        var field = Box(p.Field, p.FieldEdge, 2, 10, 6);
        var fieldFocus = Box(p.Field, p.Primary, 2, 10, 6);
        var fieldOff = Box(p.FieldDisabled, p.PaperEdge, 2, 10, 6);
        foreach (var type in new[] { "LineEdit", "TextEdit" })
        {
            theme.SetStylebox("normal", type, field);
            theme.SetStylebox("focus", type, fieldFocus);
            theme.SetStylebox("read_only", type, fieldOff);
            theme.SetColor("font_color", type, p.Ink);
            theme.SetColor("font_placeholder_color", type, p.InkFaint);
            theme.SetColor("font_uneditable_color", type, p.InkMuted);
            theme.SetColor("font_readonly_color", type, p.InkMuted);
            theme.SetColor("caret_color", type, p.Ink);
            theme.SetColor("selection_color", type, p.Selection);
            theme.SetColor("font_selected_color", type, p.Ink);
        }
        var fieldHover = Box(p.ButtonHover, p.FieldEdge, 2, 10, 6);
        SetButton(theme, "OptionButton", field, fieldHover, fieldFocus, fieldOff, p.Ink, p.InkFaint, Focus(p));
        theme.SetIcon("arrow", "OptionButton", Arrow(p.Ink));
        theme.SetConstant("arrow_margin", "OptionButton", 8);
        theme.SetConstant("h_separation", "OptionButton", 8);

        // Popups, lists and tooltips.
        var popup = Frame(p, 16, 2, 2, contentMargin: 6);
        theme.SetStylebox("panel", "PopupMenu", popup);
        theme.SetStylebox("hover", "PopupMenu", Flat(p.Pressed, 6, 3));
        theme.SetColor("font_color", "PopupMenu", p.Ink);
        theme.SetColor("font_hover_color", "PopupMenu", p.Ink);
        theme.SetColor("font_disabled_color", "PopupMenu", p.InkFaint);
        theme.SetColor("font_separator_color", "PopupMenu", p.InkMuted);
        theme.SetStylebox("separator", "PopupMenu", Line(p.Separator, false));
        theme.SetIcon("radio_checked", "PopupMenu", Dot(p.Primary, 12));
        theme.SetIcon("radio_unchecked", "PopupMenu", Dot(new Color(0, 0, 0, 0), 12));
        theme.SetStylebox("panel", "PopupPanel", popup);
        SetTooltip(theme, p);
        theme.SetColor("font_color", "TooltipLabel", p.Ink);
        theme.SetColor("font_shadow_color", "TooltipLabel", new Color(0, 0, 0, 0));
        theme.SetStylebox("panel", "ItemList", Box(p.Inset, p.InsetEdge, 2, 4, 4));
        theme.SetStylebox("focus", "ItemList", new StyleBoxEmpty());
        theme.SetStylebox("selected", "ItemList", Flat(p.Pressed, 4, 2));
        theme.SetStylebox("selected_focus", "ItemList", Flat(p.Pressed, 4, 2));
        theme.SetStylebox("hovered", "ItemList", Flat(p.ButtonHover, 4, 2));
        theme.SetStylebox("hovered_selected", "ItemList", Flat(p.Pressed, 4, 2));
        theme.SetStylebox("hovered_selected_focus", "ItemList", Flat(p.Pressed, 4, 2));
        theme.SetStylebox("cursor", "ItemList", new StyleBoxEmpty());
        theme.SetStylebox("cursor_unfocused", "ItemList", new StyleBoxEmpty());
        theme.SetColor("font_color", "ItemList", p.Ink);
        theme.SetColor("font_hovered_color", "ItemList", p.Ink);
        theme.SetColor("font_selected_color", "ItemList", p.Ink);
        theme.SetColor("font_hovered_selected_color", "ItemList", p.Ink);
        theme.SetColor("guide_color", "ItemList", new Color(0, 0, 0, 0));

        // Checks and switches.
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            theme.SetStylebox("normal", type, new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 4, ContentMarginBottom = 4 });
            theme.SetStylebox("hover", type, new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 4, ContentMarginBottom = 4 });
            theme.SetStylebox("pressed", type, new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 4, ContentMarginBottom = 4 });
            theme.SetStylebox("hover_pressed", type, new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 4, ContentMarginBottom = 4 });
            theme.SetStylebox("disabled", type, new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 4, ContentMarginBottom = 4 });
            theme.SetStylebox("focus", type, Focus(p));
            theme.SetColor("font_color", type, p.Ink);
            theme.SetColor("font_hover_color", type, p.Ink);
            theme.SetColor("font_pressed_color", type, p.Ink);
            theme.SetColor("font_hover_pressed_color", type, p.Ink);
            theme.SetColor("font_focus_color", type, p.Ink);
            theme.SetColor("font_disabled_color", type, p.InkFaint);
            theme.SetConstant("h_separation", type, 8);
        }
        theme.SetIcon("checked", "CheckBox", Check(p, true, false));
        theme.SetIcon("unchecked", "CheckBox", Check(p, false, false));
        theme.SetIcon("checked_disabled", "CheckBox", Check(p, true, true));
        theme.SetIcon("unchecked_disabled", "CheckBox", Check(p, false, true));
        theme.SetIcon("radio_checked", "CheckBox", Check(p, true, false));
        theme.SetIcon("radio_unchecked", "CheckBox", Check(p, false, false));
        foreach (var mirrored in new[] { "", "_mirrored" })
        {
            theme.SetIcon("checked" + mirrored, "CheckButton", Switch(p, true, false));
            theme.SetIcon("unchecked" + mirrored, "CheckButton", Switch(p, false, false));
            theme.SetIcon("checked_disabled" + mirrored, "CheckButton", Switch(p, true, true));
            theme.SetIcon("unchecked_disabled" + mirrored, "CheckButton", Switch(p, false, true));
        }

        // Scrollbars and separators.
        foreach (var (type, vertical) in new[] { ("VScrollBar", true), ("HScrollBar", false) })
        {
            theme.SetStylebox("scroll", type, Flat(p.Inset, vertical ? 5 : 0, vertical ? 0 : 5, p.InsetEdge));
            theme.SetStylebox("scroll_focus", type, Flat(p.Inset, vertical ? 5 : 0, vertical ? 0 : 5, p.InsetEdge));
            theme.SetStylebox("grabber", type, Bevel(p.Wood, p.WoodLight, p.WoodDark, p.WoodEdge, 3));
            theme.SetStylebox("grabber_highlight", type, Bevel(p.WoodLight, p.WoodLight, p.WoodDark, p.WoodEdge, 3));
            theme.SetStylebox("grabber_pressed", type, Bevel(p.WoodDark, p.WoodEdge, p.Wood, p.WoodEdge, 3));
        }
        theme.SetStylebox("separator", "HSeparator", Line(p.Separator, false));
        theme.SetStylebox("separator", "VSeparator", Line(p.Separator, true));

        // Dialog windows: the frame wraps the title row as well as the body.
        var window = WindowFrame(p);
        theme.SetStylebox("embedded_border", "Window", window);
        theme.SetStylebox("embedded_unfocused_border", "Window", window);
        theme.SetConstant("title_height", "Window", TitleHeight);
        theme.SetColor("title_color", "Window", p.Ink);
        theme.SetFont("title_font", "Window", UiFonts.Headings);
        theme.SetFontSize("title_font_size", "Window", UiFonts.Heading);
        theme.SetIcon("close", "Window", PixelIcons.Texture(PixelGlyph.Close, p.Ink, p.Ink, 1));
        theme.SetIcon("close_pressed", "Window", PixelIcons.Texture(PixelGlyph.Close, p.InkMuted, p.InkMuted, 1));
        theme.SetStylebox("panel", "AcceptDialog", Flat(p.Paper, 16, 12));
        return theme;
    }

    private static int tooltipScale = 1;

    /// <summary>
    /// Tooltips are windows the engine creates on demand, so their text and
    /// frame follow UI Scale through the theme instead.
    /// </summary>
    public static void ScaleTooltips(int factor)
    {
        tooltipScale = Math.Max(1, factor);
        SetTooltip(Theme, Current);
    }

    private static void SetTooltip(Theme theme, UiPalette p)
    {
        theme.SetStylebox("panel", "TooltipPanel", Box(p.Paper, p.Ink, 1, 8, 5, tooltipScale));
        theme.SetFontSize("font_size", "TooltipLabel", UiFonts.Body * tooltipScale);
    }

    /// <summary>
    /// Magnifies a separate window's contents, such as a drop-down list, by UI
    /// Scale. The main interface scales as one layer; windows need their own.
    /// </summary>
    public static void ScaleWindow(Window window, int factor)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        window.ContentScaleFactor = factor;
        window.CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest;
    }

    /// <summary>Scales a dialog's contents and the frame, title and close button drawn around it.</summary>
    public static void ScaleDialog(Window dialog, int factor)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ScaleWindow(dialog, factor);
        var frame = WindowFrame(Current, factor);
        dialog.AddThemeStyleboxOverride("embedded_border", frame);
        dialog.AddThemeStyleboxOverride("embedded_unfocused_border", frame);
        dialog.AddThemeConstantOverride("title_height", TitleHeight * factor);
        // Set on the dialog itself: a window keeps the title font it looked up
        // before the game's theme was applied.
        dialog.AddThemeFontOverride("title_font", UiFonts.Headings);
        dialog.AddThemeFontSizeOverride("title_font_size", UiFonts.Heading * factor);
        dialog.AddThemeIconOverride("close", PixelIcons.Texture(PixelGlyph.Close, Current.Ink, Current.Ink, factor));
        dialog.AddThemeIconOverride("close_pressed", PixelIcons.Texture(PixelGlyph.Close, Current.InkMuted, Current.InkMuted, factor));
    }

    private static void SetButton(Theme theme, string type, StyleBox normal, StyleBox hover, StyleBox pressed,
        StyleBox disabled, Color ink, Color disabledInk, StyleBox focus)
    {
        theme.SetStylebox("normal", type, normal);
        theme.SetStylebox("hover", type, hover);
        theme.SetStylebox("pressed", type, pressed);
        theme.SetStylebox("hover_pressed", type, pressed);
        theme.SetStylebox("disabled", type, disabled);
        theme.SetStylebox("focus", type, focus);
        foreach (var item in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            theme.SetColor(item, type, ink);
        theme.SetColor("font_disabled_color", type, disabledInk);
        foreach (var item in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color" })
            theme.SetColor(item, type, Colors.White);
        theme.SetColor("icon_disabled_color", type, new Color(1, 1, 1, 0.45f));
        theme.SetConstant("h_separation", type, 8);
    }

    private static void SetLabel(Theme theme, string type, Color color, string? baseType = null)
    {
        if (baseType is not null) theme.SetTypeVariation(type, baseType);
        theme.SetColor("font_color", type, color);
        theme.SetColor("font_shadow_color", type, new Color(0, 0, 0, 0));
    }

    private static StyleBoxFlat Flat(Color color, float horizontal, float vertical, Color? border = null)
    {
        var box = new StyleBoxFlat
        {
            BgColor = color,
            ContentMarginLeft = horizontal,
            ContentMarginRight = horizontal,
            ContentMarginTop = vertical,
            ContentMarginBottom = vertical,
        };
        if (border is { } edge)
        {
            box.BorderColor = edge;
            box.BorderWidthLeft = box.BorderWidthRight = box.BorderWidthTop = box.BorderWidthBottom = 1;
        }
        return box;
    }

    private static StyleBoxFlat Round(Color fill, Color ring, int radius, int ringWidth) => new()
    {
        BgColor = fill,
        BorderColor = ring,
        BorderWidthLeft = ringWidth,
        BorderWidthTop = ringWidth,
        BorderWidthRight = ringWidth,
        BorderWidthBottom = ringWidth,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius,
    };

    private static StyleBoxLine Line(Color color, bool vertical) =>
        new() { Color = color, Thickness = 2, Vertical = vertical };

    /// <summary>A visible keyboard-focus ring that sits just outside the control.</summary>
    private static StyleBoxFlat Focus(UiPalette p) => new()
    {
        DrawCenter = false,
        BorderColor = p.Primary,
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        ExpandMarginLeft = 2,
        ExpandMarginTop = 2,
        ExpandMarginRight = 2,
        ExpandMarginBottom = 2,
    };

    /// <summary>Padding around an icon button's 12-pixel glyph, so the button is a 32-pixel square.</summary>
    public const int IconButtonMargin = 10;

    /// <summary>A raised pixel button: dark outline with clipped corners, lit top-left, shaded bottom-right.</summary>
    private static StyleBoxTexture Bevel(Color face, Color light, Color dark, Color edge, int contentVertical = 7, int contentHorizontal = 12)
    {
        const int Size = 12;
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var ex = Math.Min(x, Size - 1 - x);
                var ey = Math.Min(y, Size - 1 - y);
                Color color;
                if (ex + ey < 2 && ex < 2 && ey < 2) color = new Color(0, 0, 0, 0);
                else if (ex < 2 || ey < 2) color = edge;
                // Split along the anti-diagonal, so the lit and shaded bands
                // meet at the top-right and bottom-left corners.
                else if (ex < 4 || ey < 4) color = x + y < Size - 1 ? light : dark;
                else color = face;
                image.SetPixel(x, y, color);
            }
        return Nine(image, 4, contentHorizontal, contentVertical);
    }

    private const int TitleHeight = 30;

    private static StyleBoxTexture WindowFrame(UiPalette p, int scale = 1)
    {
        var window = Frame(p, 28, 2, 7, scale: scale);
        window.ExpandMarginTop = (TitleHeight + 10) * scale;
        window.ExpandMarginLeft = window.ExpandMarginRight = window.ExpandMarginBottom = 10 * scale;
        return window;
    }

    /// <summary>A wooden frame around a parchment panel.</summary>
    private static StyleBoxTexture Frame(UiPalette p, int size, int edge, int wood, float? contentMargin = null, int scale = 1)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var border = edge + wood + 1;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var ex = Math.Min(x, size - 1 - x);
                var ey = Math.Min(y, size - 1 - y);
                var depth = Math.Min(ex, ey);
                Color color;
                if (ex + ey < 2 && ex < 2 && ey < 2) color = new Color(0, 0, 0, 0);
                else if (depth < edge) color = p.WoodEdge;
                else if (depth < edge + wood)
                {
                    var lit = x + y < size - 1;
                    color = depth < edge + Math.Min(2, wood - 1) ? (lit ? p.WoodLight : p.WoodDark) :
                        depth == edge + wood - 1 && wood > 3 ? p.WoodDark.Lerp(p.Wood, 0.5f) : p.Wood;
                }
                else if (depth == border - 1) color = p.PaperEdge;
                else color = p.Paper;
                image.SetPixel(x, y, color);
            }
        return Nine(image, border, contentMargin ?? border, contentMargin ?? border, scale);
    }

    /// <summary>A filled box with a solid pixel border, for fields, insets and tooltips.</summary>
    private static StyleBoxTexture Box(Color fill, Color edge, int width, float horizontal = -1, float vertical = -1, int scale = 1)
    {
        var size = width * 2 + 4;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var ex = Math.Min(x, size - 1 - x);
                var ey = Math.Min(y, size - 1 - y);
                image.SetPixel(x, y, ex < width || ey < width ? edge : fill);
            }
        return Nine(image, width, horizontal < 0 ? width : horizontal, vertical < 0 ? width : vertical, scale);
    }

    private static StyleBoxTexture Box(Color fill, Color edge, int width, float contentMargin) =>
        Box(fill, edge, width, contentMargin, contentMargin);

    /// <summary>The top bar: a plank with a lit upper lip and a dark lower edge.</summary>
    private static StyleBoxTexture TopBar(UiPalette p)
    {
        var image = Image.CreateEmpty(4, 12, false, Image.Format.Rgba8);
        for (var y = 0; y < 12; y++)
            for (var x = 0; x < 4; x++)
                image.SetPixel(x, y, y < 2 ? p.WoodLight : y >= 10 ? p.WoodEdge : y >= 8 ? p.WoodDark : p.Wood);
        var box = new StyleBoxTexture
        {
            Texture = ImageTexture.CreateFromImage(image),
            TextureMarginTop = 2,
            TextureMarginBottom = 4,
            TextureMarginLeft = 1,
            TextureMarginRight = 1,
        };
        box.ContentMarginLeft = box.ContentMarginRight = 0;
        box.ContentMarginTop = box.ContentMarginBottom = 0;
        return box;
    }

    /// <summary>A nine-patch box, optionally drawn larger by a whole number with hard pixel edges.</summary>
    private static StyleBoxTexture Nine(Image image, int margin, float horizontal, float vertical, int scale = 1)
    {
        if (scale > 1)
            image.Resize(image.GetWidth() * scale, image.GetHeight() * scale, Image.Interpolation.Nearest);
        var box = new StyleBoxTexture
        {
            Texture = ImageTexture.CreateFromImage(image),
            TextureMarginLeft = margin * scale,
            TextureMarginTop = margin * scale,
            TextureMarginRight = margin * scale,
            TextureMarginBottom = margin * scale,
        };
        box.ContentMarginLeft = box.ContentMarginRight = horizontal * scale;
        box.ContentMarginTop = box.ContentMarginBottom = vertical * scale;
        return box;
    }

    private static ImageTexture Arrow(Color color)
    {
        var image = Image.CreateEmpty(10, 6, false, Image.Format.Rgba8);
        for (var y = 0; y < 5; y++)
            for (var x = y; x < 10 - y; x++)
                image.SetPixel(x, y + 1, color);
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture Dot(Color color, int size)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 3; y < size - 3; y++)
            for (var x = 3; x < size - 3; x++)
                if ((x is 3 or 8 && y is 3 or 8) == false) image.SetPixel(x, y, color);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A square pixel checkbox; checked boxes are filled with a two-pixel tick.</summary>
    private static ImageTexture Check(UiPalette p, bool on, bool disabled)
    {
        const int Size = 18;
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        var edge = on ? p.PrimaryEdge : p.FieldEdge;
        var fill = on ? p.Primary : p.Field;
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var ex = Math.Min(x, Size - 1 - x);
                var ey = Math.Min(y, Size - 1 - y);
                image.SetPixel(x, y, ex < 2 || ey < 2 ? edge : fill);
            }
        if (on)
        {
            (int X, int Y)[] tick = [(4, 9), (5, 10), (6, 11), (7, 12), (8, 11), (9, 10), (10, 9), (11, 8), (12, 7), (13, 6)];
            foreach (var (x, y) in tick)
            {
                image.SetPixel(x, y, p.PrimaryInk);
                image.SetPixel(x, y - 1, p.PrimaryInk);
            }
        }
        if (disabled) Fade(image);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A pixel on/off switch: a knob sitting left on a plain track, or right on a green one.</summary>
    private static ImageTexture Switch(UiPalette p, bool on, bool disabled)
    {
        const int Width = 36;
        const int Height = 20;
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        var edge = on ? p.PrimaryEdge : p.FieldEdge;
        var fill = on ? p.Primary : p.Field;
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var ex = Math.Min(x, Width - 1 - x);
                var ey = Math.Min(y, Height - 1 - y);
                image.SetPixel(x, y, ex < 2 || ey < 2 ? edge : fill);
            }
        var knobLeft = on ? Width - 16 : 4;
        for (var y = 4; y < Height - 4; y++)
            for (var x = knobLeft; x < knobLeft + 12; x++)
            {
                var edgeHere = x == knobLeft || x == knobLeft + 11 || y == 4 || y == Height - 5;
                image.SetPixel(x, y, on ? p.PrimaryInk : edgeHere ? p.ButtonEdge : p.Button);
            }
        if (disabled) Fade(image);
        return ImageTexture.CreateFromImage(image);
    }

    private static void Fade(Image image)
    {
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var color = image.GetPixel(x, y);
                image.SetPixel(x, y, color with { A = color.A * 0.45f });
            }
    }
}
