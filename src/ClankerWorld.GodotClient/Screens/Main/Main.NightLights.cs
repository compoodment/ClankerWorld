using ClankerWorld.GodotClient.Protocol;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Which buildings may be lit tonight, from what the world already
    /// reports: someone is inside (a living agent stands within its
    /// footprint) or a job is running there. <see cref="NightLightShapes"/>
    /// decides what each design shows for that.
    /// </summary>
    private static List<BuildingLight> BuildingLights(OwnerWorldSnapshot snapshot)
    {
        var people = snapshot.Inhabitants
            .Where(person => !person.IsDraft && IsLiving(person))
            .Select(person => new Vector2I(person.Position.X, person.Position.Y)).ToArray();
        var working = snapshot.ProductionJobs
            .Where(job => string.Equals(job.State, "running", StringComparison.OrdinalIgnoreCase))
            .Select(job => job.BuildingInstanceId).ToHashSet(StringComparer.Ordinal);
        var lights = new List<BuildingLight>();
        var counts = new Dictionary<BuildingKind, int>();
        var mock = MockedLights();
        foreach (var building in snapshot.PlacedBuildings)
        {
            var footprint = new Rect2I(building.Position.X, building.Position.Y,
                Math.Max(1, building.Width), Math.Max(1, building.Height));
            var kind = BuildingSprites.KindFor(building.Tags);
            if (LitDesignFor(kind) is not { } design) continue;
            var door = BuildingDoor.Facing(footprint, building.Entrance is { } entrance
                ? new Vector2I(entrance.X, entrance.Y) : null);
            var (roof, yard, middle) = BuildingSprites.Plan(kind, footprint.Size.X, footprint.Size.Y, door);
            var occupied = people.Any(footprint.HasPoint);
            var busy = working.Contains(building.InstanceId);
            var index = counts.GetValueOrDefault(kind);
            counts[kind] = index + 1;
            if (mock?.TryGetValue($"{kind}{index}", out var forced) == true) (occupied, busy) = forced;
            lights.Add(new BuildingLight(footprint, new LightPlan(design, roof, yard, door.Side, middle), occupied, busy));
        }
        foreach (var item in snapshot.Objects.Where(item => BuildingSprites.KindForObject(item.Kind) == BuildingKind.Hearth))
            lights.Add(new BuildingLight(new Rect2I(item.Position.X, item.Position.Y, 1, 1),
                new LightPlan(LitDesign.Campfire, new Rect2(), null, DoorSide.South, 16), false, false));
        return lights;
    }

    /// <summary>The night-light design for a building family, or null for those with no lights.</summary>
    private static LitDesign? LitDesignFor(BuildingKind kind) => kind switch
    {
        BuildingKind.House or BuildingKind.Shelter => LitDesign.House,
        BuildingKind.Farmhouse => LitDesign.Farmhouse,
        BuildingKind.Store => LitDesign.Store,
        BuildingKind.TailorShop => LitDesign.TailorShop,
        BuildingKind.Workshop => LitDesign.Workshop,
        BuildingKind.Blacksmith => LitDesign.Blacksmith,
        BuildingKind.Warehouse or BuildingKind.Storehouse => LitDesign.Warehouse,
        BuildingKind.Silo => LitDesign.Silo,
        BuildingKind.Generic => LitDesign.Generic,
        _ => null,
    };

    /// <summary>
    /// Mockup only: CW_NIGHT_LIGHTS_MOCK="House0=O,Blacksmith0=J" forces a
    /// building occupied (O) or working (J), counted per family in map order.
    /// </summary>
    private static Dictionary<string, (bool Occupied, bool Working)>? MockedLights()
    {
        var spec = System.Environment.GetEnvironmentVariable("CW_NIGHT_LIGHTS_MOCK");
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var result = new Dictionary<string, (bool, bool)>(StringComparer.Ordinal);
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=');
            var flags = pair.Length > 1 ? pair[1] : string.Empty;
            result[pair[0]] = (flags.Contains('O'), flags.Contains('J'));
        }
        return result;
    }

    /// <summary>
    /// Mockup only: CW_STREET_LANTERNS="stone", "hanging" or "mixed" stands a
    /// lantern at every Road junction and every fourth tile of a straight
    /// Road, on alternating sides, keeping clear of doorsteps. "mixed" puts
    /// stone lamps at junctions and hanging lanterns along the Roads.
    /// </summary>
    private static List<StreetLantern> MockedStreetLanterns(OwnerWorldSnapshot snapshot)
    {
        var spec = System.Environment.GetEnvironmentVariable("CW_STREET_LANTERNS");
        if (string.IsNullOrWhiteSpace(spec)) return [];
        var mode = spec.Trim().ToLowerInvariant();
        var roads = snapshot.RoadTiles.Select(tile => new Vector2I(tile.X, tile.Y)).ToHashSet();
        var doorsteps = snapshot.PlacedBuildings.Where(building => building.Entrance is not null)
            .Select(building => new Vector2I(building.Entrance!.X, building.Entrance.Y)).ToHashSet();
        var chosen = new List<StreetLantern>();
        foreach (var tile in roads.OrderBy(tile => tile.Y).ThenBy(tile => tile.X))
        {
            if (doorsteps.Contains(tile)) continue;
            bool Road(int dx, int dy) => roads.Contains(tile + new Vector2I(dx, dy));
            var across = Road(1, 0) || Road(-1, 0);
            var along = Road(0, 1) || Road(0, -1);
            var junction = across && along;
            var regular = across && !along ? tile.X % 4 == 0 : !across && along && tile.Y % 4 == 0;
            if (!junction && !regular) continue;
            if (chosen.Any(other => Math.Max(Math.Abs(other.Tile.X - tile.X), Math.Abs(other.Tile.Y - tile.Y)) < 3)) continue;
            DoorSide edge;
            if (junction)
                edge = !Road(0, -1) ? DoorSide.North : !Road(0, 1) ? DoorSide.South : !Road(-1, 0) ? DoorSide.West : DoorSide.East;
            else if (across)
                edge = tile.X / 4 % 2 == 0 ? DoorSide.North : DoorSide.South;
            else
                edge = tile.Y / 4 % 2 == 0 ? DoorSide.West : DoorSide.East;
            // Never stand a post where a building's doorstep path meets the Road.
            var beside = tile + edge switch
            {
                DoorSide.North => new Vector2I(0, -1),
                DoorSide.South => new Vector2I(0, 1),
                DoorSide.West => new Vector2I(-1, 0),
                _ => new Vector2I(1, 0),
            };
            if (snapshot.PlacedBuildings.Any(building => building.Entrance is { } entrance &&
                    new Rect2I(building.Position.X, building.Position.Y, Math.Max(1, building.Width), Math.Max(1, building.Height)).HasPoint(beside)))
                edge = edge switch
                {
                    DoorSide.North => DoorSide.South,
                    DoorSide.South => DoorSide.North,
                    DoorSide.West => DoorSide.East,
                    _ => DoorSide.West,
                };
            var style = mode switch
            {
                "stone" => LanternStyle.Stone,
                "mixed" => junction ? LanternStyle.Stone : LanternStyle.Hanging,
                _ => LanternStyle.Hanging,
            };
            chosen.Add(new StreetLantern(tile, edge, style));
        }
        return chosen;
    }
}
