using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PortBoatRuntimeTests
{
    [Fact]
    public async Task NativeFreshwaterBoatPassengerKeepsOrdinaryWeatherExposureAcrossReload()
    {
        using var voyage = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await BuildPaidBoatAsync(freshwater: true)),
            new() { Trips = true });
        await voyage.UntilAsync(() => voyage.World.Boats[0].Journey is not null, 120);
        var state = voyage.World.ExportState();
        var actor = BoatPolicy.Author;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Survival = new SurvivalCondition(8_000), Equipment = null } : person).ToArray(),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Rain);
        using var world = new BoatScenario(state, new() { IdleActors = { actor } });
        var before = PrivateWorldRuntimeCodec.Encode(world.World.ExportState());
        Assert.False((await world.World.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.World.ExportState()));
        using var replay = new BoatScenario(PrivateWorldRuntimeCodec.Decode(before), new() { IdleActors = { actor } });
        var previousWarmth = 8_000;
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.World.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.World.AdvanceOneTickAsync()).Advanced);
            var boat = Assert.Single(world.World.Boats);
            Assert.Equal(actor, boat.Journey!.PassengerId);
            var passenger = world.World.Inhabitants.Single(person => person.InhabitantId == actor);
            Assert.Equal(boat.Position, passenger.Position);
            Assert.True(SwimmingRules.IsSwimmingWater(world.World.ExportState().Map, passenger.Position));
            var warmth = passenger.Survival!.WarmthBasisPoints;
            Assert.InRange(previousWarmth - warmth, 25, 40);
            previousWarmth = warmth;
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.World.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
        }
        var saved = PrivateWorldRuntimeCodec.Encode(world.World.ExportState());
        using var loaded = new BoatScenario(PrivateWorldRuntimeCodec.Decode(saved), new());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(loaded.World.ExportState()));
        world.World.Validate();
    }
}
