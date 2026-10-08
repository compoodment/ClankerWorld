using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class WildForageIndexTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(WorldSizePreset.Small)]
    [InlineData(WorldSizePreset.Medium)]
    public async Task GeneratedForageSearchPreservesOrderedChoicesWithoutMapSizedPredicateAllocations(WorldSizePreset size)
    {
        using var world = NormalPathWorld.CreateGenerated("wild-forage-cost-audit",
            _ => new ActionCoverageRecorder(chooseIdle: true), size: size);
        for (var tick = 0; tick < 12; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        var map = state.Map;
        var ecology = state.WorldSystems!.Ecology;
        var animals = state.AnimalWorld.Animals.Where(animal => animal.DiedTick is null && animal.HouseholdId is null).ToArray();
        Assert.Equal(8, animals.Length);
        string[][] Search(bool original, EcologyState? currentEcology = null) => animals.Select(animal => (original
                ? map.Resources.Where(source => source.NaturalObjectKind == "wild_greens" &&
                        ecology.Resources.Any(resource => resource.Id == source.Id && resource.Quantity >= AnimalRules.Definition(animal.Species).DailyFeed) &&
                        map.FootDistance(animal.Position, source.Position) <= 12)
                    .OrderBy(source => map.FootDistance(animal.Position, source.Position)).ThenBy(source => source.Id, StringComparer.Ordinal)
                : PrivateWorldRuntime.WildAnimalForageSources(map, currentEcology ?? ecology, animal))
            .Select(source => source.Id).ToArray()).ToArray();
        var expected = Search(true);
        var actual = Search(false);
        for (var index = 0; index < animals.Length; index++) Assert.Equal(expected[index], actual[index]);
        var baseline = Measure(() => Search(true));
        var current = Measure(() => Search(false));
        var cold = Measure(() => Search(false, ecology with { }));
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Size = size.ToString(),
            map.Width,
            map.Height,
            Resources = map.Resources.Count,
            Ecology = ecology.Resources.Count,
            Animals = animals.Length,
            OrderedCandidates = actual,
            Baseline = baseline,
            Current = current,
            ColdIndex = cold,
            InitialSha256 = Convert.ToHexStringLower(SHA256.HashData(saved)),
            FinalSha256 = Convert.ToHexStringLower(SHA256.HashData(PrivateWorldRuntimeCodec.Encode(world.ExportState())))
        }));
        Assert.True(current.AllocatedBytes.Average() < 50_000,
            $"Forage search allocated {current.AllocatedBytes.Average():N0} bytes for eight animals.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASecondWildAnimalCannotSpendTheFirstAnimalsConsumedFeedAcrossReload(bool birth)
    {
        using var initial = NormalPathWorld.CreateGenerated("wild-forage-cost-audit", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        var state = ShelterOrderTestFixture.WithClearWeather(initial.ExportState());
        var patch = state.Map.Resources.Single(source => source.Id == "wild-greens-patch");
        var positions = state.Map.FootNeighbors(patch.Position).Where(state.Map.IsPassable)
            .Where(point => !state.Inhabitants.Any(person => person.Position == point) &&
                !state.Map.Resources.Any(source => source.NaturalObjectKind == "wild_greens" && source.Id != patch.Id &&
                    state.Map.FootDistance(point, source.Position) <= 1)).Take(2).ToArray();
        Assert.Equal(2, positions.Length);
        var day = state.WorldSystems!.Config.TicksPerDay;
        var tick = state.Society.Society.WorldTick;
        var cows = positions.Select((point, index) => new AnimalState("feed-cow-" + index, "Moss", "cow", index == 0 ? "female" : "male",
            -7L * day, point, "feed-herd", WildWaterUntilTick: tick + day)).ToArray();
        if (birth) cows[0] = cows[0] with { Pregnancy = new(cows[1].Id, AnimalRules.Definition("cow").GestationDays * day - 1, 0) };
        state = state with
        {
            AnimalWorld = new(true, cows, []),
            WorldSystems = state.WorldSystems with
            {
                Ecology = state.WorldSystems.Ecology with
                { Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == patch.Id ? resource with { Quantity = 3 } : resource).ToArray() }
            },
        };
        var ecology = state.WorldSystems.Ecology;
        Assert.Contains(patch.Id, PrivateWorldRuntime.WildAnimalForageSources(state.Map, ecology, cows[1]).Select(source => source.Id));
        var absent = ecology with { Resources = ecology.Resources.Where(resource => resource.Id != patch.Id).ToArray() };
        Assert.DoesNotContain(patch.Id, PrivateWorldRuntime.WildAnimalForageSources(state.Map, absent, cows[1]).Select(source => source.Id));
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
        var final = world.ExportState();
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(final), PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        Assert.Equal(1, final.WorldSystems!.Ecology.GetResource(patch.Id).Quantity);
        Assert.True(final.AnimalWorld.Animals.Single(animal => animal.Id == cows[0].Id).WildFedUntilTick > world.WorldTick);
        Assert.Equal(0, final.AnimalWorld.Animals.Single(animal => animal.Id == cows[1].Id).WildFedUntilTick);
        Assert.DoesNotContain(patch.Id, PrivateWorldRuntime.WildAnimalForageSources(final.Map, final.WorldSystems.Ecology, cows[1]).Select(source => source.Id));
        Assert.Contains(patch.Id, PrivateWorldRuntime.WildAnimalForageSources(state.Map, ecology, cows[1]).Select(source => source.Id));
        if (birth)
        {
            var child = Assert.Single(final.AnimalWorld.Animals, animal => animal.BornTick == world.WorldTick);
            Assert.Null(child.HouseholdId);
            Assert.Equal(cows[0].HerdId, child.HerdId);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
            Assert.Equal(1, world.ExportState().WorldSystems!.Ecology.GetResource(patch.Id).Quantity);
            Assert.Equal(0, world.Animals.Single(animal => animal.Id == child.Id).WildFedUntilTick);
        }
        world.Validate();
    }

    private sealed record Measurement(double[] Milliseconds, long[] AllocatedBytes);

    private static Measurement Measure(Func<string[][]> search)
    {
        for (var warm = 0; warm < 5; warm++) _ = search();
        var times = new double[50];
        var allocations = new long[50];
        for (var sample = 0; sample < times.Length; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            _ = search();
            times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        return new(times, allocations);
    }
}
