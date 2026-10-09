using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PurposefulExplorationTests
{
    [Theory]
    [InlineData("resource", "wood")]
    [InlineData("terrain", "Forest")]
    public async Task FullPersonalMemoryStillHandsOffAtAnActuallyObservedPurposeWithoutInventingFactsOrOutput(string kind, string target)
    {
        var (state, actor, _, source, _) = await MaterialCourse();
        state = UnpaidWoodProject(state, actor);
        var resourceTiles = state.Map.Resources.Select(resource => resource.Position).ToHashSet();
        var start = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        // Controlled unrelated starting memory, not played visits or injected discoveries.
        // It includes neither a source position nor any matching Forest terrain.
        var facts = state.Map.Tiles.Where(tile => state.Map.IsPassable(tile.Position) &&
            !resourceTiles.Contains(tile.Position) && tile.Terrain.ToString() != "Forest" &&
            state.Map.FootDistance(start, tile.Position) > 3)
            .Take(AgentKnowledgeRules.MaximumFactsPerAgent).Select((tile, index) => new AgentKnowledgeFact(
                "full-purpose-memory:" + index, actor, actor, tile.Position, tile.Terrain.ToString(), [],
                state.Society.Society.WorldTick, "firsthand")).ToArray();
        Assert.Equal(AgentKnowledgeRules.MaximumFactsPerAgent, facts.Length);
        state = state with
        {
            Knowledge = state.Knowledge! with
            {
                Facts = state.Knowledge.Facts.Where(fact => fact.OwnerId != actor).Concat(facts).ToArray(),
            },
        };
        var choice = $"explore_for:{kind}:{target}";
        using var world = PrivateWorldRuntime.Restore(state, id => new ScoutProvider(id == actor ? choice : "safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(new SettlementExplorationGoal(kind, target), Person(world, actor).Exploration!.Goal);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
            id => new ScoutProvider(id == actor ? choice : "safe_idle"));
        var visited = new HashSet<ClankerWorld.Simulation.Kernel.GridPoint> { Person(world, actor).Position };
        for (var tick = 0; tick < 85 && Person(world, actor).Exploration!.Goal is not null; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            visited.Add(Person(world, actor).Position);
            Assert.Equal(AgentKnowledgeRules.MaximumFactsPerAgent,
                world.ExportState().Knowledge!.Facts.Count(fact => fact.OwnerId == actor));
            Assert.DoesNotContain(world.ExportState().Knowledge!.Facts,
                fact => fact.OwnerId == actor && fact.Position == source.Position);
        }
        Assert.Null(Person(world, actor).Exploration!.Goal);
        Assert.Empty(Person(world, actor).Exploration!.OutingPath);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "exploration_goal_found" &&
            item.Detail == $"{actor}:{kind}:{target}");
        if (kind == "resource")
            Assert.Contains(visited, point => state.Map.FootDistance(point, source.Position) <= 1);
        else
            Assert.Equal(target, state.Map.TerrainKindAt(Person(world, actor).Position)!.Value.ToString());
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "wood");
        Assert.Equal(0, Person(world, actor).Project!.WorkDone);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        world.Validate();
        var completed = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(completed));
        Assert.Equal(completed, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        Assert.False((await loaded.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(completed, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }
}
