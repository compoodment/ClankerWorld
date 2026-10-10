using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Polish;

/// <summary>
/// Idea 11, a smoother camera: Find moves the view from the Farmhouse to the
/// east Houses, then the view zooms in one step.
/// </summary>
public sealed class CameraProposal : IAnimatedArtProposal
{
    private const double Seconds = 4;
    public string Family => "camera";

    public IEnumerable<(string Id, IReadOnlyList<Image> Frames)> Animate()
    {
        foreach (var look in new[] { "a-today", "b-ease", "c-ease-settle" })
            yield return ($"{look}-32", Polish.Loop(Seconds, t => Frame(look, t)));
    }

    private static float Ease(string look, float k) => look switch
    {
        "a-today" => k > 0 ? 1 : 0,
        "b-ease" => CameraEasing.Amount(k),
        // A slower start and a soft settle that overshoots by about 4%.
        _ => Math.Clamp(k, 0, 1) is var x ? 1 + 2.2f * MathF.Pow(x - 1, 3) + 1.2f * MathF.Pow(x - 1, 2) : 0,
    };

    private static Image Frame(string look, double t)
    {
        const int size = 32, viewW = 320, viewH = 192;
        var scene = new Canvas(Polish.Scene(size, agents: true));
        var from = new Vector2(4.5f, 6.5f) * size;
        var to = new Vector2(14.5f, 7.0f) * size;
        var move = Ease(look, (float)(t - 0.6) / CameraEasing.MoveSeconds);
        var centre = from.Lerp(to, move);
        var zoom = 1f + 0.5f * Ease(look, (float)(t - 2.2) / CameraEasing.ZoomSeconds);
        if (t < 0.6) centre = from;
        var w = viewW / zoom; var h = viewH / zoom;
        var x = (int)Math.Clamp(MathF.Round(centre.X - w / 2), 0, scene.Width - w);
        var y = (int)Math.Clamp(MathF.Round(centre.Y - h / 2), 0, scene.Height - h);
        return scene.Crop(new Rect2I(x, y, (int)w, (int)h), viewW, viewH).ToImage();
    }
}

