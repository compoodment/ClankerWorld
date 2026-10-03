using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Visual family of a placed building or legacy camp object.</summary>
public enum BuildingKind : byte
{
    House,
    Warehouse,
    Farmhouse,
    Blacksmith,
    Silo,
    Shelter,
    Storehouse,
    Hearth,
    TailorShop,
    Store,
    Workshop,
    Path,
    Bedroll,
    Generic,
    TownHall,
}

/// <summary>The edge of a building's footprint that its door is on.</summary>
public enum DoorSide : byte
{
    South,
    North,
    East,
    West,
}

/// <summary>
/// Where a building's door is: its side, and the footprint tile along that
/// side (counted from the left or top), or null for the middle of the side.
/// The default is the middle of the south side.
/// </summary>
public readonly record struct BuildingDoor(DoorSide Side, int? Tile = null)
{
    /// <summary>A building without a recorded entrance: the middle of the south side.</summary>
    public static BuildingDoor Default => new(DoorSide.South);

    /// <summary>The door facing an entrance tile beside the footprint.</summary>
    public static BuildingDoor Facing(Rect2I footprint, Vector2I? entrance)
    {
        if (entrance is not { } tile) return Default;
        var column = tile.X >= footprint.Position.X && tile.X < footprint.End.X;
        var row = tile.Y >= footprint.Position.Y && tile.Y < footprint.End.Y;
        if (column && tile.Y == footprint.End.Y) return new(DoorSide.South, tile.X - footprint.Position.X);
        if (column && tile.Y == footprint.Position.Y - 1) return new(DoorSide.North, tile.X - footprint.Position.X);
        if (row && tile.X == footprint.End.X) return new(DoorSide.East, tile.Y - footprint.Position.Y);
        if (row && tile.X == footprint.Position.X - 1) return new(DoorSide.West, tile.Y - footprint.Position.Y);
        return Default;
    }
}

/// <summary>
/// Top-down pixel-art roofs for placed buildings, generated at their
/// footprint size (32 or 16 px per tile). Each design has one standard
/// appearance, per the vision ledger: roofs show the building's family, the
/// ridge follows its long side, and a door or doorstep marks the side the
/// building faces its Road from. Every current kind uses the drawing the
/// owner approved in the art review (<see cref="ApprovedArt"/>); only the
/// retired camp objects keep their earlier provisional drawing.
/// </summary>
public static class BuildingSprites
{
    private static readonly Dictionary<(BuildingKind Kind, int Width, int Height, int Tile, BuildingDoor Door), ImageTexture> Cache = [];
    private static readonly Color Shadow = new(0.04f, 0.06f, 0.05f, 0.30f);

    /// <summary>Chooses a family from building tags, most specific first.</summary>
    public static BuildingKind KindFor(IReadOnlyList<string>? tags)
    {
        var set = tags ?? [];
        bool Has(string tag) => set.Contains(tag, StringComparer.Ordinal);
        if (Has("house")) return BuildingKind.House;
        if (Has("warehouse")) return BuildingKind.Warehouse;
        if (Has("farmhouse")) return BuildingKind.Farmhouse;
        if (Has("blacksmith")) return BuildingKind.Blacksmith;
        if (Has("silo")) return BuildingKind.Silo;
        if (Has("workshop")) return BuildingKind.Workshop;
        if (Has("tailor")) return BuildingKind.TailorShop;
        if (Has("store")) return BuildingKind.Store;
        if (Has("town_hall")) return BuildingKind.TownHall;
        if ((Has("cooking") || Has("warmth")) && !Has("shelter")) return BuildingKind.Hearth;
        if (Has("storage")) return BuildingKind.Storehouse;
        if (Has("shelter")) return BuildingKind.Shelter;
        return BuildingKind.Generic;
    }

    /// <summary>Legacy camp objects that have a building look; others keep their text marker.</summary>
    public static BuildingKind? KindForObject(string kind) => kind switch
    {
        "campfire" or "cooking" => BuildingKind.Hearth,
        "path" => BuildingKind.Path,
        "bedroll" => BuildingKind.Bedroll,
        "shelter" => BuildingKind.Shelter,
        "storage" => BuildingKind.Storehouse,
        "workshop" => BuildingKind.Workshop,
        _ => null,
    };

    /// <summary>Main roof color, also used as a flat fill at overview zoom.</summary>
    public static Color RoofColor(BuildingKind kind) => ApprovedArt.MainRoof(kind) ?? Palette(kind).Lit;

    public static int AtlasTileSize(int drawnTileSize) => drawnTileSize >= 24 ? 32 : 16;

    public static ImageTexture Texture(BuildingKind kind, int width, int height, int tilePixels, BuildingDoor door = default)
    {
        width = Math.Clamp(width, 1, 8);
        height = Math.Clamp(height, 1, 8);
        var key = (kind, width, height, tilePixels, door);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(Render(kind, width, height, tilePixels, door));
        Cache[key] = texture;
        return texture;
    }

    public static Image Render(BuildingKind kind, int width, int height, int tilePixels, BuildingDoor door = default)
    {
        if (ApprovedArt.Draws(kind)) return ApprovedArt.Draw(kind, width, height, tilePixels, door);
        var image = Image.CreateEmpty(width * tilePixels, height * tilePixels, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        Paint(new PixelCanvas(image, new Rect2I(0, 0, width * tilePixels, height * tilePixels), tilePixels / 32f),
            kind, width * 32, height * 32, door);
        return image;
    }

    private static (Color Lit, Color Shade, Color Edge, Color Ridge) Palette(BuildingKind kind) => kind switch
    {
        BuildingKind.Shelter => (new Color("8C8A4E"), new Color("6D6B3C"), new Color("403F22"), new Color("A8A564")),
        BuildingKind.Storehouse => (new Color("8E6C47"), new Color("6E5236"), new Color("3F2E1F"), new Color("AC8A60")),
        BuildingKind.Path => (new Color("A89F8C"), new Color("857C69"), new Color("5F5848"), new Color("C4BBA6")),
        BuildingKind.Bedroll => (new Color("A0523E"), new Color("7E3F30"), new Color("4A2A20"), new Color("C97A5E")),
        _ => (new Color("8D8577"), new Color("6E675C"), new Color("3F3A33"), new Color("AAA293")),
    };

    /// <summary>The earlier provisional drawing, kept for the retired camp objects.</summary>
    private static void Paint(PixelCanvas canvas, BuildingKind kind, int width, int height, BuildingDoor door)
    {
        switch (kind)
        {
            case BuildingKind.Hearth:
                PaintHearth(canvas, width, height);
                return;
            case BuildingKind.Path:
                PaintPath(canvas, width, height);
                return;
            case BuildingKind.Bedroll:
                PaintBedroll(canvas, width, height);
                return;
        }

        var palette = Palette(kind);
        const float inset = 3;
        var roofWidth = width - inset * 2;
        var roofHeight = height - inset * 2 - 2;
        canvas.Rect(inset + 2, inset + 3, roofWidth, roofHeight, Shadow);
        canvas.Rect(inset, inset, roofWidth, roofHeight, palette.Edge);
        canvas.Rect(inset + 1, inset + 1, roofWidth - 2, roofHeight - 2, palette.Lit);

        // Gabled roofs run their ridge along the long side, the far half lit
        // and the near half shaded, so every roof reads as pitched from
        // straight above. A shelter is a hipped hide-and-pole tent instead.
        var horizontal = roofWidth >= roofHeight;
        if (kind == BuildingKind.Shelter)
        {
            canvas.Hipped(inset + 1, inset + 1, roofWidth - 2, roofHeight - 2,
                palette.Lit, palette.Shade.Lightened(0.08f), palette.Shade, palette.Lit.Darkened(0.08f));
            canvas.Disc(inset + roofWidth / 2f, inset + roofHeight / 2f - 0.5f, 1.2f, palette.Edge);
        }
        else if (horizontal)
        {
            var ridge = inset + roofHeight / 2;
            canvas.Rect(inset + 1, ridge, roofWidth - 2, inset + roofHeight - 1 - ridge, palette.Shade);
            for (var row = inset + 4; row < inset + roofHeight - 2; row += 4)
                if (Math.Abs(row - ridge) > 1) canvas.Rect(inset + 1, row, roofWidth - 2, 1, palette.Edge with { A = 0.28f });
            canvas.Rect(inset + 1, ridge - 1, roofWidth - 2, 1, palette.Ridge);
        }
        else
        {
            var ridge = inset + roofWidth / 2;
            canvas.Rect(ridge, inset + 1, inset + roofWidth - 1 - ridge, roofHeight - 2, palette.Shade);
            for (var column = inset + 4; column < inset + roofWidth - 2; column += 4)
                if (Math.Abs(column - ridge) > 1) canvas.Rect(column, inset + 1, 1, roofHeight - 2, palette.Edge with { A = 0.28f });
            canvas.Rect(ridge - 1, inset + 1, 1, roofHeight - 2, palette.Ridge);
        }

        var roof = new Rect2(inset, inset, roofWidth, roofHeight);
        if (kind == BuildingKind.Storehouse) DoorBand(canvas, roof, door, 6, 0, new Color("4A3321"));
        // A building that faces a Road gets the start of its doorstep path,
        // which the Road's own doorstep path continues.
        if (door.Tile is not null && kind != BuildingKind.Shelter)
            PathToEdge(canvas, roof, door, width, height);
    }

    private static void PathToEdge(PixelCanvas canvas, Rect2 roof, BuildingDoor door, int width, int height)
    {
        var middle = DoorMiddle(roof, door);
        var (from, to) = door.Side switch
        {
            DoorSide.North => (0f, roof.Position.Y - 2),
            DoorSide.East => (roof.End.X + 2, (float)width),
            DoorSide.West => (0f, roof.Position.X - 2),
            _ => (roof.End.Y + 2, (float)height),
        };
        if (to <= from) return;
        if (door.Side is DoorSide.North or DoorSide.South)
        {
            canvas.Rect(middle - 3.5f, from, 7, to - from, RoadSprites.WornEdge);
            canvas.Rect(middle - 2.5f, from, 5, to - from, RoadSprites.Dirt);
        }
        else
        {
            canvas.Rect(from, middle - 3.5f, to - from, 7, RoadSprites.WornEdge);
            canvas.Rect(from, middle - 2.5f, to - from, 5, RoadSprites.Dirt);
        }
    }

    /// <summary>
    /// A strip across the door, <paramref name="across"/> pixels wide, that
    /// overlaps the roof edge by one pixel and reaches two beyond it.
    /// <paramref name="outward"/> picks how far out from the roof edge it starts.
    /// </summary>
    private static void DoorBand(PixelCanvas canvas, Rect2 roof, BuildingDoor door, float across, float outward, Color color)
    {
        const float depth = 3;
        var middle = DoorMiddle(roof, door);
        var start = middle - across / 2f;
        switch (door.Side)
        {
            case DoorSide.South:
                canvas.Rect(start, roof.End.Y - 1 + outward, across, depth, color);
                break;
            case DoorSide.North:
                canvas.Rect(start, roof.Position.Y - 2 + (2 - outward - depth + 1), across, depth, color);
                break;
            case DoorSide.East:
                canvas.Rect(roof.End.X - 1 + outward, start, depth, across, color);
                break;
            case DoorSide.West:
                canvas.Rect(roof.Position.X - 2 + (2 - outward - depth + 1), start, depth, across, color);
                break;
        }
    }

    /// <summary>The door's position along its side, kept clear of the roof corners.</summary>
    private static float DoorMiddle(Rect2 roof, BuildingDoor door)
    {
        var horizontal = door.Side is DoorSide.South or DoorSide.North;
        var from = horizontal ? roof.Position.X : roof.Position.Y;
        var length = horizontal ? roof.Size.X : roof.Size.Y;
        var middle = door.Tile is { } tile ? tile * 32 + 16 : from + length / 2f;
        return Math.Clamp(middle, from + 5, from + length - 5);
    }

    private static void PaintHearth(PixelCanvas canvas, int width, int height)
    {
        var cx = width / 2f;
        var cy = height / 2f;
        canvas.Ellipse(cx + 1, cy + 2, 10, 8, Shadow);
        for (var stone = 0; stone < 10; stone++)
        {
            var angle = stone * Mathf.Tau / 10;
            canvas.Disc(cx + Mathf.Cos(angle) * 8, cy + Mathf.Sin(angle) * 7, 2.2f, new Color("7E7A72"));
            canvas.Dot(cx + Mathf.Cos(angle) * 8 - 0.6f, cy + Mathf.Sin(angle) * 7 - 0.6f, new Color("A9A59B"));
        }
        canvas.Disc(cx, cy, 5.5f, new Color("3B2A1E"));
        canvas.Lumpy(cx, cy, 4.5f, new Color("E0662A"), 5, 1);
        canvas.Lumpy(cx, cy - 0.5f, 2.8f, new Color("F5A742"), 4, 2);
        canvas.Disc(cx, cy - 0.5f, 1.2f, new Color("FFE08A"));
    }

    /// <summary>Flat, uneven flagstones laid across the footprint; a path has no roof.</summary>
    private static void PaintPath(PixelCanvas canvas, int width, int height)
    {
        var palette = Palette(BuildingKind.Path);
        for (var row = 0; row * 10 + 4 < height - 3; row++)
            for (var column = 0; column * 10 + 3 < width - 3; column++)
            {
                var offset = row % 2 == 0 ? 0 : 5;
                var x = 3 + column * 10 + offset + (int)(PixelArt.Hash(column, row, 57) % 2);
                var y = 4 + row * 10 + (int)(PixelArt.Hash(column, row, 58) % 2);
                if (x + 7 > width - 2) continue;
                canvas.Rect(x + 1, y + 1, 8, 7, Shadow);
                canvas.Rect(x, y, 8, 7, palette.Edge);
                canvas.Rect(x + 1, y, 6, 6, palette.Lit);
                canvas.Rect(x + 1, y + 4, 6, 2, palette.Shade);
                canvas.Dot(x + 2, y + 1, palette.Ridge);
            }
    }

    /// <summary>A blanket rolled out on the ground with a folded head end.</summary>
    private static void PaintBedroll(PixelCanvas canvas, int width, int height)
    {
        var palette = Palette(BuildingKind.Bedroll);
        var cx = width / 2f;
        var cy = height / 2f;
        canvas.Rect(cx - 7, cy - 11, 16, 24, Shadow);
        canvas.Rect(cx - 8, cy - 12, 16, 24, palette.Edge);
        canvas.Rect(cx - 7, cy - 11, 14, 22, palette.Lit);
        canvas.Rect(cx - 7, cy + 3, 14, 8, palette.Shade);
        canvas.Rect(cx - 7, cy - 11, 14, 5, new Color("E4DAC2"));
        canvas.Rect(cx - 7, cy - 7, 14, 1, new Color("B9AB8E"));
        for (var stripe = cy - 4; stripe < cy + 10; stripe += 4)
            canvas.Rect(cx - 7, stripe, 14, 1, palette.Ridge);
    }

    /// <summary>
    /// The building exteriors the owner approved in the art review
    /// (2026-10-01): House, Warehouse, Blacksmith, Silo and Tailor shop from
    /// the first round, and the Farmhouse, Workshop and generic building from
    /// the second, at any footprint and door side. The layout keeps the
    /// earlier contract: the roof sits three units in from the footprint, the
    /// door is on the side that faces the Road, and the doorstep path starts at
    /// the edge. Each roof is laid in a real material (clay tiles, thatch,
    /// slate, planks, shingles) in courses that follow its eaves, lit from the
    /// north-west, with ridge and hip lines, an eave shadow, a visible door
    /// over a stone doorstep, and one identifying feature per kind. Layouts
    /// are written in 32-unit tile space and drawn at 32 or 16 px per tile.
    /// </summary>
    private static class ApprovedArt
    {
        // Every colour is a step of an art style guide ramp.
        private static readonly Ramp Clay = Ramp.Of("5E2E22", "9E4E34", "C66A45", "E08E64", "EFA982");
        private static readonly Ramp Thatch = Ramp.Of("6B5528", "A98A45", "D2AE5E", "E6C77B", "F0DA9A");
        private static readonly Ramp Slate = Ramp.Of("2B2E33", "4A4E55", "62666E", "80858E", "9A9FA7");
        private static readonly Ramp GreyTimber = Ramp.Of("343C43", "59656F", "758390", "97A5B0", "AEBBC4");
        private static readonly Ramp SiloWood = Ramp.Of("54462F", "8E7A58", "B7A07A", "D3C09A", "E4D4B4");
        private static readonly Ramp DyedShingle = Ramp.Of("3E2B47", "6E4F7C", "8F6A9E", "B08CBE", "C8A8D4");
        private static readonly Ramp GreenPlank = Ramp.Of("30372D", "566150", "6F7C6A", "8E9B88", "A8B4A2");
        /// <summary>
        /// A plain building the game cannot name: the first four steps are the
        /// earlier generic palette; the highlight is one step on, toward Cloth
        /// highlight, because the style guide has no row for it.
        /// </summary>
        private static readonly Ramp Plain = Ramp.Of("3F3A33", "6E675C", "8D8577", "AAA293", "C2BBAE");
        private static readonly Ramp Timber = Ramp.Of("3F2A1A", "6E4E31", "8A6440", "A77C52", "D2AC77");
        private static readonly Ramp Doorstep = Ramp.Of("5F5848", "8C7F66", "B9AB8E", "C9BDA2", "DED3BC");
        private static readonly Ramp Rock = Ramp.Of("4A4542", "625B56", "756D68", "8B837D", "A49C95");
        private static readonly Ramp Iron = Ramp.Of("3E3A37", "524C48", "6C6560", "8A827C", "A69E98");
        private static readonly Ramp Cloth = Ramp.Of("75674D", "A09170", "CABC99", "E8DCC0", "FFF5DF");
        private static readonly Ramp Gold = Ramp.Of("8A6A1E", "B8902E", "D9AE3C", "F2CC5E", "FFE28A");
        private static readonly Ramp Leaf = Ramp.Of("3C5F2E", "4C7A3A", "5E8C45", "79A657", "9BC66F");
        private static readonly Ramp Berry = Ramp.Of("7A2A2E", "A33A3F", "C4474B", "F08A8A", "FFC2C2");
        private static readonly Ramp Dirt = Ramp.Of("6E5538", "977852", "B99A6B", "C9AC7C", "D9C08F");
        private static readonly Ramp River = Ramp.Of("2F5A75", "3B7294", "4786AB", "5695B8", "7FB4CF");

        /// <summary>Soot in a chimney flue.</summary>
        private static readonly Color Soot = new("2A2622");
        /// <summary>Forge embers: the glow, the hot middle and the white-hot core.</summary>
        private static readonly Color Ember = new("E0662A");
        private static readonly Color EmberHot = new("F5A742");
        private static readonly Color EmberCore = new("FFE08A");
        /// <summary>A building's shadow on the ground.</summary>
        private static readonly Color Shadow = new(0.04f, 0.06f, 0.05f, 0.30f);
        /// <summary>The shadow of a small object (anvil, barrel, sign) on the ground, 28%.</summary>
        private static readonly Color SmallShadow = new(0.05f, 0.08f, 0.05f, 0.28f);
        /// <summary>A chimney's shadow on a roof: the ground shadow a little stronger, so it reads on dark slate.</summary>
        private static readonly Color RoofShadow = new(0.04f, 0.06f, 0.05f, 0.38f);

        /// <summary>Roof materials; each has its own course pattern in <see cref="Surface"/>.</summary>
        private enum Material { Clay, Slate, Thatch, Shingle, Plank }

        /// <summary>A gable has two faces along the ridge; a hip adds triangular end faces.</summary>
        private enum RoofShape { Gable, Hip }

        /// <summary>The roof face a pixel lies on, named by the eave it slopes down to.</summary>
        private enum Face { North, West, East, South }

        /// <summary>
        /// How a roofed kind is built: its roof ramp and material, gabled or
        /// hipped, the seed of its course pattern, the depth of a side yard (0
        /// for none) and how far the roof stands back from the door side, so
        /// door, doorstep and path fit. The seeds are the reviewed drawing's.
        /// </summary>
        private readonly record struct Recipe(Ramp Roof, Material Material, RoofShape Shape, int Salt, float Yard = 0, float Clearance = 5);

        private static Recipe? RecipeFor(BuildingKind kind) => kind switch
        {
            BuildingKind.House => new(Clay, Material.Clay, RoofShape.Hip, 7),
            BuildingKind.Warehouse => new(GreyTimber, Material.Plank, RoofShape.Gable, 38),
            // The roof stands back eight units on the door side so the grain sacks fit beside the doorstep.
            BuildingKind.Farmhouse => new(Thatch, Material.Thatch, RoofShape.Hip, 69, Yard: 18, Clearance: 8),
            BuildingKind.Blacksmith => new(Slate, Material.Slate, RoofShape.Gable, 100, Yard: 20),
            BuildingKind.TailorShop => new(DyedShingle, Material.Shingle, RoofShape.Hip, 162),
            BuildingKind.Store => new(Timber, Material.Shingle, RoofShape.Hip, 193, Clearance: 10),
            BuildingKind.Workshop => new(GreenPlank, Material.Plank, RoofShape.Gable, 379, Yard: 16),
            BuildingKind.Generic => new(Plain, Material.Shingle, RoofShape.Hip, 410),
            BuildingKind.TownHall => new(Slate, Material.Slate, RoofShape.Hip, 286, Clearance: 22),
            _ => null,
        };

        /// <summary>Whether this kind uses the approved drawing.</summary>
        public static bool Draws(BuildingKind kind) => kind == BuildingKind.Silo || RecipeFor(kind) is not null;

        /// <summary>The main roof colour of an approved kind, for the overview fill; null for the others.</summary>
        public static Color? MainRoof(BuildingKind kind) => kind == BuildingKind.Silo ? SiloWood.Base : RecipeFor(kind)?.Roof.Base;

        /// <summary>Draws one approved kind over its footprint, transparent outside the building.</summary>
        public static Image Draw(BuildingKind kind, int tilesWide, int tilesHigh, int tilePixels, BuildingDoor door)
        {
            tilesWide = Math.Clamp(tilesWide, 1, 8);
            tilesHigh = Math.Clamp(tilesHigh, 1, 8);
            var plate = new Plate(tilesWide * tilePixels, tilesHigh * tilePixels, tilePixels / 32f);
            var w = tilesWide * 32;
            var h = tilesHigh * 32;
            if (RecipeFor(kind) is { } recipe) PaintRoofed(plate, kind, recipe, w, h, door);
            else PaintSilo(plate, w, h);
            return plate.Image;
        }

        // ------------------------------------------------------------------
        // Roofed buildings.
        // ------------------------------------------------------------------

        /// <summary>Where the roof, the yard and the door go, in 32-unit tile space.</summary>
        private readonly record struct Plan(Rect2 Roof, Rect2? Yard, float DoorMiddle);

        /// <summary>
        /// The roof is inset three units, with two more on the south for the
        /// eave shadow, and pulled back on the door side to the recipe's
        /// clearance. A yard, if any, takes one end of the footprint away from
        /// the door: across the long side of a wide footprint, or behind a
        /// one-tile-wide one.
        /// </summary>
        private static Plan Lay(int w, int h, BuildingDoor door, Recipe recipe, float doorHalf)
        {
            float left = 3, top = 3, right = w - 3, bottom = h - 5;
            switch (door.Side)
            {
                case DoorSide.North: top = Math.Max(top, recipe.Clearance); break;
                case DoorSide.East: right = Math.Min(right, w - recipe.Clearance); break;
                case DoorSide.West: left = Math.Max(left, recipe.Clearance); break;
                default: bottom = Math.Min(bottom, h - recipe.Clearance); break;
            }
            Rect2? yard = null;
            if (recipe.Yard > 0 && (w > 32 || h > 32))
            {
                var depth = recipe.Yard;
                switch (YardSide(w, h, door))
                {
                    case DoorSide.East:
                        right -= depth;
                        yard = new Rect2(right + 2, 2, w - 4 - right, h - 4);
                        break;
                    case DoorSide.West:
                        left += depth;
                        yard = new Rect2(2, 2, left - 4, h - 4);
                        break;
                    case DoorSide.South:
                        bottom -= depth;
                        yard = new Rect2(2, bottom + 2, w - 4, h - 4 - bottom);
                        break;
                    default:
                        top += depth;
                        yard = new Rect2(2, 2, w - 4, top - 4);
                        break;
                }
            }
            var roof = new Rect2(left, top, right - left, bottom - top);
            var horizontal = door.Side is DoorSide.South or DoorSide.North;
            var from = horizontal ? roof.Position.X : roof.Position.Y;
            var length = horizontal ? roof.Size.X : roof.Size.Y;
            var middle = door.Tile is { } tile ? tile * 32 + 16 : from + length / 2;
            middle = Fit(middle, from + doorHalf + 3, from + length - doorHalf - 3);
            return new Plan(roof, yard, middle);
        }

        /// <summary>The end of the footprint farthest from the door, for a yard.</summary>
        private static DoorSide YardSide(int w, int h, BuildingDoor door)
        {
            var opposite = door.Side switch
            {
                DoorSide.North => DoorSide.South,
                DoorSide.East => DoorSide.West,
                DoorSide.West => DoorSide.East,
                _ => DoorSide.North,
            };
            if (door.Side is DoorSide.South or DoorSide.North)
            {
                if (w <= 32) return opposite;
                var middle = door.Tile is { } t ? t * 32 + 16 : w / 2f;
                return middle <= w / 2f ? DoorSide.East : DoorSide.West;
            }
            if (h <= 32) return opposite;
            var along = door.Tile is { } u ? u * 32 + 16 : h / 2f;
            return along <= h / 2f ? DoorSide.South : DoorSide.North;
        }

        /// <summary>
        /// Paints a roofed kind: yard ground, then the roof with its eave
        /// shadow, then the identifying feature, the door and the path start.
        /// </summary>
        private static void PaintRoofed(Plate p, BuildingKind kind, Recipe recipe, int w, int h, BuildingDoor door)
        {
            if (kind == BuildingKind.TownHall)
            {
                PaintHall(p, recipe, w, h, door);
                return;
            }
            var doorHalf = kind == BuildingKind.Warehouse ? 7f : 3f;
            var plan = Lay(w, h, door, recipe, doorHalf);
            var roof = p.Px(plan.Roof);
            var middle = p.P(plan.DoorMiddle);

            if (plan.Yard is { } yardUnits)
            {
                var yard = p.Px(yardUnits);
                if (kind == BuildingKind.Blacksmith)
                {
                    // The forge yard: the whole end in darker packed earth with cinders.
                    YardFloor(p, yard, Dirt.Shade, Dirt.Edge, Iron.Shade, 113);
                }
                else
                {
                    // The farmyard and the work yard are smaller worn patches, not the whole end.
                    var inset = p.P(2);
                    var patch = new Rect2I(yard.Position.X + inset, yard.Position.Y + inset, yard.Size.X - inset * 2, yard.Size.Y - inset * 2);
                    YardFloor(p, patch, Dirt.Base, Dirt.Shade, Dirt.Shade, kind == BuildingKind.Farmhouse ? 117 : 131);
                }
            }
            PaintRoof(p, roof, recipe);

            var nextRow = 0;
            switch (kind)
            {
                case BuildingKind.House:
                    var (cx, cy) = ChimneySpot(roof, p.Small, door.Side, middle);
                    Chimney(p, cx, cy);
                    nextRow = Door(p, roof, door.Side, middle, 3, 3);
                    break;
                case BuildingKind.Farmhouse:
                    if (plan.Yard is { } farmYard) Sheaves(p, p.Px(farmYard));
                    nextRow = Door(p, roof, door.Side, middle, 3, 3);
                    GrainSacks(p, roof, door.Side, middle);
                    break;
                case BuildingKind.Workshop:
                    if (plan.Yard is { } workYard) WorkYard(p, p.Px(workYard));
                    var hammer = Inward(p, roof, door.Side, middle, 9);
                    HammerSign(p, hammer.X, hammer.Y);
                    nextRow = Door(p, roof, door.Side, middle, 3, 3);
                    break;
                case BuildingKind.Store:
                    StoreAwning(p, roof, door.Side);
                    nextRow = StepOnly(p, roof, door.Side, middle, 6, 3);
                    break;
                case BuildingKind.Generic:
                    nextRow = Door(p, roof, door.Side, middle, 3, 3);
                    break;
                case BuildingKind.Blacksmith:
                    if (plan.Yard is { } forgeYard) ForgeYard(p, p.Px(forgeYard));
                    nextRow = Door(p, roof, door.Side, middle, 3, 3);
                    break;
                case BuildingKind.Warehouse:
                    nextRow = LoadingDoors(p, roof, door.Side, middle);
                    break;
                case BuildingKind.TailorShop:
                    var spool = Inward(p, roof, door.Side, middle, 8);
                    SpoolSign(p, spool.X, spool.Y);
                    nextRow = Door(p, roof, door.Side, middle, 3, 3);
                    break;
            }
            // The doorstep path the Road's own doorstep piece continues.
            if (door.Tile is not null) Path(p, roof, door.Side, middle, nextRow);
        }

        /// <summary>The approved TownHall.3x4 drawing; the supported Hall binds its south door to slot one.</summary>
        private static void PaintHall(Plate p, Recipe recipe, int w, int h, BuildingDoor door)
        {
            var plan = Lay(w, h, door, recipe, 5);
            var roof = p.Px(plan.Roof);
            var middle = p.P(plan.DoorMiddle);
            Forecourt(p, roof, door.Side);
            var main = HallRoofs(p, roof, recipe, door.Side, recipe.Salt);
            middle = door.Side is DoorSide.South or DoorSide.North
                ? FitHallPixel(middle, main.Position.X + p.P(10), main.End.X - 1 - p.P(10))
                : FitHallPixel(middle, main.Position.Y + p.P(10), main.End.Y - 1 - p.P(10));
            var tower = Inward(p, main, door.Side, middle, 15);
            BellTower(p, tower.X, tower.Y);
            Door(p, main, door.Side, middle, 5, 3);
            Planters(p, main, door.Side, middle);
        }

        private static int FitHallPixel(int value, int min, int max) => max < min ? (min + max) / 2 : Math.Clamp(value, min, max);

        private static Rect2I HallRoofs(Plate p, Rect2I roof, Recipe recipe, DoorSide door, int salt)
        {
            var towardDoor = door is DoorSide.South or DoorSide.North;
            int x = roof.Position.X, y = roof.Position.Y, w = roof.Size.X, h = roof.Size.Y;
            var main = towardDoor
                ? new Rect2I(x + (int)(w * 0.2f), y, w - 2 * (int)(w * 0.2f), h)
                : new Rect2I(x, y + (int)(h * 0.2f), w, h - 2 * (int)(h * 0.2f));
            var wings = towardDoor
                ? new Rect2I(x, y + (int)(h * 0.3f), w, (int)(h * 0.38f))
                : new Rect2I(x + (int)(w * 0.3f), y, (int)(w * 0.38f), h);
            PaintRoof(p, wings, recipe with { Salt = salt + 1 });
            PaintRoof(p, main, recipe with { Salt = salt });
            // Gold finials where the main ridge ends.
            int iw = main.Size.X - 2, ih = main.Size.Y - 2;
            var size = p.Small ? 1 : 2;
            foreach (var (fx, fy) in RidgeEnds(main.Position.X + 1, main.Position.Y + 1, iw, ih))
            {
                p.Fill(fx - size / 2 + 1, fy - size / 2 + 1, size, size, RoofShadow);
                p.Fill(fx - size / 2, fy - size / 2, size, size, Gold.Base);
                p.Put(fx - size / 2, fy - size / 2, Gold.Highlight);
            }
            return main;
        }

        /// <summary>The two ends of a hipped roof's ridge, in pixels.</summary>
        private static (int X, int Y)[] RidgeEnds(int left, int top, int iw, int ih)
        {
            if (iw >= ih)
            {
                var ridge = (ih + 1) / 2 - 1;
                return [(left + ridge, top + ridge), (left + iw - 1 - ridge, top + ridge)];
            }
            var column = (iw + 1) / 2 - 1;
            return [(left + column, top + column), (left + column, top + ih - 1 - column)];
        }

        private static void Forecourt(Plate p, Rect2I roof, DoorSide side)
        {
            var inset = p.P(2);
            var area = side switch
            {
                DoorSide.North => new Rect2I(inset, 0, p.Width - inset * 2, roof.Position.Y + 1),
                DoorSide.East => new Rect2I(roof.End.X - 1, inset, p.Width - roof.End.X + 1, p.Height - inset * 2),
                DoorSide.West => new Rect2I(0, inset, roof.Position.X + 1, p.Height - inset * 2),
                _ => new Rect2I(inset, roof.End.Y - 1, p.Width - inset * 2, p.Height - roof.End.Y + 1),
            };
            Flagstones(p, area, 151);
        }

        /// <summary>Town Hall: two stone planters of flowers on the forecourt, one either side of the steps.</summary>
        private static void Planters(Plate p, Rect2I roof, DoorSide side, int middle)
        {
            var size = p.Small ? 3 : 6;
            var gap = p.Small ? 6 : 12;
            var firstRow = p.Small ? 3 : 6;
            foreach (var start in new[] { middle - gap - size, middle + gap })
                for (var k = firstRow; k < firstRow + size; k++)
                    for (var t = start; t < start + size; t++)
                    {
                        var rim = k == firstRow || k == firstRow + size - 1 || t == start || t == start + size - 1;
                        var c = rim ? (p.Small ? Doorstep.Shade : k == firstRow || t == start ? Doorstep.Light : Doorstep.Edge)
                            : (t + k) % 3 == 0 ? Leaf.Light : Leaf.Base;
                        if (!rim && (t * 5 + k * 3) % 7 == 0) c = (t + k) % 2 == 0 ? Berry.Light : Gold.Light;
                        Out(p, roof, side, k, t, c);
                    }
        }

        /// <summary>Calm flagstones: offset rows of stones in the Doorstep ramp, joints one step darker, worn border.</summary>
        private static void Flagstones(Plate p, Rect2I area, int salt)
        {
            int rowHeight = p.Small ? 3 : 5, stoneWidth = p.Small ? 4 : 7;
            for (var y = area.Position.Y; y < area.End.Y; y++)
            {
                var row = (y - area.Position.Y) / rowHeight;
                var inRow = (y - area.Position.Y) % rowHeight;
                var shift = (int)(PixelArt.Hash(row, 0, salt) % (uint)stoneWidth);
                for (var x = area.Position.X; x < area.End.X; x++)
                {
                    var position = x - area.Position.X + shift;
                    var border = x == area.Position.X || y == area.Position.Y || x == area.End.X - 1 || y == area.End.Y - 1;
                    Color c;
                    if (border) c = Doorstep.Shade;
                    else if (inRow == rowHeight - 1 || position % stoneWidth == stoneWidth - 1) c = Toward(Doorstep, 2, 1, 0.7f);
                    else
                    {
                        var roll = PixelArt.Hash(position / stoneWidth, row, salt + 1) % 6;
                        c = roll == 0 ? Toward(Doorstep, 2, 3, 0.6f) : roll == 1 ? Toward(Doorstep, 2, 1, 0.25f) : Doorstep.Base;
                        if (inRow == 0 && !p.Small) c = c.Lerp(Doorstep.Light, 0.3f);
                    }
                    p.Put(x, y, c);
                }
            }
        }

        /// <summary>
        /// B3 Town Hall: an open bell tower on the roof, seen from above. Stone
        /// walls lit north-west with corner piers, the dark belfry inside, a
        /// timber headstock across it and the bronze bell hanging from it.
        /// </summary>
        private static void BellTower(Plate p, int cx, int cy)
        {
            if (p.Small)
            {
                p.Fill(cx - 3, cy - 3, 8, 8, RoofShadow);
                p.Fill(cx - 4, cy - 4, 8, 8, Doorstep.Edge);
                p.Fill(cx - 3, cy - 3, 6, 6, Doorstep.Light);
                p.Fill(cx - 2, cy - 2, 4, 4, Soot);
                p.Fill(cx - 1, cy - 1, 2, 2, Gold.Base);
                p.Put(cx - 1, cy - 1, Gold.Highlight);
                return;
            }
            const int half = 9;
            int x = cx - half, y = cy - half, size = half * 2;
            p.Fill(x + 3, y + 3, size, size, RoofShadow);
            p.Fill(x, y, size, size, Doorstep.Edge);
            p.Fill(x + 1, y + 1, size - 2, size - 2, Doorstep.Shade);
            p.Fill(x + 1, y + 1, size - 3, size - 3, Doorstep.Base);
            p.Fill(x + 1, y + 1, size - 3, 1, Doorstep.Light);
            p.Fill(x + 1, y + 1, 1, size - 3, Doorstep.Light);
            p.Fill(x + 3, y + 3, size - 6, size - 6, Doorstep.Edge);
            p.Fill(x + 4, y + 4, size - 8, size - 8, Soot);
            // Corner piers, lit on their north-west faces.
            foreach (var (px, py) in new[] { (x, y), (x + size - 5, y), (x, y + size - 5), (x + size - 5, y + size - 5) })
            {
                p.Fill(px, py, 5, 5, Doorstep.Edge);
                p.Fill(px + 1, py + 1, 3, 3, Doorstep.Light);
                p.Put(px + 1, py + 1, Doorstep.Highlight);
                p.Put(px + 3, py + 3, Doorstep.Shade);
            }
            // Headstock beam and the bell: a bronze dome lit from the north-west.
            p.Fill(x + 4, cy - 1, size - 8, 2, Timber.Shade);
            p.Disc(cx, cy, 4.2f, Gold.Edge);
            p.Disc(cx, cy, 3.4f, Gold.Shade);
            p.Disc(cx - 0.6f, cy - 0.6f, 2.5f, Gold.Base);
            p.Disc(cx - 1.2f, cy - 1.2f, 1.2f, Gold.Light);
            p.Put(cx - 2, cy - 2, Gold.Highlight);
            p.Fill(cx - 1, cy - 1, 2, 2, Timber.Edge);
        }

        /// <summary>
        /// Paints a roof over <paramref name="roof"/> (output pixels): eave
        /// shadow, material courses per face, the one-pixel edge, then ridge
        /// and hip lines with a crease on the shaded side.
        /// </summary>
        private static void PaintRoof(Plate p, Rect2I roof, Recipe recipe)
        {
            int x0 = roof.Position.X, y0 = roof.Position.Y, w = roof.Size.X, h = roof.Size.Y;
            if (w < 5 || h < 5) return;
            var ramp = recipe.Roof;
            var thatch = recipe.Material == Material.Thatch;
            p.Fill(new Rect2I(x0 + p.P(2), y0 + p.P(3), w, h), Shadow);

            int iw = w - 2, ih = h - 2;
            for (var v = 0; v < ih; v++)
                for (var u = 0; u < iw; u++)
                {
                    var face = FaceAt(u, v, iw, ih, recipe.Shape);
                    var (along, across) = face switch
                    {
                        Face.North => (u, v),
                        Face.South => (u, ih - 1 - v),
                        Face.West => (v, u),
                        _ => (v, iw - 1 - u),
                    };
                    p.Put(x0 + 1 + u, y0 + 1 + v, Surface(recipe, face, along, across, p.Small));
                }

            // The roof's one-pixel edge; thatch eaves have rounded corners.
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                {
                    if (x != 0 && y != 0 && x != w - 1 && y != h - 1) continue;
                    var corner = (x == 0 || x == w - 1) && (y == 0 || y == h - 1);
                    if (corner && thatch) continue;
                    p.Put(x0 + x, y0 + y, ramp.Edge);
                }
            if (thatch)
                foreach (var (cx, cy) in new[] { (1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2) })
                    p.Put(x0 + cx, y0 + cy, ramp.Edge);

            RidgeAndHips(p, x0 + 1, y0 + 1, iw, ih, recipe);
        }

        /// <summary>
        /// Which face a roof pixel is on. A gable splits along the long side;
        /// a hip adds triangular end faces whose 45° hips meet the ridge.
        /// </summary>
        private static Face FaceAt(int u, int v, int iw, int ih, RoofShape shape)
        {
            if (iw >= ih)
            {
                var ridge = (ih + 1) / 2 - 1;
                var north = v <= ridge;
                if (shape == RoofShape.Gable) return north ? Face.North : Face.South;
                var eave = north ? v : ih - 1 - v;
                if (eave <= u && eave <= iw - 1 - u) return north ? Face.North : Face.South;
                return u < iw - 1 - u ? Face.West : Face.East;
            }
            else
            {
                var ridge = (iw + 1) / 2 - 1;
                var west = u <= ridge;
                if (shape == RoofShape.Gable) return west ? Face.West : Face.East;
                var eave = west ? u : iw - 1 - u;
                if (eave <= v && eave <= ih - 1 - v) return west ? Face.West : Face.East;
                return v < ih - 1 - v ? Face.North : Face.South;
            }
        }

        /// <summary>
        /// The ridge as a one-pixel light line along the long side with a
        /// crease just below it on the shaded half, and on hipped roofs the
        /// four hips: soft light where they touch a lit face, a lifted shade
        /// on the south-east hip. Thatch gets a bound ridge band instead of lines.
        /// </summary>
        private static void RidgeAndHips(Plate p, int left, int top, int iw, int ih, Recipe recipe)
        {
            var r = recipe.Roof;
            var hip = recipe.Shape == RoofShape.Hip;
            var thatch = recipe.Material == Material.Thatch;
            var horizontal = iw >= ih;
            // One routine for both directions: "along" follows the ridge, "across" spans the roof.
            void Put(int along, int across, Color c)
            {
                if (horizontal) p.Put(left + along, top + across, c);
                else p.Put(left + across, top + along, c);
            }
            var length = horizontal ? iw : ih;
            var breadth = horizontal ? ih : iw;
            var ridge = (breadth + 1) / 2 - 1;
            var from = hip ? ridge : 0;
            var to = hip ? length - 1 - ridge : length - 1;
            var litHip = Toward(r, 2, 3, thatch ? 0.45f : 0.8f);
            var shadedHip = Toward(r, 1, 2, 0.7f);

            if (hip)
            {
                for (var t = 0; t <= ridge; t++)
                {
                    Put(t, t, litHip);
                    Put(length - 1 - t, t, litHip);
                }
                for (var t = 0; breadth - 1 - t > ridge; t++)
                {
                    Put(t, breadth - 1 - t, litHip);
                    if (!thatch) Put(length - 1 - t, breadth - 1 - t, shadedHip);
                }
            }
            if (thatch)
            {
                // A bound ridge: a raised roll of straw tied down with spars every few pixels.
                var start = Math.Max(0, from - 1);
                var end = Math.Min(length - 1, to + 1);
                for (var a = start; a <= end; a++)
                {
                    if (ridge - 1 >= 0) Put(a, ridge - 1, Toward(r, 3, 4, 0.3f));
                    Put(a, ridge, (a + recipe.Salt) % 4 == 0 && !p.Small ? r[1] : r[3]);
                    if (ridge + 1 < breadth) Put(a, ridge + 1, r[2]);
                    if (ridge + 2 < breadth && !p.Small) Put(a, ridge + 2, Toward(r, 1, 0, 0.6f));
                }
                return;
            }
            for (var a = from; a <= to && ridge + 1 < breadth; a++) Put(a, ridge + 1, Toward(r, 1, 0, 0.7f));
            for (var a = from; a <= to; a++) Put(a, ridge, r[3]);
        }

        /// <summary>
        /// Part of the way from one ramp step toward another. Course lines and
        /// joints use this, so the texture stays calm at 1×.
        /// </summary>
        private static Color Toward(Ramp r, int step, int toward, float amount) => r[step].Lerp(r[toward], amount);

        /// <summary>The tones of one roof face: body, course line, soft joint, lit accent and a faint grain.</summary>
        private readonly record struct Tones(Color Body, Color Line, Color Joint, Color Accent, Color Grain);

        /// <summary>
        /// North and west faces take the base step and darken toward shade;
        /// south and east faces take the shade step and darken toward the edge.
        /// </summary>
        private static Tones TonesFor(Ramp r, bool lit) => lit
            ? new(r[2], Toward(r, 2, 1, 0.85f), Toward(r, 2, 1, 0.5f), Toward(r, 2, 3, 0.7f), Toward(r, 2, 1, 0.25f))
            : new(r[1], Toward(r, 1, 0, 0.6f), Toward(r, 1, 0, 0.32f), Toward(r, 1, 2, 0.4f), Toward(r, 1, 0, 0.15f));

        /// <summary>Colour of one roof pixel from its material, face and position in the courses.</summary>
        private static Color Surface(Recipe recipe, Face face, int along, int across, bool small)
        {
            var lit = face is Face.North or Face.West;
            var tones = TonesFor(recipe.Roof, lit);
            return recipe.Material switch
            {
                Material.Clay => ClayTiles(tones, along, across, small),
                Material.Slate => Slates(tones, along, across, small, recipe.Salt),
                Material.Shingle => Shingles(tones, lit, along, across, small, recipe.Salt),
                Material.Thatch => Straw(tones, along, across, small, recipe.Salt),
                _ => Planks(tones, along, across, small, recipe.Salt),
            };
        }

        /// <summary>
        /// House: clay tiles in courses three rows deep, offset by half a tile
        /// from course to course. Each four-pixel tile shows a rounded butt at
        /// the eave side (a lit crown between two soft gaps) and the course line
        /// above it is the shadow of the next course. At 16 px: body and line only.
        /// </summary>
        private static Color ClayTiles(Tones t, int along, int across, bool small)
        {
            const int depth = 3;
            var course = across / depth;
            var row = across % depth;
            if (row == depth - 1) return t.Line;
            if (small) return t.Body;
            if (row == 1) return t.Body;
            var u = (along + course % 2 * 2) % 4;
            return u switch { 0 => t.Joint, 1 => t.Accent, 2 => t.Body.Lerp(t.Accent, 0.4f), _ => t.Body };
        }

        /// <summary>
        /// Blacksmith: slate in 6 × 3 slabs (three face rows and a line),
        /// joints staggered by half a slab, the eave-side row lit on sunny
        /// faces and one slab in ten a touch lighter or darker.
        /// </summary>
        private static Color Slates(Tones t, int along, int across, bool small, int salt)
        {
            var depth = small ? 3 : 4;
            var length = small ? 4 : 6;
            var course = across / depth;
            var row = across % depth;
            if (row == depth - 1) return t.Line;
            var position = along + course % 2 * (length / 2);
            if (position % length == 0) return t.Joint;
            if (small) return t.Body;
            var roll = PixelArt.Hash(position / length, course, salt) % 10;
            var body = roll == 0 ? t.Body.Lerp(t.Accent, 0.45f) : roll == 1 ? t.Grain : t.Body;
            return row == 0 ? body.Lerp(t.Accent, 0.35f) : body;
        }

        /// <summary>
        /// Tailor shop: shingles in courses three rows deep with uneven widths
        /// and a mix of tones, like split and dyed wood.
        /// </summary>
        private static Color Shingles(Tones t, bool lit, int along, int across, bool small, int salt)
        {
            const int depth = 3;
            var course = across / depth;
            var row = across % depth;
            if (row == depth - 1) return t.Line;
            if (small) return t.Body;
            var length = 3 + (int)(PixelArt.Hash(course, 2, salt) % 3);
            var position = along + (int)(PixelArt.Hash(course, 1, salt) % (uint)length);
            if (position % length == 0) return t.Joint;
            var roll = PixelArt.Hash(position / length, course, salt + 5) % 6;
            var body = roll == 0 ? t.Body.Lerp(t.Accent, 0.6f) : roll == 1 ? t.Body.Lerp(t.Line, 0.35f) : t.Body;
            return row == 0 && lit ? body.Lerp(t.Accent, 0.4f) : body;
        }

        /// <summary>
        /// Warehouse: long planks along the ridge, three pixels with a
        /// one-pixel seam, butt joints far apart, a few lighter or darker
        /// boards and a faint grain.
        /// </summary>
        private static Color Planks(Tones t, int along, int across, bool small, int salt)
        {
            var depth = small ? 3 : 4;
            var course = across / depth;
            var row = across % depth;
            if (row == depth - 1) return t.Line;
            if (small) return t.Body;
            var length = 18 + (int)(PixelArt.Hash(course, 2, salt) % 13);
            var position = along + (int)(PixelArt.Hash(course, 1, salt) % (uint)length);
            if (position % length == 0) return t.Joint;
            var roll = PixelArt.Hash(position / length, course, salt + 3) % 5;
            var body = roll == 0 ? t.Body.Lerp(t.Accent, 0.35f) : roll == 1 ? t.Grain : t.Body;
            if (row == 1 && PixelArt.Hash(along / 3, course, salt + 9) % 7 == 0) body = t.Grain;
            return body;
        }

        /// <summary>
        /// Farmhouse: thatch. Trimmed straw ends along the eave with a soft
        /// shadow under the lip, faint layer lines and short strands running
        /// down the slope in light and dark.
        /// </summary>
        private static Color Straw(Tones t, int along, int across, bool small, int salt)
        {
            if (across == 0) return t.Accent;
            if (across == 1 && !small) return along % 2 == 0 ? t.Joint : t.Body;
            if (!small && across % 7 == 0 && PixelArt.Hash(along, across, salt) % 3 != 0) return t.Grain;
            var phase = (int)(PixelArt.Hash(along, 0, salt) % 3);
            var strand = PixelArt.Hash(along, (across + phase) / 3, salt + 1) % (small ? 8u : 5u);
            if (strand == 0) return t.Joint;
            if (strand == 1 && !small) return t.Body.Lerp(t.Accent, 0.6f);
            return t.Body;
        }

        // ------------------------------------------------------------------
        // Doors, doorsteps and paths.
        // ------------------------------------------------------------------

        /// <summary>Puts a pixel <paramref name="k"/> rows out from the roof edge on the door side, <paramref name="t"/> along it.</summary>
        private static void Out(Plate p, Rect2I roof, DoorSide side, int k, int t, Color c)
        {
            switch (side)
            {
                case DoorSide.North: p.Put(t, roof.Position.Y - k, c); break;
                case DoorSide.East: p.Put(roof.End.X - 1 + k, t, c); break;
                case DoorSide.West: p.Put(roof.Position.X - k, t, c); break;
                default: p.Put(t, roof.End.Y - 1 + k, c); break;
            }
        }

        /// <summary>How many pixel rows lie between the roof edge and the footprint edge on a side.</summary>
        private static int Reach(Plate p, Rect2I roof, DoorSide side) => side switch
        {
            DoorSide.North => roof.Position.Y,
            DoorSide.East => p.Width - roof.End.X,
            DoorSide.West => roof.Position.X,
            _ => p.Height - roof.End.Y,
        };

        /// <summary>The pixel span [from, to) of something <paramref name="half"/> units either side of the door middle.</summary>
        private static (int From, int To) Span(Plate p, int middle, int half) =>
            p.Small ? (middle - half / 2, middle + (half + 1) / 2) : (middle - half, middle + half);

        /// <summary>
        /// A door in Timber edge under a one-pixel Timber lintel, over a
        /// doorstep <paramref name="stepRows"/> deep in Doorstep stone (lit at
        /// the north-west corner, shaded on its outer row). At 16 px the lintel
        /// drops and the step is one row. Returns the first row free for the path.
        /// </summary>
        private static int Door(Plate p, Rect2I roof, DoorSide side, int middle, int half, int stepRows)
        {
            var (from, to) = Span(p, middle, half);
            if (!p.Small)
                for (var t = from - 1; t < to + 1; t++) Out(p, roof, side, 0, t, Timber.Light);
            for (var t = from; t < to; t++) Out(p, roof, side, 1, t, Timber.Edge);
            return Step(p, roof, side, from, to, 2, p.Small ? 1 : stepRows);
        }

        /// <summary>
        /// A doorstep across [<paramref name="from"/>, <paramref name="to"/>)
        /// from <paramref name="firstRow"/> outward, lit at its north-west corner and
        /// shaded on its outer row, clipped to the footprint. Returns the first free row.
        /// </summary>
        private static int Step(Plate p, Rect2I roof, DoorSide side, int from, int to, int firstRow, int rows)
        {
            var last = Math.Min(Reach(p, roof, side), firstRow + rows - 1);
            for (var k = firstRow; k <= last; k++)
                for (var t = from; t < to; t++)
                {
                    var c = k == last && rows > 1 ? Doorstep.Shade : Doorstep.Base;
                    if (k == firstRow && t == from && rows > 1) c = Doorstep.Light;
                    Out(p, roof, side, k, t, c);
                }
            return last + 1;
        }

        /// <summary>
        /// The doorstep path from the step to the footprint edge, five pixels
        /// of Road dirt with a feathered worn edge, on the same axis as the
        /// Road's doorstep piece so the two join at the tile edge.
        /// </summary>
        private static void Path(Plate p, Rect2I roof, DoorSide side, int middle, int fromRow)
        {
            var reach = Reach(p, roof, side);
            for (var k = fromRow; k <= reach; k++)
            {
                if (p.Small)
                {
                    for (var t = middle - 1; t < middle + 2; t++) Out(p, roof, side, k, t, Dirt.Base);
                    continue;
                }
                for (var t = middle - 2; t < middle + 3; t++) Out(p, roof, side, k, t, Dirt.Base);
                foreach (var t in new[] { middle - 3, middle + 3 })
                    if (PixelArt.Hash(t, k + (int)side * 7, 91) % 10 >= 3) Out(p, roof, side, k, t, Dirt.Shade);
            }
        }

        /// <summary>
        /// Warehouse: wide split loading doors, a Timber lintel beam, two
        /// plank leaves with a dark split, over a broad stone loading apron.
        /// </summary>
        private static int LoadingDoors(Plate p, Rect2I roof, DoorSide side, int middle)
        {
            var (from, to) = Span(p, middle, 7);
            var split = (from + to) / 2;
            if (!p.Small)
                for (var t = from - 1; t < to + 1; t++) Out(p, roof, side, 0, t, Timber.Light);
            var leafRows = p.Small ? 1 : 2;
            for (var k = 1; k <= leafRows; k++)
                for (var t = from; t < to; t++)
                {
                    var c = t == from || t == to - 1 || t == split ? Timber.Edge
                        : (t - from) % 3 == 0 && !p.Small ? Timber.Shade : k == 1 ? Timber.Base : Timber.Shade;
                    Out(p, roof, side, k, t, c);
                }
            return Step(p, roof, side, from, to, leafRows + 1, p.Small ? 1 : 2);
        }

        /// <summary>A point <paramref name="distance"/> units inside the roof from the door.</summary>
        private static (int X, int Y) Inward(Plate p, Rect2I roof, DoorSide side, int middle, float distance)
        {
            var d = p.P(distance);
            return side switch
            {
                DoorSide.North => (middle, roof.Position.Y + d),
                DoorSide.East => (roof.End.X - 1 - d, middle),
                DoorSide.West => (roof.Position.X + d, middle),
                _ => (middle, roof.End.Y - 1 - d),
            };
        }

        // ------------------------------------------------------------------
        // Identifying features.
        // ------------------------------------------------------------------

        /// <summary>
        /// House: the chimney sits on the shaded half near the ridge, at the
        /// end away from the door so it never crowds the doorway.
        /// </summary>
        private static (int X, int Y) ChimneySpot(Rect2I roof, bool small, DoorSide side, int middle)
        {
            int iw = roof.Size.X - 2, ih = roof.Size.Y - 2;
            int left = roof.Position.X + 1, top = roof.Position.Y + 1;
            var margin = small ? 3 : 6;
            if (iw < ih)
            {
                // A long north-south roof: the east face, away from an east or west door.
                var nearDoor = side is DoorSide.East or DoorSide.West && middle < top + ih / 2;
                var along = Math.Min(ih / 4, small ? 6 : 14);
                return (left + iw - margin, nearDoor ? top + ih - 1 - along : top + along);
            }
            var ridge = (ih + 1) / 2 - 1;
            if (iw - ih <= 8)
            {
                // A square roof: the east face, or the south face when the door is on the east.
                if (side == DoorSide.East) return (left + iw / 2 + (small ? 1 : 3), top + ih - margin + (small ? 0 : 1));
                return (left + iw - Math.Max(small ? 3 : 5, iw / 5), top + ridge + 1);
            }
            // A long east-west roof: the south face, away from a north or south door.
            var awayWest = side is DoorSide.North or DoorSide.South && middle > left + iw / 2;
            return (left + (int)(iw * (awayWest ? 0.3f : 0.7f)), top + ih - margin);
        }

        /// <summary>
        /// A stone chimney stack seen from above: a two-pixel cap (dark edge and
        /// a rim lit north-west, shaded south-east) around a sooty flue, casting
        /// a short shadow on the roof.
        /// </summary>
        private static void Chimney(Plate p, int cx, int cy)
        {
            if (p.Small)
            {
                // Three pixels: a lit north-west corner, a dark flue, the edge on the south-east.
                p.Fill(cx, cy, 3, 3, RoofShadow);
                p.Fill(cx - 1, cy - 1, 3, 3, Rock.Edge);
                p.Put(cx - 1, cy - 1, Rock.Highlight);
                p.Put(cx, cy - 1, Rock.Light);
                p.Put(cx - 1, cy, Rock.Light);
                p.Put(cx, cy, Soot);
                return;
            }
            int x = cx - 3, y = cy - 3;
            p.Fill(x + 2, y + 2, 6, 6, RoofShadow);
            p.Fill(x, y, 6, 6, Rock.Edge);
            p.Fill(x + 1, y + 1, 4, 4, Rock.Shade);
            p.Fill(x + 1, y + 1, 4, 1, Rock.Highlight);
            p.Fill(x + 1, y + 1, 1, 4, Rock.Highlight);
            p.Put(x + 4, y + 1, Rock.Light);
            p.Put(x + 1, y + 4, Rock.Light);
            p.Fill(x + 2, y + 2, 2, 2, Soot);
        }

        /// <summary>
        /// B3 Store: a striped awning over the door across the shop front,
        /// sloping down toward the Road with a scalloped hem.
        /// </summary>
        private static void StoreAwning(Plate p, Rect2I roof, DoorSide side)
        {
            var depth = p.Small ? 3 : 6;
            var inset = p.Small ? 1 : 2;
            var area = side switch
            {
                DoorSide.North => new Rect2I(roof.Position.X + inset, roof.Position.Y - depth + 1, roof.Size.X - inset * 2, depth),
                DoorSide.East => new Rect2I(roof.End.X - 1, roof.Position.Y + inset, depth, roof.Size.Y - inset * 2),
                DoorSide.West => new Rect2I(roof.Position.X - depth + 1, roof.Position.Y + inset, depth, roof.Size.Y - inset * 2),
                _ => new Rect2I(roof.Position.X + inset, roof.End.Y - 1, roof.Size.X - inset * 2, depth),
            };
            Awning(p, area, side, Berry);
        }

        /// <summary>
        /// A striped canvas awning in <paramref name="area"/> (pixels), sloping
        /// down toward <paramref name="front"/>. Stripes run down the slope; the
        /// hem is a darker band whose stripe middles hang one pixel lower.
        /// </summary>
        private static void Awning(Plate p, Rect2I area, DoorSide front, Ramp stripe)
        {
            p.Fill(new Rect2I(area.Position.X + p.P(2), area.Position.Y + p.P(2), area.Size.X, area.Size.Y), Shadow);
            var across = front is DoorSide.South or DoorSide.North;
            var lit = front is DoorSide.North or DoorSide.West;
            var width = p.Small ? 2 : 3;
            int length = across ? area.Size.X : area.Size.Y, depth = across ? area.Size.Y : area.Size.X;
            for (var i = 0; i < length; i++)
                for (var j = 0; j < depth; j++)
                {
                    var striped = i / width % 2 == 0;
                    var ramp = striped ? stripe : Cloth;
                    var step = striped ? 2 : lit ? 4 : 3;
                    Color c;
                    if (j == depth - 1)
                    {
                        if (i % width != width / 2) continue;
                        c = ramp[step - 1];
                    }
                    else if (j == depth - 2) c = ramp[step - 1];
                    else if (j == 0 || i == 0 || i == length - 1) c = stripe.Edge;
                    else c = ramp[step];
                    var (x, y) = front switch
                    {
                        DoorSide.North => (area.Position.X + i, area.End.Y - 1 - j),
                        DoorSide.East => (area.Position.X + j, area.Position.Y + i),
                        DoorSide.West => (area.End.X - 1 - j, area.Position.Y + i),
                        _ => (area.Position.X + i, area.Position.Y + j),
                    };
                    p.Put(x, y, c);
                }
        }

        private static int StepOnly(Plate p, Rect2I roof, DoorSide side, int middle, int startRow, int stepRows)
        {
            var (from, to) = Span(p, middle, 3);
            return Step(p, roof, side, from, to, p.Small ? startRow / 2 + 1 : startRow, p.Small ? 1 : stepRows);
        }

        /// <summary>Tailor shop: a cream sign board with a red thread spool between timber flanges.</summary>
        private static void SpoolSign(Plate p, int cx, int cy)
        {
            if (p.Small)
            {
                p.Fill(cx - 1, cy - 1, 4, 4, SmallShadow);
                p.Fill(cx - 2, cy - 2, 4, 4, Timber.Edge);
                p.Fill(cx - 1, cy - 1, 2, 2, Berry.Base);
                p.Put(cx - 1, cy - 1, Berry.Light);
                return;
            }
            p.Fill(cx - 3, cy - 3, 9, 8, SmallShadow);
            p.Fill(cx - 4, cy - 4, 9, 8, Timber.Edge);
            p.Fill(cx - 3, cy - 3, 7, 6, Cloth.Light);
            p.Fill(cx - 3, cy - 3, 7, 1, Cloth.Highlight);
            p.Fill(cx - 2, cy - 2, 5, 1, Timber.Base);
            p.Fill(cx - 2, cy + 2, 5, 1, Timber.Shade);
            p.Fill(cx - 1, cy - 1, 3, 3, Berry.Base);
            p.Put(cx - 1, cy - 1, Berry.Light);
            p.Put(cx + 1, cy + 1, Berry.Shade);
            p.Put(cx + 2, cy, Berry.Shade);
            p.Put(cx + 3, cy + 1, Iron.Light);
        }

        /// <summary>
        /// Yard ground: packed earth in <paramref name="ground"/> with soft
        /// patches a little toward <paramref name="worn"/>, a few
        /// <paramref name="speck"/> specks (cinders in the forge yard) and a
        /// feathered edge, so it reads like the Roads and stays calm.
        /// </summary>
        private static void YardFloor(Plate p, Rect2I yard, Color ground, Color worn, Color speck, int salt)
        {
            var patch = ground.Lerp(worn, 0.35f);
            for (var y = yard.Position.Y; y < yard.End.Y; y++)
                for (var x = yard.Position.X; x < yard.End.X; x++)
                {
                    var fromX = Math.Min(x - yard.Position.X, yard.End.X - 1 - x);
                    var fromY = Math.Min(y - yard.Position.Y, yard.End.Y - 1 - y);
                    if (fromX + fromY < (p.Small ? 1 : 3)) continue;
                    var border = fromX == 0 || fromY == 0 || fromX + fromY == (p.Small ? 1 : 3);
                    var roll = PixelArt.Hash(x, y, salt) % 100;
                    if (border)
                    {
                        if (roll < 40) continue;
                        p.Put(x, y, worn with { A = 0.55f });
                        continue;
                    }
                    var blob = PixelArt.Hash((x + (y / 3 % 2) * 2) / 5, y / 3, salt + 1) % 4 == 0;
                    var c = roll < 3 && !p.Small ? speck : blob ? patch : ground;
                    p.Put(x, y, c);
                }
        }

        /// <summary>
        /// Blacksmith: in the forge yard, a stone hearth with its ember glow
        /// against the wall, an anvil on a stump and a quench barrel of water.
        /// A short yard (the 1 × 2 Blacksmith) has room for the hearth and anvil only.
        /// </summary>
        private static void ForgeYard(Plate p, Rect2I yard)
        {
            var tall = yard.Size.Y >= yard.Size.X;
            (int X, int Y) At(float f) => tall
                ? (yard.Position.X + yard.Size.X / 2, yard.Position.Y + (int)(yard.Size.Y * f))
                : (yard.Position.X + (int)(yard.Size.X * f), yard.Position.Y + yard.Size.Y / 2);
            var compact = (tall ? yard.Size.Y : yard.Size.X) < p.P(40);
            var hearth = At(compact ? 0.27f : 0.22f);
            var anvil = At(compact ? 0.76f : 0.52f);
            var barrel = At(0.8f);
            if (p.Small)
            {
                p.Fill(hearth.X - 2 + 1, hearth.Y - 2 + 1, 5, 4, SmallShadow);
                p.Fill(hearth.X - 2, hearth.Y - 2, 5, 4, Rock.Edge);
                p.Fill(hearth.X - 1, hearth.Y - 1, 3, 2, Rock.Light);
                p.Put(hearth.X, hearth.Y - 1, Ember);
                p.Put(hearth.X, hearth.Y, EmberHot);
                p.Fill(anvil.X - 1, anvil.Y, 3, 2, SmallShadow);
                p.Fill(anvil.X - 2, anvil.Y - 1, 3, 2, Iron.Edge);
                p.Put(anvil.X - 1, anvil.Y - 1, Iron.Highlight);
                return;
            }
            // Hearth: a stone box with a firepit of embers and a warm glow on the stone.
            int hx = hearth.X - 5, hy = hearth.Y - 4;
            p.Fill(hx + 2, hy + 3, 10, 9, SmallShadow);
            p.Fill(hx, hy, 10, 9, Rock.Edge);
            p.Fill(hx + 1, hy + 1, 8, 7, Rock.Shade);
            p.Fill(hx + 1, hy + 1, 7, 6, Rock.Base);
            p.Fill(hx + 1, hy + 1, 7, 1, Rock.Highlight);
            p.Fill(hx + 1, hy + 1, 1, 6, Rock.Light);
            p.Fill(hx + 3, hy + 3, 4, 3, Soot);
            p.Fill(hx + 3, hy + 3, 4, 3, Ember);
            p.Put(hx + 4, hy + 4, EmberHot);
            p.Put(hx + 5, hy + 4, EmberCore);
            p.Put(hx + 5, hy + 3, EmberHot);
            p.Fill(hx + 2, hy + 2, 6, 5, Ember with { A = 0.22f });
            // Anvil on a timber stump: body, horn toward the west, lit top edge.
            int ax = anvil.X, ay = anvil.Y;
            p.Disc(ax + 1.5f, ay + 2.5f, 4, SmallShadow);
            p.Disc(ax + 0.5f, ay + 0.5f, 4, Timber.Edge);
            p.Disc(ax + 0.5f, ay + 0.5f, 3, Timber.Base);
            p.Disc(ax + 0.5f, ay + 0.5f, 1.5f, Timber.Light);
            p.Fill(ax - 3, ay - 2, 7, 4, Iron.Edge);
            p.Fill(ax - 5, ay - 1, 2, 2, Iron.Edge);
            p.Put(ax - 6, ay - 1, Iron.Edge);
            p.Fill(ax - 2, ay - 1, 5, 2, Iron.Base);
            p.Fill(ax - 4, ay - 1, 2, 1, Iron.Light);
            p.Fill(ax - 2, ay - 1, 5, 1, Iron.Highlight);
            if (compact) return;
            // Quench barrel: a timber ring holding dark water with a glint.
            int bx = barrel.X, by = barrel.Y;
            p.Disc(bx + 1.5f, by + 2.5f, 3.5f, SmallShadow);
            p.Disc(bx + 0.5f, by + 0.5f, 3.5f, Timber.Edge);
            p.Disc(bx + 0.5f, by + 0.5f, 2.6f, Timber.Light);
            p.Disc(bx + 0.5f, by + 0.5f, 1.8f, River.Shade);
            p.Put(bx - 1, by - 1, River.Highlight);
        }

        /// <summary>
        /// Farmhouse: bound sheaves of grain laid out to dry in the yard, every
        /// other one turned the other way, as a stack is laid. Each sheaf has
        /// the shape of the grain item icon: three ears on stalks, tied at the
        /// waist. There is no painted cart, because a cart is a real vehicle.
        /// </summary>
        private static void Sheaves(Plate p, Rect2I yard)
        {
            int sheafWide = p.Small ? 4 : 11, sheafHigh = p.Small ? 6 : 13, gap = 1;
            var wide = yard.Size.X >= yard.Size.Y;
            var room = wide ? yard.Size.X - 2 : yard.Size.Y - 2;
            var step = wide ? sheafWide + gap : sheafHigh + gap;
            var count = Math.Clamp(room / step, 1, 3);
            var span = count * step - gap;
            for (var i = 0; i < count; i++)
            {
                var flip = i % 2 == 1;
                var left = wide ? yard.Position.X + (yard.Size.X - span) / 2 + i * step : yard.Position.X + (yard.Size.X - sheafWide) / 2;
                var top = wide ? yard.Position.Y + (yard.Size.Y - sheafHigh) / 2 : yard.Position.Y + (yard.Size.Y - span) / 2 + i * step;
                WithShadow(p, 1, p.Small ? 1 : 2, q => Sheaf(q, left, top, flip));
            }
        }

        /// <summary>
        /// One bound sheaf lying on the ground, its top-left at (left, top), ears
        /// to the north (south when <paramref name="flip"/>): three ears in Thatch
        /// light with an edge outline and a lit tip, stalks gathering to a twine
        /// band, and the cut stalk ends splayed at the foot.
        /// </summary>
        private static void Sheaf(Plate p, int left, int top, bool flip)
        {
            if (p.Small)
            {
                // Four by six: two ears, the band, the stalk foot.
                string[] rows = ["lh.h", "bllb", ".bs.", ".ts.", ".bs.", "b..s"];
                for (var j = 0; j < rows.Length; j++)
                {
                    var row = rows[flip ? rows.Length - 1 - j : j];
                    for (var i = 0; i < row.Length; i++)
                    {
                        Color? c = row[i] switch { 'l' => Thatch.Light, 'h' => Thatch.Highlight, 'b' => Thatch.Base, 's' => Thatch.Shade, 't' => Timber.Shade, _ => null };
                        if (c is { } colour) p.Put(left + i, top + j, colour);
                    }
                }
                return;
            }
            const float height = 13;
            var middle = left + 5.5f;
            float Y(float down) => flip ? top + height - down : top + down;
            // Stalks: from each ear down to the waist, then splayed out to the cut ends.
            foreach (var x in new[] { -3f, 0f, 3f })
            {
                p.Line(middle + x, Y(6), middle, Y(8.5f), Thatch.Shade);
                p.Line(middle, Y(9.5f), middle + x * 0.9f, Y(12.5f), Thatch.Base);
            }
            foreach (var x in new[] { -3f, 3f })
                p.Put((int)(middle + x * 0.9f), (int)Y(12.5f), Thatch.Edge);
            // Ears: the side ears first, the middle one standing a pixel further out over them.
            foreach (var (x, down) in new[] { (-3f, 4f), (3f, 4f), (0f, 3f) })
            {
                p.Ellipse(middle + x, Y(down), 2.1f, 3.3f, Thatch.Edge);
                p.Ellipse(middle + x, Y(down), 1.2f, 2.4f, Thatch.Light);
                p.Put((int)(middle + x) - 1, (int)Y(down - 1), Thatch.Highlight);
                p.Put((int)(middle + x), (int)Y(down + 1), Thatch.Base);
            }
            // The twine band at the waist.
            p.Fill((int)middle - 1, (int)Y(9), 3, 1, Timber.Shade);
        }

        /// <summary>
        /// Draws something onto a scratch plate, then lays a small shadow of its
        /// whole silhouette on <paramref name="p"/> (offset by dx, dy and painted
        /// once, so overlapping parts never darken twice) and the drawing over it.
        /// </summary>
        private static void WithShadow(Plate p, int dx, int dy, Action<Plate> draw)
        {
            var scratch = new Plate(p.Width, p.Height, p.Scale);
            draw(scratch);
            var shadow = new ShadowMask(p.Width, p.Height);
            for (var y = 0; y < p.Height; y++)
                for (var x = 0; x < p.Width; x++)
                    if (scratch.Image.GetPixel(x, y).A > 0) shadow.Add(x + dx, y + dy);
            shadow.Paint(p, SmallShadow);
            for (var y = 0; y < p.Height; y++)
                for (var x = 0; x < p.Width; x++)
                    p.Put(x, y, scratch.Image.GetPixel(x, y));
        }

        /// <summary>
        /// Farmhouse: grain sacks by the doorstep, one lying on the west or
        /// north side of the step with its tied neck pointing away, one standing
        /// on the other side, and a few spilled grains.
        /// </summary>
        private static void GrainSacks(Plate p, Rect2I roof, DoorSide side, int middle)
        {
            var alongX = side is DoorSide.South or DoorSide.North;
            if (p.Small)
            {
                foreach (var t in new[] { middle - 4, middle + 3 })
                {
                    var (sx, sy) = OutPoint(roof, side, 2, t);
                    p.Fill((int)sx, (int)sy, 2, 2, SmallShadow);
                    p.Put((int)sx - 1, (int)sy - 1, Cloth.Light);
                    p.Put((int)sx, (int)sy - 1, Cloth.Base);
                    p.Put((int)sx - 1, (int)sy, Cloth.Base);
                    p.Put((int)sx, (int)sy, Cloth.Shade);
                }
                return;
            }
            var (lx, ly) = OutPoint(roof, side, 4, middle - 8);
            LyingSack(p, lx, ly, alongX);
            var (standX, standY) = OutPoint(roof, side, 4, middle + 7);
            StandingSack(p, standX, standY);
            foreach (var (k, t, colour) in new[] { (6, middle + 5, Thatch.Light), (7, middle + 6, Thatch.Base), (7, middle + 9, Thatch.Light) })
            {
                var (gx, gy) = OutPoint(roof, side, k, t);
                p.Put((int)gx, (int)gy, colour);
            }
        }

        /// <summary>
        /// The middle of the pixel <paramref name="k"/> rows out from the roof edge
        /// on the door side, <paramref name="t"/> along it (as <see cref="Out"/>).
        /// </summary>
        private static (float X, float Y) OutPoint(Rect2I roof, DoorSide side, int k, int t) => side switch
        {
            DoorSide.North => (t + 0.5f, roof.Position.Y - k + 0.5f),
            DoorSide.East => (roof.End.X - 1 + k + 0.5f, t + 0.5f),
            DoorSide.West => (roof.Position.X - k + 0.5f, t + 0.5f),
            _ => (t + 0.5f, roof.End.Y - 1 + k + 0.5f),
        };

        /// <summary>
        /// A cloth sack standing on its base, seen from above: a round body lit
        /// north-west with a shaded south-east crescent, and the gathered neck in
        /// the middle, a pale tuft tied round with twine.
        /// </summary>
        private static void StandingSack(Plate p, float cx, float cy)
        {
            p.Disc(cx + 1, cy + 1.5f, 2.6f, SmallShadow);
            p.Disc(cx, cy, 2.75f, Cloth.Edge);
            p.Disc(cx + 0.4f, cy + 0.4f, 1.9f, Cloth.Shade);
            p.Disc(cx - 0.3f, cy - 0.3f, 1.6f, Cloth.Base);
            var x = (int)cx;
            var y = (int)cy;
            p.Put(x - 1, y - 1, Cloth.Light);
            p.Put(x - 1, y, Cloth.Light);
            p.Put(x, y, Cloth.Highlight);
            p.Put(x + 1, y, Timber.Light);
            p.Put(x, y + 1, Timber.Light);
        }

        /// <summary>
        /// A cloth sack lying on its side along the x or y axis: a long rounded
        /// body, lit north-west, with its neck tied off in twine at the west or
        /// north end and the tuft flaring past it. Drawn at 32 px only.
        /// </summary>
        private static void LyingSack(Plate p, float cx, float cy, bool alongX)
        {
            var (rx, ry) = alongX ? (3.5f, 2.5f) : (2.5f, 3.5f);
            p.Ellipse(cx + 1, cy + 1.5f, rx, ry, SmallShadow);
            p.Ellipse(cx, cy, rx, ry, Cloth.Edge);
            p.Ellipse(cx + 0.4f, cy + 0.4f, rx - 0.9f, ry - 0.9f, Cloth.Shade);
            p.Ellipse(cx - 0.3f, cy - 0.3f, rx - 1.2f, ry - 1.2f, Cloth.Base);
            p.Ellipse(cx - 1, cy - 1, 0.9f, 0.9f, Cloth.Light);
            // A fold across the body where the cloth sags, and the neck tied off.
            var (x, y) = ((int)cx, (int)cy);
            if (alongX) p.Put(x + 1, y, Cloth.Shade);
            else p.Put(x, y + 1, Cloth.Shade);
            if (alongX)
            {
                var end = (int)(cx - rx);
                p.Put(end, y, Timber.Shade);
                p.Put(end - 1, y, Cloth.Light);
                p.Put(end - 2, y - 1, Cloth.Edge);
                p.Put(end - 2, y, Cloth.Base);
                p.Put(end - 2, y + 1, Cloth.Edge);
            }
            else
            {
                var end = (int)(cy - ry);
                p.Put(x, end, Timber.Shade);
                p.Put(x, end - 1, Cloth.Light);
                p.Put(x - 1, end - 2, Cloth.Edge);
                p.Put(x, end - 2, Cloth.Base);
                p.Put(x + 1, end - 2, Cloth.Edge);
            }
        }

        /// <summary>
        /// Workshop yard: a trestle bench (a long board on two trestles) with a
        /// saw on it and shavings around, and a neat stack of planks, on a worn
        /// patch of earth.
        /// </summary>
        private static void WorkYard(Plate p, Rect2I yard)
        {
            var tall = yard.Size.Y >= yard.Size.X;
            (int X, int Y) At(float f) => tall
                ? (yard.Position.X + yard.Size.X / 2, yard.Position.Y + (int)(yard.Size.Y * f))
                : (yard.Position.X + (int)(yard.Size.X * f), yard.Position.Y + yard.Size.Y / 2);
            var bench = At(0.3f);
            var stack = At(0.72f);
            // Rectangles written along the yard (a) and across it (b), from a centre.
            Rect2I Box((int X, int Y) c, int a, int b, int along, int across) => tall
                ? new Rect2I(c.X + b, c.Y + a, across, along)
                : new Rect2I(c.X + a, c.Y + b, along, across);
            if (p.Small)
            {
                p.Fill(Box(bench, -4, -1, 8, 3), SmallShadow);
                p.Fill(Box(bench, -4, -1, 8, 2), Timber.Light);
                p.Fill(Box(stack, -3, -2, 6, 4), Timber.Edge);
                p.Fill(Box(stack, -2, -1, 4, 2), Timber.Base);
                return;
            }
            var shadow = new ShadowMask(p.Width, p.Height);
            foreach (var r in new[] { Box(bench, -8, -2, 16, 5), Box(bench, -6, -5, 2, 11), Box(bench, 4, -5, 2, 11), Box(stack, -7, -5, 15, 11) })
                shadow.Add(r, 1, 2);
            shadow.Paint(p, SmallShadow);
            // Trestles: short beams across the bench, their splayed feet just showing at the ends.
            foreach (var a in new[] { -6, 4 })
            {
                p.Fill(Box(bench, a, -5, 2, 11), Timber.Edge);
                p.Fill(Box(bench, a, -4, 1, 9), Timber.Shade);
            }
            // The board: pale, lit along its north or west edge, with a saw lying on it.
            p.Fill(Box(bench, -8, -2, 16, 5), Timber.Edge);
            p.Fill(Box(bench, -7, -1, 14, 3), Timber.Light);
            p.Fill(Box(bench, -7, -1, 14, 1), Timber.Highlight);
            p.Fill(Box(bench, -5, 0, 5, 1), Iron.Light);
            p.Fill(Box(bench, -5, 1, 4, 1), Iron.Base);
            p.Fill(Box(bench, 0, 0, 2, 2), Timber.Shade);
            foreach (var (a, b) in new[] { (-9, 4), (7, -4), (9, 3), (-3, 5) })
            {
                var spot = Box(bench, a, b, 1, 1);
                p.Put(spot.Position.X, spot.Position.Y, Timber.Highlight);
            }
            // Plank stack: boards laid side by side, ends a little ragged, seams one step darker.
            p.Fill(Box(stack, -7, -5, 15, 11), Timber.Edge);
            for (var i = 0; i < 3; i++)
            {
                var offset = i == 1 ? 1 : 0;
                var board = Box(stack, -6 + offset, -4 + i * 3, 13 - offset, 3);
                p.Fill(board, Timber.Base);
                p.Fill(Box(stack, -6 + offset, -4 + i * 3, 13 - offset, 1), Timber.Light);
                p.Fill(Box(stack, -6 + offset, -2 + i * 3, 13 - offset, 1), Toward(Timber, 2, 1, 0.6f));
            }
        }

        /// <summary>Workshop: a cream sign board with an iron-headed hammer over the door.</summary>
        private static void HammerSign(Plate p, int cx, int cy)
        {
            if (p.Small)
            {
                p.Fill(cx - 1, cy - 1, 4, 4, SmallShadow);
                p.Fill(cx - 2, cy - 2, 4, 4, Timber.Edge);
                p.Fill(cx - 1, cy - 1, 2, 2, Cloth.Light);
                p.Put(cx, cy - 1, Iron.Base);
                return;
            }
            p.Fill(cx - 3, cy - 3, 9, 8, SmallShadow);
            p.Fill(cx - 4, cy - 4, 9, 8, Timber.Edge);
            p.Fill(cx - 3, cy - 3, 7, 6, Cloth.Light);
            p.Fill(cx - 3, cy - 3, 7, 1, Cloth.Highlight);
            // Handle running down to the south-west, head across its top.
            p.Put(cx - 2, cy + 2, Timber.Shade);
            p.Put(cx - 1, cy + 1, Timber.Base);
            p.Put(cx, cy, Timber.Base);
            p.Put(cx - 1, cy + 2, Timber.Edge);
            p.Fill(cx - 1, cy - 3, 4, 2, Iron.Base);
            p.Fill(cx - 1, cy - 3, 4, 1, Iron.Light);
            p.Put(cx + 2, cy - 2, Iron.Edge);
            p.Put(cx + 1, cy - 1, Iron.Edge);
            p.Put(cx - 1, cy - 3, Iron.Highlight);
        }

        // ------------------------------------------------------------------
        // Silo.
        // ------------------------------------------------------------------

        /// <summary>
        /// Silo: a round silo seen from straight above. Its conical board roof
        /// is a set of facets between faint seams; each facet takes a tone from
        /// how much it faces the north-west light, so the cone reads lit on one
        /// side and shaded on the other. A one-pixel edge, a darker eave ring
        /// and a small capped vent at the top finish it.
        /// </summary>
        private static void PaintSilo(Plate p, int w, int h)
        {
            var scale = p.Scale;
            var cx = w / 2f * scale;
            var cy = h / 2f * scale;
            var radius = (Math.Min(w, h) / 2f - 3) * scale;
            var seams = p.Small ? 8 : 16;
            var light = MathF.Atan2(-1, -1); // north-west
            p.Disc(cx + p.P(2), cy + p.P(3), radius, Shadow);
            for (var y = (int)(cy - radius - 1); y <= (int)(cy + radius + 1); y++)
                for (var x = (int)(cx - radius - 1); x <= (int)(cx + radius + 1); x++)
                {
                    var dx = x + 0.5f - cx;
                    var dy = y + 0.5f - cy;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > radius) continue;
                    var angle = MathF.Atan2(dy, dx);
                    var sector = MathF.Floor((angle + MathF.PI) / MathF.Tau * seams);
                    var centre = (sector + 0.5f) / seams * MathF.Tau - MathF.PI;
                    // 0 = facing away from the light, 1 = facing it; spread over shade..light.
                    var facing = (MathF.Cos(centre - light) + 1) / 2;
                    var tone = Tone(SiloWood, 1 + facing * 2.2f);
                    Color c;
                    if (distance > radius - 1) c = SiloWood.Edge;
                    else if (distance > radius - 2) c = tone.Lerp(SiloWood.Edge, 0.45f);
                    else
                    {
                        var boundary = (angle + MathF.PI) / MathF.Tau * seams - sector;
                        var nearSeam = MathF.Min(boundary, 1 - boundary) * MathF.Tau / seams * distance < 0.5f;
                        c = nearSeam && distance > (p.Small ? 1.5f : 3.5f) ? tone.Lerp(SiloWood.Edge, 0.28f) : tone;
                    }
                    p.Put(x, y, c);
                }
            // The vent cap at the apex: a dark rim around a lit lid.
            if (p.Small)
            {
                p.Put((int)cx, (int)cy, SiloWood.Edge);
                p.Put((int)cx - 1, (int)cy - 1, SiloWood.Highlight);
                return;
            }
            p.Disc(cx + 1, cy + 1, 2.4f, RoofShadow);
            p.Disc(cx, cy, 2.4f, SiloWood.Edge);
            p.Disc(cx - 0.3f, cy - 0.3f, 1.5f, SiloWood.Light);
            p.Put((int)cx - 1, (int)cy - 1, SiloWood.Highlight);
        }

        /// <summary>A tone between ramp steps: 1.5 is halfway from shade to base.</summary>
        private static Color Tone(Ramp r, float step)
        {
            var low = (int)MathF.Floor(step);
            return r[low].Lerp(r[low + 1], step - low);
        }

        /// <summary>Clamps to [min, max], or takes the middle when a small footprint leaves no room.</summary>
        private static float Fit(float value, float min, float max) => max < min ? (min + max) / 2 : Math.Clamp(value, min, max);

        // ------------------------------------------------------------------
        // Small helpers.
        // ------------------------------------------------------------------

        /// <summary>A five-step colour ramp from the art style guide: edge, shade, base, light, highlight.</summary>
        private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
        {
            public static Ramp Of(string edge, string shade, string @base, string light, string highlight) =>
                new(new Color(edge), new Color(shade), new Color(@base), new Color(light), new Color(highlight));

            /// <summary>Step 0 is the edge, 4 the highlight; out-of-range steps clamp.</summary>
            public Color this[int step] => step switch
            {
                <= 0 => Edge,
                1 => Shade,
                2 => Base,
                3 => Light,
                _ => Highlight,
            };
        }

        /// <summary>
        /// Collects the pixels a shadow covers and paints each once, so
        /// overlapping parts (bench, trestles, sheaves) never darken the ground twice.
        /// </summary>
        private sealed class ShadowMask(int width, int height)
        {
            private readonly bool[] covered = new bool[width * height];

            public void Add(int x, int y)
            {
                if (x >= 0 && y >= 0 && x < width && y < height) covered[y * width + x] = true;
            }

            public void Add(Rect2I area, int dx, int dy)
            {
                for (var y = area.Position.Y; y < area.End.Y; y++)
                    for (var x = area.Position.X; x < area.End.X; x++)
                        Add(x + dx, y + dy);
            }

            public void Paint(Plate p, Color color)
            {
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                        if (covered[y * width + x]) p.Put(x, y, color);
            }
        }

        /// <summary>
        /// The image a building is painted on, with the scale from the 32-unit
        /// tile space layouts are written in to output pixels (1 at 32 px, 0.5
        /// at 16 px). Painting blends, so shadows and soft edges compose. Every
        /// blended colour is snapped to the nearest 8-bit step before it is
        /// stored: Godot truncates a channel when it stores it, so without this
        /// a half-transparent shadow could land one step off the reviewed art.
        /// </summary>
        private sealed class Plate
        {
            public Plate(int width, int height, float scale)
            {
                Width = width;
                Height = height;
                Scale = scale;
                Image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
                Image.Fill(Colors.Transparent);
            }

            public Image Image { get; }
            public int Width { get; }
            public int Height { get; }
            public float Scale { get; }
            public bool Small => Scale < 1;

            /// <summary>Units to pixels, rounded.</summary>
            public int P(float units) => (int)MathF.Round(units * Scale);

            public Rect2I Px(Rect2 units)
            {
                var left = P(units.Position.X);
                var top = P(units.Position.Y);
                return new Rect2I(left, top, P(units.End.X) - left, P(units.End.Y) - top);
            }

            public void Put(int x, int y, Color color)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height || color.A <= 0) return;
                var blended = Image.GetPixel(x, y).Blend(color);
                Image.SetPixel(x, y, new Color(Step(blended.R), Step(blended.G), Step(blended.B), Step(blended.A)));
            }

            public void Fill(Rect2I area, Color color)
            {
                for (var y = area.Position.Y; y < area.End.Y; y++)
                    for (var x = area.Position.X; x < area.End.X; x++)
                        Put(x, y, color);
            }

            public void Fill(int x, int y, int width, int height, Color color) => Fill(new Rect2I(x, y, width, height), color);

            /// <summary>A disc centred on a pixel position, radius in pixels.</summary>
            public void Disc(float x, float y, float radius, Color color) =>
                Ellipse(x / Scale, y / Scale, radius / Scale, radius / Scale, color);

            /// <summary>An ellipse in tile units, covering the same pixels as <see cref="PixelCanvas.Ellipse"/>.</summary>
            public void Ellipse(float centerX, float centerY, float radiusX, float radiusY, Color color)
            {
                var cx = centerX * Scale;
                var cy = centerY * Scale;
                var rx = Math.Max(0.6f, radiusX * Scale);
                var ry = Math.Max(0.6f, radiusY * Scale);
                for (var y = (int)(cy - ry - 1); y <= (int)(cy + ry + 1); y++)
                    for (var x = (int)(cx - rx - 1); x <= (int)(cx + rx + 1); x++)
                    {
                        var dx = (x + 0.5f - cx) / rx;
                        var dy = (y + 0.5f - cy) / ry;
                        if (dx * dx + dy * dy <= 1f) Put(x, y, color);
                    }
            }

            /// <summary>A one-pixel line in tile units, in an opaque colour.</summary>
            public void Line(float fromX, float fromY, float toX, float toY, Color color) =>
                new PixelCanvas(Image, new Rect2I(0, 0, Width, Height), Scale).Line(fromX, fromY, toX, toY, color);

            private static float Step(float channel) => Math.Clamp(MathF.Round(channel * 255f), 0f, 255f) / 255f;
        }
    }
}
