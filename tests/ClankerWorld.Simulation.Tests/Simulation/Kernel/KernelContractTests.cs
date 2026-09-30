using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class KernelContractTests
{
    [Fact]
    public void ClockCalendarUsesOnlySavedIntegralTickArithmetic()
    {
        var afterOneYearAndOneMinute = KernelClock.FromWorldTick((KernelClock.TicksPerDay * KernelClock.DaysPerYear) + 1);

        Assert.Equal(365, afterOneYearAndOneMinute.DayIndex);
        Assert.Equal(0, afterOneYearAndOneMinute.DayOfYear);
        Assert.Equal(1, afterOneYearAndOneMinute.MinuteOfDay);
        Assert.Equal(6, KernelClock.ScheduledTicksPerSecond);
        Assert.Equal(1, KernelClock.TicksPerMinute);
    }

    [Fact]
    public void NamedPcgStreamIsStableAndCannotAccidentallyAliasAnotherStream()
    {
        var first = Pcg32XshRrV1.Create("fixture-seed", "weather");
        var same = Pcg32XshRrV1.Create("fixture-seed", "weather");
        var other = Pcg32XshRrV1.Create("fixture-seed", "resource:berry-patch");
        var actual = Enumerable.Range(0, 5).Select(_ => first.NextUInt()).ToArray();

        Assert.Equal("pcg32-xsh-rr-v1", Pcg32XshRrV1.AlgorithmId);
        Assert.Equal(
            new uint[] { 2_746_376_332, 2_393_862_774, 1_216_660_101, 2_255_332_717, 1_720_709_021 },
            actual);
        Assert.Equal(actual, Enumerable.Range(0, 5).Select(_ => same.NextUInt()).ToArray());
        Assert.NotEqual(actual, Enumerable.Range(0, 5).Select(_ => other.NextUInt()).ToArray());
    }
}
