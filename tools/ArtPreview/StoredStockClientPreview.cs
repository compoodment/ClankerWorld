using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview;

public sealed class StoredStockClientPreview : IArtProposal
{
    public string Family => "stock-client";

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var (level, name) in new[] { (0, "empty"), (1, "some"), (2, "full") })
                yield return new(Family, $"a-at-door-{name}-{size}", Frame(level, size), $"Live stock A: {name}");
    }

    internal static Image Frame(int level, int size)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        var paints = new List<StockPaint>();
        foreach (var (kind, tile) in new[] { (StockPileKind.Logs, new Vector2(6, 4)),
            (StockPileKind.Crates, new Vector2(7, 4)), (StockPileKind.Sacks, new Vector2(3, 8)) })
        {
            paints.Clear();
            StoredStockArt.Append(paints, kind, level, (tile + StoredStockArt.Offset(kind)) * size, size);
            foreach (var paint in paints)
                canvas.Fill((int)paint.Area.Position.X, (int)paint.Area.Position.Y,
                    (int)paint.Area.Size.X, (int)paint.Area.Size.Y, paint.Color);
        }
        return canvas.ToImage();
    }
}
