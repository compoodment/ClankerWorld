using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>One building's lights tonight: where it stands, how its lights are laid out and who is using it.</summary>
public readonly record struct BuildingLight(Rect2I Footprint, LightPlan Plan, bool Occupied, bool Working)
{
    public BuildingKind? Kind { get; init; }
    public BuildingDoor Door { get; init; }

    /// <summary>Match the integer roof layout of the atlas actually drawn at this zoom.</summary>
    public BuildingLight AtAtlas(int tilePixels)
    {
        if (Kind is not { } kind) return this;
        var (roof, yard, middle, wing) = BuildingSprites.Plan(kind, Footprint.Size.X, Footprint.Size.Y, Door, tilePixels);
        return this with { Plan = Plan with { Roof = roof, Yard = yard, DoorMiddle = middle, Wing = wing } };
    }
    /// <summary>Whether anything of this building shines, for the overview speck.</summary>
    public bool Shines => Plan.Design switch
    {
        LitDesign.Silo or LitDesign.MarketStall => false,
        LitDesign.House => Occupied,
        LitDesign.Port => true,
        _ => Occupied || Working,
    };
}

/// <summary>A completed street fitting on the saved edge of its adjoining Road tile.</summary>
public readonly record struct StreetLanternLight(Vector2I RoadTile, DoorSide Edge, LanternStyle Style)
{
    private static readonly Dictionary<(LanternStyle Style, DoorSide Edge, BuildingNeglect Neglect, int Snap), List<LightCell>> WeatheredCache = [];

    /// <summary>How the fitting has weathered: lanterns in an abandoned Town look neglected, then falling apart.</summary>
    public BuildingNeglect Neglect { get; init; }

    /// <summary>A lantern in an abandoned Town stays dark at night until the Town is resettled (computment, October 7).</summary>
    public bool Dark => Neglect != BuildingNeglect.None;

    /// <summary>
    /// Everything drawn for this fitting at the given darkness, in 32 px units
    /// of its Road tile: its light and flame or glass when lit, and its
    /// fitting, weathered in an abandoned Town, where it never lights.
    /// </summary>
    public IEnumerable<LightCell> Cells(float darkness, float time, int seed, int snap = 1)
    {
        var weathered = Neglect != BuildingNeglect.None;
        foreach (var cell in NightLightShapes.StreetLantern(Style, Post, Inward, Dark ? 0 : darkness, time, seed, snap))
            if (!weathered || cell.Kind != LightCellKind.Paint) yield return cell;
        if (weathered)
            foreach (var cell in WeatheredCells(snap)) yield return cell;
    }

    /// <summary>
    /// The weathered fitting's pixels by day, in 32 px units of its Road tile,
    /// from the approved abandoned look; a snap of 2 halves it as the 16 px
    /// buildings are. Its weeds may reach into the tile beside the Road.
    /// </summary>
    public IReadOnlyList<LightCell> WeatheredCells(int snap = 1)
    {
        var key = (Style, Edge, Neglect, snap);
        if (WeatheredCache.TryGetValue(key, out var cached)) return cached;
        using var image = BuildingSprites.NeglectedLantern(Style, Inward, Neglect, 32 / snap);
        var offset = Post - BuildingSprites.NeglectedLanternPost(Style, Inward);
        var cells = new List<LightCell>();
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                // The fitting and its weeds are solid pixels; nothing else is drawn.
                var color = image.GetPixel(x, y);
                if (color.A < 0.5f) continue;
                cells.Add(new(new Rect2(offset + new Vector2(x, y) * snap, Vector2.One * snap), color, 1, LightCellKind.Paint));
            }
        WeatheredCache[key] = cells;
        return cells;
    }

    public Vector2 Post => Edge switch
    {
        DoorSide.North => new(16, 4),
        DoorSide.East => new(28, 16),
        DoorSide.West => new(4, 16),
        _ => new(16, 28),
    };

    public Vector2 Inward => Edge switch
    {
        DoorSide.North => Vector2.Down,
        DoorSide.East => Vector2.Left,
        DoorSide.West => Vector2.Right,
        _ => Vector2.Up,
    };

    public static bool IsLantern(IReadOnlyList<string>? tags) =>
        tags?.Contains("street_lantern", StringComparer.Ordinal) == true;

    /// <summary>The small solid fitting at overview zoom, relative to the Road tile.</summary>
    public Rect2 OverviewFitting(float stride)
    {
        var size = Math.Max(1, stride / 4f);
        return new(Post * (stride / 32f) - Vector2.One * size / 2, Vector2.One * size);
    }

    /// <summary>Uses the actual saved Road neighbour, including a wrapped east/west seam.</summary>
    public static StreetLanternLight? FromBuilding(OwnerWorldPlacedBuilding building, int worldWidth, bool wrapsEastWest)
    {
        if (!IsLantern(building.Tags) || building.Width != 1 || building.Height != 1 || building.Entrance is not { } road)
            return null;
        LanternStyle? style = building.Tags?.Contains("stone_lantern", StringComparer.Ordinal) == true
            ? LanternStyle.Stone
            : building.Tags?.Contains("hanging_lantern", StringComparer.Ordinal) == true ? LanternStyle.Hanging : null;
        if (style is null) return null;
        var dx = building.Position.X - road.X;
        if (wrapsEastWest && worldWidth > 0 && Math.Abs(dx) > worldWidth / 2)
            dx -= Math.Sign(dx) * worldWidth;
        DoorSide? edge = (dx, building.Position.Y - road.Y) switch
        {
            (0, -1) => DoorSide.North,
            (1, 0) => DoorSide.East,
            (-1, 0) => DoorSide.West,
            (0, 1) => DoorSide.South,
            _ => null,
        };
        return edge is { } side ? new(new(road.X, road.Y), side, style.Value) : null;
    }
}

/// <summary>
/// Warm light on the ground around buildings in use at night, drawn from
/// <see cref="NightLightShapes"/>. Light brightens and warms the ground under
/// it instead of painting over it, so grass and dirt keep their texture, and
/// the rows are drawn weakest first so the stronger of two overlapping lights
/// shows. Lantern fittings, such as a Warehouse's, show by day too.
/// </summary>
public partial class NightLightsLayer : Control
{
    /// <summary>
    /// Brightens the ground under each light row and pulls its hue toward
    /// the light's colour by the ground's own brightness, so lit grass turns
    /// warm instead of lime. A row's alpha is its strength; full alpha draws
    /// a light fitting's pixel in solid colour.
    /// </summary>
    private const string LightShader = """
        shader_type canvas_item;
        uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;
        void fragment() {
            vec3 ground = texture(screen_texture, SCREEN_UV).rgb;
            float strength = COLOR.a;
            float shade = dot(ground, vec3(0.3, 0.59, 0.11));
            vec3 brighter = ground * (vec3(1.0) + COLOR.rgb * strength * 2.0);
            vec3 warmed = shade * (vec3(0.5) + COLOR.rgb) * (1.0 + strength);
            COLOR = strength > 0.99
                ? vec4(COLOR.rgb, 1.0)
                : vec4(mix(brighter, warmed, clamp(strength * 1.4, 0.0, 0.55)), 1.0);
        }
        """;

    /// <summary>How often the light's drift steps on, in seconds: slow enough to keep the pixel-art feel.</summary>
    private const double StepSeconds = 0.125;

    private WorldTerrainLayer? source;
    private NightLayer? night;
    private IReadOnlyList<BuildingLight> buildings = [];
    private IReadOnlyList<StreetLanternLight> lanterns = [];
    private bool wrapsEastWest;
    private Rect2 drawnCamera;
    private int drawnTileSize;
    private float drawnDarkness;
    private double clock;
    private float drawnTime;

    public NightLightsLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Material = new ShaderMaterial { Shader = new Shader { Code = LightShader } };
    }

    /// <summary>The terrain layer whose camera this follows, and the night wash whose darkness sets the lights' strength.</summary>
    public void Follow(WorldTerrainLayer terrain, NightLayer wash)
    {
        source = terrain;
        night = wash;
    }

    /// <summary>The buildings that may be lit.</summary>
    public IReadOnlyList<BuildingLight> Buildings => buildings;

    /// <summary>Sets the buildings that may be lit.</summary>
    public void SetBuildings(IReadOnlyList<BuildingLight> next)
    {
        buildings = next;
        QueueRedraw();
    }

    public IReadOnlyList<StreetLanternLight> Lanterns => lanterns;

    public void SetLanterns(IReadOnlyList<StreetLanternLight> next, bool wraps)
    {
        lanterns = next;
        wrapsEastWest = wraps;
        QueueRedraw();
    }

    /// <summary>The cells drawn last, in map pixels, for the checks: light rows and fittings.</summary>
    public IReadOnlyList<(Rect2 Area, LightCellKind Kind)> DrawnCells { get; private set; } = [];

    public override void _Process(double delta)
    {
        if (source is null || night is null || !IsVisibleInTree()) return;
        clock += delta;
        var darkness = night.ShownDarkness;
        var step = (float)(Math.Floor(clock / StepSeconds) * StepSeconds);
        if ((darkness > 0 && step != drawnTime) || darkness != drawnDarkness ||
            ((buildings.Count > 0 || lanterns.Count > 0) &&
             (source.VisibleTiles != drawnCamera || source.TileSize != drawnTileSize)))
            QueueRedraw();
    }

    public override void _Draw()
    {
        drawnDarkness = night?.ShownDarkness ?? 0;
        drawnTime = (float)(Math.Floor(clock / StepSeconds) * StepSeconds);
        DrawnCells = [];
        if (source?.World is null) return;
        drawnCamera = source.VisibleTiles;
        drawnTileSize = source.TileSize;
        var stride = source.Stride;
        var visible = new Rect2I((Vector2I)drawnCamera.Position.Floor() - Vector2I.One * 2,
            (Vector2I)drawnCamera.Size.Ceil() + Vector2I.One * 4);
        if (source.TileSize < WorldTerrainLayer.SpriteTileMinimum)
        {
            var overview = new List<(Rect2, LightCellKind)>();
            if (drawnDarkness > 0)
                foreach (var (building, _) in VisibleBuildings(visible))
                    if (building.Shines) DrawSpeck(building, stride);
            foreach (var lantern in lanterns)
                foreach (var shift in wrapsEastWest ? new[] { -source.World.Width, 0, source.World.Width } : [0])
                {
                    var tile = lantern.RoadTile + new Vector2I(shift, 0);
                    if (!visible.HasPoint(tile)) continue;
                    var point = new Vector2(tile.X, tile.Y) * stride + lantern.Post * (stride / 32f);
                    var fitting = lantern.OverviewFitting(stride);
                    fitting.Position += new Vector2(tile.X, tile.Y) * stride;
                    var fittingColor = NightLightShapes.StreetLantern(lantern.Style, lantern.Post, lantern.Inward, 0, 0, 0)[0].Color;
                    DrawRect(fitting, fittingColor.Lerp(NightLayer.Wash, NightLayer.FullNightAlpha * drawnDarkness) with { A = 1 });
                    overview.Add((fitting, LightCellKind.Paint));
                    if (drawnDarkness <= 0.05f || lantern.Dark) continue;
                    var pool = new Rect2(point - Vector2.One * stride, Vector2.One * stride * 2);
                    var color = lantern.Style == LanternStyle.Stone ? NightLightShapes.Fire : NightLightShapes.Lamp;
                    DrawRect(pool, color with { A = 0.12f * drawnDarkness });
                    DrawRect(fitting, color with { A = 1 });
                    overview.Add((pool, LightCellKind.Light));
                    overview.Add((fitting, LightCellKind.Glow));
                }
            DrawnCells = overview;
            return;
        }

        var atlasSize = BuildingSprites.AtlasTileSize(source.TileSize);
        var snap = atlasSize == 16 ? 2 : 1;
        var unit = stride / 32f;
        var placed = new List<(Vector2 Origin, LightCell Cell)>();
        var roofs = new List<Rect2>();
        foreach (var (visibleBuilding, seed) in VisibleBuildings(visible))
        {
            var building = visibleBuilding.AtAtlas(atlasSize);
            var origin = new Vector2(building.Footprint.Position.X, building.Footprint.Position.Y) * stride;
            roofs.Add(Scaled(origin, building.Plan.Roof, unit));
            if (building.Plan.Wing is { } wing) roofs.Add(Scaled(origin, wing, unit));
            foreach (var cell in NightLightShapes.Building(building.Plan, building.Occupied, building.Working,
                drawnDarkness > 0.05f, drawnTime, seed, snap))
                placed.Add((origin, cell));
        }
        foreach (var lantern in lanterns)
            foreach (var shift in wrapsEastWest ? new[] { -source.World.Width, 0, source.World.Width } : [0])
            {
                var tile = lantern.RoadTile + new Vector2I(shift, 0);
                if (!visible.HasPoint(tile)) continue;
                var origin = new Vector2(tile.X, tile.Y) * stride;
                foreach (var cell in lantern.Cells(drawnDarkness, drawnTime, Seed(lantern.RoadTile), snap))
                    placed.Add((origin, cell));
            }
        // Weakest light first, so where pools overlap the stronger one shows; fittings go on top.
        var drawn = new List<(Rect2, LightCellKind)>();
        if (drawnDarkness > 0)
            foreach (var (origin, cell) in placed.Where(item => item.Cell.Kind == LightCellKind.Light).OrderBy(item => item.Cell.Strength))
            {
                foreach (var area in NightLightShapes.OutsideRoofs(Scaled(origin, cell.Area, unit), roofs))
                {
                    DrawRect(area, cell.Color with { A = Math.Min(0.98f, cell.Strength * drawnDarkness) });
                    drawn.Add((area, cell.Kind));
                }
            }
        foreach (var (origin, cell) in placed.Where(item => item.Cell.Kind != LightCellKind.Light))
        {
            var color = cell.Kind == LightCellKind.Glow
                ? cell.Color
                : cell.Color.Lerp(NightLayer.Wash, NightLayer.FullNightAlpha * drawnDarkness);
            var area = Scaled(origin, cell.Area, unit);
            DrawRect(area, color with { A = 1 });
            drawn.Add((area, cell.Kind));
        }
        DrawnCells = drawn;
    }

    private IEnumerable<(BuildingLight Building, int Seed)> VisibleBuildings(Rect2I visible)
    {
        var shifts = source!.WrapsEastWest ? new[] { -source.World!.Width, 0, source.World.Width } : [0];
        foreach (var building in buildings)
            foreach (var shift in shifts)
            {
                var footprint = building.Footprint with { Position = building.Footprint.Position + new Vector2I(shift, 0) };
                if (footprint.Intersects(visible))
                    yield return (building with { Footprint = footprint }, Seed(building.Footprint.Position));
            }
    }

    private static Rect2 Scaled(Vector2 origin, Rect2 units, float unit) =>
        new(origin + units.Position * unit, units.Size * unit);

    /// <summary>A stable number per map tile, so each light drifts in its own way on every client.</summary>
    private static int Seed(Vector2I tile) => unchecked((tile.X * 73856093) ^ (tile.Y * 19349663)) & 0xFFFFFF;

    /// <summary>At overview zoom a lit building is a warm speck, so a Town at night reads as a cluster of lights.</summary>
    private void DrawSpeck(BuildingLight building, float stride)
    {
        var origin = new Vector2(building.Footprint.Position.X, building.Footprint.Position.Y) * stride;
        var size = new Vector2(building.Footprint.Size.X, building.Footprint.Size.Y) * stride;
        var color = building.Working ? NightLightShapes.Fire : NightLightShapes.Lamp;
        DrawRect(new Rect2(origin - Vector2.One * stride * 0.5f, size + Vector2.One * stride), color with { A = 0.12f * drawnDarkness });
        DrawRect(new Rect2(origin, size), color with { A = 0.26f * drawnDarkness });
    }
}
