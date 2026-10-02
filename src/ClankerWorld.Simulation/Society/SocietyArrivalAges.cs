using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Society;

public static partial class SocietyFixture
{
    /// <summary>
    /// Founders and added adults in day-lifecycle worlds arrive up to this many
    /// days past the adult threshold: day 15 to day 25 in playtest worlds.
    /// </summary>
    public const int ArrivalAgeSpreadDays = 10;

    /// <summary>The oldest age a founder or added adult can arrive at, still below elder age.</summary>
    public static int LatestArrivalAge(SocietyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.DayLifecycle is { } days
            ? Math.Min(days.AdultStartDay + ArrivalAgeSpreadDays, days.ElderStartDay - 1)
            : config.FounderStartingAge;
    }

    /// <summary>
    /// The starting age of the founder placed at this index, counting from
    /// zero. Each world seed orders the arrival days once, so founders get
    /// different ages and the same seed gives the same ages whatever their
    /// IDs. Year-based worlds keep the adult age.
    /// </summary>
    public static int FounderArrivalAge(SocietyConfig config, string worldSeed, int placementIndex)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfNegative(placementIndex);
        config.Validate();
        var youngest = config.FounderStartingAge;
        var ages = Enumerable.Range(youngest, LatestArrivalAge(config) - youngest + 1)
            .OrderBy(age => Pcg32XshRrV1.Create(worldSeed,
                FormattableString.Invariant($"society/founder-arrival-age:{age}")).NextUInt())
            .ThenBy(age => age)
            .ToArray();
        // Founders beyond the number of arrival days repeat the same order.
        return ages[placementIndex % ages.Length];
    }

    // Keyed by arrival order rather than the client-chosen agent ID, so the
    // world seed and its history decide the age.
    private static int AddedAdultArrivalAge(SocietyCheckpoint checkpoint)
    {
        var youngest = checkpoint.Config.FounderStartingAge;
        var random = Pcg32XshRrV1.Create(checkpoint.WorldId,
            FormattableString.Invariant($"society/adult-arrival-age:{checkpoint.Inhabitants.Count}"));
        return youngest + (int)(random.NextUInt() % (uint)(LatestArrivalAge(checkpoint.Config) - youngest + 1));
    }
}
