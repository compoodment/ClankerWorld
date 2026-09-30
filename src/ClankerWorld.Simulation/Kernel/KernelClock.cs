namespace ClankerWorld.Simulation.Kernel;

/// <summary>
/// Saved integer clock arithmetic. No host wall-clock data participates.
/// </summary>
public sealed record KernelClock(long WorldTick, long DayIndex, int DayOfYear, int MinuteOfDay)
{
    public const int TicksPerMinute = 1;
    public const int TicksPerDay = 1_440;
    public const int DaysPerYear = 365;
    public const int ScheduledTicksPerSecond = 6;

    public static KernelClock FromWorldTick(long worldTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);

        var dayIndex = worldTick / TicksPerDay;
        return new KernelClock(
            worldTick,
            dayIndex,
            (int)(dayIndex % DaysPerYear),
            (int)(worldTick % TicksPerDay));
    }
}
