using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyRoofSnowAsync()
    {
        var previous = renderedMapSnapshot!;
        var accepted = observationSession.Current;
        var zoom = cameraZoom;
        var center = cameraCenterTiles;
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        var kinds = new (BuildingKind Kind, string Tag, int Width, int Height)[]
        {
            (BuildingKind.House, "house", 1, 1), (BuildingKind.House, "house", 1, 2),
            (BuildingKind.House, "house", 2, 1), (BuildingKind.House, "house", 2, 2),
            (BuildingKind.Warehouse, "warehouse", 2, 3), (BuildingKind.Blacksmith, "blacksmith", 2, 2),
            (BuildingKind.Farmhouse, "farmhouse", 1, 2), (BuildingKind.Silo, "silo", 1, 1),
            (BuildingKind.TailorShop, "tailor", 1, 1), (BuildingKind.Store, "store", 2, 2),
            (BuildingKind.Workshop, "workshop", 2, 2), (BuildingKind.Clinic, "clinic", 2, 2),
            (BuildingKind.Restaurant, "restaurant", 2, 3), (BuildingKind.TownHall, "town_hall", 3, 4),
            (BuildingKind.Market, "market", 3, 3), (BuildingKind.MarketStall, "market_stall", 1, 1),
            (BuildingKind.Port, "port", 2, 4), (BuildingKind.Generic, "unknown", 2, 2),
        };
        var map = new OwnerWorldSnapshot("roof-snow-smoke", 0, "roof-snow-map", [], [], [], null, 0)
        {
            PackedTerrain = new(64, 64, "terrain-kind-v1", Convert.ToBase64String(new byte[64 * 64])),
            WeatherRegionSize = 32,
            WeatherRegions = [new(0, 0, "snow"), new(1, 0, "clear")],
            CalendarPace = new(1440, 100),
            Authoring = new(false, 0, 0, 0, "roof-snow-map", "roof-snow-map", "snow", "winter", []),
            PlacedBuildings = [new("snow-roof", "test/house", new(14, 12), 0, "House", ["house"], 2, 2)],
        };
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        async Task Show()
        {
            observationSession.ResetAfterLoad();
            Check(observationSession.TryAccept(new(handshake, new(map, new(map.WorldTick, 0, []))), 0, out _),
                "The actual roof-snow observation must be accepted.");
            Render(map, []);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        try
        {
            await Show();
            Check(terrainLayer.SnowCoverAt(14, 12) == 0 && terrainLayer.DrawnSnowRoofCount == 0,
                "A fresh view must not invent snow on a roof.");
            cameraZoom = 3;
            cameraCenterTiles = new(16, 14);
            map = map with { WorldTick = 30 }; await Show();
            Check(Mathf.IsEqualApprox(terrainLayer.SnowCoverAt(14, 12), 0.5f) && terrainLayer.DrawnSnowRoofCount == 1 &&
                terrainLayer.GroundSnowAt(14, 12) == 0 && Mathf.IsEqualApprox(terrainLayer.GroundSnowAt(13, 12), 0.5f),
                "The actual roof draw must share observed buildup while its footprint stays out of ground snow.");
            map = map with { WorldTick = 60 }; await Show();
            foreach (var neglect in new[] { BuildingNeglect.Neglected, BuildingNeglect.FallingApart })
            {
                using var neglected = BuildingSprites.Render(BuildingKind.House, 1, 1, 16, BuildingDoor.Default, neglect);
                using var snow = BuildingSprites.SnowOverlay(BuildingKind.House, 1, 1, 16, BuildingDoor.Default, neglect, 7);
                var covered = 0;
                for (var y = 0; y < 16; y++)
                    for (var x = 0; x < 16; x++)
                    {
                        var color = neglected.GetPixel(x, y);
                        if (color.A < 0.99f || color.R * 0.3f + color.G * 0.59f + color.B * 0.11f < 0.2f)
                            Check(snow.GetPixel(x, y).A == 0, "Mid-zoom snow must stay registered to the actual neglected roof gaps.");
                        if (snow.GetPixel(x, y).A > 0) covered++;
                    }
                Check(covered > 0, "Intact parts of neglected roofs must retain snow at mid zoom.");
            }
            foreach (var size in new[] { 32, 16 })
            {
                using var silo = BuildingSprites.SnowOverlay(BuildingKind.Silo, 1, 1, size, BuildingDoor.Default, BuildingNeglect.None, 7);
                Check(silo.GetPixel(size == 32 ? 9 : 4, size == 32 ? 5 : 3).A == 0,
                    "The actual Silo's circular outline must remain clear of snow.");
                Check(silo.GetPixel(size == 32 ? 11 : 5, size == 32 ? 7 : 4).A > 0,
                    "The Silo's roof interior must keep snow next to its clear circular rim.");
            }
            foreach (var side in new[] { DoorSide.North, DoorSide.East })
            {
                using var awning = BuildingSprites.SnowOverlay(BuildingKind.MarketStall, 1, 1, 32, new(side), BuildingNeglect.None, 7);
                var covered = 0;
                for (var y = 0; y < 32; y++)
                    for (var x = 0; x < 32; x++)
                        if (awning.GetPixel(x, y).A > 0)
                        {
                            Check(Math.Abs(awning.GetPixel(x, y).A - 0.74f) < 1f / 255,
                                "A shaded single-face awning must not invent sunny hipped-roof sectors.");
                            covered++;
                        }
                Check(covered > 0, "Shaded single-face awnings must retain snow.");
            }
            using (var awning = BuildingSprites.SnowOverlay(BuildingKind.MarketStall, 1, 1, 32, BuildingDoor.Default, BuildingNeglect.None, 7))
            {
                Check(Math.Abs(awning.GetPixel(16, 5).A - 0.64f) < 1f / 255 && awning.GetPixel(16, 16).A <= 0.48f,
                    "A south-facing awning must melt from its actual eave across its full single slope.");
            }
            using (var awning = BuildingSprites.SnowOverlay(BuildingKind.MarketStall, 1, 1, 32, new(DoorSide.West), BuildingNeglect.None, 7))
            {
                Check(Math.Abs(awning.GetPixel(26, 16).A - 0.64f) < 1f / 255 && awning.GetPixel(15, 16).A <= 0.48f,
                    "A west-facing awning must keep its ridge covered and melt toward its western eave.");
            }
            using (var arcade = BuildingSprites.SnowOverlay(BuildingKind.Market, 3, 3, 32, BuildingDoor.Default, BuildingNeglect.None, 7))
            {
                Check(Math.Abs(arcade.GetPixel(20, 80).A - 0.64f) < 1f / 255 && arcade.GetPixel(20, 87).A <= 0.48f,
                    "The actual Market arcade must follow its single slope beneath the hall.");
            }
            foreach (var (kind, tag, width, height) in kinds)
            {
                foreach (var size in new[] { 32, 16 })
                {
                    using var original = BuildingSprites.Render(kind, width, height, size);
                    using var snow = BuildingSprites.SnowOverlay(kind, width, height, size, BuildingDoor.Default, BuildingNeglect.None, 7);
                    var pixels = 0;
                    for (var y = 0; y < snow.GetHeight(); y++)
                        for (var x = 0; x < snow.GetWidth(); x++)
                        {
                            var alpha = snow.GetPixel(x, y).A;
                            if (alpha <= 0) continue;
                            pixels++;
                            Check(original.GetPixel(x, y).A >= 0.99f,
                                $"{kind} {size}px snow must not cover transparent grass shadows.");
                        }
                    Check(pixels > 0, $"{kind} {width}x{height} must retain roof snow at {size}px.");
                    if (kind == BuildingKind.House && width == 1 && height == 2)
                        Check(snow.GetPixel(size == 32 ? 21 : 10, size == 32 ? 15 : 8).A == 0,
                            "The actual chimney's lit cap and flue must stay clear.");
                }
                map = map with { PlacedBuildings = [new("snow-roof", "test/" + tag, new(14, 12), 0, tag, [tag], width, height)] };
                await Show();
                Check(terrainLayer.DrawnSnowRoofCount == 1, $"The native terrain renderer must draw the {kind} roof.");
            }
            var cache = terrainLayer.RoofSnowTextureCount;
            await Show();
            Check(terrainLayer.RoofSnowTextureCount == cache && cache <= 256 && terrainLayer.GetChildCount() == 0,
                "Repeated observations must reuse bounded snow textures without adding roof nodes.");
            map = map with { Authoring = map.Authoring! with { IsPaused = true } }; await Show();
            Check(terrainLayer.SnowCoverAt(14, 12) == 1 && terrainLayer.DrawnSnowRoofCount == 1,
                "Paused native frames must keep the same roof cover and cache.");
            map = map with { Authoring = map.Authoring! with { IsPaused = false } }; await Show();
            cameraZoom = 1; await Show();
            Check(currentTileSize is >= 12 and < 24 && terrainLayer.DrawnSnowRoofCount == 1,
                "The actual mid-zoom map must draw the 16px roof overlay.");
            cameraZoom = 3; await Show();
            var bounds = (0, 0, 31, 31);
            Check(Mathf.IsEqualApprox(RoofSnowSprites.SlopeCover(30, 1, 32, RoofSnowShape.GableNorthSouth, bounds, 7), 0.74f) &&
                RoofSnowSprites.SlopeCover(1, 30, 32, RoofSnowShape.GableNorthSouth, bounds, 7) <= 0.48f &&
                Mathf.IsEqualApprox(RoofSnowSprites.SlopeCover(1, 1, 32, RoofSnowShape.GableEastWest, bounds, 7), 0.74f) &&
                RoofSnowSprites.SlopeCover(30, 30, 32, RoofSnowShape.GableEastWest, bounds, 7) <= 0.48f,
                "A north-south ridge must split east/west; shaded slopes keep cover and sunny eaves keep a dusting.");
            map = map with { PlacedBuildings = [new("snow-roof", "test/house", new(35, 12), 0, "House", ["house"], 2, 2)] };
            cameraCenterTiles = new(36, 14); await Show();
            Check(terrainLayer.DrawnSnowRoofCount == 0 && terrainLayer.SnowCoverAt(35, 12) == 0,
                "A dry neighbouring region must not borrow roof snow.");
            map = map with { PlacedBuildings = [new("snow-roof", "test/house", new(31, 12), 0, "House", ["house"], 2, 2)] };
            cameraCenterTiles = new(32, 14); await Show();
            Check(terrainLayer.DrawnSnowRoofCount == 1 && terrainLayer.DrawnSnowRoofRegionCount == 1 &&
                Mathf.IsEqualApprox(terrainLayer.LastSnowRoofRegion.End.X, 32 * (currentTileSize + TileGap)) &&
                terrainLayer.SnowCoverAt(31, 12) == 1 && terrainLayer.SnowCoverAt(32, 12) == 0,
                "A roof crossing the region edge must use the snow record on each side.");
            map = map with { WeatherRegions = [new(0, 0, "clear")], WorldTick = 60 }; await Show();
            map = map with { WorldTick = 120 }; await Show();
            Check(Mathf.IsEqualApprox(terrainLayer.SnowCoverAt(31, 12), 0.5f) && terrainLayer.DrawnSnowRoofCount == 1,
                "Roof cover must melt with ground cover after snowfall stops.");
            map = map with { WorldTick = 180 }; await Show();
            Check(terrainLayer.SnowCoverAt(31, 12) == 0 && terrainLayer.DrawnSnowRoofCount == 0,
                "After melting, the original roof must show again.");
            map = map with { WorldTick = 200, WeatherRegions = [new(0, 0, "snow")] }; await Show();
            map = map with { WorldTick = 260 }; await Show();
            Check(terrainLayer.DrawnSnowRoofCount == 1, "Observed snowfall must rebuild roof cover.");
            map = map with { WorldTick = 5 }; await Show();
            Check(terrainLayer.DrawnSnowRoofCount == 0, "Rewind must clear roof snow along with the observed history.");
            map = map with { WorldTick = 65 }; await Show();
            map = map with { WorldId = "roof-snow-reloaded" }; await Show();
            Check(terrainLayer.DrawnSnowRoofCount == 0 && terrainLayer.SnowCoverAt(31, 12) == 0,
                "Opening another world must not replay earlier snowfall on its roofs.");
            GD.Print("NATIVE_ROOF_SNOW kinds=15 houseFootprints=4 atlas=32,16 historyBuildupPauseMidZoomDryClippedEdgeMeltRewindReloadCache=passed");
        }
        finally
        {
            cameraZoom = zoom; cameraCenterTiles = center;
            observationSession.ResetAfterLoad();
            if (accepted is not null) observationSession.TryAccept(accepted, 0, out _);
            Render(previous, []);
        }
    }
}
