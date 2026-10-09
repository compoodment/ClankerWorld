using Godot;

namespace ClankerWorld.GodotClient.UI;

public static partial class BuildingSprites
{
    /// <summary>A transparent snow layer over actual roof pixels, excluding decorations and open yards.</summary>
    internal static Image SnowOverlay(BuildingKind kind, int width, int height, int size, BuildingDoor door,
        BuildingNeglect neglect, int seed)
    {
        width = Math.Clamp(width, 1, 8);
        height = Math.Clamp(height, 1, 8);
        // The Port and neglected artwork are drawn at 32 and reduced with nearest filtering.
        if (size != 32 && (kind == BuildingKind.Port || neglect != BuildingNeglect.None))
        {
            var full = SnowOverlay(kind, width, height, 32, door, neglect, seed);
            full.Resize(width * size, height * size, Image.Interpolation.Nearest);
            return full;
        }
        using var original = Render(kind, width, height, size, door);
        using var shown = Render(kind, width, height, size, door, neglect);
        var (bare, parts) = ApprovedArt.SnowRoofs(kind, width, height, size, door);
        using var roofOnly = bare;
        var overlay = Image.CreateEmpty(width * size, height * size, false, Image.Format.Rgba8);
        foreach (var (bounds, shape) in parts)
            for (var y = Math.Max(0, bounds.Position.Y + 1); y < Math.Min(overlay.GetHeight(), bounds.End.Y - 1); y++)
                for (var x = Math.Max(0, bounds.Position.X + 1); x < Math.Min(overlay.GetWidth(), bounds.End.X - 1); x++)
                {
                    var baseColor = original.GetPixel(x, y);
                    // Comparing the undecorated roof with the real sprite keeps flues,
                    // signs, doors, yards and shadows clear without guessing their colours.
                    if (baseColor.A < 0.99f || baseColor != roofOnly.GetPixel(x, y)) continue;
                    if (kind == BuildingKind.Silo && new Vector2(x - width * size / 2f, y - height * size / 2f).Length() <= size / 8f) continue;
                    var color = shown.GetPixel(x, y);
                    if (color.A < 0.99f || color.R * 0.3f + color.G * 0.59f + color.B * 0.11f < 0.2f) continue;
                    var amount = RoofSnowSprites.SlopeCover(x, y, size, shape,
                        (bounds.Position.X, bounds.Position.Y, bounds.End.X - 1, bounds.End.Y - 1), seed);
                    overlay.SetPixel(x, y, RoofSnowSprites.Tint(color, 1) with { A = amount });
                }
        return overlay;
    }

    private static partial class ApprovedArt
    {
        public static (Image Image, List<(Rect2I Bounds, RoofSnowShape Shape)> Parts) SnowRoofs(
            BuildingKind kind, int width, int height, int size, BuildingDoor door)
        {
            var plate = new Plate(width * size, height * size, size / 32f);
            var parts = new List<(Rect2I, RoofSnowShape)>();
            void Roof(Rect2I bounds, Recipe recipe)
            {
                PaintRoof(plate, bounds, recipe);
                parts.Add((bounds, recipe.Shape == RoofShape.Hip ? RoofSnowShape.Hipped :
                    bounds.Size.X >= bounds.Size.Y ? RoofSnowShape.GableEastWest : RoofSnowShape.GableNorthSouth));
            }
            var w = width * 32;
            var h = height * 32;
            if (kind == BuildingKind.Silo)
            {
                PaintSilo(plate, w, h);
                var radius = (Math.Min(w, h) / 2f - 3) * size / 32f;
                var left = (int)Math.Ceiling(width * size / 2f - radius);
                var top = (int)Math.Ceiling(height * size / 2f - radius);
                parts.Add((new(left, top, (int)(radius * 2) + 1, (int)(radius * 2) + 1), RoofSnowShape.Cone));
            }
            else if (kind == BuildingKind.MarketStall)
            {
                var area = new Rect2(4, 4, w - 8, h - 9);
                var across = door.Side is DoorSide.South or DoorSide.North;
                var along = across ? area.Size.X : area.Size.Y;
                var depth = MathF.Round((across ? area.Size.Y : area.Size.X) * 0.6f);
                var bounds = plate.Px(Orient(area, door.Side, 0, 0, along, depth));
                Awning(plate, bounds, door.Side, Berry);
                parts.Add((bounds, RoofSnowShape.Hipped));
            }
            else if (kind == BuildingKind.Port)
            {
                var frame = new PortFrame(PortLandSide(width, height, door.Side), w, h);
                Roof(plate.Px(frame.Map(9, 5, frame.Breadth - 18, 22)), new(Timber, Material.Shingle, RoofShape.Hip, 203));
            }
            else if (RecipeFor(kind) is { } recipe)
            {
                if (kind == BuildingKind.Market)
                {
                    var frame = new MarketFrame(Opposite(door.Side), w, h);
                    var hall = plate.Px(frame.Map(3, 3, frame.Breadth - 6, frame.Length - 21));
                    var arcade = plate.Px(frame.Map(4, frame.Length - 19, frame.Breadth - 8, 12));
                    var shadow = new Rect2I(hall.Position.X + plate.P(2), hall.Position.Y + plate.P(3), hall.Size.X, hall.Size.Y);
                    LeanTo(plate, arcade, door.Side, recipe with { Salt = 225 }, shadow);
                    parts.Add((arcade, RoofSnowShape.Hipped));
                    Roof(hall, recipe);
                }
                else
                {
                    if (kind == BuildingKind.Restaurant && (w <= 32 || h <= 32)) recipe = recipe with { Yard = 0 };
                    var plan = Lay(w, h, door, recipe, kind == BuildingKind.Warehouse ? 7f : kind == BuildingKind.TownHall ? 5f : 3f);
                    var roof = plate.Px(plan.Roof);
                    if (kind == BuildingKind.TownHall)
                    {
                        var (main, wings) = HallLayout(roof, door.Side);
                        Roof(wings, recipe with { Salt = recipe.Salt + 1 });
                        Roof(main, recipe);
                    }
                    else Roof(roof, recipe);
                }
            }
            return (plate.Image, parts);
        }
    }
}
