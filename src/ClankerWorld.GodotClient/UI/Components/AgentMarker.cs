using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>A fixed-size map hit target; unlike Button, text/theme minima cannot enlarge it.</summary>
public partial class AgentMarker : Control
{
    private string caption = string.Empty;
    private bool selected;
    private bool hovered;
    private bool conversationBadgeVisible;
    private bool conversationUnread;
    private int variant;
    private int stage = 2;

    private const float SpriteScale = 1.35f;
    private const float NameMinimum = 20;
    private static readonly Color SelectedColor = new("FFD166");
    private static readonly Color HoveredColor = new("FFF0B5");
    private static readonly Color NameColor = new("FFF6E0");
    private static readonly Color NameEdge = new("1E1712");

    public event Action? Activated;
    public event Action? ConversationActivated;

    /// <summary>Names grow with UI Scale, in whole steps so the pixel font stays crisp.</summary>
    public static int TextScale { get; set; } = 1;

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

    public bool ConversationBadgeVisible
    {
        get => conversationBadgeVisible;
        set { if (conversationBadgeVisible != value) { conversationBadgeVisible = value; QueueRedraw(); } }
    }

    /// <summary>Unread indication is local to this player and is never sent to agents.</summary>
    public bool ConversationUnread
    {
        get => conversationUnread;
        set { if (conversationUnread != value) { conversationUnread = value; QueueRedraw(); } }
    }

    public Rect2 ConversationBadgeBounds
    {
        get
        {
            var side = Math.Min(Size.X, Size.Y);
            var diameter = Math.Min(side, Math.Clamp(side * 0.42f, 4f, 13f));
            return new Rect2(Size.X - diameter, 0, diameter, diameter);
        }
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

    /// <summary>
    /// The name shows only while the agent is selected or hovered, so a busy
    /// Town stays clear. It is drawn once the figure is large enough to read.
    /// </summary>
    public bool NameShown => (selected || hovered) && caption.Length > 0;

    public AgentMarker()
    {
        MouseFilter = MouseFilterEnum.Pass;
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 10;
        MouseEntered += () => { hovered = true; Raise(); };
        MouseExited += () => { hovered = false; Raise(); };
    }

    // A hovered or selected agent draws over its neighbors so its name stays readable.
    private void Raise()
    {
        ZIndex = selected || hovered ? 11 : 10;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
        {
            if (ConversationBadgeVisible && ConversationBadgeBounds.HasPoint(mouseButton.Position))
                ConversationActivated?.Invoke();
            else
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
                DrawCircle(center, Math.Max(2.5f, side / 2 + 1.5f), selected ? SelectedColor : HoveredColor);
            DrawCircle(center, Math.Max(1.5f, side / 2), new Color("1E2226"));
            DrawCircle(center, Math.Max(1f, side / 2 - 1), new Color("F4C78A"));
            DrawConversationBadge();
            return;
        }

        // The figure fills about two thirds of its sprite cell, so the cell is
        // drawn larger than the bounded hit target to keep the person readable.
        var drawn = side * SpriteScale;
        var sprite = new Rect2(center - new Vector2(drawn, drawn) / 2, new Vector2(drawn, drawn));
        if (selected || hovered)
        {
            DrawArc(center + new Vector2(0, drawn * 0.06f), drawn * 0.36f, 0, Mathf.Tau, 32,
                selected ? SelectedColor : HoveredColor, Math.Max(1.5f, drawn / 18f));
        }
        var atlasSize = drawn >= 24 ? 32 : 16;
        DrawTextureRectRegion(AgentSprites.Atlas(atlasSize), sprite, AgentSprites.Region(variant, stage, atlasSize));
        DrawConversationBadge();
        if (!NameShown || drawn < NameMinimum) return;
        var font = UiFonts.Text;
        var fontSize = UiFonts.Body * TextScale;
        var text = caption.Length > 14 ? caption[..13] + "…" : caption;
        var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
        // Light letters with a dark pixel edge and no box, gold for the selected agent.
        var baseline = new Vector2(Mathf.Floor(center.X - textSize.X / 2),
            Mathf.Floor(sprite.Position.Y + drawn * 0.86f) + font.GetAscent(fontSize));
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx != 0 || dy != 0)
                    DrawString(font, baseline + new Vector2(dx, dy) * TextScale, text, fontSize: fontSize, modulate: NameEdge);
            }
        }
        DrawString(font, baseline, text, fontSize: fontSize, modulate: selected ? SelectedColor : NameColor);
    }

    private void DrawConversationBadge()
    {
        if (!ConversationBadgeVisible) return;
        var bounds = ConversationBadgeBounds;
        var center = bounds.GetCenter();
        var radius = bounds.Size.X * 0.43f;
        DrawCircle(center, radius + 1, new Color("1E1712"));
        DrawCircle(center, radius, ConversationUnread ? new Color("FFD166") : new Color("F4E7BE"));
        var dotRadius = Math.Max(0.65f, radius * 0.13f);
        for (var index = -1; index <= 1; index++)
            DrawCircle(center + new Vector2(index * radius * 0.45f, 0), dotRadius, new Color("493522"));
        DrawLine(center + new Vector2(-radius * 0.35f, radius * 0.62f),
            center + new Vector2(-radius * 0.62f, radius * 0.94f), new Color("1E1712"), Math.Max(1, radius * 0.3f));
    }
}
