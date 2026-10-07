using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class WrappedStreetGrowthTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task AWorkshopDoorExtendsItsLegalStreetAcrossTheMapSeamAndReplays(bool seedEntrance, bool interior,
        bool growAcrossSeam)
    {
        using var generated = NormalPathWorld.CreateGenerated("street-seam-audit-10", _ => new Idle());
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var original = generated.ExportState();
        var town = Assert.Single(original.Towns!);
        var anchor = new GridPoint(interior ? 1 : growAcrossSeam ? 255 : 0, 21);
        var entrance = new GridPoint(anchor.X, 22);
        GridPoint[] seeded = interior ? [new(0, 22), entrance]
            : growAcrossSeam ? [new(254, 22), entrance]
            : seedEntrance ? [new(255, 22), entrance] : [new(255, 22)];
        // Only the saved network/border is arranged; public placement generates the new growth.
        var state = original with
        {
            RoadTiles = original.RoadTiles!.Concat(seeded).Distinct().ToArray(),
            Towns = [town with { BorderTiles = TownBorderRules.Expand(original.Map, town, seeded.Append(anchor)) }],
        };
        using var world = Restore(state);
        using var replay = Restore(state);
        var workshop = world.WorldContent.Buildings.Single(definition => definition.LocalId == "workshop");
        var continuation = Enumerable.Range(anchor.X + 1, TownStreets.RunOnTiles)
            .Select(x => state.Map.WrapColumn(new GridPoint(x, entrance.Y))).ToArray();
        Assert.All(continuation, point =>
        {
            Assert.True(state.Map.IsBuildable(point));
            Assert.DoesNotContain(point, state.RoadTiles!);
            Assert.DoesNotContain(state.Map.Resources, resource => resource.Position == point);
        });
        var placed = world.PlaceBuilding("wrapped-street-door", workshop.CanonicalId, anchor);
        var replayed = replay.PlaceBuilding("wrapped-street-door", workshop.CanonicalId, anchor);

        Assert.True(placed.Applied, placed.Failure);
        Assert.True(replayed.Applied, replayed.Failure);
        Assert.Equal(entrance, world.WorldSimulation.Buildings.Single(building => building.InstanceId == placed.InstanceId).Entrance);
        Assert.Contains(entrance, world.RoadTiles);
        Assert.All(continuation, point => Assert.Contains(point, world.RoadTiles));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var added = world.RoadTiles.Except(state.RoadTiles!).ToArray();
        var occupied = world.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                world.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        Assert.Null(RoadRoutePlanner.ValidateGrowth(state.Map, occupied, world.RoadTiles.ToHashSet(),
            world.Bridges, added, []));
        world.Validate();
        using var loaded = Restore(world.ExportState());
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }

    [Fact]
    public void StreetGrowthKeepsItsGapFromARoadAcrossTheSeam()
    {
        var map = RiverBridgeTests.Map(Enumerable.Repeat(new string('.', 15), 15).ToArray())
            with
        { WrapsEastWest = true };
        GridPoint[] existing = [new(13, 7), new(0, 6)];
        var streets = new TownStreets(map, [new(14, 6), new(14, 8)], existing);

        var growth = streets.Wander(existing[0], 0, 1, 0, Pcg32XshRrV1.Create("wrapped-gap", "test"));

        Assert.Empty(growth);
        Assert.Equal(existing, streets.Tiles);
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state)
    {
        var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new Idle());
        world.Validate();
        return world;
    }

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId, request.Observation.InhabitantId, Kind, request.ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                "safe_idle", 1, new Dictionary<string, double> { ["safe_idle"] = 1 }));
    }
}
