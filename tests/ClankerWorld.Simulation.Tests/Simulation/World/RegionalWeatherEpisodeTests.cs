using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class RegionalWeatherEpisodeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OldWorldKeepsItsStormAndEpisodeBoundsSurvivePauseAndReload()
    {
        using var seed = new PrivateWorldRuntime("episode-owner-boundary");
        var old = seed.ExportState();
        old = old with
        {
            SchemaVersion = 25,
            Society = old.Society with
            {
                Society = old.Society.Society with
                {
                    Config = old.Society.Society.Config with { TicksPerWorldDay = 16 },
                    Inhabitants = old.Society.Society.Inhabitants.Select(person => person with
                    { BirthTick = person.BirthTick / old.Society.Society.Config.TicksPerWorldDay * 16 }).ToArray(),
                }
            },
            WorldSystems = old.WorldSystems! with
            {
                SchemaVersion = 1,
                RegionalWeather = null,
                Config = old.WorldSystems.Config with
                {
                    TicksPerDay = 16,
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 100, 0)).ToArray()
                },
                Climate = old.WorldSystems.Climate with { Weather = WeatherKind.Storm },
            }
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(old)));
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
    public void WetNeighborsModestlyIncreaseRainWithoutForcingIt()
    {
        var addedRain = 0;
        var remainedDry = 0;
        for (var seed = 0; seed < 512; seed++)
        {
            var state = RegionalWeatherRules.Initialize(WorldSystemsRules.CreateGenesis($"neighbor-{seed}", Config()), Map(ClimateZone.Temperate));
            var dry = state with
            {
                RegionalWeather = state.RegionalWeather! with
                {
                    Episodes = state.RegionalWeather.Episodes
                .Select(item => item with { Weather = WeatherKind.Clear, StartedAt = 0, EndsAt = 4 }).ToArray()
                }
            };
            var wet = dry with
            {
                RegionalWeather = dry.RegionalWeather! with
                {
                    Episodes = dry.RegionalWeather.Episodes
                .Select(item => item.X + item.Y == 1 ? item with { Weather = WeatherKind.Rain } : item).ToArray()
                }
            };
            for (var tick = 0; tick < 4; tick++)
            {
                dry = WorldSystemsRules.AdvanceOneTick(dry);
                wet = WorldSystemsRules.AdvanceOneTick(wet);
            }
            var baseline = dry.RegionalWeather!.Episodes[0].Weather;
            var influenced = wet.RegionalWeather!.Episodes[0].Weather;
            if (baseline != WeatherKind.Rain && influenced == WeatherKind.Rain) addedRain++;
            if (influenced is WeatherKind.Clear or WeatherKind.Cloudy) remainedDry++;
        }
        Assert.InRange(addedRain, 1, 40);
        Assert.True(remainedDry > 200);
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

    [Fact]
    public void ReportPrototypeDistributionAgainstDailyBaseline()
    {
        long allBaselineWet = 0, allEpisodeWet = 0;
        output.WriteLine("Climate | baseline rain+storm % | episodes rain+storm % | baseline severe % | episodes severe % | baseline switches | episode switches");
        foreach (var climate in Enum.GetValues<ClimateZone>())
        {
            long baselineWet = 0, episodeWet = 0, baselineSevere = 0, episodeSevere = 0, baselineSwitches = 0, episodeSwitches = 0, count = 0;
            for (var seed = 0; seed < 6; seed++)
            {
                var baseline = WorldSystemsRules.CreateGenesis($"episode-distribution-{seed}", Config());
                var state = RegionalWeatherRules.Initialize(baseline, Map(climate));
                var previousBase = new Dictionary<(int, int), WeatherKind>();
                var previousEpisode = new Dictionary<(int, int), WeatherKind>();
                for (var tick = 0; tick < 40 * 16; tick++)
                {
                    foreach (var region in state.RegionalWeather!.Episodes)
                    {
                        var original = WeatherRules.At(baseline, new(region.X * 32, region.Y * 32), 128, climate);
                        var current = region.Weather;
                        var key = (region.X, region.Y);
                        if (original is WeatherKind.Rain or WeatherKind.Storm) baselineWet++;
                        if (current is WeatherKind.Rain or WeatherKind.Storm) episodeWet++;
                        if (original == WeatherKind.Storm) baselineSevere++;
                        if (current == WeatherKind.Storm) episodeSevere++;
                        if (previousBase.TryGetValue(key, out var a) && a != original) baselineSwitches++;
                        if (previousEpisode.TryGetValue(key, out var b) && b != current) episodeSwitches++;
                        previousBase[key] = original;
                        previousEpisode[key] = current;
                        count++;
                    }
                    baseline = WorldSystemsRules.AdvanceOneTick(baseline);
                    state = WorldSystemsRules.AdvanceOneTick(state);
                }
            }
            output.WriteLine(FormattableString.Invariant($"{climate} | {baselineWet * 100d / count:F2} | {episodeWet * 100d / count:F2} | {baselineSevere * 100d / count:F2} | {episodeSevere * 100d / count:F2} | {baselineSwitches} | {episodeSwitches}"));
            allBaselineWet += baselineWet;
            allEpisodeWet += episodeWet;
        }
        Assert.True(allEpisodeWet <= allBaselineWet, "This fixed prototype sample must not silently increase overall rainy time.");
    }

    private static WorldSystemsConfig Config() => new(TicksPerDay: 16, DaysPerYear: 40,
        SpringDays: 10, SummerDays: 10, AutumnDays: 10, WinterDays: 10);

    private static SeededMap Map(ClimateZone climate) => new(64, 128, 0, [], [], [], "weather-sample")
    {
        ClimateZones = Enumerable.Repeat((byte)climate, 64 * 128).ToArray(),
    };
}
