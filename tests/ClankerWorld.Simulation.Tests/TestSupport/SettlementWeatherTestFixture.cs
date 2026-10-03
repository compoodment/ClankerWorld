using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

internal static class SettlementWeatherTestFixture
{
    /// <summary>
    /// Worlds start at midnight. Advances a runtime to the next full daylight
    /// so a fixture about weather alone is not also measuring the night chill.
    /// </summary>
    internal static async Task AdvanceToDaylightAsync(PrivateWorldRuntime world)
    {
        while (DaylightRules.DarknessBasisPoints(world.WorldSystems) > 0)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    }

    internal static PrivateWorldRuntimeState WithWeather(PrivateWorldRuntimeState state, WeatherKind weather)
    {
        var systems = state.WorldSystems!;
        var profiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season,
            weather == WeatherKind.Clear ? 1 : 0, 0, weather == WeatherKind.Rain ? 1 : 0,
            weather == WeatherKind.Storm ? 1 : 0, weather == WeatherKind.Snow ? 1 : 0)).ToArray();
        var configured = systems with
        {
            RegionalWeather = null,
            Config = systems.Config with { WeatherProfiles = profiles },
            Climate = systems.Climate with { Weather = weather },
        };
        var regions = RegionalWeatherRules.Initialize(configured, state.Map).RegionalWeather!;
        var duration = weather == WeatherKind.Storm ? (long)systems.Config.TicksPerDay * 3 / 4 : systems.Config.TicksPerDay;
        var endsAt = checked(systems.WorldTick + duration);
        var halfDay = ((long)systems.Config.TicksPerDay + 1) / 2;
        return state with
        {
            WorldSystems = configured with
            {
                RegionalWeather = regions with
                {
                    Episodes = regions.Episodes.Select(episode => episode with
                    {
                        Weather = weather,
                        StartedAt = systems.WorldTick,
                        EndsAt = endsAt,
                        SevereAllowedAt = weather == WeatherKind.Storm ? checked(endsAt + halfDay) : checked(systems.WorldTick + halfDay),
                    }).ToArray(),
                },
            }
        };
    }
}
