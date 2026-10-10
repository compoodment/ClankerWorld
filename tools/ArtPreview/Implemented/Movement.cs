using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview;

/// <summary>The implemented C drawing uses the client's approved walking cadence unchanged.</summary>
internal static class MovementPreview
{
    public static Image Frame(int size, double seconds)
    {
        var canvas = new Canvas(Polish.Scene(size));
        var east = AgentSprites.FacingToward(1, 0);
        var step = WalkingMotion.StepAt(seconds);
        var bob = WalkingMotion.BobAt(seconds);
        var drawn = (int)MathF.Round(size * SceneComposer.AgentSpriteScale);
        var agentX = (8 + seconds) * size + size / 2f;
        var agentY = 6 * size + size / 2f;
        var agent = AgentSprites.Sprite(1, AgentSprites.StageIndex("adult"), east,
            step == 1 ? AgentFrame.Walk1 : AgentFrame.Walk2, drawn >= 24 ? 32 : 16);
        canvas.Stamp(agent, (int)Math.Round(agentX - drawn / 2f), (int)MathF.Round(agentY - drawn / 2f) + bob * Math.Max(1, size / 32), drawn);
        var cow = AnimalSprites.Sprite("cow", east, young: false, mounted: false, size: size, step: step);
        canvas.Stamp(cow, (int)Math.Round((9 + seconds) * size), 10 * size + bob, size);
        return canvas.ToImage();
    }

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var size in new[] { 32, 16 })
        {
            var directory = Path.Combine(root, "c-glide-bob-" + size);
            Directory.CreateDirectory(directory);
            for (var frame = 0; frame < 48; frame++)
                Frame(size, frame / 12.0).SavePng(Path.Combine(directory, $"{frame:D4}.png"));
            Frame(size, 1.5).SavePng(Path.Combine(root, $"c-glide-bob-{size}.png"));
        }
        Console.WriteLine("Implemented movement: 96 frames and two stills");
    }
}
