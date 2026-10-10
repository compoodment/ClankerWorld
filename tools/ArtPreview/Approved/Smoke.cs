using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Approved;

/// <summary>
/// Idea 3, chimney smoke from buildings someone is using: two Houses and the
/// Blacksmith's forge here, while the other Houses stand empty and smokeless.
/// </summary>
public sealed class SmokeProposal : IArtProposal, IAnimatedArtProposal
{
    private const double Seconds = 3;
    public string Family => "smoke";
    private static readonly Color SootColour = new("2A2622");
    private static readonly Color[] Embers = [new("E0662A"), new("F5A742"), new("FFE08A")];

    private static readonly (string Id, string Note)[] Looks =
    [
        ("a-wisps", "A: thin wisps, three small puffs drifting east and fading"),
        ("b-column", "B: a fuller rising column that leans with the wind"),
        ("c-puffs", "C: one round puff now and then"),
    ];

    public IEnumerable<Entry> Render()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var (id, note) in Looks) yield return new(Family, $"{id}-{size}", Frame(id, size, 1.2), note);
    }

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var size in new[] { 32, 16 })
            foreach (var (id, _) in Looks) yield return ($"{id}-{size}", Polish.Loop(Seconds, t => Frame(id, size, t)));
    }

    /// <summary>The chimneys of the buildings in use: two Houses by their flue, the Blacksmith by its forge.</summary>
    internal static List<Vector2> Sources(int size)
    {
        var scene = Polish.Scene(size);
        var sources = new List<Vector2>();
        foreach (var building in Polish.Buildings)
        {
            var origin = building.Footprint.Position;
            if (building.Kind == BuildingKind.House && origin is { X: 9, Y: 2 } or { X: 13, Y: 7 } &&
                Polish.Find(scene, building.Footprint, size, SootColour) is { } flue)
                sources.Add(flue);
            if (building.Kind == BuildingKind.Blacksmith && Polish.Find(scene, building.Footprint, size, Embers) is { } forge)
                sources.Add(forge);
        }
        return sources;
    }

    internal static Image Frame(string look, int size, double t)
    {
        var canvas = new Canvas(Polish.Scene(size));
        var unit = size / 32f;
        var index = 0;
        foreach (var source in Sources(size))
        {
            var (count, life, radius, rise, drift) = look switch
            {
                "b-column" => (9, 2.6, 3.4f, 26f, 11f),
                "c-puffs" => (2, 3.0, 4.2f, 18f, 7f),
                _ => (4, 2.2, 2.6f, 22f, 10f),
            };
            for (var p = 0; p < count; p++)
            {
                // Each puff repeats every loop, offset so the stream is seamless.
                var age = ((t / Seconds + p / (double)count + index * 0.37) % 1.0) * Seconds;
                if (age > life) continue;
                var k = (float)(age / life);
                var wobble = MathF.Sin((float)age * 3.1f + p) * 1.2f * unit;
                var centre = source + new Vector2(drift * k * unit + wobble, -rise * k * unit - unit);
                var r = MathF.Max(0.6f, (radius * (0.6f + 0.8f * k)) * unit);
                var alpha = 0.78f * (1 - k * k) * Polish.Smooth(k * 6);
                canvas.Disc(centre + new Vector2(0.6f * unit, 0.6f * unit), r, new Color(0.25f, 0.24f, 0.24f, alpha * 0.35f));
                canvas.Disc(centre, r, new Color(0.84f, 0.84f, 0.82f, alpha));
                canvas.Disc(centre - new Vector2(r * 0.35f, r * 0.35f), r * 0.45f, new Color(0.95f, 0.95f, 0.93f, alpha * 0.7f));
            }
            index++;
        }
        return canvas.ToImage();
    }
}

