using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The top of a building's card: its roof as drawn on the map, its name, who
/// owns it, and small Find and Close buttons that the card wires up.
/// </summary>
public partial class BuildingHeader : HBoxContainer
{
    private string? roofKey;

    public BuildingHeader()
    {
        AddThemeConstantOverride("separation", 8);
        AddChild(Roof);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 4);
        NameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(NameLabel);
        top.AddChild(Find);
        top.AddChild(Close);
        text.AddChild(top);
        text.AddChild(OwnerLabel);
        AddChild(text);
    }

    public TextureRect Roof { get; } = new()
    {
        TextureFilter = TextureFilterEnum.Nearest,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SizeFlagsVertical = SizeFlags.ShrinkBegin,
    };

    public Label NameLabel { get; } = new() { ThemeTypeVariation = "HeadingLabel", ClipText = true };

    public Label OwnerLabel { get; } = new() { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };

    public Button Find { get; } = new();

    public Button Close { get; } = new();

    /// <summary>The roof's square size in interface pixels.</summary>
    public float RoofSize
    {
        get => Roof.CustomMinimumSize.X;
        init => Roof.CustomMinimumSize = new Vector2(value, value);
    }

    /// <summary>Redraws the roof only when its look changes.</summary>
    public void SetRoof(BuildingKind kind, Vector2I size, BuildingDoor door)
    {
        var key = $"{kind}|{size}|{door}";
        if (roofKey == key) return;
        roofKey = key;
        Roof.Texture = ImageTexture.CreateFromImage(BuildingSprites.Render(kind, size.X, size.Y, 32, door));
    }

    /// <summary>The approved unlit fitting, using the same orientation as the map.</summary>
    public void SetLantern(StreetLanternLight lantern)
    {
        var key = $"lantern|{lantern.Style}|{lantern.Edge}";
        if (roofKey == key) return;
        roofKey = key;
        var cells = NightLightShapes.StreetLantern(lantern.Style, lantern.Post, lantern.Inward, 0, 0, 0);
        var bounds = cells.Select(cell => cell.Area).Aggregate((first, next) => first.Merge(next));
        var size = (int)MathF.Ceiling(Math.Max(bounds.Size.X, bounds.Size.Y)) + 4;
        var offset = (Vector2.One * size - bounds.Size) / 2 - bounds.Position;
        using var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        foreach (var cell in cells)
        {
            var area = cell.Area with { Position = cell.Area.Position + offset };
            var start = (Vector2I)area.Position.Floor();
            var end = (Vector2I)area.End.Ceil();
            PixelArt.Fill(image, new Rect2I(start, end - start), cell.Color);
        }
        Roof.Texture = ImageTexture.CreateFromImage(image);
    }
}
