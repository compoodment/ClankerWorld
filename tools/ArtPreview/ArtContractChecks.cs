using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview;

/// <summary>Checks the default scene against the current client's art, without writing pictures.</summary>
internal static class ArtContractChecks
{
    public static void Run()
    {
        var current = new ArtSet();
        var approvedBuildings = new ArtSet();
        new Proposed.Buildings.BuildingsProposal().Apply(approvedBuildings);
        foreach (var size in new[] { 16, 32 })
        {
            foreach (var (width, height) in new[] { (1, 1), (1, 2), (2, 1) })
                foreach (var side in Enum.GetValues<DoorSide>())
                {
                    var door = new BuildingDoor(side, 0);
                    Equal(current.Building(BuildingKind.Store, width, height, size, door),
                        approvedBuildings.Building(BuildingKind.Store, width, height, size, door),
                        $"The playable Store must match its approved {width}x{height} drawing facing {side} at {size} px.");
                }
            foreach (var (facing, frame) in new[] { (6, AgentFrame.Walk2), (4, AgentFrame.Carry), (2, AgentFrame.Talk) })
                Equal(current.Agent(0, 2, facing, (int)frame, size), AgentSprites.Sprite(0, 2, facing, frame, size),
                    $"The current scene must show the observed facing and frame at {size} px.");
            foreach (var eastWest in new[] { false, true })
            {
                var deck = current.Bridge?.Invoke(eastWest, size)
                    ?? throw new InvalidOperationException("The current scene must use the approved bridge deck.");
                Equal(deck, RoadSprites.BridgeDeck(eastWest, size), "Current bridge art must match the client.");
            }
            var range = SceneSpec.MountainRange().Map();
            var relief = current.Relief?.Invoke(range, size)
                ?? throw new InvalidOperationException("The current scene must show the client's mountain relief.");
            Equal(relief, ReliefRenderer.Render(range, new Rect2I(0, 0, range.Width, range.Height), size)!,
                "Current relief must match the client.");
        }
        var crossing = new SceneSpec { Width = 5, Height = 5 };
        crossing.Roads.Add(new(1, 2));
        crossing.Bridges[new(2, 2)] = true;
        if (SceneComposer.RoadLinksAt(crossing, 1, 2, true) != (RoadLinks.Road | RoadLinks.East))
            throw new InvalidOperationException("A Road must join an aligned bridge deck.");
        crossing.Bridges[new(2, 2)] = false;
        if (SceneComposer.RoadLinksAt(crossing, 1, 2, true) != RoadLinks.Road)
            throw new InvalidOperationException("A Road must not join the side of a perpendicular deck.");
        crossing.Roads.Clear();
        if (SceneComposer.RoadLinksAt(crossing, 1, 2, true) != RoadLinks.None)
            throw new InvalidOperationException("A deck must not create a Road piece on an empty bank.");
        Console.WriteLine("Current-art contract checks passed.");
    }

    private static void Equal(Image actual, Image expected, string message)
    {
        if (actual.GetWidth() != expected.GetWidth() || actual.GetHeight() != expected.GetHeight() ||
            !actual.GetData().AsSpan().SequenceEqual(expected.GetData()))
            throw new InvalidOperationException(message);
    }
}
