using ArtPreview;
using ArtPreview.Proposed.Buildings;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.NightLights;

/// <summary>
/// Night lights for every building design, built or still waiting for its
/// feature, and the two street lanterns. Each picture shows the building by
/// day, at night with nobody in it, and at night in use, drawn with the
/// game's own <see cref="NightLightShapes"/> and composited the way the map
/// does: the night wash over ground and roofs, then light that warms and
/// brightens the ground under it, then the lantern fittings on top. A night
/// street lines every Town design along one Road, with frames that show the
/// light's slow drift and the fires' flicker.
/// </summary>
public sealed class NightLightsProposal : IArtProposal
{
    public string Family => "nightlights";

    private static readonly Color Wash = new("0B1433");
    private const float WashAlpha = 0.4f;

    private readonly record struct Sample(LitDesign Design, int W, int H, DoorSide Side, int Tile, string Id, string Note);

    private static readonly Sample[] Samples =
    [
        new(LitDesign.House, 1, 1, DoorSide.South, 0, "House.1x1", "Lit while someone is inside: light from the windows on both sides and the open door. A 1-tile front has room only for the door."),
        new(LitDesign.House, 1, 2, DoorSide.South, 0, "House.1x2", "The long House: a window on each side wall, light through the door."),
        new(LitDesign.House, 2, 2, DoorSide.South, 1, "House.2x2", "The big House: windows either side of the door and on both sides."),
        new(LitDesign.Farmhouse, 1, 2, DoorSide.South, 0, "Farmhouse.1x2", "Lit like a House while someone is inside or working; the yard behind stays dark."),
        new(LitDesign.Farmhouse, 1, 1, DoorSide.South, 0, "Farmhouse.1x1", "The small Farmhouse: side windows and the door."),
        new(LitDesign.Store, 1, 1, DoorSide.South, 0, "Store.1x1", "Lit while the shopkeeper is in: light under the awning and from the side windows."),
        new(LitDesign.Store, 1, 2, DoorSide.South, 0, "Store.1x2", "The long Store."),
        new(LitDesign.TailorShop, 1, 1, DoorSide.South, 0, "TailorShop.1x1", "Lit while someone is sewing or buying."),
        new(LitDesign.TailorShop, 2, 2, DoorSide.South, 1, "TailorShop.2x2", "The big Tailor Shop: front and side windows."),
        new(LitDesign.Clinic, 1, 1, DoorSide.South, 0, "Clinic.1x1", "Lit while a healer or patient is inside."),
        new(LitDesign.Clinic, 1, 2, DoorSide.South, 0, "Clinic.1x2", "The first Clinic, 1 x 2."),
        new(LitDesign.Restaurant, 1, 2, DoorSide.South, 0, "Restaurant.1x2", "Lit while it is open with someone inside."),
        new(LitDesign.Restaurant, 2, 2, DoorSide.South, 1, "Restaurant.2x2", "The big Restaurant also hangs a lantern over its terrace tables while open."),
        new(LitDesign.Workshop, 2, 2, DoorSide.South, 1, "Workshop.2x2", "Lit while someone works inside; the work yard stays dark."),
        new(LitDesign.Generic, 1, 1, DoorSide.South, 0, "Generic.1x1", "Any building the game cannot name: lit like a House."),
        new(LitDesign.Blacksmith, 1, 2, DoorSide.South, 0, "Blacksmith.1x2", "The forge in the yard glows and flickers while a job runs; door and side windows while someone is inside."),
        new(LitDesign.Blacksmith, 2, 2, DoorSide.South, 1, "Blacksmith.2x2", "The big Blacksmith: forge glow in its yard, kept off the roof."),
        new(LitDesign.Warehouse, 2, 2, DoorSide.South, 1, "Warehouse.2x2", "No windows: a lantern on a bracket by the loading doors, lit only while someone fetches or stores goods. Its light falls away from the wall."),
        new(LitDesign.Warehouse, 2, 3, DoorSide.South, 1, "Warehouse.2x3", "The long Warehouse: the same wall lantern."),
        new(LitDesign.Silo, 1, 1, DoorSide.South, 0, "Silo.1x1", "Never lit: nobody goes inside a Silo at night."),
        new(LitDesign.MarketHall, 2, 2, DoorSide.South, 1, "Market.2x2", "Not in the game yet (approved October 1). While traders are in, light spills from the open arcade along the whole front and from the side windows."),
        new(LitDesign.MarketStall, 1, 1, DoorSide.South, 0, "MarketStall.1x1", "Not in the game yet. Stalls close at night and stay dark."),
        new(LitDesign.TownHall, 3, 4, DoorSide.South, 1, "TownHall.3x4", "During a meeting: tall windows close together on the front and sides, and a wide spill over the forecourt. The bell tower stays dark."),
        new(LitDesign.Port, 2, 4, DoorSide.North, 1, "Port.2x4", "Not in the game yet (approved October 1). The lantern on the T-head burns every night so boats can find the Port; the shed is lit while someone is in it."),
    ];

    public IEnumerable<Entry> Render()
    {
        foreach (var sample in Samples)
        {
            var (sprite, ground, plan) = BuildingsProposal.ForNight(sample.Design, sample.W, sample.H, sample.Side, sample.Tile);
            var day = Framed(sample.W, sample.H, ground, sprite, sample.Design == LitDesign.Port);
            var origin = new Vector2(32, 32);
            var seed = sample.Id.GetHashCode() & 0xFFFFFF;
            var daylight = NightLightShapes.Building(plan, false, false, false, 0.5f, seed).Select(cell => (origin, cell)).ToList();
            var empty = NightLightShapes.Building(plan, false, false, true, 0.5f, seed).Select(cell => (origin, cell)).ToList();
            var used = NightLightShapes.Building(plan, true, true, true, 0.5f, seed).Select(cell => (origin, cell)).ToList();
            yield return new(Family, sample.Id, Triptych(Compose(day, daylight, 0), Compose(day, empty, 1), Compose(day, used, 1)),
                "Day · night, nobody in · night, in use. " + sample.Note);
        }

        // The two street lanterns the owner chose, on a Road.
        foreach (var (style, id, note) in new[]
        {
            (LanternStyle.Stone, "StreetLantern.Stone", "B, stone lamp: a round stone pillar with an open flame. Built by agents, no fuel; it lights itself at dusk and its flame flickers."),
            (LanternStyle.Hanging, "StreetLantern.Hanging", "C, hanging lantern: a roadside post with an arm that hangs a glass lantern over the Road. Built by agents, no fuel; its light drifts gently."),
        })
        {
            var road = RoadStrip();
            var by = Lantern(style, 0, 0);
            var at = Lantern(style, 1, 0.5f);
            yield return new(Family, id, Triptych(Compose(road, by, 0), Compose(road, at, 1), Compose(road, Lantern(style, 1, 1.5f), 1)),
                "Day · night · night a moment later. " + note);
        }

        // A night street with every Town design, and frames of its drift.
        var street = Street();
        yield return new(Family, "Street.day", Compose(street.Day, street.Cells(0, 0), 0),
            "Every Town design along one Road by day, with stone lamps at the junction and hanging lanterns along the Road.");
        yield return new(Family, "Street.night", Compose(street.Day, street.Cells(1, 0), 1),
            "The same street at night: some buildings in use, some empty. Lights only where someone is using the building.");
        for (var frame = 0; frame < 16; frame++)
            yield return new(Family, $"Street.frame{frame:00}", Compose(street.Day, street.Cells(1, frame * 0.125f), 1),
                "Animation frame: the light drifts slowly and fires flicker, one step every eighth of a second.");
    }

    // ----------------------------------------------------------------------
    // Compositing, the way the map draws night.
    // ----------------------------------------------------------------------

    /// <summary>
    /// Applies the night wash at <paramref name="darkness"/>, then the light
    /// rows (strongest wins where they overlap), then the fittings.
    /// </summary>
    private static Image Compose(Image day, IReadOnlyList<(Vector2 Origin, LightCell Cell)> cells, float darkness)
    {
        var image = day.Duplicate();
        int w = image.GetWidth(), h = image.GetHeight();
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                image.SetPixel(x, y, image.GetPixel(x, y).Lerp(Wash, WashAlpha * darkness) with { A = 1 });
        if (darkness > 0)
        {
            var strength = new float[w * h];
            var tint = new Color[w * h];
            foreach (var (origin, cell) in cells.Where(item => item.Cell.Kind == LightCellKind.Light).OrderBy(item => item.Cell.Strength))
                ForPixels(origin, cell.Area, w, h, (x, y) =>
                {
                    strength[y * w + x] = cell.Strength * darkness;
                    tint[y * w + x] = cell.Color;
                });
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var s = strength[y * w + x];
                    if (s > 0) image.SetPixel(x, y, Lit(image.GetPixel(x, y), tint[y * w + x], s));
                }
        }
        foreach (var (origin, cell) in cells.Where(item => item.Cell.Kind != LightCellKind.Light))
        {
            var color = cell.Kind == LightCellKind.Glow ? cell.Color : cell.Color.Lerp(Wash, WashAlpha * darkness);
            ForPixels(origin, cell.Area, w, h, (x, y) => image.SetPixel(x, y, color with { A = 1 }));
        }
        return image;
    }

    /// <summary>The map's light shader: brighten the ground, then pull its hue toward the light by its own brightness.</summary>
    private static Color Lit(Color ground, Color light, float strength)
    {
        var shade = ground.R * 0.3f + ground.G * 0.59f + ground.B * 0.11f;
        var mix = Math.Clamp(strength * 1.4f, 0, 0.55f);
        float Channel(float g, float c) =>
            Math.Clamp(Mathf.Lerp(g * (1 + c * strength * 2), shade * (0.5f + c) * (1 + strength), mix), 0, 1);
        return new Color(Channel(ground.R, light.R), Channel(ground.G, light.G), Channel(ground.B, light.B));
    }

    private static void ForPixels(Vector2 origin, Rect2 area, int w, int h, Action<int, int> paint)
    {
        var x0 = Math.Max(0, (int)MathF.Round(origin.X + area.Position.X));
        var y0 = Math.Max(0, (int)MathF.Round(origin.Y + area.Position.Y));
        var x1 = Math.Min(w, (int)MathF.Round(origin.X + area.End.X));
        var y1 = Math.Min(h, (int)MathF.Round(origin.Y + area.End.Y));
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                paint(x, y);
    }

    /// <summary>Three pictures side by side with a dark gap, for day · night empty · night in use.</summary>
    private static Image Triptych(Image a, Image b, Image c)
    {
        const int gap = 6;
        var w = a.GetWidth();
        var image = Image.CreateEmpty(w * 3 + gap * 2, a.GetHeight(), false, Image.Format.Rgba8);
        image.Fill(new Color("2A1F14"));
        image.BlitRect(a, new Rect2I(0, 0, w, a.GetHeight()), new Vector2I(0, 0));
        image.BlitRect(b, new Rect2I(0, 0, w, a.GetHeight()), new Vector2I(w + gap, 0));
        image.BlitRect(c, new Rect2I(0, 0, w, a.GetHeight()), new Vector2I((w + gap) * 2, 0));
        return image;
    }

    // ----------------------------------------------------------------------
    // Ground and scenes.
    // ----------------------------------------------------------------------

    private static Image Grass(int tilesWide, int tilesHigh)
    {
        var ground = Image.CreateEmpty(tilesWide * 32, tilesHigh * 32, false, Image.Format.Rgba8);
        for (var y = 0; y < tilesHigh; y++)
            for (var x = 0; x < tilesWide; x++)
                ground.BlitRect(TerrainTextures.Tile(TerrainStyle.Grass, TerrainTextures.VariantAt(x, y), 32),
                    new Rect2I(0, 0, 32, 32), new Vector2I(x * 32, y * 32));
        return ground;
    }

    /// <summary>A building over its own ground with a tile of margin all round: grass, or river water around a Port.</summary>
    private static Image Framed(int w, int h, Image ground, Image sprite, bool water)
    {
        var image = Grass(w + 2, h + 2);
        if (water)
        {
            var atlas = WaterTextures.Atlas(32).GetImage();
            for (var y = 1; y < h + 2; y++)
                for (var x = 0; x < w + 2; x++)
                    image.BlitRect(atlas, (Rect2I)WaterTextures.Region(TerrainStyle.River, x, y, 32), new Vector2I(x * 32, y * 32));
        }
        image.BlitRect(ground, new Rect2I(0, 0, ground.GetWidth(), ground.GetHeight()), new Vector2I(32, 32));
        Sheet.Blend(image, sprite, 32, 32);
        return image;
    }

    /// <summary>Three tiles of east-west Road with grass above and below.</summary>
    private static Image RoadStrip()
    {
        var image = Grass(3, 3);
        for (var x = 0; x < 3; x++)
            Sheet.Blend(image, RoadSprites.Render(RoadLinks.Road | RoadLinks.East | RoadLinks.West, x % RoadSprites.VariantCount, 32, false), x * 32, 32);
        return image;
    }

    /// <summary>A lantern on the north edge of the middle Road tile of <see cref="RoadStrip"/>.</summary>
    private static List<(Vector2 Origin, LightCell Cell)> Lantern(LanternStyle style, float darkness, float time) =>
        NightLightShapes.StreetLantern(style, new Vector2(16, 4), new Vector2(0, 1), darkness, time, 11)
            .Select(cell => (new Vector2(32, 32), cell)).ToList();

    private readonly record struct Placed(LitDesign Design, int X, int Y, int W, int H, DoorSide Side, int Tile, bool Occupied, bool Working);

    private sealed record StreetScene(Image Day, Func<float, float, List<(Vector2 Origin, LightCell Cell)>> Cells);

    /// <summary>
    /// A 28 x 9 street: a Road along row 4 with a side Road south from a
    /// junction, every Town design facing it, and lanterns: stone lamps at the
    /// junction and its end, hanging lanterns every few tiles along the Road.
    /// </summary>
    private static StreetScene Street()
    {
        const int width = 28, height = 9, road = 4, side = 11;
        Placed[] buildings =
        [
            new(LitDesign.House, 1, 3, 1, 1, DoorSide.South, 0, true, false),
            new(LitDesign.Farmhouse, 3, 2, 1, 2, DoorSide.South, 0, true, false),
            new(LitDesign.Blacksmith, 5, 2, 2, 2, DoorSide.South, 1, false, true),
            new(LitDesign.Store, 8, 2, 1, 2, DoorSide.South, 0, false, false),
            new(LitDesign.MarketHall, 13, 2, 2, 2, DoorSide.South, 1, true, false),
            new(LitDesign.TownHall, 16, 0, 3, 4, DoorSide.South, 1, true, false),
            new(LitDesign.Restaurant, 20, 2, 2, 2, DoorSide.South, 1, true, false),
            new(LitDesign.TailorShop, 23, 3, 1, 1, DoorSide.South, 0, false, false),
            new(LitDesign.Clinic, 25, 2, 1, 2, DoorSide.South, 0, true, false),
            new(LitDesign.Warehouse, 1, 5, 2, 2, DoorSide.North, 1, true, false),
            new(LitDesign.Silo, 4, 5, 1, 1, DoorSide.North, 0, false, false),
            new(LitDesign.House, 6, 5, 1, 2, DoorSide.North, 0, false, false),
            new(LitDesign.Workshop, 13, 5, 2, 2, DoorSide.North, 1, true, false),
            new(LitDesign.Generic, 17, 5, 1, 1, DoorSide.North, 0, false, false),
            new(LitDesign.House, 20, 5, 2, 2, DoorSide.North, 1, true, false),
            new(LitDesign.House, 24, 5, 1, 1, DoorSide.North, 0, true, false),
        ];
        var roads = new HashSet<Vector2I>();
        for (var x = 0; x < width; x++) roads.Add(new Vector2I(x, road));
        for (var y = road; y < height; y++) roads.Add(new Vector2I(side, y));
        var doors = new Dictionary<Vector2I, RoadLinks>();
        foreach (var b in buildings)
        {
            var tile = b.Side == DoorSide.South ? new Vector2I(b.X + b.Tile, b.Y + b.H) : new Vector2I(b.X + b.Tile, b.Y - 1);
            doors[tile] = (doors.GetValueOrDefault(tile)) | (b.Side == DoorSide.South ? RoadLinks.DoorNorth : RoadLinks.DoorSouth);
        }

        var day = Grass(width, height);
        foreach (var tile in roads)
        {
            var links = RoadLinks.Road;
            if (roads.Contains(tile + new Vector2I(0, -1))) links |= RoadLinks.North;
            if (roads.Contains(tile + new Vector2I(1, 0))) links |= RoadLinks.East;
            if (roads.Contains(tile + new Vector2I(0, 1))) links |= RoadLinks.South;
            if (roads.Contains(tile + new Vector2I(-1, 0))) links |= RoadLinks.West;
            links |= doors.GetValueOrDefault(tile);
            Sheet.Blend(day, RoadSprites.Render(links, (tile.X + tile.Y) % RoadSprites.VariantCount, 32, false), tile.X * 32, tile.Y * 32);
        }
        var plans = new List<(Placed Building, LightPlan Plan)>();
        foreach (var b in buildings)
        {
            var (sprite, _, plan) = BuildingsProposal.ForNight(b.Design, b.W, b.H, b.Side, b.Tile);
            Sheet.Blend(day, sprite, b.X * 32, b.Y * 32);
            plans.Add((b, plan));
        }

        // Lanterns: stone lamps at the junction and the side Road's end, hanging lanterns every
        // four tiles along the main Road, on whichever edge is clear of buildings and doorsteps.
        bool Built(Vector2I tile) => buildings.Any(b => tile.X >= b.X && tile.X < b.X + b.W && tile.Y >= b.Y && tile.Y < b.Y + b.H);
        var lanterns = new List<(Vector2I Tile, DoorSide Edge, LanternStyle Style)>
        {
            (new Vector2I(side, road), DoorSide.South, LanternStyle.Stone),
            (new Vector2I(side, height - 1), DoorSide.West, LanternStyle.Stone),
        };
        for (var x = 0; x < width; x += 4)
        {
            var tile = new Vector2I(x, road);
            if (x == side || doors.ContainsKey(tile)) tile = new Vector2I(x + 1, road);
            if (doors.ContainsKey(tile) || Math.Abs(tile.X - side) < 2) continue;
            var edge = !Built(tile + new Vector2I(0, -1)) ? DoorSide.North : !Built(tile + new Vector2I(0, 1)) ? DoorSide.South : (DoorSide?)null;
            if (edge is { } clear) lanterns.Add((tile, clear, LanternStyle.Hanging));
        }

        List<(Vector2 Origin, LightCell Cell)> Cells(float darkness, float time)
        {
            var cells = new List<(Vector2, LightCell)>();
            foreach (var (b, plan) in plans)
            {
                var origin = new Vector2(b.X * 32, b.Y * 32);
                foreach (var cell in NightLightShapes.Building(plan, b.Occupied, b.Working, darkness > 0, time, b.X * 131 + b.Y * 17))
                    cells.Add((origin, cell));
            }
            foreach (var (tile, edge, style) in lanterns)
            {
                var (post, inward) = edge switch
                {
                    DoorSide.North => (new Vector2(16, 4), new Vector2(0, 1)),
                    DoorSide.East => (new Vector2(28, 16), new Vector2(-1, 0)),
                    DoorSide.West => (new Vector2(4, 16), new Vector2(1, 0)),
                    _ => (new Vector2(16, 28), new Vector2(0, -1)),
                };
                foreach (var cell in NightLightShapes.StreetLantern(style, post, inward, darkness, time, tile.X * 7 + tile.Y))
                    cells.Add((new Vector2(tile.X * 32, tile.Y * 32), cell));
            }
            return cells;
        }

        return new StreetScene(day, Cells);
    }
}
