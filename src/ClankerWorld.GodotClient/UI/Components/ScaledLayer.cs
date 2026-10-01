using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Holds interface panels at UI Scale. Children lay out in unscaled pixels
/// over the parent's whole area, and the layer magnifies them by a whole
/// number, so frames, padding, icons and letters all grow together and stay
/// crisp. The map underneath keeps its own resolution.
/// </summary>
public partial class ScaledLayer : Control
{
    private int factor = 1;

    public ScaledLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>How many screen pixels each interface pixel covers.</summary>
    public int Factor
    {
        get => factor;
        set
        {
            factor = Math.Max(1, value);
            Fit();
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationParented || GetParent() is not Control parent) return;
        parent.Resized += Fit;
        Fit();
    }

    private void Fit()
    {
        if (GetParent() is not Control parent) return;
        Position = Vector2.Zero;
        Scale = new Vector2(factor, factor);
        Size = (parent.Size / factor).Floor();
    }
}
