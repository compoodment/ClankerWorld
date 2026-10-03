using ClankerWorld.GodotClient.Protocol;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Which buildings are lit tonight, from what the world already reports:
    /// a light is on only where someone is using the building. A House glows
    /// at its windows and door while anyone is inside; a work building while
    /// anyone is inside or a job runs there; the Blacksmith's forge while it
    /// works; a Warehouse shows only a door lantern while someone fetches or
    /// stores goods; a Silo never glows; a campfire always burns.
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
            var door = BuildingDoor.Facing(footprint, building.Entrance is { } entrance
                ? new Vector2I(entrance.X, entrance.Y) : null);
            var occupied = people.Any(footprint.HasPoint);
            var busy = working.Contains(building.InstanceId);
            var glow = kind switch
            {
                BuildingKind.Silo or BuildingKind.Path => BuildingGlow.None,
                BuildingKind.Warehouse or BuildingKind.Storehouse => occupied ? BuildingGlow.Lantern : BuildingGlow.None,
                BuildingKind.Blacksmith => (busy ? BuildingGlow.Forge : BuildingGlow.None) |
                    (occupied ? BuildingGlow.Door | BuildingGlow.Windows : BuildingGlow.None),
                BuildingKind.House or BuildingKind.Shelter => occupied ? BuildingGlow.Windows | BuildingGlow.Door : BuildingGlow.None,
                _ => occupied || busy ? BuildingGlow.Windows | BuildingGlow.Door : BuildingGlow.None,
            };
            var index = counts.GetValueOrDefault(kind);
            counts[kind] = index + 1;
            if (mock?.TryGetValue($"{kind}{index}", out var mocked) == true) glow = mocked;
            lights.Add(new BuildingLight(footprint, kind, door, glow));
        }
        foreach (var item in snapshot.Objects.Where(item => BuildingSprites.KindForObject(item.Kind) == BuildingKind.Hearth))
            lights.Add(new BuildingLight(new Rect2I(item.Position.X, item.Position.Y, 1, 1),
                BuildingKind.Hearth, BuildingDoor.Default, BuildingGlow.Fire));
        return lights;
    }

    /// <summary>Mockup only: CW_NIGHT_LIGHTS_MOCK="House0=WD,Blacksmith0=F,Warehouse0=L" forces lights per building.</summary>
    private static Dictionary<string, BuildingGlow>? MockedLights()
    {
        var spec = System.Environment.GetEnvironmentVariable("CW_NIGHT_LIGHTS_MOCK");
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var result = new Dictionary<string, BuildingGlow>(StringComparer.Ordinal);
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=');
            var glow = BuildingGlow.None;
            foreach (var letter in pair.Length > 1 ? pair[1] : string.Empty)
                glow |= letter switch
                {
                    'W' => BuildingGlow.Windows,
                    'D' => BuildingGlow.Door,
                    'L' => BuildingGlow.Lantern,
                    'F' => BuildingGlow.Forge,
                    'C' => BuildingGlow.Fire,
                    _ => BuildingGlow.None,
                };
            result[pair[0]] = glow;
        }
        return result;
    }

    /// <summary>
    /// Mockup only: CW_STREET_LANTERNS="post", "stone", "hanging" or "mixed" stands a
    /// lantern at every Road junction and every fourth tile of a straight
    /// Road, on alternating sides, keeping clear of doorsteps.
    /// </summary>
    private static List<StreetLantern> MockedStreetLanterns(OwnerWorldSnapshot snapshot)
    {
        var spec = System.Environment.GetEnvironmentVariable("CW_STREET_LANTERNS");
        if (string.IsNullOrWhiteSpace(spec)) return [];
        var style = spec.Trim().ToLowerInvariant() switch
        {
            "stone" => LanternStyle.Stone,
            "hanging" => LanternStyle.Hanging,
            _ => LanternStyle.Post,
        };
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
            var styled = spec.Trim().Equals("mixed", StringComparison.OrdinalIgnoreCase) ? (LanternStyle)(chosen.Count % 3) : style;
            chosen.Add(new StreetLantern(tile, edge, styled));
        }
        return chosen;
    }
}
