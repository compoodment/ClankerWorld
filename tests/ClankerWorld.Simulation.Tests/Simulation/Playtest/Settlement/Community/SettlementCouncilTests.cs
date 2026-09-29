using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementCouncilTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StewardCannotChangeFoodAccessWithoutIndependentMajorityAcrossRestart(bool reject)
    {
        var state = await PreparedState();
        var steward = state.Council!.StewardId!;
        using var world = PrivateWorldRuntime.Restore(state, id => new CouncilProvider(id == steward ? "council_propose:" : "safe_idle"));
        await world.AdvanceOneTickAsync();
        Assert.Equal("open", world.ExportState().Council!.FoodPolicy);
        Assert.Equal([steward], world.ExportState().Council!.Ballot!.Approvals);
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new CouncilProvider(reject ? "council_vote_no" : "council_vote_yes"));
        restored.Resume();
        await restored.AdvanceOneTickAsync();
        Assert.Null(restored.ExportState().Council!.Ballot);
        Assert.Equal(reject ? "open" : "essential_first", restored.ExportState().Council!.FoodPolicy);
        var visible = new OwnerWorldObservationStore(restored).GetSnapshot().Council!;
        Assert.NotNull(visible.StewardName);
        Assert.Equal(restored.ExportState().Council!.FoodPolicy, visible.FoodPolicy);
    }

    [Theory]
    [InlineData("open", 6_000, true)]
    [InlineData("essential_first", 6_000, false)]
    public async Task AdoptedPolicyChangesSharedFoodAccessButProtectsHungryMembers(string policy, int hunger, bool allowed)
    {
        var state = await PreparedState();
        var collector = state.Inhabitants[1].InhabitantId;
        state = state with
        {
            Council = state.Council! with { FoodPolicy = policy },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == collector
                ? person with { HungerBasisPoints = hunger } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, id => new CouncilProvider(id == collector ? "collect_shared_food" : "safe_idle"));
        for (var tick = 0; tick < 40; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        Assert.Equal(allowed, world.ExportState().Events.Any(item => item.Kind == "household_food_collected" &&
            item.Detail.StartsWith(collector + ":", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StewardDeathPreservesPolicyAndSelectsALivingSuccessor()
    {
        var state = await PreparedState();
        var departed = state.Council!.StewardId!;
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, departed, SocietyDeathCause.Accident, checkpoint.WorldTick));
        state = state with
        {
            Society = society.ExportState(),
            Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != departed).ToArray(),
            Council = state.Council with { FoodPolicy = "essential_first" },
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new CouncilProvider("safe_idle"));
        await world.AdvanceOneTickAsync();
        var council = world.ExportState().Council!;
        Assert.Equal("essential_first", council.FoodPolicy);
        Assert.NotNull(council.StewardId);
        Assert.NotEqual(departed, council.StewardId);
        Assert.Equal(SocietyInhabitantStatus.Active, world.Society.GetInhabitant(council.StewardId!).Status);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "council_steward_changed");
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(council, restored.ExportState().Council);
    }

    [Fact]
    public async Task CouncilJournalReportsPolicyTransitionWithoutFreeTextNames()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-council-log-");
        try
        {
            var state = await PreparedState();
            var steward = state.Council!.StewardId!;
            state = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == steward
                            ? person with { Name = "council-free-text-secret" } : person).ToArray(),
                    },
                },
            };
            using var world = PrivateWorldRuntime.Restore(state, id => new CouncilProvider(id == steward ? "council_propose:" : "safe_idle"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains("settlement_council", StringComparison.Ordinal) &&
                message.Contains("event=council_policy_proposed", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("council-free-text-secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ForgedBallotsAndOldSchemaCouncilStateFailClosed()
    {
        var state = await PreparedState();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with { SchemaVersion = 6 }));
        var actor = state.Inhabitants[0].InhabitantId;
        var invalid = state with
        {
            Council = state.Council! with
            {
                Ballot = new("essential_first", state.Society.Society.WorldTick, state.Society.Society.WorldTick + 120,
                    [actor], [actor], [actor]),
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalid));
    }

    private static async Task<PrivateWorldRuntimeState> PreparedState()
    {
        using var seed = new PrivateWorldRuntime("household-council-test", _ => new CouncilProvider("safe_idle"));
        seed.StageStarterContent();
        for (var tick = 0; tick < 305; tick++)
        {
            await seed.AdvanceOneTickAsync();
        }
        var state = seed.ExportState();
        var food = state.Society.Society.Inventory.Lots.First(lot => lot.ItemKind == "food" && lot.OwnerId == "household:camp-alpha");
        return state with
        {
            Council = new(state.Inhabitants[0].InhabitantId, "open", 0),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 7_000,
                Survival = new(),
            }).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == food.Id ? lot with { Quantity = 3 } : lot).ToArray(),
                    },
                },
            },
        };
    }

    private sealed class CouncilProvider(string prefix) : IDecisionProvider
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
