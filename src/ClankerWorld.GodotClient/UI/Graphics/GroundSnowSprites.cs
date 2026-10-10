using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>The approved covered-ground colour and pressed footprints, shared with the art renderer.</summary>
public static class GroundSnowSprites
{
    private static readonly Color Snow = new("E9EEF2");
    private static readonly Color Shade = new("C9D3DC");

    public static Color Overlay(Color ground, bool road)
    {
        var luma = ground.R * 0.3f + ground.G * 0.59f + ground.B * 0.11f;
        var amount = road ? 0.72f : 0.9f - Math.Clamp(0.35f - luma, 0, 0.35f) * 0.9f;
        var snow = Shade.Lerp(Snow, Math.Clamp(luma * 1.8f, 0, 1));
        return (road ? snow.Lerp(new Color("B7BCBF"), 0.3f) : snow) with { A = amount };
    }

    public static Color Tint(Color ground, bool road)
    {
        var overlay = Overlay(ground, road);
        return ground.Lerp(overlay with { A = 1 }, overlay.A);
    }

    public static Image OverlayTile(TerrainStyle style, int variant, RoadLinks links, int roadVariant, int size)
    {
        using var ground = TerrainTextures.Tile(style, variant, size);
        using var road = RoadSprites.Draws(links)
            ? RoadSprites.Render(links, roadVariant, size, RoadSprites.NeedsDarkEdge(style)) : null;
        var result = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var color = ground.GetPixel(x, y);
                if (road is not null)
                {
                    var dirt = road.GetPixel(x, y);
                    color = color.Lerp(dirt with { A = 1 }, dirt.A);
                }
                result.SetPixel(x, y, Overlay(color, links.HasFlag(RoadLinks.Road) && color.R > color.G - 0.02f));
            }
        return result;
    }

    public static float PrintAlpha(float ageInHours)
    {
        var age = Math.Clamp(ageInHours, 0, 1);
        return 0.9f * (1 - age * age * (3 - 2 * age));
    }

    public static IEnumerable<(Rect2I Area, Color Color)> Print(int x, int y, int size, float alpha)
    {
        var dot = Math.Max(1, size / 16);
        yield return (new Rect2I(x, y, dot * 3, dot * 2), new Color("7D8B99") with { A = alpha });
        yield return (new Rect2I(x, y, dot * 3, dot), new Color("98A6B3") with { A = alpha * 0.8f });
    }
}
