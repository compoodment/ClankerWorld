using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static readonly JsonSerializerOptions MapMeasurementJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private async Task MeasureMapDrawAsync()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            var flag = Array.IndexOf(args, "--measure-map-draw");
            if (flag < 0 || flag + 1 >= args.Length)
                throw new ArgumentException("Supply the generated public map JSON after --measure-map-draw.");
            using var document = JsonDocument.Parse(File.ReadAllText(args[flag + 1]));
            var root = document.RootElement;
            var width = root.GetProperty("Width").GetInt32();
            var height = root.GetProperty("Height").GetInt32();
            var wrap = root.GetProperty("WrapsEastWest").GetBoolean();
            var map = WorldTerrainMap.FromPacked(new(width, height, "terrain-kind-v1", root.GetProperty("Terrain").GetString()!),
                new(width, height, "map-layers-v1", root.GetProperty("Climate").GetString()!,
                    root.GetProperty("Elevation").GetString()!, root.GetProperty("Hydrology").GetString()!,
                    root.GetProperty("Surface").GetString()!, root.GetProperty("Vegetation").GetString()!), wrap);
            var resources = JsonSerializer.Deserialize<OwnerWorldResource[]>(root.GetProperty("Resources").GetRawText(),
                MapMeasurementJsonOptions)!;
            var layer = new WorldTerrainLayer { MeasureDrawCost = true };
            AddChild(layer);
            layer.SetWorld(map);
            layer.SetTrees(resources);
            layer.SetNaturalObjects(resources);
            foreach (var (name, visible, size) in new[]
            {
                ("overview", new Rect2(0, 0, width, height), 2),
                ("detail", new Rect2(width / 4, height / 4, Math.Min(96, width), Math.Min(48, height)), 16),
            })
            {
                layer.SetCamera(visible, size, 0, wrap);
                var samples = new List<double>();
                for (var frame = 0; frame < 25; frame++)
                {
                    var previous = layer.DrawSampleCount;
                    layer.QueueRedraw();
                    for (var attempt = 0; attempt < 10 && layer.DrawSampleCount == previous; attempt++)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (layer.DrawSampleCount == previous)
                        throw new InvalidOperationException("The engine did not submit a map draw; no timing was recorded.");
                    if (frame >= 5) samples.Add(layer.LastDrawMilliseconds);
                }
                GD.Print("MAP_DRAW_MEASUREMENT " + JsonSerializer.Serialize(new
                {
                    Fixture = Path.GetFileName(args[flag + 1]),
                    Camera = name,
                    VisibleTiles = layer.VisibleTileCount,
                    ResourceCount = resources.Length,
                    MeasuredDraws = samples.Count,
                    MedianCpuSubmissionMs = samples.Order().ElementAt(samples.Count / 2),
                    MaximumCpuSubmissionMs = samples.Max(),
                    DisplayServer = Godot.DisplayServer.GetName(),
                }));
            }
            layer.QueueFree();
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PrintErr("Map draw measurement failed: " + exception.Message);
            GetTree().Quit(1);
        }
    }
}
