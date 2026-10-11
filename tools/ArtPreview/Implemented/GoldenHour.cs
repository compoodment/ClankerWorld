using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;
using Polish = ArtPreview.Proposed.Polish.Polish;

namespace ArtPreview;

/// <summary>Approved rose/gold reference, sharing the client's colours and maximum multiply.</summary>
internal static class GoldenHourPreview
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var size in new[] { 32, 16 })
            Frame(size, 2).SavePng(Path.Combine(root, $"c-rose-gold-dusk-{size}.png"));
        var frames = Polish.Loop(8, t => Frame(32, t));
        var folder = Path.Combine(root, "c-rose-gold-day-32");
        Directory.CreateDirectory(folder);
        for (var frame = 0; frame < frames.Count; frame++)
            frames[frame].SavePng(Path.Combine(folder, $"{frame:D4}.png"));
    }

    /// <summary>
    /// The loop: day (0–1 s), dusk glow building (1–2.5), into night (2.5–4),
    /// night (4–5), dawn glow (5–6.5) and back to day (6.5–8).
    /// </summary>
    public static (float Night, float Gold, bool Dawn) Light(double t) => t switch
    {
        < 1 => (0, 0, false),
        < 2.5 => (0, Polish.Smooth((float)(t - 1) / 1.5f), false),
        < 4 => (Polish.Smooth((float)(t - 2.5) / 1.5f), 1 - Polish.Smooth((float)(t - 2.5) / 1.5f), false),
        < 5 => (1, 0, true),
        < 6.5 => (1 - Polish.Smooth((float)(t - 5) / 1.5f), Polish.Smooth((float)(t - 5) / 1.5f), true),
        _ => (0, 1 - Polish.Smooth((float)(t - 6.5) / 1.5f), true),
    };

    public static Image Frame(int size, double t)
    {
        var canvas = new Canvas(Polish.Scene(size, agents: true));
        var (night, gold, dawn) = Light(t);
        canvas.Multiply(GoldenHourTint.ColorAt(dawn), GoldenHourTint.MaximumAmount * gold);
        // The review's existing night context is preserved; this PR does not alter the client's night wash.
        canvas.Multiply(new Color("3C4C6E"), 0.45f * night);
        return canvas.ToImage();
    }
}
