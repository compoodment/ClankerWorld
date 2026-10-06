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
    private readonly HBoxContainer capacityRow = new() { Name = "StorageSpace", Visible = false };
    private readonly PixelMeter capacityMeter = new() { Name = "StorageSpaceMeter", Kind = MeterKind.Progress, CaptionWidth = 0 };
    private readonly Label capacityText = new()
    {
        Name = "StorageSpaceText",
        ThemeTypeVariation = "DimLabel",
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };
    private string? renderedItems;

    public ItemStorage()
    {
        AddThemeConstantOverride("separation", 4);
        var heading = new HBoxContainer();
        heading.AddChild(titleLabel);
        heading.AddChild(summaryLabel);
        AddChild(heading);
        capacityRow.AddThemeConstantOverride("separation", 6);
        capacityRow.AddChild(capacityMeter);
        capacityRow.AddChild(capacityText);
        AddChild(capacityRow);
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

    /// <summary>A missing item list shows no inventory claim, including no empty-storage message.</summary>
    public void SetItems(IReadOnlyList<(string Kind, int Quantity, string DisplayName)>? items)
    {
        var recorded = items is not null;
        items ??= [];
        var total = items.Sum(item => item.Quantity);
        summaryLabel.Text = items.Count == 0 ? string.Empty : Named
            ? $"{Plural(items.Count, "kind")} · {Plural(total, "item")}"
            : Plural(total, "item");
        emptyLabel.Visible = recorded && items.Count == 0;
        slots.Visible = items.Count > 0;
        var signature = string.Join('|', items.Select(item => $"{item.Kind}:{item.Quantity}"));
        if (renderedItems == signature) return;
        renderedItems = signature;
        foreach (var child in slots.GetChildren())
        {
            slots.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var (kind, quantity, displayName) in items)
        {
            var slot = new ItemSlot { IconSize = 32, Named = Named };
            slot.SetItem(kind, quantity, displayName);
            slots.AddChild(slot);
        }
    }

    /// <summary>Shows only recorded storage occupancy; item rows may omit other owners' goods.</summary>
    public void SetCapacity(int? capacity, int storedQuantity)
    {
        var limit = capacity.GetValueOrDefault();
        capacityRow.Visible = limit > 0 && storedQuantity >= 0;
        if (!capacityRow.Visible) return;
        capacityMeter.Percent = (int)Math.Clamp((long)storedQuantity * 100 / limit, 0, 100);
        capacityText.Text = $"{storedQuantity} / {limit} used";
        capacityMeter.TooltipText = $"Storage used: {storedQuantity} of {limit} units.";
    }

    private static string Plural(int amount, string noun) =>
        $"{amount.ToString(CultureInfo.CurrentCulture)} {noun}{(amount == 1 ? "" : "s")}";
}
