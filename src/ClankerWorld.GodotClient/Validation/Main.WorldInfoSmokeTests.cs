using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// World Info shows its facts as pictures: the World page as a today card,
    /// eight counted tiles and the F1 keycap; the Towns page with residents'
    /// portraits, a Find button, household stores as item slots and projects
    /// with a progress bar. Long Town details scroll instead of running off
    /// the screen.
    /// </summary>
    private async Task VerifyWorldInfoPagesAsync()
    {
        if (renderedMapSnapshot is not { } shown)
            throw new InvalidOperationException("The World Info checks need a world on screen.");
        var mira = PanelSmokeAgent("world-info-mira", "Mira", new OwnerWorldPosition(1, 1)) with
        {
            Project = new OwnerWorldProject("Kiln", "building", 36, 100, "Needs 4 more clay before work can start.", 0),
            SocialNotes = ["Shared berries with Ilya."],
        };
        var ilya = PanelSmokeAgent("world-info-ilya", "Ilya", new OwnerWorldPosition(2, 1));
        var snapshot = shown with
        {
            Inhabitants = [mira, ilya],
            Towns = [new OwnerWorldTown("town:river", "Riverbend", "founded", 0, [mira.Id, ilya.Id], [], [new OwnerWorldPosition(1, 1)])],
            Stockpiles =
            [
                new OwnerWorldStockpile("household:ash", "Ash household", [new("berries", 9), new("bread", 3), new("wood", 12), new("clay", 3)]),
                new OwnerWorldStockpile("household:reed", "Reed household", []),
            ],
            CalendarPace = new OwnerWorldCalendarPace(360, 40),
        };
        var wasVisible = worldInfoPanel.Visible;
        var showedTowns = WorldInfoShowsTowns;
        try
        {
            RenderWorldInfo(snapshot);
            RenderTownExtras(snapshot);
            worldInfoPanel.Show();
            ShowWorldInfoPage(towns: false);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var world = PageText(worldStatsPage);
            var tiles = worldStatsPage.FindChildren("*", nameof(PanelContainer), recursive: true, owned: false)
                .OfType<PanelContainer>().Count(tile => tile.ThemeTypeVariation == "InsetRow");
            if (tiles != 8 || !world.Contains(DisplayWorldClock(snapshot.WorldTick), StringComparison.Ordinal) ||
                !world.Contains("2 living agents", StringComparison.Ordinal) || !world.Contains("1 Town", StringComparison.Ordinal) ||
                !world.Contains("2 households", StringComparison.Ordinal) || !world.Contains("40-day years", StringComparison.Ordinal))
                throw new InvalidOperationException($"The World page must show today's date and eight counted tiles: tiles={tiles} text={world.ReplaceLineEndings(" / ")}");
            if (worldStatsPage.FindChildren("*", nameof(PanelContainer), recursive: true, owned: false).OfType<PanelContainer>()
                    .FirstOrDefault(cap => cap.GetChildCount() == 1 && cap.GetChild(0) is Label { Text: "F1" }) is null)
                throw new InvalidOperationException("The World page must point to the controls list with an F1 keycap.");

            await VerifyWorldStatsRefreshAsync(snapshot);
            RenderWorldInfo(snapshot);

            ShowWorldInfoPage(towns: true);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var portraits = townList.FindChildren("*", nameof(TextureRect), recursive: true, owned: false)
                .OfType<TextureRect>().Where(picture => picture.TooltipText is "Mira" or "Ilya").ToArray();
            var show = townList.FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>().FirstOrDefault();
            if (portraits.Length != 2 || show is null ||
                show.Icon != PixelIcons.Themed(PixelGlyph.Find, UiTheme.Current.Primary, 1) ||
                show.Size.Y >= ((Control)show.GetParent()).Size.Y)
                throw new InvalidOperationException($"Each Town must show its residents' portraits and a Find button sized to its text: portraits={portraits.Length}.");
            var towns = PageText(townsPage);
            if (towns.Contains("Town borders", StringComparison.Ordinal))
                throw new InvalidOperationException("The Towns page must not repeat the map filter state.");
            var slots = townExtras.FindChildren("*", "", recursive: true, owned: false).OfType<ItemSlot>().Count();
            var meter = townExtras.FindChildren("*", "", recursive: true, owned: false).OfType<PixelMeter>().SingleOrDefault();
            var blocker = townExtras.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>()
                .FirstOrDefault(label => label.Text.StartsWith("Needs 4 more clay", StringComparison.Ordinal));
            if (slots != 4 || !towns.Contains("27 items", StringComparison.Ordinal) || !towns.Contains("Nothing stored", StringComparison.Ordinal) ||
                meter?.Percent != 36 || !towns.Contains("36% · building", StringComparison.Ordinal) || blocker?.ThemeTypeVariation != "BadLabel")
                throw new InvalidOperationException($"Household stores must be item slots and projects a progress bar with any blocker in red: slots={slots} meter={meter?.Percent} text={towns.ReplaceLineEndings(" / ")}");

            VerifyTownProjectHistory(snapshot);

            // Many notes make the Town details scroll inside a panel that stays on screen.
            RenderTownExtras(snapshot with
            {
                Inhabitants = [mira with { SocialNotes = [.. Enumerable.Range(1, 2).Select(index => $"Talked with Ilya about the harvest, part {index}.")] },
                    .. Enumerable.Range(0, 30).Select(index => PanelSmokeAgent($"world-info-{index}", $"Agent {index}", new OwnerWorldPosition(index % 8, 2)) with
                    {
                        SocialNotes = [$"Agent {index} shared food with a neighbour and planned tomorrow's work."],
                    })],
            });
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (worldInfoPanel.Position.Y + worldInfoPanel.Size.Y > UiSize.Y + 1 ||
                townsScroll.CustomMinimumSize.Y >= townsPage.GetCombinedMinimumSize().Y ||
                townsGap.GetThemeConstant("margin_right") != SettingsScrollGap)
                throw new InvalidOperationException($"Long Town details must scroll inside World Info, clear of the scrollbar: panel={worldInfoPanel.GetRect()} screen={UiSize}.");
        }
        finally
        {
            Render(shown, []);
            RenderTownExtras(shown);
            ShowWorldInfoPage(showedTowns);
            worldInfoPanel.Visible = wasVisible;
        }
    }
    private async Task VerifyWorldStatsRefreshAsync(OwnerWorldSnapshot initial)
    {
        ulong[] TileIds() => worldStatsPage.FindChildren("*", nameof(PanelContainer), recursive: true, owned: false)
            .OfType<PanelContainer>().Where(tile => tile.ThemeTypeVariation == "InsetRow")
            .Select(tile => tile.GetInstanceId()).ToArray();
        var latest = initial;
        foreach (var (panelVisible, worldVisible) in new[] { (false, false), (true, false), (true, true) })
        {
            worldInfoPanel.Visible = panelVisible;
            ShowWorldInfoPage(towns: !worldVisible);
            var ids = TileIds();
            latest = latest with
            {
                WorldTick = latest.WorldTick + 90,
                Authoring = new(false, 0, 0, 0, latest.MapManifestDigest, latest.MapManifestDigest, "cloudy", "autumn", []),
                WeatherRegionSize = 16,
                WeatherRegions = [new(Math.Max(0, (int)MathF.Floor(cameraCenterTiles.X / 16)),
                    Math.Max(0, (int)MathF.Floor(cameraCenterTiles.Y / 16)), "rain", 73)],
                CalendarPace = new(360, 48),
            };
            Render(latest, []);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var text = PageText(worldStatsPage);
            if (!ids.SequenceEqual(TileIds()) || !text.Contains(DisplayWorldClock(latest.WorldTick), StringComparison.Ordinal) ||
                !text.Contains("Rain here", StringComparison.Ordinal) || !text.Contains("Soil moisture here: 73%", StringComparison.Ordinal) ||
                !text.Contains("48-day years", StringComparison.Ordinal))
                throw new InvalidOperationException("Clock and local-condition updates must preserve all eight count tiles, even when the World page or panel is hidden.");
            worldInfoPanel.Show();
            ShowWorldInfoPage(towns: false);
            if (!ids.SequenceEqual(TileIds()) || !PageText(worldStatsPage).Contains(DisplayWorldClock(latest.WorldTick), StringComparison.Ordinal))
                throw new InvalidOperationException("Opening the hidden World page must immediately show the latest date without replacing its tiles.");
        }
        var savedCamera = cameraCenterTiles;
        try
        {
            latest = latest with { WeatherRegionSize = 1, WeatherRegions = [new(0, 0, "clear", 18), new(1, 0, "snow", 92)] };
            cameraCenterTiles = new(0, 0);
            RenderWorldStats(latest);
            var cameraTiles = TileIds();
            cameraCenterTiles = new(1, 0);
            RenderWorldStats(latest);
            var local = PageText(worldStatsPage);
            if (!cameraTiles.SequenceEqual(TileIds()) || !local.Contains("Snow here", StringComparison.Ordinal) ||
                !local.Contains("Soil moisture here: 92%", StringComparison.Ordinal) || local.Contains("Clear here", StringComparison.Ordinal))
                throw new InvalidOperationException("Panning into another weather region must refresh local conditions without replacing count tiles.");
        }
        finally { cameraCenterTiles = savedCamera; }
        var counts = TileIds();
        latest = latest with { Authoring = null, WeatherRegions = [], CalendarPace = null };
        Render(latest, []);
        if (!counts.SequenceEqual(TileIds()) || !PageText(worldStatsPage).Contains("Season and weather not reported", StringComparison.Ordinal) ||
            PageText(worldStatsPage).Contains("Soil moisture here", StringComparison.Ordinal) || PageText(worldStatsPage).Contains("-day years", StringComparison.Ordinal))
            throw new InvalidOperationException("Missing conditions must remove stale date-card details without replacing unchanged counts.");

        // Each independently changing count must invalidate its derived grid.
        Func<OwnerWorldSnapshot, OwnerWorldSnapshot>[] changes =
        [
            value => value with { Inhabitants = [value.Inhabitants[0]] },
            value => value with { Towns = [] },
            value => value with { Stockpiles = [] },
            value => value with { PlacedBuildings = [.. value.PlacedBuildings, new("world-info-building", "test-building", new(0, 0), 0)] },
            value => value with { RoadTiles = [.. value.RoadTiles, new(0, 0)] },
            value => value with { Bridges = [.. value.Bridges, new("world-info-bridge", "footbridge", "river", "east-west", [], [], 0)] },
            value => value with { Resources = [.. value.Resources, new("world-info-resource", "wood", new(0, 0), true, "available")] },
            value => value with { PackedTerrain = new(MapDimensions(value).Width + 1, MapDimensions(value).Height, "terrain-kind-v1",
                Convert.ToBase64String(new byte[(MapDimensions(value).Width + 1) * MapDimensions(value).Height])) },
        ];
        foreach (var change in changes)
        {
            RenderWorldStats(initial);
            var before = TileIds();
            var changed = change(initial);
            RenderWorldStats(changed);
            if (before.SequenceEqual(TileIds()) || TileIds().Length != 8)
                throw new InvalidOperationException("A changed authoritative world count must refresh the eight-tile grid.");
            var changedIds = TileIds();
            RenderWorldStats(changed);
            if (!changedIds.SequenceEqual(TileIds()))
                throw new InvalidOperationException("An unchanged authoritative count snapshot must reuse its grid.");
            var text = PageText(worldStatsPage);
            if (!text.Contains($"{LivingPopulation(changed)} living agent", StringComparison.Ordinal) ||
                !text.Contains($"{changed.Towns.Count} Town", StringComparison.Ordinal) ||
                !text.Contains($"{changed.Stockpiles.Count} household", StringComparison.Ordinal) ||
                !text.Contains($"{changed.PlacedBuildings.Count} building", StringComparison.Ordinal) ||
                !text.Contains($"{changed.RoadTiles.Count} road tile", StringComparison.Ordinal) ||
                !text.Contains($"{changed.Bridges.Count} bridge", StringComparison.Ordinal) ||
                !text.Contains($"{changed.Resources.Count} resource site", StringComparison.Ordinal) ||
                !text.Contains($"{MapDimensions(changed).Width} × {MapDimensions(changed).Height}", StringComparison.Ordinal))
                throw new InvalidOperationException("The refreshed grid must display the current authoritative counts.");
        }
        var savedTheme = UiTheme.Current;
        try
        {
            RenderWorldStats(initial);
            var before = TileIds();
            UiTheme.Apply(GetTree().Root, ReferenceEquals(savedTheme, UiTheme.Dark) ? UiTheme.Light : UiTheme.Dark);
            RenderWorldStats(initial);
            if (before.SequenceEqual(TileIds()) || TileIds().Length != 8)
                throw new InvalidOperationException("A theme change must redraw the statistic icons in the current palette.");
            var personIcon = worldStatsPage.FindChildren("*", nameof(TextureRect), recursive: true, owned: false)
                .OfType<TextureRect>().FirstOrDefault(icon => ReferenceEquals(icon.Texture,
                    PixelIcons.Texture(PixelGlyph.Person, UiTheme.Current.Ink, BuiltAccent, 1)));
            if (personIcon is null)
                throw new InvalidOperationException("The statistic grid must use the current theme's icon palette.");
        }
        finally { UiTheme.Apply(GetTree().Root, savedTheme); }
        Render(initial, []);
    }

    private void VerifyTownProjectHistory(OwnerWorldSnapshot snapshot)
    {
        var project = new OwnerWorldTownProject("town:river:proposal:1:construction", "proposal:1",
            "Communal Hall", "former:worker", "Mira", "town-hall", "Town Hall", new(4, 4), new(5, 8),
            3, 4, [new("wood", 24, 24), new("stone", 12, 12)], 10, 10, "completed", null,
            "paid-hall", new("proposal:1", "town_project", "Build the Hall", "passed", 2, 0, 2, 0));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var removedJson = JsonSerializer.SerializeToNode(project, options)!;
        removedJson["removedTick"] = 60;
        var removed = removedJson.Deserialize<OwnerWorldTownProject>(options)!;
        var town = snapshot.Towns[0];
        RenderWorldInfo(snapshot with { Towns = [town with { Projects = [project] }] });
        var activeText = PageText(townList);
        if (!activeText.Contains("Built · select the Town Hall", StringComparison.Ordinal))
            throw new InvalidOperationException("A standing paid Hall must keep its map selection hint.");
        RenderWorldInfo(snapshot with { Towns = [town with { Projects = [removed] }] });
        var removedText = PageText(townList);
        if (!removedText.Contains("Built, later removed", StringComparison.Ordinal) ||
            removedText.Contains("Built · select", StringComparison.Ordinal) ||
            !removedText.Contains("10 / 10", StringComparison.Ordinal) ||
            !removedText.Contains("24 / 24", StringComparison.Ordinal))
            throw new InvalidOperationException("A removed Hall must retain its paid history without offering an absent building on the map.");
        var blocked = project with { Stage = "blocked", Blocker = "A household land request is pending.", CompletedBuildingId = null };
        RenderWorldInfo(snapshot with { Towns = [town with { Projects = [blocked] }] });
        var blockedText = PageText(townList);
        if (!blockedText.Contains("Waiting: A household land request is pending.", StringComparison.Ordinal) ||
            !blockedText.Contains("Work retries", StringComparison.Ordinal))
            throw new InvalidOperationException("A blocked Town project must show its reason and explain when work retries.");
        RenderWorldInfo(snapshot with { Towns = [town with { Projects = [blocked with { Stage = "cancelled" }] }] });
        var cancelledText = PageText(townList);
        if (!cancelledText.Contains("Not built:", StringComparison.Ordinal) || cancelledText.Contains("Work retries", StringComparison.Ordinal))
            throw new InvalidOperationException("A cancelled Town project must not promise another retry.");
        RenderWorldInfo(snapshot);
    }

}
