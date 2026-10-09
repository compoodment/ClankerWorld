using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Dictionary<string, MapMarkerMotion> markerMotions = new(StringComparer.Ordinal);

    private sealed class MapMarkerMotion(Control marker, bool agent, bool bob)
    {
        public Control Marker { get; } = marker;
        public bool Agent { get; } = agent;
        public bool Bob { get; } = bob;
        public Vector2 Offset { get; set; }
        public Vector2 Tile { get; private set; }
        public bool Moving { get; private set; }
        public double WalkingSeconds { get; private set; }
        private Vector2 start;
        private Vector2 target;
        private Vector2I? reported;
        private string? worldId;
        private double elapsed;

        public void Observe(string world, OwnerWorldPosition position, int width, bool wraps, bool snap, bool paused)
        {
            var next = new Vector2I(position.X, position.Y);
            if (reported is not { } prior || worldId != world || snap)
            {
                worldId = world;
                reported = next;
                Tile = target = new(position.X, position.Y);
                Moving = false;
                WalkingSeconds = elapsed = 0;
                return;
            }
            if (prior == next) return;
            if (paused)
            {
                reported = next;
                Tile = target = new(next.X, next.Y);
                Moving = false;
                WalkingSeconds = elapsed = 0;
                return;
            }
            reported = next;
            var dx = next.X - prior.X;
            if (wraps && width > 0) dx -= (int)Math.Round(dx / (double)width) * width;
            var dy = next.Y - prior.Y;
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > AgentMarker.MaxStepTiles)
            {
                Tile = target = new(next.X, next.Y);
                Moving = false;
                WalkingSeconds = elapsed = 0;
                return;
            }
            start = Tile;
            // Target the nearest copy across a wrapped seam, then keep the view's copy near the camera.
            var targetX = next.X;
            if (wraps && width > 0) targetX += (int)Math.Round((Tile.X - targetX) / width) * width;
            target = new(targetX, next.Y);
            elapsed = 0;
            if (!Moving) WalkingSeconds = 0;
            Moving = Tile != target;
        }

        public void Advance(double delta)
        {
            if (!Moving) return;
            var advance = Math.Max(0, delta);
            elapsed = Math.Min(WalkingMotion.GlideSeconds, elapsed + advance);
            WalkingSeconds += advance;
            Tile = start + (target - start) * (float)(elapsed / WalkingMotion.GlideSeconds);
            if (elapsed >= WalkingMotion.GlideSeconds) Moving = false;
        }
    }

    private void PresentMovingMarker(string id, Control marker, OwnerWorldPosition position, Vector2 offset,
        OwnerWorldSnapshot snapshot, bool agent, bool bob, bool snap = false)
    {
        if (!markerMotions.TryGetValue(id, out var motion))
            markerMotions.Add(id, motion = new(marker, agent, bob));
        motion.Offset = offset;
        motion.Observe(snapshot.WorldId, position, MapDimensions(snapshot).Width, snapshot.WrapsEastWest, snap, snapshot.Authoring?.IsPaused == true);
        ApplyMarkerMotion(id, motion, snapshot);
    }

    private void ApplyMarkerMotion(string id, MapMarkerMotion motion, OwnerWorldSnapshot snapshot)
    {
        var stride = currentTileSize + TileGap;
        var canonical = motion.Tile * stride + motion.Offset;
        var bob = motion.Moving && motion.Bob ? WalkingMotion.BobAt(motion.WalkingSeconds) : 0;
        motion.Marker.Position = new(WrappedMarkerX(canonical.X, MapDimensions(snapshot).Width, stride, snapshot.WrapsEastWest), canonical.Y + bob);
        if (motion.Agent)
        {
            var personId = id["agent:".Length..];
            inhabitantCanonicalXs[personId] = canonical.X;
            ((AgentMarker)motion.Marker).ShowMotion(motion.Moving, motion.WalkingSeconds);
        }
        else
        {
            mapObjectCanonicalXs[id] = canonical.X;
            motion.Marker.GetNodeOrNull<AnimalMapSprite>("AnimalSprite")?.ShowMotion(motion.Moving, motion.WalkingSeconds);
        }
    }

    private void AdvanceMapMarkers(double delta)
    {
        if (renderedMapSnapshot is not { } snapshot || snapshot.Authoring?.IsPaused == true) return;
        var changed = false;
        foreach (var (id, motion) in markerMotions)
        {
            if (!motion.Moving) continue;
            motion.Advance(delta);
            ApplyMarkerMotion(id, motion, snapshot);
            changed = true;
        }
        if (!changed) return;
        PositionSelectedInhabitantCard(snapshot);
        RefreshTileHoverAtMouse();
    }
}
