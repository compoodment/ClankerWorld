using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class LongerExplorationTests
{
    [Fact]
    public async Task RealOutingContinuesPastEightStepsThenChoosesReturnAcrossReplay()
    {
        using var seed = NormalPathWorld.CreateGenerated("longer-scouting", _ => new ScoutProvider("safe_idle"));
        Assert.True((await seed.AdvanceOneTickAsync()).Advanced);
        var state = SettlementWeatherTestFixture.WithWeather(seed.ExportState(), WeatherKind.Clear);
        var actor = state.Inhabitants[0].InhabitantId;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_500,
                Survival = new(),
                Project = null,
                LastDecisionContext = null,
                TravelCooldownTicks = 0,
                Exploration = null,
            }).ToArray(),
        };
        var scout = new ScoutProvider("explore");
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? scout : new ScoutProvider("safe_idle"));
        var start = Person(world, actor).Position;
        var reached = new HashSet<ClankerWorld.Simulation.Harness.GridPoint> { start };
        for (var tick = 0; tick < 80 && (Person(world, actor).Exploration?.OutingPath.Count ?? 0) < 13; tick++)
        {
            var before = Person(world, actor).Position;
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var after = Person(world, actor).Position;
            if (before != after) Assert.True(state.Map.CanFootStep(before, after));
            reached.Add(after);
        }
        var outing = Assert.IsType<SettlementExploration>(Person(world, actor).Exploration);
        Assert.True(outing.OutingPath.Count >= 13, "The real outward trip must continue beyond the prototype's eight steps.");
        Assert.False(outing.Returning);
        Assert.True(scout.Calls < outing.OutingPath.Count - 1, "Continuing the outing must not request a model decision for each step.");
        Assert.All(world.ExportState().Knowledge!.Facts.Where(fact => fact.OwnerId == actor),
            fact => Assert.Contains(fact.Position, reached));

        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var returning = new ScoutProvider("explore_return");
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
            id => id == actor ? returning : new ScoutProvider("safe_idle"));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        scout.Preferred = "explore_return";
        var suggestion = new OwnerInstructionRequest("return-scout", "owner:test", actor,
            OwnerInstructionKind.Suggestive, "Please return to where this trip started.");
        world.SubmitInstruction(suggestion);
        replay.SubmitInstruction(suggestion);
        var returningSaved = false;
        for (var tick = 0; tick < 100 && Person(world, actor).Exploration!.OutingPath.Count > 0; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            if (!returningSaved && Person(world, actor).Exploration!.Returning)
            {
                using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
                    PrivateWorldRuntimeCodec.Encode(world.ExportState())),
                    id => new ScoutProvider(id == actor ? "explore_return" : "safe_idle"));
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
                returningSaved = true;
            }
        }
        Assert.True(returningSaved);
        Assert.Empty(Person(world, actor).Exploration!.OutingPath);
        Assert.Equal(start, Person(world, actor).Position);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "exploration_completed" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "exploration_aborted" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        Assert.Empty(world.ExportState().Knowledge!.Artifacts);
        world.Validate();
    }

    private static PlaytestInhabitantState Person(PrivateWorldRuntime world, string actor) =>
        world.Inhabitants.Single(person => person.InhabitantId == actor);

    private sealed class ScoutProvider(string preferred) : IDecisionProvider
    {
        public string Preferred { get; set; } = preferred;
        public int Calls { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var choice = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == Preferred)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
