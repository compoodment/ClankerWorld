using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static void CheckThemeTextContrast()
    {
        var failures = new List<string>();
        foreach (var palette in new[] { UiTheme.Light, UiTheme.Dark })
        {
            using var theme = UiTheme.Build(palette);
            var checkedColors = new HashSet<(string Type, string Color)>();
            var paper = Surface("PanelContainer", "panel");
            var textSurfaces = new[]
            {
                ("parchment", paper),
                ("inset", Surface("InsetPanel", "panel")),
                ("hovered inset", Surface("InsetPanelHover", "panel")),
                ("selected inset", Surface("InsetPanelSelected", "panel")),
            };

            Color Surface(string type, string style) => ThemeTextSurface(theme.GetStylebox(style, type), paper: palette.Paper);
            void Check(string type, string color, string context, Color surface, float minimum = 4.5f)
            {
                checkedColors.Add((type, color));
                var foreground = theme.GetColor(color, type);
                var ratio = UiTheme.Contrast(Composite(foreground, surface), surface);
                if (ratio < minimum)
                    failures.Add($"{palette.Name} {type}.{color} on {context}: {ratio:0.00}:1 < {minimum}:1");
            }

            foreach (var type in new[] { "Button", "PrimaryButton", "DangerButton", "EmberButton", "IconButton", "TabButton", "OptionButton", "CheckBox", "CheckButton" })
            {
                foreach (var (color, style) in new[]
                {
                    ("font_color", "normal"), ("font_hover_color", "hover"),
                    ("font_pressed_color", "pressed"), ("font_hover_pressed_color", "hover_pressed"),
                    ("font_disabled_color", "disabled"),
                })
                {
                    var box = theme.GetStylebox(style, type);
                    if (box is StyleBoxEmpty)
                        foreach (var (context, surface) in textSurfaces) Check(type, color, context, surface);
                    else Check(type, color, style, Surface(type, style));
                }
                // Godot Button uses focus ink only in DRAW_NORMAL; hovered/pressed keep their own ink.
                if (theme.GetStylebox("normal", type) is StyleBoxEmpty)
                    foreach (var (context, surface) in textSurfaces) Check(type, "font_focus_color", $"focused {context}", surface);
                else Check(type, "font_focus_color", "focused normal", Surface(type, "normal"));
            }

            foreach (var type in new[] { "Label", "HeadingLabel", "SectionLabel", "SoftLabel", "DimLabel", "WarningLabel", "GoodLabel", "BadLabel", "KeyLabel", "NewMarkLabel", "TitleLabel", "TagNoteLabel" })
                foreach (var (context, surface) in textSurfaces) Check(type, "font_color", context, surface);
            foreach (var type in new[] { "WoodLabel", "WoodSoftLabel" })
                Check(type, "font_color", "wooden bar", Surface("TopBar", "panel"));
            Check("TagLabel", "font_color", "filled tag", Surface("TagLabel", "normal"));
            Check("TooltipLabel", "font_color", "tooltip", Surface("TooltipPanel", "panel"));
            Check("Window", "title_color", "dialog frame", Surface("Window", "embedded_border"));

            foreach (var (context, surface) in textSurfaces)
            {
                Check("RichTextLabel", "default_color", context, surface);
                Check("RichTextLabel", "font_selected_color", $"selected text on {context}", Composite(theme.GetColor("selection_color", "RichTextLabel"), surface));
            }
            foreach (var type in new[] { "LineEdit", "TextEdit" })
            {
                foreach (var style in new[] { "normal", "read_only" })
                {
                    var surface = Surface(type, style);
                    Check(type, "font_color", style, surface);
                    Check(type, "font_placeholder_color", $"placeholder {style}", surface);
                    Check(type, "font_selected_color", $"selected {style}", Composite(theme.GetColor("selection_color", type), surface));
                }
                Check(type, "font_uneditable_color", "uneditable field", Surface(type, "read_only"));
                Check(type, "font_readonly_color", "read-only field", Surface(type, "read_only"));
            }
            foreach (var color in new[] { "font_color", "font_disabled_color", "font_separator_color" })
                Check("PopupMenu", color, "popup", Surface("PopupMenu", "panel"));
            Check("PopupMenu", "font_hover_color", "hovered menu item", Surface("PopupMenu", "hover"));
            foreach (var (color, style) in new[]
            {
                ("font_color", "panel"), ("font_hovered_color", "hovered"),
                ("font_selected_color", "selected"), ("font_selected_color", "selected_focus"),
                ("font_hovered_selected_color", "hovered_selected"), ("font_hovered_selected_color", "hovered_selected_focus"),
            }) Check("ItemList", color, style, Surface("ItemList", style));

            void CheckCustom(string context, Color ink, Color surface)
            {
                var ratio = UiTheme.Contrast(Composite(ink, surface), surface);
                if (ratio < 4.5f) failures.Add($"{palette.Name} {context}: {ratio:0.00}:1 < 4.5:1");
            }
            foreach (var fill in new[] { palette.Seal, palette.Ember })
                CheckCustom("count badge", UiTheme.ReadableInk(palette, palette.SealInk, fill), fill);
            for (var number = 0; number <= 4; number++)
            {
                var branch = SaveTimelineLayout.BranchColor(palette, number);
                CheckCustom($"saved branch {number} badge", UiTheme.ReadableInk(palette, palette.Paper, branch), branch);
                CheckCustom($"unsaved branch {number} badge", UiTheme.ReadableInk(palette, branch, palette.Inset), palette.Inset);
            }
            foreach (var (context, surface) in textSurfaces)
                CheckCustom($"historical roster text on {context}", palette.InkMuted, surface);
            foreach (var ink in new[] { palette.Section, palette.Link, palette.Warning, palette.InkMuted })
                foreach (var (context, surface) in textSurfaces.Where(pair => pair.Item1 != "selected inset"))
                    CheckCustom($"rich text accent on {context}", ink, surface);

            // A new text color must acquire a surface/state assertion rather than silently escaping this check.
            foreach (var typeName in theme.GetColorTypeList())
            {
                var type = typeName.ToString();
                foreach (var colorName in theme.GetColorList(type))
                {
                    var color = colorName.ToString();
                    if ((color.StartsWith("font_", StringComparison.Ordinal) && color != "font_shadow_color" || color is "default_color" or "title_color") &&
                        !checkedColors.Contains((type, color)))
                        failures.Add($"{palette.Name} {type}.{color} has no text contrast assertion.");
                }
            }
            if (UiTheme.Contrast(theme.GetColor("font_color", "Label"), paper) < 7f)
                failures.Add($"{palette.Name} normal parchment text must retain 7:1 contrast.");
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("Theme text contrast failed:\n" + string.Join('\n', failures));
    }

    private static Color ThemeTextSurface(StyleBox style, Color paper) => style switch
    {
        StyleBoxEmpty => paper,
        StyleBoxFlat flat => Composite(flat.BgColor, paper),
        StyleBoxTexture texture => TextureCenter(texture),
        _ => throw new InvalidOperationException($"Unknown text surface {style.GetClass()}."),
    };

    private static Color TextureCenter(StyleBoxTexture texture)
    {
        using var image = texture.Texture.GetImage();
        return image.GetPixel(image.GetWidth() / 2, image.GetHeight() / 2);
    }

    private static Color Composite(Color ink, Color surface) => new(
        ink.R * ink.A + surface.R * (1 - ink.A),
        ink.G * ink.A + surface.G * (1 - ink.A),
        ink.B * ink.A + surface.B * (1 - ink.A));
}
