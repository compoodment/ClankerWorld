using ArtPreview.Proposed.Polish;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview;

internal static class GraveClientPreview
{
    public static Image Frame(bool headstone, int size)
    {
        var scene = new Canvas(Polish.Scene(size, agents: true));
        scene.Stamp(GraveArt.Sprite(headstone, size), 7 * size, 10 * size, size);
        return scene.ToImage();
    }
}
