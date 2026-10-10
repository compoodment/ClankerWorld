using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Unsaved, once-only moments for construction observed finishing in the visible map.</summary>
public partial class BuildingCompletionLayer : Control
{
    private WorldTerrainLayer? terrain;
    private readonly List<(string Id, Rect2I Footprint, double Age)> moments = [];
    private HashSet<string> buildings = new(StringComparer.Ordinal);
    private HashSet<(string Definition, Rect2I Footprint)> sites = [];
    private long? lastTick;
    private bool paused;
    private Rect2 lastVisible;
    private int lastSize;

    public int ActiveCount => moments.Count;
    public int DrawnMomentCount { get; private set; }
    public int DrawnSpanCount { get; private set; }
    public double FirstAge => moments.Count == 0 ? 0 : moments[0].Age;

    public BuildingCompletionLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void Follow(WorldTerrainLayer source) => terrain = source;

    public void Reset()
    {
        moments.Clear(); buildings.Clear(); sites.Clear(); lastTick = null;
        QueueRedraw();
    }

    public void Observe(OwnerWorldSnapshot snapshot)
    {
        paused = snapshot.Authoring?.IsPaused == true;
        var nextBuildings = snapshot.PlacedBuildings.Select(building => building.InstanceId).ToHashSet(StringComparer.Ordinal);
        var nextSites = snapshot.ConstructionSites.Select(site =>
            (site.DefinitionId, new Rect2I(site.Site.X, site.Site.Y, Math.Max(1, site.Width), Math.Max(1, site.Height)))).ToHashSet();
        moments.RemoveAll(moment => !nextBuildings.Contains(moment.Id));
        if (lastTick is { } tick && snapshot.WorldTick > tick &&
            snapshot.WorldTick - tick <= Math.Max(1, (snapshot.CalendarPace?.TicksPerDay ?? 1440) / 24) && !paused)
            foreach (var building in snapshot.PlacedBuildings)
            {
                var footprint = new Rect2I(building.Position.X, building.Position.Y, Math.Max(1, building.Width), Math.Max(1, building.Height));
                var key = (building.DefinitionId, footprint);
                if (buildings.Contains(building.InstanceId) || building.PlacedTick <= tick || building.PlacedTick > snapshot.WorldTick ||
                    !sites.Contains(key) || nextSites.Contains(key) || terrain is not { TileSize: >= WorldTerrainLayer.SpriteTileMinimum } ||
                    !Copies(footprint).Any(copy => copy.Intersects(terrain.VisibleTiles))) continue;
                if (moments.Count == 128) moments.RemoveAt(0);
                moments.Add((building.InstanceId, footprint, 0));
            }
        else if (lastTick is { } previous && (snapshot.WorldTick < previous ||
            snapshot.WorldTick - previous > Math.Max(1, (snapshot.CalendarPace?.TicksPerDay ?? 1440) / 24))) moments.Clear();
        buildings = nextBuildings; sites = nextSites; lastTick = snapshot.WorldTick;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        var changed = terrain is not null && (lastVisible != terrain.VisibleTiles || lastSize != terrain.TileSize);
        if (terrain is not null) { lastVisible = terrain.VisibleTiles; lastSize = terrain.TileSize; }
        if (!paused && moments.Count > 0)
        {
            for (var i = 0; i < moments.Count; i++)
            {
                var moment = moments[i];
                moments[i] = moment with { Age = moment.Age + Math.Max(0, delta) };
            }
            moments.RemoveAll(moment => moment.Age >= BuildingCompletionArt.Duration);
            changed = true;
        }
        if (changed) QueueRedraw();
    }

    private IEnumerable<Rect2> Copies(Rect2I footprint)
    {
        if (terrain?.World is not { } map) yield break;
        foreach (var shift in terrain.WrapsEastWest ? new[] { -map.Width, 0, map.Width } : [0])
            yield return new Rect2(footprint.Position.X + shift, footprint.Position.Y, footprint.Size.X, footprint.Size.Y);
    }

    public override void _Draw()
    {
        DrawnMomentCount = 0; DrawnSpanCount = 0;
        if (terrain is not { TileSize: >= WorldTerrainLayer.SpriteTileMinimum } source) return;
        foreach (var moment in moments)
            foreach (var copy in Copies(moment.Footprint))
            {
                if (!copy.Grow(0.5f).Intersects(source.VisibleTiles)) continue;
                var rect = new Rect2(copy.Position * source.Stride,
                    copy.Size * source.Stride - new Vector2(source.Stride - source.TileSize, source.Stride - source.TileSize));
                BuildingCompletionArt.Paint(rect, source.TileSize, moment.Age, (x, y, width, colour) =>
                {
                    if (colour.A <= 0) return;
                    DrawRect(new Rect2(x, y, width, 1), colour); DrawnSpanCount++;
                });
                DrawnMomentCount++;
            }
    }
}
