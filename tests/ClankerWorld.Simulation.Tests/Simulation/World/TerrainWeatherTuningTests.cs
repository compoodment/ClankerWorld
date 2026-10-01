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

    [Theory]
    [InlineData("probe-a")]
    [InlineData("probe-b")]
    public void NewForestsHaveManyTreesAndCoastsHaveSeparateBeachStretches(string seed)
    {
        var map = GeneratedCampMapGenerator.Generate(Current(seed));
        var trees = map.Resources.Where(resource => resource.TreeKind is not null)
            .Select(resource => resource.Position).ToHashSet();
        var forest = map.Tiles.Select(tile => tile.Position).Where(point =>
            map.SurfaceAt(point) == SurfaceKind.Grass && map.VegetationAt(point) == VegetationCover.Forest).ToArray();
        Assert.NotEmpty(forest);
        Assert.InRange(forest.Count(trees.Contains) * 100 / forest.Length, 25, 45);
        Assert.All(map.Tiles.Where(tile => map.SurfaceAt(tile.Position) == SurfaceKind.ForestFloor),
            tile => Assert.Contains(tile.Position, trees));

        var shore = map.Tiles.Select(tile => tile.Position).Where(point =>
            map.HydrologyAt(point) == WaterKind.Land && map.ClimateAt(point) is not (ClimateZone.Dry or ClimateZone.Cold or ClimateZone.Polar) &&
            map.ElevationAt(point) < 175 && BesideOcean(map, point)).ToArray();
        Assert.NotEmpty(shore);
        Assert.InRange(shore.Count(point => map.SurfaceAt(point) == SurfaceKind.Sand) * 100 / shore.Length, 1, 20);
        Assert.Contains(shore, point => map.SurfaceAt(point) == SurfaceKind.Grass);
        Assert.True(MapAcceptance.Validate(map, allowEmptyCamp: true).IsValid);
    }

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
    public void DefaultWeatherHasAboutOneQuarterFewerWetDaysInEveryClimateAndSeason()
    {
        var previous = WorldSystemsConfig.Default with { WeatherProfiles = PreviousWeatherProfiles };
        foreach (var season in Enum.GetValues<SeasonKind>())
        {
            AssertReduction(day => WeatherRules.WeatherForDay("weather-quarter", day, season, WorldSystemsConfig.Default),
                day => WeatherRules.WeatherForDay("weather-quarter", day, season, previous));
            foreach (var climate in Enum.GetValues<ClimateZone>())
                foreach (var row in new[] { 0, 2 })
                    AssertReduction(day => WeatherRules.WeatherForRegion("weather-quarter", day, season,
                            WorldSystemsConfig.Default, 0, row, 5, climate),
                        day => WeatherRules.WeatherForRegion("weather-quarter", day, season, previous, 0, row, 5, climate));
        }
    }

    [Fact]
    public void SavedDefaultAndExplicitWeatherProfilesKeepTheirRolls()
    {
        foreach (var explicitProfiles in new[] { false, true })
        {
            var config = WorldSystemsConfig.Default with { WeatherProfiles = explicitProfiles ? PreviousWeatherProfiles : null };
            var saved = WorldSystemsRules.CreateGenesis("weather-config-reload", config);
            var encoded = WorldSystemsCodec.Encode(saved);
            var loaded = WorldSystemsCodec.Decode(encoded);
            Assert.Equal(encoded, WorldSystemsCodec.Encode(loaded));
            if (explicitProfiles) Assert.Equal(PreviousWeatherProfiles, loaded.Config.WeatherProfiles);
            else Assert.Null(loaded.Config.WeatherProfiles);
            foreach (var season in Enum.GetValues<SeasonKind>())
                foreach (var day in Enumerable.Range(0, 64))
                {
                    Assert.Equal(WeatherRules.WeatherForDay(saved.WorldSeed, day, season, saved.Config),
                        WeatherRules.WeatherForDay(loaded.WorldSeed, day, season, loaded.Config));
                    Assert.Equal(WeatherRules.WeatherForRegion(saved.WorldSeed, day, season, saved.Config, 0, 0, 5, ClimateZone.Tropical),
                        WeatherRules.WeatherForRegion(loaded.WorldSeed, day, season, loaded.Config, 0, 0, 5, ClimateZone.Tropical));
                }
        }
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

    [Fact]
    public void ExplicitWeatherProfilesKeepTheirDeclaredWeights()
    {
        var forced = WorldSystemsConfig.Default with
        {
            WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 100, 0)).ToArray(),
        };
        foreach (var climate in Enum.GetValues<ClimateZone>().Where(climate => climate != ClimateZone.Dry))
            foreach (var day in Enumerable.Range(0, 32))
            {
                Assert.Equal(WeatherKind.Storm, WeatherRules.WeatherForDay("forced-storm", day, SeasonKind.Spring, forced));
                Assert.Equal(WeatherKind.Storm, WeatherRules.WeatherForRegion("forced-storm", day, SeasonKind.Spring,
                    forced, 0, 0, 5, climate));
            }
        // Dry climates already divert half of an explicitly configured storm
        // weight to clear weather. The new default tuning must not reduce that
        // custom distribution again.
        var dryStorms = Enumerable.Range(0, 8_000).Count(day => WeatherRules.WeatherForRegion("forced-storm", day,
            SeasonKind.Spring, forced, 0, 0, 5, ClimateZone.Dry) == WeatherKind.Storm);
        Assert.InRange(dryStorms, 3_680, 4_320);
    }

    private static void AssertReduction(Func<int, WeatherKind> current, Func<int, WeatherKind> previous)
    {
        const int samples = 8_000;
        var before = Enumerable.Range(0, samples).Count(day => IsWet(previous(day)));
        var after = Enumerable.Range(0, samples).Count(day => IsWet(current(day)));
        Assert.True(before > 0);
        Assert.InRange((double)after / before, 0.69, 0.81);
    }

    private static bool IsWet(WeatherKind weather) => weather is WeatherKind.Rain or WeatherKind.Storm or WeatherKind.Snow;

    private static GeographyOptions Current(string seed) =>
        new(seed, WorldSizePreset.Small, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion);

    private static bool BesideOcean(SeededMap map, GridPoint point) =>
        new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }.Any(offset =>
        {
            var x = point.X + offset.Item1;
            if (map.WrapsEastWest) x = (x % map.Width + map.Width) % map.Width;
            var near = new GridPoint(x, point.Y + offset.Item2);
            return map.Contains(near) && map.HydrologyAt(near) == WaterKind.Ocean;
        });
}
