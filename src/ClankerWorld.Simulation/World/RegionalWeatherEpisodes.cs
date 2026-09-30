using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.World;

public sealed record RegionalWeatherEpisode(int X, int Y, ClimateZone? Climate, WeatherKind Weather,
    long StartedAt, long EndsAt, long SevereAllowedAt);

public sealed record RegionalWeatherState(int Version, int Columns, int Rows,
    IReadOnlyList<RegionalWeatherEpisode> Episodes, bool WrapsEastWest = false);

/// <summary>Provisional regional episodes for owner playtesting, not approved rain balance.</summary>
public static class RegionalWeatherRules
{
    public static WorldSystemsState Initialize(WorldSystemsState state, SeededMap map)
    {
        if (state.RegionalWeather is not null) return state;
        var columns = (map.Width + WeatherRules.RegionSize - 1) / WeatherRules.RegionSize;
        var rows = (map.Height + WeatherRules.RegionSize - 1) / WeatherRules.RegionSize;
        var episodes = new List<RegionalWeatherEpisode>();
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                var position = new GridPoint(x * WeatherRules.RegionSize, y * WeatherRules.RegionSize);
                var climate = WeatherRules.RegionClimate(map, position);
                var weather = WeatherRules.At(state, position, map.Height, climate);
                var start = state.WorldTick;
                var duration = Duration(state, x, y, start, weather);
                // Import the current daily storm's original age; migration must
                // not restart its maximum three-quarter-day allowance.
                if (weather == WeatherKind.Storm)
                {
                    start -= state.WorldTick % state.Config.TicksPerDay;
                    duration = (int)((long)state.Config.TicksPerDay * 3 / 4);
                }
                var end = checked(start + duration);
                episodes.Add(new(x, y, climate, weather, start, end,
                    weather == WeatherKind.Storm ? checked(end + HalfDay(state)) : checked(state.WorldTick + HalfDay(state))));
            }
        return state with { SchemaVersion = WorldSystemsRules.SchemaVersion, RegionalWeather = new(1, columns, rows, episodes, map.WrapsEastWest) };
    }

    public static RegionalWeatherState? Advance(WorldSystemsState state, long tick)
    {
        if (state.RegionalWeather is not { } regions) return null;
        // All transitions see the same pre-transition snapshot, including when
        // several neighbors expire together. Neither rendering nor loop order participates.
        var snapshot = regions.Episodes.ToDictionary(item => (item.X, item.Y));
        var episodes = regions.Episodes.OrderBy(item => item.Y).ThenBy(item => item.X).Select(previous =>
        {
            if (tick < previous.EndsAt) return previous;
            var season = WorldCalendarRules.FromTick(tick, state.Config).Season;
            var weights = WeatherRules.RegionalWeights(season, state.Config, previous.Y, regions.Rows, previous.Climate);
            // A conservative wet-weight reduction pays for the modest neighbor
            // and persistence bonuses. Distribution evidence accompanies this prototype.
            foreach (var wet in new[] { WeatherKind.Rain, WeatherKind.Storm, WeatherKind.Snow })
            {
                var reduction = weights[(int)wet] / 4;
                weights[(int)wet] -= reduction;
                weights[(int)WeatherKind.Clear] += reduction;
            }
            var wetNeighbors = new[] { (previous.X - 1, previous.Y), (previous.X + 1, previous.Y),
                (previous.X, previous.Y - 1), (previous.X, previous.Y + 1) }
                .Select(key => regions.WrapsEastWest ? ((key.Item1 + regions.Columns) % regions.Columns, key.Item2) : key)
                .Where(key => key != (previous.X, previous.Y)).Distinct()
                .Count(key => snapshot.TryGetValue(key, out var neighbor) && neighbor.Weather is WeatherKind.Rain or WeatherKind.Storm);
            var bonus = Math.Min(weights[(int)WeatherKind.Clear], wetNeighbors);
            if (weights[(int)WeatherKind.Rain] > 0)
            {
                weights[(int)WeatherKind.Rain] += bonus;
                weights[(int)WeatherKind.Clear] -= bonus;
            }
            if (previous.Weather != WeatherKind.Storm && weights[(int)previous.Weather] <= int.MaxValue - 3)
                weights[(int)previous.Weather] += 3;
            if (tick < previous.SevereAllowedAt || state.Config.TicksPerDay < 2)
            {
                weights[(int)WeatherKind.Cloudy] += weights[(int)WeatherKind.Storm];
                weights[(int)WeatherKind.Storm] = 0;
            }
            var random = Pcg32XshRrV1.Create(state.WorldSeed, FormattableString.Invariant($"weather/episode:{tick}/region:{previous.X},{previous.Y}/choice"));
            var roll = (long)random.NextUInt() % weights.Sum(weight => (long)weight);
            var selected = WeatherKind.Clear;
            foreach (var weather in Enum.GetValues<WeatherKind>())
            {
                roll -= weights[(int)weather];
                if (roll < 0) { selected = weather; break; }
            }
            var end = checked(tick + Duration(state, previous.X, previous.Y, tick, selected));
            return previous with
            {
                Weather = selected,
                StartedAt = tick,
                EndsAt = end,
                SevereAllowedAt = selected == WeatherKind.Storm ? checked(end + HalfDay(state)) : previous.SevereAllowedAt
            };
        }).ToArray();
        return regions with { Episodes = episodes };
    }

    public static void ValidateMap(WorldSystemsState state, SeededMap map)
    {
        if (state.RegionalWeather is not { } regions) return;
        if (regions.Columns != (map.Width + WeatherRules.RegionSize - 1) / WeatherRules.RegionSize ||
            regions.Rows != (map.Height + WeatherRules.RegionSize - 1) / WeatherRules.RegionSize ||
            regions.WrapsEastWest != map.WrapsEastWest)
            throw new InvalidDataException("Regional weather does not match the saved map.");
    }

    public static void Validate(RegionalWeatherState? regions, long tick, WorldSystemsConfig config)
    {
        if (regions is null) return;
        if (regions.Version != 1 || regions.Columns <= 0 || regions.Rows <= 0 ||
            (long)regions.Columns * regions.Rows > 65_536 || regions.Episodes is null ||
            regions.Episodes.Count != (long)regions.Columns * regions.Rows ||
            regions.Episodes.Any(item => item is null) ||
            regions.Episodes.Select(item => (item.X, item.Y)).Distinct().Count() != regions.Episodes.Count)
            throw new InvalidDataException("Regional weather topology is invalid.");
        foreach (var item in regions.Episodes)
            if (item.X < 0 || item.X >= regions.Columns || item.Y < 0 || item.Y >= regions.Rows ||
                !Enum.IsDefined(item.Weather) || item.Climate is { } climate && !Enum.IsDefined(climate) ||
                item.StartedAt < 0 || item.StartedAt > tick || item.EndsAt <= tick || item.SevereAllowedAt < 0 ||
                item.EndsAt - item.StartedAt > config.TicksPerDay ||
                item.Weather == WeatherKind.Storm && (item.EndsAt - item.StartedAt > (long)config.TicksPerDay * 3 / 4 ||
                    item.SevereAllowedAt < item.EndsAt + ((long)config.TicksPerDay + 1) / 2))
                throw new InvalidDataException("Regional weather episode is invalid.");
    }

    private static long HalfDay(WorldSystemsState state) => ((long)state.Config.TicksPerDay + 1) / 2;

    private static int Duration(WorldSystemsState state, int x, int y, long tick, WeatherKind weather)
    {
        var minimum = Math.Max(1, (int)(((long)state.Config.TicksPerDay + 3) / 4));
        var maximum = weather == WeatherKind.Storm ? Math.Max(1, (int)((long)state.Config.TicksPerDay * 3 / 4)) : state.Config.TicksPerDay;
        var random = Pcg32XshRrV1.Create(state.WorldSeed, FormattableString.Invariant($"weather/episode:{tick}/region:{x},{y}/duration"));
        return minimum + (int)(random.NextUInt() % (uint)(maximum - minimum + 1));
    }
}
