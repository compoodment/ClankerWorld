namespace ClankerWorld.Simulation.World;

/// <summary>
/// Time of day, derived only from the saved world tick and the world's ticks
/// per day; nothing about it is saved. Night is 40% of every day, the same
/// all year (agreed in #641), centred on midnight of the 24-hour clock the
/// game shows: it runs from 19:12 to 04:48. Dusk and dawn each fade over the
/// clock hour centred on those times (18:42–19:42 and 04:18–05:18), so the
/// darker half of each fade counts as night and night covers exactly 40% of
/// the day.
/// </summary>
public static class DaylightRules
{
    /// <summary>Darkness at full night; full daylight is zero.</summary>
    public const int FullDarkness = 10_000;

    /// <summary>Clock minutes in one world day, whatever its length in ticks.</summary>
    public const int ClockMinutesPerDay = 1_440;

    /// <summary>Night's share of every world day, in basis points.</summary>
    public const int NightShareBasisPoints = 4_000;

    /// <summary>Clock minutes that each dusk or dawn fade takes, centred on the start or end of night.</summary>
    public const int FadeClockMinutes = 60;

    private const int NightHalfClockMinutes = ClockMinutesPerDay * NightShareBasisPoints / FullDarkness / 2;

    /// <summary>
    /// How dark the world is at a tick: zero in daylight, <see cref="FullDarkness"/>
    /// at night, and a straight fade between them at dusk and dawn.
    /// </summary>
    public static int DarknessBasisPoints(long worldTick, int ticksPerDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        var tickOfDay = worldTick % ticksPerDay;
        // Clock minutes from the nearest midnight, kept multiplied by the
        // day's ticks so days that do not divide the clock evenly stay exact.
        var fromMidnight = Math.Min(tickOfDay, ticksPerDay - tickOfDay) * ClockMinutesPerDay;
        var darkUntil = (long)(NightHalfClockMinutes - FadeClockMinutes / 2) * ticksPerDay;
        var lightFrom = (long)(NightHalfClockMinutes + FadeClockMinutes / 2) * ticksPerDay;
        if (fromMidnight <= darkUntil) return FullDarkness;
        if (fromMidnight >= lightFrom) return 0;
        return (int)((lightFrom - fromMidnight) * FullDarkness / (lightFrom - darkUntil));
    }

    public static int DarknessBasisPoints(WorldSystemsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return DarknessBasisPoints(state.WorldTick, state.Config.TicksPerDay);
    }
}
