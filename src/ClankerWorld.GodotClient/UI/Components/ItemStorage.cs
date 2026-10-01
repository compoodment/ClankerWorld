using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// A titled grid of stored items, such as a building's STORAGE: each kind as
/// an <see cref="ItemSlot"/>, with how much is stored on the right of the title.
/// </summary>
public partial class ItemStorage : VBoxContainer
{
    private readonly Label titleLabel = new() { ThemeTypeVariation = "SectionLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label summaryLabel = new() { ThemeTypeVariation = "DimLabel" };
    private readonly GridContainer slots = new();
    private readonly Label emptyLabel = new() { Text = "Nothing stored here yet.", ThemeTypeVariation = "DimLabel" };
    private string? renderedItems;

    public ItemStorage()
    {
        AddThemeConstantOverride("separation", 4);
        var heading = new HBoxContainer();
        heading.AddChild(titleLabel);
        heading.AddChild(summaryLabel);
        AddChild(heading);
        slots.AddThemeConstantOverride("h_separation", 4);
        slots.AddThemeConstantOverride("v_separation", 4);
        AddChild(slots);
        AddChild(emptyLabel);
    }

    public string Title
    {
        get => titleLabel.Text;
        init => titleLabel.Text = value;
    }

    public int Columns
    {
        get => slots.Columns;
        init => slots.Columns = value;
    }

    /// <summary>Whether each slot names its item underneath; the summary then also counts kinds.</summary>
    public bool Named { get; init; }

    /// <summary>The number of slots shown, for checks.</summary>
    public int SlotCount => slots.GetChildCount();

    public string Summary => summaryLabel.Text;

    public void SetItems(IReadOnlyList<(string Kind, int Quantity, string DisplayName)> items,
        IReadOnlyDictionary<string, string>? details = null)
        => SetItems(items.Select(item => (item.Kind, item.Quantity, item.DisplayName, (int?)null, 0)).ToArray(), details);

    public void SetItems(IReadOnlyList<(string Kind, int Quantity, string DisplayName, int? Condition, int Broken)> items,
        IReadOnlyDictionary<string, string>? details = null)
    {
        var total = items.Sum(item => item.Quantity);
        summaryLabel.Text = items.Count == 0 ? string.Empty : Named
            ? $"{Plural(items.Count, "kind")} · {Plural(total, "item")}"
            : Plural(total, "item");
        emptyLabel.Visible = items.Count == 0;
        slots.Visible = items.Count > 0;
        var signature = string.Join('|', items.Select(item => $"{item.Kind}:{item.Quantity}:{item.Condition}:{item.Broken}:{details?.GetValueOrDefault(item.Kind)}"));
        if (renderedItems == signature) return;
        renderedItems = signature;
        foreach (var child in slots.GetChildren())
        {
            slots.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var (kind, quantity, displayName, condition, broken) in items)
        {
            var slot = new ItemSlot { IconSize = 32, Named = Named };
            slot.SetItem(kind, quantity, displayName);
            if (condition is { } value) slot.TooltipText += $" · condition {value / 100}%";
            if (broken > 0) slot.TooltipText += $" · {broken} broken";
            if (details?.TryGetValue(kind, out var detail) == true) slot.TooltipText += " · " + detail;
            slots.AddChild(slot);
        }
    }

    private static string Plural(int amount, string noun) =>
        $"{amount.ToString(CultureInfo.CurrentCulture)} {noun}{(amount == 1 ? "" : "s")}";
}
