using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Which lights a building shows at night.</summary>
[Flags]
public enum BuildingGlow : byte
{
    None = 0,
    /// <summary>Light from the windows, on the ground just outside each wall.</summary>
    Windows = 1,
    /// <summary>Light spilling out of the open door onto the doorstep.</summary>
    Door = 2,
    /// <summary>A small lantern by the door, for buildings without windows.</summary>
    Lantern = 4,
    /// <summary>The Blacksmith's open forge in its yard.</summary>
    Forge = 8,
    /// <summary>A campfire.</summary>
    Fire = 16,
}

/// <summary>One building's lights: where it stands, which way it faces and what is lit.</summary>
public readonly record struct BuildingLight(Rect2I Footprint, BuildingKind Kind, BuildingDoor Door, BuildingGlow Glow);

/// <summary>Mockup styles for a street lantern that agents build beside a Road.</summary>
public enum LanternStyle : byte
{
    /// <summary>A square glass lantern on top of a wooden post.</summary>
    Post,
    /// <summary>A round stone pillar with an open flame in a bowl.</summary>
    Stone,
    /// <summary>A post at the roadside with an arm that hangs a lantern over the Road.</summary>
    Hanging,
}

/// <summary>A street lantern on one edge of a Road tile.</summary>
public readonly record struct StreetLantern(Vector2I Tile, DoorSide Edge, LanternStyle Style);

/// <summary>
/// Warm light on the ground around lit buildings at night. Roofs never glow:
/// light comes out of windows and doors onto the ground beside the walls, so
/// a lived-in House shows patches on each side and a pool at its door, while
/// a windowless Warehouse shows at most a lantern by its loading door. The
/// pools are stepped on the art's pixel grid, fade in with dusk, and add to
/// the night wash below them, so lit ground is never brighter than daylight.
/// </summary>
public partial class NightLightsLayer : Control
{
    private static readonly Color Lamp = new(1f, 0.56f, 0.18f);
    private static readonly Color Ember = new(1f, 0.4f, 0.1f);
    private static readonly Color FlameColor = new("FFD27A");

    /// <summary>
    /// Brightens the ground under each light shape in its warm colour instead
    /// of painting over it, so grass, roads and stones keep their texture.
    /// The shape's alpha is the light's strength; each shape replaces what is
    /// below it, so the stepped rings are drawn from the outside in. Full
    /// alpha draws a flame pixel in solid colour.
    /// </summary>
    private const string LightShader = """
        shader_type canvas_item;
        uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;
        void fragment() {
            vec3 ground = texture(screen_texture, SCREEN_UV).rgb;
            float strength = COLOR.a;
            // Full alpha marks a flame itself, drawn as solid colour.
            // Brighten the ground, then pull its hue toward the light's colour
            // by its own brightness, so lit grass turns warm instead of lime.
            float shade = dot(ground, vec3(0.3, 0.59, 0.11));
            vec3 brighter = ground * (vec3(1.0) + COLOR.rgb * strength * 2.0);
            vec3 warmed = shade * (vec3(0.5) + COLOR.rgb) * (1.0 + strength);
            COLOR = strength > 0.99
                ? vec4(COLOR.rgb, 1.0)
                : vec4(mix(brighter, warmed, clamp(strength * 1.4, 0.0, 0.55)), 1.0);
        }
        """;

    /// <summary>How long a fire keeps one flicker step, in seconds.</summary>
    private const double FlickerSeconds = 0.14;

    private WorldTerrainLayer? source;
    private NightLayer? night;
    private IReadOnlyList<BuildingLight> lights = [];
    private IReadOnlyList<StreetLantern> lanterns = [];
    private Rect2 drawnCamera;
    private int drawnTileSize;
    private float drawnDarkness;
    private double flickerClock;
    private int flickerStep;

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

    /// <summary>The buildings to light.</summary>
    public void SetLights(IReadOnlyList<BuildingLight> next)
    {
        lights = next;
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
        var darkness = night.ShownDarkness;
        var flickers = darkness > 0 && (lanterns.Any(lantern => lantern.Style == LanternStyle.Stone) ||
            lights.Any(light => (light.Glow & (BuildingGlow.Fire | BuildingGlow.Forge)) != 0));
        if (flickers && (flickerClock += delta) >= FlickerSeconds)
        {
            flickerClock = 0;
            flickerStep++;
            QueueRedraw();
        }
        if (darkness != drawnDarkness || ((darkness > 0 || lanterns.Count > 0) &&
            (source.VisibleTiles != drawnCamera || source.TileSize != drawnTileSize)))
            QueueRedraw();
    }

    public override void _Draw()
    {
        drawnDarkness = night?.ShownDarkness ?? 0;
        if (source?.World is null || (lanterns.Count == 0 && (drawnDarkness <= 0 || lights.Count == 0))) return;
        drawnCamera = source.VisibleTiles;
        drawnTileSize = source.TileSize;
        var stride = source.Stride;
        var snap = BuildingSprites.AtlasTileSize(source.TileSize) == 16 ? 2 : 1;
        var visible = new Rect2I((Vector2I)drawnCamera.Position.Floor() - Vector2I.One * 2,
            (Vector2I)drawnCamera.Size.Ceil() + Vector2I.One * 4);
        if (drawnDarkness > 0)
            foreach (var light in lights)
            {
                if (light.Glow == BuildingGlow.None || !light.Footprint.Intersects(visible)) continue;
                var origin = new Vector2(light.Footprint.Position.X, light.Footprint.Position.Y) * stride;
                if (source.TileSize < WorldTerrainLayer.SpriteTileMinimum)
                    DrawSpeck(light, origin, stride);
                else
                    DrawBuilding(light, new Pen(this, origin, stride / 32f, drawnDarkness, snap));
            }
        if (source.TileSize < WorldTerrainLayer.SpriteTileMinimum) return;
        var shown = lanterns.Where(lantern => visible.HasPoint(lantern.Tile)).ToArray();
        // All glows first, then every lantern over them, so no pool paints over a neighbouring post.
        foreach (var lantern in shown)
            if (drawnDarkness > 0)
                LanternGlow(lantern, new Pen(this, new Vector2(lantern.Tile.X, lantern.Tile.Y) * stride, stride / 32f, drawnDarkness, snap));
        foreach (var lantern in shown)
            LanternSprite(lantern, new Pen(this, new Vector2(lantern.Tile.X, lantern.Tile.Y) * stride, stride / 32f, drawnDarkness, snap));
    }

    /// <summary>Where a lantern's post stands and where its light hangs, in the Road tile's 32-unit space.</summary>
    private static (Vector2 Post, Vector2 Light) LanternSpots(StreetLantern lantern)
    {
        var (post, inward) = lantern.Edge switch
        {
            DoorSide.North => (new Vector2(16, 4), new Vector2(0, 1)),
            DoorSide.East => (new Vector2(28, 16), new Vector2(-1, 0)),
            DoorSide.West => (new Vector2(4, 16), new Vector2(1, 0)),
            _ => (new Vector2(16, 28), new Vector2(0, -1)),
        };
        return (post, lantern.Style == LanternStyle.Hanging ? post + inward * 8 : post);
    }

    private void LanternGlow(StreetLantern lantern, Pen pen)
    {
        var (_, light) = LanternSpots(lantern);
        if (lantern.Style == LanternStyle.Stone)
            pen.SoftPool(light.X, light.Y, 20, Ember, Flicker(new Rect2I(lantern.Tile, Vector2I.One), 2));
        else
            pen.SoftPool(light.X, light.Y, 24, Lamp, 0);
    }

    private static readonly Color Iron = new("2A2420");
    private static readonly Color IronCap = new("3E3630");
    private static readonly Color GlassLit = new("FFD27A");
    private static readonly Color GlassHot = new("FFF1C4");
    private static readonly Color GlassDark = new("8E948C");
    private static readonly Color GlassShine = new("B9BEB5");
    private static readonly Color Stone = new("8D8A83");
    private static readonly Color StoneEdge = new("4A4743");
    private static readonly Color StoneLight = new("B2AFA7");
    private static readonly Color Soot = new("2E2A27");
    private static readonly Color FlameHot = new("FFE9A8");
    private static readonly Color FlameBody = new("FFA444");
    private static readonly Color Wood = new("6B4A2E");
    private static readonly Color WoodEdge = new("2C1E12");

    /// <summary>
    /// The lantern itself, seen straight from above: a lit lantern's glass
    /// shines, everything else takes the night tint like the ground around it.
    /// </summary>
    private void LanternSprite(StreetLantern lantern, Pen pen)
    {
        var (post, light) = LanternSpots(lantern);
        var lit = drawnDarkness > 0.05f;
        Color Night(Color color) => color.Lerp(NightLayer.Wash, NightLayer.FullNightAlpha * drawnDarkness);
        switch (lantern.Style)
        {
            case LanternStyle.Post:
                pen.Solid(post.X - 3, post.Y - 3, 6, 6, Night(Iron));
                pen.Solid(post.X - 2, post.Y - 2, 4, 4, lit ? GlassLit : Night(GlassDark));
                if (!lit) pen.Solid(post.X - 2, post.Y - 2, 1, 1, Night(GlassShine));
                pen.Solid(post.X - 1, post.Y - 1, 2, 2, Night(IronCap));
                break;
            case LanternStyle.Stone:
                pen.SolidDisc(post.X, post.Y, 4.5f, Night(StoneEdge));
                pen.SolidDisc(post.X, post.Y, 3.5f, Night(Stone));
                pen.Solid(post.X - 3, post.Y - 2, 1, 2, Night(StoneLight));
                pen.Solid(post.X - 2, post.Y - 3, 2, 1, Night(StoneLight));
                pen.Solid(post.X - 1.5f, post.Y - 1.5f, 3, 3, Night(Soot));
                if (lit)
                {
                    pen.Solid(post.X - 1, post.Y - 1, 2, 2, FlameBody);
                    pen.Solid(post.X - 1, post.Y - 1, 1, 1, FlameHot);
                }
                break;
            default:
                var arm = light - post;
                var step = arm.Normalized();
                for (var k = 1; k < (int)arm.Length(); k++)
                    pen.Solid(post.X + step.X * k - 0.5f, post.Y + step.Y * k - 0.5f, 1, 1, Night(Iron));
                pen.Solid(post.X - 2, post.Y - 2, 4, 4, Night(WoodEdge));
                pen.Solid(post.X - 1, post.Y - 1, 2, 2, Night(Wood));
                pen.Solid(light.X - 2.5f, light.Y - 2.5f, 5, 5, Night(Iron));
                pen.Solid(light.X - 1.5f, light.Y - 1.5f, 3, 3, lit ? GlassLit : Night(GlassDark));
                pen.Solid(light.X - 0.5f, light.Y - 0.5f, 1, 1, lit ? GlassHot : Night(IronCap));
                break;
        }
    }

    /// <summary>At overview zoom a lit building is a warm speck, so a Town at night reads as a cluster of lights.</summary>
    private void DrawSpeck(BuildingLight light, Vector2 origin, float stride)
    {
        var size = new Vector2(light.Footprint.Size.X, light.Footprint.Size.Y) * stride;
        var color = (light.Glow & (BuildingGlow.Fire | BuildingGlow.Forge)) != 0 ? Ember : Lamp;
        var strength = light.Glow == BuildingGlow.Lantern ? 0.5f : 1f;
        DrawRect(new Rect2(origin - Vector2.One * stride * 0.5f, size + Vector2.One * stride), color with { A = 0.10f * strength * drawnDarkness });
        DrawRect(new Rect2(origin, size), color with { A = 0.22f * strength * drawnDarkness });
    }

    private void DrawBuilding(BuildingLight light, Pen pen)
    {
        var (roof, yard, doorMiddle) = BuildingSprites.Plan(light.Kind, light.Footprint.Size.X, light.Footprint.Size.Y, light.Door);
        if ((light.Glow & BuildingGlow.Windows) != 0)
            foreach (var side in new[] { DoorSide.South, DoorSide.North, DoorSide.East, DoorSide.West })
                foreach (var along in Windows(roof, side, light.Door.Side == side ? doorMiddle : null))
                    pen.Fan(roof, side, along, 3, 5, 0.35f, Lamp, 1f);
        if ((light.Glow & BuildingGlow.Door) != 0)
            pen.Fan(roof, light.Door.Side, doorMiddle, 3.5f, 9, 0.45f, Lamp, 1.15f);
        if ((light.Glow & BuildingGlow.Lantern) != 0)
        {
            var (x, y) = Outside(roof, light.Door.Side, doorMiddle + (light.Kind == BuildingKind.Warehouse ? 11 : 6), 2);
            pen.Pool(x, y, 8, Lamp, 0.9f, 0);
            pen.Flame(x, y);
        }
        if ((light.Glow & BuildingGlow.Forge) != 0 && yard is { } forgeYard)
        {
            var (x, y) = Hearth(forgeYard);
            pen.Pool(x, y, 13, Ember, 1.05f, Flicker(light.Footprint, 0));
        }
        if ((light.Glow & BuildingGlow.Fire) != 0)
            pen.Pool(light.Footprint.Size.X * 16f, light.Footprint.Size.Y * 16f, 20, Ember, 1.15f, Flicker(light.Footprint, 1));
    }

    /// <summary>
    /// Window positions along one wall: one per tile of wall, kept clear of
    /// the door, and none beside the door on a wall too short for both.
    /// </summary>
    private static IEnumerable<float> Windows(Rect2 roof, DoorSide side, float? door)
    {
        var horizontal = side is DoorSide.South or DoorSide.North;
        var from = horizontal ? roof.Position.X : roof.Position.Y;
        var length = horizontal ? roof.Size.X : roof.Size.Y;
        if (door is not null && length < 40) yield break;
        var count = Math.Max(1, (int)Math.Round(length / 32f));
        if (door is not null) count = Math.Max(2, count);
        for (var i = 0; i < count; i++)
        {
            var along = from + length * (i + 0.5f) / count;
            if (door is { } middle && Math.Abs(along - middle) < 10)
                along = middle + (along < middle ? -12 : 12);
            if (along < from + 5 || along > from + length - 5) continue;
            yield return along;
        }
    }

    private static (float X, float Y) Outside(Rect2 roof, DoorSide side, float along, float out_) => side switch
    {
        DoorSide.North => (along, roof.Position.Y - out_),
        DoorSide.East => (roof.End.X + out_, along),
        DoorSide.West => (roof.Position.X - out_, along),
        _ => (along, roof.End.Y + out_),
    };

    /// <summary>The forge hearth's spot in the Blacksmith's yard, matching the approved drawing.</summary>
    private static (float X, float Y) Hearth(Rect2 yard)
    {
        var tall = yard.Size.Y >= yard.Size.X;
        var compact = (tall ? yard.Size.Y : yard.Size.X) < 40;
        var f = compact ? 0.27f : 0.22f;
        return tall
            ? (yard.Position.X + yard.Size.X / 2, yard.Position.Y + yard.Size.Y * f)
            : (yard.Position.X + yard.Size.X * f, yard.Position.Y + yard.Size.Y / 2);
    }

    /// <summary>A small radius change, the same for every client at the same moment, that makes a fire flicker.</summary>
    private int Flicker(Rect2I footprint, int salt)
    {
        var hash = HashCode.Combine(footprint.Position.X, footprint.Position.Y, flickerStep, salt);
        return (hash & 7) switch { 0 => -1, 1 or 2 => 1, _ => 0 };
    }

    /// <summary>Draws stepped light shapes in a building's 32-unit space, snapped to the art's pixels.</summary>
    private readonly struct Pen(NightLightsLayer layer, Vector2 origin, float unit, float darkness, int snap)
    {
        /// <summary>Strength of the outer, middle and inner steps; each inner step is drawn over the outer ones.</summary>
        private static readonly float[] Steps = [0.13f, 0.22f, 0.32f];

        /// <summary>
        /// Light thrown outward from a wall opening: rows that start at the
        /// roof edge and widen as they leave the wall, rounding off at the
        /// far end, so the roof itself is never lit.
        /// </summary>
        public void Fan(Rect2 roof, DoorSide side, float along, float halfWidth, float reach, float spread, Color color, float strength)
        {
            for (var step = 0; step < Steps.Length; step++)
            {
                var grow = Steps.Length - 1 - step;
                var half = halfWidth + grow * 1.5f;
                var depth = reach + grow * reach * 0.5f;
                for (var k = 0f; k < depth; k += snap)
                {
                    var t = (k + snap / 2f) / depth;
                    var round = t > 0.6f ? MathF.Sqrt(Math.Max(0, 1 - MathF.Pow((t - 0.6f) / 0.4f, 2))) : 1;
                    var width = Snap((half + k * spread) * 2 * round);
                    if (width <= 0) break;
                    var start = Snap(along - width / 2);
                    var rect = side switch
                    {
                        DoorSide.North => new Rect2(start, Snap(roof.Position.Y) - k - snap, width, snap),
                        DoorSide.East => new Rect2(Snap(roof.End.X) + k, start, snap, width),
                        DoorSide.West => new Rect2(Snap(roof.Position.X) - k - snap, start, snap, width),
                        _ => new Rect2(start, Snap(roof.End.Y) + k, width, snap),
                    };
                    Fill(rect, color, Steps[step] * strength);
                }
            }
        }

        /// <summary>A stepped round pool of light, for fires and lanterns.</summary>
        public void Pool(float x, float y, float radius, Color color, float strength, int flicker)
        {
            var cx = Snap(x);
            var cy = Snap(y);
            for (var step = 0; step < Steps.Length; step++)
            {
                var r = Math.Max(snap, radius * (step + 1) / Steps.Length + flicker);
                for (var k = -Snap(r); k < r; k += snap)
                {
                    var t = (k + snap / 2f) / r;
                    var width = Snap(r * MathF.Sqrt(Math.Max(0, 1 - t * t)) * 2);
                    if (width <= 0) continue;
                    Fill(new Rect2(cx - width / 2, cy + k, width, snap), color, Steps[step] * strength);
                }
            }
        }

        /// <summary>A solid art-pixel rectangle, drawn as it is rather than as light.</summary>
        public void Solid(float x, float y, float width, float height, Color color) =>
            layer.DrawRect(new Rect2(origin + new Vector2(Snap(x), Snap(y)) * unit,
                new Vector2(Math.Max(snap, Snap(width)), Math.Max(snap, Snap(height))) * unit), color with { A = 1 });

        /// <summary>A solid stepped disc, for round stonework.</summary>
        public void SolidDisc(float x, float y, float radius, Color color)
        {
            var cx = Snap(x);
            var cy = Snap(y);
            for (var k = -Snap(radius); k < radius; k += snap)
            {
                var t = (k + snap / 2f) / radius;
                var width = Snap(radius * MathF.Sqrt(Math.Max(0, 1 - t * t)) * 2);
                if (width > 0) Solid(cx - width / 2, cy + k, width, snap, color);
            }
        }

        /// <summary>A lantern's flame: one solid warm art pixel.</summary>
        public void Flame(float x, float y) =>
            layer.DrawRect(new Rect2(origin + new Vector2(Snap(x), Snap(y)) * unit, Vector2.One * snap * unit), FlameColor);

        /// <summary>
        /// A wide pool that fades over five steps, for a street lantern
        /// lighting the Road around it.
        /// </summary>
        public void SoftPool(float x, float y, float radius, Color color, int flicker)
        {
            var cx = Snap(x);
            var cy = Snap(y);
            const int rings = 5;
            for (var step = 0; step < rings; step++)
            {
                var r = Math.Max(snap, radius * (rings - step) / rings + flicker);
                var strength = 0.36f * MathF.Pow((step + 1) / (float)rings, 1.4f);
                for (var k = -Snap(r); k < r; k += snap)
                {
                    var t = (k + snap / 2f) / r;
                    var width = Snap(r * MathF.Sqrt(Math.Max(0, 1 - t * t)) * 2);
                    if (width > 0) Fill(new Rect2(cx - width / 2, cy + k, width, snap), color, strength);
                }
            }
        }

        private float Snap(float value) => MathF.Floor(value / snap + 0.5f) * snap;

        private void Fill(Rect2 units, Color color, float alpha) =>
            layer.DrawRect(new Rect2(origin + units.Position * unit, units.Size * unit), color with { A = Math.Min(1, alpha * darkness) });
    }
}
