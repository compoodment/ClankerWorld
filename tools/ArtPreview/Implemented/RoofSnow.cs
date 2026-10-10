using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;
using Polish = ArtPreview.Proposed.Polish.Polish;

namespace ArtPreview;

/// <summary>The owner-approved roof snow A2 drawing, using the live tint and slope rules.</summary>
internal static class RoofSnowReview
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var size in new[] { 32, 16 })
            Render(size).SavePng(Path.Combine(root, $"roof-snow-a2-{size}.png"));
    }

    /// <summary>The shape each building's roof art draws, read off the art.</summary>
    private static RoofSnowShape ShapeOf(BuildingKind kind) => kind switch
    {
        BuildingKind.Warehouse => RoofSnowShape.GableEastWest,
        BuildingKind.Blacksmith => RoofSnowShape.GableNorthSouth,
        BuildingKind.Silo => RoofSnowShape.Cone,
        _ => RoofSnowShape.Hipped,
    };

    /// <summary>
    /// Which building's roof each pixel belongs to, or -1. Like the original roof mask,
    /// but leaves out the building's shadow on the grass (a pixel that is only a
    /// darker copy of the ground), the Blacksmith's open forge yard and the
    /// Farmhouse's grain sacks.
    /// </summary>
    private static int[] RoofOwners(int size)
    {
        var with = Polish.Scene(size);
        var without = Polish.Scene(size, buildings: false);
        var owners = new int[with.GetWidth() * with.GetHeight()];
        Array.Fill(owners, -1);
        for (var i = 0; i < Polish.Buildings.Count; i++)
        {
            var f = Polish.Buildings[i].Footprint;
            var kind = Polish.Buildings[i].Kind;
            var right = kind == BuildingKind.Blacksmith ? f.Position.X * size + f.Size.X * size * 0.66f : f.End.X * size;
            var bottom = kind == BuildingKind.Farmhouse ? f.Position.Y * size + f.Size.Y * size * 0.68f : f.End.Y * size;
            for (var y = f.Position.Y * size; y < bottom; y++)
                for (var x = f.Position.X * size; x < right; x++)
                {
                    Color w = with.GetPixel(x, y), o = without.GetPixel(x, y);
                    if (w == o) continue;
                    float rr = w.R / MathF.Max(o.R, 0.01f), rg = w.G / MathF.Max(o.G, 0.01f), rb = w.B / MathF.Max(o.B, 0.01f);
                    var shadow = MathF.Max(rr, MathF.Max(rg, rb)) - MathF.Min(rr, MathF.Min(rg, rb)) < 0.1f && rg is > 0.35f and < 0.98f;
                    if (!shadow) owners[y * with.GetWidth() + x] = i;
                }
        }
        return owners;
    }

    /// <summary>
    /// Snow that follows each roof's own slopes. The shaded faces (north and
    /// east, away from the north-west light the art uses) stay covered. Each
    /// sunlit face (south and west) keeps snow from its ridge down to a ragged
    /// melt line that runs along its eave, and below that a thin, broken
    /// dusting, so no face is ever bare. A gable roof has two faces, a hipped
    /// roof four (the nearest eave decides the face), and the Silo's cone
    /// melts on its south-west side.
    /// </summary>
    public static Image Render(int size)
    {
        var canvas = WeatherMarksProposal.Winter(size);
        var owners = RoofOwners(size);
        var bounds = new (int X0, int Y0, int X1, int Y1)[Polish.Buildings.Count];
        Array.Fill(bounds, (int.MaxValue, int.MaxValue, -1, -1));
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                var i = owners[y * canvas.Width + x];
                if (i < 0) continue;
                var b = bounds[i];
                bounds[i] = (Math.Min(b.X0, x), Math.Min(b.Y0, y), Math.Max(b.X1, x), Math.Max(b.Y1, y));
            }
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                var i = owners[y * canvas.Width + x];
                if (i < 0) continue;
                var c = canvas.Get(x, y);
                var luma = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
                if (luma < 0.2f) continue; // outlines, flues and eave shadow stay dark
                var amount = RoofSnowSprites.SlopeCover(x, y, size, ShapeOf(Polish.Buildings[i].Kind), bounds[i], i);
                if (amount <= 0) continue;
                canvas.Set(x, y, RoofSnowSprites.Tint(c, amount));
            }
        return canvas.ToImage();
    }

}
