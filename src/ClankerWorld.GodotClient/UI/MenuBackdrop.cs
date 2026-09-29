using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Draws <see cref="MenuScene"/> behind the Main Menu, covering the window
/// with crisp pixels. Clouds drift, chimney smoke curls up, birds cross the
/// morning sky, the river sparkles downstream, and at dusk stars twinkle,
/// lights flicker and fireflies wander. Motion steps at a pixel-art frame
/// rate and only advances while the backdrop is visible.
/// </summary>
public partial class MenuBackdrop : Control
{
    public const int FramesPerSecond = 12;

    private static readonly Dictionary<bool, Art> Cached = [];
    private static readonly Dictionary<int, ImageTexture> Discs = [];
    private static readonly Color Firefly = new("D6EC78");
    private static readonly Color MoonGlint = new(0.886f, 0.824f, 0.667f, 0.55f);
    private static readonly (int X, int Y)[] WingsUp = [(-2, -1), (-1, 0), (0, 0), (1, 0), (2, -1)];
    private static readonly (int X, int Y)[] WingsDown = [(-2, 1), (-1, 0), (0, 0), (1, 0), (2, 1)];

    private Art? art;
    private bool night;
    private double elapsed;
    private int drawnFrame = -1;

    public MenuBackdrop()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        ClipContents = true;
    }

    /// <summary>Dusk for the Dark theme, a clear morning otherwise.</summary>
    public bool Night
    {
        get => night;
        set
        {
            if (art is not null && value == night) return;
            night = value;
            art = Load(value);
            QueueRedraw();
        }
    }

    public MenuScene Scene => (art ??= Load(night)).Scene;

    /// <summary>Seconds of animation shown so far; it holds still while hidden.</summary>
    public double AnimationTime => elapsed;

    public override void _Ready()
    {
        art ??= Load(night);
        SetProcess(IsVisibleInTree());
    }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsInsideTree())
            SetProcess(IsVisibleInTree());
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        elapsed += delta;
        if ((int)(elapsed * FramesPerSecond) != drawnFrame) QueueRedraw();
    }

    public override void _Draw()
    {
        art ??= Load(night);
        drawnFrame = (int)(elapsed * FramesPerSecond);
        var time = drawnFrame / (float)FramesPerSecond;
        var scene = art.Scene;
        var scale = Mathf.Max(Size.X / MenuScene.Width, Size.Y / MenuScene.Height);
        var origin = (Size - new Vector2(MenuScene.Width, MenuScene.Height) * scale) / 2;
        Rect2 At(float x, float y, float width, float height) =>
            new(origin + new Vector2(x, y) * scale, new Vector2(width, height) * scale);
        void Dot(float x, float y, Color color) => DrawRect(At(Mathf.Floor(x), Mathf.Floor(y), 1, 1), color);

        DrawTextureRect(art.Sky, At(0, 0, MenuScene.Width, MenuScene.Height), false);
        if (scene.Night) DrawStars(scene, time, Dot);
        for (var i = 0; i < scene.Clouds.Count; i++)
        {
            var cloud = scene.Clouds[i];
            var width = cloud.Image.GetWidth();
            var x = Mathf.PosMod(cloud.X + cloud.Speed * time + width, MenuScene.Width + width) - width;
            DrawTextureRect(art.Clouds[i], At(Mathf.Floor(x), cloud.Y, width, cloud.Image.GetHeight()), false);
        }
        DrawTextureRect(art.Land, At(0, 0, MenuScene.Width, MenuScene.Height), false);
        if (!scene.Night) DrawBirds(time, Dot);
        DrawWater(scene, time, Dot);
        DrawSmoke(scene, time, At);
        if (!scene.Night) return;
        for (var i = 0; i < scene.Glows.Count; i++)
        {
            var glow = scene.Glows[i];
            var flicker = PixelArt.Hash(i, (int)(time * 8), 7) / 4294967296f;
            var alpha = glow.Forge ? 0.7f + 0.3f * flicker : 0.9f + 0.1f * flicker;
            DrawTextureRect(art.Glows[i], At(glow.Position.X, glow.Position.Y, glow.Image.GetWidth(), glow.Image.GetHeight()),
                false, new Color(1, 1, 1, alpha));
        }
        DrawFireflies(scene, time, Dot);
    }

    private static void DrawStars(MenuScene scene, float time, Action<float, float, Color> dot)
    {
        foreach (var star in scene.Stars)
        {
            var dim = (int)(time * 4 + star.Phase) % 16 < 2;
            var color = dim ? new Color(star.Color, 0.35f) : star.Color;
            dot(star.Position.X, star.Position.Y, color);
            if (!star.Large || dim) continue;
            var arm = new Color(star.Color, 0.5f);
            dot(star.Position.X + 1, star.Position.Y, arm);
            dot(star.Position.X - 1, star.Position.Y, arm);
            dot(star.Position.X, star.Position.Y + 1, arm);
            dot(star.Position.X, star.Position.Y - 1, arm);
        }
    }

    /// <summary>Two small flocks cross now and then, flapping as they go.</summary>
    private static void DrawBirds(float time, Action<float, float, Color> dot)
    {
        (float X, float Y)[] flock = [(0, 0), (-7, 3), (-13, -1)];
        for (var group = 0; group < 2; group++)
        {
            var period = 70f + group * 23f;
            var x = -14 + Mathf.PosMod(time + group * 31f, period) * 5.5f;
            if (x > MenuScene.Width + 14) continue;
            var y = 42 + group * 12 + Mathf.Round(Mathf.Sin(time * 0.7f + group) * 1.5f);
            for (var bird = 0; bird < flock.Length; bird++)
            {
                var wings = ((int)(time * 4) + bird) % 2 == 0 ? WingsUp : WingsDown;
                foreach (var (dx, dy) in wings)
                    dot(Mathf.Floor(x + flock[bird].X) + dx, y + flock[bird].Y + dy, MenuScene.BirdColor);
            }
        }
    }

    /// <summary>Sparkles travel downstream; at dusk the moon's reflection shimmers.</summary>
    private static void DrawWater(MenuScene scene, float time, Action<float, float, Color> dot)
    {
        var step = (int)(time * 3);
        foreach (var sparkle in scene.Sparkles)
            if (Mathf.PosMod(step - sparkle.Position.Y / 2 + sparkle.Phase, 24) < 2)
                dot(sparkle.Position.X, sparkle.Position.Y, sparkle.Color);
        var jitter = (int)(time * 2.5f);
        foreach (var glint in scene.MoonReflection)
            if (PixelArt.Hash(glint.X, glint.Y, jitter) % 3 != 0)
                dot(glint.X, glint.Y, MoonGlint);
    }

    /// <summary>Each chimney lets out a puff about twice a second that grows, sways and fades as it rises.</summary>
    private void DrawSmoke(MenuScene scene, float time, Func<float, float, float, float, Rect2> at)
    {
        const float interval = 0.5f;
        const int puffs = 11;
        for (var i = 0; i < scene.Chimneys.Count; i++)
        {
            var chimney = scene.Chimneys[i];
            var tint = chimney.Forge ? scene.ForgeSmokeColor : scene.SmokeColor;
            var offset = Mathf.PosMod(time + i * 0.37f, interval);
            for (var n = 0; n < puffs; n++)
            {
                var k = (offset + n * interval) / interval;
                var alpha = 0.62f - k * 0.058f;
                if (alpha <= 0.02f) continue;
                var radius = 0.9f + k * 0.28f;
                var x = chimney.Position.X + k * 1.1f + Mathf.Sin(k * 0.9f + time * 0.8f + i) * 1.4f + k * k * 0.06f;
                var y = chimney.Position.Y - 1 - k * 2.4f;
                var disc = DiscTexture(radius, out var extent);
                DrawTextureRect(disc, at(Mathf.Round(x) - extent, Mathf.Round(y) - extent, extent * 2 + 1, extent * 2 + 1),
                    false, new Color(tint, alpha));
            }
        }
    }

    private static void DrawFireflies(MenuScene scene, float time, Action<float, float, Color> dot)
    {
        if (scene.FireflyHomes.Count == 0) return;
        for (var i = 0; i < 12; i++)
        {
            if (Mathf.PosMod(time * 0.8f + i * 0.53f, 3) >= 2) continue;
            var home = scene.FireflyHomes[i % scene.FireflyHomes.Count];
            var x = home.X + Mathf.Sin(time * 0.6f * (1 + i % 3 * 0.3f) + i * 1.7f) * 6;
            var y = home.Y + Mathf.Sin(time * 0.9f + i * 2.3f) * 3;
            dot(x, y, Firefly);
            var halo = new Color(Firefly, 0.3f);
            dot(x + 1, y, halo);
            dot(x - 1, y, halo);
            dot(x, y + 1, halo);
            dot(x, y - 1, halo);
        }
    }

    private static ImageTexture DiscTexture(float radius, out int extent)
    {
        var key = Mathf.RoundToInt(radius * 4);
        extent = Mathf.CeilToInt(key / 4f);
        if (Discs.TryGetValue(key, out var cached)) return cached;
        var size = extent * 2 + 1;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                if (new Vector2(x - extent, y - extent).Length() <= key / 4f)
                    image.SetPixel(x, y, Colors.White);
        var texture = ImageTexture.CreateFromImage(image);
        Discs[key] = texture;
        return texture;
    }

    private static Art Load(bool night)
    {
        if (Cached.TryGetValue(night, out var cached)) return cached;
        var scene = MenuScene.Create(night);
        var art = new Art(scene, ImageTexture.CreateFromImage(scene.Sky), ImageTexture.CreateFromImage(scene.Land),
            scene.Clouds.Select(cloud => ImageTexture.CreateFromImage(cloud.Image)).ToArray(),
            scene.Glows.Select(glow => ImageTexture.CreateFromImage(glow.Image)).ToArray());
        Cached[night] = art;
        return art;
    }

    private sealed record Art(MenuScene Scene, ImageTexture Sky, ImageTexture Land,
        IReadOnlyList<ImageTexture> Clouds, IReadOnlyList<ImageTexture> Glows);
}
