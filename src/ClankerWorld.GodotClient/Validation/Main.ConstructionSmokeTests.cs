using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// A building under construction shows the approved stage its work has
    /// reached, a different picture at each stage and never the finished
    /// building; a street lantern's fitting goes up unlit on its Road edge
    /// while its materials lie on its own tile, and hovering a site says how
    /// far it has got.
    /// </summary>
    private void VerifyConstructionSites()
    {
        OwnerWorldConstructionSite Site(string id, string name, IReadOnlyList<string> tags, int x, int done,
            OwnerWorldPosition? entrance = null) =>
            new(id, "test/" + id, name, tags, new(x, 2), 1, 1, entrance, done, 10, "working", "alder", null);
        var sites = new[]
        {
            Site("town:early", "House", ["house"], 2, 1),
            Site("town:frame", "House", ["house"], 4, 5),
            Site("town:walls", "House", ["house"], 6, 8),
            Site("town:lamp", "Hanging street lantern", ["street_lantern", "hanging_lantern"], 8, 5, new(8, 3)),
        };
        terrainLayer.SetConstructionSites(sites);
        if (terrainLayer.ConstructionSiteCount != 4 || terrainLayer.ConstructionStageAt(new(2, 2)) != 1 ||
            terrainLayer.ConstructionStageAt(new(4, 2)) != 2 || terrainLayer.ConstructionStageAt(new(6, 2)) != 3 ||
            terrainLayer.ConstructionStageAt(new(8, 2)) != 2 || terrainLayer.ConstructionStageAt(new(3, 2)) != 0)
            throw new InvalidOperationException("Each construction site must show the stage its work has reached.");

        var door = new BuildingDoor(DoorSide.South);
        foreach (var size in new[] { 16, 32 })
        {
            using var finished = BuildingSprites.Render(BuildingKind.House, 1, 1, size, door);
            var stages = new List<string>();
            foreach (var stage in new[] { 1, 2, 3 })
            {
                using var site = BuildingSprites.RenderConstruction(BuildingKind.House, 1, 1, size, door, stage);
                if (site.GetWidth() != size || site.GetHeight() != size)
                    throw new InvalidOperationException($"A construction site must fill its footprint at {size} px.");
                stages.Add(HandcartPixelDigest(site));
            }
            if (stages.Distinct().Count() != 3 || stages.Contains(HandcartPixelDigest(finished)))
                throw new InvalidOperationException($"The three construction stages must differ from each other and from the finished building at {size} px.");
        }

        // The lantern goes up where it will stand: on the Road edge its project was planned against.
        var lantern = StreetLanternLight.FromSite(sites[3], 64, false)
            ?? throw new InvalidOperationException("A lantern site must find its planned Road edge.");
        if (lantern != new StreetLanternLight(new(8, 3), DoorSide.North, LanternStyle.Hanging))
            throw new InvalidOperationException("A lantern site must stand on the Road tile its project names.");
        var fitting = NightLightShapes.StreetLantern(lantern.Style, lantern.Post, lantern.Inward, 0, 0, 0)
            .Where(cell => cell.Kind == LightCellKind.Paint).ToList();
        var hole = new StreetLanternSite(lantern, 1).Cells();
        var stub = new StreetLanternSite(lantern, 2).Cells();
        var whole = new StreetLanternSite(lantern, 3).Cells();
        static float Area(IEnumerable<LightCell> cells) => cells.Sum(cell => cell.Area.Size.X * cell.Area.Size.Y);
        if (hole.Count == 0 || stub.Count == 0 || !whole.SequenceEqual(fitting) || Area(stub) >= Area(whole) ||
            new[] { hole, stub, whole }.Any(cells => cells.Any(cell => cell.Kind != LightCellKind.Paint)) ||
            stub.Any(piece => !fitting.Any(cell => cell.Area.Encloses(piece.Area) && cell.Color == piece.Color)))
            throw new InvalidOperationException("A lantern must go up as a hole, then part of its fitting, then the whole fitting, unlit until it is finished.");
        var stone = lantern with { Style = LanternStyle.Stone };
        if (Area(new StreetLanternSite(stone, 2).Cells()) >= Area(new StreetLanternSite(stone, 3).Cells()))
            throw new InvalidOperationException("A stone lamp's stub must be less than the finished pillar.");
        if (BuildingSprites.LanternSiteTexture(32, 1) is null || BuildingSprites.LanternSiteTexture(16, 2) is not { } logs ||
            logs.GetWidth() != 16 || BuildingSprites.LanternSiteTexture(32, 3) is not null)
            throw new InvalidOperationException("A lantern's materials must lie on its tile until the fitting stands.");

        if (renderedMapSnapshot is { } map && terrainMap is { } terrain)
        {
            var building = map with { ConstructionSites = sites, PlacedBuildings = [] };
            if (StreetLanternSites(building) is not [{ Stage: 2 } only] || only.Lantern != lantern)
                throw new InvalidOperationException("Only lantern sites must go to the Road-edge drawing.");
            if (!HoverSummary(building, terrain, new Vector2I(4, 2)).Contains("House · Being built · 50% done", StringComparison.Ordinal))
                throw new InvalidOperationException("Hovering a construction site must say what it is and how far it has got.");
        }
        terrainLayer.SetConstructionSites([]);
        nightLightsLayer.SetLanternSites([]);
    }
}
