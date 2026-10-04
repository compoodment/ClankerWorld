using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class DaylightRulesTests
{
    [Theory]
    [InlineData(360)]
    [InlineData(1_440)]
    public void AFortyPercentNightCoversItsShareAndTheDarkerHalfOfEachFadeCountsAsNight(int ticksPerDay)
    {
        foreach (var day in new long[] { 0, 1, 17, 39, 40, 365 })
        {
            var darkness = Enumerable.Range(0, ticksPerDay)
                .Select(tick => DaylightRules.DarknessBasisPoints(day * ticksPerDay + tick, ticksPerDay)).ToArray();
            var darker = darkness.Count(value => value > DaylightRules.FullDarkness / 2);
            var balanced = darkness.Count(value => value == DaylightRules.FullDarkness / 2);
            // Ticks exactly at 19:12 and 04:48 are half dark; each counts as half a night tick.
            Assert.Equal(ticksPerDay * 2 * DaylightRules.NightShareBasisPoints / DaylightRules.FullDarkness,
                darker * 2 + balanced);
            Assert.Contains(darkness, value => value == 0);
            Assert.Contains(darkness, value => value == DaylightRules.FullDarkness);
        }
    }

    [Theory]
    [InlineData(24)]
    [InlineData(96)]
    [InlineData(360)]
    [InlineData(999)]
    [InlineData(1_000)]
    [InlineData(1_440)]
    [InlineData(1_441)]
    public void TotalDarknessOverADayMatchesTheNightShareForAnySavedDayLength(int ticksPerDay)
    {
        var total = Enumerable.Range(0, ticksPerDay)
            .Sum(tick => (long)DaylightRules.DarknessBasisPoints(tick, ticksPerDay));
        var expected = (long)ticksPerDay * DaylightRules.NightShareBasisPoints;
        // Sampling the fades once a tick and rounding each step can miss by
        // less than one tick of full night, never more.
        Assert.InRange(total, expected - DaylightRules.FullDarkness, expected + DaylightRules.FullDarkness);
    }

    [Fact]
    public void SixMinuteDayFollowsTheShownClock()
    {
        // 360 ticks a day: one tick is four clock minutes, so tick 288 is 19:12.
        static int At(int hour, int minute) => DaylightRules.DarknessBasisPoints((hour * 60 + minute) / 4, 360);

        Assert.Equal(DaylightRules.FullDarkness, At(0, 0));
        Assert.Equal(DaylightRules.FullDarkness, At(4, 16));
        Assert.Equal(DaylightRules.FullDarkness / 2, At(4, 48));
        Assert.Equal(0, At(5, 20));
        Assert.Equal(0, At(12, 0));
        Assert.Equal(0, At(18, 40));
        Assert.Equal(DaylightRules.FullDarkness / 2, At(19, 12));
        Assert.Equal(DaylightRules.FullDarkness, At(19, 44));
        Assert.Equal(DaylightRules.FullDarkness, At(23, 56));

        // 216 seconds of daylight and 144 of night at one tick per second.
        var night = Enumerable.Range(0, 360).Count(tick => DaylightRules.DarknessBasisPoints(tick, 360) > 5_000) +
            Enumerable.Range(0, 360).Count(tick => DaylightRules.DarknessBasisPoints(tick, 360) == 5_000) / 2;
        Assert.Equal(144, night);
    }

    [Fact]
    public void DuskAndDawnFadeStepByStepWithinOneClockHour()
    {
        const int ticksPerDay = 1_440;
        var dusk = Enumerable.Range(18 * 60, 120).Select(tick => DaylightRules.DarknessBasisPoints(tick, ticksPerDay)).ToArray();
        var dawn = Enumerable.Range(4 * 60, 120).Select(tick => DaylightRules.DarknessBasisPoints(tick, ticksPerDay)).ToArray();

        Assert.True(dusk.Zip(dusk.Skip(1)).All(pair => pair.Second >= pair.First), "Dusk must only darken.");
        Assert.True(dawn.Zip(dawn.Skip(1)).All(pair => pair.Second <= pair.First), "Dawn must only lighten.");
        Assert.Equal(DaylightRules.FadeClockMinutes - 1,
            dusk.Count(value => value is > 0 and < DaylightRules.FullDarkness));
        Assert.Equal(DaylightRules.FadeClockMinutes - 1,
            dawn.Count(value => value is > 0 and < DaylightRules.FullDarkness));
        // Dawn mirrors dusk around midnight.
        Assert.All(Enumerable.Range(-60, 121), offset => Assert.Equal(
            DaylightRules.DarknessBasisPoints(19 * 60 + 12 + offset, ticksPerDay),
            DaylightRules.DarknessBasisPoints(4 * 60 + 48 - offset, ticksPerDay)));
    }

    private static readonly WorldSystemsConfig PlaytestYear = WorldSystemsConfig.Default with
    {
        TicksPerDay = 360,
        DaysPerYear = 40,
        SpringDays = 10,
        SummerDays = 10,
        AutumnDays = 10,
        WinterDays = 10,
    };

    [Fact]
    public void NightIsShortestAtTheStartOfSummerAndLongestAtTheStartOfWinter()
    {
        Assert.Equal(4_000, DaylightRules.NightShare(PlaytestYear, 0));
        Assert.Equal(3_500, DaylightRules.NightShare(PlaytestYear, 5));
        Assert.Equal(3_000, DaylightRules.NightShare(PlaytestYear, 10));
        Assert.Equal(4_000, DaylightRules.NightShare(PlaytestYear, 20));
        Assert.Equal(5_000, DaylightRules.NightShare(PlaytestYear, 30));
        Assert.Equal(4_100, DaylightRules.NightShare(PlaytestYear, 39));
        // The next year repeats the first.
        Assert.Equal(4_000, DaylightRules.NightShare(PlaytestYear, 40));
        Assert.Equal(3_000, DaylightRules.NightShare(PlaytestYear, 50));

        // Every day moves by the same small step, with no jump between seasons or years.
        var shares = Enumerable.Range(0, 81).Select(day => DaylightRules.NightShare(PlaytestYear, day)).ToArray();
        Assert.All(shares.Zip(shares.Skip(1)), pair => Assert.Equal(100, Math.Abs(pair.Second - pair.First)));
    }

    [Fact]
    public void NightLengthFollowsEachWorldsOwnSeasonLengths()
    {
        var legacy = WorldSystemsConfig.Default;
        Assert.Equal(4_000, DaylightRules.NightShare(legacy, 0));
        Assert.Equal(3_000, DaylightRules.NightShare(legacy, legacy.SpringDays));
        Assert.Equal(4_000, DaylightRules.NightShare(legacy, legacy.SpringDays + legacy.SummerDays));
        Assert.Equal(5_000, DaylightRules.NightShare(legacy, legacy.SpringDays + legacy.SummerDays + legacy.AutumnDays));
        Assert.Equal(5_000 - 1_000 * (legacy.WinterDays - 1) / legacy.WinterDays,
            DaylightRules.NightShare(legacy, legacy.DaysPerYear - 1));
        // Over a whole year night still averages 40% of the day.
        var average = Enumerable.Range(0, legacy.DaysPerYear).Average(day => DaylightRules.NightShare(legacy, day));
        Assert.InRange(average, 3_990, 4_010);
    }

    [Fact]
    public void SummerAndWinterNightsFollowTheShownClock()
    {
        // 360 ticks a day: one tick is four clock minutes.
        static int At(int share, int hour, int minute) => DaylightRules.DarknessBasisPoints((hour * 60 + minute) / 4, 360, share);

        // First day of summer: night 20:24 to 03:36, fading 19:54-20:54 and 03:06-04:06.
        Assert.Equal(0, At(3_000, 19, 52));
        Assert.Equal(DaylightRules.FullDarkness / 2, At(3_000, 20, 24));
        Assert.Equal(DaylightRules.FullDarkness, At(3_000, 21, 0));
        Assert.Equal(DaylightRules.FullDarkness / 2, At(3_000, 3, 36));
        Assert.Equal(0, At(3_000, 4, 8));
        // First day of winter: night 18:00 to 06:00, fading 17:30-18:30 and 05:30-06:30.
        Assert.Equal(0, At(5_000, 17, 28));
        Assert.Equal(DaylightRules.FullDarkness / 2, At(5_000, 18, 0));
        Assert.Equal(DaylightRules.FullDarkness, At(5_000, 5, 28));
        Assert.Equal(DaylightRules.FullDarkness / 2, At(5_000, 6, 0));
        Assert.Equal(0, At(5_000, 6, 32));

        static int NightTicks(int share) =>
            Enumerable.Range(0, 360).Count(tick => DaylightRules.DarknessBasisPoints(tick, 360, share) > 5_000) +
            Enumerable.Range(0, 360).Count(tick => DaylightRules.DarknessBasisPoints(tick, 360, share) == 5_000) / 2;
        Assert.Equal(108, NightTicks(3_000));
        Assert.Equal(180, NightTicks(5_000));
    }

    [Fact]
    public void TheWorldsDarknessUsesTodaysSeasonalNight()
    {
        var summer = WorldSystemsRules.CreateGenesis("seasonal-night", PlaytestYear) with { WorldTick = 10 * 360 + (19 * 60) / 4 };
        var spring = summer with { WorldTick = (19 * 60) / 4 };
        var winter = summer with { WorldTick = 30 * 360 + (19 * 60) / 4 };

        // 19:00 is still daylight at the start of summer, dusk at the start of spring and full night in winter.
        Assert.Equal(0, DaylightRules.DarknessBasisPoints(summer));
        Assert.InRange(DaylightRules.DarknessBasisPoints(spring), 1, DaylightRules.FullDarkness - 1);
        Assert.Equal(DaylightRules.FullDarkness, DaylightRules.DarknessBasisPoints(winter));
    }

    [Fact]
    public void RejectsImpossibleClockValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DaylightRules.DarknessBasisPoints(-1, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaylightRules.DarknessBasisPoints(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaylightRules.DarknessBasisPoints(0, 360, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaylightRules.DarknessBasisPoints(0, 360, DaylightRules.FullDarkness));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaylightRules.NightShare(PlaytestYear, -1));
        Assert.Throws<ArgumentNullException>(() => DaylightRules.DarknessBasisPoints(null!));
    }
}
