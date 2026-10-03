using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    // These tests exercise the next real runtime transition. Move only the fixture
    // clock past idle waiting; do not manufacture a birth, plan transition or action.
    private static void PositionFamilyFixtureAt(PrivateWorldRuntime world, long targetTick)
    {
        Assert.True(targetTick >= world.WorldTick);
        var resume = !world.Society.IsPaused;
        world.Pause();
        var state = world.ExportState();
        var society = SocietyFixture.AdvanceTo(SocietyFixture.Resume(state.Society.Society).Checkpoint,
            targetTick).Checkpoint;
        var systems = state.WorldSystems!;
        world.LoadPausedCheckpoint(state with
        {
            Society = state.Society with { Society = SocietyFixture.Pause(society).Checkpoint },
            WorldSystems = systems with
            {
                WorldTick = targetTick,
                RegionalWeather = RegionalWeatherRules.Advance(systems, targetTick),
                Climate = WeatherRules.Advance(systems.Climate, targetTick, state.WorldSeed, systems.Config),
            },
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
        });
        if (resume) world.Resume();
    }

    private static void PositionChildBeforeAge(PrivateWorldRuntime world, string childId, int age)
    {
        var resume = !world.Society.IsPaused;
        world.Pause();
        var state = world.ExportState();
        var society = state.Society.Society;
        var child = society.GetInhabitant(childId);
        var rate = society.LifeClock!.Rate;
        var before = (child.BirthLifeTick ?? child.BirthTick) + age * society.Config.TicksPerLifecycleAge - rate;
        Assert.True(before >= society.LifeTickAt(society.WorldTick));
        world.LoadPausedCheckpoint(state with
        {
            Society = state.Society with
            {
                Society = society with { LifeClock = new(rate, society.WorldTick, before) },
            },
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
        });
        if (resume) world.Resume();
    }

}
