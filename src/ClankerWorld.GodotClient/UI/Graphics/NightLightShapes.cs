using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>How a <see cref="LightCell"/> is drawn.</summary>
public enum LightCellKind : byte
{
    /// <summary>Warms and brightens whatever is below it by the cell's strength.</summary>
    Light,
    /// <summary>A solid art pixel of a light fitting, tinted by the night like the ground.</summary>
    Paint,
    /// <summary>A solid art pixel that shines (lit glass or a flame), never tinted.</summary>
    Glow,
}

/// <summary>One row of art pixels of light or of a light fitting, in a building's 32-unit tile space.</summary>
public readonly record struct LightCell(Rect2 Area, Color Color, float Strength, LightCellKind Kind);

/// <summary>Every design that has night lights, built or still waiting for its feature.</summary>
public enum LitDesign : byte
{
    House,
    Farmhouse,
    Store,
    TailorShop,
    Clinic,
    Restaurant,
    Workshop,
    Generic,
    Blacksmith,
    Warehouse,
    Silo,
    MarketHall,
    MarketStall,
    TownHall,
    Port,
}

/// <summary>Street lantern styles agents build beside Roads.</summary>
public enum LanternStyle : byte
{
    /// <summary>A round stone pillar with an open flame in a bowl.</summary>
    Stone,
    /// <summary>A post at the roadside with an arm that hangs a lantern over the Road.</summary>
    Hanging,
}

/// <summary>
/// Where a building's lights can come from, in 32-unit tile space relative
/// to its footprint: its roof, door, yard and, for a Port, the spot of its
/// lantern. A cross-plan building such as the Town Hall gives its main roof
/// as <see cref="Roof"/> and the lower wings across it as <see cref="Wing"/>.
/// </summary>
public readonly record struct LightPlan(LitDesign Design, Rect2 Roof, Rect2? Yard, DoorSide Door, float DoorMiddle,
    Vector2? Lantern = null, Rect2? Wing = null);

/// <summary>
/// The shapes of light at night, shared by the map and the art preview so
/// both draw exactly the same thing. Light comes out of windows on a
/// building's front and sides, out of its open door, from a forge or a
/// campfire, or from a lantern; it never lights a roof. Each pool has a
/// ragged edge that drifts slowly, and fire flickers faster, so the light
/// looks alive without moving more than an art pixel or two. Everything is
/// stepped on the art's pixel grid.
/// </summary>
public static class NightLightShapes
{
    public static readonly Color Lamp = new(1f, 0.56f, 0.18f);
    public static readonly Color Fire = new(1f, 0.4f, 0.1f);

    private static readonly Color Iron = new("2A2420");
    private static readonly Color IronLight = new("4A403A");
    private static readonly Color GlassLit = new("FFD27A");
    private static readonly Color GlassHot = new("FFF1C4");
    private static readonly Color GlassDark = new("8E948C");
    private static readonly Color Stone = new("8D8A83");
    private static readonly Color StoneEdge = new("4A4743");
    private static readonly Color StoneLight = new("B2AFA7");
    private static readonly Color Soot = new("2E2A27");
    private static readonly Color FlameHot = new("FFE9A8");
    private static readonly Color FlameBody = new("FFA444");
    private static readonly Color FlameEdge = new("E0662A");
    private static readonly Color Wood = new("6B4A2E");
    private static readonly Color WoodEdge = new("2C1E12");

    /// <summary>Strength of the outer, middle and inner steps of a window or door spill.</summary>
    private static readonly float[] SpillSteps = [0.12f, 0.2f, 0.3f];

    /// <summary>
    /// The lights a building shows: windows and door while someone is inside,
    /// work lights while a job runs there, and lights that burn every night
    /// once built. Lantern fittings show by day too, unlit unless it is
    /// <paramref name="night"/>. <paramref name="time"/> is in seconds and
    /// drives the drift.
    /// </summary>
    public static List<LightCell> Building(LightPlan plan, bool occupied, bool working, bool night, float time, int seed, int snap = 1)
    {
        var cells = new List<LightCell>();
        var pen = new Pen(cells, snap, plan.Roof, time, seed);
        var lived = occupied || working;
        switch (plan.Design)
        {
            case LitDesign.Silo or LitDesign.MarketStall:
                break;
            case LitDesign.Warehouse:
                // No windows: a lantern on the wall by the loading doors, lit while someone fetches or stores goods.
                pen.WallLantern(plan.Door, plan.DoorMiddle + 12, lit: night && occupied);
                break;
            case LitDesign.Blacksmith:
                if (working && plan.Yard is { } yard) pen.Pool(ForgeHearth(yard), 17, 4, Fire, 0.44f, flicker: true, clip: true);
                if (occupied) Openings(pen, plan, windowGap: 32, doorReach: 9);
                break;
            case LitDesign.TownHall:
                // Tall windows close together along the front and sides, and a wide spill over the forecourt.
                if (lived) Openings(pen, plan, windowGap: 14, doorReach: 14, doorHalf: 6);
                break;
            case LitDesign.MarketHall:
                // Light from the open arcade along the whole front, between its posts.
                if (lived)
                {
                    Openings(pen, plan, windowGap: 32, doorReach: 8);
                    foreach (var along in Along(plan.Roof, plan.Door, 10, null))
                        pen.Spill(plan.Roof, plan.Door, along, 2.5f, 5, 0.25f, Lamp, 0.8f);
                }
                break;
            case LitDesign.Port:
                // The pier's lantern burns every night so boats can find the Port; the shed is lit while someone is in it.
                if (plan.Lantern is { } pier) pen.HangingLantern(pier, pier, lit: night, armless: true);
                if (lived) Openings(pen, plan, windowGap: 32, doorReach: 8);
                break;
            case LitDesign.Restaurant:
                if (lived)
                {
                    Openings(pen, plan, windowGap: 32, doorReach: 9);
                    if (plan.Yard is { } terrace)
                    {
                        var middle = terrace.Position + terrace.Size / 2;
                        pen.HangingLantern(middle, middle, lit: night, armless: true, radius: 15);
                    }
                }
                break;
            default:
                // House, Farmhouse, Store, Tailor Shop, Clinic, Workshop and any other building.
                if (lived) Openings(pen, plan, windowGap: 32, doorReach: 9);
                break;
        }
        return cells;
    }

    /// <summary>
    /// A street lantern standing at <paramref name="post"/> in a Road tile's
    /// 32-unit space, its light hanging toward <paramref name="inward"/>.
    /// It is drawn by day too; at night its glass or flame shines.
    /// </summary>
    public static List<LightCell> StreetLantern(LanternStyle style, Vector2 post, Vector2 inward, float darkness, float time, int seed, int snap = 1)
    {
        var cells = new List<LightCell>();
        var pen = new Pen(cells, snap, new Rect2(), time, seed);
        var lit = darkness > 0.05f;
        if (style == LanternStyle.Stone)
        {
            if (lit) pen.Pool(post, 21, 5, Fire, 0.36f, flicker: true, clip: false);
            pen.StoneLamp(post, lit);
        }
        else
        {
            pen.HangingLantern(post, post + inward * 8, lit);
        }
        return cells;
    }

    /// <summary>
    /// Windows on the front and both sides, but not the back, and the door's
    /// spill. On a cross plan the wings cover part of the main side walls, so
    /// those windows move to the wings' own end walls.
    /// </summary>
    private static void Openings(Pen pen, LightPlan plan, float windowGap, float doorReach, float doorHalf = 3.5f)
    {
        var back = plan.Door switch
        {
            DoorSide.North => DoorSide.South,
            DoorSide.South => DoorSide.North,
            DoorSide.East => DoorSide.West,
            _ => DoorSide.East,
        };
        foreach (var side in new[] { DoorSide.South, DoorSide.North, DoorSide.East, DoorSide.West })
        {
            if (side == back) continue;
            var door = side == plan.Door ? plan.DoorMiddle : (float?)null;
            foreach (var along in Along(plan.Roof, side, windowGap, door))
            {
                if (plan.Wing is { } covering && side != plan.Door && Covered(covering, side, along)) continue;
                pen.Spill(plan.Roof, side, along, 2.5f, 5, 0.3f, Lamp, 1f);
            }
            if (plan.Wing is { } wing && side != plan.Door)
                foreach (var along in Along(wing, side, windowGap, null))
                    pen.Spill(wing, side, along, 2.5f, 5, 0.3f, Lamp, 1f);
        }
        pen.Spill(plan.Roof, plan.Door, plan.DoorMiddle, doorHalf, doorReach, 0.45f, Lamp, 1.15f);
    }

    /// <summary>Whether a wing hides this stretch of a main side wall.</summary>
    private static bool Covered(Rect2 wing, DoorSide side, float along) => side is DoorSide.East or DoorSide.West
        ? along >= wing.Position.Y - 3 && along <= wing.End.Y + 3
        : along >= wing.Position.X - 3 && along <= wing.End.X + 3;

    /// <summary>
    /// Evenly spaced spots along one wall, about <paramref name="gap"/>
    /// apart, kept clear of the door and the corners. A wall too short to
    /// have room beside its door gets none.
    /// </summary>
    private static IEnumerable<float> Along(Rect2 roof, DoorSide side, float gap, float? door)
    {
        var horizontal = side is DoorSide.South or DoorSide.North;
        var from = horizontal ? roof.Position.X : roof.Position.Y;
        var length = horizontal ? roof.Size.X : roof.Size.Y;
        if (door is not null && length < 40) yield break;
        var count = Math.Max(door is null ? 1 : 2, (int)MathF.Round(length / gap));
        for (var i = 0; i < count; i++)
        {
            var along = from + length * (i + 0.5f) / count;
            if (door is { } middle && MathF.Abs(along - middle) < 9) continue;
            if (along < from + 5 || along > from + length - 5) continue;
            yield return along;
        }
    }

    /// <summary>The forge hearth's spot in a Blacksmith's yard, matching the approved drawing.</summary>
    public static Vector2 ForgeHearth(Rect2 yard)
    {
        var tall = yard.Size.Y >= yard.Size.X;
        var compact = (tall ? yard.Size.Y : yard.Size.X) < 40;
        var f = compact ? 0.27f : 0.22f;
        return tall
            ? new Vector2(yard.Position.X + yard.Size.X / 2, yard.Position.Y + yard.Size.Y * f)
            : new Vector2(yard.Position.X + yard.Size.X * f, yard.Position.Y + yard.Size.Y / 2);
    }

    private static Vector2 Outward(DoorSide side) => side switch
    {
        DoorSide.North => new Vector2(0, -1),
        DoorSide.East => new Vector2(1, 0),
        DoorSide.West => new Vector2(-1, 0),
        _ => new Vector2(0, 1),
    };

    /// <summary>Builds the cells of one light, stepped on the art's pixels and kept off the roof.</summary>
    private readonly struct Pen(List<LightCell> cells, int snap, Rect2 roof, float time, int seed)
    {
        /// <summary>
        /// How far the edge of a pool reaches at <paramref name="angle"/>, as
        /// a fraction of its radius: three slow waves with their own phases,
        /// so no two lights wobble alike and none looks like a circle.
        /// </summary>
        private float Edge(float angle, float amount, float speed)
        {
            var p1 = (seed & 255) / 40f;
            var p2 = ((seed >> 8) & 255) / 40f;
            var p3 = ((seed >> 16) & 255) / 40f;
            var t = time * speed;
            return 1 + amount * (0.5f * MathF.Sin(3 * angle + p1 + t) +
                0.3f * MathF.Sin(5 * angle + p2 - t * 1.3f) +
                0.2f * MathF.Sin(2 * angle + p3 + t * 0.7f));
        }

        /// <summary>A ragged round pool of light that fades over several steps.</summary>
        public void Pool(Vector2 at, float radius, int rings, Color color, float strength, bool flicker, bool clip)
        {
            var amount = flicker ? 0.2f : 0.12f;
            var speed = flicker ? 5f : 0.9f;
            // A fire's whole pool also breathes a little.
            var breathe = flicker ? 1 + 0.05f * MathF.Sin(time * 7.3f + seed % 13) : 1;
            var cx = Snap(at.X);
            var cy = Snap(at.Y);
            for (var step = 0; step < rings; step++)
            {
                var r = radius * breathe * (rings - step) / rings;
                var power = strength * MathF.Pow((step + 1) / (float)rings, 1.3f);
                var reach = r * (1 + amount);
                for (var y = -Snap(reach); y < reach; y += snap)
                {
                    var middle = y + snap / 2f;
                    var left = Extent(r, middle, MathF.PI, amount, speed);
                    var right = Extent(r, middle, 0, amount, speed);
                    if (left + right <= 0) continue;
                    var x0 = Snap(cx - left);
                    var x1 = Snap(cx + right);
                    if (x1 > x0) Light(new Rect2(x0, cy + y, x1 - x0, snap), color, power, clip);
                }
            }
        }

        /// <summary>How far a pool row at height <paramref name="y"/> reaches toward one side.</summary>
        private float Extent(float r, float y, float side, float amount, float speed)
        {
            var flat = MathF.Sqrt(Math.Max(0, r * r - y * y));
            var angle = MathF.Atan2(y, side == 0 ? flat : -flat);
            var reach = r * Edge(angle, amount, speed);
            return MathF.Sqrt(Math.Max(0, reach * reach - y * y));
        }

        /// <summary>
        /// Light thrown out of a wall opening: rows that start at the roof
        /// edge and widen away from the wall, rounding off at the far end,
        /// their ends drifting by an art pixel now and then.
        /// </summary>
        public void Spill(Rect2 box, DoorSide side, float along, float halfWidth, float reach, float spread, Color color, float strength)
        {
            for (var step = 0; step < SpillSteps.Length; step++)
            {
                var grow = SpillSteps.Length - 1 - step;
                var half = halfWidth + grow * 1.5f;
                var depth = reach * (1 + grow * 0.5f) * Edge(along * 0.21f + (int)side, 0.12f, 0.9f);
                for (var k = 0f; k < depth; k += snap)
                {
                    var t = (k + snap / 2f) / depth;
                    var round = t > 0.6f ? MathF.Sqrt(Math.Max(0, 1 - MathF.Pow((t - 0.6f) / 0.4f, 2))) : 1;
                    var width = (half + k * spread) * 2 * round;
                    var wobble = Edge(k * 0.7f + along, 0.18f, 0.9f) - 1;
                    var start = Snap(along - width / 2 - wobble * 2);
                    var end = Snap(along + width / 2 + wobble * 2);
                    if (end <= start) break;
                    var rect = side switch
                    {
                        DoorSide.North => new Rect2(start, Snap(box.Position.Y) - k - snap, end - start, snap),
                        DoorSide.East => new Rect2(Snap(box.End.X) + k, start, snap, end - start),
                        DoorSide.West => new Rect2(Snap(box.Position.X) - k - snap, start, snap, end - start),
                        _ => new Rect2(start, Snap(box.End.Y) + k, end - start, snap),
                    };
                    Light(rect, color, SpillSteps[step] * strength, clip: false);
                }
            }
        }

        /// <summary>
        /// A lantern on a bracket on the wall: the fitting itself, and when
        /// lit a pool thrown away from the wall, so it plainly comes from the
        /// lantern rather than from a dot in the middle of the ground.
        /// </summary>
        public void WallLantern(DoorSide side, float along, bool lit)
        {
            var outward = Outward(side);
            var wall = side switch
            {
                DoorSide.North => new Vector2(along, roof.Position.Y),
                DoorSide.East => new Vector2(roof.End.X, along),
                DoorSide.West => new Vector2(roof.Position.X, along),
                _ => new Vector2(along, roof.End.Y),
            };
            var lantern = wall + outward * 3;
            if (lit) Pool(lantern + outward * 4, 15, 4, Lamp, 0.36f, flicker: false, clip: true);
            // Bracket from the wall, then the lantern: an iron frame round lit glass.
            Paint(wall + outward * 1, 1, 1, Iron);
            Paint(lantern - new Vector2(1.5f, 1.5f), 3, 3, Iron);
            if (lit)
            {
                Glow(lantern - new Vector2(0.5f, 0.5f), 1, 1, GlassHot);
                Glow(lantern + outward - new Vector2(0.5f, 0.5f), 1, 1, GlassLit);
            }
            else
            {
                Paint(lantern - new Vector2(0.5f, 0.5f), 1, 1, GlassDark);
            }
        }

        /// <summary>A round stone pillar with an open flame in its bowl, seen from above.</summary>
        public void StoneLamp(Vector2 at, bool lit)
        {
            Disc(at, 4.5f, StoneEdge);
            Disc(at, 3.5f, Stone);
            Paint(at + new Vector2(-3, -2), 1, 2, StoneLight);
            Paint(at + new Vector2(-2, -3), 2, 1, StoneLight);
            Paint(at - new Vector2(1.5f, 1.5f), 3, 3, Soot);
            if (!lit) return;
            // The flame leans a pixel now and then.
            var lean = MathF.Sin(time * 9 + seed % 7) > 0.6f ? 1 : 0;
            Glow(at - new Vector2(1, 1), 2, 2, FlameBody);
            Glow(at + new Vector2(-1 + lean, -1), 1, 1, FlameHot);
            Glow(at + new Vector2(lean, 0), 1, 1, FlameEdge);
        }

        /// <summary>
        /// A post with an arm hanging a lantern at <paramref name="light"/>;
        /// <paramref name="armless"/> hangs the lantern alone, as on a pier
        /// post or over a terrace.
        /// </summary>
        public void HangingLantern(Vector2 post, Vector2 light, bool lit, bool armless = false, float radius = 24)
        {
            if (lit) Pool(light, radius, 5, Lamp, 0.36f, flicker: false, clip: false);
            if (!armless)
            {
                var arm = light - post;
                var direction = arm.Normalized();
                for (var k = 1; k < (int)arm.Length(); k++)
                    Paint(post + direction * k - new Vector2(0.5f, 0.5f), 1, 1, Iron);
                Paint(post - new Vector2(2, 2), 4, 4, WoodEdge);
                Paint(post - new Vector2(1, 1), 2, 2, Wood);
            }
            Paint(light - new Vector2(2.5f, 2.5f), 5, 5, Iron);
            Paint(light - new Vector2(2.5f, 2.5f), 5, 1, IronLight);
            if (lit)
            {
                Glow(light - new Vector2(1.5f, 1.5f), 3, 3, GlassLit);
                Glow(light - new Vector2(0.5f, 0.5f), 1, 1, GlassHot);
            }
            else
            {
                Paint(light - new Vector2(1.5f, 1.5f), 3, 3, GlassDark);
            }
        }

        private void Disc(Vector2 at, float radius, Color color)
        {
            var cx = Snap(at.X);
            var cy = Snap(at.Y);
            for (var y = -Snap(radius); y < radius; y += snap)
            {
                var t = (y + snap / 2f) / radius;
                var width = Snap(radius * MathF.Sqrt(Math.Max(0, 1 - t * t)) * 2);
                if (width > 0) cells.Add(new LightCell(new Rect2(cx - width / 2, cy + y, width, snap), color, 1, LightCellKind.Paint));
            }
        }

        private void Paint(Vector2 at, float width, float height, Color color) =>
            cells.Add(new LightCell(Pixels(at, width, height), color, 1, LightCellKind.Paint));

        private void Glow(Vector2 at, float width, float height, Color color) =>
            cells.Add(new LightCell(Pixels(at, width, height), color, 1, LightCellKind.Glow));

        private Rect2 Pixels(Vector2 at, float width, float height) =>
            new(Snap(at.X), Snap(at.Y), Math.Max(snap, Snap(width)), Math.Max(snap, Snap(height)));

        /// <summary>Adds a row of light, cutting out any part that would fall on the roof.</summary>
        private void Light(Rect2 row, Color color, float strength, bool clip)
        {
            if (!clip || roof.Size.X <= 0 || row.Position.Y >= roof.End.Y || row.End.Y <= roof.Position.Y ||
                row.Position.X >= roof.End.X || row.End.X <= roof.Position.X)
            {
                cells.Add(new LightCell(row, color, strength, LightCellKind.Light));
                return;
            }
            if (row.Position.X < roof.Position.X)
                cells.Add(new LightCell(new Rect2(row.Position.X, row.Position.Y, roof.Position.X - row.Position.X, row.Size.Y), color, strength, LightCellKind.Light));
            if (row.End.X > roof.End.X)
                cells.Add(new LightCell(new Rect2(roof.End.X, row.Position.Y, row.End.X - roof.End.X, row.Size.Y), color, strength, LightCellKind.Light));
        }

        private float Snap(float value) => MathF.Floor(value / snap + 0.5f) * snap;
    }
}
