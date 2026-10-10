using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Agents face the way they last moved (eight ways, the short way round a
    /// wrapping world, south until they first move), step through the walk
    /// frames only while their tile changes, and otherwise show what the
    /// observation says they are doing. Their bounded hit targets travel with
    /// the gliding drawing and arrive at the tile the host reported.
    /// </summary>
    private async Task VerifyAgentPosesAsync(OwnerWorldSnapshot baseMap, OwnerWorldInhabitant template)
    {
        var expectedFacings = new (int Dx, int Dy, int Facing)[]
        {
            (0, 1, 0), (-1, 1, 1), (-1, 0, 2), (-1, -1, 3), (0, -1, 4), (1, -1, 5), (1, 0, 6), (1, 1, 7),
            (0, 0, AgentSprites.South), (2, 1, 7), (1, -3, 4), (-3, 1, 2),
        };
        foreach (var (dx, dy, expected) in expectedFacings)
        {
            if (AgentSprites.FacingToward(dx, dy) != expected)
                throw new InvalidOperationException($"A move of ({dx}, {dy}) must face {expected}, not {AgentSprites.FacingToward(dx, dy)}.");
        }

        var facings = Enumerable.Range(0, AgentSprites.FacingCount)
            .Select(facing => Convert.ToBase64String(AgentSprites.Sprite(0, 2, facing, AgentFrame.Still, 32).GetData()))
            .ToHashSet(StringComparer.Ordinal);
        var southStill = Convert.ToBase64String(AgentSprites.Sprite(0, 2, AgentSprites.South, AgentFrame.Still, 32).GetData());
        if (facings.Count != AgentSprites.FacingCount ||
            Enumerable.Range(1, AgentSprites.FrameCount - 1).Any(frame =>
                Convert.ToBase64String(AgentSprites.Sprite(0, 2, AgentSprites.South, (AgentFrame)frame, 32).GetData()) == southStill) ||
            southStill != Convert.ToBase64String(AgentSprites.Sprite(0, 2, 32).GetData()))
            throw new InvalidOperationException("Each facing and frame must look different, and the south still must match the portrait sprite.");

        var walker = template with
        {
            Id = "agent:pose-ui-test",
            Survival = null,
            PublicIntention = new OwnerWorldPublicIntention("safe_idle", "keeping a safe routine", "deterministic", 1),
        };
        var map = baseMap with { Resources = [], PlacedBuildings = [], Authoring = null };
        var wasProcessing = IsProcessing();
        SetProcess(false);
        try
        {
            AgentMarker Show(int x, int y, OwnerWorldInhabitant? person = null)
            {
                var at = new OwnerWorldPosition(x, y);
                RenderMap(map with { Inhabitants = [(person ?? walker) with { Position = at }] });
                return inhabitantVisuals[walker.Id];
            }
            void Expect(AgentMarker marker, int facing, AgentFrame frame, string when)
            {
                if (marker.Facing != facing || marker.Frame != frame)
                    throw new InvalidOperationException($"An agent {when} must face {facing} in frame {frame}, not {marker.Facing} in {marker.Frame}.");
            }

            var marker = Show(1, 1);
            Expect(marker, AgentSprites.South, AgentFrame.Still, "who has not moved yet");
            Expect(Show(2, 1), 6, AgentFrame.Walk1, "stepping east");
            AdvanceMapMarkers(WalkingMotion.FrameSeconds);
            Expect(Show(2, 0), 4, AgentFrame.Walk2, "stepping north without restarting the walk clock");
            marker = Show(1, 1);
            Expect(marker, 1, AgentFrame.Walk2, "stepping south-west with a continuous walk clock");
            AdvanceMapMarkers(WalkingMotion.GlideSeconds);
            var tileRect = new Rect2(new Vector2(currentTileSize, currentTileSize), new Vector2(currentTileSize, currentTileSize));
            if (!tileRect.Encloses(new Rect2(marker.Position, marker.Size)))
                throw new InvalidOperationException("A walking agent's marker must finish at the tile the host reports.");
            Expect(Show(1, 1), 1, AgentFrame.Still, "after arriving");
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            AdvanceMapMarkers(WalkingMotion.GlideSeconds + 0.1);
            Expect(marker, 1, AgentFrame.Still, "who stopped walking");

            var talking = walker with { PublicIntention = new("trade_propose:agent:other", "offering a trade", "deterministic", 2) };
            Expect(Show(1, 1, talking), 1, AgentFrame.Talk, "talking while standing");
            Expect(Show(2, 1, talking), 6, AgentFrame.Walk1, "walking to a conversation");
            var ill = talking with { Survival = new OwnerWorldSurvival(8_000, 3_000, true, false, 6_000, null) };
            Expect(Show(3, 1, ill), 6, AgentFrame.Hurt, "who is ill, even mid-step");
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            AdvanceMapMarkers(WalkingMotion.GlideSeconds);
            var waiting = walker with { DecisionFactors = [new("model-status", "waiting")] };
            marker = Show(3, 1, waiting);
            var waitingBounds = new Rect2(marker.Position, marker.Size);
            marker.ConversationBadgeVisible = true;
            var conversationBounds = marker.ConversationBadgeBounds;
            marker._Process(0.5);
            if (marker.WaitingMarkerVisible) throw new InvalidOperationException("A quick model reply must not flash the waiting spark.");
            Show(3, 1, walker);
            marker._Process(3);
            if (marker.WaitingMarkerVisible) throw new InvalidOperationException("A completed reply must clear the waiting spark.");
            marker = Show(3, 1, waiting);
            marker.ConversationBadgeVisible = true;
            marker._Process(AgentMarker.WaitingMarkerDelaySeconds + 0.01);
            marker = Show(3, 1, waiting);
            marker.ConversationBadgeVisible = true;
            if (!marker.WaitingMarkerVisible || !marker.IsProcessing() || marker.Frame != AgentFrame.Still ||
                new Rect2(marker.Position, marker.Size) != waitingBounds || marker.ConversationBadgeBounds != conversationBounds)
                throw new InvalidOperationException("A slow reply must keep its spark through refresh and walk expiry, beside the unchanged conversation hit target.");
            for (var frame = 0; frame < 8; frame++)
            {
                marker._Process(0.125);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            marker.ObserveModelWait(map.WorldId, waiting: true, paused: true);
            if (marker.WaitingMarkerVisible || marker.IsProcessing())
                throw new InvalidOperationException("Pause must clear the waiting marker and stop its animation.");
            marker.ObserveModelWait(map.WorldId, waiting: true, paused: false);
            marker._Process(3);
            marker.ObserveModelWait("another-wait-world", waiting: true, paused: false);
            if (marker.WaitingMarkerVisible) throw new InvalidOperationException("Another world must start a fresh waiting delay.");
            marker.ObserveModelWait(map.WorldId, waiting: false, paused: false);

            var swimmer = walker with { Route = walker.Route with { Status = "swim" } };
            marker = Show(4, 1, swimmer);
            Expect(marker, 6, AgentFrame.Work, "swimming instead of walking");
            if (!marker.Swimming || !marker.NameShown || !marker.TooltipText.Contains("Swimming", StringComparison.Ordinal))
                throw new InvalidOperationException("The map must visibly distinguish authoritative swimming movement.");
            marker._Process(0.51);
            AdvanceMapMarkers(WalkingMotion.GlideSeconds + 0.1);
            Expect(marker, 6, AgentFrame.Still, "between slow swimming steps");
            marker = Show(5, 1);
            if (marker.Swimming) throw new InvalidOperationException("The water movement must stop after returning to shore.");
            Expect(marker, 6, AgentFrame.Walk1, "walking again on shore");

            var loose = new AgentMarker();
            try
            {
                loose.ObserveTile("wrap-test", new Vector2I(0, 5), 64, wrapsEastWest: true);
                loose.ObserveTile("wrap-test", new Vector2I(63, 5), 64, wrapsEastWest: true);
                Expect(loose, 2, AgentFrame.Walk1, "stepping west across the wrapped seam");
                loose.ObserveTile("wrap-test", new Vector2I(40, 9), 64, wrapsEastWest: true);
                Expect(loose, 2, AgentFrame.Still, "moved far away at once");
                loose.ObserveTile("another-world", new Vector2I(40, 8), 64, wrapsEastWest: true);
                Expect(loose, AgentSprites.South, AgentFrame.Still, "seen in another world");
            }
            finally
            {
                loose.Free();
            }

            OwnerWorldInhabitant Doing(string? intention, IReadOnlyList<OwnerWorldInventoryEntry>? carried = null) => walker with
            {
                PublicIntention = intention is null ? null : new OwnerWorldPublicIntention(intention, "busy", "deterministic", 3),
                Inventory = carried ?? [],
            };
            var activities = new (OwnerWorldInhabitant Agent, AgentFrame Expected, string What)[]
            {
            (Doing("haul_farm_grain", [new("grain", 2), new("clothing", 1)]), AgentFrame.Carry, "hauling grain"),
            (Doing("haul_farm_grain", [new("clothing", 1)]), AgentFrame.Still, "on the way to collect a load"),
            (Doing("supply_workstation:wood", [new("wood", 3)]), AgentFrame.Carry, "supplying a workstation"),
            (Doing("child_converse:agent:friend"), AgentFrame.Talk, "chatting"),
            (Doing("knowledge_share:map-1|agent:friend"), AgentFrame.Talk, "sharing a map"),
            (Doing("harvest_food"), AgentFrame.Work, "gathering food"),
            (Doing("build:recipe:shelter") with { Project = new("Shelter", "working", 3, 10, null, 1) }, AgentFrame.Work, "building on site"),
            (Doing("build:recipe:shelter") with { Project = new("Shelter", "travelling", 0, 10, null, 1) }, AgentFrame.Still, "heading to a building site"),
            (Doing("safe_idle"), AgentFrame.Still, "taking it easy"),
            (Doing("dance_in_the_rain"), AgentFrame.Still, "doing something unknown"),
            (Doing(null), AgentFrame.Still, "with no intention"),
            (Doing("harvest_food") with { Survival = new(8_000, 2_500, true, false, 6_000, null) }, AgentFrame.Hurt, "ill"),
            (Doing("harvest_food") with { Survival = new(8_000, 2_499, true, false, 6_000, null) }, AgentFrame.Work, "slightly unwell"),
            };
            foreach (var (agent, expected, what) in activities)
            {
                if (AgentMarker.ActivityFor(agent) != expected)
                    throw new InvalidOperationException($"An agent {what} must show {expected}, not {AgentMarker.ActivityFor(agent)}.");
            }
        }
        finally { SetProcess(wasProcessing); }

    }
}
