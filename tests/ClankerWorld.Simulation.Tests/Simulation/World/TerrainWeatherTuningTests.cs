using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class TerrainWeatherTuningTests(ITestOutputHelper output)
{
    private static readonly WeatherProfile[] PreviousWeatherProfiles =
    [
        new(SeasonKind.Spring, 45, 25, 25, 5, 0),
        new(SeasonKind.Summer, 55, 20, 15, 10, 0),
        new(SeasonKind.Autumn, 35, 30, 20, 5, 10),
        new(SeasonKind.Winter, 30, 30, 5, 5, 30),
    ];

    [Fact]
    public void CactiStayOnDesertSandAndAcceptanceRefusesThemElsewhere()
    {
        var options = Current("terrain-dry-cacti") with
        {
            ClimateMode = ClimateMode.Uniform,
            SelectedClimate = ClimateZone.Dry,
            LatitudeCooling = false,
        };
        var map = GeneratedCampMapGenerator.Generate(options);
        var geography = GeographyGenerator.Generate(options);
        var cacti = map.Tiles.Where(tile => map.VegetationAt(tile.Position) == VegetationCover.Cactus).ToArray();
        Assert.NotEmpty(cacti);
        Assert.All(cacti, tile =>
        {
            Assert.Equal(SurfaceKind.Sand, map.SurfaceAt(tile.Position));
            Assert.Equal(ClimateZone.Dry, map.ClimateAt(tile.Position));
            // A dry-climate beach is not enough: cacti require the actual
            // rainfall-based desert rule as well.
            Assert.InRange(geography.At(tile.Position.X, tile.Position.Y).Rainfall, 0, 42);
        });
        var balanced = GeneratedCampMapGenerator.Generate(Current("probe-a"));
        foreach (var point in new[]
                 {
                     map.Tiles.First(tile => map.SurfaceAt(tile.Position) == SurfaceKind.DryScrub).Position,
                     map.Tiles.First(tile => map.HydrologyAt(tile.Position) == WaterKind.Ocean).Position,
                 })
        {
            var cover = map.VegetationKinds!.ToArray();
            cover[point.Y * map.Width + point.X] = (byte)VegetationCover.Cactus;
            var misplaced = map with { VegetationKinds = cover };
            Assert.False(MapAcceptance.Validate(misplaced, allowEmptyCamp: true).IsValid);
        }
        var beach = balanced.Tiles.First(tile => balanced.SurfaceAt(tile.Position) == SurfaceKind.Sand &&
            balanced.ClimateAt(tile.Position) != ClimateZone.Dry).Position;
        var beachCover = balanced.VegetationKinds!.ToArray();
        beachCover[beach.Y * balanced.Width + beach.X] = (byte)VegetationCover.Cactus;
        Assert.False(MapAcceptance.Validate(balanced with { VegetationKinds = beachCover }, allowEmptyCamp: true).IsValid);
    }

    [Fact]
    public void ActiveWeatherEpisodesReduceWetTimeAcrossClimates()
    {
        var current = WorldSystemsConfig.Default with
        {
            TicksPerDay = 16,
            DaysPerYear = 40,
            SpringDays = 10,
            SummerDays = 10,
            AutumnDays = 10,
            WinterDays = 10,
        };
        var previous = current with { WeatherProfiles = PreviousWeatherProfiles };
        foreach (var climate in Enum.GetValues<ClimateZone>())
        {
            var map = new SeededMap(64, 128, 0, [], [], [], "weather-tuning")
            {
                ClimateZones = Enumerable.Repeat((byte)climate, 64 * 128).ToArray(),
            };
            long before = 0, after = 0;
            for (var seed = 0; seed < 4; seed++)
            {
                var original = RegionalWeatherRules.Initialize(WorldSystemsRules.CreateGenesis($"quarter-episode-{seed}", previous), map);
                var reduced = RegionalWeatherRules.Initialize(WorldSystemsRules.CreateGenesis($"quarter-episode-{seed}", current), map);
                for (var tick = 0; tick < 160 * current.TicksPerDay; tick++)
                {
                    before += original.RegionalWeather!.Episodes.Count(episode => IsWet(episode.Weather));
                    after += reduced.RegionalWeather!.Episodes.Count(episode => IsWet(episode.Weather));
                    original = WorldSystemsRules.AdvanceOneTick(original);
                    reduced = WorldSystemsRules.AdvanceOneTick(reduced);
                }
            }
            output.WriteLine($"{climate}: wet episode time {before} -> {after}; ratio {(double)after / before:F3}");
            Assert.InRange((double)after / before, 0.60, 0.90);
        }
    }

    private static bool IsWet(WeatherKind weather) => weather is WeatherKind.Rain or WeatherKind.Storm or WeatherKind.Snow;

    private static GeographyOptions Current(string seed) =>
        new(seed, WorldSizePreset.Small, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);

}
