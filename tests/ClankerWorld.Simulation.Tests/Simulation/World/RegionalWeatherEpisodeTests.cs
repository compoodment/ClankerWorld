using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class RegionalWeatherEpisodeTests
{
    [Fact]
    public async Task CurrentWorldCanKeepItsStormWithoutRegionalWeatherEpisodes()
    {
        using var seed = new PrivateWorldRuntime("episode-owner-boundary");
        var initialState = seed.ExportState();
        initialState = initialState with
        {
            Society = initialState.Society with
            {
                Society = initialState.Society.Society with
                {
                    Config = initialState.Society.Society.Config with { TicksPerWorldDay = 16 },
                    Inhabitants = initialState.Society.Society.Inhabitants.Select(person => person with
                    { BirthTick = person.BirthTick / initialState.Society.Society.Config.TicksPerWorldDay * 16 }).ToArray(),
                }
            },
            WorldSystems = initialState.WorldSystems! with
            {
                SchemaVersion = 1,
                RegionalWeather = null,
                Config = initialState.WorldSystems.Config with
                {
                    TicksPerDay = 16,
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 100, 0)).ToArray()
                },
                Climate = initialState.WorldSystems.Climate with { Weather = WeatherKind.Storm },
            }
        };
        using var world = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(initialState)));
        world.Pause();
        Assert.Null(world.ExportState().WorldSystems!.RegionalWeather);
        Assert.All(new OwnerWorldObservationStore(world).GetSnapshot().WeatherRegions, region => Assert.Equal("storm", region.Weather));
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Null(world.ExportState().WorldSystems!.RegionalWeather);
        world.Resume();
        long lastStormEnd = 0;
        var storms = new HashSet<long>();
        for (var tick = 1; tick <= 64; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var state = world.ExportState();
            var episode = Assert.Single(state.WorldSystems!.RegionalWeather!.Episodes);
            Assert.All(new OwnerWorldObservationStore(world).GetSnapshot().WeatherRegions,
                region => Assert.Equal(episode.Weather.ToString().ToLowerInvariant(), region.Weather));
            if (episode.Weather == WeatherKind.Storm)
            {
                Assert.InRange(episode.EndsAt - episode.StartedAt, 1, 12);
                if (storms.Add(episode.StartedAt))
                {
                    if (lastStormEnd > 0) Assert.True(episode.StartedAt >= lastStormEnd + 8);
                    lastStormEnd = episode.EndsAt;
                }
            }
            if (tick is 11 or 12 or 19 or 20)
            {
                world.Pause();
                var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
                using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
                Assert.Equal(JsonSerializer.Serialize(world.ExportState().WorldSystems), JsonSerializer.Serialize(restored.ExportState().WorldSystems));
                Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
                world.Resume();
                restored.Resume();
                Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
                var predicted = WorldSystemsRules.AdvanceOneTick(state.WorldSystems);
                Assert.Equal(JsonSerializer.Serialize(predicted.RegionalWeather), JsonSerializer.Serialize(restored.ExportState().WorldSystems!.RegionalWeather));
            }
        }
        Assert.True(storms.Count >= 2);
    }

    [Fact]
    public void RegionOrderAndObservationDoNotChangeSavedFutureTransitions()
    {
        var state = RegionalWeatherRules.Initialize(WorldSystemsRules.CreateGenesis("weather-order", Config()), Map(ClimateZone.Temperate));
        var shuffled = state with { RegionalWeather = state.RegionalWeather! with { Episodes = state.RegionalWeather.Episodes.Reverse().ToArray() } };
        var differed = false;
        for (var tick = 0; tick < 160; tick++)
        {
            foreach (var region in state.RegionalWeather!.Episodes)
                _ = WeatherRules.At(state, new(region.X * 32, region.Y * 32), 128);
            state = WorldSystemsRules.AdvanceOneTick(state);
            shuffled = WorldSystemsRules.AdvanceOneTick(shuffled);
            Assert.Equal(JsonSerializer.Serialize(state.RegionalWeather), JsonSerializer.Serialize(shuffled.RegionalWeather));
            differed |= state.RegionalWeather!.Episodes.Select(item => item.Weather).Distinct().Count() > 1;
            if (tick == 79) shuffled = WorldSystemsCodec.Decode(WorldSystemsCodec.Encode(shuffled));
        }
        Assert.True(differed);
    }

    [Fact]
    public void InvalidEpisodeBoundsAndUnsupportedVersionAreRejectedBySaveCodec()
    {
        var state = RegionalWeatherRules.Initialize(WorldSystemsRules.CreateGenesis("episode-validation", Config()), Map(ClimateZone.Temperate));
        Assert.Throws<InvalidDataException>(() => WorldSystemsCodec.Encode(state with
        { RegionalWeather = state.RegionalWeather! with { Version = 99 } }));
        Assert.Throws<InvalidDataException>(() => WorldSystemsCodec.Encode(state with
        {
            RegionalWeather = state.RegionalWeather! with
            {
                Episodes = state.RegionalWeather.Episodes
            .Select(item => item with { EndsAt = 0 }).ToArray()
            }
        }));
        Assert.Throws<InvalidDataException>(() => WorldSystemsCodec.Encode(state with
        {
            RegionalWeather = state.RegionalWeather! with
            {
                Episodes = state.RegionalWeather.Episodes
            .Select(item => item with { Weather = WeatherKind.Storm, StartedAt = 0, EndsAt = 13, SevereAllowedAt = 21 }).ToArray()
            }
        }));
    }

    private static WorldSystemsConfig Config() => new(TicksPerDay: 16, DaysPerYear: 40,
        SpringDays: 10, SummerDays: 10, AutumnDays: 10, WinterDays: 10);

    private static SeededMap Map(ClimateZone climate) => new(64, 128, 0, [], [], [], "weather-sample")
    {
        ClimateZones = Enumerable.Repeat((byte)climate, 64 * 128).ToArray(),
    };
}
