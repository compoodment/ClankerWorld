using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class WeatherWeightBoundaryTests
{
    [Theory]
    [InlineData(100, ClimateZone.Cold, 40)]
    [InlineData(100, ClimateZone.Polar, 80)]
    [InlineData(1_000_000_000, ClimateZone.Cold, 400_000_000)]
    [InlineData(1_000_000_000, ClimateZone.Polar, 800_000_000)]
    [InlineData(int.MaxValue, ClimateZone.Cold, 858_993_458)]
    [InlineData(int.MaxValue, ClimateZone.Polar, 1_717_986_917)]
    public void AcceptedRainWeightsMatchTheDeclaredClimateSplit(int rain, ClimateZone climate, int expectedSnow)
    {
        var config = Config(0, rain, 0);
        config.Validate();
        var expected = config with
        {
            WeatherProfiles = Enum.GetValues<SeasonKind>()
                .Select(season => new WeatherProfile(season, 0, 0, rain - expectedSnow, 0, expectedSnow)).ToArray(),
        };
        expected.Validate();
        for (var day = 0; day < 256; day++)
            Assert.Equal(
                WeatherRules.WeatherForRegion("weather-weight-boundary", day, SeasonKind.Spring, expected, 0, 0, 4, ClimateZone.Temperate),
                WeatherRules.WeatherForRegion("weather-weight-boundary", day, SeasonKind.Spring, config, 0, 0, 4, climate));
    }

    [Theory]
    [InlineData(97)]
    [InlineData(int.MaxValue - 3)]
    public void StormCooldownKeepsCloudyOnlyProfileCloudyAfterSaveReload(int cloudy)
    {
        var state = WorldSystemsRules.CreateGenesis("weather-episode-overflow-audit", Config(cloudy, 0, 3)) with
        {
            RegionalWeather = new RegionalWeatherState(1, 1, 1,
                [new RegionalWeatherEpisode(0, 0, ClimateZone.Temperate, WeatherKind.Cloudy, 0, 1, 16)]),
        };
        var restored = WorldSystemsCodec.Decode(WorldSystemsCodec.Encode(state));
        for (var tick = 1; tick <= 64; tick++)
        {
            state = WorldSystemsRules.AdvanceOneTick(state);
            restored = WorldSystemsRules.AdvanceOneTick(restored);
            if (tick < 16)
                Assert.Equal(WeatherKind.Cloudy, Assert.Single(restored.RegionalWeather!.Episodes).Weather);
            Assert.Equal(WorldSystemsCodec.Encode(state), WorldSystemsCodec.Encode(restored));
            if (tick == 8) restored = WorldSystemsCodec.Decode(WorldSystemsCodec.Encode(restored));
        }
    }

    [Theory]
    [InlineData(ClimateZone.Cold, 458_993_458)]
    [InlineData(ClimateZone.Polar, 917_986_917)]
    public void MaximumCombinedRainAndSnowKeepsExistingSnow(ClimateZone climate, int shiftedSnow)
    {
        const int rain = 1_147_483_647;
        const int snow = 1_000_000_000;
        var config = Config(0, 0, 0) with
        {
            WeatherProfiles = Enum.GetValues<SeasonKind>()
                .Select(season => new WeatherProfile(season, 0, 0, rain, 0, snow)).ToArray(),
        };
        var expected = config with
        {
            WeatherProfiles = Enum.GetValues<SeasonKind>()
                .Select(season => new WeatherProfile(season, 0, 0, rain - shiftedSnow, 0, snow + shiftedSnow)).ToArray(),
        };
        config.Validate();
        expected.Validate();
        for (var day = 0; day < 256; day++)
            Assert.Equal(
                WeatherRules.WeatherForRegion("weather-combined-boundary", day, SeasonKind.Winter, expected, 0, 0, 4, ClimateZone.Temperate),
                WeatherRules.WeatherForRegion("weather-combined-boundary", day, SeasonKind.Winter, config, 0, 0, 4, climate));
    }

    private static WorldSystemsConfig Config(int cloudy, int rain, int storm) => new(
        TicksPerDay: 16, DaysPerYear: 40, SpringDays: 10, SummerDays: 10, AutumnDays: 10, WinterDays: 10,
        WeatherProfiles: Enum.GetValues<SeasonKind>()
            .Select(season => new WeatherProfile(season, 0, cloudy, rain, storm, 0)).ToArray());
}
