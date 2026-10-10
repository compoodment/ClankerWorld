using ClankerWorld.GodotClient.UI;
using Godot;
using System.Reflection;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyGravesAsync(OwnerWorldSnapshot original)
    {
        var (zoom, center) = (cameraZoom, cameraCenterTiles);
        // A deceased owner observation has an archived position and explicit death tick.
        var dead = new OwnerWorldInhabitant("grave-a", "Past Resident", "dead", new(10, 10), 0,
            [], [new("death-tick", "100")], new("deceased", null, null, [], string.Empty),
            new(new(10, 10), [new(10, 10)], [new(10, 10)]), false);
        var snapshot = original with
        {
            WorldId = original.WorldId + ":graves",
            PackedTerrain = original.PackedTerrain! with { Data = Convert.ToBase64String(new byte[256 * 128]) },
            CalendarPace = new(10, 40),
            WorldTick = 150,
            Inhabitants = [dead, dead with { Id = "grave-b", Position = new(12, 10) }],
            Resources = [],
            Objects = [],
            Fields = [],
            GroundStocks = [],
            ConstructionSites = [],
            Bridges = [],
            PlacedBuildings = [],
            Towns = [],
            RoadTiles = [],
            TownLandTitles = [],
            ProductionJobs = [],
        };
        GraveMarker[] Markers(OwnerWorldSnapshot map) => GraveMarkers(map, terrainMap!).ToArray();
        GraveDraw[] Frame() => ((List<GraveDraw>)typeof(GraveLayer).GetField("drawnFrame",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graveLayer)!).ToArray();
        async Task Settle()
        {
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        try
        {
            RenderMap(snapshot);
            cameraCenterTiles = new(11, 10);
            cameraZoom = maximumCameraZoom;
            RenderMap(snapshot);
            await Settle();
            var markers = Markers(snapshot);
            if (markers.Length != 2 || markers.Select(marker => marker.Headstone).Distinct().Count() != 2 ||
                markers.Any(marker => marker.Opacity != 1) || Frame().Length != 2 || Frame().Any(draw => draw.AtlasSize != 32) ||
                graveLayer.MouseFilter != Control.MouseFilterEnum.Ignore || graveLayer.GetChildCount() != 0)
                throw new InvalidOperationException("Saved dead positions must show both approved markers at close zoom without per-grave nodes or input interception.");
            var textures = (Dictionary<(bool, int), ImageTexture>)typeof(GraveLayer).GetField("textures",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graveLayer)!;
            void VerifyTextures(int atlas)
            {
                foreach (var headstone in new[] { false, true })
                {
                    // Independent approved preview hashes, not computed from the live painter.
                    var expected = (headstone, atlas) switch
                    {
                        (false, 16) => "1653666B2705735C26C120FE94D57492A2BF2521EAD10AF13375C43287338B76",
                        (true, 16) => "B0181D68B5ABD0E76D5141564EF86B9E4D929071422D0CB059B19B6BF7B3B45D",
                        (false, 32) => "A0A56109C9D0C5D37273771A29B51FCCDEA0794D7B74CA888F0A7FEF23564236",
                        _ => "74B4A36CDFF8ECF9E6098950CBA1A9FA5BFD5AAB1C0CAC80F95E696340C215E7",
                    };
                    var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(textures[(headstone, atlas)].GetImage().GetData()));
                    if (actual != expected)
                        throw new InvalidOperationException($"The real native grave texture must match approved {(headstone ? "B" : "A")} pixels at {atlas}: {actual} versus {expected}.");
                }
            }
            VerifyTextures(32);
            RenderMap(snapshot); // repeat the accepted archived-death observation
            await Settle();
            if (!Markers(snapshot).SequenceEqual(markers))
                throw new InvalidOperationException("Grave placement and SHA-256 marker selection must survive a repeated observation unchanged.");
            cameraZoom = 1;
            RenderMap(snapshot);
            await Settle();
            if (Frame().Length != 2 || Frame().Any(draw => draw.AtlasSize != 16))
                throw new InvalidOperationException("Mid zoom must use both approved nearest-sampled 16 px grave sprites.");
            VerifyTextures(16);
            RenderMap(snapshot with { WorldTick = 500 }); // exactly one 40-day year after death
            await Settle();
            if (Frame().Any(draw => draw.Opacity != 1))
                throw new InvalidOperationException("Graves must stay fully visible for the saved calendar's whole year.");
            RenderMap(snapshot with { WorldTick = 505 });
            await Settle();
            if (Frame().Length != 2 || Frame().Any(draw => Math.Abs(draw.Opacity - 0.5f) > 0.001f))
                throw new InvalidOperationException("After a saved calendar year, the actual grave draw must fade over one calendar day.");
            RenderMap(snapshot with { WorldTick = 510 });
            await Settle();
            if (Frame().Length != 0)
                throw new InvalidOperationException("Expired graves must be removed from the native draw list.");

            var terrain = new byte[256 * 128];
            terrain[9 * 256 + 10] = 1; // water to the north
            var blocked = snapshot with
            {
                PackedTerrain = snapshot.PackedTerrain! with { Data = Convert.ToBase64String(terrain) },
                MapManifestDigest = "blocked-grave-map",
                Authoring = snapshot.Authoring! with { CurrentMapManifestDigest = "blocked-grave-map" },
                Inhabitants = [dead],
                RoadTiles = [new(10, 10)],
                PlacedBuildings = [new("grave-house", "test/house", new(9, 10), 0, "House", ["house"], Entrance: new(9, 11))],
                Resources = [new("grave-tree", "wood", new(11, 10), false, "available", 1, TreeKind: "broadleaf")],
            };
            RenderMap(blocked);
            await Settle();
            if (Markers(blocked).Single().Tile != new Vector2I(10, 11))
                throw new InvalidOperationException("A grave on a Road must choose the nearest legal ground, skipping water, the building and its tree.");
            var bridgeApproach = snapshot with
            {
                Inhabitants = [dead],
                Bridges = [new("grave-traffic-bridge", "plank_span_1", "traffic", "east_west",
                    [new(10, 10), new(12, 10)], [new(11, 10)], 0)],
            };
            RenderMap(bridgeApproach);
            await Settle();
            if (Markers(bridgeApproach).Single().Tile != new Vector2I(10, 9))
                throw new InvalidOperationException("A grave must avoid a traffic bridge approach even when no Road tile marks its entrance.");
            var layerLength = 256 * 128;
            var hydrology = new byte[layerLength];
            var surface = new byte[layerLength];
            var elevation = Enumerable.Repeat((byte)100, layerLength).ToArray();
            hydrology[10 * 256 + 10] = 3; // river, despite the v1 packed meadow projection
            surface[9 * 256 + 10] = 4; // shallow water without hydrology
            elevation[10 * 256 + 9] = 250; // peak to the west
            var vegetation = new byte[layerLength];
            vegetation[10 * 256 + 9] = 2; // forest styling must not conceal peak elevation
            var layered = snapshot with
            {
                Inhabitants = [dead],
                RoadTiles = [new(11, 10)],
                MapLayersDigest = "grave-layered-map",
                PackedMapLayers = new(256, 128, "map-layers-v1",
                    Convert.ToBase64String(Enumerable.Repeat((byte)2, layerLength).ToArray()),
                    Convert.ToBase64String(elevation), Convert.ToBase64String(hydrology),
                    Convert.ToBase64String(surface), Convert.ToBase64String(vegetation)),
            };
            RenderMap(layered);
            await Settle();
            if (Markers(layered).Single().Tile != new Vector2I(10, 11))
                throw new InvalidOperationException("Grave ground must respect independent river, shallow-water and elevation layers over an older meadow projection.");
            var builtOver = snapshot with
            {
                Inhabitants = [dead],
                PlacedBuildings =
                [new("new-house", "test/house", new(10, 10), 151, "House", ["house"])]
            };
            RenderMap(builtOver);
            await Settle();
            if (Markers(builtOver).Single().Tile == new Vector2I(10, 10))
                throw new InvalidOperationException("Construction on a grave must relocate its derived marker off the occupied footprint.");
            RenderMap(snapshot with
            {
                Inhabitants = [dead with { Lifecycle = "active" }, dead with { Id = "draft-death", IsDraft = true },
                dead with { Id = "missing-time", DecisionFactors = [] }, dead with { Id = "future-death", DecisionFactors = [new("death-tick", "999")] }]
            });
            await Settle();
            if (Frame().Length != 0)
                throw new InvalidOperationException("Live/draft people and unknown/future deaths must never invent graves.");
            RenderMap(snapshot);
            cameraCenterTiles = new(210, 100);
            cameraZoom = maximumCameraZoom;
            RenderMap(snapshot);
            await Settle();
            if (Frame().Length != 0) throw new InvalidOperationException("Offscreen graves must not produce draw commands.");
            cameraCenterTiles = new(11, 10);
            cameraZoom = minimumCameraZoom;
            RenderMap(snapshot);
            await Settle();
            if (Frame().Length != 0) throw new InvalidOperationException("Overview zoom must omit individual grave sprites.");
            var wrapped = snapshot with
            {
                WorldId = snapshot.WorldId + ":wrapped",
                WrapsEastWest = true,
                Inhabitants = [dead with { Position = new(0, 10) }],
                RoadTiles = [new(0, 10), new(0, 9), new(1, 10), new(0, 11)]
            };
            RenderMap(wrapped);
            cameraCenterTiles = new(0, 10);
            cameraZoom = maximumCameraZoom;
            RenderMap(wrapped);
            await Settle();
            if (Markers(wrapped).Single().Tile != new Vector2I(255, 10) || Frame().Length == 0 ||
                Frame().All(draw => !terrainLayer.VisibleTiles.Intersects(new Rect2(draw.Area.Position / terrainLayer.Stride, Vector2.One))))
                throw new InvalidOperationException("Nearest grave ground and visible copies must cross the wrapped world seam.");
        }
        finally
        {
            RenderMap(original);
            cameraZoom = zoom;
            cameraCenterTiles = center;
            RenderMap(original);
        }
    }
}
