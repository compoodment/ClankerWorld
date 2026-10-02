using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// A short choice such as Low, Normal or High shown as a row of linked
/// buttons, so every option is visible and the chosen one stays pressed. It
/// offers the parts of <see cref="OptionButton"/> the menus use.
/// </summary>
public partial class SegmentedChoice : PanelContainer
{
    [Signal]
    public delegate void ItemSelectedEventHandler(long index);

    private readonly HBoxContainer row = new();
    private readonly List<(Button Button, int Id)> items = [];

    public SegmentedChoice()
    {
        ThemeTypeVariation = "SegmentedPanel";
        row.AddThemeConstantOverride("separation", 2);
        AddChild(row);
    }

    public int ItemCount => items.Count;

    /// <summary>Whether every option can be reached with Tab and pressed with Enter.</summary>
    public bool KeyboardReachable => items.All(item => item.Button.FocusMode != FocusModeEnum.None);

    /// <summary>The pressed option's position, or -1 when none is chosen.</summary>
    public int Selected => items.FindIndex(item => item.Button.ButtonPressed);

    public void AddItem(string text, int id = -1)
    {
        var index = items.Count;
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            ThemeTypeVariation = "TabButton",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        // Pressing the chosen option again keeps it chosen.
        button.Pressed += () =>
        {
            Select(index);
            EmitSignal(SignalName.ItemSelected, index);
        };
        row.AddChild(button);
        items.Add((button, id < 0 ? index : id));
    }

    /// <summary>Chooses an option without reporting it, like <see cref="OptionButton.Select"/>.</summary>
    public void Select(int index)
    {
        for (var item = 0; item < items.Count; item++)
            items[item].Button.SetPressedNoSignal(item == index);
    }

    public int GetSelectedId() => Selected is >= 0 and var index ? items[index].Id : -1;

    public int GetItemIndex(int id) => items.FindIndex(item => item.Id == id);

    public void SetItemTooltip(int index, string tooltip) => items[index].Button.TooltipText = tooltip;

    public void SetItemText(int index, string text) => items[index].Button.Text = text;
}
