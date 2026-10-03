using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>One building's lights tonight: where it stands, how its lights are laid out and who is using it.</summary>
public readonly record struct BuildingLight(Rect2I Footprint, LightPlan Plan, bool Occupied, bool Working)
{
    /// <summary>Whether anything of this building shines, for the overview speck.</summary>
    public bool Shines => Plan.Design switch
    {
        LitDesign.Silo or LitDesign.MarketStall => false,
        LitDesign.Port => true,
        _ => Occupied || Working,
    };
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

    /// <summary>The cells drawn last, in map pixels, for the checks: light rows and fittings.</summary>
    public IReadOnlyList<(Rect2 Area, LightCellKind Kind)> DrawnCells { get; private set; } = [];

    public override void _Process(double delta)
    {
        if (source is null || night is null || !IsVisibleInTree()) return;
        clock += delta;
        var darkness = night.ShownDarkness;
        var step = (float)(Math.Floor(clock / StepSeconds) * StepSeconds);
        if ((darkness > 0 && step != drawnTime) || darkness != drawnDarkness ||
            (buildings.Count > 0 &&
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
            if (drawnDarkness > 0)
                foreach (var building in buildings)
                    if (building.Shines && building.Footprint.Intersects(visible)) DrawSpeck(building, stride);
            return;
        }

        var snap = BuildingSprites.AtlasTileSize(source.TileSize) == 16 ? 2 : 1;
        var unit = stride / 32f;
        var placed = new List<(Vector2 Origin, LightCell Cell)>();
        foreach (var building in buildings)
        {
            if (!building.Footprint.Intersects(visible)) continue;
            var origin = new Vector2(building.Footprint.Position.X, building.Footprint.Position.Y) * stride;
            foreach (var cell in NightLightShapes.Building(building.Plan, building.Occupied, building.Working,
                drawnDarkness > 0.05f, drawnTime, Seed(building.Footprint.Position), snap))
                placed.Add((origin, cell));
        }
        // Weakest light first, so where pools overlap the stronger one shows; fittings go on top.
        var drawn = new List<(Rect2, LightCellKind)>();
        if (drawnDarkness > 0)
            foreach (var (origin, cell) in placed.Where(item => item.Cell.Kind == LightCellKind.Light).OrderBy(item => item.Cell.Strength))
            {
                var area = Scaled(origin, cell.Area, unit);
                DrawRect(area, cell.Color with { A = Math.Min(0.98f, cell.Strength * drawnDarkness) });
                drawn.Add((area, cell.Kind));
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
