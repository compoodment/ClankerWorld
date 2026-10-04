using Godot;

namespace ClankerWorld.GodotClient.UI;

public static partial class NatureSprites
{
    private static readonly Ramp Cloth = Ramp.Of("75674D", "A09170", "CABC99", "E8DCC0", "FFF5DF");

    // Read the original ramps after type initialization, independently of partial
    // file ordering. PixelCanvas snaps the approved proposal's blended colours.
    private static Ramp IronGravel => new(Iron.Edge, Iron.Shade.Lerp(Rust.Shade, 0.25f),
        Iron.Base.Lerp(Rust.Shade, 0.25f), Iron.Light.Lerp(Rust.Shade, 0.25f), Iron.Highlight);

    // Approved nature drawings from tools/ArtPreview/Proposed/Nature.cs. Keep
    // their geometry and palettes unchanged; the preview checks compare pixels.
    private static void Rubble(Layers s, Ramp stone, Ore ore)
    {
        s.Shadow(17, 19, 8, 4.5f);
        var g = s.GroundCanvas;
        g.Lumpy(16, 17, 10, stone.Shade with { A = 0.6f }, 6, 1);
        g.Lumpy(15.6f, 16.6f, 9, stone.Base with { A = 0.8f }, 6, 1);
        // The hollow: lit far lip, dark near wall, floor.
        g.Ellipse(16.8f, 17.6f, 5.6f, 3.7f, stone.Light);
        g.Ellipse(16.2f, 17, 5.2f, 3.3f, stone.Edge with { A = 0.85f });
        g.Ellipse(16.9f, 17.7f, 4.2f, 2.5f, stone.Shade);
        if (s.Fine)
        {
            // Gravel: a few pale grains on the apron and in the hollow.
            foreach (var (x, y) in new[] { (9, 13), (12, 22), (22, 13), (24, 20), (8, 18), (18, 18) })
                g.Dot(x, y, stone.Light);
        }
        // Left-over chips on the rim; the smallest are dropped at 16 px.
        var c = s.Canvas;
        foreach (var (x, y, r) in new[] { (9.5f, 15.5f, 2.2f), (23f, 22f, 2.4f), (21.5f, 11.5f, 1.7f), (12.5f, 23f, 1.6f) })
        {
            if (!s.Fine && r < 2) continue;
            c.Disc(x, y, r, stone.Shade);
            c.Disc(x - 0.5f, y - 0.5f, r - 0.8f, stone.Light);
            if (s.Fine && r >= 2) c.Dot(x - 1, y - 1, stone.Highlight);
        }
        RubbleTrace(s, ore);
    }

    private static void RubbleTrace(Layers s, Ore ore)
    {
        var g = s.GroundCanvas;
        var c = s.Canvas;
        switch (ore)
        {
            case Ore.Iron:
                if (s.Fine)
                {
                    // A rust stain across the hollow floor and on two chips.
                    g.Line(14, 18, 19, 19, Rust.Shade with { A = 0.8f });
                    g.Dot(16, 17, Rust.Edge);
                    c.Dot(9, 15, Rust.Shade);
                    c.Dot(22, 22, Rust.Shade);
                }
                else c.Dot(23, 22, Rust.Shade);
                break;
            case Ore.Gold:
                // Single pixels in Gold base and shade: duller and smaller than the outcrop's 2×2 flecks.
                if (s.Fine)
                {
                    g.Dot(15, 18, Gold.Base);
                    g.Dot(22, 15, Gold.Base);
                    g.Dot(11, 21, Gold.Shade);
                }
                else g.Dot(17, 18, Gold.Base);
                break;
            case Ore.Diamond:
                // One broken shard in the hollow, in the dull crystal steps.
                if (s.Fine)
                {
                    g.Dot(17, 18, Diamond.Shade);
                    g.Dot(18, 18, Diamond.Edge);
                    g.Dot(12, 21, Diamond.Shade with { A = 0.7f });
                }
                else g.Dot(17, 18, Diamond.Shade);
                break;
        }
    }

    private static void HerbPatch(Layers s)
    {
        s.Shadow(17.5f, 19.5f, 8.5f, 6.5f);
        var c = s.Canvas;
        if (!s.Fine)
        {
            s.Lobed(16, 16, 6.6f, Canopy.Base, 7, 0.5f, 0.24f);
            s.Lobed(15, 15, 4, Canopy.Light, 5, 1, 0.2f);
            foreach (var (x, y) in new[] { (11, 12), (20, 11), (21, 19), (13, 20) }) c.Dot(x, y, Cloth.Highlight);
            return;
        }
        c.Disc(16, 16.5f, 3f, Canopy.Shade);
        ReadOnlySpan<(float Angle, float Length)> sprigs =
        [
            (0.2f, 7), (1.1f, 6), (1.9f, 7.5f), (2.8f, 6.5f), (3.7f, 7.5f), (4.6f, 6.5f), (5.45f, 7),
        ];
        foreach (var (angle, length) in sprigs)
        {
            var dir = Vector2.FromAngle(angle);
            var lit = FacesLight(angle, -0.2f);
            var leaf = lit ? Canopy.Light : Canopy.Base;
            c.Line(16, 16.5f, 16 + dir.X * length, 16.5f + dir.Y * length, Canopy.Shade);
            // Two leaf pairs along the stem, angled forward, and one tip leaf.
            foreach (var along in new[] { 0.45f, 0.78f })
                foreach (var side in new[] { -0.75f, 0.75f })
                {
                    var leafAngle = angle + side;
                    var at = new Vector2(16, 16.5f) + dir * (length * along) + Vector2.FromAngle(leafAngle) * 1.4f;
                    c.Leaf(at.X, at.Y, 1.6f, 1f, leafAngle, leaf, leaf);
                }
            var tip = new Vector2(16, 16.5f) + dir * (length + 0.6f);
            c.Leaf(tip.X, tip.Y, 1.6f, 1.1f, angle, lit ? Canopy.Highlight : Canopy.Light, lit ? Canopy.Highlight : Canopy.Light);
        }
        // Flowers: four pale petals round a gold centre, on alternate sprig tips.
        foreach (var index in new[] { 0, 2, 4, 6 })
        {
            var (angle, length) = sprigs[index];
            var at = new Vector2(16, 16.5f) + Vector2.FromAngle(angle) * (length + 0.5f);
            var x = MathF.Round(at.X);
            var y = MathF.Round(at.Y);
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }) c.Dot(x + dx, y + dy, Cloth.Highlight);
            c.Dot(x, y, Gold.Light);
        }
    }

    private static void WildGreensPicked(Layers s)
    {
        s.Shadow(17, 18, 6.5f, 5);
        var c = s.Canvas;
        var leaves = s.Fine ? 7 : 5;
        for (var i = 0; i < leaves; i++)
        {
            var angle = i * Mathf.Tau / leaves + 0.45f;
            var lit = FacesLight(angle);
            var dir = Vector2.FromAngle(angle);
            var color = lit ? Canopy.Light : Canopy.Base;
            c.Leaf(16 + dir.X * 3.2f, 16 + dir.Y * 3.2f, 2.6f, 1.5f, angle, color, color);
            if (s.Fine) c.Dot(16 + dir.X * 5.4f, 16 + dir.Y * 5.4f, GreensVein);
        }
        c.Disc(16, 16, 1.8f, s.Fine ? Canopy.Shade : GreensVein);
        if (!s.Fine) return;
        for (var i = 0; i < 3; i++)
        {
            var angle = i * Mathf.Tau / 3 - 1.2f;
            var dir = Vector2.FromAngle(angle);
            c.Leaf(16 + dir.X * 2.4f, 16 + dir.Y * 2.4f, 2.4f, 1.3f, angle, Canopy.Highlight, GreensVein);
        }
    }

    private static void FiberPlantHarvested(Layers s)
    {
        s.Shadow(17, 18.5f, 7, 4.8f);
        var c = s.Canvas;
        // The fibre plant's eleven blade angles, cut to stubs that alternate long and short so they stay apart.
        ReadOnlySpan<(float Angle, float Length)> stubs =
        [
            (0.1f, 6.5f), (0.75f, 5), (1.35f, 6.5f), (1.95f, 5), (2.5f, 6.5f), (3.05f, 5),
            (3.65f, 7), (4.15f, 5), (4.7f, 7), (5.25f, 5), (5.8f, 6.5f),
        ];
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < stubs.Length; i++)
            {
                if (!s.Fine && i % 3 == 1) continue;
                var (angle, length) = stubs[i];
                var half = length / 2;
                var dir = Vector2.FromAngle(angle);
                var offset = pass == 0 ? s.Pixel : 0;
                var lit = FacesLight(angle, 0.1f);
                var color = pass == 0 ? Fiber.Edge : lit ? Fiber.Light : Fiber.Base;
                c.Leaf(16 + dir.X * (half + 1) + offset, 16 + dir.Y * (half + 1) + offset, half, 0.9f, angle, color, color);
            }
            if (pass == 0) c.Disc(16 + s.Pixel, 16.5f + s.Pixel, 3.6f, Fiber.Edge);
        }
        c.Disc(16, 16.5f, 3.4f, Fiber.Shade);
        c.Disc(15, 15.5f, 1.6f, Fiber.Base);
        if (!s.Fine) return;
        foreach (var (angle, length) in stubs)
        {
            var dir = Vector2.FromAngle(angle);
            c.Dot(16 + dir.X * (length + 0.3f), 16 + dir.Y * (length + 0.3f), FacesLight(angle, 0.1f) ? Fiber.Highlight : Fiber.Light);
        }
    }

    private static void HerbPatchPicked(Layers s)
    {
        s.Shadow(17.5f, 19, 7, 5);
        var c = s.Canvas;
        if (!s.Fine)
        {
            s.Lobed(16, 16, 5.2f, Canopy.Base, 7, 0.5f, 0.24f);
            s.Lobed(15, 15, 2.8f, Canopy.Light, 5, 1, 0.2f);
            return;
        }
        c.Disc(16, 16.5f, 2.2f, Canopy.Shade);
        // The herb patch's seven sprigs, as drawn on the unpicked patch.
        ReadOnlySpan<(float Angle, float Length)> sprigs =
        [
            (0.2f, 7), (1.1f, 6), (1.9f, 7.5f), (2.8f, 6.5f), (3.7f, 7.5f), (4.6f, 6.5f), (5.45f, 7),
        ];
        for (var index = 0; index < sprigs.Length; index++)
        {
            var (angle, length) = sprigs[index];
            var dir = Vector2.FromAngle(angle);
            var lit = FacesLight(angle, -0.2f);
            var leaf = lit ? Canopy.Light : Canopy.Base;
            var cut = length * 0.8f;
            c.Line(16, 16.5f, 16 + dir.X * cut, 16.5f + dir.Y * cut, Canopy.Shade);
            // The lower leaf pair stays; of the upper pair only one leaf is left, on alternate sides, so the clump looks snipped.
            foreach (var (along, side) in new[] { (0.45f, -0.75f), (0.45f, 0.75f), (0.72f, index % 2 == 0 ? -0.75f : 0.75f) })
            {
                var leafAngle = angle + side;
                var at = new Vector2(16, 16.5f) + dir * (length * along) + Vector2.FromAngle(leafAngle) * 1.4f;
                c.Leaf(at.X, at.Y, 1.6f, 1f, leafAngle, leaf, leaf);
            }
            c.Dot(16 + dir.X * (cut + 0.5f), 16.5f + dir.Y * (cut + 0.5f), GreensVein);
        }
        // The heart of the clump, filled with leaves so it does not read as a ring.
        c.Disc(16, 16.5f, 2.2f, Canopy.Base);
        c.Disc(15.5f, 16, 1.2f, Canopy.Light);
    }

    private static void ClayPit(Layers s)
    {
        s.Shadow(17, 19, 8.5f, 5);
        var g = s.GroundCanvas;
        g.Lumpy(16, 17, 10.5f, Bank.Shade with { A = 0.6f }, 6, 1);
        g.Lumpy(15.6f, 16.6f, 9.5f, Bank.Base with { A = 0.8f }, 6, 1);
        g.Ellipse(17.8f, 18.6f, 6.4f, 4.2f, Clay.Light);
        g.Ellipse(17.2f, 18, 6, 3.8f, Clay.Edge with { A = 0.9f });
        g.Ellipse(17.9f, 18.7f, 4.9f, 3, Clay.Shade);
        if (s.Fine)
        {
            g.Line(17, 18, 18, 20, Clay.Base);
            g.Line(20, 17.5f, 21, 19.5f, Clay.Base);
            // Crumbs of bank soil on the apron.
            foreach (var (x, y) in new[] { (9, 15), (24, 14), (12, 23), (25, 20) }) g.Dot(x, y, Bank.Light);
        }
        // The low hump of the old bank on the north-west, lit on its crest, with a grass tuft.
        s.Lobed(11, 12, 4.4f, Bank.Shade, 4, 2, 0.15f);
        s.Lobed(10.5f, 11.5f, 3.5f, Bank.Base, 4, 2, 0.15f);
        if (s.Fine)
        {
            s.Canvas.Disc(9.6f, 10.6f, 1.3f, Bank.Light);
            s.Canvas.Line(12, 10, 13, 8.5f, GrassTuft);
        }
        // Dug clods of fresh clay left on the apron.
        Mark(s, 24, 23, Clay.Base, Clay.Light);
        Mark(s, 22, 12, Clay.Base, Clay.Light);
    }
}
