// The three protocol records the terrain map reads, copied from the client's
// Protocol/OwnerWorldContracts.cs so the map compiles without the network code.
namespace ClankerWorld.GodotClient.UI;

public sealed record OwnerWorldTile(int X, int Y, string Terrain);
public sealed record OwnerWorldPackedTerrain(int Width, int Height, string Encoding, string Data);
public sealed record OwnerWorldPackedMapLayers(int Width, int Height, string Encoding,
    string Climate, string Elevation, string Hydrology, string Surface, string Vegetation)
{
    public string? Fertility { get; init; }
}
