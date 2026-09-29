using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Visual family of a placed building or legacy camp object.</summary>
public enum BuildingKind : byte
{
    House,
    Warehouse,
    Farmhouse,
    Blacksmith,
    Shelter,
    Storehouse,
    Hearth,
    Weaving,
    Workshop,
    Path,
    Bedroll,
    Generic,
}

/// <summary>
/// Provisional top-down pixel-art roofs for placed buildings, generated at
/// their footprint size (32 or 16 px per tile). Each design has one standard
/// appearance, per the vision ledger: roofs show the building's family, the
/// ridge follows its long side, and a small doorstep marks the south side.
/// </summary>
public static class BuildingSprites
{
    private static readonly Dictionary<(BuildingKind Kind, int Width, int Height, int Tile), ImageTexture> Cache = [];
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
        if (Has("workshop")) return BuildingKind.Workshop;
        if (Has("weaving")) return BuildingKind.Weaving;
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
    public static Color RoofColor(BuildingKind kind) => Palette(kind).Lit;

    public static int AtlasTileSize(int drawnTileSize) => drawnTileSize >= 24 ? 32 : 16;

    public static ImageTexture Texture(BuildingKind kind, int width, int height, int tilePixels)
    {
        width = Math.Clamp(width, 1, 8);
        height = Math.Clamp(height, 1, 8);
        var key = (kind, width, height, tilePixels);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(Render(kind, width, height, tilePixels));
        Cache[key] = texture;
        return texture;
    }

    public static Image Render(BuildingKind kind, int width, int height, int tilePixels)
    {
        var image = Image.CreateEmpty(width * tilePixels, height * tilePixels, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        Paint(new PixelCanvas(image, new Rect2I(0, 0, width * tilePixels, height * tilePixels), tilePixels / 32f),
            kind, width * 32, height * 32);
        return image;
    }

    private static (Color Lit, Color Shade, Color Edge, Color Ridge) Palette(BuildingKind kind) => kind switch
    {
        BuildingKind.House => (new Color("C66A45"), new Color("9E4E34"), new Color("5E2E22"), new Color("E08E64")),
        BuildingKind.Warehouse => (new Color("758390"), new Color("59656F"), new Color("343C43"), new Color("97A5B0")),
        BuildingKind.Farmhouse => (new Color("D2AE5E"), new Color("A98A45"), new Color("6B5528"), new Color("E6C77B")),
        BuildingKind.Blacksmith => (new Color("62666E"), new Color("4A4E55"), new Color("2B2E33"), new Color("80858E")),
        BuildingKind.Shelter => (new Color("8C8A4E"), new Color("6D6B3C"), new Color("403F22"), new Color("A8A564")),
        BuildingKind.Storehouse => (new Color("8E6C47"), new Color("6E5236"), new Color("3F2E1F"), new Color("AC8A60")),
        BuildingKind.Workshop => (new Color("6F7C6A"), new Color("566150"), new Color("30372D"), new Color("8E9B88")),
        BuildingKind.Path => (new Color("A89F8C"), new Color("857C69"), new Color("5F5848"), new Color("C4BBA6")),
        BuildingKind.Bedroll => (new Color("A0523E"), new Color("7E3F30"), new Color("4A2A20"), new Color("C97A5E")),
        _ => (new Color("8D8577"), new Color("6E675C"), new Color("3F3A33"), new Color("AAA293")),
    };

    private static void Paint(PixelCanvas canvas, BuildingKind kind, int width, int height)
    {
        switch (kind)
        {
            case BuildingKind.Hearth:
                PaintHearth(canvas, width, height);
                return;
            case BuildingKind.Weaving:
                PaintWeavingFrame(canvas, width, height);
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

        switch (kind)
        {
            case BuildingKind.House:
                Chimney(canvas, inset + roofWidth - 9, inset + 3, new Color("7B756E"), new Color("3A3632"));
                Doorstep(canvas, width, height, inset, roofHeight);
                break;
            case BuildingKind.Warehouse:
                // Wide loading doors on the south side.
                var doorWidth = Math.Min(16, roofWidth - 8);
                canvas.Rect(width / 2f - doorWidth / 2f, inset + roofHeight - 1, doorWidth, 3, new Color("5A3E28"));
                canvas.Rect(width / 2f - 0.5f, inset + roofHeight - 1, 1, 3, new Color("C9A36B"));
                break;
            case BuildingKind.Farmhouse:
                Thatch(canvas, inset, roofWidth, roofHeight, palette.Edge with { A = 0.35f });
                Doorstep(canvas, width, height, inset, roofHeight);
                break;
            case BuildingKind.Blacksmith:
                // A forge chimney with a glowing ember marks metalworking.
                Chimney(canvas, inset + 3, inset + 3, new Color("5A5550"), new Color("2A2622"));
                canvas.Disc(inset + 6, inset + 6, 1.6f, new Color("F0732A"));
                canvas.Dot(inset + 6, inset + 6, new Color("FFD27A"));
                Doorstep(canvas, width, height, inset, roofHeight);
                break;
            case BuildingKind.Storehouse:
                canvas.Rect(width / 2f - 3, inset + roofHeight - 1, 6, 3, new Color("4A3321"));
                break;
            case BuildingKind.Workshop:
                // A hammer sign over the door marks a place for making things.
                var signX = width / 2f;
                var signY = inset + roofHeight - 9;
                canvas.Rect(signX - 5, signY - 4, 10, 9, palette.Edge);
                canvas.Rect(signX - 4, signY - 3, 8, 7, new Color("B99A6B"));
                canvas.Line(signX - 2, signY + 2, signX + 1.5f, signY - 1.5f, new Color("5A3E28"));
                canvas.Rect(signX, signY - 3, 3, 2, new Color("6E737A"));
                canvas.Rect(signX + 1, signY - 1, 2, 1, new Color("6E737A"));
                Doorstep(canvas, width, height, inset, roofHeight);
                break;
        }
    }

    private static void Chimney(PixelCanvas canvas, float x, float y, Color stone, Color opening)
    {
        canvas.Rect(x, y, 6, 6, stone.Darkened(0.35f));
        canvas.Rect(x + 1, y + 1, 4, 4, stone);
        canvas.Rect(x + 2, y + 2, 2, 2, opening);
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

    private static void Doorstep(PixelCanvas canvas, int width, int height, float inset, float roofHeight)
    {
        var y = inset + roofHeight - 1;
        if (y + 3 > height) return;
        canvas.Rect(width / 2f - 3, y, 6, 3, new Color("B9AB8E"));
        canvas.Rect(width / 2f - 3, y + 2, 6, 1, new Color("8C7F66"));
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

    private static void PaintWeavingFrame(PixelCanvas canvas, int width, int height)
    {
        canvas.Rect(7, 8, width - 12, height - 14, Shadow);
        canvas.Rect(5, 6, width - 10, 2, new Color("7A5634"));
        canvas.Rect(5, height - 10, width - 10, 2, new Color("7A5634"));
        canvas.Rect(5, 6, 2, height - 14, new Color("6A4A2C"));
        canvas.Rect(width - 7, 6, 2, height - 14, new Color("6A4A2C"));
        for (var thread = 9; thread < width - 8; thread += 3)
            canvas.Rect(thread, 8, 1, height - 18, new Color("E8DDBF"));
        canvas.Rect(8, height / 2f - 2, width - 16, 4, new Color("B2533E"));
    }
}
