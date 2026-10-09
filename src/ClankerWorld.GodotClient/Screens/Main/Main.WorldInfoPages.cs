using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

/// <summary>
/// World Info drawn as pictures rather than lines of text: the World page as
/// a "today" card and a grid of counts with icons, and the Towns page with
/// each household's stores as item slots and projects with progress bars.
/// </summary>
public partial class Main
{
    private readonly VBoxContainer worldStatsPage = new();
    private readonly VBoxContainer townExtras = new();
    private readonly MarginContainer townsGap = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private string? renderedWorldStats;
    private string? renderedTownExtras;
    private const int ResidentPortraitLimit = 10;

    /// <summary>
    /// The Towns page, Town list then stores, projects and activity, scrolls as
    /// one once it would pass the bottom of the screen; the World page follows.
    /// </summary>
    private void BuildWorldInfoPages(VBoxContainer body)
    {
        townExtras.AddThemeConstantOverride("separation", 6);
        townExtras.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        townsPage.AddChild(townExtras);
        townsGap.AddChild(townsPage);
        townsScroll.AddChild(townsGap);
        body.AddChild(townsScroll);
        worldStatsPage.AddThemeConstantOverride("separation", 8);
        body.AddChild(worldStatsPage);
    }

    /// <summary>The wood brown of the Town house icon, for icons of things people built.</summary>
    private static Color BuiltAccent => UiTheme.Current.Name == "dark" ? new Color("D89A5A") : new Color("B8733A");

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

    /// <summary>The World page: today's date, season and weather where the camera is, then the world's counts.</summary>
    private void RenderWorldStats(OwnerWorldSnapshot snapshot)
    {
        var (width, height) = MapDimensions(snapshot);
        var weather = WeatherAtCamera(snapshot);
        var moisture = WeatherRegionAtCamera(snapshot)?.SoilMoisture;
        var signature = string.Join("|", DisplayWorldClock(snapshot.WorldTick), snapshot.Authoring?.Season, weather, moisture,
            snapshot.CalendarPace?.DaysPerYear, LivingPopulation(snapshot), snapshot.Towns.Count, snapshot.Stockpiles.Count,
            snapshot.PlacedBuildings.Count, snapshot.RoadTiles.Count, snapshot.Bridges.Count, snapshot.Resources.Count,
            width, height, UiTheme.Current.Name);
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
        if (snapshot.Authoring is { } season)
            todayRow.AddChild(new TextureRect { Texture = PixelIcons.Season(season.Season, 2), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var todayText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        todayText.AddThemeConstantOverride("separation", 0);
        todayText.AddChild(new Label { Text = DisplayWorldClock(snapshot.WorldTick), ThemeTypeVariation = "HeadingLabel" });
        // A season date already names the season, so only the weather follows it.
        var conditions = snapshot.Authoring is { } authoring
            ? (DatesShowSeason ? "" : $"{Pretty(authoring.Season)} · ") + $"{Pretty(weather)} here"
            : "Season and weather not reported";
        if (snapshot.CalendarPace is { } pace) conditions += $" · {pace.DaysPerYear}-day years";
        todayText.AddChild(new Label { Text = conditions, ThemeTypeVariation = "DimLabel" });
        if (moisture is { } wet)
            todayText.AddChild(new Label { Text = $"Soil moisture here: {wet}%", ThemeTypeVariation = "DimLabel" });
        todayRow.AddChild(todayText);
        if (snapshot.Authoring is not null)
            todayRow.AddChild(new TextureRect { Texture = PixelIcons.Weather(weather, 2), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        today.AddChild(todayRow);
        worldStatsPage.AddChild(today);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        var p = UiTheme.Current;
        static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
        static string Plural(int value, string one, string many) => value == 1 ? one : many;
        var living = LivingPopulation(snapshot);
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Person, p.Ink, BuiltAccent, 1), Count(living), Plural(living, "living agent", "living agents")));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Flag, p.Ink, p.Primary, 1), Count(snapshot.Towns.Count), Plural(snapshot.Towns.Count, "Town", "Towns")));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Heart, p.Partner, p.Partner, 1), Count(snapshot.Stockpiles.Count), Plural(snapshot.Stockpiles.Count, "household", "households")));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.House, p.Ink, BuiltAccent, 1), Count(snapshot.PlacedBuildings.Count), Plural(snapshot.PlacedBuildings.Count, "building", "buildings")));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Road, p.Ink, BuiltAccent, 1), Count(snapshot.RoadTiles.Count), Plural(snapshot.RoadTiles.Count, "road tile", "road tiles")));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Bridge, p.Ink, p.Name == "dark" ? new Color("7FB4E0") : new Color("2F6FA3"), 1), Count(snapshot.Bridges.Count), Plural(snapshot.Bridges.Count, "bridge", "bridges")));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Leaf, p.Ink, p.Primary, 1), Count(snapshot.Resources.Count), Plural(snapshot.Resources.Count, "resource site", "resource sites")));
        grid.AddChild(StatTile(PixelIcons.Texture(PixelGlyph.Globe, p.Ink, p.Primary, 1), $"{width} × {height}", "map tiles"));
        worldStatsPage.AddChild(grid);

        var hint = new HBoxContainer();
        hint.AddThemeConstantOverride("separation", 6);
        hint.AddChild(Keycap("F1"));
        hint.AddChild(new Label { Text = "All keyboard and mouse controls", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        worldStatsPage.AddChild(hint);
        if (worldStatsPage.Visible) worldInfoPanel.ResetSize();
    }

    /// <summary>
    /// Under the Town list: each household's stores as item slots, projects
    /// with their worker and progress, the household council and recent
    /// social activity. A section with nothing in it says so instead of
    /// leaving a gap.
    /// </summary>
    private void RenderTownExtras(OwnerWorldSnapshot snapshot)
    {
        var workers = snapshot.Inhabitants.Where(person => person.Project is not null).ToArray();
        var notes = snapshot.Inhabitants.SelectMany(person => person.SocialNotes.Take(2).Select(note => (person, note))).ToArray();
        var council = snapshot.Council;
        var signature = string.Join("\n", snapshot.Stockpiles.Select(store => store.Name + ":" + string.Join(",", store.Items.Select(item => item.Kind + item.Quantity)))) +
            "|" + string.Join(",", workers.Select(person =>
                $"{person.Id}:{person.DisplayName}:{person.Project!.Label}:{person.Project.Stage}:{person.Project.WorkDone}/{person.Project.WorkRequired}:{person.Project.Blocker}")) +
            "|" + council + "|" + string.Join(",", notes.Select(entry => $"{entry.person.Id}:{entry.person.DisplayName}:{IsLiving(entry.person)}:{entry.note}")) +
            "|" + UiTheme.Current.Name;
        if (signature == renderedTownExtras) return;
        renderedTownExtras = signature;
        foreach (var child in townExtras.GetChildren())
        {
            townExtras.RemoveChild(child);
            child.QueueFree();
        }
        void Section(string title)
        {
            // A little more room above each heading than between its rows.
            if (townExtras.GetChildCount() > 0) townExtras.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2), MouseFilter = Control.MouseFilterEnum.Ignore });
            var band = new HBoxContainer();
            band.AddThemeConstantOverride("separation", 8);
            band.AddChild(new Label { Text = title.ToUpperInvariant(), ThemeTypeVariation = "SectionLabel" });
            band.AddChild(new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            townExtras.AddChild(band);
        }
        void Note(string text) => townExtras.AddChild(new Label { Text = text, ThemeTypeVariation = "DimLabel" });
        static TextureRect Portrait(OwnerWorldInhabitant person, Control.SizeFlags vertical) => new()
        {
            Texture = AgentPortrait(person, IsLiving(person)),
            TooltipText = person.DisplayName,
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            SizeFlagsVertical = vertical,
        };

        Section("Household stores");
        if (snapshot.Stockpiles.Count == 0) Note("No household stores yet.");
        foreach (var store in snapshot.Stockpiles)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label { Text = store.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var total = store.Items.Sum(item => item.Quantity);
            row.AddChild(new Label { Text = total == 0 ? "Nothing stored" : total == 1 ? "1 item" : $"{total} items", ThemeTypeVariation = "DimLabel" });
            townExtras.AddChild(row);
            if (store.Items.Count == 0) continue;
            var slots = new HFlowContainer();
            slots.AddThemeConstantOverride("h_separation", 4);
            slots.AddThemeConstantOverride("v_separation", 4);
            foreach (var item in store.Items)
            {
                var slot = new ItemSlot();
                slot.SetItem(item.Kind, item.Quantity, GameUiText.ItemName(item.Kind));
                slots.AddChild(slot);
            }
            townExtras.AddChild(slots);
        }

        Section("Projects");
        if (workers.Length == 0) Note("No one is working on a project right now.");
        foreach (var person in workers)
        {
            var project = person.Project!;
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(Portrait(person, Control.SizeFlags.ShrinkBegin));
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(new Label { Text = $"{person.DisplayName} · {project.Label}" });
            var done = project.WorkRequired <= 0 ? 0 : Math.Clamp(project.WorkDone * 100 / project.WorkRequired, 0, 100);
            var progress = new HBoxContainer();
            progress.AddThemeConstantOverride("separation", 6);
            progress.AddChild(new PixelMeter { Kind = MeterKind.Progress, CaptionWidth = 0, Percent = done, TooltipText = $"{done}% done" });
            progress.AddChild(new Label { Text = $"{done}% · {Pretty(project.Stage).ToLowerInvariant()}", ThemeTypeVariation = "DimLabel" });
            text.AddChild(progress);
            if (project.Blocker is { } blocker)
                text.AddChild(new Label { Text = GameUiText.PlainEllipses(blocker), ThemeTypeVariation = "BadLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            line.AddChild(text);
            townExtras.AddChild(line);
        }

        if (council is not null)
        {
            Section("Household council");
            townExtras.AddChild(new Label { Text = $"Steward: {council.StewardName ?? "awaiting a contributor"}" });
            townExtras.AddChild(new Label { Text = council.FoodPolicy == "essential_first" ? "Food reserve: hungry members first" : "Shared food: open access" });
            if (council.ProposedPolicy is not null)
                townExtras.AddChild(new Label { Text = $"Vote: {Pretty(council.ProposedPolicy)} · {council.Approvals} yes · {council.Rejections} no · {council.Voters} voters" });
        }

        Section("Social activity");
        if (notes.Length == 0) Note("Nothing to report yet.");
        foreach (var (person, note) in notes)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(Portrait(person, Control.SizeFlags.ShrinkBegin));
            line.AddChild(new Label
            {
                Text = $"{person.DisplayName}: {GameUiText.PlainEllipses(note)}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(200, 0),
            });
            townExtras.AddChild(line);
        }
        QueueHudListsFit();
    }

    /// <summary>A row of the residents' portraits under a Town's name.</summary>
    private static HBoxContainer ResidentPortraits(OwnerWorldSnapshot snapshot, OwnerWorldTown town)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        foreach (var id in town.ResidentIds.Take(ResidentPortraitLimit))
            if (snapshot.Inhabitants.FirstOrDefault(person => person.Id == id) is { } person)
                row.AddChild(new TextureRect { Texture = AgentPortrait(person, IsLiving(person)), TooltipText = person.DisplayName, StretchMode = TextureRect.StretchModeEnum.KeepCentered });
        if (town.ResidentIds.Count > ResidentPortraitLimit)
            row.AddChild(new Label { Text = $"+{town.ResidentIds.Count - ResidentPortraitLimit}", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        return row;
    }

    /// <summary>What the resident portraits show, so the Town list is redrawn when one changes.</summary>
    private static string ResidentPortraitsKey(OwnerWorldSnapshot snapshot, OwnerWorldTown town) =>
        string.Join(",", town.ResidentIds.Take(ResidentPortraitLimit).Select(id => snapshot.Inhabitants.FirstOrDefault(person => person.Id == id) is { } person
            ? $"{id}:{person.DisplayName.Length}:{person.DisplayName}:{IsLiving(person)}:{person.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail}"
            : id));

    /// <summary>Every label and tooltip on a World Info page, for checks and assistive reading.</summary>
    private static string PageText(Control page) => string.Join("\n",
        page.FindChildren("*", "", recursive: true, owned: false).OfType<Control>()
            .SelectMany(control => new[] { (control as Label)?.Text, control.TooltipText })
            .Where(text => !string.IsNullOrEmpty(text)));
}
