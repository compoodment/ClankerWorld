using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// A fixed-size map hit target; unlike Button, text/theme minima cannot enlarge it.
/// It draws the agent facing the way they last moved, steps through the walk
/// frames while their position changes, and otherwise shows what the latest
/// observation says they are doing. It only presents: the host decides where
/// agents go and what they do.
/// </summary>
public partial class AgentMarker : Control
{
    private string caption = string.Empty;
    private bool selected;
    private bool hovered;
    private bool conversationBadgeVisible;
    private bool conversationUnread;
    private int variant;
    private int stage = 2;
    private int facing = AgentSprites.South;
    private AgentFrame activity;
    private bool swimming;
    private double swimmingSeconds;
    private AgentFrame step;
    private double stepSecondsLeft;
    private string? motionWorldId;
    private Vector2I? lastTile;
    private bool modelWaiting;
    private double modelWaitSeconds;
    private string? modelWaitWorldId;

    public const double WaitingMarkerDelaySeconds = 2;
    public bool WaitingMarkerVisible => modelWaiting && modelWaitSeconds >= WaitingMarkerDelaySeconds;
    private int WaitingFrame => (int)(Math.Max(0, modelWaitSeconds - WaitingMarkerDelaySeconds) * 8) % 8;

    private const float SpriteScale = 1.35f;
    private const float NameMinimum = 20;

    /// <summary>
    /// How long the last walk frame stays after a step. Observations arrive
    /// about once a second, so an agent who keeps walking alternates walk
    /// frames on every step and stands still again once they stop.
    /// </summary>
    public const double StepSeconds = 1.5;

    /// <summary>A position change longer than this many tiles is a relocation, such as a reload, not a step.</summary>
    public const int MaxStepTiles = 3;

    /// <summary>Illness from this level on slows work and travel, so the agent shows as hurt.</summary>
    public const int HurtIllnessBasisPoints = 2_500;

    // Moving goods to where they are needed: carry while holding something.
    private static readonly string[] HaulingIntentions =
    [
        "haul_farm_grain", "haul_farm_flour", "haul_smith_input", "haul_household_stock", "store_town_resources",
        "store_household_food", "deliver_smith_ore", "supply_workstation:", "assist:", "tend_fire", "child_help_food",
    ];

    // Talking with someone: offers, answers, lessons and shared knowledge.
    private static readonly string[] ConversationIntentions =
    [
        "child_converse:", "child_learn:", "knowledge_share:", "trade_", "partner_", "parent_", "council_", "learn:",
        "lesson_", "guardian_offer:", "guardian_accept:", "guardian_refuse:", "guardian_end:",
    ];

    // Hands-on work at a site. Building counts only while its project is being worked on.
    private static readonly string[] WorkIntentions =
    [
        "harvest_food", "plant_tree", "replant_tree", "gather_smith_ore", "gather_building_material:",
    ];

    // Worn or held equipment is not a load.
    private static readonly HashSet<string> EquipmentKinds = new(StringComparer.Ordinal)
    {
        "clothing", "tool", "wooden_axe", "wooden_pickaxe", "crude_wooden_axe", "crude_wooden_pickaxe",
    };

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

    /// <summary>Which way the agent faces, in <see cref="AgentSprites"/> order; south until they first move.</summary>
    public int Facing => facing;

    /// <summary>What the agent is doing, from <see cref="ActivityFor"/>; shown while they stand.</summary>
    public AgentFrame Activity
    {
        get => activity;
        set { if (activity != value) { activity = value; QueueRedraw(); } }
    }

    /// <summary>
    /// The frame drawn now. Swimming alternates existing work and still poses.
    /// On land, hurt and carrying show mid-step; otherwise a moving agent
    /// shows a walk frame and a standing agent shows their activity.
    /// </summary>
    public bool Swimming
    {
        get => swimming;
        set
        {
            if (swimming == value) return;
            swimming = value;
            swimmingSeconds = 0;
            SetProcess(swimming || step != AgentFrame.Still || modelWaiting);
            QueueRedraw();
        }
    }

    public AgentFrame Frame => swimming ? activity == AgentFrame.Hurt ? AgentFrame.Hurt
        : (int)(swimmingSeconds * 2) % 2 == 0 ? AgentFrame.Work : AgentFrame.Still
        : activity is AgentFrame.Hurt or AgentFrame.Carry ? activity
        : step != AgentFrame.Still ? step : activity;

    /// <summary>
    /// Selected or hovered agents show their name; swimmers show an activity
    /// label. Text is drawn once the figure is large enough to read.
    /// </summary>
    public bool NameShown => swimming || (selected || hovered) && caption.Length > 0;

    public AgentMarker()
    {
        MouseFilter = MouseFilterEnum.Pass;
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 10;
        MouseEntered += () => { hovered = true; Raise(); };
        MouseExited += () => { hovered = false; Raise(); };
    }

    public override void _Ready() => SetProcess(swimming || step != AgentFrame.Still || modelWaiting);

    /// <summary>A slow model reply gets a local marker; pause, completion and another world clear it immediately.</summary>
    public void ObserveModelWait(string worldId, bool waiting, bool paused)
    {
        waiting &= !paused;
        if (modelWaitWorldId == worldId && modelWaiting == waiting) return;
        modelWaitWorldId = worldId;
        modelWaiting = waiting;
        modelWaitSeconds = 0;
        SetProcess(swimming || step != AgentFrame.Still || modelWaiting);
        QueueRedraw();
    }

    /// <summary>
    /// Records the agent's tile from an observation. A change of tile turns the
    /// agent toward the move (the shorter way round a world that wraps east and
    /// west) and shows the next walk frame; standing keeps the last facing. A
    /// new world, or a jump longer than <see cref="MaxStepTiles"/>, starts over
    /// without a step.
    /// </summary>
    public void ObserveTile(string worldId, Vector2I tile, int mapWidth, bool wrapsEastWest)
    {
        if (!string.Equals(motionWorldId, worldId, StringComparison.Ordinal))
        {
            motionWorldId = worldId;
            lastTile = tile;
            facing = AgentSprites.South;
            EndStep();
            QueueRedraw();
            return;
        }
        if (lastTile is not { } previous || previous == tile)
        {
            lastTile = tile;
            return;
        }
        lastTile = tile;
        var dx = tile.X - previous.X;
        var dy = tile.Y - previous.Y;
        if (wrapsEastWest && mapWidth > 0) dx -= (int)Math.Round(dx / (double)mapWidth) * mapWidth;
        if ((dx == 0 && dy == 0) || Math.Max(Math.Abs(dx), Math.Abs(dy)) > MaxStepTiles) return;
        facing = AgentSprites.FacingToward(dx, dy);
        step = step == AgentFrame.Walk1 ? AgentFrame.Walk2 : AgentFrame.Walk1;
        stepSecondsLeft = StepSeconds;
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (step != AgentFrame.Still)
        {
            stepSecondsLeft -= delta;
            if (stepSecondsLeft <= 0) EndStep();
        }
        if (swimming)
        {
            swimmingSeconds += delta;
            QueueRedraw();
        }
        if (!modelWaiting) return;
        var visible = WaitingMarkerVisible;
        var frame = WaitingFrame;
        modelWaitSeconds += delta;
        if (visible != WaitingMarkerVisible || WaitingMarkerVisible && frame != WaitingFrame) QueueRedraw();
    }

    private void EndStep()
    {
        if (step != AgentFrame.Still) QueueRedraw();
        step = AgentFrame.Still;
        stepSecondsLeft = 0;
        SetProcess(swimming || modelWaiting);
    }

    /// <summary>
    /// What an agent is doing, read only from facts the observation already
    /// carries: illness, the current intention, carried goods and the stage of
    /// their project. Anything not recognized shows the still frame.
    /// </summary>
    public static AgentFrame ActivityFor(OwnerWorldInhabitant inhabitant)
    {
        if (inhabitant.Survival?.IllnessBasisPoints >= HurtIllnessBasisPoints) return AgentFrame.Hurt;
        var intention = inhabitant.PublicIntention?.CandidateId ?? string.Empty;
        if (Matches(intention, HaulingIntentions) &&
            inhabitant.Inventory.Any(item => item.Quantity > 0 && !EquipmentKinds.Contains(item.Kind)))
            return AgentFrame.Carry;
        if (Matches(intention, ConversationIntentions)) return AgentFrame.Talk;
        if (Matches(intention, WorkIntentions) ||
            (intention.StartsWith("build:", StringComparison.Ordinal) &&
             string.Equals(inhabitant.Project?.Stage, "working", StringComparison.Ordinal)))
            return AgentFrame.Work;
        return AgentFrame.Still;
    }

    // Intentions match by their whole ID, or by an ID prefix ending in ':' or '_'.
    private static bool Matches(string intention, string[] patterns) => patterns.Any(pattern =>
        pattern[^1] is ':' or '_' ? intention.StartsWith(pattern, StringComparison.Ordinal)
            : string.Equals(intention, pattern, StringComparison.Ordinal));

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

    private void DrawWaitingSpark(float side)
    {
        if (!WaitingMarkerVisible) return;
        foreach (var (position, color) in AgentSprites.WaitingSparkFrame(Math.Clamp((int)side, 4, 78), WaitingFrame))
            DrawRect(new Rect2(new Vector2(position.X, position.Y), Vector2.One), color);
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
            DrawWaitingSpark(side);
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
        var frame = Frame;
        DrawTextureRectRegion(AgentSprites.PoseAtlas(variant, stage, facing, frame, atlasSize), sprite,
            AgentSprites.PoseRegion(facing, frame, atlasSize));
        DrawConversationBadge();
        DrawWaitingSpark(side);
        if (!NameShown || drawn < NameMinimum) return;
        var font = UiFonts.Text;
        var fontSize = UiFonts.Body * TextScale;
        var text = swimming ? "Swimming" : caption.Length > 14 ? caption[..13] + "..." : caption;
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
