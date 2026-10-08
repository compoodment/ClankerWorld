using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.WaitingMarker;

/// <summary>
/// Map markers for an agent whose model has not replied yet (#1315): a
/// thought cloud above the head, an hourglass chip, or a spark circling the
/// head. Each is drawn on the reference Town's agents at 32 and 16 px per
/// tile, frame by frame, and once next to the conversation badge it must not
/// be confused with.
/// </summary>
public sealed class WaitingMarkerProposal : IArtProposal
{
    private static readonly Color Ink = new("1E1712");
    private static readonly Color Cream = new("F4E7BE");
    private static readonly Color Brown = new("493522");
    private static readonly Color Sand = new("E0A43A");
    private static readonly Color Spark = new("FFF6D0");
    private static readonly Color Gold = new("F2C14E");
    private static readonly Dictionary<int, Image> Scenes = [];

    public string Family => "waiting";

    public static int FrameCount(char option) => option switch { 'a' => 4, 'b' => 4, _ => 8 };

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var option in "abc")
            {
                for (var frame = 0; frame < FrameCount(option); frame++)
                    yield return new(Family, $"{option}-{size}-f{frame}", Frame(option, size, frame, badge: false),
                        frame == 0 ? Note(option) : null);
                yield return new(Family, $"{option}-{size}-badge", Frame(option, size, 0, badge: true), "with conversation badge");
            }
    }

    private static string Note(char option) => option switch
    {
        'a' => "A: thought cloud",
        'b' => "B: hourglass chip",
        _ => "C: circling spark",
    };

    /// <summary>A 4 × 3 tile corner of the reference Town around the agent at (8, 4), with the marker drawn the way AgentMarker would.</summary>
    public static Image Frame(char option, int size, int frame, bool badge)
    {
        if (!Scenes.TryGetValue(size, out var scene))
            Scenes[size] = scene = SceneComposer.Render(SceneSpec.TownCorner(), new ArtSet(), size);
        var image = Image.CreateEmpty(size * 4, size * 3, false, Image.Format.Rgba8);
        image.BlitRect(scene, new Rect2I(size * 6, size * 3, size * 4, size * 3), Vector2I.Zero);
        // The agent's tile, as AgentMarker's control rectangle.
        var origin = new Vector2I(size * 2, size);
        var canvas = new Canvas(image, origin, size);
        if (badge) canvas.ConversationBadge();
        switch (option)
        {
            case 'a': canvas.Cloud(frame); break;
            case 'b': canvas.Hourglass(frame); break;
            default: canvas.Orbit(frame); break;
        }
        return image;
    }

    private sealed class Canvas(Image image, Vector2I origin, int side)
    {
        private float Drawn => side * SceneComposer.AgentSpriteScale;

        private void Pixel(int x, int y, Color color)
        {
            x += origin.X;
            y += origin.Y;
            if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight() || color.A <= 0) return;
            image.SetPixel(x, y, image.GetPixel(x, y).Blend(color));
        }

        private void Bitmap(int left, int top, string[] rows, Func<char, Color?> palette)
        {
            for (var y = 0; y < rows.Length; y++)
                for (var x = 0; x < rows[y].Length; x++)
                    if (palette(rows[y][x]) is { } color) Pixel(left + x, top + y, color);
        }

        private void Disc(Vector2 center, float radius, Color color)
        {
            for (var y = (int)MathF.Floor(center.Y - radius); y <= (int)MathF.Ceiling(center.Y + radius); y++)
                for (var x = (int)MathF.Floor(center.X - radius); x <= (int)MathF.Ceiling(center.X + radius); x++)
                    if ((new Vector2(x + 0.5f, y + 0.5f) - center).Length() <= radius) Pixel(x, y, color);
        }

        /// <summary>Today's conversation badge, drawn as AgentMarker draws it, top right.</summary>
        public void ConversationBadge()
        {
            var diameter = Math.Min(side, Math.Clamp(side * 0.42f, 4f, 13f));
            var center = new Vector2(side - diameter / 2, diameter / 2);
            var radius = diameter * 0.43f;
            Disc(center, radius + 1, Ink);
            Disc(center, radius, Cream);
            var dot = Math.Max(0.65f, radius * 0.13f);
            for (var index = -1; index <= 1; index++)
                Disc(center + new Vector2(index * radius * 0.45f, 0), dot, Brown);
        }

        /// <summary>
        /// A: a small cream thought cloud above and left of the head, joined
        /// to it by two puffs, with one dark dot moving across it as the
        /// agent thinks. Puffs, not a tail, so it never reads as speech.
        /// </summary>
        public void Cloud(int frame)
        {
            var big = side >= 24;
            string[] rows = big
                ?
                [
                    "..ooo.ooo....",
                    ".occcoccco...",
                    "occccccccco..",
                    "occccccccco..",
                    ".occccccco...",
                    "..ooooooo....",
                    "........ooo..",
                    "........oco..",
                    "........ooo..",
                    "...........oo",
                    "...........oo",
                ]
                :
                [
                    ".ooooo.",
                    "occccco",
                    "occccco",
                    ".ooooo.",
                    ".....o.",
                ];
            var left = big ? side / 2 - 15 : side / 2 - 8;
            var top = big ? -9 : -4;
            Bitmap(left, top, rows, c => c switch { 'o' => Ink, 'c' => Cream, _ => null });
            if (frame == 3) return;
            // One dot steps left to right, then the cloud rests empty for a beat.
            if (big)
            {
                var x = left + 3 + frame * 2;
                Pixel(x, top + 2, Brown);
                Pixel(x + 1, top + 2, Brown);
                Pixel(x, top + 3, Brown);
                Pixel(x + 1, top + 3, Brown);
            }
            else
            {
                Pixel(left + 1 + frame * 2, top + 1, Brown);
                Pixel(left + 1 + frame * 2, top + 2, Brown);
            }
        }

        /// <summary>
        /// B: an hourglass on a round chip at the top left, the same size as
        /// the conversation badge on the other side; its sand runs down and
        /// it turns over.
        /// </summary>
        public void Hourglass(int frame)
        {
            // At 16 px the chip is a little larger than the badge so the glass still reads.
            var diameter = side >= 24 ? Math.Min(side, Math.Clamp(side * 0.42f, 4f, 13f)) : 9f;
            var center = new Vector2(diameter / 2 - (side >= 24 ? 0 : 1), diameter / 2 - (side >= 24 ? 0 : 1));
            var radius = diameter * 0.43f + 0.5f;
            Disc(center, radius + 1, Ink);
            Disc(center, radius, Cream);
            string[][] big =
            [
                ["ooooo", "oyyyo", ".oyo.", "..o..", ".o.o.", "o...o", "ooooo"],
                ["ooooo", "o.y.o", ".oyo.", "..y..", ".o.o.", "oyyyo", "ooooo"],
                ["ooooo", "o...o", ".o.o.", "..o..", ".oyo.", "oyyyo", "ooooo"],
                ["o...o", "oo.oo", "oyoyo", "oyyyo", "oyoyo", "oo.oo", "o...o"],
            ];
            string[][] small =
            [
                ["ooo", "oyo", ".o.", "o.o", "ooo"],
                ["ooo", "o.o", ".y.", "oyo", "ooo"],
                ["ooo", "o.o", ".o.", "oyo", "ooo"],
                ["o.o", "ooo", "oyo", "ooo", "o.o"],
            ];
            var rows = side >= 24 ? big[frame] : small[frame];
            Bitmap((int)MathF.Round(center.X - rows[0].Length / 2f), (int)MathF.Round(center.Y - rows.Length / 2f), rows,
                c => c switch { 'o' => Brown, 'y' => Sand, _ => null });
        }

        /// <summary>
        /// C: a small gold spark circling just above the head with a short
        /// fading trail, dimmer while it passes behind; no chip or bubble.
        /// </summary>
        public void Orbit(int frame)
        {
            var center = new Vector2(side / 2f, side / 2f - Drawn * 0.33f);
            var radius = new Vector2(Drawn * 0.24f, Drawn * 0.08f);
            Vector2 At(float step) => center + new Vector2(MathF.Cos(step / 8f * MathF.Tau) * radius.X, MathF.Sin(step / 8f * MathF.Tau) * radius.Y);
            for (var trail = 2; trail >= 1; trail--)
            {
                var point = At(frame - trail * 0.5f);
                Pixel((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y), Gold with { A = 0.6f / trail });
            }
            var spark = At(frame);
            var behind = MathF.Sin(frame / 8f * MathF.Tau) < 0;
            var strength = behind ? 0.7f : 1f;
            var x = (int)MathF.Floor(spark.X);
            var y = (int)MathF.Floor(spark.Y);
            // A plus-shaped spark with a dark rim, so it reads on grass, road or roof.
            var arm = side >= 24 ? 2 : 1;
            for (var dy = -arm - 1; dy <= arm + 1; dy++)
                for (var dx = -arm - 1; dx <= arm + 1; dx++)
                {
                    var onPlus = (dx == 0 && Math.Abs(dy) <= arm) || (dy == 0 && Math.Abs(dx) <= arm);
                    var nearPlus = !onPlus && (Math.Abs(dx) <= 1 && Math.Abs(dy) <= arm || Math.Abs(dy) <= 1 && Math.Abs(dx) <= arm) &&
                        Math.Abs(dx) + Math.Abs(dy) <= arm + 1;
                    if (nearPlus) Pixel(x + dx, y + dy, Ink with { A = 0.75f * strength });
                }
            for (var d = -arm; d <= arm; d++)
            {
                var tip = Math.Abs(d) == arm && arm > 1;
                Pixel(x + d, y, (d == 0 ? Spark : tip ? Gold with { A = 0.8f } : Gold) with { A = strength });
                if (d != 0) Pixel(x, y + d, (tip ? Gold with { A = 0.8f } : Gold) with { A = strength });
            }
        }
    }
}
