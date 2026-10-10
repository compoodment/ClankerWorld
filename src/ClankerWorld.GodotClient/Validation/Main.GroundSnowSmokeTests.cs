using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyGroundSnowAsync()
    {
        var previous = renderedMapSnapshot!;
        var previousObservation = observationSession.Current;
        var zoomBefore = cameraZoom;
        var centerBefore = cameraCenterTiles;
        var person = PanelSmokeAgent("snow-agent", "Rowan", new(120, 60));
        var cow = new OwnerWorldAnimal("snow-cow", "Moss", "cow", "female", 9, "adult", new(120, 62),
            null, null, "wild", null, 0, null, null, null, null, false, [], []);
        var terrain = new byte[256 * 128];
        terrain[32 * 256 + 112] = 9; // Existing permanent snow.
        terrain[32 * 256 + 113] = 1; // Water must never receive ground snow.
        var map = new OwnerWorldSnapshot("ground-snow-smoke", 0, "ground-snow-map", [], [], [], null, 0)
        {
            PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(terrain)),
            Inhabitants = [person],
            Animals = [cow],
            WeatherRegionSize = 32,
            WeatherRegions = [new(3, 1, "snow"), new(4, 1, "clear")],
            RoadTiles = [new(118, 60), new(119, 60), new(120, 60)],
            PlacedBuildings = [new("snow-house", "test/house", new(114, 32), 0, "House", ["house"], 2, 2)],
            Authoring = new(false, 0, 0, 0, "ground-snow-map", "ground-snow-map", "snow", "winter", []),
        };
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        void Show()
        {
            observationSession.ResetAfterLoad();
            if (!observationSession.TryAccept(new(handshake, new(map, new(map.WorldTick, 0, []))), 0, out var failure))
                throw new InvalidOperationException("Snow UI observation fixture was refused: " + failure);
            Render(map, []);
        }
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        try
        {
            Show();
            Check(terrainLayer.GroundSnowAt(120, 60) == 0, "A fresh view must not invent earlier snow cover.");
            map = map with { WorldTick = 30 }; Show();
            Check(Mathf.IsEqualApprox(terrainLayer.GroundSnowAt(120, 60), 0.5f), "Observed snowfall must build up with world time.");
            map = map with { WorldTick = 60 }; Show();
            Check(terrainLayer.GroundSnowAt(120, 60) == 1 && terrainLayer.GroundSnowAt(150, 60) == 0 &&
                terrainLayer.GroundSnowAt(112, 32) == 0 && terrainLayer.GroundSnowAt(113, 32) == 0 &&
                terrainLayer.GroundSnowAt(114, 32) == 0,
                "Only the region that snowed may whiten; permanent snow, water and buildings keep their original art.");
            cameraCenterTiles = new(120, 60); cameraZoom = 3; Show();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(terrainLayer.DrawnSnowTileCount > 0 && terrainLayer.GetChildCount() == 0,
                "The real terrain draw must show cover without a node per tile.");
            person = person with { Position = new(121, 60) };
            map = map with { WorldTick = 61, Inhabitants = [person] }; Show();
            var agentPrints = terrainLayer.SnowFootprintCount;
            Check(agentPrints > 0, "A short authoritative agent move over snow must leave prints.");
            cow = cow with { Position = new(121, 62) };
            map = map with { WorldTick = 62, Animals = [cow] }; Show();
            Check(terrainLayer.SnowFootprintCount > agentPrints, "A moving animal must leave prints too.");
            var printCount = terrainLayer.SnowFootprintCount;
            Show();
            Check(terrainLayer.SnowFootprintCount == printCount, "A repeated observation or redraw must not make more prints.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(terrainLayer.DrawnSnowFootprintCount > 0, "Native terrain drawing must actually submit the snowy footprints.");
            map = map with { Authoring = map.Authoring! with { IsPaused = true } }; Show();
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(terrainLayer.SnowFootprintCount == printCount && terrainLayer.GroundSnowAt(120, 60) == 1,
                "Paused frames must neither melt snow nor age footprints.");
            person = person with { Position = new(122, 60) };
            map = map with { Inhabitants = [person] }; Show();
            Check(terrainLayer.SnowFootprintCount == printCount, "Paused authoring relocation is not a walk.");
            map = map with
            {
                Authoring = map.Authoring! with { IsPaused = false },
                WorldTick = 63,
                Inhabitants = [person with { Position = new(150, 60) }]
            }; Show();
            Check(terrainLayer.SnowFootprintCount == printCount, "A long jump must not draw a made-up trail.");
            map = map with { WorldTick = 64, Inhabitants = [person with { Position = new(151, 60) }] }; Show();
            Check(terrainLayer.SnowFootprintCount == printCount, "Walking in a dry region must leave no snow prints.");
            cameraZoom = minimumCameraZoom; Show();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(terrainLayer.DrawnSnowTileCount > 0 && terrainLayer.DrawnSnowFootprintCount == 0 &&
                terrainLayer.SnowFootprintCount == printCount, "Overview shows cover and retains, but does not draw, tiny footprints.");
            Check(terrainLayer.SnowOverviewDrawCount > 0 && terrainLayer.SnowOverviewDrawCount * 4 < terrainLayer.DrawnSnowTileCount,
                "Overview cover must be batched into cached textures, rather than one draw command per snowy tile.");
            var snowyTiles = terrainLayer.DrawnSnowTileCount;
            var originalBuildings = map.PlacedBuildings;
            map = map with
            {
                PlacedBuildings = [.. originalBuildings,
                new("snow-new-house", "test/house", new(121, 60), 0, "House", ["house"], 1, 1)]
            }; Show();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(terrainLayer.DrawnSnowTileCount == snowyTiles - 1 && terrainLayer.GroundSnowAt(121, 60) == 0,
                "A new building must invalidate the cached overview snow mask even at the same world tick.");
            map = map with { PlacedBuildings = originalBuildings }; Show();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(terrainLayer.DrawnSnowTileCount == snowyTiles,
                "Removing a building must restore its ground snow in the actual overview drawing.");
            cameraZoom = 3; Show();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(terrainLayer.DrawnSnowFootprintCount > 0, "Zooming back in restores the same saved local prints.");
            map = map with { WorldTick = 122 }; Show();
            Check(terrainLayer.SnowFootprintCount == 0, "All prints must disappear after one game hour.");
            map = map with { WeatherRegions = [new(3, 1, "clear"), new(4, 1, "clear")] }; Show();
            Check(terrainLayer.GroundSnowAt(120, 60) == 1, "Snow must remain when falling flakes first stop.");
            map = map with { WorldTick = 182 }; Show();
            Check(Mathf.IsEqualApprox(terrainLayer.GroundSnowAt(120, 60), 0.5f), "Cover must melt gradually after snowfall ends.");
            map = map with { WorldTick = 242 }; Show();
            Check(terrainLayer.GroundSnowAt(120, 60) == 0 && terrainLayer.GroundSnowAt(150, 60) == 0,
                "Melted cover must leave the original ground; the dry region stays dry.");
            map = map with { WorldTick = 300, WeatherRegions = [new(4, 1, "snow")] }; Show();
            Check(terrainLayer.GroundSnowAt(150, 60) == 0, "A newly snowing region must not accumulate the preceding dry interval.");
            map = map with { WorldTick = 360 }; Show();
            Check(terrainLayer.GroundSnowAt(150, 60) == 1, "A second region builds only after observed snowfall there.");
            map = map with { WorldTick = 10 }; Show();
            Check(terrainLayer.GroundSnowAt(150, 60) == 0 && terrainLayer.SnowFootprintCount == 0,
                "A checkpoint rewind clears local snow and footprint history.");
            map = map with
            {
                WorldId = "ground-snow-wrap",
                WorldTick = 0,
                WrapsEastWest = true,
                WeatherRegions = [new(7, 1, "snow"), new(0, 1, "snow")],
                Inhabitants = [person with { Position = new(255, 60) }],
                Animals = []
            }; Show();
            map = map with { WorldTick = 60 }; Show();
            map = map with { WorldTick = 61, Inhabitants = [person with { Position = new(0, 60) }] }; Show();
            Check(terrainLayer.SnowFootprintCount is > 0 and < 12,
                "A wrapped one-tile move leaves a short trail rather than crossing the whole map.");
            Check(terrainLayer.GroundSnowAt(-1, 60) == terrainLayer.GroundSnowAt(255, 60),
                "Snow cover must use canonical wrapped tiles.");
            map = map with { WorldId = "ground-snow-new", WeatherRegions = [new(0, 1, "clear")] }; Show();
            Check(terrainLayer.GroundSnowAt(0, 60) == 0 && terrainLayer.SnowFootprintCount == 0,
                "Changing worlds clears both cover and trails.");
            foreach (var size in new[] { 32, 16 })
            {
                using var overlay = GroundSnowSprites.OverlayTile(TerrainStyle.Grass, 0, RoadLinks.Road | RoadLinks.East | RoadLinks.West, 0, size);
                Check(overlay.GetPixel(size / 2, size / 2).A > 0.7f &&
                    overlay.GetPixel(size / 2, size / 2).R < overlay.GetPixel(0, 0).R,
                    "The approved snowy Road stays greyer than its grassy edges at both atlas sizes.");
            }
            Check(GroundSnowSprites.PrintAlpha(0) > GroundSnowSprites.PrintAlpha(0.5f) && GroundSnowSprites.PrintAlpha(1) == 0,
                "Pressed prints fade smoothly, then become transparent.");
        }
        finally
        {
            cameraZoom = zoomBefore; cameraCenterTiles = centerBefore;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null) observationSession.TryAccept(previousObservation, 0, out _);
            Render(previous, []);
        }
    }
}
