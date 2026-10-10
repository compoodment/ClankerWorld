using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Polish;

/// <summary>Completed leaves A review, using the live client's approved leaf geometry.</summary>
internal static class AutumnLeavesReview
{
    public static Image Draw(int size)
    {
        var spec = SceneSpec.TownCorner();
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        canvas.Multiply(new Color("E8C9A0"), 0.12f);
        var bare = Polish.Scene(size, agents: true, nature: false);
        var full = Polish.Scene(size, agents: true);
        foreach (var tree in spec.Nature.Where(item => AutumnLeaves.FallsFrom(item.Value)).Select(item => item.Key))
            for (var index = 0; index < AutumnLeaves.Count; index++)
            {
                var leaf = AutumnLeaves.At(tree.X, tree.Y, index, size);
                if (leaf.X < 0 || leaf.Y < 0 || leaf.X >= canvas.Width || leaf.Y >= canvas.Height) continue;
                var tile = new Vector2I(leaf.X / size, leaf.Y / size);
                if (spec.Hydrology[tile.Y * spec.Width + tile.X] != 0 || spec.Roads.Contains(tile) ||
                    spec.Buildings.Any(building => building.Footprint.HasPoint(tile))) continue;
                if (bare.GetPixel(leaf.X, leaf.Y) != full.GetPixel(leaf.X, leaf.Y)) continue;
                canvas.Fill(leaf.X, leaf.Y, leaf.Size, leaf.Size, leaf.Color);
            }
        return canvas.ToImage();
    }
}
