using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// An animal on the map. Like an agent, it shows the next walking step each
/// time it moves to a new tile and stands again once it has not moved for
/// <see cref="AgentMarker.StepSeconds"/>, so an animal that keeps walking
/// alternates its two steps (walking steps approved October 7).
/// </summary>
public partial class AnimalMapSprite : TextureRect
{
    private (string Species, int Facing, bool Young, bool Mounted, bool Saddled, bool Shorn, int Size) look;
    private int step;
    private double stepSecondsLeft;

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
            stepSecondsLeft = 0;
            SetProcess(false);
        }
        else if (stepped)
        {
            step = step == 1 ? 2 : 1;
            stepSecondsLeft = AgentMarker.StepSeconds;
            SetProcess(true);
        }
        Refresh();
    }

    public override void _Process(double delta)
    {
        stepSecondsLeft -= delta;
        if (stepSecondsLeft > 0) return;
        step = 0;
        SetProcess(false);
        Refresh();
    }

    private void Refresh() => Texture = AnimalSprites.Texture(look.Species, look.Facing, look.Young, look.Mounted, look.Saddled, look.Shorn,
        look.Size, step);
}
