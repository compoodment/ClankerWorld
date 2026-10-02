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
}
