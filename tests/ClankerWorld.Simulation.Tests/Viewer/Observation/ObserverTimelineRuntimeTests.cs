using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ObserverTimelineRuntimeTests
{
    [Fact]
    public async Task TimelineChangesOnlyAfterSuccessfulCheckpointReplacementAndIsNeverSaved()
    {
        using var world = new PrivateWorldRuntime("observer-timeline-state",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = world.ExportObservation().Timeline;
        Assert.False(string.IsNullOrWhiteSpace(initial.InstanceId));
        Assert.Equal(0, initial.Generation);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(initial, world.ExportObservation().Timeline);
        world.Pause();
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(initial, world.ExportObservation().Timeline);
        var paused = world.ExportState();
        var bytes = PrivateWorldRuntimeCodec.Encode(paused);
        var store = new OwnerWorldObservationStore(world);

        Assert.Throws<InvalidDataException>(() => world.LoadPausedCheckpoint(paused with { SchemaVersion = -1 }));
        Assert.Throws<InvalidDataException>(() => world.SwitchPausedWorld(paused with { SchemaVersion = -1 }));
        using var other = new PrivateWorldRuntime("observer-timeline-other");
        Assert.Throws<InvalidDataException>(() => world.LoadPausedCheckpoint(other.ExportState()));
        Assert.Equal(initial, world.ExportObservation().Timeline);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        world.LoadPausedCheckpoint(paused);

        var loaded = world.ExportObservation().Timeline;
        Assert.Equal(initial.InstanceId, loaded.InstanceId);
        Assert.Equal(initial.Generation + 1, loaded.Generation);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(new ViewerObserverTimeline(loaded.InstanceId, loaded.Generation), store.GetReconnectBaseline(0).Timeline);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.NotEqual(loaded.InstanceId, restored.ExportObservation().Timeline.InstanceId);
        Assert.Equal(0, restored.ExportObservation().Timeline.Generation);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        world.SwitchPausedWorld(other.ExportState());
        Assert.Equal(loaded.InstanceId, world.ExportObservation().Timeline.InstanceId);
        Assert.Equal(loaded.Generation + 1, world.ExportObservation().Timeline.Generation);
        Assert.Equal(other.Society.WorldId, world.Society.WorldId);
        world.Validate();
    }

    [Fact]
    public void RealHistoryCompactionAndResumePreserveTheObserverTimeline()
    {
        var directory = Directory.CreateTempSubdirectory("clanker-observer-timeline-history-");
        try
        {
            using var world = new PrivateWorldRuntime("observer-timeline-compaction");
            var timeline = world.ExportObservation().Timeline;
            for (var index = 0; index <= PrivateWorldHistory.CompactionThreshold / 2; index++)
            {
                world.Pause();
                world.Resume();
            }
            world.Pause();
            var before = world.ExportState();
            Assert.True(before.Events.Count > PrivateWorldHistory.CompactionThreshold);
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));

            Assert.True(file.Save(world, resumeOnSuccess: true));

            var after = world.ExportState();
            Assert.NotNull(after.HistoryArchiveHead);
            Assert.True(after.EventHistoryFloor > before.EventHistoryFloor);
            Assert.False(after.Society.Society.IsPaused);
            Assert.Equal(timeline, world.ExportObservation().Timeline);
            var baseline = new OwnerWorldObservationStore(world).GetReconnectBaseline(0);
            Assert.True(baseline.Events.ResetRequired);
            Assert.Equal(new ViewerObserverTimeline(timeline.InstanceId, timeline.Generation), baseline.Timeline);
            world.Validate();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
