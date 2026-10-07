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
        VerifyAbandonedLanternsAndBridges();
    }

    /// <summary>
    /// Street lanterns and bridges in an abandoned Town weather with its
    /// buildings: neglected, then falling apart after a full season. A
    /// lantern keeps its place on the Road edge.
    /// </summary>
    private void VerifyAbandonedLanternsAndBridges()
    {
        OwnerWorldBridge Bridge(string id, int y) => new(id, "plank", "test", "east_west",
            [new(9, y), new(13, y)], [new(10, y), new(11, y), new(12, y)], 0);
        OwnerWorldTown Town(string id, IReadOnlyList<string> residents, bool fallingApart, int y) =>
            new(id, id, "founded", 0, residents, [], [new(9, y), new(13, y)]) { FallingApart = fallingApart };
        terrainLayer.SetBridges([Bridge("lived", 4), Bridge("empty", 6), Bridge("ruin", 8)],
            [Town("alder", ["rowan"], false, 4), Town("birch", [], false, 6), Town("cedar", [], true, 8)]);
        if (terrainLayer.BridgeNeglectAt(new(11, 4)) != BuildingNeglect.None ||
            terrainLayer.BridgeNeglectAt(new(10, 6)) != BuildingNeglect.Neglected ||
            terrainLayer.BridgeNeglectAt(new(12, 8)) != BuildingNeglect.FallingApart ||
            terrainLayer.BridgeNeglectAt(new(9, 8)) != BuildingNeglect.None)
            throw new InvalidOperationException("A bridge's deck must weather with its abandoned Town and stay plain in a lived-in one.");
        foreach (var size in new[] { 16, 32 })
        {
            using var neglected = BuildingSprites.RenderNeglectedBridge(true, 3, size, BuildingNeglect.Neglected);
            using var ruin = BuildingSprites.RenderNeglectedBridge(true, 3, size, BuildingNeglect.FallingApart);
            using var upright = BuildingSprites.RenderNeglectedBridge(false, 2, size, BuildingNeglect.FallingApart);
            if (neglected.GetWidth() != 5 * size || neglected.GetHeight() != size ||
                upright.GetWidth() != size || upright.GetHeight() != 4 * size ||
                HandcartPixelDigest(neglected) == HandcartPixelDigest(ruin))
                throw new InvalidOperationException("A weathered bridge must span bank to bank and lose planks once it falls apart.");
        }
        terrainLayer.SetBridges([]);

        if (renderedMapSnapshot is not { } map) return;
        OwnerWorldPlacedBuilding Lantern(string id, string town, int x) =>
            new(id, "test/stone-lantern", new(x, 8), 0, "Stone street lamp", ["street_lantern", "stone_lantern"], 1, 1, town,
                Entrance: new(x, 9));
        var abandoned = map with
        {
            PlacedBuildings = [Lantern("lamp-lived", "alder", 2), Lantern("lamp-empty", "birch", 4), Lantern("lamp-ruin", "cedar", 6)],
            Towns = [Town("alder", ["rowan"], false, 0), Town("birch", [], false, 0), Town("cedar", [], true, 0)],
        };
        var lanterns = StreetLanterns(abandoned);
        BuildingNeglect At(int x) => lanterns.Single(lantern => lantern.RoadTile.X == x).Neglect;
        if (lanterns.Count != 3 || At(2) != BuildingNeglect.None || At(4) != BuildingNeglect.Neglected ||
            At(6) != BuildingNeglect.FallingApart || lanterns.Any(lantern => lantern.Edge != DoorSide.North))
            throw new InvalidOperationException("Street lanterns must weather with their abandoned Town and keep their Road edge.");
        var plain = NightLightShapes.StreetLantern(LanternStyle.Stone, lanterns[1].Post, lanterns[1].Inward, 0, 0, 0)
            .Where(cell => cell.Kind == LightCellKind.Paint).ToList();
        var neglectedCells = lanterns.Single(lantern => lantern.RoadTile.X == 4).WeatheredCells();
        var halved = lanterns.Single(lantern => lantern.RoadTile.X == 6).WeatheredCells(2);
        if (neglectedCells.Count == 0 || neglectedCells.Any(cell => cell.Kind != LightCellKind.Paint) ||
            neglectedCells.Select(cell => cell.Color).ToHashSet().SetEquals(plain.Select(cell => cell.Color)) ||
            halved.Count == 0 || halved.Any(cell => cell.Area.Size != new Vector2(2, 2)))
            throw new InvalidOperationException("A weathered lantern must show its own faded fitting, halved at mid zoom.");
    }
}
