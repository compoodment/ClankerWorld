using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;
using Polish = ArtPreview.Proposed.Polish.Polish;

namespace ArtPreview;

/// <summary>The implemented finish-C stills and loop, drawn with the native client's pixel spans.</summary>
internal static class BuildingCompletionPreview
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var size in new[] { 32, 16 })
        {
            Frame(size, 0.9).SavePng(Path.Combine(root, $"finish-c-sparkle-{size}.png"));
            var frames = Polish.Loop(BuildingCompletionArt.Duration, t => Frame(size, t));
            var folder = Path.Combine(root, $"finish-c-sparkle-{size}");
            Directory.CreateDirectory(folder);
            for (var frame = 0; frame < frames.Count; frame++)
                frames[frame].SavePng(Path.Combine(folder, $"{frame:D2}.png"));
        }
    }

    public static Image Frame(int size, double seconds)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        BuildingCompletionArt.Paint(new Rect2(13 * size, 7 * size, 2 * size, size), size, seconds,
            (x, y, width, colour) => canvas.Fill(x, y, width, 1, colour));
        return canvas.ToImage();
    }
}
