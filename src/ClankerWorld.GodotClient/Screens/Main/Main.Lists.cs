using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The Agents list as portrait cards, the Event Log as rows with an icon per
/// kind of event, and the controls list as keycaps.
/// </summary>
public partial class Main
{
    private readonly SlotList rosterCards = new();
    private readonly List<string> rosterCardIds = [];
    private readonly VBoxContainer eventRows = new();
    private readonly ScrollContainer eventScroll = new();

    /// <summary>A small framed portrait: the agent's own map sprite on green.</summary>
    private static ImageTexture RosterPortrait(OwnerWorldInhabitant person, bool living)
    {
        var age = person.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail;
        var sprite = AgentSprites.Sprite(AgentSprites.VariantFor(person.Id), AgentSprites.StageIndex(age), 16);
        var dark = UiTheme.Current.Name == "dark";
        var image = Image.CreateEmpty(20, 20, false, Image.Format.Rgba8);
        image.Fill(UiTheme.Current.WoodEdge);
        image.FillRect(new Rect2I(1, 1, 18, 18), living ? dark ? new Color("3E5A2E") : new Color("8FB06A") : UiTheme.Current.InkFaint);
        sprite.Convert(Image.Format.Rgba8);
        image.BlendRect(sprite, new Rect2I(0, 0, 16, 16), new Vector2I(2, 2));
        return ImageTexture.CreateFromImage(image);
    }

    private void BuildRosterCards(Control body)
    {
        rosterCards.Compact = true;
        rosterCards.CustomMinimumSize = new Vector2(380, 60);
        rosterCards.ItemSelected += index =>
        {
            if (index < 0 || index >= rosterCardIds.Count) return;
            for (var row = 0; row < inhabitantList.ItemCount; row++)
                if (inhabitantList.GetItemMetadata(row).AsString() == rosterCardIds[(int)index])
                {
                    SelectInhabitantFromList(row);
                    return;
                }
        };
        rosterCards.ItemActivated += _ => OpenAgentProfile(speak: false);
        body.AddChild(rosterCards);
        inhabitantList.Hide();
    }

    private void RenderRosterCards(OwnerWorldInhabitant[] inhabitants)
    {
        rosterCards.Clear();
        rosterCardIds.Clear();
        foreach (var person in inhabitants)
        {
            var living = IsLiving(person);
            List<SlotTag> tags = [];
            string detail;
            if (!living) detail = "Died";
            else
            {
                detail = person.DecisionFactors.Any(factor => factor.Key == "decision-pending")
                    ? "Deciding what to do"
                    : Capitalize(GameUiText.ActivityPhrase(person.PublicIntention?.CandidateId, person.PublicIntention?.Summary));
                if (GameUiText.FullnessState(person.HungerBasisPoints) is "hungry" or "very hungry") tags.Add(new SlotTag("Hungry", Note: true));
                if (person.Survival is { } survival)
                {
                    if (survival.WarmthBasisPoints < 4_000) tags.Add(new SlotTag("Cold", Note: true));
                    if (survival.IllnessBasisPoints >= 1_500) tags.Add(new SlotTag("Ill", Note: true));
                }
            }
            rosterCards.AddItem(person.DisplayName, detail, RosterPortrait(person, living), tags, muted: !living);
            rosterCardIds.Add(person.Id);
            if (person.Id == selectedInhabitantId) rosterCards.Select(rosterCardIds.Count - 1);
        }
        var rows = Math.Max(1, inhabitants.Length);
        rosterCards.CustomMinimumSize = new Vector2(380, Math.Min(rows * 50, 460));
        inhabitantList.Hide();
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>An icon for each kind of event, in the colors its subject uses elsewhere.</summary>
    private static ImageTexture EventIcon(OwnerWorldEvent worldEvent)
    {
        var dark = UiTheme.Current.Name == "dark";
        var ink = UiTheme.Current.Ink;
        var food = dark ? new Color("E8B04A") : new Color("B77C10");
        var wood = dark ? new Color("C99A62") : new Color("9C6C42");
        var green = dark ? new Color("8DBA6A") : new Color("4A7033");
        var pink = UiTheme.Current.Partner;
        var stone = dark ? new Color("A89C8C") : new Color("84715A");
        return worldEvent.Kind switch
        {
            "weather_changed" => PixelIcons.Weather(worldEvent.Detail.Split(':').LastOrDefault() ?? "clear", 1),
            "food_harvested" or "food_consumed" => PixelIcons.Texture(PixelGlyph.Basket, ink, food, 1),
            "build_started" or "build_completed" or "building_placed" or "recipe_started" or "recipe_completed" or "bridge_built"
                => PixelIcons.Texture(PixelGlyph.Hammer, ink, wood, 1),
            "tree_planted" or "tree_replanted" or "crop_moisture_effect" => PixelIcons.Texture(PixelGlyph.Leaf, ink, green, 1),
            "child_born" or "partnership_accepted" or "partnership_ended" or "caregiver_assigned"
                => PixelIcons.Texture(PixelGlyph.Heart, pink, pink, 1),
            "inhabitant_removed" or "estate_will_accepted" or "estate_will_default" => PixelIcons.Texture(PixelGlyph.Grave, ink, stone, 1),
            "town_founded" or "settlement_founded" or "town_founding_started" or "town_border_expanded"
                => PixelIcons.Texture(PixelGlyph.Flag, ink, green, 1),
            "town_resident_joined" or "town_resident_left" or "town_membership_evaluated" or "town_building_assigned"
                => PixelIcons.Texture(PixelGlyph.House, ink, wood, 1),
            "paused" => PixelIcons.Texture(PixelGlyph.Pause, ink, ink, 1),
            "resumed" => PixelIcons.Texture(PixelGlyph.Play, ink, ink, 1),
            "instruction_not_understood" or "inhabitant_building_proposed" => PixelIcons.Texture(PixelGlyph.Speech, ink, UiTheme.Current.Paper, 1),
            "settlement_trade_completed" => PixelIcons.Texture(PixelGlyph.Box, ink, wood, 1),
            _ => PixelIcons.Texture(PixelGlyph.Globe, ink, green, 1),
        };
    }

    private void BuildEventRows(Control body)
    {
        eventScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        eventScroll.CustomMinimumSize = new Vector2(400, 120);
        eventRows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        eventRows.AddThemeConstantOverride("separation", 2);
        eventScroll.AddChild(eventRows);
        body.AddChild(eventScroll);
    }

    /// <summary>Newest first under a heading for each day; a located event gets a button that finds it on the map.</summary>
    private void RenderEventRows((long EventId, bool Located, string Clock, string Text)[] entries)
    {
        foreach (var child in eventRows.GetChildren())
        {
            eventRows.RemoveChild(child);
            child.QueueFree();
        }
        if (entries.Length == 0)
        {
            eventRows.AddChild(new Label { Text = "Nothing notable has happened yet.", ThemeTypeVariation = "DimLabel" });
            eventScroll.CustomMinimumSize = new Vector2(400, 30);
            return;
        }
        string? day = null;
        var height = 0f;
        foreach (var entry in entries)
        {
            var (date, time) = SplitClock(entry.Clock);
            if (date != day)
            {
                var band = new HBoxContainer();
                band.AddThemeConstantOverride("separation", 8);
                if (day is not null) eventRows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
                band.AddChild(new Label { Text = date.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel" });
                band.AddChild(new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                eventRows.AddChild(band);
                height += day is null ? 18 : 24;
                day = date;
            }
            var worldEvent = knownEvents[entry.EventId];
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 24) };
            row.AddThemeConstantOverride("separation", 8);
            var fresh = entry.EventId > newEventsAfter;
            var dot = new ColorRect
            {
                Color = fresh ? UiTheme.Current.Ember : Colors.Transparent,
                CustomMinimumSize = new Vector2(4, 4),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = fresh ? "New since you last looked" : string.Empty,
            };
            row.AddChild(dot);
            row.AddChild(new TextureRect { Texture = EventIcon(worldEvent), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(new Label { Text = time, ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(new Label
            {
                Text = entry.Text,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(120, 0),
            });
            if (entry.Located)
            {
                var find = new Button
                {
                    TooltipText = "Show where this happened",
                    FocusMode = Control.FocusModeEnum.None,
                    Flat = true,
                    Icon = PixelIcons.Themed(PixelGlyph.Find, UiTheme.Current.Primary, 1),
                    MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                };
                foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
                    find.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2 });
                find.AddThemeColorOverride("icon_hover_color", UiTheme.Current.Link);
                var id = entry.EventId.ToString(CultureInfo.InvariantCulture);
                find.Pressed += () => JumpToEvent(id);
                find.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.AddChild(find);
            }
            eventRows.AddChild(row);
            height += 26;
        }
        eventScroll.CustomMinimumSize = new Vector2(400, Math.Min(height, 560));
    }

    private static readonly (string Section, bool Right, (string[] Keys, string Action)[] Rows)[] ControlGroups =
    [
        ("Map", false, [
            (["W", "A", "S", "D"], "Move the map (or the arrows)"),
            (["+", "−"], "Zoom in or out"),
            (["H"], "Back to the first Town"),
            (["M"], "World Map"),
        ]),
        ("Time and agents", false, [
            (["Space"], "Pause or resume (or P)"),
            (["N"], "Next agent"),
            (["Shift", "N"], "Previous agent"),
            (["C"], "Center on the selected agent"),
        ]),
        ("Mouse", true, [
            (["Click"], "Select an agent or inspect a tile"),
            (["Wheel"], "Zoom toward the pointer"),
            (["Middle-drag"], "Move the map"),
        ]),
        ("Panels", true, [
            (["F"], "Map filters"),
            (["I"], "World Info"),
            (["T"], "Towns"),
            (["R"], "Agents"),
            (["E"], "Event Log"),
            (["F1"], "This list (or ?)"),
            (["Esc"], "Close a panel, or open the Pause Menu"),
        ]),
    ];

    /// <summary>A key drawn as a small raised keycap, or a mouse action with a mouse icon.</summary>
    private static Control Keycap(string key)
    {
        if (key is "Click" or "Wheel" or "Middle-drag")
        {
            var mouse = new HBoxContainer();
            mouse.AddThemeConstantOverride("separation", 4);
            mouse.AddChild(new TextureRect
            {
                Texture = PixelIcons.Texture(PixelGlyph.Mouse, UiTheme.Current.Ink, UiTheme.Current.Primary, 1),
                StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            mouse.AddChild(new Label { Text = key });
            return mouse;
        }
        var p = UiTheme.Current;
        var cap = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        cap.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = p.ButtonLight,
            BorderColor = p.ButtonEdge,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 3,
            ContentMarginLeft = 5,
            ContentMarginRight = 5,
            ContentMarginTop = 1,
            ContentMarginBottom = 1,
        });
        cap.AddChild(new Label { Text = key, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(10, 0) });
        return cap;
    }

    private void BuildControlsGroups(Control content)
    {
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        VBoxContainer Column()
        {
            var column = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            column.AddThemeConstantOverride("separation", 4);
            columns.AddChild(column);
            return column;
        }
        var left = Column();
        var right = Column();
        foreach (var (section, onRight, rows) in ControlGroups)
        {
            var column = onRight ? right : left;
            if (column.GetChildCount() > 0) column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
            column.AddChild(new Label { Text = section.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel" });
            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 4);
            foreach (var (keys, action) in rows)
            {
                var caps = new HBoxContainer();
                caps.AddThemeConstantOverride("separation", 3);
                for (var index = 0; index < keys.Length; index++)
                {
                    if (index > 0 && keys[index - 1] == "Shift")
                        caps.AddChild(new Label { Text = "+", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                    caps.AddChild(Keycap(keys[index]));
                }
                grid.AddChild(caps);
                grid.AddChild(new Label { Text = action, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            }
            column.AddChild(grid);
        }
        AddClosablePanelContents(controlsPanel, "Controls", columns);
        controlsPanel.ZIndex = 85;
        controlsPanel.Resized += PositionControlsPanel;
        controlsPanel.Hide();
        content.AddChild(controlsPanel);
    }
}
