using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Buildings in an abandoned Town look neglected, and falling apart once
    /// it has stood empty for a full season; a lived-in Town's buildings are
    /// unchanged.
    /// </summary>
    private void VerifyAbandonedBuildingLooks()
    {
        OwnerWorldPlacedBuilding House(string id, int x, string town) =>
            new(id, "sha256:test/house", new(x, 2), 0, "House", ["house"], 1, 1, town);
        OwnerWorldTown Town(string id, IReadOnlyList<string> residents, bool fallingApart) =>
            new(id, id, "founded", 0, residents, [], []) { FallingApart = fallingApart };
        terrainLayer.SetBuildings([House("lived", 2, "alder"), House("empty", 4, "birch"), House("ruin", 6, "cedar")], [],
            [Town("alder", ["rowan"], false), Town("birch", [], false), Town("cedar", [], true)]);
        if (terrainLayer.NeglectAt(new(2, 2)) != BuildingNeglect.None || terrainLayer.NeglectAt(new(4, 2)) != BuildingNeglect.Neglected ||
            terrainLayer.NeglectAt(new(6, 2)) != BuildingNeglect.FallingApart)
            throw new InvalidOperationException("Buildings must look neglected in an abandoned Town and fall apart after a season.");
        var door = new BuildingDoor(DoorSide.South);
        foreach (var size in new[] { 16, 32 })
        {
            using var kept = BuildingSprites.Render(BuildingKind.House, 1, 1, size, door);
            using var neglected = BuildingSprites.Render(BuildingKind.House, 1, 1, size, door, BuildingNeglect.Neglected);
            using var ruin = BuildingSprites.Render(BuildingKind.House, 1, 1, size, door, BuildingNeglect.FallingApart);
            if (neglected.GetWidth() != size || HandcartPixelDigest(kept) == HandcartPixelDigest(neglected) ||
                HandcartPixelDigest(neglected) == HandcartPixelDigest(ruin))
                throw new InvalidOperationException("The neglected and falling-apart looks must differ from a lived-in building at both tile sizes.");
        }
        terrainLayer.SetBuildings([], []);
    }
}
