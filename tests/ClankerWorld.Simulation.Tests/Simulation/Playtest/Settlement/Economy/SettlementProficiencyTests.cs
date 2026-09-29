using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementProficiencyTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(10, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public async Task PracticeImprovesWorkAndOnlySuccessfulCompletionEarnsCredit(int experience, bool finish)
    {
        using var seed = new PrivateWorldRuntime("practice-work", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var actor = state.Inhabitants[0];
        var site = state.Map.Tiles.Last(tile => state.Map.IsPassable(tile.Position) &&
            !state.Map.CampObjects.Any(item => item.Position == tile.Position) &&
            !state.Map.Resources.Any(item => item.Position == tile.Position) &&
            !state.Inhabitants.Any(person => person.Position == tile.Position)).Position;
        var definition = seed.WorldContent.Buildings.Single(building => building.LocalId == "fire");
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "practice-tool", "tool", actor.InhabitantId, 1),
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person with { Name = "sk-private-practice-name" }).ToArray(),
                }
            },
            Inhabitants = state.Inhabitants.Select(person => person == actor ? person with
            {
                Position = site,
                HungerBasisPoints = 9_000,
                Proficiency = new(experience),
                Project = new("build:building:" + definition.CanonicalId, definition.DisplayName, seed.WorldTick, "working",
                    finish ? 10 : 0, LastTransitionTick: seed.WorldTick),
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var directory = Directory.CreateTempSubdirectory("practice-log-");
        try
        {
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            var result = world.Inhabitants.Single(person => person.InhabitantId == actor.InhabitantId);
            Assert.Equal(finish ? Math.Min(30, experience + 1) : experience, result.Proficiency!.Building);
            Assert.Equal(finish ? 10 : 2 + experience / 10, result.Project!.WorkDone);
            Assert.Equal(finish ? "completed" : "working", result.Project.Stage);
            Assert.Equal(finish && experience < 30, logger.Messages.Any(message => message.Contains("work_practice tick=", StringComparison.Ordinal)));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("sk-private-practice-name", StringComparison.Ordinal));
            var projection = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == actor.InhabitantId);
            Assert.Equal(result.Proficiency.Building, projection.Proficiency!.Building);
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new IdleProvider());
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            if (finish)
            {
                await restored.AdvanceOneTickAsync();
                Assert.Equal(result.Proficiency, restored.Inhabitants.Single(person => person.InhabitantId == actor.InhabitantId).Proficiency);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(-1, 11)]
    [InlineData(1, 10)]
    public void InvalidOrOldSchemaPracticeFailsClosed(int experience, int schema)
    {
        using var world = new PrivateWorldRuntime("invalid-practice");
        var state = world.ExportState();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            SchemaVersion = schema,
            Inhabitants = state.Inhabitants.Select(person => person with { Proficiency = new(experience) }).ToArray(),
        }));
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = request.Observation.Candidates.Where(candidate => candidate.Id == "safe_idle").ToArray() },
            }, cancellationToken);
    }
}
