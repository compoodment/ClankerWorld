using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.SmallVehicles;

/// <summary>
/// Issue #914 (October 7): 16 px handcarts and rowing boats for medium map
/// zoom, where the map today shows the cart's item icon and shrinks the boat.
/// Each is made from the approved 32 px drawing so it keeps the same shape,
/// colours and north-west light: every 2 × 2 block of the approved picture
/// becomes one pixel, taking the block's most common solid colour (the darker
/// one on a tie, so rims and outlines survive), and a thin part such as a
/// shaft or an oar keeps its pixel even when it fills only part of a block.
/// The ground shadow stays soft. Every facing, empty and loaded, both pulled
/// poses and the boat at rest and rowing are shown beside the approved 32 px
/// picture.
/// </summary>
public sealed class SmallVehiclesProposal : IArtProposal
{
    public string Family => "small-vehicles";

    /// <summary>The game's facing order: S, SW, W, NW, N, NE, E, SE.</summary>
    private static readonly string[] Names = ["S", "SW", "W", "NW", "N", "NE", "E", "SE"];

    public IEnumerable<Entry> Render()
    {
        yield return new(Family, "handcart.empty.turnaround", Strip(f => HandcartSprites.Sprite(f, false, false), Grass),
            "The empty handcart at 16 px in all eight facings, S, SW, W, NW, N, NE, E, SE, above the approved 32 px drawing halved by eye.");
        yield return new(Family, "handcart.loaded.turnaround", Strip(f => HandcartSprites.Sprite(f, true, false), Grass),
            "Loaded with lashed logs and a sack.");
        yield return new(Family, "handcart.pulled", Strip(f => HandcartSprites.Sprite(f, true, true), Grass, [6, 7]),
            "Being pulled east and south-east: the shafts lifted, as in the approved 32 px poses.");
        yield return new(Family, "boat.rowing.turnaround", Strip(f => BoatSprites.Sprite(f, true), Water),
            "The rowing boat at 16 px in all eight facings, oars out.");
        yield return new(Family, "boat.resting.turnaround", Strip(f => BoatSprites.Sprite(f, false), Water),
            "At rest, oars shipped.");
        foreach (var facing in new[] { 0, 6, 7 })
        {
            yield return new(Family, $"handcart.loaded.{Names[facing]}.16", Over(Grass(16), Small(HandcartSprites.Sprite(facing, true, false))));
            yield return new(Family, $"boat.rowing.{Names[facing]}.16", Over(Water(16), Small(BoatSprites.Sprite(facing, true))));
        }
    }

    private static Image Grass(int size) => TerrainTextures.Tile(TerrainStyle.Grass, 0, size);

    private static Image Water(int size)
    {
        var atlas = WaterTextures.Atlas(size).GetImage();
        var tile = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        tile.BlitRect(atlas, (Rect2I)WaterTextures.Region(TerrainStyle.River, 0, 0, size), Vector2I.Zero);
        return tile;
    }

    private static Image Over(Image ground, Image sprite)
    {
        Sheet.Blend(ground, sprite, 0, 0);
        return ground;
    }

    /// <summary>Review aid: the 16 px drawings on the top row (shown at 2×) and the approved 32 px drawings below, one column per facing.</summary>
    private static Image Strip(Func<int, Image> draw, Func<int, Image> ground, int[]? facings = null)
    {
        facings ??= [0, 1, 2, 3, 4, 5, 6, 7];
        var image = Bitmap.Empty(32 * facings.Length, 64);
        for (var k = 0; k < facings.Length; k++)
        {
            var approved = draw(facings[k]);
            var small = Over(Grounded(ground, 16), Small(approved));
            small.Resize(32, 32, Image.Interpolation.Nearest);
            image.BlitRect(small, new Rect2I(0, 0, 32, 32), new Vector2I(k * 32, 0));
            image.BlitRect(Over(Grounded(ground, 32), approved), new Rect2I(0, 0, 32, 32), new Vector2I(k * 32, 32));
        }
        return image;
    }

    private static Image Grounded(Func<int, Image> ground, int size) => ground(size).Duplicate();

    /// <summary>
    /// The 16 px drawing made from an approved 32 px one. Each 2 × 2 block
    /// becomes its most common solid colour, or on a tie the one nearest the
    /// block's average brightness, so planks and decks keep their middle
    /// tones; a block with a single solid pixel keeps it, so one-pixel shafts
    /// and oars stay; a block of only shadow keeps the shadow. Then every
    /// pixel on the outside of the silhouette takes the darkest colour of its
    /// block, so the drawing keeps its dark rim as the approved one does.
    /// </summary>
    public static Image Small(Image approved)
    {
        var small = Bitmap.Empty(16, 16);
        var rims = new Color?[16, 16];
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
            {
                var block = new[]
                {
                    approved.GetPixel(2 * x, 2 * y), approved.GetPixel(2 * x + 1, 2 * y),
                    approved.GetPixel(2 * x, 2 * y + 1), approved.GetPixel(2 * x + 1, 2 * y + 1),
                };
                var solid = block.Where(c => c.A > 0.6f).ToArray();
                if (solid.Length > 0)
                {
                    var mean = solid.Average(c => c.Luminance);
                    var pick = solid.GroupBy(c => (MathF.Round(c.R * 255), MathF.Round(c.G * 255), MathF.Round(c.B * 255)))
                        .OrderByDescending(group => group.Count())
                        .ThenBy(group => MathF.Abs(group.First().Luminance - mean))
                        .First().First();
                    small.SetPixel(x, y, pick);
                    rims[x, y] = solid.MinBy(c => c.Luminance);
                    continue;
                }
                var shade = block.Where(c => c.A > 0.02f).ToArray();
                if (shade.Length >= 2) small.SetPixel(x, y, shade[0]);
            }
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < 16 && y < 16 && rims[x, y] is not null;
        var rimmed = small.Duplicate();
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                if (rims[x, y] is { } rim && (!Solid(x + 1, y) || !Solid(x - 1, y) || !Solid(x, y + 1) || !Solid(x, y - 1)) &&
                    small.GetPixel(x, y).Luminance - rim.Luminance > 0.12f)
                    rimmed.SetPixel(x, y, rim);
        return rimmed;
    }
}
