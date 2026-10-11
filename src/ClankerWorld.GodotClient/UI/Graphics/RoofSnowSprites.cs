using Godot;

namespace ClankerWorld.GodotClient.UI;

internal enum RoofSnowShape { GableEastWest, GableNorthSouth, Hipped, Cone, SingleNorth, SingleEast, SingleSouth, SingleWest }

/// <summary>Approved roof snow A2: shade, ragged sunlit melt lines and a dusting below them.</summary>
internal static class RoofSnowSprites
{
    private static readonly Color Snow = new("E9EEF2");
    private static readonly Color SnowShade = new("C9D3DC");
    private static float Hash01(int x, int y, int salt) => PixelArt.Hash(x, y, salt) % 10_000 / 10_000f;

    public static Color Tint(Color color, float amount)
    {
        var luma = color.R * 0.3f + color.G * 0.59f + color.B * 0.11f;
        return color.Lerp(SnowShade.Lerp(Snow, Math.Clamp((luma - 0.2f) * 2.2f, 0, 1)), amount);
    }

    public static float SlopeCover(int x, int y, int size, RoofSnowShape shape, (int X0, int Y0, int X1, int Y1) roof, int seed)
    {
        const float shaded = 0.74f, sunlit = 0.64f, dusting = 0.24f;
        float w = roof.X1 - roof.X0 + 1, h = roof.Y1 - roof.Y0 + 1;
        float px = x + 0.5f - roof.X0, py = y + 0.5f - roof.Y0;
        var grain = Math.Max(1, size / 16);
        // On a sunlit face: snow down to a ragged line about 45% of the way from
        // ridge to eave, then a thin dusting with a few brighter flecks.
        float Sunlit(float fromEave, float depth, float along)
        {
            var line = depth * (0.55f + (Hash01((int)(along / (2 * grain)), seed, 47) - 0.5f) * 0.18f);
            if (fromEave > line) return sunlit;
            return Hash01(x / grain, y / grain, 53 + seed) < 0.2f ? sunlit * 0.75f : dusting;
        }
        switch (shape)
        {
            case RoofSnowShape.SingleNorth:
            case RoofSnowShape.SingleEast:
                return shaded;
            case RoofSnowShape.SingleSouth:
                return Sunlit(h - py, h, px);
            case RoofSnowShape.SingleWest:
                return Sunlit(px, w, py);
            case RoofSnowShape.GableEastWest:
                return py < h / 2 ? shaded : Sunlit(h - py, h / 2, px);
            case RoofSnowShape.GableNorthSouth:
                return px >= w / 2 ? shaded : Sunlit(px, w / 2, py);
            case RoofSnowShape.Cone:
                {
                    float dx = px - w / 2, dy = py - h / 2, r = MathF.Sqrt(dx * dx + dy * dy), radius = w / 2;
                    if (dx - dy > 0) return shaded;
                    return Sunlit(radius - r, radius, MathF.Atan2(dy, dx) * radius);
                }
            default:
                {
                    // A hipped roof at 45 degrees: the nearest eave names the face.
                    float north = py, south = h - py, west = px, east = w - px;
                    var depth = MathF.Min(w, h) / 2;
                    var nearest = MathF.Min(MathF.Min(north, south), MathF.Min(west, east));
                    if (nearest == north || nearest == east) return shaded;
                    return nearest == south ? Sunlit(south, depth, px) : Sunlit(west, depth, py);
                }
        }
    }

}
