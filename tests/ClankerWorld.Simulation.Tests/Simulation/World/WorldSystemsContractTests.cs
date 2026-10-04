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

    [Theory]
    [InlineData(4_000)]
    public void EcologyLookupReadsEachResourceAboutOnceAsTheWorldGrows(int count)
    {
        // A tick looks up thousands of sources. Searching the list for each one
        // made Small-world ticks grow with the square of the resource count.
        var resources = new CountingResourceList(Enumerable.Range(0, count).Select(index => new EcologyResource(
            $"tree-{index:D5}", "construction", new GridPoint(index % 256, index / 256), false, 1, 1, 0, 0,
            SeasonKind.Spring, 0, EcologyResourceState.Available)).ToArray());
        var ecology = new EcologyState(resources);

        for (var index = count - 1; index >= 0; index--)
            Assert.Same(resources.Items[index], ecology.GetResource($"tree-{index:D5}"));

        Assert.InRange(resources.Reads, count, 2 * count);
    }

    [Fact]
    public void EcologyLookupFollowsReplacedResourcesAndKeepsLookupErrors()
    {
        var first = new EcologyResource("tree-a", "construction", new GridPoint(1, 1), true, 1, 1, 1, 4,
            SeasonKind.Spring, 4, EcologyResourceState.Available);
        var second = first with { Id = "tree-b", Position = new GridPoint(2, 1) };
        var ecology = new EcologyState([first, second]);
        Assert.Same(second, ecology.GetResource("tree-b"));

        // `with` copies the state; the copy must find its own list's resources.
        var felled = second with { Quantity = 0, State = EcologyResourceState.Regenerating };
        var replaced = ecology with { Resources = [felled, first] };
        Assert.Same(felled, replaced.GetResource("tree-b"));
        Assert.Same(first, replaced.GetResource("tree-a"));
        Assert.Same(second, ecology.GetResource("tree-b"));

        Assert.Throws<InvalidOperationException>(() => ecology.GetResource("tree-c"));
        Assert.Throws<InvalidOperationException>(() => new EcologyState([first, second, first]).GetResource("tree-a"));
        Assert.Same(second, new EcologyState([first, second, first]).GetResource("tree-b"));
    }

    private sealed class CountingResourceList(EcologyResource[] items) : IReadOnlyList<EcologyResource>
    {
        public EcologyResource[] Items { get; } = items;
        public int Reads { get; private set; }
        public int Count => Items.Length;

        public EcologyResource this[int index]
        {
            get
            {
                Reads++;
                return Items[index];
            }
        }

        public IEnumerator<EcologyResource> GetEnumerator()
        {
            foreach (var item in Items)
            {
                Reads++;
                yield return item;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
