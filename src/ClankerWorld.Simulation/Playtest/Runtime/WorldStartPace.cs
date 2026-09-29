using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>New-world rules only; existing saves retain their saved configs.</summary>
public enum WorldStartPace
{
    Legacy,
    DecidedPlaytest,
    FounderSetup,
}

internal static class WorldStartPaceRules
{
    public static WorldSystemsConfig WorldSystems(WorldStartPace pace) => pace switch
    {
        WorldStartPace.Legacy => WorldSystemsConfig.Default,
        WorldStartPace.DecidedPlaytest or WorldStartPace.FounderSetup => WorldSystemsConfig.Default with
        {
            ContractVersion = 2,
            TicksPerDay = 360,
            DaysPerYear = 40,
            SpringDays = 10,
            SummerDays = 10,
            AutumnDays = 10,
            WinterDays = 10,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(pace)),
    };

    public static SocietyConfig Society(WorldStartPace pace) => pace switch
    {
        WorldStartPace.Legacy => new SocietyConfig(),
        WorldStartPace.DecidedPlaytest or WorldStartPace.FounderSetup => new SocietyConfig(
            TicksPerWorldDay: 360,
            DaysPerWorldYear: 40,
            ContractVersion: 3,
            DayLifecycle: new SocietyDayLifecycle()),
        _ => throw new ArgumentOutOfRangeException(nameof(pace)),
    };
}
