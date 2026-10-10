using ClankerWorld.GodotClient.UI;
using Godot;
using System.Reflection;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyStoredStockAsync(OwnerWorldSnapshot map)
    {
        var (zoom, center) = (cameraZoom, cameraCenterTiles);
        var land = Enumerable.Range(8, 8).SelectMany(y => Enumerable.Range(8, 16)
            .Select(x => new OwnerWorldPosition(x, y))).ToArray();
        var house = new OwnerWorldPlacedBuilding("stock-house", "test/house", new(11, 10), 0, "House", ["house"],
            TownId: "stock-town", StoredItems: [new("wood", 40), new("grain", 35), new("stone", 5)],
            Entrance: new(11, 11), StorageCapacity: 100, StoredQuantity: 80);
        var occupied = map with
        {
            WorldId = map.WorldId + ":stock",
            PackedTerrain = map.PackedTerrain! with { Data = Convert.ToBase64String(new byte[256 * 128]) },
            Inhabitants = [],
            Objects = [],
            Resources = [],
            Fields = [],
            GroundStocks = [],
            ConstructionSites = [],
            Bridges = [],
            PlacedBuildings = [house, new("empty-store", "test/warehouse", new(20, 10), 0, "Warehouse", ["warehouse"],
                2, 2, "stock-town", StoredItems: [], Entrance: new(21, 12), StorageCapacity: 100)],
            Towns = [new("stock-town", "Stock Town", "founded", 0, ["resident"], [house.InstanceId], land)],
            RoadTiles = [new(10, 11)],
            TownLandTitles = [],
            ProductionJobs = [],
        };
        StoredStockPile[] Piles(OwnerWorldSnapshot snapshot) => StoredStockPiles(snapshot, terrainMap!).ToArray();
        StockPaint[] Frame() => ((List<StockPaint>)typeof(StoredStockLayer).GetField("drawnFrame",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storedStockLayer)!).ToArray();
        async Task Settle()
        {
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        RenderMap(occupied);
        cameraZoom = maximumCameraZoom;
        cameraCenterTiles = new(16, 11);
        RenderMap(occupied);
        await Settle();
        var piles = Piles(occupied);
        if (piles.Length != 3 || piles.Select(pile => pile.Tile).Distinct().Count() != 3 ||
            piles.Any(pile => pile.Level != 2 || occupied.PlacedBuildings.Any(building =>
                new Rect2I(building.Position.X, building.Position.Y, building.Width, building.Height).HasPoint(pile.Tile)) ||
                pile.Tile == new Vector2I(10, 11) || pile.Tile == new Vector2I(11, 11) ||
                !land.Any(tile => tile.X == pile.Tile.X && tile.Y == pile.Tile.Y)) || Frame().Length == 0 ||
            storedStockLayer.MouseFilter != Control.MouseFilterEnum.Ignore || storedStockLayer.GetChildCount() != 0)
            throw new InvalidOperationException("Real stored kinds must produce separate full piles on clear Town ground, never the Road or doorstep; empty stores emit none.");
        foreach (var (itemKind, pileKind) in new[]
        {
            ("wood", StockPileKind.Logs), ("grain", StockPileKind.Sacks), ("stone", StockPileKind.Crates)
        })
        {
            var singleKind = occupied with
            {
                PlacedBuildings = [house with { StoredItems = [new(itemKind, 80)] }]
            };
            var singlePile = Piles(singleKind);
            if (singlePile.Length != 1 || singlePile[0].Kind != pileKind)
                throw new InvalidOperationException($"Accepted {itemKind} stock must draw as {pileKind}.");
        }
        foreach (var paint in Frame())
            if (!piles.Any(pile => new Rect2((Vector2)pile.Tile * terrainLayer.Stride, Vector2.One * terrainLayer.Stride)
                .Grow(0.01f).Encloses(paint.Area)))
                throw new InvalidOperationException("Every stock pixel and shadow must stay within its admitted tile.");
        var partial = occupied with
        {
            PlacedBuildings = [house with
            { StoredItems = [new("wood", 39), new("grain", 35), new("stone", 5)], StoredQuantity = 79 }]
        };
        RenderMap(partial);
        await Settle();
        if (Piles(partial).Length != 3 || Piles(partial).Any(pile => pile.Level != 1))
            throw new InvalidOperationException("Piles must shrink when accepted stock drops below the full drawing's capacity band.");
        cameraZoom = 1;
        RenderMap(partial);
        await Settle();
        if (BuildingSprites.AtlasTileSize(terrainLayer.TileSize) != 16 || Frame().Length == 0)
            throw new InvalidOperationException("Stored-stock piles must also use their approved mid-zoom drawing.");
        var empty = occupied with { PlacedBuildings = [house with { StoredItems = [], StoredQuantity = 0 }] };
        RenderMap(empty);
        await Settle();
        if (Frame().Length != 0) throw new InvalidOperationException("An emptied store must remove its drawn piles.");
        var blocked = occupied with { RoadTiles = [new(10, 10), new(12, 10), new(10, 11), new(12, 11)] };
        RenderMap(blocked);
        await Settle();
        if (Frame().Length != 0) throw new InvalidOperationException("Stock must stay hidden when every beside-door tile is occupied by Roads.");
        var buildingBlocked = occupied with
        {
            PlacedBuildings = [house,
                house with { InstanceId = "west-store", Position = new(10, 10), Height = 2,
                    StoredItems = [], StoredQuantity = 0, Entrance = new(10, 12) },
                house with { InstanceId = "east-store", Position = new(12, 10), Height = 2,
                    StoredItems = [], StoredQuantity = 0, Entrance = new(12, 12) }]
        };
        if (Piles(buildingBlocked).Length != 0)
            throw new InvalidOperationException("Stock must stay hidden when adjacent building footprints occupy every beside-door tile.");
        if (Piles(occupied with { Towns = [] }).Length != 0 ||
            Piles(occupied with { PlacedBuildings = [house with { StoredItems = null }] }).Length != 0)
            throw new InvalidOperationException("Stock without accepted Town ground or known stored kinds must not invent piles.");
        var edge = occupied with { Towns = [occupied.Towns[0] with { BorderTiles = [house.Position, house.Entrance!] }] };
        if (Piles(edge).Length != 0) throw new InvalidOperationException("Piles must not spill outside a Town with no free claimed ground beside its door.");
        var wetBytes = new byte[256 * 128];
        foreach (var tile in new[] { new Vector2I(10, 10), new Vector2I(12, 10), new Vector2I(10, 11), new Vector2I(12, 11) })
            wetBytes[tile.Y * 256 + tile.X] = 1;
        var wet = occupied with { PackedTerrain = occupied.PackedTerrain! with { Data = Convert.ToBase64String(wetBytes) } };
        var wetMap = WorldTerrainMap.FromPacked(wet.PackedTerrain!);
        if (StoredStockPiles(wet, wetMap).Count != 0)
            throw new InvalidOperationException("Recorded stock must never put piles on water beside a store.");
        var unlimited = occupied with { PlacedBuildings = [house with { StorageCapacity = null }] };
        if (Piles(unlimited).Length != 3 || Piles(unlimited).Any(pile => pile.Level != 1))
            throw new InvalidOperationException("Unknown capacity must show known contents without inventing a fullness limit.");
        cameraZoom = maximumCameraZoom;
        cameraCenterTiles = new(150, 80);
        RenderMap(occupied);
        await Settle();
        if (Frame().Length != 0) throw new InvalidOperationException("Offscreen stock must submit no draw commands.");
        var east = terrainMap!.Width - 1;
        var wrapped = occupied with
        {
            WorldId = occupied.WorldId + ":wrap",
            WrapsEastWest = true,
            RoadTiles = [],
            PlacedBuildings = [house with { Position = new(east, 10), Entrance = new(0, 10) }],
            Towns = [occupied.Towns[0] with { BorderTiles = Enumerable.Range(8, 5)
                .SelectMany(y => new[] { east, 0, 1 }.Select(x => new OwnerWorldPosition(x, y))).ToArray() }],
        };
        RenderMap(wrapped);
        cameraZoom = maximumCameraZoom;
        cameraCenterTiles = new(0, 10);
        RenderMap(wrapped);
        await Settle();
        if (Frame().Length == 0 || Frame().Any(paint => Math.Abs(paint.Area.Position.X) > 3 * terrainLayer.Stride))
            throw new InvalidOperationException("Stored piles must use the actual wrapped entrance and draw its visible seam copy.");
        RenderMap(map);
        cameraZoom = zoom;
        cameraCenterTiles = center;
        RenderMap(map);
    }
}
