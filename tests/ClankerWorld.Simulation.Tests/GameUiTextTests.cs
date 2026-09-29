using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class GameUiTextTests
{
    private static readonly int[] UiScalePercentages = [100, 125, 150, 175, 200];

    [Fact]
    public void OwnerSnapshotReportsTheSavedWorldCalendarPace()
    {
        using var world = new PrivateWorldRuntime("calendar-projection");
        var saved = world.ExportState();
        var pace = new OwnerWorldObservationStore(world).GetSnapshot().CalendarPace;
        Assert.NotNull(pace);
        Assert.Equal(saved.WorldSystems!.Config.TicksPerDay, pace.TicksPerDay);
        Assert.Equal(saved.WorldSystems.Config.DaysPerYear, pace.DaysPerYear);
    }

    [Fact]
    public void ResourceHelpDistinguishesExhaustionRegrowthAndLegacyUnknownQuantities()
    {
        var wood = new OwnerWorldResource("wood", "construction", new(0, 0), false, "depleted", 0, 12, 0, 0, "spring");
        Assert.Equal("Wild timber · 0/12\nDepleted\nFinite — no natural regrowth.", GameUiText.ResourceTooltip(wood));
        var berries = wood with { Id = "food", Kind = "food", IsRenewable = true, RegenerationAmount = 4, RegenerationIntervalDays = 1 };
        Assert.Contains("+4 every 1 world day(s) in Spring", GameUiText.ResourceTooltip(berries), StringComparison.Ordinal);
        Assert.Equal("Soil 3/3", GameUiText.ResourceQuantity("fertile_land", 3, 3));
        var legacy = new OwnerWorldResource("old", "food", new(0, 0), true, "available");
        Assert.Contains("details unavailable", GameUiText.ResourceTooltip(legacy), StringComparison.Ordinal);
        Assert.DoesNotContain("0/", GameUiText.ResourceTooltip(legacy), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "01-01-0001 · 00:00")]
    [InlineData(1_440, "02-01-0001 · 00:00")]
    [InlineData(44_640, "01-02-0001 · 00:00")]
    public void WorldClockUsesTheSavedCalendarInsteadOfRawTicks(long worldTick, string expected)
    {
        Assert.Equal(expected, GameUiText.FormatWorldClock(worldTick));
    }

    [Theory]
    [InlineData(0, "01-01-0001 · 12:00 AM")]
    [InlineData(720, "01-01-0001 · 12:00 PM")]
    [InlineData(780, "01-01-0001 · 1:00 PM")]
    [InlineData(1_439, "01-01-0001 · 11:59 PM")]
    public void WorldClockCanUseTwelveHourDisplayWithoutChangingWorldTime(long worldTick, string expected)
    {
        Assert.Equal(expected, GameUiText.FormatWorldClock(worldTick, useTwelveHourClock: true));
    }

    [Fact]
    public void CustomFortyDayYearUsesFourTenDayMonthsAndScalesClockFromWorldTicks()
    {
        var calendar = new OwnerWorldCalendarPace(360, 40);
        Assert.Equal("01-01-0001 · 00:04", GameUiText.FormatWorldClock(1, calendarPace: calendar));
        Assert.Equal("01-02-0001 · 00:00", GameUiText.FormatWorldClock(3_600, calendarPace: calendar));
        Assert.Equal("01-01-0002 · 00:00", GameUiText.FormatWorldClock(14_400, calendarPace: calendar));
        Assert.Equal("02-01-0002 · 12:00 PM", GameUiText.FormatWorldClock(14_940,
            useTwelveHourClock: true, calendarPace: calendar));
        Assert.Equal("01-02-0002 · 12:00", GameUiText.FormatWorldClock(14_940,
            calendarPace: calendar, dateFormat: "mdy"));
        Assert.Equal("0002-01-02 · 12:00", GameUiText.FormatWorldClock(14_940,
            calendarPace: calendar, dateFormat: "ymd"));
    }

    [Fact]
    public void GameClockPreferencePersistsOutsideWorldSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "clankerworld-display-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GameDisplayPreferencesStore(Path.Combine(directory, "game-settings.json"));
            Assert.False(store.Load().UseTwelveHourClock);
            store.Save(new GameDisplayPreferences(UseTwelveHourClock: true,
                WindowWidth: 1600, WindowHeight: 900, RenderWidth: 1920, RenderHeight: 1080,
                UiScalePercent: 150, Fullscreen: false));
            var restored = new GameDisplayPreferencesStore(Path.Combine(directory, "game-settings.json")).Load();
            Assert.True(restored.UseTwelveHourClock);
            Assert.Equal((1600, 900), (restored.WindowWidth, restored.WindowHeight));
            Assert.Equal((1920, 1080), (restored.RenderWidth, restored.RenderHeight));
            Assert.False(restored.UsesAutomaticRenderResolution);
            Assert.Equal(150, restored.UiScalePercent);
            Assert.False(restored.UsesFullscreen);
            store.Save(restored with { DateFormat = "ymd" });
            Assert.Equal("ymd", store.Load().DateFormat);
            Assert.Equal((1600, 900), (store.Load().WindowWidth, store.Load().WindowHeight));
            Assert.Equal((1920, 1080), (store.Load().RenderWidth, store.Load().RenderHeight));
            Assert.Equal(150, store.Load().UiScalePercent);
            Assert.False(store.Load().UsesFullscreen);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UiScaleOffersAccessiblePercentagesAndNormalizesUnsupportedSavedValues()
    {
        Assert.Equal(UiScalePercentages, DisplayUiScalePolicy.SupportedPercentages);
        Assert.Equal(1f, DisplayUiScalePolicy.ScaleFactor(100));
        Assert.Equal(1.5f, DisplayUiScalePolicy.ScaleFactor(150));
        Assert.Equal(2f, DisplayUiScalePolicy.ScaleFactor(200));
        Assert.Equal(100, DisplayUiScalePolicy.NormalizePercent(123));
        Assert.Equal(100, new GameDisplayPreferences().UiScalePercent);
        Assert.True(new GameDisplayPreferences().UsesFullscreen);

        var directory = Directory.CreateTempSubdirectory("clanker-display-ui-scale-");
        try
        {
            var path = Path.Combine(directory.FullName, "game-settings.json");
            File.WriteAllText(path, "{\"UiScalePercent\":300}");
            var store = new GameDisplayPreferencesStore(path);
            Assert.Equal(100, store.Load().UiScalePercent);

            store.Save(store.Load() with { UiScalePercent = 200 });
            Assert.Equal(200, store.Load().UiScalePercent);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AutomaticRenderResolutionFollowsA1440pDisplayAndWindowWithoutHidingExplicitChoices()
    {
        var monitor = new DisplayDimensions(2560, 1440);
        Assert.Equal(monitor, DisplayResolutionPolicy.AutomaticRenderSize(
            monitor, new DisplayDimensions(1600, 900), fullscreen: true));
        Assert.Equal(new DisplayDimensions(1600, 900), DisplayResolutionPolicy.AutomaticRenderSize(
            monitor, new DisplayDimensions(1600, 900), fullscreen: false));
        Assert.Contains(monitor, DisplayResolutionPolicy.FixedRenderSizes(monitor));
        Assert.DoesNotContain(monitor, DisplayResolutionPolicy.FixedRenderSizes(new DisplayDimensions(1920, 1080)));
        Assert.Contains(monitor, DisplayResolutionPolicy.FixedRenderSizes(
            new DisplayDimensions(1920, 1080), saved: monitor));
        Assert.Contains(new DisplayDimensions(1920, 1080), DisplayResolutionPolicy.FixedRenderSizes(
            new DisplayDimensions(1920, 1080), saved: monitor));
    }

    [Fact]
    public void OldDefaultRenderChoiceMigratesToAutomaticWhileExplicitLegacyChoiceStaysFixed()
    {
        var directory = Directory.CreateTempSubdirectory("clanker-display-migration-");
        try
        {
            var path = Path.Combine(directory.FullName, "game-settings.json");
            File.WriteAllText(path, "{\"RenderWidth\":1280,\"RenderHeight\":720}");
            var store = new GameDisplayPreferencesStore(path);
            Assert.True(store.Load().UsesAutomaticRenderResolution);
            store.Save(store.Load() with { AutoRenderResolution = false });
            Assert.False(store.Load().UsesAutomaticRenderResolution);

            File.WriteAllText(path, "{\"RenderWidth\":1920,\"RenderHeight\":1080}");
            Assert.False(store.Load().UsesAutomaticRenderResolution);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("tick_advanced", false)]
    [InlineData("initial_content_activated", false)]
    [InlineData("build_completed", true)]
    public void EventTimelineKeepsWorldNewsAndDropsProtocolNoise(string kind, bool expected)
    {
        Assert.Equal(expected, GameUiText.IsPlayerFacingEvent(kind));
    }

    [Theory]
    [InlineData("build:building:sha256:abcdef/building/stone-hearth@1.0.0", "build Stone hearth")]
    [InlineData("seek_food", "find food")]
    [InlineData("guardian_tend:dependent-42", "care for an ill dependent")]
    public void InternalIdentifiersBecomeReadablePhrases(string value, string expected)
    {
        Assert.Equal(expected, GameUiText.HumanizeIdentifier(value));
    }
}
