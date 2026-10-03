using System.Numerics;
using System.Text.Json;
using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class GameUiTextTests
{
    private static readonly JsonSerializerOptions HostJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions GameJson = new() { PropertyNameCaseInsensitive = true };

    [Theory]
    [InlineData("future_internal_diagnostic", false)]
    [InlineData("saved_road_footprints_repaired", false)]
    [InlineData("tick_advanced", false)]
    [InlineData("tree_planting_refused", false)]
    [InlineData("instruction_applied", false)]
    [InlineData("housing_blocked", true)]
    [InlineData("household_delivery_recovered", true)]
    [InlineData("housing_answer_recorded", false)]
    [InlineData("housing_request_cancelled", false)]
    [InlineData("model_call_warning", true)]
    public void EventLogSelectsKnownPlayerEventsInsteadOfPublishingUnknownDiagnostics(string kind, bool visible)
    {
        Assert.Equal(visible, GameUiText.IsPlayerFacingEvent(kind));
    }

    [Theory]
    [InlineData("  Alexandria   Smith  ", "Alexandria")]
    [InlineData("Álexandriannnnnnnn", "Á.")]
    [InlineData("李 小龙", "李")]
    [InlineData("  ", "?")]
    public void MapNamesUseWholeGivenNamesOrWholeTextElementInitials(string fullName, string expected) =>
        Assert.Equal(expected, GameUiText.ActorMapLabel(fullName));

    [Fact]
    public void PlayerFailuresDescribeRecoveryWithoutExposingRawExceptionText()
    {
        const string secret = "sk-private /home/private/save provider-response";
        Exception[] failures = [new InvalidOperationException(secret), new IOException(secret),
            new UnauthorizedAccessException(secret), new ArgumentException(secret),
            new System.Text.Json.JsonException(secret), new System.Security.Cryptography.CryptographicException(secret),
            new HttpRequestException(secret, null, System.Net.HttpStatusCode.Forbidden),
            new TaskCanceledException(secret)];
        foreach (var failure in failures)
        {
            var message = GameUiText.FriendlyFailure(failure);
            Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
            Assert.DoesNotContain(failure.GetType().Name, message, StringComparison.Ordinal);
            Assert.NotEmpty(message);
        }
        Assert.Contains("connecting", GameUiText.FriendlyFailure(failures[6]), StringComparison.Ordinal);
        Assert.Contains("storage", GameUiText.FriendlyFailure(failures[1]), StringComparison.Ordinal);
    }

    [Fact]
    public void HouseholdDisplayCleanupPreservesSavedMembershipAndCustomNames()
    {
        using var world = new PrivateWorldRuntime("household-display");
        var state = world.ExportState();
        Assert.Equal("First household", Assert.Single(state.Society.Society.Households).Name);
        foreach (var name in new[] { "Camp Alpha", "River family" })
        {
            var legacy = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Households = state.Society.Society.Households.Select(home => home with { Name = name }).ToArray(),
                    },
                },
            };
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(legacy)));
            var before = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
            var stockpile = Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Stockpiles);
            Assert.Equal(name == "Camp Alpha" ? "First household" : name, stockpile.Name);
            Assert.Equal("household:camp-alpha", stockpile.OwnerId);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            Assert.Equal(state.Society.Society.Households[0].MemberIds,
                restored.ExportState().Society.Society.Households[0].MemberIds);
        }
    }

    [Theory]
    [InlineData(WorldStartPace.Legacy)]
    [InlineData(WorldStartPace.DecidedPlaytest)]
    [InlineData(WorldStartPace.FounderSetup)]
    public void OwnerSnapshotReportsTheSavedWorldCalendarPace(WorldStartPace startPace)
    {
        using var world = new PrivateWorldRuntime("calendar-projection", startPace: startPace);
        var config = world.ExportState().WorldSystems!.Config;
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var pace = snapshot.CalendarPace;
        Assert.Equal(new ViewerCalendarPace(config.TicksPerDay, config.DaysPerYear,
            config.SpringDays, config.SummerDays, config.AutumnDays, config.WinterDays, config.CalendarOffsetTicks), pace);

        // The game reads the season lengths the host sends rather than keeping
        // its own copy, so every date names the season the world is in.
        var client = JsonSerializer.Deserialize<OwnerWorldSnapshot>(JsonSerializer.Serialize(snapshot, HostJson), GameJson)!;
        var received = client.CalendarPace;
        Assert.Equal(0, client.WorldTick);
        Assert.Equal(startPace == WorldStartPace.Legacy ? DaylightRules.FullDarkness : 0, client.DarknessBasisPoints);
        Assert.Equal(startPace == WorldStartPace.Legacy ? "Spring 1, Year 1 · 00:00" : "Spring 1, Year 1 · 06:00",
            GameUiText.FormatWorldClock(client.WorldTick, calendarPace: received));
        Assert.True(GameUiText.ShowsSeasonDates(received, GameUiText.SeasonDates));
        for (var day = 0; day < config.DaysPerYear * 2; day++)
        {
            var tick = (long)day * config.TicksPerDay + config.TicksPerDay / 2 - config.CalendarOffsetTicks;
            var calendar = WorldCalendarRules.FromTick(tick, config);
            Assert.Equal($"{calendar.Season} {calendar.DayOfSeason + 1}, Year {day / config.DaysPerYear + 1} · 12:00",
                GameUiText.FormatWorldClock(tick, calendarPace: received));
        }
    }

    [Theory]
    [InlineData(0, "Spring 1, Year 1 · 06:00")]
    [InlineData(1, "Spring 1, Year 1 · 06:04")]
    [InlineData(269, "Spring 1, Year 1 · 23:56")]
    [InlineData(270, "Spring 2, Year 1 · 00:00")]
    [InlineData(3509, "Spring 10, Year 1 · 23:56")]
    [InlineData(3510, "Summer 1, Year 1 · 00:00")]
    [InlineData(14309, "Winter 10, Year 1 · 23:56")]
    [InlineData(14310, "Spring 1, Year 2 · 00:00")]
    public void MorningCalendarFormatsRawHistoryTicksAcrossDateBoundaries(long tick, string expected)
    {
        var calendar = new OwnerWorldCalendarPace(360, 40, 10, 10, 10, 10, CalendarOffsetTicks: 90);
        Assert.Equal(expected, GameUiText.FormatWorldClock(tick, calendarPace: calendar));
    }

    [Fact]
    public void MorningOffsetAppliesToEveryDateStyleAndOldHostsKeepMidnight()
    {
        var calendar = new OwnerWorldCalendarPace(360, 40, 10, 10, 10, 10, CalendarOffsetTicks: 90);
        Assert.Equal("Spring 1, Year 1 · 6:00 AM", GameUiText.FormatWorldClock(0, true, calendar));
        Assert.Equal("02-01-0001 · 00:00", GameUiText.FormatWorldClock(270, calendarPace: calendar, dateFormat: "dmy"));
        Assert.Equal("01-02-0001 · 12:00 AM", GameUiText.FormatWorldClock(270, true, calendar, "mdy"));
        Assert.Equal("0001-01-02 · 00:00", GameUiText.FormatWorldClock(270, calendarPace: calendar, dateFormat: "ymd"));
        var oldHost = JsonSerializer.Deserialize<OwnerWorldCalendarPace>(
            """{"ticksPerDay":360,"daysPerYear":40,"springDays":10,"summerDays":10,"autumnDays":10,"winterDays":10}""", GameJson)!;
        Assert.Equal(0, oldHost.CalendarOffsetTicks);
        Assert.Equal("Spring 1, Year 1 · 00:00", GameUiText.FormatWorldClock(0, calendarPace: oldHost));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameUiText.FormatWorldClock(0, calendarPace: calendar with { CalendarOffsetTicks = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameUiText.FormatWorldClock(0, calendarPace: calendar with { CalendarOffsetTicks = 360 }));
    }

    [Theory]
    [InlineData(360, 90)]
    [InlineData(int.MaxValue, int.MaxValue - 1)]
    public void CalendarOffsetFormatsTheLargestRepresentableTickWithoutOverflow(int ticksPerDay, int offset)
    {
        var calendar = new OwnerWorldCalendarPace(ticksPerDay, 40, 10, 10, 10, 10, offset);
        var shifted = (BigInteger)long.MaxValue + offset;
        var day = shifted / ticksPerDay;
        var dayOfYear = (int)(day % 40);
        var minutes = (int)((shifted % ticksPerDay) * 1440 / ticksPerDay);
        var season = new[] { "Spring", "Summer", "Autumn", "Winter" }[dayOfYear / 10];
        var expected = $"{season} {dayOfYear % 10 + 1}, Year {day / 40 + 1} · {minutes / 60:00}:{minutes % 60:00}";
        Assert.Equal(expected, GameUiText.FormatWorldClock(long.MaxValue, calendarPace: calendar));
    }

    [Fact]
    public void SeasonDatesAreTheDefaultAndWorkWithBothClocks()
    {
        var calendar = new OwnerWorldCalendarPace(360, 40, 10, 10, 10, 10);
        // Day 22 of the year at 14:20 is the second day of Autumn.
        const long autumnAfternoon = 21 * 360 + 215;
        Assert.Equal("Autumn 2, Year 1 · 14:20", GameUiText.FormatWorldClock(autumnAfternoon, calendarPace: calendar));
        Assert.Equal("Autumn 2, Year 1 · 2:20 PM", GameUiText.FormatWorldClock(autumnAfternoon,
            useTwelveHourClock: true, calendarPace: calendar));
        Assert.Equal("Autumn 2, Year 1 · 14:20", GameUiText.FormatWorldClock(autumnAfternoon,
            calendarPace: calendar, dateFormat: new GameDisplayPreferences().DateStyle));
        Assert.Equal("Spring 1, Year 1 · 12:00 AM", GameUiText.FormatWorldClock(0,
            useTwelveHourClock: true, calendarPace: calendar));
        Assert.Equal("Winter 10, Year 1 · 23:56", GameUiText.FormatWorldClock(14_399, calendarPace: calendar));
        Assert.Equal("Spring 1, Year 2 · 00:00", GameUiText.FormatWorldClock(14_400, calendarPace: calendar));
        Assert.Equal("Summer 10, Year 12 · 12:00 PM", GameUiText.FormatWorldClock(11 * 14_400 + 19 * 360 + 180,
            useTwelveHourClock: true, calendarPace: calendar));

        // Numeric orders keep both clocks, and the season date is not shown.
        Assert.Equal("02-03-0001 · 14:20", GameUiText.FormatWorldClock(autumnAfternoon,
            calendarPace: calendar, dateFormat: "dmy"));
        Assert.Equal("03-02-0001 · 2:20 PM", GameUiText.FormatWorldClock(autumnAfternoon,
            useTwelveHourClock: true, calendarPace: calendar, dateFormat: "mdy"));
        Assert.Equal("0001-03-02 · 14:20", GameUiText.FormatWorldClock(autumnAfternoon,
            calendarPace: calendar, dateFormat: "ymd"));
        Assert.False(GameUiText.ShowsSeasonDates(calendar, "dmy"));
        Assert.True(GameUiText.ShowsSeasonDates(calendar, GameUiText.SeasonDates));
        Assert.True(GameUiText.ShowsSeasonDates(calendar, "an-unknown-style"));

        // Without the world's season lengths (an older host, or lengths that
        // do not fill the year), dates fall back to numbers instead of guessing.
        foreach (var unknown in new[] { new OwnerWorldCalendarPace(360, 40), calendar with { WinterDays = 9 }, calendar with { SpringDays = 0, SummerDays = 20 } })
        {
            Assert.False(GameUiText.ShowsSeasonDates(unknown, GameUiText.SeasonDates));
            Assert.Equal("02-03-0001 · 14:20", GameUiText.FormatWorldClock(autumnAfternoon, calendarPace: unknown));
        }
        Assert.False(GameUiText.ShowsSeasonDates(null, GameUiText.SeasonDates));
    }

    [Fact]
    public void ResourceHelpDistinguishesExhaustionRegrowthAndLegacyUnknownQuantities()
    {
        var wood = new OwnerWorldResource("wood", "construction", new(0, 0), false, "depleted", 0, 12, 0, 0, "spring");
        Assert.Equal("Wild timber · 0/12\nDepleted\nDoes not grow back.", GameUiText.ResourceTooltip(wood));
        var berries = wood with { Id = "food", Kind = "food", IsRenewable = true, RegenerationAmount = 4, RegenerationIntervalDays = 1 };
        Assert.Contains("+4 every 1 day in Spring", GameUiText.ResourceTooltip(berries), StringComparison.Ordinal);
        Assert.Equal("Soil 3/3", GameUiText.ResourceQuantity("fertile_land", 3, 3));
        var legacy = new OwnerWorldResource("old", "food", new(0, 0), true, "available");
        Assert.Contains("Grows back.", GameUiText.ResourceTooltip(legacy), StringComparison.Ordinal);
        Assert.Equal(string.Empty, GameUiText.ResourceMapCaption(legacy, 16));
        Assert.Equal("Food", GameUiText.ResourceMapCaption(legacy, 48));
        Assert.Equal("Wood 0/12", GameUiText.ResourceMapCaption(wood, 48));
        Assert.Equal("Plot Soil 3/3", GameUiText.ResourceMapCaption(wood with { Kind = "fertile_land", Quantity = 3, Capacity = 3 }, 48));
        Assert.DoesNotContain("0/", GameUiText.ResourceTooltip(legacy), StringComparison.Ordinal);
    }

    [Fact]
    public void WorldClockUsesDefaultAndCustomCalendarBoundariesAndScalesWorldTicks()
    {
        Assert.Equal("01-02-0001 · 00:00", GameUiText.FormatWorldClock(44_640));
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
                WindowWidth: 1600, WindowHeight: 900, Fullscreen: false));
            var restored = new GameDisplayPreferencesStore(Path.Combine(directory, "game-settings.json")).Load();
            Assert.True(restored.UseTwelveHourClock);
            Assert.Equal("01-01-0001 · 12:00 AM", GameUiText.FormatWorldClock(0,
                useTwelveHourClock: restored.UseTwelveHourClock));
            Assert.Equal("01-01-0001 · 11:59 PM", GameUiText.FormatWorldClock(1_439,
                useTwelveHourClock: restored.UseTwelveHourClock));
            Assert.Equal((1600, 900), (restored.WindowWidth, restored.WindowHeight));
            Assert.False(restored.UsesFullscreen);
            Assert.Equal(GameUiText.SeasonDates, restored.DateStyle);
            store.Save(restored with { DateStyle = "ymd" });
            Assert.Equal("ymd", store.Load().DateStyle);
            Assert.Equal((1600, 900), (store.Load().WindowWidth, store.Load().WindowHeight));
            Assert.False(store.Load().UsesFullscreen);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OlderSavedInterfaceAndRenderChoicesAreIgnored()
    {
        Assert.True(new GameDisplayPreferences().UsesFullscreen);
        var directory = Directory.CreateTempSubdirectory("clanker-display-ui-scale-");
        try
        {
            var path = Path.Combine(directory.FullName, "game-settings.json");
            // The older DateFormat entry held "dmy" whether or not the player
            // chose it, so season dates replace it rather than guessing.
            File.WriteAllText(path, "{\"UiScalePercent\":400,\"RenderWidth\":1920,\"RenderHeight\":1080,\"Theme\":\"dark\",\"DateFormat\":\"dmy\"}");
            var store = new GameDisplayPreferencesStore(path);
            Assert.Equal("dark", store.Load().Theme);
            Assert.Equal(GameUiText.SeasonDates, store.Load().DateStyle);
            store.Save(store.Load());
            Assert.DoesNotContain("UiScalePercent", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.DoesNotContain("RenderWidth", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.DoesNotContain("DateFormat", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheInterfaceDropsASizeRatherThanSqueezeTheMenus()
    {
        Assert.Equal(1, DisplayUiScalePolicy.FittingFactor(1700, 1080));
        Assert.Equal(2, DisplayUiScalePolicy.FittingFactor(2560, 1600));
    }

    [Fact]
    public void ThePictureFollowsTheScreenInFullscreenAndTheWindowOtherwise()
    {
        var monitor = new DisplayDimensions(2560, 1440);
        Assert.Equal(monitor, DisplayResolutionPolicy.AutomaticRenderSize(
            monitor, new DisplayDimensions(1600, 900), fullscreen: true));
        Assert.Equal(new DisplayDimensions(1600, 900), DisplayResolutionPolicy.AutomaticRenderSize(
            monitor, new DisplayDimensions(1600, 900), fullscreen: false));
    }

    [Theory]
    [InlineData("seek_food", "looking for food", "looking for food")]
    [InlineData("build:building:sha256:abcdef/building/stone-hearth@1.0.0", "build:building:sha256:abcdef/building/stone-hearth@1.0.0", "build Stone hearth")]
    [InlineData(null, null, "taking in the surroundings")]
    public void RosterActivitiesReadAsShortPhrases(string? candidate, string? summary, string expected)
    {
        Assert.Equal(expected, GameUiText.ActivityPhrase(candidate, summary));
    }

    [Fact]
    public void PartyNamesUseAgentHouseholdAndTownNamesInsteadOfIds()
    {
        var position = new OwnerWorldPosition(0, 0);
        var agent = new OwnerWorldInhabitant("founder:1", "Rowan", "active", position, 8_000, [], [],
            new OwnerWorldRoute("idle", null, null, [], string.Empty),
            new OwnerWorldSpatialKnowledge(position, [position], [position]), false);
        var snapshot = new OwnerWorldSnapshot("names", 0, "names-map", [], [], [], null, 0)
        {
            Inhabitants = [agent],
            Stockpiles = [new OwnerWorldStockpile("household:camp-alpha", "Camp Alpha", [])],
            Towns = [new OwnerWorldTown("town:first", "First Town", "founding", 0, [], [], [])],
        };
        Assert.Equal("Rowan", GameUiText.PartyName(snapshot, "founder:1"));
        Assert.Equal("Camp Alpha", GameUiText.PartyName(snapshot, "household:camp-alpha"));
        Assert.Equal("First Town", GameUiText.PartyName(snapshot, "town:first"));
        Assert.Equal("a household", GameUiText.PartyName(snapshot, "household:agent:123"));
        Assert.DoesNotContain("household:", GameUiText.PartyName(null, "household:camp-beta"), StringComparison.Ordinal);
    }
}
