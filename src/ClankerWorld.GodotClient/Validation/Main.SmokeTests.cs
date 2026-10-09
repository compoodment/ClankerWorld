using ClankerWorld.GodotClient.UI;
using Godot;
using System.Reflection;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifySmokeAsync(OwnerWorldSnapshot map)
    {
        var (zoom, center, paused) = (cameraZoom, cameraCenterTiles, weatherLayer.Paused);
        var person = new OwnerWorldInhabitant("agent:smoke", "Ada", "active", new(11, 10), 8_000, [], [],
            new OwnerWorldRoute("idle", null, null, [], string.Empty),
            new OwnerWorldSpatialKnowledge(new(11, 10), [new(11, 10)], [new(11, 10)]), false);
        OwnerWorldPlacedBuilding Building(string id, string tag, int x, int y, int height = 1) =>
            new(id, $"test/{tag}", new(x, y), 0, id, [tag], 1, height, Entrance: new(x, y + height));
        var occupied = map with
        {
            Inhabitants = [person],
            Objects = [],
            PlacedBuildings = [Building("home", "house", 11, 10), Building("empty", "house", 13, 10),
                Building("forge", "blacksmith", 17, 9, 2), Building("idle", "blacksmith", 20, 9, 2),
                Building("store", "warehouse", 23, 10)],
            ProductionJobs = [new("forge-job", "test/axe", "forge", person.Id, 10, 90, "running"),
                new("empty-job", "test/axe", "empty", person.Id, 10, 90, "running"),
                new("store-job", "test/axe", "store", person.Id, 10, 90, "running")],
        };
        // Read the command buffer actually consumed by _Draw; no production test API is needed.
        SmokeSpan[] Frame() => ((List<SmokeSpan>)typeof(SmokeLayer).GetField("drawnFrame",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(smokeLayer)!).ToArray();
        async Task Settle()
        {
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        weatherLayer.Paused = true;
        cameraZoom = maximumCameraZoom;
        cameraCenterTiles = new(17, 10);
        RenderMap(occupied);
        await Settle();
        var columns = Frame();
        if (columns.Length == 0 || columns.Any(span => span.Color.A <= 0 || span.Color.A >= 1) ||
            columns.Any(span => span.Area.Position.X < 10 * terrainLayer.Stride || span.Area.End.X > 19 * terrainLayer.Stride) ||
            !columns.Any(span => span.Area.Position.X < 13 * terrainLayer.Stride) ||
            !columns.Any(span => span.Area.Position.X > 17 * terrainLayer.Stride) ||
            smokeLayer.MouseFilter != Control.MouseFilterEnum.Ignore || smokeLayer.GetChildCount() != 0 ||
            mapStage.GetChildren().IndexOf(smokeLayer) >= mapStage.GetChildren().IndexOf(weatherLayer))
            throw new InvalidOperationException("Only the occupied House and working forge may draw translucent chimney columns, below weather and without intercepting input.");
        await Settle();
        if (!columns.SequenceEqual(Frame())) throw new InvalidOperationException("Paused smoke must hold its exact drawn pixels.");
        // Mid zoom uses the actual 16px chimney rather than shrinking the close-zoom source.
        cameraZoom = 1;
        RenderMap(occupied);
        await Settle();
        if (BuildingSprites.AtlasTileSize(terrainLayer.TileSize) != 16 || Frame().Length == 0)
            throw new InvalidOperationException("Chimney smoke must also draw at mid zoom with its 16px atlas.");
        cameraZoom = maximumCameraZoom;
        RenderMap(occupied with { Inhabitants = [], ProductionJobs = [] });
        await Settle();
        if (Frame().Length != 0) throw new InvalidOperationException("Smoke must stop when the House empties and the forge stops.");
        foreach (var absent in new[] { person with { IsDraft = true }, person with { Lifecycle = "deceased" } })
        {
            RenderMap(occupied with { Inhabitants = [absent], ProductionJobs = [] });
            await Settle();
            if (Frame().Length != 0) throw new InvalidOperationException("Draft and deceased inhabitants must not make a House smoke.");
        }
        cameraCenterTiles = new(150, 80);
        RenderMap(occupied);
        await Settle();
        if (Frame().Length != 0) throw new InvalidOperationException("Offscreen chimneys must not generate draw commands.");
        var east = terrainMap!.Width - 1;
        var wrapped = occupied with
        {
            WorldId = occupied.WorldId + ":smoke-wrap",
            WrapsEastWest = true,
            Inhabitants = [person with { Position = new(east, 10) }],
            PlacedBuildings = [Building("seam", "house", east, 10)],
            ProductionJobs = [],
        };
        RenderMap(wrapped);
        cameraZoom = maximumCameraZoom;
        cameraCenterTiles = new(0, 10);
        RenderMap(wrapped);
        await Settle();
        if (Frame().Length == 0 || Frame().Any(span => span.Area.Position.X > 2 * terrainLayer.Stride))
            throw new InvalidOperationException("A wrapped House must smoke at its visible seam copy.");
        columns = Frame();
        weatherLayer.Paused = false;
        for (var frame = 0; frame < 16; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (columns.SequenceEqual(Frame())) throw new InvalidOperationException("Smoke must rise again after unpausing.");
        weatherLayer.Paused = paused;
        RenderMap(map);
        cameraZoom = zoom;
        cameraCenterTiles = center;
        RenderMap(map);
    }
}
