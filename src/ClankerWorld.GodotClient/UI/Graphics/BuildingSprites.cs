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
    Workshop,
    Path,
    Bedroll,
    Generic,
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
/// building faces its Road from. The House, Warehouse, Blacksmith, Silo and
/// Tailor shop use the drawing the owner approved in the first art review
/// (<see cref="ApprovedArt"/>); the Farmhouse, Workshop, generic building and
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
        BuildingKind.Farmhouse => (new Color("D2AE5E"), new Color("A98A45"), new Color("6B5528"), new Color("E6C77B")),
        BuildingKind.Shelter => (new Color("8C8A4E"), new Color("6D6B3C"), new Color("403F22"), new Color("A8A564")),
        BuildingKind.Storehouse => (new Color("8E6C47"), new Color("6E5236"), new Color("3F2E1F"), new Color("AC8A60")),
        BuildingKind.Workshop => (new Color("6F7C6A"), new Color("566150"), new Color("30372D"), new Color("8E9B88")),
        BuildingKind.Path => (new Color("A89F8C"), new Color("857C69"), new Color("5F5848"), new Color("C4BBA6")),
        BuildingKind.Bedroll => (new Color("A0523E"), new Color("7E3F30"), new Color("4A2A20"), new Color("C97A5E")),
        _ => (new Color("8D8577"), new Color("6E675C"), new Color("3F3A33"), new Color("AAA293")),
    };

    /// <summary>The earlier provisional drawing, kept for the kinds the art review has not redrawn.</summary>
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
        switch (kind)
        {
            case BuildingKind.Farmhouse:
                Thatch(canvas, inset, roofWidth, roofHeight, palette.Edge with { A = 0.35f });
                Doorstep(canvas, roof, door);
                break;
            case BuildingKind.Storehouse:
                DoorBand(canvas, roof, door, 6, 0, new Color("4A3321"));
                break;
            case BuildingKind.Workshop:
                // A hammer sign over the door marks a place for making things.
                var (signX, signY) = Inward(roof, door, 9);
                canvas.Rect(signX - 5, signY - 4, 10, 9, palette.Edge);
                canvas.Rect(signX - 4, signY - 3, 8, 7, new Color("B99A6B"));
                canvas.Line(signX - 2, signY + 2, signX + 1.5f, signY - 1.5f, new Color("5A3E28"));
                canvas.Rect(signX, signY - 3, 3, 2, new Color("6E737A"));
                canvas.Rect(signX + 1, signY - 1, 2, 1, new Color("6E737A"));
                Doorstep(canvas, roof, door);
                break;
            case BuildingKind.Generic:
                Doorstep(canvas, roof, door);
                break;
        }
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

    /// <summary>Uneven straw bundles in rows, for thatched roofs.</summary>
    private static void Thatch(PixelCanvas canvas, float inset, float roofWidth, float roofHeight, Color strand)
    {
        if (canvas.Unit < 1) return;
        for (var row = 0; row < roofHeight - 4; row += 3)
            for (var column = 0; column < roofWidth - 4; column += 4)
            {
                var jitter = (int)(PixelArt.Hash(column, row, 41) % 3);
                canvas.Rect(inset + 2 + column + jitter, inset + 2 + row, 2, 1, strand);
            }
    }

    /// <summary>A small stone doorstep with a darker outer edge.</summary>
    private static void Doorstep(PixelCanvas canvas, Rect2 roof, BuildingDoor door)
    {
        DoorBand(canvas, roof, door, 6, 0, new Color("B9AB8E"));
        DoorBand(canvas, roof, door, 6, 2, new Color("8C7F66"), 1);
    }

    /// <summary>
    /// A strip across the door, <paramref name="across"/> pixels wide, that
    /// overlaps the roof edge by one pixel and reaches two beyond it.
    /// <paramref name="outward"/> and <paramref name="depth"/> pick rows of it,
    /// counted out from the roof edge.
    /// </summary>
    private static void DoorBand(PixelCanvas canvas, Rect2 roof, BuildingDoor door, float across, float outward,
        Color color, float depth = 3)
    {
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

    /// <summary>A point inside the roof, <paramref name="distance"/> pixels in from the door.</summary>
    private static (float X, float Y) Inward(Rect2 roof, BuildingDoor door, float distance)
    {
        var middle = DoorMiddle(roof, door);
        return door.Side switch
        {
            DoorSide.North => (middle, roof.Position.Y + distance),
            DoorSide.East => (roof.End.X - distance, middle),
            DoorSide.West => (roof.Position.X + distance, middle),
            _ => (middle, roof.End.Y - distance),
        };
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
    /// The building exteriors the owner approved in the first art review
    /// (2026-10-01): House, Warehouse, Blacksmith, Silo and Tailor shop, at
    /// any footprint and door side. The layout keeps the earlier contract:
    /// the roof sits three units in from the footprint, the door is on the
    /// side that faces the Road, and the doorstep path starts at the edge.
    /// Each roof is laid in a real material (clay tiles, slate, planks,
    /// shingles) in courses that follow its eaves, lit from the north-west,
    /// with ridge and hip lines, an eave shadow, a visible door over a stone
    /// doorstep, and one identifying feature per kind. Layouts are written in
    /// 32-unit tile space and drawn at 32 or 16 px per tile.
    /// </summary>
    private static class ApprovedArt
    {
        // Every colour is a step of an art style guide ramp.
        private static readonly Ramp Clay = Ramp.Of("5E2E22", "9E4E34", "C66A45", "E08E64", "EFA982");
        private static readonly Ramp Slate = Ramp.Of("2B2E33", "4A4E55", "62666E", "80858E", "9A9FA7");
        private static readonly Ramp GreyTimber = Ramp.Of("343C43", "59656F", "758390", "97A5B0", "AEBBC4");
        private static readonly Ramp SiloWood = Ramp.Of("54462F", "8E7A58", "B7A07A", "D3C09A", "E4D4B4");
        private static readonly Ramp DyedShingle = Ramp.Of("3E2B47", "6E4F7C", "8F6A9E", "B08CBE", "C8A8D4");
        private static readonly Ramp Timber = Ramp.Of("3F2A1A", "6E4E31", "8A6440", "A77C52", "D2AC77");
        private static readonly Ramp Doorstep = Ramp.Of("5F5848", "8C7F66", "B9AB8E", "C9BDA2", "DED3BC");
        private static readonly Ramp Rock = Ramp.Of("4A4542", "625B56", "756D68", "8B837D", "A49C95");
        private static readonly Ramp Iron = Ramp.Of("3E3A37", "524C48", "6C6560", "8A827C", "A69E98");
        private static readonly Ramp Cloth = Ramp.Of("75674D", "A09170", "CABC99", "E8DCC0", "FFF5DF");
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
        private enum Material { Clay, Slate, Shingle, Plank }

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
            BuildingKind.Blacksmith => new(Slate, Material.Slate, RoofShape.Gable, 100, Yard: 20),
            BuildingKind.TailorShop => new(DyedShingle, Material.Shingle, RoofShape.Hip, 162),
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
            var doorHalf = kind == BuildingKind.Warehouse ? 7f : 3f;
            var plan = Lay(w, h, door, recipe, doorHalf);
            var roof = p.Px(plan.Roof);
            var middle = p.P(plan.DoorMiddle);

            // Only the Blacksmith has a yard: its packed-earth forge yard.
            if (plan.Yard is { } yardUnits) YardFloor(p, p.Px(yardUnits), Dirt.Shade, Dirt.Edge, 113);
            PaintRoof(p, roof, recipe);

            var nextRow = 0;
            switch (kind)
            {
                case BuildingKind.House:
                    var (cx, cy) = ChimneySpot(roof, p.Small, door.Side, middle);
                    Chimney(p, cx, cy);
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

            // The roof's one-pixel edge.
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                {
                    if (x != 0 && y != 0 && x != w - 1 && y != h - 1) continue;
                    p.Put(x0 + x, y0 + y, ramp.Edge);
                }

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
        /// on the south-east hip.
        /// </summary>
        private static void RidgeAndHips(Plate p, int left, int top, int iw, int ih, Recipe recipe)
        {
            var r = recipe.Roof;
            var hip = recipe.Shape == RoofShape.Hip;
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
            var litHip = Toward(r, 2, 3, 0.8f);
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
                    Put(length - 1 - t, breadth - 1 - t, shadedHip);
                }
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
        /// patches a little toward <paramref name="worn"/>, a few cinders and a
        /// feathered edge, so it reads like the Roads and stays calm.
        /// </summary>
        private static void YardFloor(Plate p, Rect2I yard, Color ground, Color worn, int salt)
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
                    var c = roll < 3 && !p.Small ? Iron.Shade : blob ? patch : ground;
                    p.Put(x, y, c);
                }
        }

        /// <summary>
        /// Blacksmith: in the forge yard, a stone hearth with its ember glow
        /// against the wall, an anvil on a stump and a quench barrel of water.
        /// </summary>
        private static void ForgeYard(Plate p, Rect2I yard)
        {
            var tall = yard.Size.Y >= yard.Size.X;
            (int X, int Y) At(float f) => tall
                ? (yard.Position.X + yard.Size.X / 2, yard.Position.Y + (int)(yard.Size.Y * f))
                : (yard.Position.X + (int)(yard.Size.X * f), yard.Position.Y + yard.Size.Y / 2);
            var hearth = At(0.22f);
            var anvil = At(0.52f);
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
            // Quench barrel: a timber ring holding dark water with a glint.
            int bx = barrel.X, by = barrel.Y;
            p.Disc(bx + 1.5f, by + 2.5f, 3.5f, SmallShadow);
            p.Disc(bx + 0.5f, by + 0.5f, 3.5f, Timber.Edge);
            p.Disc(bx + 0.5f, by + 0.5f, 2.6f, Timber.Light);
            p.Disc(bx + 0.5f, by + 0.5f, 1.8f, River.Shade);
            p.Put(bx - 1, by - 1, River.Highlight);
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
        /// The image a building is painted on, with the scale from the 32-unit
        /// tile space layouts are written in to output pixels (1 at 32 px, 0.5
        /// at 16 px). Painting blends, so shadows and soft edges compose.
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
                Image.SetPixel(x, y, Image.GetPixel(x, y).Blend(color));
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
                new PixelCanvas(Image, new Rect2I(0, 0, Width, Height), Scale).Disc(x / Scale, y / Scale, radius / Scale, color);
        }
    }
}
