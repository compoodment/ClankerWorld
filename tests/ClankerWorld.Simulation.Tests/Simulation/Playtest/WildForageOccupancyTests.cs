using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class WildForageOccupancyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WildCowFeedsFromAReachablePatchWhenNearerApproachIsOccupied(bool openNearer)
    {
        using var initial = NormalPathWorld.CreateGenerated("wild-forage-occupancy-audit", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        var state = ShelterOrderTestFixture.WithClearWeather(initial.ExportState());
        var near = state.Map.Resources.Single(resource => resource.Id == "wild-greens-patch");
        var alternate = state.Map.Resources.Single(resource => resource.Id == "wild-128-48");
        Assert.Equal(new GridPoint(131, 62), near.Position);
        Assert.Equal(new GridPoint(135, 59), alternate.Position);
        var origin = new GridPoint(129, 60);
        var points = Enumerable.Range(-1, 3).SelectMany(y => Enumerable.Range(-1, 3)
                .Select(x => new GridPoint(near.Position.X + x, near.Position.Y + y)))
            .OrderBy(point => state.Map.FootDistance(origin, point)).ThenBy(point => point.Y).ThenBy(point => point.X).ToArray();
        Assert.All(points, point => Assert.True(state.Map.IsPassable(point)));
        Assert.Equal(4, state.Inhabitants.Count);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select((person, index) => person with
            {
                Position = openNearer && index == 0 ? new(126, 60) : points[index],
                HungerBasisPoints = 9_500,
                LastDecisionContext = null,
                Project = null,
            }).ToArray(),
        };
        var day = state.WorldSystems!.Config.TicksPerDay;
        var cow = new AnimalState("forage-cow", "Moss", "cow", "female", -7L * day,
            origin, "forage-cow-herd", WildWaterUntilTick: state.Society.Society.WorldTick + day);
        var horses = points.Skip(4).Select((point, index) => new AnimalState("forage-blocker-" + index, "Ash",
            "horse", "female", -(long)AnimalRules.Definition("horse").AdultDays * day,
            point, "forage-blocker-herd", CareUntilTick: state.Society.Society.WorldTick + day)).ToArray();
        state = state with { AnimalWorld = new(true, horses.Prepend(cow).ToArray(), []) };
        var occupied = state.Inhabitants.Select(person => person.Position).Concat(horses.Select(animal => animal.Position)).ToHashSet();
        using (var search = new UnoccupiedRouteSearch(state.Map, origin, occupied, state.Map.FootStepCost))
        {
            Assert.Equal(openNearer, search.RouteTo(near.Position, 1).Count > 0);
            Assert.True(search.RouteTo(alternate.Position, 1).Count > 1);
        }
        var nearBefore = state.WorldSystems.Ecology.GetResource(near.Id).Quantity;
        var alternateBefore = state.WorldSystems.Ecology.GetResource(alternate.Id).Quantity;
        Assert.True(nearBefore >= AnimalRules.Definition("cow").DailyFeed);
        Assert.True(alternateBefore >= AnimalRules.Definition("cow").DailyFeed);
        var policy = new MarketRulesPolicy
        { Choose = (_, candidates) => candidates.Single(candidate => candidate.Id == "safe_idle") };
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var previous = origin;
        for (var tick = 0; tick < 24; tick++)
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
        var fed = final.AnimalWorld.Animals.Single(animal => animal.Id == cow.Id);
        Assert.True(fed.WildFedUntilTick > world.WorldTick);
        Assert.Null(fed.HouseholdId);
        Assert.Equal(cow.HerdId, fed.HerdId);
        Assert.Equal(nearBefore - (openNearer ? AnimalRules.Definition("cow").DailyFeed : 0),
            final.WorldSystems!.Ecology.GetResource(near.Id).Quantity);
        Assert.Equal(alternateBefore - (openNearer ? 0 : AnimalRules.Definition("cow").DailyFeed),
            final.WorldSystems.Ecology.GetResource(alternate.Id).Quantity);
        Assert.All(horses, animal => Assert.Equal(animal.Position, final.AnimalWorld.Animals.Single(item => item.Id == animal.Id).Position));
        bytes = PrivateWorldRuntimeCodec.Encode(final);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
