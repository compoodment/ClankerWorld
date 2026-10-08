using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    // Native regressions retain approved pixels and put construction parts beside their real Road/bank.
    private static void VerifyConstructionPixelContracts()
    {
        foreach (var (kind, width, height) in new[]
        {
            (BuildingKind.House, 1, 1), (BuildingKind.House, 2, 3),
            (BuildingKind.Port, 2, 4), (BuildingKind.Port, 4, 2), (BuildingKind.AnimalYard, 2, 2),
        })
            foreach (var side in Enum.GetValues<DoorSide>())
                foreach (var stage in new[] { 1, 2, 3 })
                {
                    using var full = BuildingSprites.RenderConstruction(kind, width, height, 32, new BuildingDoor(side), stage);
                    using var half = BuildingSprites.RenderConstruction(kind, width, height, 16, new BuildingDoor(side), stage);
                    RequireConstructionHalf(full, half, $"{kind} {side} stage {stage}");
                }
        foreach (var stage in new[] { 1, 2 })
        {
            using var full = BuildingSprites.LanternSiteTexture(32, stage)!.GetImage();
            using var half = BuildingSprites.LanternSiteTexture(16, stage)!.GetImage();
            RequireConstructionHalf(full, half, $"Lantern materials stage {stage}");
        }

        foreach (var (side, width, height) in new[]
        {
            (DoorSide.North, 2, 4), (DoorSide.South, 2, 4),
            (DoorSide.East, 4, 2), (DoorSide.West, 4, 2),
        })
            foreach (var stage in new[] { 1, 2 })
            {
                using var image = BuildingSprites.RenderConstruction(BuildingKind.Port, width, height, 32, new BuildingDoor(side), stage);
                var pixels = image.GetData();
                var caps = new List<Vector2I>();
                for (var y = 0; y < image.GetHeight() - 1; y++)
                    for (var x = 0; x < image.GetWidth() - 11; x++)
                        if (Enumerable.Range(0, 9).All(offset => ConstructionColor(pixels, image.GetWidth(), x + offset, y, 167, 124, 82)) &&
                            ConstructionColor(pixels, image.GetWidth(), x + 9, y, 210, 172, 119) &&
                            ConstructionColor(pixels, image.GetWidth(), x + 10, y, 210, 172, 119) &&
                            ConstructionColor(pixels, image.GetWidth(), x + 10, y + 1, 110, 78, 49))
                            caps.Add(new(x, y));
                if (caps.Count != 1 || caps.Any(cap => side switch
                    {
                        DoorSide.North => cap.Y >= 32,
                        DoorSide.South => cap.Y < image.GetHeight() - 32,
                        DoorSide.West => cap.X >= 32,
                        _ => cap.X < image.GetWidth() - 32,
                    }))
                    throw new InvalidOperationException($"Port construction materials must wait on the {side} bank at stage {stage}.");
            }

        foreach (var side in new[] { DoorSide.East, DoorSide.West })
            foreach (var doorTile in new[] { 0, 2 })
            {
                using var image = BuildingSprites.RenderConstruction(BuildingKind.House, 2, 3, 32, new BuildingDoor(side, doorTile), 3);
                var pixels = image.GetData();
                var ladders = new List<Vector2I>();
                for (var y = 0; y < image.GetHeight() - 8; y++)
                    for (var x = 0; x < image.GetWidth() - 4; x++)
                        if (Enumerable.Range(0, 8).All(offset =>
                                ConstructionColor(pixels, image.GetWidth(), x, y + offset, 167, 124, 82) &&
                                ConstructionColor(pixels, image.GetWidth(), x + 3, y + offset, 138, 100, 64)) &&
                            Enumerable.Range(0, 4).All(rung => ConstructionColor(pixels, image.GetWidth(), x + 1, y + 2 * rung + 1, 210, 172, 119)))
                            ladders.Add(new(x, y));
                if (ladders.Count != 1 || ladders.Any(ladder =>
                        (side == DoorSide.West ? ladder.X >= image.GetWidth() / 2 : ladder.X < image.GetWidth() / 2) ||
                        (doorTile == 0 ? ladder.Y >= image.GetHeight() / 2 : ladder.Y < image.GetHeight() / 2)))
                    throw new InvalidOperationException($"The construction ladder must stand beside the {side} door at tile {doorTile}.");
            }
    }

    private static void RequireConstructionHalf(Image full, Image half, string name)
    {
        var original = full.GetData();
        var actual = half.GetData();
        if (full.GetWidth() != half.GetWidth() * 2 || full.GetHeight() != half.GetHeight() * 2)
            throw new InvalidOperationException($"{name} must keep its footprint at both zooms.");
        for (var y = 0; y < half.GetHeight(); y++)
            for (var x = 0; x < half.GetWidth(); x++)
                for (var channel = 0; channel < 4; channel++)
                    if (original[((2 * y) * full.GetWidth() + 2 * x) * 4 + channel] !=
                        actual[(y * half.GetWidth() + x) * 4 + channel])
                        throw new InvalidOperationException($"{name} must retain the approved top-left pixels at 16 px.");
    }

    private static bool ConstructionColor(byte[] pixels, int width, int x, int y, byte red, byte green, byte blue)
    {
        var offset = (y * width + x) * 4;
        return pixels[offset] == red && pixels[offset + 1] == green &&
            pixels[offset + 2] == blue && pixels[offset + 3] == 255;
    }
}
