using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class EcologyBatchValidationTests
{
    private static readonly WorldCalendar Calendar = new(0, 0, 0, 0, 0, SeasonKind.Spring);

    [Fact]
    public void EcologyBatchReadsWeatherProfilesOnceInsteadOfOncePerResource()
    {
        var profiles = new CountingWeatherProfiles(Profiles());
        var config = Config(profiles);
        var resources = Enumerable.Range(0, 64).Select(index => Resource($"stone-{index:D3}")).Reverse().ToArray();

        var result = EcologyRules.Advance(new(resources), Calendar, config);

        // The allowance covers profile uniqueness and weight checks without
        // depending on their exact implementation. It must not grow with the map.
        Assert.InRange(profiles.Reads, 4, 16);
        Assert.Equal(resources.OrderBy(resource => resource.Id, StringComparer.Ordinal), result.Resources);
        Assert.All(result.Resources, resource => Assert.Equal(1, resource.Quantity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LaterBatchesAndStandaloneRegenerationRejectChangedProfileInputs(int corruption)
    {
        var profiles = Profiles().ToList();
        var config = Config(profiles);
        var resource = Resource("stone");
        var ecology = new EcologyState([resource]);
        EcologyRules.Advance(ecology, Calendar, config);

        switch (corruption)
        {
            case 0: profiles.RemoveAt(3); break;
            case 1: profiles[3] = profiles[0]; break;
            case 2: profiles[0] = profiles[0] with { ClearWeight = -1 }; break;
        }

        // Reuse the same config object with its changed mutable profile list.
        Assert.ThrowsAny<ArgumentException>(() => EcologyRules.Advance(ecology, Calendar, config));
        Assert.ThrowsAny<ArgumentException>(() => EcologyRules.Regenerate(resource, Calendar, config));
    }

    [Fact]
    public void EcologyBatchKeepsResourceBoundsAndValidationBeforeEarlyReturns()
    {
        var config = Config(Profiles());
        var first = Resource("stone-a");
        var invalid = Resource("stone-b") with { Quantity = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => EcologyRules.Advance(new([first, invalid]), Calendar, config));
        Assert.Throws<ArgumentOutOfRangeException>(() => EcologyRules.Regenerate(invalid, Calendar, config));
        Assert.Throws<ArgumentException>(() => EcologyRules.Advance(new([first, first]), Calendar, config));
        Assert.Throws<ArgumentOutOfRangeException>(() => EcologyRules.Advance(new([first, Resource("stone-b")]), Calendar,
            config with { MaxResourcesPerChunk = 1 }));
    }

    private static EcologyResource Resource(string id) => new(id, "stone", new GridPoint(1, 1), false,
        1, 1, 0, 0, SeasonKind.Spring, 0, EcologyResourceState.Available);

    private static WeatherProfile[] Profiles() => Enum.GetValues<SeasonKind>()
        .Select(season => new WeatherProfile(season, 1, 1, 1, 0, 0)).ToArray();

    private static WorldSystemsConfig Config(IReadOnlyList<WeatherProfile> profiles) => WorldSystemsConfig.Default with
    {
        MaxChunkCount = 1,
        MaxResourcesPerChunk = 128,
        WeatherProfiles = profiles,
    };

    private sealed class CountingWeatherProfiles(WeatherProfile[] profiles) : IReadOnlyList<WeatherProfile>
    {
        public int Reads { get; private set; }
        public int Count => profiles.Length;
        public WeatherProfile this[int index]
        {
            get { Reads++; return profiles[index]; }
        }

        public IEnumerator<WeatherProfile> GetEnumerator()
        {
            foreach (var profile in profiles)
            {
                Reads++;
                yield return profile;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
