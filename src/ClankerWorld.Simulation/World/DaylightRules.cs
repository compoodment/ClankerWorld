namespace ClankerWorld.Simulation.World;

/// <summary>
/// Time of day, derived only from the saved world tick and the world's saved
/// calendar; nothing about it is saved. Night follows the seasons (agreed on
/// October 3, replacing #641's same-all-year night): it is 30% of the day on
/// the first day of summer, 50% on the first day of winter and 40% on the
/// first days of spring and autumn, changing evenly from day to day in
/// between. Night is centred on midnight of the 24-hour clock the game shows,
/// so at 40% it runs from 19:12 to 04:48. Dusk and dawn each fade over the
/// clock hour centred on the start and end of night, so the darker half of
/// each fade counts as night and night covers exactly its share of the day.
/// Only whole-number arithmetic is used, so every platform agrees on every
/// tick.
/// </summary>
public static class DaylightRules
{
    /// <summary>Darkness at full night; full daylight is zero.</summary>
    public const int FullDarkness = 10_000;

    /// <summary>Clock minutes in one world day, whatever its length in ticks.</summary>
    public const int ClockMinutesPerDay = 1_440;

    /// <summary>Night's share of the day on the first days of spring and autumn, and over a whole year, in basis points.</summary>
    public const int NightShareBasisPoints = 4_000;

    /// <summary>Night's share of the day on the first day of summer, the shortest night.</summary>
    public const int ShortestNightShareBasisPoints = 3_000;

    /// <summary>Night's share of the day on the first day of winter, the longest night.</summary>
    public const int LongestNightShareBasisPoints = 5_000;

    /// <summary>Clock minutes that each dusk or dawn fade takes, centred on the start or end of night.</summary>
    public const int FadeClockMinutes = 60;

    /// <summary>
    /// How dark the world is at a tick when night takes
    /// <paramref name="nightShareBasisPoints"/> of the day: zero in daylight,
    /// <see cref="FullDarkness"/> at night, and a straight fade between them
    /// at dusk and dawn.
    /// </summary>
    public static int DarknessBasisPoints(long worldTick, int ticksPerDay, int nightShareBasisPoints = NightShareBasisPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        ArgumentOutOfRangeException.ThrowIfLessThan(nightShareBasisPoints, 2 * FadeClockMinutes * FullDarkness / ClockMinutesPerDay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nightShareBasisPoints, FullDarkness - 2 * FadeClockMinutes * FullDarkness / ClockMinutesPerDay);
        var tickOfDay = worldTick % ticksPerDay;
        // Clock minutes from the nearest midnight, kept multiplied by the day's
        // ticks and by twice FullDarkness, so days that do not divide the clock
        // evenly and night shares that do not give whole minutes stay exact.
        const long scale = 2L * FullDarkness;
        var fromMidnight = Math.Min(tickOfDay, ticksPerDay - tickOfDay) * ClockMinutesPerDay * scale;
        var halfNight = (long)ClockMinutesPerDay * nightShareBasisPoints * ticksPerDay;
        var halfFade = FadeClockMinutes / 2 * scale * ticksPerDay;
        var darkUntil = halfNight - halfFade;
        var lightFrom = halfNight + halfFade;
        if (fromMidnight <= darkUntil) return FullDarkness;
        if (fromMidnight >= lightFrom) return 0;
        return (int)((lightFrom - fromMidnight) * FullDarkness / (lightFrom - darkUntil));
    }

    /// <summary>
    /// Night's share of world day <paramref name="dayIndex"/>: shortest on the
    /// first day of summer, longest on the first day of winter, the yearly
    /// average on the first days of spring and autumn, and an even daily step
    /// between them across each season of the world's own length.
    /// </summary>
    public static int NightShare(WorldSystemsConfig config, long dayIndex)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);
        var day = (int)(dayIndex % config.DaysPerYear);
        var (from, to, start, length) = day switch
        {
            _ when day < config.SpringDays =>
                (NightShareBasisPoints, ShortestNightShareBasisPoints, 0, config.SpringDays),
            _ when day < config.SpringDays + config.SummerDays =>
                (ShortestNightShareBasisPoints, NightShareBasisPoints, config.SpringDays, config.SummerDays),
            _ when day < config.SpringDays + config.SummerDays + config.AutumnDays =>
                (NightShareBasisPoints, LongestNightShareBasisPoints, config.SpringDays + config.SummerDays, config.AutumnDays),
            _ => (LongestNightShareBasisPoints, NightShareBasisPoints,
                config.SpringDays + config.SummerDays + config.AutumnDays, config.WinterDays),
        };
        return from + (to - from) * (day - start) / length;
    }

    /// <summary>How dark the world is now, with that day's seasonal night length.</summary>
    public static int DarknessBasisPoints(WorldSystemsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var ticksPerDay = state.Config.TicksPerDay;
        return DarknessBasisPoints(state.WorldTick, ticksPerDay,
            NightShare(state.Config, state.WorldTick / ticksPerDay));
    }
}
