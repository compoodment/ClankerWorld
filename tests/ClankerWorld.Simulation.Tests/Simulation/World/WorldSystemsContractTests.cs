using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldSystemsContractTests
{
    [Theory]
    [InlineData(5)]
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
