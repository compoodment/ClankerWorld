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

            ShowWorldInfoPage(towns: true);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var portraits = townList.FindChildren("*", nameof(TextureRect), recursive: true, owned: false)
                .OfType<TextureRect>().Where(picture => picture.TooltipText is "Mira" or "Ilya").ToArray();
            var show = townList.FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>().FirstOrDefault();
            if (portraits.Length != 2 || show is null ||
                show.Icon != PixelIcons.Themed(PixelGlyph.Find, UiTheme.Current.Primary, 1) ||
                show.Size.Y >= ((Control)show.GetParent()).Size.Y)
                throw new InvalidOperationException($"Each Town must show its residents' portraits and a Find button sized to its text: portraits={portraits.Length}.");
            await VerifyTownPortraitRefreshAsync(snapshot, ilya);
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
            RenderWorldInfo(shown);
            RenderTownExtras(shown);
            ShowWorldInfoPage(showedTowns);
            worldInfoPanel.Visible = wasVisible;
        }
    }

    private async Task VerifyTownPortraitRefreshAsync(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant resident)
    {
        var child = resident with { DisplayName = "Robin0 Vale", DecisionFactors = [new("age-band", "child")] };
        var catalog = snapshot with
        {
            Inhabitants = [.. snapshot.Inhabitants.Where(person => person.Id != child.Id), child],
            Towns = [snapshot.Towns[0], .. Enumerable.Range(1, 20).Select(index =>
                snapshot.Towns[0] with { Id = $"town:portrait-{index}", Name = $"Portrait Town {index}", ResidentIds = [] })],
        };
        var scrollBefore = townsScroll.ScrollVertical;
        TextureRect Portrait(string name) => townList.FindChildren("*", nameof(TextureRect), true, false)
            .OfType<TextureRect>().Single(picture => picture.TooltipText == name);
        static byte[] Pixels(TextureRect picture)
        {
            using var image = ((ImageTexture)picture.Texture!).GetImage();
            return image.GetData();
        }
        async Task Settle()
        {
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        try
        {
            RenderTownList(catalog); await Settle();
            townsScroll.ScrollVertical = 80; await Settle();
            var scroll = townsScroll.ScrollVertical;
            if (scroll <= 0) throw new InvalidOperationException("The rename check needs an actually scrolled Town list.");
            var before = Portrait(child.DisplayName);
            var beforeId = before.GetInstanceId();
            var childPixels = Pixels(before);
            RenderTownList(catalog);
            if (Portrait(child.DisplayName).GetInstanceId() != beforeId)
                throw new InvalidOperationException("An unchanged Town observation must reuse its resident portrait.");
            child = child with { DisplayName = "Liora Vale" };
            catalog = catalog with { Inhabitants = [.. catalog.Inhabitants.Select(person => person.Id == child.Id ? child : person)] };
            RenderTownList(catalog); await Settle();
            var renamed = Portrait("Liora Vale");
            if (renamed.GetInstanceId() == beforeId || !Pixels(renamed).SequenceEqual(childPixels) ||
                townsScroll.ScrollVertical != scroll || townList.FindChildren("*", nameof(TextureRect), true, false)
                    .OfType<TextureRect>().Any(picture => picture.TooltipText == "Robin0 Vale"))
                throw new InvalidOperationException("A child rename must refresh its tooltip, preserve its portrait art and keep Town scrolling.");
            RenderTownList(catalog);
            if (Portrait("Liora Vale").GetInstanceId() != renamed.GetInstanceId())
                throw new InvalidOperationException("After a rename, unchanged observations must reuse the refreshed portrait.");
            child = child with { DecisionFactors = [new("age-band", "adult")] };
            catalog = catalog with { Inhabitants = [.. catalog.Inhabitants.Select(person => person.Id == child.Id ? child : person)] };
            RenderTownList(catalog);
            var adultPixels = Pixels(Portrait("Liora Vale"));
            if (adultPixels.SequenceEqual(childPixels)) throw new InvalidOperationException("A resident's changed age band must still refresh the portrait art.");
            child = child with { Lifecycle = "deceased" };
            catalog = catalog with { Inhabitants = [.. catalog.Inhabitants.Select(person => person.Id == child.Id ? child : person)] };
            RenderTownList(catalog);
            if (Pixels(Portrait("Liora Vale")).SequenceEqual(adultPixels))
                throw new InvalidOperationException("A resident's death must still refresh the portrait background.");
            GD.Print("NATIVE_TOWN_PORTRAIT_RENAME tooltipArtScrollUnchangedReuseAgeLiving=passed");
        }
        finally
        {
            RenderTownList(snapshot);
            townsScroll.ScrollVertical = scrollBefore;
        }
    }

}
