using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview;

/// <summary>Compare the approved seasonal rule on the same art and scene geometry.</summary>
public static class SeasonalScenes
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var size in new[] { 16, 32 })
            foreach (var season in Enum.GetValues<LandscapeSeason>())
            {
                var art = new ArtSet
                {
                    Name = season.ToString().ToLowerInvariant(),
                    Tile = (style, variant, pixels) => TerrainTextures.Tile(style, variant, pixels, season),
                    GroundColour = style => LandscapePalette.Ground(TerrainTextures.BaseColor(style), style, season),
                    Nature = (sprite, pixels) => NatureSprites.Sprite(sprite, pixels, season),
                    EdgePiece = (style, piece, pixels) => SeasonalEdge(style, piece, pixels, season),
                };
                foreach (var (name, spec) in new[] { ("town", SceneSpec.TownCorner()), ("mountains", SceneSpec.MountainRange()) })
                {
                    var image = SceneComposer.Render(spec, art, size);
                    File.WriteAllBytes(Path.Combine(root, $"{name}-{art.Name}-{size}.png"), image.SavePngToBuffer());
                }
            }
        Console.WriteLine("Seasonal Town and mountain scenes rendered at 16 and 32 px.");
    }

    private static Image SeasonalEdge(TerrainStyle style, int piece, int size, LandscapeSeason season)
    {
        var image = TerrainTransitions.Piece(style, piece, size);
        if (!LandscapePalette.HasGrass(style) || season == LandscapeSeason.Summer) return image;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                image.SetPixel(x, y, LandscapePalette.Apply(image.GetPixel(x, y), season));
        return image;
    }
}
