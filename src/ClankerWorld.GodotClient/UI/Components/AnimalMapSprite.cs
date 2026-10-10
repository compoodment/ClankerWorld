using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// An animal on the map: its observed appearance and facing, with the approved
/// quarter-second walk cadence supplied by the map's local glide clock.
/// </summary>
public partial class AnimalMapSprite : TextureRect
{
    private (string Species, int Facing, bool Young, bool Mounted, bool Saddled, bool Shorn, int Size) look;
    private int step;

    public AnimalMapSprite()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        SetProcess(false);
    }

    /// <summary>The walking step shown: 0 standing, or 1 or 2.</summary>
    public int Step => step;

    /// <summary>
    /// Shows the animal as the latest observation describes it. <paramref name="stepped"/>
    /// is true when it has just moved one short step, which shows the next walking frame.
    /// <paramref name="resetStep"/> clears motion after relocation, death or a new world observation.
    /// </summary>
    public void Show(string species, int facing, bool young, bool mounted, bool saddled, bool shorn, int size, bool stepped, bool resetStep)
    {
        look = (species, facing, young, mounted, saddled, shorn, size);
        if (resetStep)
        {
            step = 0;
            SetProcess(false);
        }
        else if (stepped)
        {
            step = 1;
        }
        Refresh();
    }

    /// <summary>Walking follows the map's local quarter-second glide clock.</summary>
    public void ShowMotion(bool moving, double seconds)
    {
        var frame = moving ? WalkingMotion.StepAt(seconds) : 0;
        SetProcess(false);
        if (step == frame) return;
        step = frame;
        Refresh();
    }

    private void Refresh() => Texture = AnimalSprites.Texture(look.Species, look.Facing, look.Young, look.Mounted, look.Saddled, look.Shorn,
        look.Size, step);
}
