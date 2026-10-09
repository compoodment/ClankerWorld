using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static List<GraveMarker> GraveMarkers(OwnerWorldSnapshot snapshot, WorldTerrainMap map)
    {
        var ticksPerDay = snapshot.CalendarPace is { TicksPerDay: > 0 } pace ? pace.TicksPerDay : 1_440;
        var daysPerYear = snapshot.CalendarPace is { DaysPerYear: > 0 } calendar ? calendar.DaysPerYear : 365;
        var deaths = snapshot.Inhabitants.Where(person => !person.IsDraft && person.Lifecycle == "dead")
            .Select(person => (Person: person, Tick: long.TryParse(person.DecisionFactors.FirstOrDefault(factor =>
                factor.Key == "death-tick")?.Detail, NumberStyles.None, CultureInfo.InvariantCulture, out var tick) ? tick : -1))
            .Where(death => death.Tick >= 0 && death.Tick <= snapshot.WorldTick &&
                GraveArt.Opacity(snapshot.WorldTick - death.Tick, ticksPerDay, daysPerYear) > 0)
            .OrderBy(death => death.Tick).ThenBy(death => death.Person.Id, StringComparer.Ordinal).ToArray();
        var result = new List<GraveMarker>();
        if (deaths.Length == 0) return result;
        Vector2I Canonical(Vector2I tile) => map.WrapsEastWest
            ? new((tile.X % map.Width + map.Width) % map.Width, tile.Y) : tile;
        Vector2I Tile(OwnerWorldPosition point) => Canonical(new(point.X, point.Y));
        var forbidden = snapshot.RoadTiles.Concat(snapshot.Bridges.SelectMany(bridge => bridge.Span))
            .Concat(snapshot.Resources.Select(resource => resource.Position)).Concat(snapshot.Objects.Select(item => item.Position))
            .Concat(snapshot.Fields.Select(field => field.Position)).Concat(snapshot.GroundStocks.Select(stock => stock.Position))
            .Select(Tile).ToHashSet();
        foreach (var building in snapshot.PlacedBuildings)
        {
            for (var y = building.Position.Y; y < building.Position.Y + Math.Max(1, building.Height); y++)
                for (var x = building.Position.X; x < building.Position.X + Math.Max(1, building.Width); x++)
                    forbidden.Add(Canonical(new(x, y)));
            if (building.Entrance is { } entrance) forbidden.Add(Tile(entrance));
        }
        foreach (var site in snapshot.ConstructionSites)
            for (var y = site.Site.Y; y < site.Site.Y + site.Height; y++)
                for (var x = site.Site.X; x < site.Site.X + site.Width; x++) forbidden.Add(Canonical(new(x, y)));
        bool Open(Vector2I tile) => tile.X >= 0 && tile.X < map.Width && tile.Y >= 0 && tile.Y < map.Height &&
            !forbidden.Contains(tile) && map.At(tile.X, tile.Y) is 1 or 7 or 8 or 9 &&
            map.StyleAt(tile.X, tile.Y) is not (TerrainStyle.Mountain or TerrainStyle.Peak or TerrainStyle.Unknown or
                TerrainStyle.Ocean or TerrainStyle.Lake or TerrainStyle.River or TerrainStyle.ShallowWater);
        var directions = new[] { Vector2I.Up, Vector2I.Left, Vector2I.Right, Vector2I.Down };
        foreach (var death in deaths)
        {
            var origin = Tile(death.Person.Position);
            if (origin.X < 0 || origin.X >= map.Width || origin.Y < 0 || origin.Y >= map.Height) continue;
            Vector2I? chosen = null;
            // Breadth-first Manhattan search, with north, west, east, south ties.
            // This is display placement, not a walking route. Each canonical tile
            // is visited at most once, including across the world seam.
            var visited = new HashSet<Vector2I> { origin };
            var queue = new Queue<Vector2I>();
            queue.Enqueue(origin);
            while (queue.TryDequeue(out var tile))
            {
                if (Open(tile)) { chosen = tile; break; }
                foreach (var direction in directions)
                {
                    var next = Canonical(tile + direction);
                    if (next.X < 0 || next.X >= map.Width || next.Y < 0 || next.Y >= map.Height || !visited.Add(next)) continue;
                    queue.Enqueue(next);
                }
            }
            if (chosen is not { } at) continue;
            forbidden.Add(at);
            result.Add(new(death.Person.Id, at, GraveArt.HeadstoneFor(death.Person.Id),
                GraveArt.Opacity(snapshot.WorldTick - death.Tick, ticksPerDay, daysPerYear)));
        }
        return result;
    }
}
