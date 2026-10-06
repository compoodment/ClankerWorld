using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Species, age and mount silhouettes in the game's eight map directions.</summary>
public static class AnimalSprites
{
    private static readonly Dictionary<(string Species, int Facing, bool Young, bool Mounted), Texture2D> Textures = [];
    public static Texture2D Texture(string species, int facing, bool young, bool mounted)
    {
        facing = (facing % 8 + 8) % 8;
        var key = (species, facing, young, mounted && species == "horse");
        if (Textures.TryGetValue(key, out var texture)) return texture;
        texture = ImageTexture.CreateFromImage(Sprite(key.species, key.facing, key.young, key.Item4));
        Textures.Add(key, texture);
        return texture;
    }

    public static Image Sprite(string species, int facing, bool young, bool mounted)
    {
        var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var scale = species == "chicken" ? young ? .45f : .65f : young ? .65f : 1f;
        var angle = (facing % 8) * MathF.PI / 4;
        var forward = new Vector2(-MathF.Sin(angle), MathF.Cos(angle));
        var right = new Vector2(forward.Y, -forward.X);
        var body = new Color(species switch { "chicken" => "E1C695", "sheep" => "DDD7C3", "cow" => "B67D51", _ => "89603F" });
        var shade = body.Darkened(.3f);
        var hoof = new Color("3F352B");
        void Oval(float along, float across, float length, float width, Color color)
        {
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
            {
                var local = new Vector2(x - 15.5f, y - 17.5f);
                var u = (local.Dot(forward) / scale - along) / length;
                var v = (local.Dot(right) / scale - across) / width;
                if (u * u + v * v <= 1) image.SetPixel(x, y, color);
            }
        }
        Oval(1, 1, 11, 7, new Color(0, 0, 0, .22f));
        foreach (var along in new[] { -5f, 5f }) foreach (var across in new[] { -4f, 4f }) Oval(along, across, 3, 1.4f, hoof);
        Oval(-7, 0, 4, .9f, shade);
        Oval(0, 0, 8, 5, shade);
        Oval(-1, -1, 7, 4, body);
        if (species == "sheep")
            foreach (var along in new[] { -5f, 0f, 4f }) Oval(along, -2, 3.5f, 2.5f, new Color("F1EAD9"));
        if (species == "cow") { Oval(-3, 0, 2.8f, 2.8f, new Color("EBD7B6")); Oval(3, -2, 2, 2, hoof); }
        Oval(8, 0, 4, 2.8f, body.Lightened(.08f));
        Oval(8, -3, 1.8f, 1.8f, shade); Oval(8, 3, 1.8f, 1.8f, shade);
        if (species == "cow") { Oval(9, -3.5f, 2, .8f, new Color("DACBA7")); Oval(9, 3.5f, 2, .8f, new Color("DACBA7")); }
        Oval(10, -1.7f, .7f, .7f, new Color("241F1B"));
        if (species == "chicken") { Oval(12, 0, 2, 1, new Color("DDA13B")); Oval(8, 0, 2, 1.1f, new Color("A94437")); }
        if (species == "horse") Oval(5, 0, 5, 1, new Color("3F2E23"));
        if (mounted && species == "horse")
        {
            Oval(0, 0, 3, 4, new Color("614430"));
            Oval(-1, 0, 3, 2.7f, new Color("466274"));
            Oval(-3, 0, 2, 2, new Color("D0A278"));
        }
        return image;
    }
}
