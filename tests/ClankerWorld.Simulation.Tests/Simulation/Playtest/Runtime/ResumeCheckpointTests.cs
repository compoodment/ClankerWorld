using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ResumeCheckpointTests
{
    [Fact]
    public void ResumeIsInvisibleUntilPersistSucceedsAndFailurePreservesTheEpochAndEvents()
    {
        using var world = new PrivateWorldRuntime("durable-resume-boundary");
        world.Pause();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<IOException>(() => world.PersistCheckpoint(proposed =>
        {
            Assert.True(world.Society.IsPaused);
            Assert.False(proposed.Society.Society.IsPaused);
            throw new IOException("Fixture write failure");
        }, resumeOnSuccess: true));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        byte[]? saved = null;
        world.PersistCheckpoint(proposed =>
        {
            Assert.True(world.Society.IsPaused);
            Assert.False(proposed.Society.Society.IsPaused);
            saved = PrivateWorldRuntimeCodec.Encode(proposed);
            return proposed;
        }, resumeOnSuccess: true);
        Assert.False(world.Society.IsPaused);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }
}
