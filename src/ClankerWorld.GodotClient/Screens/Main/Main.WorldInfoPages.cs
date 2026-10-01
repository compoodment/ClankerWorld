using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

/// <summary>
/// World Info drawn as pictures rather than lines of text: the World page as
/// a "today" card and a grid of counts with icons, and the Towns page with
/// residents' portraits, each household's stores as item slots, and projects
/// with progress bars.
/// </summary>
public partial class Main
{
    private readonly VBoxContainer worldStatsPage = new();
    private readonly VBoxContainer townExtras = new();
    private readonly ScrollContainer townExtrasScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly MarginContainer townExtrasGap = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private string? renderedWorldStats;
    private string? renderedTownExtras;

    private void BuildWorldInfoPages(VBoxContainer body)
    {
        worldStatsPage.AddThemeConstantOverride("separation", 8);
        body.AddChild(worldStatsPage);
        townExtras.AddThemeConstantOverride("separation", 6);
        townExtras.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        townExtras.MinimumSizeChanged += FitTownExtras;
        townExtrasGap.AddChild(townExtras);
        townExtrasScroll.AddChild(townExtrasGap);
        townsPage.AddChild(townExtrasScroll);
        worldDetails.Hide();
        worldInfoText.Hide();
    }

    private static Color AccentWood => UiTheme.Current.Name == "dark" ? new Color("C99A62") : new Color("9C6C42");
    private static Color AccentGreen => UiTheme.Current.Name == "dark" ? new Color("8DBA6A") : new Color("4A7033");

    /// <summary>One count with its icon: the number large, what it counts underneath.</summary>
    private static PanelContainer StatTile(Texture2D icon, string value, string label)
    {
        var tile = new PanelContainer { ThemeTypeVariation = "InsetRow", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = $"{value} {label}" };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        top.AddChild(new TextureRect { Texture = icon, StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        top.AddChild(new Label { Text = value, ThemeTypeVariation = "HeadingLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        column.AddChild(top);
        column.AddChild(new Label { Text = label, ThemeTypeVariation = "DimLabel" });
        tile.AddChild(column);
        return tile;
    }

    private void RenderWorldStats(OwnerWorldSnapshot snapshot)
    {
        var (width, height) = MapDimensions(snapshot);
        var moisture = WeatherRegionAtCamera(snapshot)?.SoilMoisture;
        var signature = string.Join("|", DisplayWorldClock(snapshot.WorldTick), snapshot.Authoring?.Season, WeatherAtCamera(snapshot),
            LivingPopulation(snapshot), snapshot.Towns.Count, snapshot.PlacedBuildings.Count, snapshot.RoadTiles.Count,
            snapshot.Bridges.Count, snapshot.Resources.Count, width, height, snapshot.CalendarPace?.DaysPerYear, moisture, UiTheme.Current.Name);
        if (signature == renderedWorldStats) return;
        renderedWorldStats = signature;
        foreach (var child in worldStatsPage.GetChildren())
        {
            worldStatsPage.RemoveChild(child);
            child.QueueFree();
        }

        var today = new PanelContainer { ThemeTypeVariation = "InsetPanel" };
        var todayRow = new HBoxContainer();
        todayRow.AddThemeConstantOverride("separation", 10);
        if (snapshot.Authoring is { } authoring)
        {
            todayRow.AddChild(new TextureRect { Texture = PixelIcons.Season(authoring.Season, 2), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        }
        var todayText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        todayText.AddThemeConstantOverride("separation", 0);
        todayText.AddChild(new Label { Text = DisplayWorldClock(snapshot.WorldTick), ThemeTypeVariation = "HeadingLabel" });
        var conditions = snapshot.Authoring is { } season
            ? $"{Pretty(season.Season)} · {Pretty(WeatherAtCamera(snapshot))} here"
            : "Season not reported";
        if (moisture is { } wet) conditions += $" · soil {wet}% damp";
        if (snapshot.CalendarPace is { } year) conditions += $" · {year.DaysPerYear}-day years";
        todayText.AddChild(new Label { Text = conditions, ThemeTypeVariation = "DimLabel" });
        todayRow.AddChild(todayText);
        if (snapshot.Authoring is not null)
            todayRow.AddChild(new TextureRect { Texture = PixelIcons.Weather(WeatherAtCamera(snapshot), 2), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        today.AddChild(todayRow);
        worldStatsPage.AddChild(today);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        var ink = UiTheme.Current.Ink;
        string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Person, ink, AccentWood, 1), Count(LivingPopulation(snapshot)), "living agents"));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Flag, ink, AccentGreen, 1), Count(snapshot.Towns.Count), snapshot.Towns.Count == 1 ? "Town" : "Towns"));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Heart, UiTheme.Current.Partner, UiTheme.Current.Partner, 1), Count(snapshot.Stockpiles.Count), snapshot.Stockpiles.Count == 1 ? "household" : "households"));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.House, ink, AccentWood, 1), Count(snapshot.PlacedBuildings.Count), "buildings"));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Road, ink, AccentWood, 1), Count(snapshot.RoadTiles.Count), "road tiles"));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Bridge, ink, new Color("4786AB"), 1), Count(snapshot.Bridges.Count), snapshot.Bridges.Count == 1 ? "bridge" : "bridges"));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Leaf, ink, AccentGreen, 1), Count(snapshot.Resources.Count), "resource sites"));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Globe, ink, AccentGreen, 1), $"{width} × {height}", "map tiles"));
        worldStatsPage.AddChild(grid);

        var hint = new HBoxContainer();
        hint.AddThemeConstantOverride("separation", 6);
        hint.AddChild(Keycap("F1"));
        hint.AddChild(new Label { Text = "All keyboard and mouse controls", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        worldStatsPage.AddChild(hint);
    }

    private void RenderTownExtras(OwnerWorldSnapshot snapshot)
    {
        var signature = string.Join("\n", snapshot.Stockpiles.Select(item => item.Name + ":" + string.Join(",", item.Items.Select(entry => entry.Kind + entry.Quantity)))) +
            "|" + string.Join(",", snapshot.Inhabitants.Where(person => person.Project is not null).Select(person =>
                $"{person.DisplayName}:{person.Project!.Label}:{person.Project.WorkDone}/{person.Project.WorkRequired}:{person.Project.Blocker}")) +
            "|" + string.Join(",", snapshot.Inhabitants.SelectMany(person => person.SocialNotes.Take(2))) + "|" + UiTheme.Current.Name;
        if (signature == renderedTownExtras) return;
        renderedTownExtras = signature;
        foreach (var child in townExtras.GetChildren())
        {
            townExtras.RemoveChild(child);
            child.QueueFree();
        }
        void Section(string title, string? note = null)
        {
            var band = new HBoxContainer();
            band.AddThemeConstantOverride("separation", 8);
            band.AddChild(new Label { Text = title.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel" });
            band.AddChild(new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            if (note is not null) band.AddChild(new Label { Text = note, ThemeTypeVariation = "DimLabel" });
            townExtras.AddChild(band);
        }

        Section("Household stores");
        if (snapshot.Stockpiles.Count == 0)
            townExtras.AddChild(new Label { Text = "No household stores yet.", ThemeTypeVariation = "DimLabel" });
        foreach (var stockpile in snapshot.Stockpiles)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label { Text = stockpile.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var total = stockpile.Items.Sum(item => item.Quantity);
            row.AddChild(new Label { Text = total == 0 ? "Nothing stored" : $"{total} items", ThemeTypeVariation = "DimLabel" });
            townExtras.AddChild(row);
            if (stockpile.Items.Count == 0) continue;
            var slots = new HFlowContainer();
            slots.AddThemeConstantOverride("h_separation", 4);
            slots.AddThemeConstantOverride("v_separation", 4);
            foreach (var item in stockpile.Items)
            {
                var slot = new ItemSlot { TooltipText = $"{Pretty(item.Kind)} × {item.Quantity}" };
                slot.SetItem(item.Kind, item.Quantity, Pretty(item.Kind));
                slots.AddChild(slot);
            }
            townExtras.AddChild(slots);
        }

        var workers = snapshot.Inhabitants.Where(person => person.Project is not null).ToArray();
        Section("Projects", workers.Length == 0 ? "none right now" : null);
        foreach (var person in workers)
        {
            var project = person.Project!;
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(new TextureRect { Texture = RosterPortrait(person, true), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(new Label { Text = $"{person.DisplayName} · {project.Label}" });
            var done = project.WorkRequired <= 0 ? 0 : Math.Clamp(project.WorkDone * 100 / project.WorkRequired, 0, 100);
            var meter = new PixelMeter { Kind = MeterKind.Diet, CaptionWidth = 0 };
            meter.Percent = done;
            var meterRow = new HBoxContainer();
            meterRow.AddThemeConstantOverride("separation", 6);
            meterRow.AddChild(meter);
            meterRow.AddChild(new Label { Text = $"{done}% · {Pretty(project.Stage).ToLowerInvariant()}", ThemeTypeVariation = "DimLabel" });
            text.AddChild(meterRow);
            if (project.Blocker is { } blocker)
                text.AddChild(new Label { Text = blocker, ThemeTypeVariation = "BadLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            line.AddChild(text);
            townExtras.AddChild(line);
        }

        if (snapshot.Council is { } council)
        {
            Section("Household council");
            townExtras.AddChild(new Label { Text = $"Steward: {council.StewardName ?? "awaiting a contributor"}" });
            townExtras.AddChild(new Label { Text = council.FoodPolicy == "essential_first" ? "Food reserve: hungry members first" : "Shared food: open access" });
            if (council.ProposedPolicy is not null)
                townExtras.AddChild(new Label { Text = $"Vote: {Pretty(council.ProposedPolicy)} · {council.Approvals} yes · {council.Rejections} no · {council.Voters} voters" });
        }

        var notes = snapshot.Inhabitants.SelectMany(person => person.SocialNotes.Take(2).Select(note => (person, note))).ToArray();
        Section("Social activity", notes.Length == 0 ? "nothing yet" : null);
        foreach (var (person, note) in notes)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(new TextureRect { Texture = RosterPortrait(person, IsLiving(person)), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin });
            line.AddChild(new Label { Text = $"{person.DisplayName}: {note}", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200, 0) });
            townExtras.AddChild(line);
        }
        FitTownExtras();
    }

    /// <summary>
    /// Stores, projects and activity grow with the Town, so they scroll once the
    /// panel would reach the bottom of the screen.
    /// </summary>
    private void FitTownExtras()
    {
        var content = townExtras.GetCombinedMinimumSize().Y;
        var rest = worldInfoPanel.GetCombinedMinimumSize().Y - townExtrasScroll.CustomMinimumSize.Y;
        var room = Math.Max(120, UiSize.Y - HudTop - 16 - rest);
        var scrolls = content > room;
        townExtrasGap.AddThemeConstantOverride("margin_right", scrolls ? SettingsScrollGap : 0);
        townExtrasScroll.CustomMinimumSize = new Vector2(0, scrolls ? room : content);
        worldInfoPanel.ResetSize();
    }

    /// <summary>A row of the residents' portraits under a Town's name.</summary>
    private static HBoxContainer ResidentPortraits(OwnerWorldSnapshot snapshot, OwnerWorldTown town)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        foreach (var id in town.ResidentIds.Take(10))
            if (snapshot.Inhabitants.FirstOrDefault(person => person.Id == id) is { } person)
                row.AddChild(new TextureRect { Texture = RosterPortrait(person, IsLiving(person)), TooltipText = person.DisplayName, StretchMode = TextureRect.StretchModeEnum.KeepCentered });
        if (town.ResidentIds.Count > 10)
            row.AddChild(new Label { Text = $"+{town.ResidentIds.Count - 10}", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        return row;
    }
}
