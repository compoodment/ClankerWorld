using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static List<StoredStockPile> StoredStockPiles(OwnerWorldSnapshot snapshot, WorldTerrainMap map)
    {
        Vector2I Tile(OwnerWorldPosition point) => Canonical(new(point.X, point.Y));
        Vector2I Canonical(Vector2I point) => map.WrapsEastWest
            ? new((point.X % map.Width + map.Width) % map.Width, point.Y) : point;
        Rect2I Footprint(OwnerWorldPlacedBuilding building) =>
            new(building.Position.X, building.Position.Y, Math.Max(1, building.Width), Math.Max(1, building.Height));
        Vector2I Door(OwnerWorldPlacedBuilding building)
        {
            if (building.Entrance is { } entrance) return Tile(entrance);
            var footprint = Footprint(building);
            return Canonical(new(footprint.Position.X + footprint.Size.X / 2, footprint.End.Y));
        }
        var forbidden = snapshot.RoadTiles.Concat(snapshot.Bridges.SelectMany(bridge => bridge.Span))
            .Concat(snapshot.Resources.Select(resource => resource.Position))
            .Concat(snapshot.Objects.Select(item => item.Position)).Concat(snapshot.Fields.Select(field => field.Position))
            .Concat(snapshot.GroundStocks.Select(stock => stock.Position)).Select(Tile).ToHashSet();
        foreach (var building in snapshot.PlacedBuildings)
        {
            var footprint = Footprint(building);
            for (var y = footprint.Position.Y; y < footprint.End.Y; y++)
                for (var x = footprint.Position.X; x < footprint.End.X; x++) forbidden.Add(Canonical(new(x, y)));
            forbidden.Add(Door(building));
        }
        foreach (var site in snapshot.ConstructionSites)
            for (var y = site.Site.Y; y < site.Site.Y + site.Height; y++)
                for (var x = site.Site.X; x < site.Site.X + site.Width; x++) forbidden.Add(Canonical(new(x, y)));
        var townLand = snapshot.Towns.ToDictionary(town => town.Id, town => town.BorderTiles
            .Concat(snapshot.TownLandTitles.Where(title => title.TownId == town.Id).SelectMany(title => title.Tiles))
            .Select(Tile).ToHashSet(), StringComparer.Ordinal);
        var result = new List<StoredStockPile>();
        foreach (var building in snapshot.PlacedBuildings.OrderBy(building => building.InstanceId, StringComparer.Ordinal))
        {
            if (building.StoredQuantity <= 0 || building.StoredItems is null || building.TownId is null ||
                !townLand.TryGetValue(building.TownId, out var land)) continue;
            var footprint = Footprint(building);
            var door = Door(building);
            Vector2I? entranceTile = building.Entrance is { } entrance ? new(entrance.X, entrance.Y) : null;
            if (map.WrapsEastWest && entranceTile is { } raw && Math.Abs(raw.X - footprint.Position.X) > map.Width / 2)
                entranceTile = new(raw.X + Math.Sign(footprint.Position.X - raw.X) * map.Width, raw.Y);
            var facing = BuildingDoor.Facing(footprint, entranceTile);
            var outward = facing.Side switch
            {
                DoorSide.North => Vector2I.Up,
                DoorSide.South => Vector2I.Down,
                DoorSide.West => Vector2I.Left,
                _ => Vector2I.Right,
            };
            int XDistance(int x) => map.WrapsEastWest
                ? Math.Min(Math.Abs(x - door.X), map.Width - Math.Abs(x - door.X)) : Math.Abs(x - door.X);
            var candidates = new List<Vector2I>();
            var ring = footprint.Grow(1);
            for (var y = ring.Position.Y; y < ring.End.Y; y++)
                for (var x = ring.Position.X; x < ring.End.X; x++)
                {
                    var tile = Canonical(new(x, y));
                    if (tile.X < 0 || tile.X >= map.Width || tile.Y < 0 || tile.Y >= map.Height ||
                        forbidden.Contains(tile) || !land.Contains(tile) || map.At(tile.X, tile.Y) is not (1 or 7 or 8 or 9) ||
                        map.StyleAt(tile.X, tile.Y) is TerrainStyle.Mountain or TerrainStyle.Peak or TerrainStyle.Unknown ||
                        XDistance(tile.X) + Math.Abs(tile.Y - door.Y) > 2) continue;
                    // Front ground and the nearest side-wall tile are beside the door; never go behind it.
                    var dx = tile.X - door.X;
                    if (map.WrapsEastWest && Math.Abs(dx) > map.Width / 2) dx -= Math.Sign(dx) * map.Width;
                    if (dx * outward.X + (tile.Y - door.Y) * outward.Y < -1) continue;
                    candidates.Add(tile);
                }
            var next = candidates.Distinct().OrderBy(tile => XDistance(tile.X) + Math.Abs(tile.Y - door.Y))
                .ThenBy(tile => tile.Y).ThenBy(tile => tile.X).ToArray();
            var level = building.StorageCapacity is > 0 && (long)building.StoredQuantity * 5 >= (long)building.StorageCapacity * 4 ? 2 : 1;
            var groups = building.StoredItems.Where(item => item.Quantity > 0).Select(item => StoredStockArt.KindFor(item.Kind))
                .Distinct().Order().ToArray();
            var index = 0;
            foreach (var kind in groups)
            {
                while (index < next.Length && forbidden.Contains(next[index])) index++;
                if (index == next.Length) break;
                var tile = next[index++];
                forbidden.Add(tile);
                result.Add(new(tile, kind, level));
            }
        }
        return result;
    }
}
