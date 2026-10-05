using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using static ClankerWorld.Simulation.Tests.ShelterOrderTestFixture;

namespace ClankerWorld.Simulation.Tests;

public sealed class ShelterSiteInspectionCheckpointTests
{
    [Theory]
    [InlineData("seek shelter", "ocean")]
    [InlineData("light a fire", "ocean")]
    [InlineData("seek shelter", "other_impassable")]
    [InlineData("light a fire", "other_impassable")]
    [InlineData("seek shelter", "land")]
    [InlineData("light a fire", "land")]
    public async Task InspectingAnUnusableSiteKeepsTheHostSaveableAndLearnsOnlyPassableTiles(string command, string site)
    {
        var state = WithClearWeather(Prepared());
        var actor = Actor(state);
        var (origin, target) = InspectionSite(state, actor, site);
        state = At(state, actor, origin);
        Assert.Equal(site == "land", state.Map.IsPassable(target));
        Assert.DoesNotContain(state.Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == target);
        var initial = PrivateWorldRuntimeCodec.Encode(state);
        IDecisionProvider Providers(string id) => id == actor
            ? new DeterministicDecisionProvider() : new ActionCoverageRecorder(chooseIdle: true);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(initial), Providers);
        var normalized = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using (var validated = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(normalized), Providers))
            Assert.Equal(normalized, PrivateWorldRuntimeCodec.Encode(validated.ExportState()));
        var receipt = world.SubmitInstruction(new OwnerInstructionRequest("inspect-site", "owner:test", actor,
            OwnerInstructionKind.MustDo, $"{command} at ({target.X},{target.Y})"));
        using var host = new CheckpointHost(world);
        await host.AdvanceAndCheckSaved();

        var reached = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        Assert.NotEqual(origin, reached);
        Assert.True(state.Map.IsPassable(reached));
        Assert.InRange(state.Map.FootDistance(reached, target), 0, 1);
        var checkpoint = world.ExportState();
        Assert.Contains(checkpoint.Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == reached &&
            fact.Acquisition == "firsthand");
        if (site == "land")
            Assert.Contains(checkpoint.Knowledge.Facts, fact => fact.OwnerId == actor && fact.Position == target &&
                fact.Acquisition == "firsthand");
        else
            Assert.DoesNotContain(checkpoint.Knowledge.Facts, fact => fact.OwnerId == actor && fact.Position == target);
        Assert.All(checkpoint.Knowledge.Facts, fact => Assert.True(state.Map.IsPassable(fact.Position)));

        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(host.Saved()), Providers);
        Assert.Equal(host.Saved(), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var replayHost = new CheckpointHost(replay);
        world.Pause();
        replay.Pause();
        world.Resume();
        replay.Resume();
        for (var step = 0; step < 3; step++)
        {
            await host.AdvanceAndCheckSaved();
            await replayHost.AdvanceAndCheckSaved();
            Assert.Equal(host.Saved(), replayHost.Saved());
        }
        var order = Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(("blocked", 0), (order.Status, order.CompletedUnits));
        Assert.NotEmpty(order.BlockedReason!);
        Assert.Null(order.ShelterBinding);
        Assert.Null(order.ShelterCompletion);
        Assert.Null(order.LastEffectId);
    }

    private static (GridPoint Origin, GridPoint Target) InspectionSite(PrivateWorldRuntimeState state, string actor, string site)
    {
        if (site == "ocean")
        {
            var target = new GridPoint(61, 0);
            Assert.Equal(TerrainKind.Ocean, state.Map.TerrainKindAt(target));
            return (new(63, 1), target);
        }
        if (site == "land") return (new(63, 1), new(65, 1));

        // Use an actual generated non-Ocean obstacle and a free two-step
        // approach. Starting position/weather are controlled; terrain is untouched.
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != actor)
            .Select(person => person.Position).ToHashSet();
        var buildings = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
        foreach (var target in state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => !state.Map.IsPassable(point) && state.Map.TerrainKindAt(point) != TerrainKind.Ocean))
        {
            foreach (var shore in state.Map.Tiles.Select(tile => tile.Position).Where(point =>
                state.Map.FootDistance(point, target) == 1 && state.Map.IsPassable(point) &&
                !occupied.Contains(point) && !buildings.Contains(point)))
            {
                foreach (var origin in state.Map.FootNeighbors(shore).Where(point =>
                    state.Map.FootDistance(point, target) == 2 && !occupied.Contains(point) && !buildings.Contains(point) &&
                    (!state.Map.IsDiagonalFootStep(point, shore) ||
                        !occupied.Contains(new(point.X, shore.Y)) && !occupied.Contains(new(shore.X, point.Y)))))
                    return (origin, target);
            }
        }
        throw new InvalidOperationException("The generated fixture needs another impassable site with a free approach.");
    }

    private sealed class CheckpointHost : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("clanker-shelter-inspection-checkpoint-");
        private readonly PrivateWorldRuntime world;
        private readonly PrivateWorldRuntimeService service;
        private readonly RecordingLogger<PrivateWorldRuntimeService> logger = new();
        private readonly PrivateWorldStateFile file;

        public CheckpointHost(PrivateWorldRuntime world)
        {
            this.world = world;
            file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            service = new PrivateWorldRuntimeService(world, file, presence, logger);
        }

        public byte[] Saved() => File.ReadAllBytes(file.Path);

        public async Task AdvanceAndCheckSaved()
        {
            Assert.True(await service.TryAdvanceOnceAsync(), string.Join("\n", logger.Messages));
            Assert.False(world.Society.IsPaused);
            Assert.Equal(world.WorldTick, PrivateWorldRuntimeCodec.Decode(Saved()).Society.Society.WorldTick);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), Saved());
        }

        public void Dispose()
        {
            service.Dispose();
            directory.Delete(recursive: true);
        }
    }
}
