using System.Text;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldSystemsContractTests
{
    [Fact]
    public void SeasonalCalendarAndWeatherAreDeterministicAtDayAndSeasonBoundaries()
    {
        var config = SmallConfig();
        var first = WeatherRules.CreateGenesis("world-systems-seed", config);
        var second = WeatherRules.CreateGenesis("world-systems-seed", config);

        Assert.Equal(first, second);
        Assert.Equal(SeasonKind.Spring, first.Season);
        Assert.Equal(first.Weather, WeatherRules.WeatherForDay("world-systems-seed", 0, SeasonKind.Spring, config));

        var summerTick = checked((long)config.TicksPerDay * config.SpringDays);
        var summer = WeatherRules.Advance(first, summerTick, "world-systems-seed", config);
        var summerAgain = WeatherRules.Advance(first, summerTick, "world-systems-seed", config);

        Assert.Equal(SeasonKind.Summer, summer.Season);
        Assert.Equal(summer, summerAgain);
        Assert.Equal(
            summer.Weather,
            WeatherRules.WeatherForDay("world-systems-seed", config.SpringDays, SeasonKind.Summer, config));
        Assert.Equal(SeasonKind.Spring, WorldCalendarRules.FromTick(0, config).Season);
        Assert.Equal(SeasonKind.Summer, WorldCalendarRules.FromTick(summerTick, config).Season);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void SevereStormEndsWithinThreeQuartersOfEachSavedWorldDay(int ticksPerDay)
    {
        var config = SmallConfig() with
        {
            TicksPerDay = ticksPerDay,
            WeatherProfiles = Enum.GetValues<SeasonKind>()
                .Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray(),
        };
        var state = WorldSystemsRules.CreateGenesis("storm-duration", config);
        var position = new GridPoint(20, 20);
        for (var tick = 0; tick < 2 * config.TicksPerDay; tick++)
        {
            var expected = tick % config.TicksPerDay < 3 * config.TicksPerDay / 4
                ? WeatherKind.Storm : WeatherKind.Rain;
            Assert.Equal(expected, WeatherRules.At(state, position, 128));
            Assert.Equal(expected, WeatherRules.At(state, position, 32));
            if (tick < 2 * config.TicksPerDay - 1) state = WorldSystemsRules.AdvanceOneTick(state);
        }
    }

    [Fact]
    public void RecentRainRaisesLocalSoilMoistureWhileDryDaysDepleteIt()
    {
        var baseConfig = SmallConfig();
        WorldSystemsState AdvanceWith(WeatherKind weather)
        {
            var profiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season,
                weather == WeatherKind.Clear ? 1 : 0, 0, weather == WeatherKind.Rain ? 1 : 0, 0, 0)).ToArray();
            var state = WorldSystemsRules.CreateGenesis("soil-test", config: baseConfig with { WeatherProfiles = profiles });
            for (var tick = 0; tick < 8; tick++) state = WorldSystemsRules.AdvanceOneTick(state);
            return state;
        }

        var position = new GridPoint(20, 20);
        var wet = WeatherRules.SoilMoistureAt(AdvanceWith(WeatherKind.Rain), position, 128);
        var dry = WeatherRules.SoilMoistureAt(AdvanceWith(WeatherKind.Clear), position, 128);

        Assert.InRange(wet, 70, 100);
        Assert.InRange(dry, 0, 14);
        Assert.True(wet > dry);
    }

    [Fact]
    public void EcologyHarvestAndRegenerationAreBoundedAndIdempotent()
    {
        var resource = new EcologyResource(
            "berry-patch",
            "food",
            new GridPoint(2, 3),
            true,
            0,
            10,
            3,
            2,
            SeasonKind.Spring,
            0,
            EcologyResourceState.Depleted);
        var config = SmallConfig();
        var calendar = WorldCalendarRules.FromTick(0, config);

        var regenerated = EcologyRules.Regenerate(resource, calendar, config);
        var repeated = EcologyRules.Regenerate(regenerated, calendar, config);
        var harvested = EcologyRules.Harvest(regenerated, 2);

        Assert.Equal(3, regenerated.Quantity);
        Assert.Equal(EcologyResourceState.Available, regenerated.State);
        Assert.Equal(2, regenerated.NextRegenerationDay);
        Assert.Equal(regenerated, repeated);
        Assert.True(harvested.IsValid);
        Assert.Equal(1, harvested.Resource!.Quantity);
        Assert.False(EcologyRules.Harvest(regenerated, 99).IsValid);
    }

    [Fact]
    public void LawEvaluationProducesDeterministicViolationsAndUpdatesStanding()
    {
        var state = new FactionState(
            [new FactionDefinition("guild", "River Guild", ["riverfolk"])],
            [new FactionStanding("actor", "guild", 500)],
            [new LawRule("no-theft", "guild", LawActionKind.Theft, LawSeverity.Major, 250, 30, true)],
            []);
        var action = new LawAction("action-1", "actor", "guild", LawActionKind.Theft, 12);

        var evaluation = FactionRules.Evaluate(state, action);
        var committed = FactionRules.ApplyViolations(state, evaluation);
        var permitted = FactionRules.Evaluate(state, action with { HasPermit = true });

        Assert.True(evaluation.IsViolation);
        Assert.Single(evaluation.Violations);
        Assert.Equal("violation:action-1:no-theft", evaluation.Violations[0].ViolationId);
        Assert.Equal(250, committed.FindStanding("actor", "guild")!.StandingBasisPoints);
        Assert.False(permitted.IsViolation);
    }

    [Fact]
    public void CurrencyTransferRequiresOwnerFundsAndMatchingCurrency()
    {
        var state = new CurrencyState(
            [new CurrencyDefinition("copper", "Copper", "cp")],
            [
                new CurrencyAccount("alice-wallet", "alice", "copper", 100),
                new CurrencyAccount("bob-wallet", "bob", "copper", 10),
            ],
            []);
        var transfer = new CurrencyTransfer("transfer-1", "alice", "alice-wallet", "bob-wallet", "copper", 40, 1);

        var result = CurrencyRules.ApplyTransfer(state, transfer);
        var insufficient = CurrencyRules.ValidateTransfer(
            result.State!,
            transfer with { TransferId = "transfer-2", AmountMinorUnits = 1_000 });
        var forged = CurrencyRules.ValidateTransfer(
            state,
            transfer with { TransferId = "transfer-3", InitiatorId = "mallory" });

        Assert.True(result.IsValid);
        Assert.Equal(60, result.State!.GetAccount("alice-wallet").BalanceMinorUnits);
        Assert.Equal(50, result.State.GetAccount("bob-wallet").BalanceMinorUnits);
        Assert.False(insufficient.IsValid);
        Assert.False(forged.IsValid);
    }

    [Fact]
    public void CultureTagsNormalizeAndRemainDataOnly()
    {
        var tags = CultureRules.NormalizeTags(["River", "river", "trade-route"], 4);
        var culture = new CultureDefinition("riverfolk", "River Folk", tags);
        var enriched = CultureRules.AddTag(culture, "harvest", 4);

        Assert.Equal(["river", "trade-route"], tags);
        Assert.Equal(["harvest", "river", "trade-route"], enriched.Tags);
        Assert.Throws<ArgumentException>(() => CultureRules.NormalizeTag("not allowed"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CultureRules.AddTag(enriched, "winter", 3));
    }

    [Fact]
    public void ChunkCoordinatesAndManifestDigestAreCanonical()
    {
        var point = new GridPoint(17, -1);
        var coordinate = ChunkRules.ToChunkCoordinate(point);
        var local = ChunkRules.ToLocalPoint(point);
        var manifest = ChunkManifestCodec.WithDigest(new ChunkManifest(
            coordinate,
            ChunkRules.DefaultChunkSize,
            16,
            16,
            "temperate-fixture",
            "v1",
            [new ChunkResourceMetadata("berry-patch", "food", local, true)]));
        var reordered = manifest with
        {
            Resources = [new ChunkResourceMetadata("berry-patch", "food", local, true)],
        };

        Assert.Equal(new ChunkCoordinate(1, -1), coordinate);
        Assert.Equal(new GridPoint(1, 15), local);
        Assert.Equal(manifest.ManifestDigest, ChunkManifestCodec.Digest(reordered));
        Assert.Equal(manifest.ManifestDigest, ChunkManifestCodec.WithDigest(reordered).ManifestDigest);
    }

    [Fact]
    public void ComposedStateAdvancesAndRoundTripsWithStableBytes()
    {
        var config = SmallConfig();
        var chunk = ChunkManifestCodec.WithDigest(new ChunkManifest(
            new ChunkCoordinate(0, 0),
            4,
            4,
            4,
            "fixture",
            "v1",
            []));
        var resource = new EcologyResource(
            "spring-plant",
            "food",
            new GridPoint(1, 1),
            true,
            0,
            4,
            2,
            1,
            SeasonKind.Spring,
            0,
            EcologyResourceState.Depleted);
        var state = WorldSystemsRules.CreateGenesis(
            "composed-seed",
            config,
            [resource],
            chunks: [chunk]);

        var advanced = WorldSystemsRules.AdvanceOneTick(state);
        var encoded = WorldSystemsCodec.Encode(advanced);
        var decoded = WorldSystemsCodec.Decode(encoded);
        var reencoded = WorldSystemsCodec.Encode(decoded);

        Assert.Equal(1, advanced.WorldTick);
        Assert.Equal(2, advanced.Ecology.GetResource("spring-plant").Quantity);
        Assert.True(encoded.AsSpan().SequenceEqual(reencoded));
        Assert.True(Encoding.UTF8.GetString(encoded).Contains("world-systems", StringComparison.Ordinal));
    }

    private static WorldSystemsConfig SmallConfig() => new(
        TicksPerDay: 4,
        DaysPerYear: 8,
        SpringDays: 2,
        SummerDays: 2,
        AutumnDays: 2,
        WinterDays: 2,
        MaxChunkCount: 8,
        MaxResourcesPerChunk: 8,
        MaxCultureTags: 8,
        WeatherProfiles:
        [
            new WeatherProfile(SeasonKind.Spring, 1, 1, 1, 0, 0),
            new WeatherProfile(SeasonKind.Summer, 1, 1, 1, 1, 0),
            new WeatherProfile(SeasonKind.Autumn, 1, 1, 1, 0, 1),
            new WeatherProfile(SeasonKind.Winter, 1, 1, 0, 0, 1),
        ]);
}
