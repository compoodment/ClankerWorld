using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class WildWaterOccupancyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task WildCowDrinksAtAnOpenShoreWhenThePreferredApproachIsCrowded(bool openApproach, bool occupyPreferredShore)
    {
        using var initial = NormalPathWorld.CreateGenerated("wild-forage-occupancy-audit", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        var state = ShelterOrderTestFixture.WithClearWeather(initial.ExportState());
        var origin = new GridPoint(255, 6);
        var preferred = new GridPoint(4, 0);
        var alternate = new GridPoint(5, 1);
        foreach (var shore in new[] { preferred, alternate })
        {
            Assert.Equal(WaterKind.Land, state.Map.HydrologyAt(shore));
            Assert.True(state.Map.IsPassable(shore));
            Assert.Contains(new (int X, int Y)[] { (0, -1), (1, 0), (0, 1), (-1, 0) }, offset =>
            {
                var neighbor = state.Map.WrapColumn(new GridPoint(shore.X + offset.X, shore.Y + offset.Y));
                return state.Map.Contains(neighbor) && state.Map.HydrologyAt(neighbor) is WaterKind.River or WaterKind.Lake;
            });
        }
        GridPoint[] blockedApproaches = [new(4, 1), new(3, 0), new(3, 1)];
        var day = state.WorldSystems!.Config.TicksPerDay;
        var until = state.Society.Society.WorldTick + day;
        var cow = new AnimalState("water-cow", "Moss", "cow", "female", -7L * day,
            origin, "water-cow-herd", WildFedUntilTick: until);
        var blockers = blockedApproaches.Where(point => !openApproach || point != new GridPoint(4, 1))
            .Concat(occupyPreferredShore ? [preferred] : Array.Empty<GridPoint>())
            .Select((point, index) => new AnimalState("water-blocker-" + index, "Ash", "horse", "female", -7L * day,
                point, "water-blocker-herd", CareUntilTick: until)).ToArray();
        state = state with { AnimalWorld = new(true, blockers.Prepend(cow).ToArray(), []) };
        var occupied = state.Inhabitants.Select(person => person.Position).Concat(blockers.Select(animal => animal.Position)).ToHashSet();
        using (var search = new UnoccupiedRouteSearch(state.Map, origin, occupied, state.Map.FootStepCost))
        {
            Assert.Equal(openApproach, search.RouteTo(preferred, 0).Count > 0);
            Assert.True(search.RouteTo(alternate, 0).Count > 1);
        }
        var policy = new MarketRulesPolicy { Choose = (_, candidates) => candidates.Single(candidate => candidate.Id == "safe_idle") };
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var previous = origin;
        for (var tick = 0; tick < 30; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var moved = world.Animals.Single(animal => animal.Id == cow.Id);
            Assert.DoesNotContain(moved.Position, occupied);
            if (moved.Position != previous)
            {
                Assert.True(state.Map.CanFootStep(previous, moved.Position));
                if (moved.Position.X != previous.X && moved.Position.Y != previous.Y)
                {
                    Assert.DoesNotContain(new GridPoint(previous.X, moved.Position.Y), occupied);
                    Assert.DoesNotContain(new GridPoint(moved.Position.X, previous.Y), occupied);
                }
            }
            previous = moved.Position;
        }
        var final = world.ExportState();
        bytes = PrivateWorldRuntimeCodec.Encode(final);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var drank = final.AnimalWorld.Animals.Single(animal => animal.Id == cow.Id);
        Assert.Null(drank.HouseholdId);
        Assert.Equal(cow.HerdId, drank.HerdId);
        Assert.Equal(until, drank.WildFedUntilTick);
        Assert.All(blockers, animal => Assert.Equal(animal.Position, final.AnimalWorld.Animals.Single(item => item.Id == animal.Id).Position));
        Assert.True(drank.WildWaterUntilTick > world.WorldTick);
        Assert.Equal(Math.Min(drank.WildFedUntilTick, drank.WildWaterUntilTick), drank.CareUntilTick);
    }
}
