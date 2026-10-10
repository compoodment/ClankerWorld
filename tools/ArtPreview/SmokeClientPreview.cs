using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview;

/// <summary>The live client column in the approved reference scene, without the alternative drawings.</summary>
public sealed class SmokeClientPreview : IArtProposal, IAnimatedArtProposal
{
    public string Family => "smoke-client";

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
            yield return new(Family, $"b-column-{size}", Frame(size, 1.2), "Approved smoke B, drawn by the client.");
    }

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var size in new[] { 32, 16 })
            yield return ($"b-column-{size}", Polish.Loop(3, time => Frame(size, time)));
    }

    internal static IEnumerable<Vector2> Sources(int size) => Chimneys(size).Select(chimney => chimney.Local + chimney.Origin);

    private static IEnumerable<(Vector2 Local, Vector2 Origin)> Chimneys(int size)
    {
        foreach (var building in Polish.Buildings)
        {
            var origin = building.Footprint.Position;
            var occupied = building.Kind == BuildingKind.House && origin is { X: 9, Y: 2 } or { X: 13, Y: 7 };
            if (!SmokeArt.InUse(building.Kind, occupied, building.Kind == BuildingKind.Blacksmith)) continue;
            if (SmokeArt.Source(building.Kind, building.Footprint.Size.X, building.Footprint.Size.Y,
                    size, building.Door) is { } local)
                yield return (local, (Vector2)origin * size);
        }
    }

    internal static Image Frame(int size, double time)
    {
        var canvas = new Canvas(Polish.Scene(size));
        var index = 0;
        var spans = new List<SmokeSpan>();
        foreach (var (source, origin) in Chimneys(size))
        {
            spans.Clear();
            SmokeArt.AppendColumn(spans, source + origin, size, time, index, Vector2.Zero, 1);
            foreach (var span in spans)
                canvas.Fill((int)span.Area.Position.X, (int)span.Area.Position.Y,
                    (int)span.Area.Size.X, (int)span.Area.Size.Y, span.Color);
            index++;
        }
        return canvas.ToImage();
    }
}
