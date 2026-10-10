using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;
using Polish = ArtPreview.Proposed.Polish.Polish;

namespace ArtPreview;

/// <summary>Approved ground snow A and footprints, rendered with the client's exact pixel rules.</summary>
internal static class SnowMarksPreview
{
    private static readonly Color Snow = new("E9EEF2");
    private static readonly Color SnowShade = new("C9D3DC");

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var size in new[] { 32, 16 })
        {
            // Retain the reviewed still's roof dusting as reference context; client roof snow is a separate issue.
            Scene(size).SavePng(Path.Combine(root, $"ground-snow-a-{size}.png"));
            var frames = Polish.Loop(5, t => Footprints(size, t));
            var folder = Path.Combine(root, $"footprints-{size}");
            Directory.CreateDirectory(folder);
            for (var frame = 0; frame < frames.Count; frame++)
                frames[frame].SavePng(Path.Combine(folder, $"{frame:D4}.png"));
        }
    }

    public static Image Scene(int size, bool patchy = false)
    {
        var canvas = WeatherMarksProposal.Winter(size);
        GroundSnow(canvas, size, patchy);
        var roofs = WeatherMarksProposal.Roofs(size);
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                if (!roofs[y * canvas.Width + x]) continue;
                var c = canvas.Get(x, y);
                var luma = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
                if (luma < 0.2f) continue;
                canvas.Set(x, y, c.Lerp(SnowShade.Lerp(Snow, Math.Clamp((luma - 0.2f) * 2.2f, 0, 1)), patchy ? 0.45f : 0.6f));
            }
        return canvas.ToImage();
    }

    public static void GroundSnow(Canvas canvas, int size, bool patchy, bool agents = true)
    {
        var spec = SceneSpec.TownCorner();
        var roofs = WeatherMarksProposal.Roofs(size);
        var withThings = Polish.Scene(size, agents: agents);
        var withoutAgents = Polish.Scene(size, agents: false, buildings: true, nature: false);
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                var tx = x / size; var ty = y / size;
                if (spec.Hydrology[ty * spec.Width + tx] != 0 || spec.Bridges.ContainsKey(new(tx, ty))) continue;
                // Roofs get their own snow; yards, shadows and doorsteps get ground snow.
                if (roofs[y * canvas.Width + x]) continue;
                // Skip anything standing on the ground: trees, plants and agents.
                if (withThings.GetPixel(x, y) != withoutAgents.GetPixel(x, y)) continue;
                var c = canvas.Get(x, y);
                // Within a Road tile only the packed dirt is trodden; its grassy edges snow over like any ground.
                var road = spec.Roads.Contains(new(tx, ty)) && c.R > c.G - 0.02f;
                if (!patchy)
                {
                    canvas.Set(x, y, GroundSnowSprites.Tint(c, road));
                    continue;
                }
                var overlay = GroundSnowSprites.Overlay(c, road);
                var n = WeatherMarksProposal.Noise(x / (1.1f * size), y / (1.1f * size));
                canvas.Set(x, y, c.Lerp(overlay with { A = 1 }, overlay.A * Polish.Smooth((n - 0.38f) / 0.18f)));
            }
    }

    public static Image Footprints(int size, double t)
    {
        // Snow lying on open ground, as the world reports it for a snowy region.
        var canvas = new Canvas(Polish.Scene(size));
        GroundSnow(canvas, size, patchy: false, agents: false);
        var speed = 1.6;
        var x0 = 6 * size + size / 2f;
        var y = 9.4f * size;
        var traveled = (float)(t * speed * size);
        var stride = size * 0.28f;
        for (var n = 0; n * stride < traveled; n++)
        {
            var age = (traveled - n * stride) / (float)(speed * size);
            var alpha = GroundSnowSprites.PrintAlpha(age / 3.6f);
            var px = x0 + n * stride;
            var py = y + (n % 2 == 0 ? -2.5f : 2.5f) * size / 32f;
            foreach (var (area, color) in GroundSnowSprites.Print((int)px, (int)py, size, alpha))
                canvas.Fill(area.Position.X, area.Position.Y, area.Size.X, area.Size.Y, color);
        }
        var drawn = (int)MathF.Round(size * SceneComposer.AgentSpriteScale);
        var agent = AgentSprites.Sprite(2, AgentSprites.StageIndex("adult"), AgentSprites.FacingToward(1, 0),
            (int)(t * 4) % 2 == 0 ? AgentFrame.Walk1 : AgentFrame.Walk2, drawn >= 24 ? 32 : 16);
        canvas.Stamp(agent, (int)(x0 + traveled - drawn / 2f), (int)(y - drawn * 0.62f), drawn);
        return canvas.ToImage();
    }
}
