using Godot;

namespace ClankerWorld.GodotClient.UI;

public enum StockPileKind : byte { Logs, Crates, Sacks }
public readonly record struct StockPaint(Rect2 Area, Color Color);
public readonly record struct StoredStockPile(Vector2I Tile, StockPileKind Kind, int Level);

/// <summary>The approved stock A primitives, keeping their pixel casts, colours and paint order.</summary>
public static class StoredStockArt
{
    public static StockPileKind KindFor(string kind) => kind switch
    {
        "wood" => StockPileKind.Logs,
        "food" or "grain" or "grain_seed" or "flour" or "potatoes" or "potato_seed" or
        "cultivated_greens" or "cultivated_green_seed" or "green_seed" or "wild_greens" or
        "berries" or "fruit" or "nuts" or "fiber" or "medicinal_herbs" or "animal_feed" => StockPileKind.Sacks,
        _ => StockPileKind.Crates,
    };

    public static void Append(List<StockPaint> output, StockPileKind kind, int level, Vector2 at, int size)
    {
        if (level <= 0) return;
        var unit = size / 32f;
        switch (kind)
        {
            case StockPileKind.Logs:
                Logs(output, at, unit, level == 2 ? 5 : 2);
                break;
            case StockPileKind.Crates:
                Crates(output, at, unit, level == 2 ? 4 : 1);
                break;
            case StockPileKind.Sacks:
                Sacks(output, at, unit, level == 2 ? 6 : 2);
                break;
        }
    }

    public static Vector2 Offset(StockPileKind kind) => kind switch
    {
        StockPileKind.Logs => new(0.1f, 0.15f),
        StockPileKind.Crates => new(0.05f, 0.15f),
        _ => new(0.05f, 0.1f),
    };

    private static void Fill(List<StockPaint> output, int x, int y, int width, int height, Color color)
    {
        if (width > 0 && height > 0) output.Add(new(new Rect2(x, y, width, height), color));
    }

    private static void Put(List<StockPaint> output, int x, int y, Color color) => Fill(output, x, y, 1, 1, color);

    private static void Shadow(List<StockPaint> output, float x, float y, float width, float height, float unit) =>
        Fill(output, (int)(x + 2 * unit), (int)(y + 2 * unit), (int)MathF.Max(1, width), (int)MathF.Max(1, height),
            new Color(0.05f, 0.08f, 0.05f, 0.28f));

    private static void Logs(List<StockPaint> output, Vector2 at, float unit, int rows)
    {
        var len = 15 * unit;
        var thickness = 4 * unit;
        for (var row = 0; row < rows; row++)
        {
            var x = at.X + row % 2 * 3 * unit;
            var y = at.Y + row * (thickness + unit * 0.5f);
            Shadow(output, x, y, len, thickness, unit);
            Fill(output, (int)x, (int)y, (int)len, (int)MathF.Max(1, thickness), new Color("6E4E31"));
            Fill(output, (int)x, (int)y, (int)len, (int)MathF.Max(1, thickness * 0.4f), new Color("8A6440"));
            Fill(output, (int)(x + len - thickness), (int)y, (int)MathF.Max(1, thickness),
                (int)MathF.Max(1, thickness), new Color("D2AC77"));
            Put(output, (int)(x + len - thickness / 2), (int)(y + thickness / 2), new Color("8A6440"));
        }
    }

    private static void Crates(List<StockPaint> output, Vector2 at, float unit, int count)
    {
        var side = 9 * unit;
        for (var n = 0; n < count; n++)
        {
            var x = at.X + n % 2 * (side + unit);
            var y = at.Y + n / 2 * (side + unit);
            Shadow(output, x, y, side, side, unit);
            Fill(output, (int)x, (int)y, (int)side, (int)side, new Color("3F2A1A"));
            Fill(output, (int)(x + unit), (int)(y + unit), (int)MathF.Max(1, side - 2 * unit),
                (int)MathF.Max(1, side - 2 * unit), new Color("A77C52"));
            Fill(output, (int)(x + unit), (int)(y + side / 2), (int)MathF.Max(1, side - 2 * unit),
                (int)MathF.Max(1, unit), new Color("6E4E31"));
            Put(output, (int)(x + unit), (int)(y + unit), new Color("D2AC77"));
        }
    }

    private static void Sacks(List<StockPaint> output, Vector2 at, float unit, int count)
    {
        var radius = 3.8f * unit;
        for (var n = 0; n < count; n++)
        {
            var centre = at + new Vector2(radius + n % 2 * (2 * radius), radius + n / 2 * (1.6f * radius));
            Disc(output, centre + new Vector2(2 * unit, 2 * unit), radius, new Color(0.05f, 0.08f, 0.05f, 0.28f));
            Disc(output, centre, radius, new Color("7E6E4A"));
            Disc(output, centre, MathF.Max(0.6f, radius - unit), new Color("C8B78C"));
            Disc(output, centre - new Vector2(radius * 0.35f, radius * 0.35f), MathF.Max(0.5f, radius * 0.35f), new Color("E3D6B5"));
            Put(output, (int)centre.X, (int)(centre.Y - radius + unit), new Color("7E6E4A"));
        }
    }

    private static void Disc(List<StockPaint> output, Vector2 centre, float radius, Color color)
    {
        for (var y = (int)MathF.Floor(centre.Y - radius); y <= (int)MathF.Ceiling(centre.Y + radius); y++)
        {
            var first = int.MaxValue;
            var last = int.MinValue;
            for (var x = (int)MathF.Floor(centre.X - radius); x <= (int)MathF.Ceiling(centre.X + radius); x++)
                if ((new Vector2(x + 0.5f, y + 0.5f) - centre).LengthSquared() <= radius * radius)
                {
                    first = Math.Min(first, x);
                    last = x;
                }
            if (first <= last) Fill(output, first, y, last - first + 1, 1, color);
        }
    }
}
