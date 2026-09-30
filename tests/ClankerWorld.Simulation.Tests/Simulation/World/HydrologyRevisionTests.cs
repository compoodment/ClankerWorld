using System.Text;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class HydrologyRevisionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(WorldSizePreset.Small, true)]
    [InlineData(WorldSizePreset.Small, false)]
    [InlineData(WorldSizePreset.Medium, true)]
    [InlineData(WorldSizePreset.Medium, false)]
    public void LakeScaleCoverageAndTerminalDrainageHoldAcrossSeeds(WorldSizePreset size, bool wrap)
    {
        foreach (var water in new[] { 20, 45, 70 })
            for (var seed = 0; seed < 3; seed++)
            {
                var options = new GeographyOptions($"inland-water-{seed}", size, wrap, water);
                var before = GeographyGenerator.Generate(options);
                var currentOptions = options with { HydrologyVersion = GeographyGenerator.CurrentHydrologyVersion };
                var after = GeographyGenerator.Generate(currentOptions);
                var repeat = GeographyGenerator.Generate(currentOptions);
                var oldLake = LargestLake(before);
                var newLake = LargestLake(after);
                var area = after.Width * after.Height;
                Assert.InRange(newLake, 0, area / 200);
                Assert.Equal(before.Count(WaterKind.Lake) + before.Count(WaterKind.Ocean),
                    after.Count(WaterKind.Lake) + after.Count(WaterKind.Ocean));
                Assert.True(after.Count(WaterKind.Ocean) > newLake);
                var formerLakeNowLand = 0;
                for (var y = 0; y < after.Height; y++)
                    for (var x = 0; x < after.Width; x++)
                    {
                        if (before.At(x, y).Water == WaterKind.Lake && after.At(x, y).Water == WaterKind.Land) formerLakeNowLand++;
                        Assert.Equal(after.At(x, y), repeat.At(x, y));
                        Assert.Equal(after.DownstreamAt(x, y), repeat.DownstreamAt(x, y));
                        if (after.At(x, y).Water is WaterKind.Lake or WaterKind.Ocean)
                            Assert.Null(after.DownstreamAt(x, y));
                    }
                var reduced = before.Count(WaterKind.Lake) - after.Count(WaterKind.Lake);
                Assert.True(formerLakeNowLand >= reduced / 2,
                    "Shrinking an inland lake must create substantial dry land, not merely relabel its water as sea.");
                output.WriteLine($"size={size}; wrap={wrap}; water={water}; seed={seed}; largestLake={oldLake}/{newLake}; openWater={before.Count(WaterKind.Lake) + before.Count(WaterKind.Ocean)}/{after.Count(WaterKind.Lake) + after.Count(WaterKind.Ocean)}; rivers={before.Count(WaterKind.River)}/{after.Count(WaterKind.River)}");
            }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SavedMapRetainsItsHydrologyLineage(int version)
    {
        var geography = new GeographyOptions("hydrology-save", WorldSizePreset.Small,
            HydrologyVersion: version);
        using var world = new PrivateWorldRuntime(geography.Seed, startPace: WorldStartPace.FounderSetup,
            geographyOptions: geography);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        if (version == 0)
            Assert.DoesNotContain("hydrologyVersion", Encoding.UTF8.GetString(before), StringComparison.OrdinalIgnoreCase);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before));
        Assert.Equal(version, restored.ExportState().Geography!.HydrologyVersion);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void UnknownHydrologyVersionIsRejectedInsteadOfSubstitutingAnotherMap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeographyGenerator.Generate(
            new GeographyOptions("unsupported", WorldSizePreset.Small, HydrologyVersion: 2)));
    }

    private static int LargestLake(GeneratedGeography map)
    {
        var visited = new HashSet<(int X, int Y)>();
        var largest = 0;
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                if (map.At(x, y).Water != WaterKind.Lake || !visited.Add((x, y))) continue;
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((x, y));
                var count = 0;
                while (queue.TryDequeue(out var point))
                {
                    count++;
                    foreach (var next in new[] { (point.X - 1, point.Y), (point.X + 1, point.Y),
                                 (point.X, point.Y - 1), (point.X, point.Y + 1) })
                    {
                        var nx = map.WrapsEastWest ? (next.Item1 + map.Width) % map.Width : next.Item1;
                        var ny = next.Item2;
                        if (nx < 0 || nx >= map.Width || ny < 0 || ny >= map.Height ||
                            map.At(nx, ny).Water != WaterKind.Lake || !visited.Add((nx, ny))) continue;
                        queue.Enqueue((nx, ny));
                    }
                }
                largest = Math.Max(largest, count);
            }
        return largest;
    }
}
