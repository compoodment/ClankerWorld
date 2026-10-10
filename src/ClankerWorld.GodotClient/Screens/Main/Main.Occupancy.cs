using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Dictionary<string, OccupancyBadge> occupancyBadges = [];
    private readonly Dictionary<string, float> occupancyCanonicalXs = [];

    /// <summary>
    /// Who is inside each building, by building id (agreed October 1): living
    /// agents on the map standing under its roof or wing, so not in its yard,
    /// and not riding. They are hidden from the map, and the building shows a
    /// badge with how many are inside.
    /// </summary>
    private static Dictionary<string, OwnerWorldInhabitant[]> PeopleInside(OwnerWorldSnapshot snapshot, string? buildingId = null)
    {
        var riders = snapshot.Animals.Where(animal => animal.RiderId is not null).Select(animal => animal.RiderId!)
            .ToHashSet(StringComparer.Ordinal);
        var people = snapshot.Inhabitants
            .Where(person => !person.IsDraft && string.Equals(person.Lifecycle, "active", StringComparison.OrdinalIgnoreCase) &&
                !riders.Contains(person.Id))
            .ToArray();
        var inside = new Dictionary<string, OwnerWorldInhabitant[]>(StringComparer.Ordinal);
        if (people.Length == 0) return inside;
        foreach (var building in snapshot.PlacedBuildings.Where(building => buildingId is null || building.InstanceId == buildingId))
        {
            if (StreetLanternLight.IsLantern(building.Tags)) continue;
            var kind = BuildingSprites.KindFor(building.Tags);
            if (!BuildingSprites.HasInterior(kind)) continue;
            var footprint = new Rect2I(building.Position.X, building.Position.Y, Math.Max(1, building.Width), Math.Max(1, building.Height));
            var door = BuildingDoor.Facing(footprint, building.Entrance is { } entrance ? new Vector2I(entrance.X, entrance.Y) : null);
            var (roof, _, _, wing) = BuildingSprites.Plan(kind, footprint.Size.X, footprint.Size.Y, door);
            bool UnderRoof(Vector2I tile)
            {
                var middle = new Vector2((tile.X - footprint.Position.X) * 32 + 16, (tile.Y - footprint.Position.Y) * 32 + 16);
                return roof.HasPoint(middle) || wing is { } part && part.HasPoint(middle);
            }
            var here = people.Where(person =>
            {
                var tile = new Vector2I(person.Position.X, person.Position.Y);
                return footprint.HasPoint(tile) && UnderRoof(tile);
            }).ToArray();
            if (here.Length > 0) inside[building.InstanceId] = here;
        }
        return inside;
    }

    /// <summary>A badge on the corner of each building with people inside, at sprite zoom; none at overview zoom.</summary>
    private void RenderOccupancyBadges(OwnerWorldSnapshot snapshot, IReadOnlyDictionary<string, OwnerWorldInhabitant[]> inside)
    {
        var shown = currentTileSize >= WorldTerrainLayer.SpriteTileMinimum ? inside : new Dictionary<string, OwnerWorldInhabitant[]>();
        foreach (var id in occupancyBadges.Keys.Where(id => !shown.ContainsKey(id)).ToArray())
        {
            occupancyBadges[id].QueueFree();
            occupancyBadges.Remove(id);
            occupancyCanonicalXs.Remove(id);
        }
        var stride = currentTileSize + TileGap;
        var mapWidth = terrainMap?.Width ?? 0;
        foreach (var (id, people) in shown)
        {
            if (snapshot.PlacedBuildings.FirstOrDefault(item => item.InstanceId == id) is not { } building) continue;
            if (!occupancyBadges.TryGetValue(id, out var badge))
            {
                badge = new OccupancyBadge();
                entityLayer.AddChild(badge);
                occupancyBadges.Add(id, badge);
            }
            badge.Count = people.Length;
            badge.Size = OccupancyBadge.SizeFor(currentTileSize, people.Length);
            var canonicalX = (building.Position.X + Math.Max(1, building.Width)) * stride - TileGap - badge.Size.X - 2;
            occupancyCanonicalXs[id] = canonicalX;
            badge.Position = new Vector2(WrappedMarkerX(canonicalX, mapWidth, stride, snapshot.WrapsEastWest), building.Position.Y * stride + 2);
            var names = string.Join(", ", people.Select(person => person.DisplayName).Order(StringComparer.CurrentCulture));
            badge.TooltipText = (people.Length == 1 ? "1 person inside: " : $"{people.Length} people inside: ") + names;
        }
    }
}
