using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class CalendarOffsetTests
{
    [Theory]
    [InlineData(0, 0, 90, 0, SeasonKind.Spring)]
    [InlineData(269, 0, 359, 0, SeasonKind.Spring)]
    [InlineData(270, 1, 0, 1, SeasonKind.Spring)]
    [InlineData(3509, 9, 359, 9, SeasonKind.Spring)]
    [InlineData(3510, 10, 0, 0, SeasonKind.Summer)]
    [InlineData(14309, 39, 359, 9, SeasonKind.Winter)]
    [InlineData(14310, 40, 0, 0, SeasonKind.Spring)]
    public void MorningOffsetMovesCivilMidnightSeasonAndYearWithoutChangingElapsedTicks(
        long tick, long day, int tickOfDay, int dayOfSeason, SeasonKind season)
    {
        var calendar = WorldCalendarRules.FromTick(tick, Config());
        Assert.Equal(tick, calendar.WorldTick);
        Assert.Equal(day, calendar.DayIndex);
        Assert.Equal(day % 40, calendar.DayOfYear);
        Assert.Equal(tickOfDay, calendar.TickOfDay);
        Assert.Equal(dayOfSeason, calendar.DayOfSeason);
        Assert.Equal(season, calendar.Season);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(360, 90)]
    [InlineData(int.MaxValue, int.MaxValue - 1)]
    public void CalendarHandlesLargestRawTickWithoutOverflow(int ticksPerDay, int offset)
    {
        var config = Config() with { TicksPerDay = ticksPerDay, CalendarOffsetTicks = offset };
        var calendar = WorldCalendarRules.FromTick(long.MaxValue, config);
        var civilTick = new BigInteger(long.MaxValue) + offset;
        Assert.Equal(long.MaxValue, calendar.WorldTick);
        Assert.Equal((long)(civilTick / ticksPerDay), calendar.DayIndex);
        Assert.Equal((int)(civilTick % ticksPerDay), calendar.TickOfDay);
        Assert.Equal((int)(civilTick / ticksPerDay % 40), calendar.DayOfYear);
    }

    [Fact]
    public void OffsetChangesDaylightPhaseButNotEachCalendarDaysNight()
    {
        var state = WorldSystemsRules.CreateGenesis("offset-daylight", Config());
        var darkness = Enumerable.Range(0, 360)
            .Select(tick => DaylightRules.DarknessBasisPoints(state with { WorldTick = tick })).ToArray();
        Assert.Equal(0, darkness[0]);
        Assert.Equal(DaylightRules.FullDarkness, darkness[270]);
        // Elapsed tick 0 is 06:00 on the first calendar day, whose night is 40%; from midnight
        // the next calendar day's own seasonal night applies.
        Assert.Equal(DaylightRules.NightShareBasisPoints, DaylightRules.NightShare(Config(), 0));
        Assert.All(Enumerable.Range(0, 360), tick => Assert.Equal(
            DaylightRules.DarknessBasisPoints((tick + 90) % 360, 360, DaylightRules.NightShare(Config(), (tick + 90) / 360)),
            darkness[tick]));
    }

    [Fact]
    public void SavedOffsetReplaysAcrossMidnightSeasonsAndYearWithMatchingWeather()
    {
        var config = new WorldSystemsConfig(TicksPerDay: 8, DaysPerYear: 8,
            SpringDays: 2, SummerDays: 2, AutumnDays: 2, WinterDays: 2, CalendarOffsetTicks: 2);
        var original = WorldSystemsRules.CreateGenesis("offset-replay", config);
        var replay = WorldSystemsCodec.Decode(WorldSystemsCodec.Encode(original));
        for (var tick = 0; tick < 64; tick++)
        {
            var calendar = WorldCalendarRules.FromTick(tick, config);
            Assert.Equal(WeatherRules.WeatherForDay(original.WorldSeed, calendar.DayIndex, calendar.Season, config),
                original.Climate.Weather);
            Assert.Equal(WorldSystemsCodec.Encode(original), WorldSystemsCodec.Encode(replay));
            original = WorldSystemsRules.AdvanceOneTick(original);
            replay = WorldSystemsRules.AdvanceOneTick(replay);
            if (tick is 5 or 13 or 61)
                replay = WorldSystemsCodec.Decode(WorldSystemsCodec.Encode(replay));
        }
        Assert.Equal(64, original.WorldTick);
        Assert.Equal(8, WorldCalendarRules.FromTick(original.WorldTick, config).DayIndex);
        Assert.Equal(SeasonKind.Spring, original.Climate.Season);
        Assert.Equal(2, replay.Config.CalendarOffsetTicks);
    }

    [Fact]
    public void OffsetMustFitOneDayAndRequiresItsSaveSchema()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Config() with { CalendarOffsetTicks = -1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Config() with { CalendarOffsetTicks = 360 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldCalendarRules.FromTick(-1, Config()));
        var morning = WorldSystemsRules.CreateGenesis("offset-schema", Config());
        foreach (var oldSchema in new[] { 1, 2 })
        {
            Assert.Throws<InvalidDataException>(() => WorldSystemsCodec.Encode(morning with { SchemaVersion = oldSchema }));
            var legacy = WorldSystemsRules.CreateGenesis("offset-schema", Config() with { CalendarOffsetTicks = 0 }) with
            {
                SchemaVersion = oldSchema,
            };
            var bytes = WorldSystemsCodec.Encode(legacy);
            Assert.DoesNotContain("calendarOffsetTicks", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(bytes, WorldSystemsCodec.Encode(WorldSystemsCodec.Decode(bytes)));
        }
        var invalid = JsonNode.Parse(WorldSystemsCodec.Encode(morning))!;
        invalid["state"]!["config"]!["calendarOffsetTicks"] = 360;
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSystemsCodec.Decode(Encoding.UTF8.GetBytes(invalid.ToJsonString())));
    }

    private static WorldSystemsConfig Config() => new(TicksPerDay: 360, DaysPerYear: 40,
        SpringDays: 10, SummerDays: 10, AutumnDays: 10, WinterDays: 10, CalendarOffsetTicks: 90);
}
