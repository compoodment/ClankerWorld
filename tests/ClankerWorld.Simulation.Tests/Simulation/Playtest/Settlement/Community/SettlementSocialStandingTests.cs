using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementSocialStandingTests
{
    [Theory]
    [InlineData(0, 12, false)]
    [InlineData(1, 12, true)]
    public void InvalidStandingFailsClosed(int trust, int schema, bool unknownSubject)
    {
        using var world = new PrivateWorldRuntime("invalid-social-standing");
        var state = world.ExportState();
        var owner = state.Inhabitants[0];
        var subject = unknownSubject ? "missing-person" : state.Inhabitants[1].InhabitantId;
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            SchemaVersion = schema,
            Inhabitants = state.Inhabitants.Select(person => person == owner ? person with
            {
                SocialStanding = [new(subject, trust, state.Society.Society.WorldTick)],
            } : person).ToArray(),
        }));
    }

    [Fact]
    public void LegacyCooperationProjectsAsTrustWithoutRewritingCheckpoint()
    {
        using var seed = new PrivateWorldRuntime("legacy-social-standing");
        var state = seed.ExportState() with { SchemaVersion = 11 };
        var owner = state.Inhabitants[0].InhabitantId;
        var subject = state.Inhabitants[1].InhabitantId;
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.RecordSocialMemory(checkpoint, new(
            $"project-gratitude:{owner}:{subject}", owner, subject, "Prior material help.", "public", checkpoint.WorldTick)));
        state = state with { Society = society.ExportState() };
        using var world = PrivateWorldRuntime.Restore(state);
        var checkpointBeforeProjection = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var projected = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == owner);
        Assert.Equal(2, projected.SocialStanding.Single(item => item.SubjectId == subject).Trust);
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, world.ExportState().SchemaVersion);
        Assert.Equal(checkpointBeforeProjection, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.All(world.Inhabitants, person => Assert.Null(person.SocialStanding));
    }

    [Fact]
    public void TrustSurvivesTheSubjectsDeathAsHistoricalContext()
    {
        using var seed = new PrivateWorldRuntime("historical-social-standing");
        var state = seed.ExportState();
        var owner = state.Inhabitants[0].InhabitantId;
        var subject = state.Inhabitants[1].InhabitantId;
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, subject, SocietyDeathCause.Accident, checkpoint.WorldTick));
        state = state with
        {
            SchemaVersion = PrivateWorldRuntime.StateSchemaVersion,
            Society = society.ExportState(),
            Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != subject).Select(person => person.InhabitantId == owner
                ? person with { SocialStanding = [new(subject, 3, state.Society.Society.WorldTick)] }
                : person).ToArray(),
        };
        using var restored = PrivateWorldRuntime.Restore(state);
        Assert.Equal(3, restored.Inhabitants.Single(person => person.InhabitantId == owner).SocialStanding!
            .Single(item => item.SubjectId == subject).Trust);
    }

    [Fact]
    public async Task CompletedExchangeLogsBoundedTrustWithoutPrivateNames()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-standing-log-");
        try
        {
            using var seed = new PrivateWorldRuntime("social-standing-log");
            var state = seed.ExportState();
            var first = state.Inhabitants[0].InhabitantId;
            var second = state.Inhabitants[1].InhabitantId;
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person with
                { HungerBasisPoints = 7_500 }).ToArray(),
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == second
                            ? person with { Name = "private-standing-name" } : person).ToArray(),
                        Inventory = InventoryFixture.AddLot(
                        InventoryFixture.AddLot(state.Society.Society.Inventory, "standing-food", "food", first, 4),
                        "standing-clothes", "clothing", second, 2),
                    }
                },
            };
            IDecisionProvider Provider(string actor) => new ChoiceProvider(actor == first ? "trade_propose:" : actor == second
                ? "trade_accept:" : "safe_idle");
            using var world = PrivateWorldRuntime.Restore(state, Provider);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            for (var tick = 0; tick < 120 && !logger.Messages.Any(message => message.Contains("social_standing", StringComparison.Ordinal)); tick++)
            {
                Assert.True(await service.TryAdvanceOnceAsync());
            }
            Assert.Contains(logger.Messages, message => message.Contains("social_standing", StringComparison.Ordinal) &&
                message.Contains("trust=1", StringComparison.Ordinal) && message.Contains("reason=barter_completed", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("private-standing-name", StringComparison.Ordinal));
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
            Assert.Equal(1, restored.Inhabitants.Single(person => person.InhabitantId == first).SocialStanding!
                .Single(item => item.SubjectId == second).Trust);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class ChoiceProvider(string prefix) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [candidate] },
            }, cancellationToken);
        }
    }
}
