using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>A fixed-size map hit target; unlike Button, text/theme minima cannot enlarge it.</summary>
public partial class AgentMarker : Control
{
    private string caption = string.Empty;
    private bool selected;
    private bool hovered;
    private int variant;
    private int stage = 2;
    private bool showNameTag;

    private const float SpriteScale = 1.35f;
    private const float NameTagMinimum = 36;

    public event Action? Activated;

    public string Caption
    {
        get => caption;
        set { if (caption != value) { caption = value; QueueRedraw(); } }
    }

    public bool Selected
    {
        get => selected;
        set { if (selected != value) { selected = value; Raise(); } }
    }

    /// <summary>Appearance variant from <see cref="AgentSprites.VariantFor"/>.</summary>
    public int Variant
    {
        get => variant;
        set { if (variant != value) { variant = value; QueueRedraw(); } }
    }

    /// <summary>Life-stage row from <see cref="AgentSprites.StageIndex"/>.</summary>
    public int Stage
    {
        get => stage;
        set { if (stage != value) { stage = value; QueueRedraw(); } }
    }

    /// <summary>Always draws the caption as a name tag at close zoom; off when several agents share a tile. Hover and selection show it regardless.</summary>
    public bool ShowNameTag
    {
        get => showNameTag;
        set { if (showNameTag != value) { showNameTag = value; QueueRedraw(); } }
    }

    public AgentMarker()
    {
        MouseFilter = MouseFilterEnum.Pass;
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 10;
        MouseEntered += () => { hovered = true; Raise(); };
        MouseExited += () => { hovered = false; Raise(); };
    }

    // A hovered or selected agent draws over its neighbors so its name tag stays readable.
    private void Raise()
    {
        ZIndex = selected || hovered ? 11 : 10;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Activated?.Invoke();
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var side = Math.Min(Size.X, Size.Y);
        var center = Size / 2;
        if (side < 10)
        {
            // Too small for a readable figure: a clear dot in the agent's colors.
            if (selected || hovered)
                DrawCircle(center, Math.Max(2.5f, side / 2 + 1.5f), new Color(selected ? "FFD166" : "FFF0B5"));
            DrawCircle(center, Math.Max(1.5f, side / 2), new Color("1E2226"));
            DrawCircle(center, Math.Max(1f, side / 2 - 1), new Color("F4C78A"));
            return;
        }

        // The figure fills about two thirds of its sprite cell, so the cell is
        // drawn larger than the bounded hit target to keep the person readable.
        var drawn = side * SpriteScale;
        var sprite = new Rect2(center - new Vector2(drawn, drawn) / 2, new Vector2(drawn, drawn));
        if (selected || hovered)
        {
            DrawArc(center + new Vector2(0, drawn * 0.06f), drawn * 0.36f, 0, Mathf.Tau, 32,
                new Color(selected ? "FFD166" : "FFF0B5"), Math.Max(1.5f, drawn / 18f));
        }
        var atlasSize = drawn >= 24 ? 32 : 16;
        DrawTextureRectRegion(AgentSprites.Atlas(atlasSize), sprite, AgentSprites.Region(variant, stage, atlasSize));
        var named = selected || hovered ? drawn >= 20 : showNameTag && drawn >= NameTagMinimum;
        if (!named || caption.Length == 0) return;
        var font = UiFonts.Text;
        const int fontSize = UiFonts.Body;
        var text = caption.Length > 14 ? caption[..13] + "…" : caption;
        var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
        var tag = new Rect2(new Vector2(center.X - textSize.X / 2 - 4, sprite.Position.Y + drawn * 0.86f),
            new Vector2(textSize.X + 8, textSize.Y + 2));
        DrawRect(tag, new Color(0.06f, 0.09f, 0.11f, 0.78f));
        DrawString(font, new Vector2(tag.Position.X + 4, tag.Position.Y + font.GetAscent(fontSize) + 1),
            text, fontSize: fontSize, modulate: Colors.White);
    }
}
