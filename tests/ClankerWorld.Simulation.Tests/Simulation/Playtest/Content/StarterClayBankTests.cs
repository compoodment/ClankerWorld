using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class StarterClayBankTests
{
    [Fact]
    public async Task GeneratedRockyFreshwaterWorldKeepsAdvancingAndCheckpointingAcrossStarterClayInsertion()
    {
        using var world = NormalPathWorld.CreateGenerated("default-estate-council", _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var mountain = new GridPoint(93, 95);
        Assert.True(world.ExportState().Map.IsPassable(mountain));
        Assert.False(world.ExportState().Map.IsBuildable(mountain));
        // This normal seed previously chose that passable mountain as its clay
        // bank, making the first host checkpoint invalid and pausing the world.
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial), _ => new ActionCoverageRecorder(chooseIdle: true));
        var directory = Path.Combine(Path.GetTempPath(), "clankerworld-starter-clay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "world.json");
            var file = new PrivateWorldStateFile(path);
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(1));
            presence.RecordAuthenticatedReconnect("owner:test");
            using var service = new PrivateWorldRuntimeService(world, file, presence);
            for (var tick = 0; tick < 4; tick++)
            {
                Assert.True(await service.TryAdvanceOnceAsync());
                Assert.False(world.Society.IsPaused);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                var checkpoint = PrivateWorldRuntimeCodec.Encode(world.ExportState());
                Assert.Equal(checkpoint, File.ReadAllBytes(path));
                Assert.Equal(checkpoint, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
                using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(path)));
                Assert.Equal(checkpoint, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
            }
            var map = world.ExportState().Map;
            var clay = Assert.Single(map.Resources, resource => resource.Id == "settlement-clay");
            Assert.NotEqual(mountain, clay.Position);
            Assert.True(map.IsBuildable(clay.Position));
            Assert.True(map.IsReachableFromCampOnFoot(clay.Position));
            Assert.Equal(WaterKind.Land, map.HydrologyAt(clay.Position));
            GridPoint[] neighbors = [new(clay.Position.X, clay.Position.Y - 1), new(clay.Position.X + 1, clay.Position.Y),
                new(clay.Position.X, clay.Position.Y + 1), new(clay.Position.X - 1, clay.Position.Y)];
            Assert.Contains(neighbors.Select(map.WrapColumn).Where(map.Contains), point => map.HydrologyAt(point) is WaterKind.River or WaterKind.Lake);
            Assert.True(MapAcceptance.Validate(map, allowEmptyCamp: true).IsValid);
            Assert.Equal(16, world.ExportState().WorldSystems!.Ecology.GetResource(clay.Id).Quantity);
            world.Validate();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
