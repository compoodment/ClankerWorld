using Godot;

namespace ClankerWorld.GodotClient.UI;

public partial class WorldTerrainLayer
{
    private readonly Dictionary<(BuildingKind Kind, int Width, int Height, int Size, BuildingDoor Door, BuildingNeglect Neglect, int Seed), ImageTexture> roofSnowTextures = [];
    internal int DrawnSnowRoofCount { get; private set; }
    internal int DrawnSnowRoofRegionCount { get; private set; }
    internal Rect2 LastSnowRoofRegion { get; private set; }
    internal int RoofSnowTextureCount => roofSnowTextures.Count;

    private void DrawRoofSnow(Rect2I footprint, BuildingKind kind, BuildingDoor door, BuildingNeglect neglect,
        int atlasSize, int shift, Rect2 placed, int stride)
    {
        if (tileSize < SpriteTileMinimum || groundSnowRegions.Count == 0 ||
            kind is BuildingKind.AnimalYard or BuildingKind.Hearth or BuildingKind.Path or BuildingKind.Bedroll or BuildingKind.Shelter or BuildingKind.Storehouse) return;
        var seed = (int)(PixelArt.Hash(footprint.Position.X, footprint.Position.Y, 47) % 10_000);
        var key = (kind, footprint.Size.X, footprint.Size.Y, atlasSize, door, neglect, seed);
        ImageTexture? texture = null;
        var drawn = false;
        // Split by authoritative weather regions, so a roof crossing an edge
        // does not borrow snow from its dry neighbour. Wrapped copies share art.
        for (var ry = footprint.Position.Y / weatherRegionSize; ry <= (footprint.End.Y - 1) / weatherRegionSize; ry++)
            for (var rx = footprint.Position.X / weatherRegionSize; rx <= (footprint.End.X - 1) / weatherRegionSize; rx++)
            {
                var cover = SnowCoverAt(rx * weatherRegionSize, ry * weatherRegionSize);
                if (cover <= 0) continue;
                if (texture is null && !roofSnowTextures.TryGetValue(key, out texture))
                {
                    using var image = BuildingSprites.SnowOverlay(kind, footprint.Size.X, footprint.Size.Y, atlasSize, door, neglect, seed);
                    texture = ImageTexture.CreateFromImage(image);
                    if (roofSnowTextures.Count >= 256) roofSnowTextures.Remove(roofSnowTextures.Keys.First());
                    roofSnowTextures.Add(key, texture);
                }
                var region = new Rect2((rx * weatherRegionSize + shift) * stride, ry * weatherRegionSize * stride,
                    weatherRegionSize * stride, weatherRegionSize * stride);
                var shown = placed.Intersection(region);
                if (!shown.HasArea()) continue;
                var scale = new Vector2(texture.GetWidth() / placed.Size.X, texture.GetHeight() / placed.Size.Y);
                DrawTextureRectRegion(texture, shown,
                    new Rect2((shown.Position - placed.Position) * scale, shown.Size * scale), Colors.White with { A = cover });
                DrawnSnowRoofRegionCount++;
                LastSnowRoofRegion = shown;
                drawn = true;
            }
        if (drawn) DrawnSnowRoofCount++;
    }
}
