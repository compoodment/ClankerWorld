#pragma warning disable CA1859, CA1822
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

// LOCAL ONLY - never commit. Agent card and map nameplate mockups for the owner to pick from.
public partial class Main
{
    private static readonly Dictionary<string, string[]> MockGlyphs = new()
    {
        ["pencil"] = ["............", ".........##.", "........#oo#", ".......#oo#.", "......#oo#..", ".....#oo#...", "....#oo#....", "...#oo#.....", "..#oo#......", "..#o#.......", "..##........", "............"],
        ["find"] = [".....##.....", "...######...", "..#..##..#..", ".#...##...#.", ".#........#.", "####.oo.####", "####.oo.####", ".#........#.", ".#...##...#.", "..#..##..#..", "...######...", ".....##....."],
        ["tree"] = ["............", "....####....", "....#oo#....", "....####....", ".....##.....", "..########..", "..#......#..", ".####..####.", ".#oo#..#oo#.", ".####..####.", "............", "............"],
        ["key"] = ["............", "............", "............", ".###........", "#ooo#.......", "#o.o#######.", "#ooo#######.", ".###...#.##.", ".......#.##.", "............", "............", "............"],
        ["speech"] = ["............", ".##########.", "#oooooooooo#", "#o#######oo#", "#oooooooooo#", "#o#####oooo#", "#oooooooooo#", ".##########.", "...##.......", "...#........", "............", "............"],
        ["heart"] = ["............", "............", "..###..###..", ".#ooo##ooo#.", ".#oooooooo#.", ".#oooooooo#.", "..#oooooo#..", "...#oooo#...", "....#oo#....", ".....##.....", "............", "............"],
        ["thought"] = ["............", "...######...", "..#oooooo#..", ".#oooooooo#.", ".#oooooooo#.", "..#oooooo#..", "...######...", "...##.......", "...##.......", ".#..........", "............", "............"],
        ["basket"] = ["............", "............", "....####....", "...#....#...", "..#......#..", ".##########.", ".#oooooooo#.", ".#o#o#o#o##.", ".#oooooooo#.", "..#oooooo#..", "...######...", "............"],
        ["flame"] = ["............", ".....#......", ".....##.....", "....#o#.....", "...#oo##....", "...#ooo#....", "..#ooooo#...", "..#oo#oo#...", "..#o###o#...", "...#####....", "............", "............"],
        ["hammer"] = ["............", "..######....", ".#oooooo#...", ".#oooooo#...", "..###o##....", "....#o#.....", "....#o#.....", "....#o#.....", "....#o#.....", "....###.....", "............", "............"],
        ["wheat"] = [".....#......", "....#o#.....", "...#o#o#....", "....#o#.....", "...#o#o#....", "....#o#.....", "...#o#o#....", ".....#......", ".....#......", ".....#......", ".....#......", "............"],
    };

    private static ImageTexture MockIcon(string key, Color main, Color accent, int scale = 1)
    {
        var rows = MockGlyphs[key];
        var image = Image.CreateEmpty(12 * scale, 12 * scale, false, Image.Format.Rgba8);
        for (var y = 0; y < 12; y++)
            for (var x = 0; x < 12; x++)
            {
                var color = rows[y][x] switch { '#' => main, 'o' => accent, _ => new Color(0, 0, 0, 0) };
                if (color.A > 0) image.FillRect(new Rect2I(x * scale, y * scale, scale, scale), color);
            }
        return ImageTexture.CreateFromImage(image);
    }

    private static bool MockDark => UiTheme.Current.Name == "dark";
    private static Color MockFood => MockDark ? new Color("E8B04A") : new Color("B77C10");
    private static Color MockWarm => MockDark ? new Color("F08A5A") : new Color("C8502A");
    private static Color MockDiet => MockDark ? new Color("8DBA6A") : new Color("4A7033");
    private static Color MockIll => MockDark ? new Color("C08AD8") : new Color("7A4A96");
    private static Color MockAccent => MockDark ? new Color("C99A62") : new Color("9C6C42");

    private static Label MockText(string text, string variation = "", bool wrap = false)
    {
        var label = new Label { Text = text, ThemeTypeVariation = variation };
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.CustomMinimumSize = new Vector2(40, 0);
        }
        return label;
    }

    private static Label MockSection(string text) => new() { Text = text.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel" };

    private static void MockCompact(Button button, int horizontal, int vertical)
    {
        var type = button.ThemeTypeVariation.ToString() is { Length: > 0 } variation ? variation : "Button";
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            var box = (StyleBox)UiTheme.Theme.GetStylebox(state, type).Duplicate();
            box.ContentMarginLeft = box.ContentMarginRight = horizontal;
            box.ContentMarginTop = box.ContentMarginBottom = vertical;
            button.AddThemeStyleboxOverride(state, box);
        }
    }

    private static Button MockIconButton(ImageTexture icon, string tip)
    {
        var button = new Button { Icon = icon, TooltipText = tip, IconAlignment = HorizontalAlignment.Center, FocusMode = Control.FocusModeEnum.None };
        MockCompact(button, 6, 6);
        return button;
    }

    private static Button MockButton(string text, ImageTexture? icon = null, bool primary = false, bool expand = false)
    {
        var button = new Button { Text = text, Icon = icon, FocusMode = Control.FocusModeEnum.None };
        if (primary) button.ThemeTypeVariation = "PrimaryButton";
        MockCompact(button, 9, 6);
        if (expand) button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return button;
    }

    private static Button MockFlatIcon(string glyph, string tip)
    {
        var button = new Button
        {
            Flat = true,
            Icon = MockIcon(glyph, UiTheme.Current.InkMuted, MockAccent),
            TooltipText = tip,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginLeft = 3, ContentMarginRight = 3, ContentMarginTop = 2, ContentMarginBottom = 2 });
        return button;
    }

    /// <summary>A segmented pixel meter with its caption, like "Warmth ▰▰▰▰▰▰▰▰▱▱".</summary>
    private static Control MockMeter(string caption, int percent, Color fill, int captionWidth, bool showValue = false, int segments = 10)
    {
        var width = captionWidth + segments * 6 + 1 + (showValue ? 34 : 0);
        var control = new Control { CustomMinimumSize = new Vector2(width, 14), TooltipText = $"{caption} {percent}%", MouseFilter = Control.MouseFilterEnum.Pass };
        control.Draw += () =>
        {
            var p = UiTheme.Current;
            var font = UiFonts.Text;
            var baseline = Mathf.Floor((control.Size.Y - font.GetHeight(12)) / 2) + font.GetAscent(12);
            if (captionWidth > 0)
                control.DrawString(font, new Vector2(0, baseline), caption, fontSize: 12, modulate: p.InkMuted);
            var x = (float)captionWidth;
            var y = Mathf.Floor((control.Size.Y - 9) / 2);
            control.DrawRect(new Rect2(x, y, segments * 6 + 1, 9), p.ButtonEdge);
            var lit = (int)Math.Round(percent / 100f * segments);
            if (percent > 0 && lit == 0) lit = 1;
            for (var i = 0; i < segments; i++)
            {
                var cell = new Rect2(x + 1 + i * 6, y + 1, 5, 7);
                if (i < lit)
                {
                    control.DrawRect(cell, fill);
                    control.DrawRect(new Rect2(cell.Position, new Vector2(5, 1)), fill.Lightened(0.35f));
                    control.DrawRect(new Rect2(cell.Position.X, cell.End.Y - 1, 5, 1), fill.Darkened(0.25f));
                }
                else control.DrawRect(cell, p.Inset);
            }
            if (showValue)
                control.DrawString(font, new Vector2(x + segments * 6 + 7, baseline), $"{percent}%", fontSize: 12, modulate: p.InkMuted);
        };
        return control;
    }

    private static Control MockPortrait(OwnerWorldInhabitant who, int scale)
    {
        var frame = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = MockDark ? new Color("3E5A2E") : new Color("8FB06A"),
            BorderColor = UiTheme.Current.WoodEdge,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 2, ContentMarginTop = 2, ContentMarginRight = 2, ContentMarginBottom = 2,
        });
        var age = who.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail;
        var sprite = new AtlasTexture
        {
            Atlas = AgentSprites.Atlas(32),
            Region = AgentSprites.Region(AgentSprites.VariantFor(who.Id), AgentSprites.StageIndex(age), 32),
        };
        frame.AddChild(new TextureRect
        {
            Texture = sprite,
            CustomMinimumSize = new Vector2(32 * scale, 32 * scale),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        });
        return frame;
    }

    private string MockThoughtTime(long tick, long now)
    {
        var full = DisplayWorldClock(tick);
        var today = DisplayWorldClock(now);
        var split = full.LastIndexOf(" · ", StringComparison.Ordinal);
        return split > 0 && today.StartsWith(full[..split], StringComparison.Ordinal) ? full[(split + 3)..] : full;
    }

    private Control MockThoughts(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant who, int count, int height)
    {
        var inset = new PanelContainer { ThemeTypeVariation = "InsetPanel" };
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            FitContent = height <= 0,
            CustomMinimumSize = new Vector2(0, Math.Max(0, height)),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        text.AddThemeConstantOverride("paragraph_separation", 4);
        var dim = UiTheme.Current.InkMuted.ToHtml(false);
        text.Text = string.Join("\n", who.RecentPrivateThoughts.Reverse().Take(count).Select(thought =>
            $"[color=#{dim}]{MockThoughtTime(thought.WorldTick, snapshot.WorldTick)}[/color]  {thought.Text}"));
        inset.AddChild(text);
        inset.TooltipText = "Only you can see these thoughts.";
        return inset;
    }

    private static Control MockPeopleInline()
    {
        var p = UiTheme.Current;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        row.AddChild(new TextureRect { Texture = MockIcon("heart", p.Partner.Darkened(0.35f), p.Partner), StretchMode = TextureRect.StretchModeEnum.KeepCentered });
        row.AddChild(MockText("Partner", "DimLabel"));
        row.AddChild(MockText("Rowan"));
        row.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        row.AddChild(new TextureRect { Texture = PixelIcons.Themed(PixelGlyph.Person, MockAccent, 1), StretchMode = TextureRect.StretchModeEnum.KeepCentered });
        row.AddChild(MockText("Trusts", "DimLabel"));
        row.AddChild(MockText("Ash 7/10"));
        return row;
    }

    private static Control MockPeople()
    {
        var p = UiTheme.Current;
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 3);
        grid.AddChild(new TextureRect { Texture = MockIcon("heart", p.Partner.Darkened(0.35f), p.Partner), StretchMode = TextureRect.StretchModeEnum.KeepCentered });
        grid.AddChild(MockText("Partner", "DimLabel"));
        grid.AddChild(MockText("Rowan"));
        grid.AddChild(new TextureRect { Texture = PixelIcons.Themed(PixelGlyph.Person, MockAccent, 1), StretchMode = TextureRect.StretchModeEnum.KeepCentered });
        grid.AddChild(MockText("Trusts", "DimLabel"));
        grid.AddChild(MockText("Ash · 7 of 10"));
        return grid;
    }

    private static Control MockActions(bool stacked, bool shortLabels)
    {
        var p = UiTheme.Current;
        BoxContainer row = stacked ? new VBoxContainer() : new HBoxContainer();
        row.AddThemeConstantOverride("separation", stacked ? 4 : 6);
        var memories = MockButton(shortLabels ? "Memories" : "Memories + maps", PixelIcons.Themed(PixelGlyph.Book, MockAccent, 1), expand: !stacked);
        var family = MockButton(shortLabels ? "Family" : "Family Tree", MockIcon("tree", p.Ink, MockDiet), expand: !stacked);
        var model = MockButton(shortLabels ? "Model" : "Model and key", MockIcon("key", p.Ink, MockFood), expand: !stacked);
        foreach (var button in new[] { memories, family, model })
        {
            if (stacked) button.Alignment = HorizontalAlignment.Left;
            row.AddChild(button);
        }
        return row;
    }

    /// <summary>"Speak to them" with a Suggest / Order switch beside the heading, then the message and Send.</summary>
    private static Control MockSpeak(bool multiline = false, bool heading = true)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 0);
        var title = MockSection(heading ? "Speak to them" : "");
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.VerticalAlignment = VerticalAlignment.Center;
        top.AddChild(title);
        foreach (var (text, on) in new[] { ("Suggest", true), ("Order", false) })
        {
            var toggle = new Button { Text = text, ThemeTypeVariation = "TabButton", ToggleMode = true, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None };
            MockCompact(toggle, 7, 3);
            top.AddChild(toggle);
        }
        column.AddChild(top);
        if (multiline)
        {
            column.AddChild(new TextEdit { PlaceholderText = "Say something…", CustomMinimumSize = new Vector2(0, 58), WrapMode = TextEdit.LineWrappingMode.Boundary });
            var send = MockButton("Send", primary: true);
            send.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            column.AddChild(send);
            return column;
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(new LineEdit { PlaceholderText = "Say something…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(MockButton("Send", primary: true));
        column.AddChild(row);
        return column;
    }

    private Control MockHeader(OwnerWorldInhabitant who, bool activityLine, bool tools = true, int portraitScale = 1)
    {
        var p = UiTheme.Current;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(MockPortrait(who, portraitScale));
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        text.AddThemeConstantOverride("separation", 2);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 2);
        nameRow.AddChild(new Label { Text = who.DisplayName, ThemeTypeVariation = "HeadingLabel" });
        nameRow.AddChild(MockFlatIcon("pencil", "Rename"));
        text.AddChild(nameRow);
        text.AddChild(MockText("Adult · 24 years", "DimLabel"));
        if (activityLine) text.AddChild(MockText("Gathering clay by the river", wrap: true));
        row.AddChild(text);
        if (tools)
        {
            var buttons = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            buttons.AddThemeConstantOverride("separation", 4);
            buttons.AddChild(MockIconButton(MockIcon("find", p.Ink, p.Primary), "Find on the map"));
            buttons.AddChild(MockIconButton(PixelIcons.Themed(PixelGlyph.Close, p.Ink, 1), "Close"));
            row.AddChild(buttons);
        }
        return row;
    }

    private static Control MockMeters(int columns, int captionWidth, bool showValue = false, bool withDiet = true)
    {
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 4);
        grid.AddChild(MockMeter("Fullness", 72, MockFood, captionWidth, showValue));
        grid.AddChild(MockMeter("Warmth", 82, MockWarm, captionWidth, showValue));
        if (withDiet) grid.AddChild(MockMeter("Diet", 74, MockDiet, captionWidth, showValue));
        grid.AddChild(MockMeter("Illness", 3, MockIll, captionWidth, showValue));
        return grid;
    }

    private static PanelContainer MockPanel(Control body, float width, string variation = "")
    {
        var card = new PanelContainer { ThemeTypeVariation = variation };
        AddPanelContents(card, body);
        card.CustomMinimumSize = new Vector2(width, 0);
        card.ZIndex = 70;
        return card;
    }

    private static VBoxContainer MockColumn(int separation = 6)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", separation);
        return column;
    }

    /// <summary>A: one tidy column with sections, meters and a one-line speak row.</summary>
    private PanelContainer MockCardA(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant who, float width = 320)
    {
        var body = MockColumn();
        body.AddChild(MockHeader(who, activityLine: true));
        body.AddChild(MockText("Carrying nothing · Clothed · Has a tool", "DimLabel", wrap: true));
        body.AddChild(MockMeters(2, 54));
        body.AddChild(MockSection("Thoughts"));
        body.AddChild(MockThoughts(snapshot, who, 3, 130));
        body.AddChild(MockSection("People"));
        body.AddChild(MockPeopleInline());
        body.AddChild(MockActions(stacked: false, shortLabels: true));
        body.AddChild(MockSpeak());
        return MockPanel(body, width);
    }

    /// <summary>B: a fixed header with tabs underneath, so only one section shows at a time.</summary>
    private PanelContainer MockCardB(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant who, int tab)
    {
        var body = MockColumn();
        body.AddChild(MockHeader(who, activityLine: true));
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 2);
        var names = new[] { "Now", "Thoughts", "People", "Speak" };
        for (var i = 0; i < names.Length; i++)
        {
            var button = new Button { Text = names[i], ThemeTypeVariation = "TabButton", ToggleMode = true, ButtonPressed = i == tab, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            MockCompact(button, 6, 5);
            tabs.AddChild(button);
        }
        body.AddChild(tabs);
        var page = new PanelContainer { ThemeTypeVariation = "InsetPanel", CustomMinimumSize = new Vector2(0, 168) };
        var content = MockColumn(5);
        switch (tab)
        {
            case 0:
                content.AddChild(MockMeters(2, 54));
                content.AddChild(MockText("Carrying nothing", wrap: true));
                content.AddChild(MockText("Clothed · Has a tool", wrap: true));
                content.AddChild(MockText("Chosen by OpenAI", "DimLabel"));
                break;
            case 1:
                var list = MockThoughts(snapshot, who, 3, 120);
                list.ThemeTypeVariation = string.Empty;
                list.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
                content.AddChild(list);
                break;
            case 2:
                content.AddChild(MockPeople());
                break;
            default:
                content.AddChild(MockSpeak(multiline: true, heading: false));
                break;
        }
        page.AddChild(content);
        body.AddChild(page);
        body.AddChild(MockActions(stacked: false, shortLabels: true));
        return MockPanel(body, 300);
    }

    /// <summary>C: a wider, shorter character sheet in two columns.</summary>
    private PanelContainer MockCardC(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant who)
    {
        var p = UiTheme.Current;
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 10);

        var left = MockColumn(5);
        left.CustomMinimumSize = new Vector2(128, 0);
        left.AddChild(MockPortrait(who, 2));
        var nameRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        nameRow.AddChild(new Label { Text = who.DisplayName, ThemeTypeVariation = "HeadingLabel" });
        nameRow.AddChild(MockFlatIcon("pencil", "Rename"));
        left.AddChild(nameRow);
        left.AddChild(new Label { Text = "Adult · 24 years", ThemeTypeVariation = "DimLabel", HorizontalAlignment = HorizontalAlignment.Center });
        left.AddChild(new HSeparator());
        left.AddChild(MockMeters(1, 58));
        left.AddChild(MockText("Clothed · Has a tool", "DimLabel", wrap: true));
        left.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        left.AddChild(MockActions(stacked: true, shortLabels: false));
        columns.AddChild(left);
        columns.AddChild(new VSeparator());

        var right = MockColumn(5);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 4);
        var now = MockSection("Now");
        now.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(now);
        top.AddChild(MockIconButton(MockIcon("find", p.Ink, p.Primary), "Find on the map"));
        top.AddChild(MockIconButton(PixelIcons.Themed(PixelGlyph.Close, p.Ink, 1), "Close"));
        right.AddChild(top);
        right.AddChild(MockText("Gathering clay by the river.", wrap: true));
        right.AddChild(MockText("Carrying nothing · Chosen by OpenAI", "DimLabel", wrap: true));
        right.AddChild(MockSection("Thoughts"));
        right.AddChild(MockThoughts(snapshot, who, 3, 96));
        right.AddChild(MockSection("People"));
        right.AddChild(MockPeople());
        right.AddChild(MockSpeak());
        columns.AddChild(right);
        return MockPanel(columns, 440);
    }

    /// <summary>D, first step: a small card beside the agent.</summary>
    private PanelContainer MockGlance(OwnerWorldInhabitant who)
    {
        var p = UiTheme.Current;
        var body = MockColumn(5);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 4);
        var name = new Label { Text = who.DisplayName, ThemeTypeVariation = "HeadingLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        top.AddChild(name);
        top.AddChild(MockIconButton(MockIcon("find", p.Ink, p.Primary), "Find on the map"));
        top.AddChild(MockIconButton(PixelIcons.Themed(PixelGlyph.Close, p.Ink, 1), "Close"));
        body.AddChild(top);
        body.AddChild(MockText("Gathering clay by the river", "SoftLabel", wrap: true));
        body.AddChild(MockMeters(1, 54, withDiet: false));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(MockButton("Profile", PixelIcons.Themed(PixelGlyph.Person, MockAccent, 1), expand: true));
        row.AddChild(MockButton("Speak", MockIcon("speech", p.Ink, p.Paper), expand: true));
        body.AddChild(row);
        return MockPanel(body, 206, "HudPanel");
    }

    private static TextureRect MockRoof(BuildingKind kind, int w, int h, BuildingDoor door, float size)
    {
        var texture = ImageTexture.CreateFromImage(BuildingSprites.Render(kind, w, h, 32, door));
        return new TextureRect
        {
            Texture = texture,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private Control MockBuildingHeader(TextureRect roof, string name, string subtitle)
    {
        var p = UiTheme.Current;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(roof);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 4);
        top.AddChild(new Label { Text = name, ThemeTypeVariation = "HeadingLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        top.AddChild(MockIconButton(MockIcon("find", p.Ink, p.Primary), "Find on the map"));
        top.AddChild(MockIconButton(PixelIcons.Themed(PixelGlyph.Close, p.Ink, 1), "Close"));
        text.AddChild(top);
        text.AddChild(MockText(subtitle, "DimLabel", wrap: true));
        row.AddChild(text);
        return row;
    }

    private static GridContainer MockFacts(params (string Key, string Value)[] rows)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 3);
        foreach (var (key, value) in rows)
        {
            grid.AddChild(MockText(key, "DimLabel"));
            grid.AddChild(MockText(value, wrap: true));
        }
        return grid;
    }

    /// <summary>
    /// An item slot: its icon, a count badge in the corner (left off where a
    /// count means nothing, such as what a building makes), and optionally its
    /// name underneath, wrapped to two short lines.
    /// </summary>
    private static Control MockSlot(string kind, int? count, int iconSize, bool named)
    {
        var box = iconSize + 12;
        var cell = named ? Math.Max(box, 52) : box;
        var slot = new Control
        {
            CustomMinimumSize = new Vector2(cell, box + (named ? 28 : 0)),
            TooltipText = count is { } shown ? $"{Pretty(kind)} × {shown}" : Pretty(kind),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        slot.Draw += () =>
        {
            var p = UiTheme.Current;
            var font = UiFonts.Text;
            var left = Mathf.Floor((cell - box) / 2f);
            slot.DrawRect(new Rect2(left, 0, box, box), MockDark ? p.Paper.Lightened(0.07f) : p.Paper.Darkened(0.06f));
            slot.DrawRect(new Rect2(left + 0.5f, 0.5f, box - 1, box - 1), p.PaperEdge, false, 1);
            slot.DrawTextureRect(ItemIcons.Texture(kind, iconSize), new Rect2(left + 6, count is null ? 6 : 4, iconSize, iconSize), false);
            if (count is { } number)
            {
                var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 12).X;
                var badge = new Rect2(left + box - width - 7, box - 12, width + 7, 13);
                slot.DrawRect(badge, new Color("2E2016"));
                slot.DrawString(font, new Vector2(badge.Position.X + 3.5f, badge.End.Y - 3), text, fontSize: 12, modulate: new Color("FFF3D6"));
            }
            if (!named) return;
            var lines = new List<string> { string.Empty };
            foreach (var word in Pretty(kind).Split(' '))
            {
                var candidate = lines[^1].Length == 0 ? word : lines[^1] + " " + word;
                if (lines[^1].Length > 0 && font.GetStringSize(candidate, HorizontalAlignment.Left, -1, 12).X > cell && lines.Count < 2)
                    lines.Add(word);
                else lines[^1] = candidate;
            }
            for (var i = 0; i < lines.Count; i++)
            {
                var lineWidth = font.GetStringSize(lines[i], HorizontalAlignment.Left, -1, 12).X;
                slot.DrawString(font, new Vector2(Mathf.Floor((cell - lineWidth) / 2), box + 12 + i * 13), lines[i], fontSize: 12, modulate: p.InkMuted);
            }
        };
        return slot;
    }

    private static Control MockSlots((string Kind, int Count)[] items, int iconSize, bool named, int columns, bool counts = true)
    {
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (var (kind, count) in items) grid.AddChild(MockSlot(kind, counts ? count : null, iconSize, named));
        return grid;
    }

    /// <summary>A short row of small item icons with amounts, like "Uses [stone] 2 [wood] 1".</summary>
    private static Control MockAmounts(string lead, (string Kind, int Count)[] items, string tail)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 3);
        row.AddChild(MockText(lead, "DimLabel"));
        foreach (var (kind, count) in items)
        {
            row.AddChild(new TextureRect { Texture = ItemIcons.Texture(kind, 16), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(MockText(count.ToString(System.Globalization.CultureInfo.InvariantCulture), "DimLabel"));
        }
        row.AddChild(MockText(tail, "DimLabel"));
        return row;
    }

    private static Control MockSectionRow(string title, string detail)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = title.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(MockText(detail, "DimLabel"));
        return row;
    }

    private static Control MockStatus(ImageTexture icon, string text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new TextureRect { Texture = icon, StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        row.AddChild(MockText(text, wrap: true));
        return row;
    }

    private static Control MockMiniFacts(params (string Key, string Value)[] rows)
    {
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 2);
        foreach (var (key, value) in rows)
        {
            grid.AddChild(MockText(key, "DimLabel"));
            var label = MockText(value);
            label.CustomMinimumSize = new Vector2(84, 0);
            grid.AddChild(label);
        }
        return grid;
    }

    private Control MockDetailsButton()
    {
        var button = MockButton("Details", null, expand: true);
        button.Text = "Details  ›";
        return button;
    }

    /// <summary>Quick card: who is there, what is going on, and what is stored, at a glance.</summary>
    private PanelContainer MockBuildingHouse()
    {
        var p = UiTheme.Current;
        var body = MockColumn(7);
        body.AddChild(MockBuildingHeader(MockRoof(BuildingKind.House, 1, 1, new BuildingDoor(DoorSide.South, 0), 36),
            "House", "Ash household · First Town"));
        body.AddChild(MockStatus(PixelIcons.Themed(PixelGlyph.Person, MockAccent, 1), "Mira is inside"));
        body.AddChild(MockSectionRow("Stored", "12 items"));
        body.AddChild(MockSlots([("berries", 6), ("bread", 2), ("wood", 4)], 32, false, 5));
        body.AddChild(MockDetailsButton());
        return MockPanel(body, 250, "HudPanel");
    }

    private PanelContainer MockBuildingBlacksmith()
    {
        var body = MockColumn(7);
        body.AddChild(MockBuildingHeader(MockRoof(BuildingKind.Blacksmith, 1, 2, new BuildingDoor(DoorSide.North, 0), 36),
            "Blacksmith", "Reed household · First Town"));
        var work = new HBoxContainer();
        work.AddThemeConstantOverride("separation", 6);
        work.AddChild(new TextureRect { Texture = ItemIcons.Texture("wooden_axe", 32), StretchMode = TextureRect.StretchModeEnum.KeepCentered });
        var lines = MockColumn(2);
        lines.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lines.AddChild(MockText("Oren is making a stone axe", wrap: true));
        lines.AddChild(MockMeter("", 60, MockDiet, 0, showValue: true, segments: 16));
        lines.AddChild(MockText("About 2 hours left", "DimLabel"));
        work.AddChild(lines);
        body.AddChild(work);
        body.AddChild(MockSectionRow("Stored", "5 items"));
        body.AddChild(MockSlots([("stone", 3), ("wood", 2)], 32, false, 5));
        body.AddChild(MockDetailsButton());
        return MockPanel(body, 250, "HudPanel");
    }

    /// <summary>Details: everything recorded about the building, docked at the side.</summary>
    private PanelContainer MockBuildingWarehouse()
    {
        var body = MockColumn(8);
        body.AddChild(MockBuildingHeader(MockRoof(BuildingKind.Warehouse, 2, 2, new BuildingDoor(DoorSide.North, 0), 48),
            "Warehouse", "First Town's shared store"));
        body.AddChild(MockMiniFacts(("Owner", "First Town"), ("Built", "Spring 1, y1"), ("Used by", "Residents"), ("Door", "North side")));
        body.AddChild(MockSectionRow("Storage", "6 kinds · 38 items"));
        body.AddChild(MockSlots([("wood", 14), ("stone", 6), ("clay", 3), ("fiber", 8), ("wooden_axe", 1), ("wooden_pickaxe", 1)], 32, true, 5));
        body.AddChild(MockSectionRow("People", "Nobody inside"));
        body.AddChild(MockText("Every First Town resident can store and take here.", "DimLabel", wrap: true));
        body.AddChild(MockSectionRow("Recently", ""));
        body.AddChild(MockStatus(ItemIcons.Texture("wood", 16), "Tomas took 2 wood · 3 h ago"));
        body.AddChild(MockStatus(ItemIcons.Texture("stone", 16), "Oren stored 6 stone · yesterday"));
        return MockPanel(body, 290, "HudPanel");
    }

    private PanelContainer MockBuildingSmithDetails()
    {
        var body = MockColumn(8);
        body.AddChild(MockBuildingHeader(MockRoof(BuildingKind.Blacksmith, 1, 2, new BuildingDoor(DoorSide.North, 0), 48),
            "Blacksmith", "Reed household · First Town"));
        body.AddChild(MockMiniFacts(("Owner", "Reed household"), ("Built", "Spring 1, y1"), ("Used by", "The Reeds"), ("Door", "North side")));
        body.AddChild(MockSectionRow("Working", "1 of 1 place"));
        var work = new HBoxContainer();
        work.AddThemeConstantOverride("separation", 6);
        work.AddChild(new TextureRect { Texture = ItemIcons.Texture("wooden_axe", 32), StretchMode = TextureRect.StretchModeEnum.KeepCentered });
        var lines = MockColumn(2);
        lines.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lines.AddChild(MockText("Oren is making a stone axe", wrap: true));
        lines.AddChild(MockMeter("", 60, MockDiet, 0, showValue: true, segments: 20));
        lines.AddChild(MockAmounts("Uses", [("stone", 2), ("wood", 1)], "· 2 h left"));
        work.AddChild(lines);
        body.AddChild(work);
        body.AddChild(MockSectionRow("Makes", ""));
        body.AddChild(MockSlots([("wooden_axe", 1), ("wooden_pickaxe", 1), ("tool", 1)], 32, true, 5, counts: false));
        body.AddChild(MockSectionRow("Storage", "2 kinds · 5 items"));
        body.AddChild(MockSlots([("stone", 3), ("wood", 2)], 32, true, 5));
        return MockPanel(body, 290, "HudPanel");
    }

    private async Task MockFrames(int count)
    {
        for (var f = 0; f < count; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task MockShot(string path)
    {
        selectedInhabitantCard.Hide();
        await MockFrames(6);
        GetViewport().GetTexture().GetImage().SavePng(path);
    }

    private async Task RunMocks(OwnerWorldSnapshot snapshot, string output)
    {
        eventsPanel.Hide();
        hoverReadout.Hide();
        var who = snapshot.Inhabitants.First(person => person.Id == "mira");
        var anchor = selectedInhabitantCard.Position;
        var factor = uiLayer.Factor;
        var marker = inhabitantVisuals["mira"];
        var markerRect = marker.GetGlobalRect();
        var markerUi = new Rect2(markerRect.Position / factor, markerRect.Size / factor);

        async Task Show(Control card, Vector2 position, string name)
        {
            uiLayer.AddChild(card);
            await MockFrames(2);
            var excess = card.GetCombinedMinimumSize().Y - (UiSize.Y - position.Y - 12);
            if (excess > 0 && card.FindChildren("*", nameof(RichTextLabel), true, false).FirstOrDefault() is RichTextLabel thoughts)
                thoughts.CustomMinimumSize = new Vector2(0, Math.Max(36, thoughts.CustomMinimumSize.Y - excess));
            await MockFrames(1);
            card.Size = card.GetCombinedMinimumSize();
            card.Position = position;
            await MockShot($"{output}-{name}.png");
            uiLayer.RemoveChild(card);
            card.QueueFree();
        }

        anchor = new Vector2(Mathf.Floor(markerUi.End.X + 12), HudTop);
        await Show(MockCardA(snapshot, who, 300), anchor, "card-a");
        await Show(MockCardB(snapshot, who, 0), anchor, "card-b0");
        await Show(MockCardB(snapshot, who, 1), anchor, "card-b1");
        await Show(MockCardB(snapshot, who, 3), anchor, "card-b3");
        await Show(MockCardC(snapshot, who), anchor, "card-c");
        await Show(MockGlance(who), new Vector2(markerUi.End.X + 6, markerUi.Position.Y - 30), "card-d1");
        var profile = MockCardA(snapshot, who, 300);
        await Show(profile, new Vector2(14, HudTop), "card-d2");

        // Map nameplates.
        selectedInhabitantCard.Hide();
        foreach (var style in new[] { "now", "parchment", "icons", "outline", "wood", "quiet" })
        {
            var overlays = ApplyNameplates(snapshot, style);
            await MockShot($"{output}-plates-{style}.png");
            foreach (var overlay in overlays) overlay.QueueFree();
        }
    }

    private List<Control> ApplyNameplates(OwnerWorldSnapshot snapshot, string style)
    {
        var overlays = new List<Control>();
        var activities = new Dictionary<string, (string Glyph, Color Accent)>
        {
            ["mira"] = ("basket", new Color("C9A15A")),
            ["rowan"] = ("flame", new Color("F2A95A")),
            ["ash"] = ("hammer", new Color("9C6C42")),
            ["wren"] = ("wheat", new Color("E8C85A")),
            ["pip"] = ("speech", new Color("F6EBCF")),
            ["tamsin"] = ("wheat", new Color("E8C85A")),
        };
        foreach (var (id, marker) in inhabitantVisuals)
        {
            var person = snapshot.Inhabitants.First(item => item.Id == id);
            var name = GameUiText.ActorMapLabel(person.DisplayName);
            if (style == "now")
            {
                marker.Caption = $"{"○"} {name}";
                continue;
            }
            marker.Caption = string.Empty;
            if (style == "quiet" && !marker.Selected) continue;
            var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 12, Position = marker.Position, Size = marker.Size };
            var selected = marker.Selected;
            var activity = activities.GetValueOrDefault(id);
            overlay.Draw += () => DrawMockAgentTag(overlay, name, selected, style, activity.Glyph, activity.Accent);
            marker.GetParent().AddChild(overlay);
            overlays.Add(overlay);
        }
        foreach (var (id, label) in mapObjectVisuals)
        {
            if (!id.StartsWith("building:", StringComparison.Ordinal)) continue;
            label.Visible = style == "now";
            if (style is "now" or "quiet") continue;
            var building = snapshot.PlacedBuildings.First(item => "building:" + item.InstanceId == id);
            var overlay = new Control
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 6,
                Position = label.Position - new Vector2(4, 4),
                Size = label.Size + new Vector2(8, 8),
            };
            overlay.Draw += () => DrawMockBuildingSign(overlay, building.DisplayName ?? "Building", style);
            label.GetParent().AddChild(overlay);
            overlays.Add(overlay);
        }
        return overlays;
    }

    private static void MockOutlined(CanvasItem canvas, Font font, Vector2 position, string text, int size, Color fill, Color edge, int px)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0)
                    canvas.DrawString(font, position + new Vector2(dx * px, dy * px), text, fontSize: size, modulate: edge);
        canvas.DrawString(font, position, text, fontSize: size, modulate: fill);
    }

    private static void DrawMockAgentTag(Control canvas, string text, bool selected, string style, string? glyph, Color accent)
    {
        var p = UiTheme.Current;
        var s = AgentMarker.TextScale;
        var side = Math.Min(canvas.Size.X, canvas.Size.Y);
        var center = canvas.Size / 2;
        var drawn = side * 1.35f;
        var top = Mathf.Floor(center.Y - drawn / 2 + drawn * 0.86f);
        var font = UiFonts.Text;
        var size = UiFonts.Body * s;
        var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var gold = new Color("FFD166");
        if (style == "outline")
        {
            var position = new Vector2(Mathf.Floor(center.X - textSize.X / 2), top + font.GetAscent(size));
            MockOutlined(canvas, font, position, text, size, selected ? gold : new Color("FFF6E0"), new Color("1E1712"), s);
            return;
        }
        var icon = style == "icons" && glyph is not null ? 12 * s + 3 * s : 0;
        var width = Mathf.Floor(textSize.X + icon + 8 * s);
        var height = Mathf.Floor(textSize.Y + 2 * s);
        var rect = new Rect2(Mathf.Floor(center.X - width / 2), top, width, height);
        Color edge, fill, ink;
        if (style == "wood")
        {
            (edge, fill, ink) = (p.WoodEdge, p.Wood, p.OnWood);
        }
        else
        {
            (edge, fill, ink) = (p.WoodEdge, p.Paper, p.Ink);
        }
        if (selected) canvas.DrawRect(rect.Grow(s), gold);
        canvas.DrawRect(rect, edge);
        canvas.DrawRect(rect.Grow(-s), fill);
        if (style == "wood")
        {
            canvas.DrawRect(new Rect2(rect.Position + new Vector2(s, s), new Vector2(rect.Size.X - 2 * s, s)), p.WoodLight);
            canvas.DrawRect(new Rect2(rect.Position.X + s, rect.End.Y - 2 * s, rect.Size.X - 2 * s, s), p.WoodDark);
        }
        var x = rect.Position.X + 4 * s;
        if (icon > 0)
        {
            var texture = MockIcon(glyph!, ink, accent, s);
            canvas.DrawTexture(texture, new Vector2(x - s, rect.Position.Y + Mathf.Floor((height - 12 * s) / 2)));
            x += icon;
        }
        canvas.DrawString(font, new Vector2(x, rect.Position.Y + s + font.GetAscent(size)), text, fontSize: size, modulate: ink);
    }

    private static void DrawMockBuildingSign(Control canvas, string text, string style)
    {
        var p = UiTheme.Current;
        var s = AgentMarker.TextScale;
        var font = UiFonts.Headings;
        var size = UiFonts.Body * s;
        var caps = text.ToUpperInvariant();
        var textSize = font.GetStringSize(caps, HorizontalAlignment.Left, -1, size);
        var bottom = canvas.Size.Y;
        if (style == "outline")
        {
            var position = new Vector2(Mathf.Floor((canvas.Size.X - textSize.X) / 2), bottom + font.GetAscent(size) - 2 * s);
            MockOutlined(canvas, font, position, caps, size, new Color("F6EBCF"), new Color("1E1712"), s);
            return;
        }
        var width = Mathf.Floor(textSize.X + 8 * s);
        var height = Mathf.Floor(font.GetHeight(size) + 3 * s);
        var rect = new Rect2(Mathf.Floor((canvas.Size.X - width) / 2), bottom - 3 * s, width, height);
        var wood = style == "wood";
        canvas.DrawRect(rect, p.WoodEdge);
        canvas.DrawRect(rect.Grow(-s), wood ? p.Wood : p.Paper);
        if (wood)
        {
            canvas.DrawRect(new Rect2(rect.Position + new Vector2(s, s), new Vector2(rect.Size.X - 2 * s, s)), p.WoodLight);
            canvas.DrawRect(new Rect2(rect.Position.X + s, rect.End.Y - 2 * s, rect.Size.X - 2 * s, s), p.WoodDark);
        }
        canvas.DrawString(font, new Vector2(rect.Position.X + 4 * s, rect.Position.Y + 2 * s + font.GetAscent(size)), caps,
            fontSize: size, modulate: wood ? p.OnWood : p.Ink);
    }

    // Local road audit: draws a town dumped from the real layout planner.
    private async Task RealBuildingShots(OwnerWorldSnapshot snapshot, OwnerWorldPlacedBuilding[] buildings, string prefix)
    {
        OwnerWorldPlacedBuilding Pick(string role) => buildings.FirstOrDefault(b => b.InstanceId.Contains(role, StringComparison.Ordinal)) ?? buildings[0];
        var houseA = Pick("house-a");
        var smith = Pick("blacksmith");
        var warehouse = Pick("warehouse");
        OwnerWorldInhabitant Person(string id, string name, OwnerWorldPosition at, string household) =>
            new(id, name, "active", at, 8000, [], [], new OwnerWorldRoute("idle", null, null, [], ""),
                new OwnerWorldSpatialKnowledge(at, [at], [at]), false)
            { Relationships = [new("m:" + id, household, "household_membership", "accepted", "household", 1)] };
        var mira = Person("agent:mira", "Mira", houseA.Position, "household:ash");
        var tomas = Person("agent:tomas", "Tomas", new(houseA.Position.X + 3, houseA.Position.Y + 3), "household:ash");
        var oren = Person("agent:oren", "Oren", smith.Position, "household:reed");
        var enriched = snapshot with
        {
            WorldTick = 400,
            Stockpiles = [new("household:ash", "Ash household", []), new("household:reed", "Reed household", [])],
            Inhabitants = [mira, tomas, oren],
            PlacedBuildings = buildings.Select(b =>
                b == houseA ? b with { DisplayName = "House", TownId = "town:first", HouseholdId = "household:ash", StoredItems = [new("berries", 6), new("bread", 2), new("wood", 4)] } :
                b == smith ? b with { DisplayName = "Blacksmith", TownId = "town:first", HouseholdId = "household:reed", StoredItems = [new("stone", 3), new("wood", 2), new("iron_ore", 4)] } :
                b == warehouse ? b with { DisplayName = "Warehouse", TownId = "town:first", StoredItems = [new("wood", 14), new("stone", 6), new("clay", 3), new("fiber", 8), new("wooden_axe", 1), new("wooden_pickaxe", 1), new("fruit", 5)] } :
                b with { DisplayName = Pretty(b.DisplayName ?? "house"), TownId = "town:first" }).ToArray(),
            ProductionJobs = [new("job-1", "sha256:x/wooden-axe", smith.InstanceId, oren.Id, 380, 420, "running")],
        };
        Render(enriched, []);
        await MockFrames(4);
        foreach (var (building, shot) in new[] { (houseA, "house"), (smith, "smith"), (warehouse, "warehouse") })
        {
            SelectBuilding(building.InstanceId);
            await MockFrames(4);
            GetViewport().GetTexture().GetImage().SavePng($"{prefix}-real-{shot}.png");
            OpenBuildingDetails();
            await MockFrames(4);
            GetViewport().GetTexture().GetImage().SavePng($"{prefix}-real-{shot}-details.png");
            ClearBuildingSelection();
        }
    }

    private async Task RunTownJson(string[] paths, string output)
    {
        foreach (var panel in HudPanels()) panel.Hide();
        eventsPanel.Hide();
        selectedInhabitantId = null;
        var styleArg = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--road-style=", StringComparison.Ordinal));
        RoadMockSprites.Style = styleArg is null ? 0 : int.Parse(styleArg["--road-style=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        // WorldTerrainLayer.MockRoadPiece = RoadMockSprites.Style == 0 ? null : RoadMockSprites.Piece;
        var borderArg = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--border-style=", StringComparison.Ordinal));
        _ = borderArg;
        var serial = 0;
        foreach (var path in paths)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var width = root.GetProperty("width").GetInt32();
            var height = root.GetProperty("height").GetInt32();
            var terrain = root.GetProperty("terrain").EnumerateArray().Select(item => (byte)item.GetInt32()).ToArray();
            var resources = root.GetProperty("resources").EnumerateArray()
                .Where(item => item.GetProperty("tree").ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(item => new OwnerWorldResource("t" + serial++, "wood",
                    new OwnerWorldPosition(item.GetProperty("x").GetInt32(), item.GetProperty("y").GetInt32()), true, "available", 10,
                    TreeKind: item.GetProperty("tree").GetString()))
                .ToArray();
            var buildings = root.GetProperty("buildings").EnumerateArray().Select(item =>
            {
                var def = item.GetProperty("def").GetString()!;
                var tag = def.Contains("warehouse") ? "warehouse" : def.Contains("farmhouse") ? "farmhouse" :
                    def.Contains("blacksmith") ? "blacksmith" : "house";
                var id = item.GetProperty("id").GetString()!;
                var entrance = item.TryGetProperty("ex", out var ex) && ex.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? new OwnerWorldPosition(ex.GetInt32(), item.GetProperty("ey").GetInt32()) : null;
                return new OwnerWorldPlacedBuilding(id, def, new OwnerWorldPosition(item.GetProperty("x").GetInt32(), item.GetProperty("y").GetInt32()),
                    0, tag, [tag], item.GetProperty("w").GetInt32(), item.GetProperty("h").GetInt32(), Entrance: entrance);
            }).ToArray();
            var roads = root.GetProperty("roads").EnumerateArray()
                .Select(item => new OwnerWorldPosition(item.GetProperty("x").GetInt32(), item.GetProperty("y").GetInt32())).ToArray();
            var name = Path.GetFileNameWithoutExtension(path);
            var border = root.TryGetProperty("border", out var borderJson)
                ? borderJson.EnumerateArray().Select(item => new OwnerWorldPosition(item.GetProperty("x").GetInt32(), item.GetProperty("y").GetInt32())).ToArray()
                : [];
            var snapshot = new OwnerWorldSnapshot("road-audit-" + name, 400, "road-audit-map-" + name, [], [], resources, null, 12)
            {
                Towns = border.Length == 0 ? [] : [new OwnerWorldTown("town:first", "First Town", "founded", 0, [], [], border)],
                PackedTerrain = new OwnerWorldPackedTerrain(width, height, "terrain-kind-v1", Convert.ToBase64String(terrain)),
                RoadTiles = roads,
                PlacedBuildings = buildings,
                CalendarPace = new OwnerWorldCalendarPace(360, 40),
            };
            townBorderFilter.ButtonPressed = border.Length > 0;
            Render(snapshot, []);
            var points = buildings.Select(item => new Vector2(item.Position.X + item.Width / 2f, item.Position.Y + item.Height / 2f))
                .Concat(roads.Select(item => new Vector2(item.X + 0.5f, item.Y + 0.5f))).ToArray();
            cameraZoom = float.Parse(OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--town-zoom=", StringComparison.Ordinal))?["--town-zoom=".Length..] ?? "5", System.Globalization.CultureInfo.InvariantCulture);
            cameraCenterTiles = new Vector2(points.Average(item => item.X), points.Average(item => item.Y));
            Render(snapshot, []);
            statusToast.Hide();
            Input.WarpMouse(new Vector2(4, GetViewport().GetVisibleRect().Size.Y - 4));
            await MockFrames(10);
            RefreshTileHoverAtMouse();
            await MockFrames(2);
            GetViewport().GetTexture().GetImage().SavePng($"{output}-{name}.png");
            if (OS.GetCmdlineUserArgs().Contains("--building-real", StringComparer.Ordinal))
                await RealBuildingShots(snapshot, buildings, $"{output}-{name}");
            if (!OS.GetCmdlineUserArgs().Contains("--building-mock", StringComparer.Ordinal)) continue;
            Rect2 Screen(string role)
            {
                var building = buildings.FirstOrDefault(item => item.InstanceId.Contains(role, StringComparison.Ordinal)) ?? buildings[0];
                var rect = mapObjectVisuals["building:" + building.InstanceId].GetGlobalRect();
                return new Rect2(rect.Position / uiLayer.Factor, rect.Size / uiLayer.Factor);
            }
            async Task Card(Control card, Vector2 position, string shot, Rect2? highlight)
            {
                uiLayer.AddChild(card);
                Control? ring = null;
                if (highlight is { } box)
                {
                    ring = new Panel { Position = box.Position - new Vector2(3, 3), Size = box.Size + new Vector2(6, 6), ZIndex = 60, MouseFilter = Control.MouseFilterEnum.Ignore };
                    ring.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = new Color("FFF1C9"), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 });
                    uiLayer.AddChild(ring);
                }
                await MockFrames(2);
                card.Size = card.GetCombinedMinimumSize();
                card.Position = new Vector2(Math.Max(12, Math.Min(position.X, UiSize.X - card.Size.X - 12)), Math.Max(HudTop, Math.Min(position.Y, UiSize.Y - card.Size.Y - 12)));
                await MockFrames(3);
                GetViewport().GetTexture().GetImage().SavePng($"{output}-{name}-{shot}.png");
                uiLayer.RemoveChild(card);
                card.QueueFree();
                ring?.QueueFree();
            }
            var house = Screen("house-a");
            await Card(MockBuildingHouse(), new Vector2(house.End.X + 12, house.Position.Y - 40), "house", house);
            var smith = Screen("blacksmith");
            await Card(MockBuildingBlacksmith(), new Vector2(smith.End.X + 12, smith.Position.Y - 40), "smith", smith);
            var warehouse = Screen("warehouse");
            await Card(MockBuildingWarehouse(), new Vector2(14, HudTop), "warehouse", warehouse);
            await Card(MockBuildingSmithDetails(), new Vector2(14, HudTop), "smith-details", smith);
        }
    }

    private async Task RunAudit(OwnerWorldSnapshot snapshot, string output)
    {
        eventsPanel.Hide();
        async Task Shot(string name, Action open, Action close)
        {
            foreach (var panel in HudPanels()) panel.Hide();
            open();
            await MockFrames(8);
            GetViewport().GetTexture().GetImage().SavePng($"{output}-audit-{name}.png");
            close();
            await MockFrames(2);
        }
        if (OS.GetCmdlineUserArgs().Contains("--models-only"))
        {
            OwnerProviderModelChoice[] offered = [new("gpt-6.1-sol", true), new("gpt-6-astra", false), new("gpt-6-sol", true), new("gpt-6-luna", true)];
            await Shot("models-list", () => { founderSetupPanel.Show(); founderModelPicker.SetModel("gpt-6-luna"); founderModelPicker.BeginLoading("gpt-6-luna"); founderModelPicker.ShowList(offered, "gpt-6-luna"); }, () => { });
            await Shot("models-open", () => { founderSetupPanel.Show(); founderModelPicker.Choice.ShowPopup(); }, () => founderModelPicker.Choice.GetPopup().Hide());
            await Shot("models-loading", () => { founderSetupPanel.Show(); founderModelPicker.SetModel("gpt-6-luna"); founderModelPicker.BeginLoading("gpt-6-luna"); }, () => { });
            await Shot("models-error", () => { founderSetupPanel.Show(); founderModelPicker.ShowError("OpenAI refused this key.", "gpt-6-luna"); }, () => { });
            await Shot("models-typed", () => { founderSetupPanel.Show(); founderModelPicker.ShowList(offered, "gpt-6-luna"); var i = founderModelPicker.Choice.ItemCount - 1; founderModelPicker.Choice.Select(i); founderModelPicker.Choice.EmitSignal(OptionButton.SignalName.ItemSelected, i); founderModelPicker.TypedInput.Text = "my-fine-tune"; }, () => { });
            await Shot("models-notoffered", () => { founderSetupPanel.Show(); founderModelPicker.SetModel("gpt-3.5-turbo"); founderModelPicker.ShowList(offered, "gpt-6-luna"); }, () => { });
            return;
        }
        await Shot("card", () => { selectedInhabitantId = "mira"; Render(snapshot, []); }, () => { });
        await Shot("profile", () => { agentProfileRequested = true; Render(snapshot, []); }, () => { });
        await Shot("thoughts", () => { agentProfileRequested = true; Render(snapshot, []); OpenThoughtsReader(); }, () => thoughtsPanel.Hide());
        await Shot("profile-hover", () => { agentProfileRequested = true; Render(snapshot, []); thoughtsInset.ThemeTypeVariation = "InsetPanelHover"; }, () => thoughtsInset.ThemeTypeVariation = "InsetPanel");
        await Shot("profile-rename", () => { agentProfileRequested = true; Render(snapshot, []); ToggleRenameRow(); }, () => renameRow.Hide());
        agentProfileRequested = false;
        var historical = snapshot with
        {
            Inhabitants = snapshot.Inhabitants.Select(person => person.Id != "ash" ? person : person with
            {
                Lifecycle = "dead",
                DecisionFactors = [.. person.DecisionFactors, new("death-tick", "700"), new("death-cause", "cold_exposure"), new("will-status", "default")],
                RecentPrivateThoughts = [new OwnerWorldPrivateThought(650, "The wind is getting colder. I should have finished the roof first.")],
            }).ToArray(),
        };
        await Shot("profile-dead", () => { selectedInhabitantId = "ash"; Render(historical, []); }, () => { });
        selectedInhabitantId = null;
        Render(snapshot, []);
        selectedInhabitantId = null;
        Render(snapshot, []);
        await Shot("roster", ToggleInhabitants, () =>
        {
            GD.Print($"AUDIT roster size={rosterPanel.Size} min={rosterPanel.GetCombinedMinimumSize()} list={inhabitantList.GetCombinedMinimumSize()} listSize={inhabitantList.Size} summary={rosterSummaryLabel.GetCombinedMinimumSize()}");
            foreach (var child in rosterPanel.FindChildren("*", nameof(Control), true, false).OfType<Control>())
                GD.Print($"AUDIT  {child.GetType().Name} {child.Name} min={child.GetCombinedMinimumSize()} size={child.Size} vis={child.Visible}");
            rosterPanel.Hide();
        });
        await Shot("filters", ToggleMapFilters, () => filtersPanel.Hide());
        await Shot("events", ToggleEvents, () => eventsPanel.Hide());
        await Shot("worldinfo-world", () => { ToggleWorldInfo(); ShowWorldInfoPage(false); }, () => worldInfoPanel.Hide());
        await Shot("overview", () => mapButton.EmitSignal(BaseButton.SignalName.Pressed), () => worldOverviewPanel.Hide());
        await Shot("controls", ToggleControlsPanel, () => controlsPanel.Hide());
        selectedInhabitantId = "mira";
        Render(snapshot, []);
        await Shot("memories", OpenMemories, () => memoriesPanel.Hide());
        await Shot("family", OpenFamilyTree, () => familyTreePanel.Hide());
        selectedInhabitantId = null;
        Render(snapshot, []);
        await Shot("mods", ShowModLibrary, () => { });
        await Shot("founders", () => founderSetupPanel.Show(), () => founderSetupPanel.Hide());
        await Shot("saves", () => { manualSaveHeading.Text = "Save World"; manualSaveOverlay.Show(); }, () => manualSaveOverlay.Hide());
        await Shot("loadworld", () => { worldMenuHeading.Text = "Load World"; worldMenuOverlay.Show(); }, () => worldMenuOverlay.Hide());
        deletionConfirmation.DialogText = "Permanently delete ‘Riverbend Valley’ and all of its manual saves and autosaves? Your other worlds and account settings stay unchanged. There is no undo.";
        deletionConfirmation.DialogText = "Permanently delete ‘Riverbend Valley’ and all of its manual saves and autosaves? There is no undo.";
        await Shot("delete", () => deletionConfirmation.PopupCentered(new Vector2I(1080, 400)), () => deletionConfirmation.Hide());
        await Shot("quitmenu", () => menuQuitToMainButton.EmitSignal(BaseButton.SignalName.Pressed), () => quitToMenuConfirmation.Hide());
    }
}

/// <summary>MOCKUP ONLY: packed-dirt road pieces joined along the road, diagonals included.</summary>
internal static class RoadMockSprites
{
    public static int Style { get; set; }
    private static readonly Dictionary<(int, int, int), ImageTexture> Cache = [];

    public static Texture2D? Piece(int mask, int x, int y)
    {
        var variant = (int)(PixelArt.Hash(x, y, 7) % 4);
        var key = (Style, mask, variant);
        if (!Cache.TryGetValue(key, out var texture))
        {
            texture = ImageTexture.CreateFromImage(Render(mask, variant));
            Cache[key] = texture;
        }
        return texture;
    }

    private const int S = 32;

    private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
    {
        var dx = bx - ax; var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0f : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0f, 1f);
        var qx = ax + t * dx - px; var qy = ay + t * dy - py;
        return MathF.Sqrt(qx * qx + qy * qy);
    }

    private static Image Render(int mask, int variant)
    {
        var image = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        bool n = (mask & 1) != 0, e = (mask & 2) != 0, s = (mask & 4) != 0, w = (mask & 8) != 0;
        bool ne = (mask & 32) != 0, se = (mask & 64) != 0, sw = (mask & 128) != 0, nw = (mask & 256) != 0;
        var self = (mask & 1024) != 0;
        var door = (mask & 16) != 0;
        const float r = 8f;
        var segments = new List<(float, float, float, float, float)>();
        if (self)
        {
            segments.Add((16, 16, 16, 16, r));
            if (n) segments.Add((16, 16, 16, -16, r));
            if (e) segments.Add((16, 16, 48, 16, r));
            if (s) segments.Add((16, 16, 16, 48, r));
            if (w) segments.Add((16, 16, -16, 16, r));
            // A diagonal step joins directly only where no straight path does.
            if (ne && !n && !e) segments.Add((16, 16, 48, -16, r));
            if (se && !s && !e) segments.Add((16, 16, 48, 48, r));
            if (sw && !s && !w) segments.Add((16, 16, -16, 48, r));
            if (nw && !n && !w) segments.Add((16, 16, -16, -16, r));
            if (door) segments.Add((16, 16, 16, -6, 3.5f));
        }
        else
        {
            // The corner of a diagonal road passing next to this tile.
            if (n && e && !ne) segments.Add((16, -16, 48, 16, r));
            if (e && s && !se) segments.Add((48, 16, 16, 48, r));
            if (s && w && !sw) segments.Add((16, 48, -16, 16, r));
            if (w && n && !nw) segments.Add((-16, 16, 16, -16, r));
        }
        bool Plaza(int px, int py) => self && (
            px >= 16 && py < 16 && n && e && ne || px >= 16 && py >= 16 && s && e && se ||
            px < 16 && py >= 16 && s && w && sw || px < 16 && py < 16 && n && w && nw);
        for (var py = 0; py < S; py++)
            for (var px = 0; px < S; px++)
            {
                var inside = float.MaxValue;
                foreach (var (ax, ay, bx, by, radius) in segments)
                    inside = Math.Min(inside, SegmentDistance(px + 0.5f, py + 0.5f, ax, ay, bx, by) - radius);
                if (Plaza(px, py)) inside = Math.Min(inside, -4f);
                if (inside > 0) continue;
                var h = PixelArt.Hash(px + variant * 97 + (self ? 0 : 31), py, 3) % 100;
                var edge = inside > -1.4f;
                if (edge && h < 30) continue;
                var c = new Color("B99A6B");
                if (edge) c = new Color("977852");
                else if (h < 6) c = new Color("D1B98D");
                else if (h < 10) c = new Color("8C7050");
                else if (h < 40) c = new Color("B39365");
                image.SetPixel(px, py, c);
            }
        return image;
    }
}
