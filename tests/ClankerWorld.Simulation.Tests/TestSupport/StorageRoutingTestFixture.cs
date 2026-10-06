using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

internal static class StorageRoutingTestFixture
{
    internal static PrivateWorldRuntimeState BlockAccess(PrivateWorldRuntime initial, GridPoint storage, int range)
    {
        var map = initial.ExportState().Map;
        var positions = map.Tiles.Where(tile => map.IsPassable(tile.Position) &&
            map.FootDistance(tile.Position, storage) <= range).Select(tile => tile.Position).ToArray();
        Assert.NotEmpty(positions);
        var placements = new Dictionary<string, GridPoint>(StringComparer.Ordinal);
        foreach (var position in positions.Where(position => !initial.Inhabitants.Any(person => person.Position == position)))
        {
            var id = $"agent:{placements.Count + 100:D32}";
            // Agents can walk onto passable gathering sites, although Add Agent
            // requires an empty tile. Set up standing blockers without removing
            // the real resource or changing the map's reachability.
            var spare = map.Tiles.First(tile => map.IsBuildable(tile.Position) &&
                map.FootDistance(tile.Position, storage) > range &&
                !map.Resources.Any(resource => resource.Position == tile.Position) &&
                !map.CampObjects.Any(item => item.Position == tile.Position) &&
                !initial.Inhabitants.Any(person => person.Position == tile.Position) &&
                initial.Towns.Any(town => town.BorderTiles.Contains(tile.Position))).Position;
            initial.AddAgent(id, spare);
            placements.Add(id, position);
        }
        var state = initial.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => placements.TryGetValue(person.InhabitantId, out var position)
                ? person with { Position = position } : person).ToArray(),
        };
        Assert.All(positions, position => Assert.Contains(state.Inhabitants, person => person.Position == position));
        return state;
    }
}
