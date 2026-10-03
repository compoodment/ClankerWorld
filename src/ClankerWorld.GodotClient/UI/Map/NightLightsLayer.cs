using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>One building's lights tonight: where it stands, how its lights are laid out and who is using it.</summary>
public readonly record struct BuildingLight(Rect2I Footprint, LightPlan Plan, bool Occupied, bool Working)
{
    /// <summary>Whether anything of this building shines, for the overview speck.</summary>
    public bool Shines => Plan.Design switch
    {
        LitDesign.Silo or LitDesign.MarketStall => false,
        LitDesign.Campfire or LitDesign.Port => true,
        _ => Occupied || Working,
    };
}

/// <summary>A street lantern on one edge of a Road tile.</summary>
public readonly record struct StreetLantern(Vector2I Tile, DoorSide Edge, LanternStyle Style);

/// <summary>
/// Warm light on the ground around lit buildings and street lanterns at
/// night, drawn from <see cref="NightLightShapes"/>. Light brightens and
/// warms the ground under it instead of painting over it, so grass and dirt
/// keep their texture, and the rows are drawn weakest first so overlapping
/// lights add up to the stronger one. Lantern fittings show by day too.
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
    private IReadOnlyList<StreetLantern> lanterns = [];
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
    public void SetBuildings(IReadOnlyList<BuildingLight> next)
    {
        buildings = next;
        QueueRedraw();
    }

    /// <summary>Street lanterns, drawn by day too and lit at night.</summary>
    public void SetLanterns(IReadOnlyList<StreetLantern> next)
    {
        lanterns = next;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (source is null || night is null || !IsVisibleInTree()) return;
        clock += delta;
        var darkness = night.ShownDarkness;
        var step = (float)(Math.Floor(clock / StepSeconds) * StepSeconds);
        if ((darkness > 0 && step != drawnTime) || darkness != drawnDarkness ||
            ((darkness > 0 || lanterns.Count > 0 || buildings.Count > 0) &&
             (source.VisibleTiles != drawnCamera || source.TileSize != drawnTileSize)))
            QueueRedraw();
    }

    public override void _Draw()
    {
        drawnDarkness = night?.ShownDarkness ?? 0;
        drawnTime = (float)(Math.Floor(clock / StepSeconds) * StepSeconds);
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
        foreach (var lantern in lanterns)
        {
            if (!visible.HasPoint(lantern.Tile)) continue;
            var origin = new Vector2(lantern.Tile.X, lantern.Tile.Y) * stride;
            var (post, inward) = LanternSpot(lantern.Edge);
            foreach (var cell in NightLightShapes.StreetLantern(lantern.Style, post, inward, drawnDarkness, drawnTime, Seed(lantern.Tile), snap))
                placed.Add((origin, cell));
        }

        // Weakest light first, so where pools overlap the stronger one shows; fittings go on top.
        if (drawnDarkness > 0)
            foreach (var (origin, cell) in placed.Where(item => item.Cell.Kind == LightCellKind.Light).OrderBy(item => item.Cell.Strength))
                DrawRect(Scaled(origin, cell.Area, unit), cell.Color with { A = Math.Min(0.98f, cell.Strength * drawnDarkness) });
        foreach (var (origin, cell) in placed.Where(item => item.Cell.Kind != LightCellKind.Light))
        {
            var color = cell.Kind == LightCellKind.Glow
                ? cell.Color
                : cell.Color.Lerp(NightLayer.Wash, NightLayer.FullNightAlpha * drawnDarkness);
            DrawRect(Scaled(origin, cell.Area, unit), color with { A = 1 });
        }
    }

    private static Rect2 Scaled(Vector2 origin, Rect2 units, float unit) =>
        new(origin + units.Position * unit, units.Size * unit);

    /// <summary>Where a lantern's post stands on its Road tile, and which way its arm reaches.</summary>
    private static (Vector2 Post, Vector2 Inward) LanternSpot(DoorSide edge) => edge switch
    {
        DoorSide.North => (new Vector2(16, 4), new Vector2(0, 1)),
        DoorSide.East => (new Vector2(28, 16), new Vector2(-1, 0)),
        DoorSide.West => (new Vector2(4, 16), new Vector2(1, 0)),
        _ => (new Vector2(16, 28), new Vector2(0, -1)),
    };

    /// <summary>A stable number per map tile, so each light drifts in its own way on every client.</summary>
    private static int Seed(Vector2I tile) => unchecked((tile.X * 73856093) ^ (tile.Y * 19349663)) & 0xFFFFFF;

    /// <summary>At overview zoom a lit building is a warm speck, so a Town at night reads as a cluster of lights.</summary>
    private void DrawSpeck(BuildingLight building, float stride)
    {
        var origin = new Vector2(building.Footprint.Position.X, building.Footprint.Position.Y) * stride;
        var size = new Vector2(building.Footprint.Size.X, building.Footprint.Size.Y) * stride;
        var color = building.Plan.Design is LitDesign.Campfire || building.Working ? NightLightShapes.Fire : NightLightShapes.Lamp;
        DrawRect(new Rect2(origin - Vector2.One * stride * 0.5f, size + Vector2.One * stride), color with { A = 0.12f * drawnDarkness });
        DrawRect(new Rect2(origin, size), color with { A = 0.26f * drawnDarkness });
    }
}
