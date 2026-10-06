using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ExplorationBridgeTimingTests
{
    private static readonly GridPoint Start = new(0, 101);
    private static readonly GridPoint Water = new(0, 102);

    [Fact]
    public async Task ABridgePlacedAfterTheFirstActualStepInTheSameTickKeepsThatStepSaveable()
    {
        using var world = ExplorationBridgeSaveTests.CreateScoutingWorld();
        var actor = world.Inhabitants[0].InhabitantId;
        for (var tick = 0; tick < 400 && world.Inhabitants[0].Exploration!.OutingPath.Count < 2; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var before = world.ExportState();
        var exploration = before.Inhabitants.Single(person => person.InhabitantId == actor).Exploration!;
        Assert.Equal([Start, Water], exploration.OutingPath);
        Assert.Equal(world.WorldTick, exploration.LastOutingTick);
        Assert.True(before.Map.CanFootStep(Start, Water));
        var beforeBytes = PrivateWorldRuntimeCodec.Encode(before);
        using (var unbridged = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(beforeBytes)))
            Assert.Equal(beforeBytes, PrivateWorldRuntimeCodec.Encode(unbridged.ExportState()));

        var workshop = world.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
        var placed = world.PlaceBuilding("same-tick-road-workshop", workshop.CanonicalId, new(3, 102));
        Assert.True(placed.Applied, placed.Failure);
        var bridge = Assert.Single(world.Bridges, item => item.Id == "bridge-0-102-ew-2");
        Assert.Equal("bridge-0-102-ew-2", bridge.Id);
        Assert.Equal(exploration.LastOutingTick, bridge.BuiltTick);
        var after = world.ExportState();
        Assert.Equal(exploration, after.Inhabitants.Single(person => person.InhabitantId == actor).Exploration);
        Assert.Equal(before.Knowledge!.Facts, after.Knowledge!.Facts);
        Assert.False(after.Map.CanFootStep(Start, Water));
        foreach (var cost in workshop.BuildCosts)
            Assert.Equal(cost.Amount, before.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity)
                - after.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity));
        var saved = PrivateWorldRuntimeCodec.Encode(after);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        using var direct = PrivateWorldRuntime.Restore(after);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(direct.ExportState()));
    }

    [Theory]
    [InlineData("nonadjacent")]
    [InlineData("river_diagonal")]
    [InlineData("out_of_bounds")]
    public async Task HistoricalPathValidationStillRefusesMalformedTravel(string scenario)
    {
        using var world = await CreateWorldWithLaterPaidBridge();
        var state = world.ExportState();
        var destination = scenario switch
        {
            "nonadjacent" => new GridPoint(0, 103),
            "river_diagonal" => new GridPoint(1, 102),
            "out_of_bounds" => new GridPoint(-1, 101),
            _ => Start,
        };
        Assert.False(state.Map.CanFootStep(Start, destination));
        if (scenario != "out_of_bounds") Assert.True(state.Map.IsPassable(destination));
        var actor = state.Inhabitants[0].InhabitantId;
        var tampered = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Exploration = new SettlementExploration([Start], [Start, destination], 0, true, []),
            } : person).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(tampered));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(tampered));
    }

    private static async Task<PrivateWorldRuntime> CreateWorldWithLaterPaidBridge()
    {
        var world = ExplorationBridgeSaveTests.CreateScoutingWorld();
        try
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var before = world.ExportState();
            var workshop = world.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
            var placed = world.PlaceBuilding("later-malformed-control-workshop", workshop.CanonicalId, new(3, 102));
            Assert.True(placed.Applied, placed.Failure);
            var bridge = Assert.Single(world.Bridges, item => item.Id == "bridge-0-102-ew-2");
            Assert.Equal("bridge-0-102-ew-2", bridge.Id);
            Assert.Equal(1, bridge.BuiltTick);
            Assert.True(bridge.BuiltTick > 0); // Every damaged outing below claims start tick zero.
            var state = world.ExportState();
            foreach (var cost in workshop.BuildCosts)
                Assert.Equal(cost.Amount, before.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity)
                    - state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity));
            var bytes = PrivateWorldRuntimeCodec.Encode(state);
            using var valid = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(valid.ExportState()));
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }
}
