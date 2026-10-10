using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyBuildingCompletionAsync()
    {
        var previous = renderedMapSnapshot!;
        var previousObservation = observationSession.Current;
        var zoomBefore = cameraZoom;
        var centerBefore = cameraCenterTiles;
        var processingBefore = buildingCompletionLayer.IsProcessing();
        buildingCompletionLayer.SetProcess(false);
        var map = new OwnerWorldSnapshot("completion-smoke", 100, "completion-map", [], [], [], null, 0)
        {
            PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(new byte[256 * 128])),
            CalendarPace = new(1440, 120, 30, 30, 30, 30),
            Authoring = new(false, 0, 0, 0, "completion-map", "completion-map", "clear", "spring", []),
        };
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        OwnerWorldConstructionSite Site(int x, int y) => new("site", "house", "House", ["house"], new(x, y), 2, 1,
            null, 29, 30, "building", null, null);
        OwnerWorldPlacedBuilding Finished(string id, int x, int y, long tick) =>
            new(id, "house", new(x, y), tick, "House", ["house"], 2, 1);
        void Show()
        {
            observationSession.ResetAfterLoad();
            if (!observationSession.TryAccept(new(handshake, new(map, new(map.WorldTick, 0, []))), 0, out var failure))
                throw new InvalidOperationException("Completion UI observation fixture was refused: " + failure);
            Render(map, []);
            buildingCompletionLayer._Process(0);
        }
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        async Task Draw() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            map = map with { PlacedBuildings = [Finished("old", 119, 60, 90)] }; Show();
            cameraZoom = 32 / 12f; cameraCenterTiles = new(120, 60); Show(); await Draw();
            Check(buildingCompletionLayer.ActiveCount == 0 && buildingCompletionLayer.DrawnSpanCount == 0,
                "Loading existing completed buildings must not play completion moments.");
            map = map with { WorldTick = 101, ConstructionSites = [Site(121, 60)] }; Show();
            map = map with { WorldTick = 102, ConstructionSites = [], PlacedBuildings = [.. map.PlacedBuildings, Finished("new", 121, 60, 102)] }; Show();
            buildingCompletionLayer._Process(0.9); await Draw();
            Check(buildingCompletionLayer.ActiveCount == 1 && buildingCompletionLayer.DrawnMomentCount == 1 &&
                buildingCompletionLayer.DrawnSpanCount > 30 && buildingCompletionLayer.GetChildCount() == 0 &&
                buildingCompletionLayer.MouseFilter == Control.MouseFilterEnum.Ignore,
                "A real observed construction transition must draw the approved dust and twinkles once, without per-particle nodes.");
            var age = buildingCompletionLayer.FirstAge;
            Show(); map = map with { WorldTick = 103 }; Show();
            Check(buildingCompletionLayer.ActiveCount == 1 && buildingCompletionLayer.FirstAge == age,
                "Repeated snapshots and later ticks must not replay an already completed building.");
            map = map with { Authoring = map.Authoring! with { IsPaused = true } }; Show(); buildingCompletionLayer._Process(10); await Draw();
            Check(buildingCompletionLayer.FirstAge == age && buildingCompletionLayer.DrawnMomentCount == 1,
                "Pause must hold the exact completion frame.");
            cameraZoom = 16 / 12f; Show(); await Draw();
            Check(terrainLayer.TileSize >= WorldTerrainLayer.SpriteTileMinimum && buildingCompletionLayer.DrawnMomentCount == 1,
                "The moment must remain visible at mid zoom while paused.");
            cameraZoom = minimumCameraZoom; Show(); await Draw();
            Check(buildingCompletionLayer.ActiveCount == 1 && buildingCompletionLayer.DrawnMomentCount == 0,
                "Overview must hide the effect without replaying or discarding its frozen clock.");
            cameraZoom = 32 / 12f; map = map with { Authoring = map.Authoring! with { IsPaused = false } }; Show();
            buildingCompletionLayer._Process(2.2); await Draw();
            Check(buildingCompletionLayer.ActiveCount == 0 && buildingCompletionLayer.DrawnSpanCount == 0,
                "Resume must finish the three-second moment and leave no particles or sparkle drawing.");
            // A cancelled site, old completion, authoring insertion and off-screen completion are all quiet.
            map = map with { WorldTick = 104, ConstructionSites = [Site(121, 62)] }; Show();
            map = map with { WorldTick = 105, ConstructionSites = [] }; Show();
            Check(buildingCompletionLayer.ActiveCount == 0, "Cancelling construction must not create a finish effect.");
            map = map with { WorldTick = 106, ConstructionSites = [Site(121, 62)] }; Show();
            map = map with { WorldTick = 107, ConstructionSites = [], PlacedBuildings = [.. map.PlacedBuildings, Finished("stale", 121, 62, 90)] }; Show();
            Check(buildingCompletionLayer.ActiveCount == 0, "A building with an old placement time must stay quiet.");
            map = map with { WorldTick = 108, ConstructionSites = [Site(121, 64)], Authoring = map.Authoring! with { IsPaused = true } }; Show();
            map = map with { ConstructionSites = [], PlacedBuildings = [.. map.PlacedBuildings, Finished("authored", 121, 64, 108)] }; Show();
            map = map with { WorldTick = 109, Authoring = map.Authoring! with { IsPaused = false } }; Show();
            Check(buildingCompletionLayer.ActiveCount == 0, "Paused authoring must not become a queued completion on resume.");
            map = map with { WorldTick = 110, ConstructionSites = [Site(220, 100)] }; Show();
            map = map with { WorldTick = 111, ConstructionSites = [], PlacedBuildings = [.. map.PlacedBuildings, Finished("hidden", 220, 100, 111)] }; Show();
            cameraCenterTiles = new(221, 100); Show(); await Draw();
            Check(buildingCompletionLayer.ActiveCount == 0, "Panning to an off-screen completion later must not replay it.");
            // Wrapped copies follow the actual camera at the seam, rather than the canonical footprint alone.
            map = map with { WrapsEastWest = true, WorldTick = 112, ConstructionSites = [Site(255, 60)] };
            cameraCenterTiles = new(0, 60); Show();
            map = map with { WorldTick = 113, ConstructionSites = [], PlacedBuildings = [.. map.PlacedBuildings, Finished("seam", 255, 60, 113)] }; Show();
            buildingCompletionLayer._Process(0.9); await Draw(); await Draw();
            Check(buildingCompletionLayer.ActiveCount == 1 && buildingCompletionLayer.DrawnMomentCount == 1,
                "A completion across the east/west seam must draw on its visible wrapped copy.");
            var layers = mapStage.GetChildren();
            Check(layers.IndexOf(buildingCompletionLayer) > layers.IndexOf(nightLightsLayer) &&
                layers.IndexOf(buildingCompletionLayer) < layers.IndexOf(objectLayer) &&
                layers.IndexOf(buildingCompletionLayer) < layers.IndexOf(weatherLayer),
                "Moments must sit over roofs and light, below map objects, figures and weather.");
            map = map with { PlacedBuildings = map.PlacedBuildings.Where(building => building.InstanceId != "seam").ToArray() }; Show();
            Check(buildingCompletionLayer.ActiveCount == 0, "Removing a building must remove its active moment.");
            map = map with { WorldTick = 114, ConstructionSites = [Site(255, 60)] }; Show();
            map = map with { WorldTick = 115, ConstructionSites = [], PlacedBuildings = [.. map.PlacedBuildings, Finished("seam-again", 255, 60, 115)] }; Show();
            Check(buildingCompletionLayer.ActiveCount == 1, "Rebuilding after actual construction must create its own single moment.");
            map = map with { WorldTick = 100 }; Show();
            Check(buildingCompletionLayer.ActiveCount == 0, "Rewind must clear old completion clocks immediately.");
            map = map with { WorldTick = 116, ConstructionSites = [Site(255, 62)] }; Show();
            map = map with { WorldTick = 200, ConstructionSites = [], PlacedBuildings = [.. map.PlacedBuildings, Finished("gap", 255, 62, 200)] }; Show();
            Check(buildingCompletionLayer.ActiveCount == 0, "A long reconnect gap must not replay unobserved construction.");
            map = map with { WorldTick = 201, ConstructionSites = [Site(255, 60)], PlacedBuildings = [] }; Show();
            map = map with { WorldTick = 202, ConstructionSites = [], PlacedBuildings = [Finished("reload-reset", 255, 60, 202)] }; Show();
            Check(buildingCompletionLayer.ActiveCount == 1, "Reload reset must start with an actual active completion moment.");
            ResetDisplayedWorldContext();
            Check(buildingCompletionLayer.ActiveCount == 0, "Reload and timeline reset must immediately clear the active completion clock.");
            Show();
            Check(buildingCompletionLayer.ActiveCount == 0, "Reload and timeline reset must establish a quiet baseline.");
            map = map with { WorldTick = 203, ConstructionSites = [Site(255, 62)], PlacedBuildings = [] }; Show();
            map = map with { WorldTick = 204, ConstructionSites = [], PlacedBuildings = [Finished("world-reset", 255, 62, 204)] }; Show();
            Check(buildingCompletionLayer.ActiveCount == 1, "World switch must start with an actual active completion moment.");
            map = map with { WorldId = "completion-other-world" }; Show();
            Check(buildingCompletionLayer.ActiveCount == 0, "Switching worlds must establish a quiet baseline.");
        }
        finally
        {
            buildingCompletionLayer.SetProcess(processingBefore);
            cameraZoom = zoomBefore; cameraCenterTiles = centerBefore;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null) observationSession.TryAccept(previousObservation, 0, out _);
            Render(previous, []);
        }
    }
}
