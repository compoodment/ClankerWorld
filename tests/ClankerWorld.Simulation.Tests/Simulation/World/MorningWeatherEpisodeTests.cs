using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class MorningWeatherEpisodeTests
{
    [Theory]
    [InlineData(0, 0, 0, 270)]
    [InlineData(0, 50, 0, 270)]
    [InlineData(90, 0, 0, 180)]
    [InlineData(90, 50, 0, 180)]
    [InlineData(90, 179, 0, 180)]
    [InlineData(90, 300, 270, 540)]
    public void ImportedStormKeepsOnlyTheRemainingCivilDayAllowance(
        int offset, int rawTick, long expectedStart, long expectedEnd)
    {
        var state = AtRawTick(offset, rawTick);
        var map = WeatherMap();
        Assert.Equal(WeatherKind.Storm, WeatherRules.At(state, new(0, 0), map.Height));

        var initialized = RegionalWeatherRules.Initialize(state, map);
        var episode = Assert.Single(initialized.RegionalWeather!.Episodes);

        Assert.Equal(rawTick, initialized.WorldTick);
        Assert.Equal(WeatherKind.Storm, episode.Weather);
        Assert.Equal(expectedStart, episode.StartedAt);
        Assert.Equal(expectedEnd, episode.EndsAt);
        Assert.InRange(episode.StartedAt, 0, initialized.WorldTick);
        Assert.Equal(270, WorldCalendarRules.FromTick(episode.EndsAt, initialized.Config).TickOfDay);
        Assert.Equal(180, episode.SevereAllowedAt - episode.EndsAt);
        RegionalWeatherRules.ValidateMap(initialized, map);
        var bytes = WorldSystemsCodec.Encode(initialized);
        var restored = WorldSystemsCodec.Decode(bytes);
        Assert.Equal(bytes, WorldSystemsCodec.Encode(restored));
        Assert.Same(restored, RegionalWeatherRules.Initialize(restored, map));
        Assert.Equal(WorldSystemsCodec.Encode(WorldSystemsRules.AdvanceOneTick(initialized)),
            WorldSystemsCodec.Encode(WorldSystemsRules.AdvanceOneTick(restored)));
    }

    [Fact]
    public void MorningStormEndsAtCivilEveningAndItsCooldownSurvivesReload()
    {
        var state = RegionalWeatherRules.Initialize(AtRawTick(90, 0), WeatherMap());
        var initial = Assert.Single(state.RegionalWeather!.Episodes);
        Assert.Equal(90, WorldCalendarRules.FromTick(state.WorldTick, state.Config).TickOfDay);
        Assert.Equal(0, initial.StartedAt);
        Assert.Equal(180, initial.EndsAt);
        Assert.Equal(360, initial.SevereAllowedAt);
        while (state.WorldTick < 90)
            state = WorldSystemsRules.AdvanceOneTick(state);
        var bytes = WorldSystemsCodec.Encode(state);
        var restored = WorldSystemsCodec.Decode(bytes);
        Assert.Equal(bytes, WorldSystemsCodec.Encode(restored));

        while (state.WorldTick < initial.SevereAllowedAt - 1)
        {
            state = WorldSystemsRules.AdvanceOneTick(state);
            restored = WorldSystemsRules.AdvanceOneTick(restored);
            var episode = Assert.Single(state.RegionalWeather!.Episodes);
            var restoredEpisode = Assert.Single(restored.RegionalWeather!.Episodes);
            Assert.Equal(episode, restoredEpisode);
            if (state.WorldTick < initial.EndsAt)
            {
                Assert.Equal(initial, episode);
                Assert.Equal(WeatherKind.Storm, WeatherRules.At(state, new(0, 0), 32));
            }
            else
            {
                Assert.NotEqual(WeatherKind.Storm, episode.Weather);
                Assert.Equal(initial.SevereAllowedAt, episode.SevereAllowedAt);
            }
            if (state.WorldTick == initial.EndsAt)
            {
                Assert.Equal(270, WorldCalendarRules.FromTick(state.WorldTick, state.Config).TickOfDay);
                Assert.Equal(WeatherKind.Cloudy, episode.Weather);
                Assert.Equal(initial.EndsAt, episode.StartedAt);
            }
        }

        Assert.Equal(WorldSystemsCodec.Encode(state), WorldSystemsCodec.Encode(restored));
    }

    [Theory]
    [InlineData(0, 270)]
    [InlineData(90, 180)]
    [InlineData(359, 0)]
    public void InitializationAfterTheCivilStormCutoffDoesNotRestartAStorm(int offset, int rawTick)
    {
        var state = AtRawTick(offset, rawTick);
        var map = WeatherMap();
        Assert.Equal(WeatherKind.Storm, state.Climate.Weather);
        Assert.Equal(WeatherKind.Rain, WeatherRules.At(state, new(0, 0), map.Height));

        var initialized = RegionalWeatherRules.Initialize(state, map);
        var episode = Assert.Single(initialized.RegionalWeather!.Episodes);

        Assert.Equal(WeatherKind.Rain, episode.Weather);
        Assert.Equal(rawTick, episode.StartedAt);
        Assert.True(episode.EndsAt > rawTick);
        RegionalWeatherRules.ValidateMap(initialized, map);
        var bytes = WorldSystemsCodec.Encode(initialized);
        Assert.Equal(bytes, WorldSystemsCodec.Encode(WorldSystemsCodec.Decode(bytes)));
    }

    private static WorldSystemsState AtRawTick(int offset, int rawTick)
    {
        var config = new WorldSystemsConfig(TicksPerDay: 360, DaysPerYear: 40,
            SpringDays: 10, SummerDays: 10, AutumnDays: 10, WinterDays: 10,
            WeatherProfiles: Enum.GetValues<SeasonKind>()
                .Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray(),
            CalendarOffsetTicks: offset);
        var state = WorldSystemsRules.CreateGenesis("morning-weather-episode", config);
        while (state.WorldTick < rawTick)
            state = WorldSystemsRules.AdvanceOneTick(state);
        return state;
    }

    private static SeededMap WeatherMap() => new(32, 32, 0, [], [], [], "morning-weather-map");
}
