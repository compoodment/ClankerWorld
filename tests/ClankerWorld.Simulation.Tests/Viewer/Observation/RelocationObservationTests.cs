using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class RelocationObservationTests
{
    [Fact]
    public async Task MoveOutNoticeShowsTheCurrentHomeCountsAndWorldTimeAcrossPauseAndReload()
    {
        using var world = HouseRelocationTestWorld.Restore(HouseRelocationTestWorld.Crowded());
        await HouseRelocationTestWorld.AdvanceTo(world, 1);
        var actor = Assert.Single(HouseRelocationTestWorld.Noticed(world)).InhabitantId;
        var notice = Housing(world, actor);
        Assert.Contains("House overcrowded: 4 residents, 3 places", notice, StringComparison.Ordinal);
        Assert.Contains("about 24 world hours left", notice, StringComparison.Ordinal);
        Assert.Contains("Notice issued as the most recent arrival when no family had a majority", notice, StringComparison.Ordinal);
        Assert.Contains("Seek an accepting household", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("No home", notice, StringComparison.OrdinalIgnoreCase);
        var staying = world.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        Assert.Contains("1 adult has notice", Housing(world, staying), StringComparison.Ordinal);

        await HouseRelocationTestWorld.AdvanceTo(world, 4);
        Assert.Contains("about 18 world hours left", Housing(world, actor), StringComparison.Ordinal);
        world.Pause();
        var paused = Housing(world, actor);
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, Housing(world, actor));
        using var restored = HouseRelocationTestWorld.Restore(world.ExportState());
        Assert.Equal(paused, Housing(restored, actor));
        await HouseRelocationTestWorld.AdvanceTo(restored, 5);
        Assert.Contains("about 16 world hours left", Housing(restored, actor), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RelocationLogsRecordNoticeAndCancellationWithoutPrivateContext()
    {
        var directory = Directory.CreateTempSubdirectory("relocation-observation-");
        try
        {
            const string secret = "private-relocation-personality-and-model-text";
            var initial = HouseRelocationTestWorld.Crowded(day: 120);
            initial = initial with
            {
                Inhabitants = initial.Inhabitants.Select(person => person with { Personality = secret }).ToArray(),
            };
            using var world = HouseRelocationTestWorld.Restore(initial);
            world.Resume();
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains("settlement_housing tick=", StringComparison.Ordinal) &&
                message.Contains("event=relocation_notice", StringComparison.Ordinal));
            var noticeActor = Assert.Single(HouseRelocationTestWorld.Noticed(world)).InhabitantId;
            var departing = world.Inhabitants.First(person => person.InhabitantId != noticeActor).InhabitantId;
            // A separate departure creates room before the next notice check.
            Assert.True(world.DisplaceAdult(departing));
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Empty(HouseRelocationTestWorld.Noticed(world));
            Assert.Contains(logger.Messages, message => message.Contains("settlement_housing tick=", StringComparison.Ordinal) &&
                message.Contains("event=relocation_cancelled", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains(secret, StringComparison.Ordinal));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static string Housing(PrivateWorldRuntime world, string actor) => new OwnerWorldObservationStore(world)
        .GetSnapshot().Inhabitants.Single(person => person.Id == actor).DecisionFactors.Single(factor => factor.Key == "housing").Detail;
}
