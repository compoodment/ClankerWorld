using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Approved;

/// <summary>
/// Idea 9, stock you can see: log, crate and sack piles beside the
/// Warehouse and between the Farmhouse and Silo, growing with what is stored.
/// </summary>
public sealed class StockProposal : IArtProposal
{
    public string Family => "stock";

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var placement in new[] { "a-at-door", "b-along-wall" })
                foreach (var (level, name) in new[] { (0, "empty"), (1, "some"), (2, "full") })
                    yield return new(Family, $"{placement}-{name}-{size}", Frame(placement, level, size),
                        (placement == "a-at-door" ? "A: piles on the ground beside the building" : "B: stacks against the building's wall") + $", {name}");
    }

    internal static Image Frame(string placement, int level, int size)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        if (level == 0) return canvas.ToImage();
        var unit = size / 32f;
        // Warehouse at (6,2) 2×2: wood and crates south of it. Farmhouse (2,7) and Silo (4,8): grain sacks at (3,8).
        if (placement == "a-at-door")
        {
            Logs(canvas, new Vector2(6.1f * size, 4.15f * size), unit, level == 2 ? 5 : 2);
            Crates(canvas, new Vector2(7.05f * size, 4.15f * size), unit, level == 2 ? 4 : 1);
            Sacks(canvas, new Vector2(3.05f * size, 8.1f * size), unit, level == 2 ? 6 : 2);
        }
        else
        {
            Logs(canvas, new Vector2(6.05f * size, 3.98f * size), unit, level == 2 ? 4 : 2, row: true);
            Crates(canvas, new Vector2(7.0f * size, 3.95f * size), unit, level == 2 ? 3 : 1, row: true);
            Sacks(canvas, new Vector2(3.02f * size, 7.85f * size), unit, level == 2 ? 5 : 2, row: true);
        }
        return canvas.ToImage();
    }

    private static void Shadow(Canvas c, float x, float y, float w, float h, float unit) =>
        c.Fill((int)(x + 2 * unit), (int)(y + 2 * unit), (int)MathF.Max(1, w), (int)MathF.Max(1, h), new Color(0.05f, 0.08f, 0.05f, 0.28f));

    private static void Logs(Canvas c, Vector2 at, float unit, int rows, bool row = false)
    {
        var len = 15 * unit; var th = 4 * unit;
        for (var r = 0; r < rows; r++)
        {
            var x = at.X + (row ? 0 : (r % 2) * 3 * unit); var y = at.Y + r * (row ? th * 0.7f : th + unit * 0.5f);
            Shadow(c, x, y, len, th, unit);
            c.Fill((int)x, (int)y, (int)len, (int)MathF.Max(1, th), new Color("6E4E31"));
            c.Fill((int)x, (int)y, (int)len, (int)MathF.Max(1, th * 0.4f), new Color("8A6440"));
            c.Fill((int)(x + len - th), (int)y, (int)MathF.Max(1, th), (int)MathF.Max(1, th), new Color("D2AC77"));
            c.Put((int)(x + len - th / 2), (int)(y + th / 2), new Color("8A6440"));
        }
    }

    private static void Crates(Canvas c, Vector2 at, float unit, int count, bool row = false)
    {
        var s = 9 * unit;
        for (var n = 0; n < count; n++)
        {
            var x = at.X + (row ? n * (s + unit) : (n % 2) * (s + unit));
            var y = at.Y + (row ? 0 : (n / 2) * (s + unit)) - (row ? 0 : 0);
            Shadow(c, x, y, s, s, unit);
            c.Fill((int)x, (int)y, (int)s, (int)s, new Color("3F2A1A"));
            c.Fill((int)(x + unit), (int)(y + unit), (int)MathF.Max(1, s - 2 * unit), (int)MathF.Max(1, s - 2 * unit), new Color("A77C52"));
            c.Fill((int)(x + unit), (int)(y + s / 2), (int)MathF.Max(1, s - 2 * unit), (int)MathF.Max(1, unit), new Color("6E4E31"));
            c.Put((int)(x + unit), (int)(y + unit), new Color("D2AC77"));
        }
    }

    private static void Sacks(Canvas c, Vector2 at, float unit, int count, bool row = false)
    {
        var r = 3.8f * unit;
        for (var n = 0; n < count; n++)
        {
            var centre = at + new Vector2(r + (row ? n * (2 * r + unit * 0.5f) : (n % 2) * (2 * r)), r + (row ? 0 : (n / 2) * (1.6f * r)));
            c.Disc(centre + new Vector2(2 * unit, 2 * unit), r, new Color(0.05f, 0.08f, 0.05f, 0.28f));
            c.Disc(centre, r, new Color("7E6E4A"));
            c.Disc(centre, MathF.Max(0.6f, r - unit), new Color("C8B78C"));
            c.Disc(centre - new Vector2(r * 0.35f, r * 0.35f), MathF.Max(0.5f, r * 0.35f), new Color("E3D6B5"));
            c.Put((int)centre.X, (int)(centre.Y - r + unit), new Color("7E6E4A"));
        }
    }
}

