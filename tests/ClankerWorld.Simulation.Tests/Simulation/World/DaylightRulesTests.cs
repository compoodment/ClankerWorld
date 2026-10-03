using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class DaylightRulesTests
{
    [Theory]
    [InlineData(360)]
    [InlineData(1_440)]
    public void NightIsFortyPercentOfEveryDayAndDarkerHalfOfEachFadeCountsAsNight(int ticksPerDay)
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

    [Fact]
    public void RejectsImpossibleClockValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DaylightRules.DarknessBasisPoints(-1, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaylightRules.DarknessBasisPoints(0, 0));
        Assert.Throws<ArgumentNullException>(() => DaylightRules.DarknessBasisPoints(null!));
    }
}
