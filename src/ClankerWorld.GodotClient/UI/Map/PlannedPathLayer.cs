using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Developer tools: the route one agent is walking, drawn over the map as a
/// dark-edged pale line through the middle of each tile ahead, a dot on each
/// step and a gold square around where they are heading. It only draws what
/// the server reported; it never plans a route itself. Points are in map
/// stage pixels, so the line moves with the camera.
/// </summary>
public partial class PlannedPathLayer : Control
{
    private static readonly Color Line = new("FFF0B5");
    private static readonly Color Edge = new("1E1712");
    private static readonly Color Goal = new("FFD166");

    private Vector2[] points = [];
    private Vector2? destination;
    private float tileSize;

    public PlannedPathLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // Above object labels, below agents, so the walker stays on top of their path.
        ZIndex = 6;
    }

    /// <summary>The agent's tile followed by each step ahead, as tile centers in stage pixels.</summary>
    public IReadOnlyList<Vector2> Points => points;

    public Vector2? Destination => destination;

    public void SetPath(IReadOnlyList<Vector2> path, Vector2? goal, float tile)
    {
        points = [.. path];
        destination = goal;
        tileSize = tile;
        QueueRedraw();
    }

    public void Clear()
    {
        if (points.Length == 0 && destination is null) return;
        points = [];
        destination = null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (points.Length < 2) return;
        var width = Mathf.Max(2f, Mathf.Round(tileSize / 12f));
        DrawPolyline(points, Edge, width + 2);
        DrawPolyline(points, Line, width);
        var dot = Mathf.Max(2f, Mathf.Round(width * 1.5f));
        for (var index = 1; index < points.Length; index++)
        {
            var center = points[index].Floor();
            DrawRect(new Rect2(center - new Vector2(dot / 2 + 1, dot / 2 + 1), new Vector2(dot + 2, dot + 2)), Edge);
            DrawRect(new Rect2(center - new Vector2(dot / 2, dot / 2), new Vector2(dot, dot)), Line);
        }
        if (destination is { } goal)
        {
            var half = Mathf.Max(4f, Mathf.Round(tileSize * 0.4f));
            var box = new Rect2(goal.Floor() - new Vector2(half, half), new Vector2(half * 2, half * 2));
            DrawRect(box.Grow(1), Edge, filled: false, width: width + 2);
            DrawRect(box, Goal, filled: false, width: width);
        }
    }
}
