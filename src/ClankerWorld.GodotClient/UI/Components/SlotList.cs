using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>A small label on a world or save card, such as Current or Autosave.</summary>
/// <param name="Note">Drawn outlined and muted rather than filled, for notes such as "Can't open".</param>
public readonly record struct SlotTag(string Text, bool Note = false);

/// <summary>
/// A scrolling list of worlds or saves drawn as cards: a pixel icon, the name
/// in the Timber lettering, a line of detail and small tags. Click a card to
/// choose it; double-click it or press Enter to open it. It offers the parts
/// of <see cref="ItemList"/> the menus use.
/// </summary>
public partial class SlotList : ScrollContainer
{
    [Signal]
    public delegate void ItemSelectedEventHandler(long index);

    [Signal]
    public delegate void ItemActivatedEventHandler(long index);

    private readonly VBoxContainer cards = new();
    private readonly Label placeholder = new()
    {
        ThemeTypeVariation = "DimLabel",
        HorizontalAlignment = HorizontalAlignment.Center,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };
    private readonly List<PanelContainer> items = [];
    private readonly List<string> titles = [];
    private int selected = -1;
    private int hovered = -1;

    public SlotList()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        FocusMode = FocusModeEnum.All;
        cards.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cards.AddThemeConstantOverride("separation", 6);
        cards.AddChild(placeholder);
        AddChild(cards);
    }

    public int ItemCount => items.Count;

    /// <summary>Shown in place of the cards while the list is empty, such as "No saves yet."</summary>
    public string Placeholder
    {
        get => placeholder.Text;
        set
        {
            placeholder.Text = value;
            placeholder.Visible = items.Count == 0 && value.Length > 0;
        }
    }

    public string GetItemTitle(int index) => titles[index];

    public void Clear()
    {
        foreach (var card in items)
        {
            cards.RemoveChild(card);
            card.QueueFree();
        }
        items.Clear();
        titles.Clear();
        selected = -1;
        hovered = -1;
        placeholder.Visible = placeholder.Text.Length > 0;
    }

    /// <summary>Adds a card and returns its position.</summary>
    /// <param name="muted">Fades the card, for a world or save that can't be opened.</param>
    public int AddItem(string title, string detail = "", Texture2D? icon = null, IReadOnlyList<SlotTag>? tags = null, bool muted = false)
    {
        var index = items.Count;
        var card = new PanelContainer
        {
            ThemeTypeVariation = "InsetPanel",
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = detail,
        };
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        if (icon is not null)
            row.AddChild(new TextureRect
            {
                Texture = icon,
                StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 2);
        text.AddChild(new FittedLabel
        {
            FullText = title,
            ThemeTypeVariation = "HeadingLabel",
            MouseFilter = MouseFilterEnum.Ignore,
        });
        text.AddChild(new FittedLabel
        {
            FullText = detail,
            ThemeTypeVariation = "DimLabel",
            MouseFilter = MouseFilterEnum.Ignore,
        });
        row.AddChild(text);
        foreach (var tag in tags ?? [])
            row.AddChild(new Label
            {
                Text = tag.Text.ToUpperInvariant(),
                ThemeTypeVariation = tag.Note ? "TagNoteLabel" : "TagLabel",
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        if (muted) row.Modulate = new Color(1, 1, 1, 0.55f);
        card.AddChild(row);
        card.GuiInput += input => OnCardInput(index, input);
        card.MouseEntered += () => SetHovered(index);
        card.MouseExited += () => SetHovered(hovered == index ? -1 : hovered);
        cards.AddChild(card);
        items.Add(card);
        titles.Add(title);
        placeholder.Hide();
        return index;
    }

    /// <summary>Chooses a card without reporting it, like <see cref="ItemList.Select"/>.</summary>
    public void Select(int index)
    {
        selected = index >= 0 && index < items.Count ? index : -1;
        Restyle();
        if (selected < 0) return;
        // Scroll once the card is laid out; the list may have been refilled by then.
        var card = items[selected];
        Callable.From(() =>
        {
            if (IsInstanceValid(card) && card.IsInsideTree()) EnsureControlVisible(card);
        }).CallDeferred();
    }

    public int[] GetSelectedItems() => selected >= 0 ? [selected] : [];

    public override void _GuiInput(InputEvent @event)
    {
        if (items.Count == 0) return;
        if (@event.IsActionPressed("ui_down") || @event.IsActionPressed("ui_up"))
        {
            var step = @event.IsActionPressed("ui_down") ? 1 : -1;
            Choose(Math.Clamp(selected < 0 ? 0 : selected + step, 0, items.Count - 1));
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_accept") && selected >= 0)
        {
            EmitSignal(SignalName.ItemActivated, selected);
            AcceptEvent();
        }
    }

    private void OnCardInput(int index, InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click) return;
        GrabFocus();
        Choose(index);
        if (click.DoubleClick) EmitSignal(SignalName.ItemActivated, index);
        AcceptEvent();
    }

    private void Choose(int index)
    {
        Select(index);
        EmitSignal(SignalName.ItemSelected, index);
    }

    private void SetHovered(int index)
    {
        hovered = index;
        Restyle();
    }

    private void Restyle()
    {
        for (var index = 0; index < items.Count; index++)
            items[index].ThemeTypeVariation = index == selected ? "InsetPanelSelected"
                : index == hovered ? "InsetPanelHover" : "InsetPanel";
    }
}
