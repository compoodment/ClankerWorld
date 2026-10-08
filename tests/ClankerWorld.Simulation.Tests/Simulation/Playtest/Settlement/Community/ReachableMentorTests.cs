using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ReachableMentorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnIsolatedSkilledMentorDoesNotHideAReachableLesson(bool isolatedSkilled)
    {
        var state = await Prepared(isolatedSkilled);
        var adults = state.Inhabitants.Select(person => person.InhabitantId).Order(StringComparer.Ordinal).ToArray();
        var isolated = adults[0];
        var reachable = adults[1];
        var learner = adults[2];
        var policy = Policy(isolated, reachable, learner);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), policy.CreateProvider);
        for (var tick = 0; tick < 50 && (world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson?.Progress ?? 0) < 3; tick++)
            await world.AdvanceOneTickAsync();
        Assert.Contains(policy.OfferedTo(learner), candidate => candidate.Id == "learn:building:" + reachable);
        Assert.DoesNotContain(policy.OfferedTo(learner), candidate => candidate.Id == "learn:building:" + isolated);
        Assert.InRange(world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson!.Progress, 3, 19);
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(paused), Policy(isolated, reachable, learner).CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(paused), Policy(isolated, reachable, learner).CreateProvider);
        restored.Resume();
        replay.Resume();
        var before = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        Assert.False((await restored.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        for (var tick = 0; tick < 60 && restored.Inhabitants.Single(person => person.InhabitantId == learner).Lesson?.Stage != "completed"; tick++)
        {
            await restored.AdvanceOneTickAsync();
            await replay.AdvanceOneTickAsync();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var person = restored.Inhabitants.Single(person => person.InhabitantId == learner);
        Assert.Equal("completed", person.Lesson?.Stage);
        Assert.Equal(20, person.Lesson?.Progress);
        var skill = Assert.Single(person.Skills!);
        Assert.Equal((SettlementSkillKind.Building, reachable), (skill.Kind, skill.TeacherId));
        Assert.Equal(person.Lesson!.LastTransitionTick, skill.LearnedTick);
        var bytes = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        using var completed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(completed.ExportState()));
    }

    [Fact]
    public async Task AnIsolatedLearnerIsNotOfferedAnUnreachableLesson()
    {
        var state = await Prepared(isolatedSkilled: false);
        var adults = state.Inhabitants.Select(person => person.InhabitantId).Order(StringComparer.Ordinal).ToArray();
        var learner = adults[2];
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == learner
            ? person with { Position = new(0, 0), LastDecisionContext = null } : person).ToArray()
        };
        var policy = Policy(adults[0], adults[1], learner);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), policy.CreateProvider);
        for (var tick = 0; tick < 4; tick++) await world.AdvanceOneTickAsync();
        Assert.NotEmpty(policy.OfferedTo(learner));
        Assert.DoesNotContain(policy.OfferedTo(learner), candidate => candidate.Id.StartsWith("learn:", StringComparison.Ordinal));
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == learner).Lesson);
        Assert.Empty(world.Inhabitants.Single(person => person.InhabitantId == learner).Skills ?? []);
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static async Task<PrivateWorldRuntimeState> Prepared(bool isolatedSkilled)
    {
        // Existing readiness/refusal/training checks do not cover a disconnected mentor
        // winning the single per-skill slot. Native selection, acceptance and training
        // must award the skill from a mentor who can reach the actual lesson site.
        var initialPolicy = new MarketRulesPolicy { Choose = (_, candidates) => candidates.Single(candidate => candidate.Id == "safe_idle") };
        using var generated = NormalPathWorld.CreateGenerated("island-lesson-audit-0", initialPolicy.CreateProvider);
        await generated.AdvanceOneTickAsync();
        var state = generated.ExportState();
        var adults = state.Inhabitants.Select(person => person.InhabitantId).Order(StringComparer.Ordinal).ToArray();
        var isolated = adults[0];
        var reachable = adults[1];
        var learner = adults[2];
        var site = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse").Position;
        var island = new GridPoint(0, 0);
        Assert.True(state.Map.IsPassable(island));
        using (var search = new UnoccupiedRouteSearch(state.Map, island, [], (_, _) => 1))
            Assert.Empty(search.RouteTo(site, 1));
        var beside = state.Map.FootNeighbors(site).First(point => state.Map.IsPassable(point) &&
            state.Map.FootDistance(point, site) == 1 && !state.Inhabitants.Any(person => person.Position == point));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == isolated ? island : person.InhabitantId == reachable ? site :
                    person.InhabitantId == learner ? beside : person.Position,
                Skills = person.InhabitantId == reachable || person.InhabitantId == isolated && isolatedSkilled
                    ? [new(SettlementSkillKind.Building, state.Society.Society.WorldTick)] : null,
                Project = null,
                HungerBasisPoints = 10_000,
                Survival = new(10_000),
                LastDecisionContext = null,
                TravelCooldownTicks = 0,
            }).ToArray(),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        return state;
    }

    private static MarketRulesPolicy Policy(string isolated, string reachable, string learner) => new()
    {
        Choose = (actor, candidates) => (actor == learner
            ? candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("learn:building:", StringComparison.Ordinal) || candidate.Id == "lesson_attend")
            : actor == isolated || actor == reachable
                ? candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("lesson_accept:", StringComparison.Ordinal) || candidate.Id.StartsWith("lesson_teach:", StringComparison.Ordinal))
                : null) ?? candidates.Single(candidate => candidate.Id == "safe_idle"),
    };
}
