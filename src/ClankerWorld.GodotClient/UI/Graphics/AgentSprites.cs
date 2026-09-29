using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Provisional straight top-down agent sprites: head and hair seen from
/// above over shoulders and arms, with a few appearance variants per life
/// stage and no genders, as the asset roster describes. Worn clothing does
/// not change the sprite; the shirt color is part of the appearance variant.
/// </summary>
public static class AgentSprites
{
    public const int VariantCount = 6;
    private static readonly string[] Stages = ["infant", "child", "adult", "elder"];
    private static readonly Dictionary<int, ImageTexture> Textures = [];
    private static readonly Dictionary<int, Image> Images = [];
    private static readonly Color[] Shirts =
    [
        new("3F6FA8"), new("B0523E"), new("4E8A5A"), new("C19A3A"), new("7A5A9E"), new("3E8C8C"),
    ];
    private static readonly Color[] Skins =
    [
        new("F0C8A0"), new("C99A6E"), new("8D5E3C"), new("E2B48A"), new("5E3B24"), new("B8845A"),
    ];
    private static readonly Color[] Hair =
    [
        new("3A2A1C"), new("6B4226"), new("1E1A18"), new("A8742E"), new("2E2420"), new("7A3A22"),
    ];

    /// <summary>Stable appearance variant for an agent ID, so the same person always looks the same.</summary>
    public static int VariantFor(string agentId) => (int)(PixelArt.Hash(agentId.Length, StableCode(agentId), 5) % VariantCount);

    public static int StageIndex(string? ageBand) => ageBand switch
    {
        "infant" => 0,
        "child" => 1,
        "elder" => 3,
        _ => 2,
    };

    public static ImageTexture Atlas(int size)
    {
        if (Textures.TryGetValue(size, out var cached)) return cached;
        var texture = ImageTexture.CreateFromImage(AtlasImage(size));
        Textures[size] = texture;
        return texture;
    }

    public static Rect2 Region(int variant, int stage, int size) => new(variant * size, stage * size, size, size);

    public static Image Sprite(int variant, int stage, int size) =>
        AtlasImage(size).GetRegion(new Rect2I(variant * size, stage * size, size, size));

    private static Image AtlasImage(int size)
    {
        if (Images.TryGetValue(size, out var cached)) return cached;
        var image = Image.CreateEmpty(size * VariantCount, size * Stages.Length, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        for (var stage = 0; stage < Stages.Length; stage++)
            for (var variant = 0; variant < VariantCount; variant++)
                Paint(new PixelCanvas(image, new Rect2I(variant * size, stage * size, size, size), size / 32f), variant, stage);
        Images[size] = image;
        return image;
    }

    private static void Paint(PixelCanvas canvas, int variant, int stage)
    {
        var shirt = Shirts[variant];
        var skin = Skins[variant];
        var hair = stage == 3 ? new Color("D9D6CF") : Hair[variant];
        var outline = new Color("1E2226");
        canvas.Ellipse(17, 22, 9, 5, new Color(0.03f, 0.05f, 0.04f, 0.32f));
        if (stage == 0)
        {
            // An infant is carried or laid down, wrapped in a pale blanket.
            canvas.Ellipse(16, 17, 7.5f, 9.5f, outline);
            canvas.Ellipse(16, 17, 6.5f, 8.5f, new Color("E9E1CF"));
            canvas.Ellipse(16, 20, 5.5f, 5, new Color("CFC5AE"));
            canvas.Disc(16, 12, 3.6f, skin);
            canvas.Ellipse(16, 10.8f, 3.4f, 2.2f, hair);
            return;
        }
        var scale = stage == 1 ? 0.78f : 1f;
        var bodyY = 19f;
        var headY = 13f + (1 - scale) * 4;
        canvas.Ellipse(16, bodyY, 10 * scale, 6.5f * scale, outline);
        canvas.Ellipse(16, bodyY, 9 * scale, 5.5f * scale, shirt);
        canvas.Ellipse(16, bodyY + 1.5f * scale, 7 * scale, 3 * scale, shirt.Darkened(0.2f));
        canvas.Disc(16 - 9.5f * scale, bodyY + 1, 2.2f * scale, outline);
        canvas.Disc(16 + 9.5f * scale, bodyY + 1, 2.2f * scale, outline);
        canvas.Disc(16 - 9.5f * scale, bodyY + 1, 1.5f * scale, skin);
        canvas.Disc(16 + 9.5f * scale, bodyY + 1, 1.5f * scale, skin);
        canvas.Disc(16, headY, 6.2f * scale, outline);
        canvas.Disc(16, headY, 5.4f * scale, skin);
        // Hair covers the crown seen from above; the face peeks out south.
        canvas.Ellipse(16, headY - 1.2f * scale, 5.4f * scale, 4.2f * scale, hair);
        canvas.Dot(14 * scale + 16 * (1 - scale), headY - 3 * scale, hair.Lightened(0.25f));
    }

    private static int StableCode(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in value) hash = hash * 31 + character;
            return hash;
        }
    }
}
