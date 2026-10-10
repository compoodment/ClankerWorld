using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>A single multiply over the ground, below lights, labels, moving figures and weather.</summary>
public partial class GoldenHourLayer : Control
{
    private WorldTerrainLayer? source;
    private string? worldId;
    private long? lastTick;
    private float target;
    private float shown;
    private bool dawn;
    private bool paused;
    private Rect2 drawnCamera;
    private int drawnTileSize;
    private Color drawnMultiplier = Colors.White;

    public GoldenHourLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul };
    }

    public float ShownStrength => shown;
    public bool IsDawn => dawn;
    public Color CurrentMultiplier => GoldenHourTint.Multiplier(dawn, shown);
    public Rect2 DrawnArea { get; private set; }

    public void Follow(WorldTerrainLayer terrain) => source = terrain;

    /// <summary>Use the host's twilight, including seasonal night length, rather than recomputing a night schedule.</summary>
    public void Observe(string nextWorldId, long tick, int ticksPerDay, int offsetTicks, int? darknessBasisPoints, bool isPaused)
    {
        var day = Math.Max(1, ticksPerDay);
        var clockTick = (tick % day + offsetTicks % (long)day + day) % day;
        var morning = clockTick * 2 < day;
        var reset = worldId != nextWorldId || lastTick is not { } previous || tick < previous ||
            tick - previous > day / 24f || morning != dawn;
        worldId = nextWorldId;
        lastTick = tick;
        dawn = morning;
        paused = isPaused;
        target = GoldenHourTint.StrengthAt(NightLayer.FromBasisPoints(darknessBasisPoints));
        if (reset) Settle();
    }

    public void Reset()
    {
        worldId = null;
        lastTick = null;
        target = shown = 0;
        QueueRedraw();
    }

    public void Settle()
    {
        shown = target;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (source is null || !IsVisibleInTree()) return;
        if (!paused) shown = (float)Mathf.MoveToward(shown, target, delta * 2);
        if (CurrentMultiplier != drawnMultiplier || (shown > 0 &&
            (source.VisibleTiles != drawnCamera || source.TileSize != drawnTileSize))) QueueRedraw();
    }

    public override void _Draw()
    {
        drawnMultiplier = CurrentMultiplier;
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
        // Mul uses opaque source RGB as a multiplier; white is the identity.
        // A normal alpha colour wash would brighten the scene and lose its contrast.
        DrawRect(covered, CurrentMultiplier);
        DrawnArea = covered;
    }
}
