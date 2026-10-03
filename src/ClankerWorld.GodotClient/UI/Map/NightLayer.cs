using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// A gentle blue night wash over the ground, buildings and trees. It sits
/// under map labels, agent markers and weather, so names and people stay as
/// bright as by day, and every panel stays untouched. The host decides how
/// dark it is; this layer only eases between the steps it reports, so dusk
/// and dawn fade smoothly instead of jumping once a second.
/// </summary>
public partial class NightLayer : Control
{
    /// <summary>
    /// The wash's opacity at full night. The ground keeps 60% of its daylight
    /// contrast, so water, forest, sand, rock, roads and fields stay easy to
    /// tell apart at every zoom.
    /// </summary>
    public const float FullNightAlpha = 0.4f;

    /// <summary>A deep evening blue rather than black, so night reads as night and not as a dimmed screen.</summary>
    public static readonly Color Wash = new("0B1433");

    /// <summary>How quickly the shown darkness follows the host, in full nights per second.</summary>
    private const float EasePerSecond = 0.5f;

    private WorldTerrainLayer? source;
    private float target;
    private float shown;
    private Rect2 drawnCamera;
    private int drawnTileSize;
    private float drawnDarkness;

    public NightLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The terrain layer whose camera and map this wash covers.</summary>
    public void Follow(WorldTerrainLayer terrain) => source = terrain;

    /// <summary>The host's darkness, from 0 in daylight to 1 at full night.</summary>
    public float Darkness
    {
        get => target;
        set => target = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>The darkness currently drawn, which eases toward <see cref="Darkness"/>.</summary>
    public float ShownDarkness => shown;

    /// <summary>The wash as it is drawn now.</summary>
    public Color CurrentWash => Wash with { A = FullNightAlpha * shown };

    /// <summary>The map area the wash covered when last drawn, in map pixels; empty by day.</summary>
    public Rect2 DrawnArea { get; private set; }

    /// <summary>Show the host's darkness at once, for a newly opened world.</summary>
    public void Settle()
    {
        shown = target;
        QueueRedraw();
    }

    /// <summary>Converts a host darkness reading (0 to 10,000) to this layer's 0 to 1 scale.</summary>
    public static float FromBasisPoints(int? darknessBasisPoints) =>
        Math.Clamp(darknessBasisPoints ?? 0, 0, 10_000) / 10_000f;

    public override void _Process(double delta)
    {
        if (source is null || !IsVisibleInTree()) return;
        if (shown != target)
            shown = (float)Mathf.MoveToward(shown, target, delta * EasePerSecond);
        if (shown != drawnDarkness || (shown > 0 &&
            (source.VisibleTiles != drawnCamera || source.TileSize != drawnTileSize)))
            QueueRedraw();
    }

    public override void _Draw()
    {
        drawnDarkness = shown;
        DrawnArea = new Rect2();
        if (source?.World is not { } world || shown <= 0) return;
        drawnCamera = source.VisibleTiles;
        drawnTileSize = source.TileSize;
        var stride = source.Stride;
        var map = source.WrapsEastWest
            ? new Rect2(-1e7f, 0, 2e7f, world.Height * stride)
            : new Rect2(0, 0, world.Width * stride, world.Height * stride);
        var covered = new Rect2(drawnCamera.Position * stride, drawnCamera.Size * stride).Intersection(map);
        if (!covered.HasArea()) return;
        DrawRect(covered, CurrentWash);
        DrawnArea = covered;
    }
}
