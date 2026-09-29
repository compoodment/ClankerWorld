using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Animated regional weather drawn over the visible map: falling rain that
/// lands with small splash rings, darker storms with slanted rain and the odd
/// lightning flash, drifting snow, and a faint cloud haze that comes and goes.
/// Weather regions are square on the host, so their edges here are warped and
/// softened into wandering shapes that creep slowly; nothing is drawn as one
/// tile-aligned block. It draws with a few commands per frame and never adds
/// a node per tile or drop.
/// </summary>
public partial class WeatherLayer : Control
{
    /// <summary>Screen cell that holds one or two falling drops.</summary>
    private const float DropCell = 34f;
    private const int FieldMargin = 12;
    private const float FieldRefreshSeconds = 2f;
    private const int CloudTexels = 128;
    /// <summary>World tiles covered by one cloud texel.</summary>
    private const float CloudTileScale = 2f;

    private WorldTerrainLayer? source;
    private int fieldVersion = -1;
    private Rect2 fieldCamera;
    private int fieldTileSize;
    private double fieldTime = double.NegativeInfinity;
    private int fieldLeft;
    private int fieldTop;
    private int fieldWidth;
    private int fieldHeight;
    private int fieldResolution = 1;
    private float[] rain = [];
    private float[] storm = [];
    private float[] snow = [];
    private float[] cloudy = [];
    private Image? tintImage;
    private ImageTexture? tintTexture;
    private Image? stormImage;
    private ImageTexture? stormTexture;
    private float cloudiness = 0.5f;
    private double animationTime;
    private Rect2 drawnCamera;
    private int drawnTileSize;
    private float drawnCloudiness;
    private bool paused;
    private static ImageTexture? cloudTexture;

    public WeatherLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // Weather tints and haze are soft washes; drops are drawn as plain
        // rectangles, so linear filtering only affects the washes.
        TextureFilter = TextureFilterEnum.Linear;
        TextureRepeat = TextureRepeatEnum.Enabled;
    }

    /// <summary>Faint drifting cloud haze; players can turn it off.</summary>
    public bool CloudsEnabled { get; set; } = true;

    /// <summary>Brief, soft storm flashes; players can turn them off.</summary>
    public bool LightningEnabled { get; set; } = true;

    /// <summary>How much of this map tile the given weather covers after its edge is softened, or -1 off the camera.</summary>
    public float CoverageAt(int x, int y, string weather)
    {
        var field = weather switch
        {
            "rain" => rain,
            "storm" => storm,
            "snow" => snow,
            "cloudy" => cloudy,
            _ => null,
        };
        if (field is null || fieldWidth == 0) return -1;
        var column = (x - fieldLeft) / fieldResolution;
        var row = (y - fieldTop) / fieldResolution;
        if (x < fieldLeft || y < fieldTop || column >= fieldWidth || row >= fieldHeight) return -1;
        return field[row * fieldWidth + column];
    }

    /// <summary>
    /// Weather holds still while the world's time is stopped: drops, drift
    /// and creeping edges resume where they left off, and no lightning flashes.
    /// </summary>
    public bool Paused
    {
        get => paused;
        set
        {
            if (paused == value) return;
            paused = value;
            // Clear a flash already painted when time stops, without waiting
            // for another weather or camera update.
            QueueRedraw();
        }
    }

    /// <summary>Seconds of weather motion shown so far; it stands still while paused.</summary>
    internal double AnimationTime => animationTime;

    /// <summary>The terrain layer whose camera and weather regions this layer follows.</summary>
    public void Follow(WorldTerrainLayer terrain) => source = terrain;

    public override void _Process(double delta)
    {
        if (source is null || !IsVisibleInTree() || source.TileSize <= 0) return;
        // The weather's own clock only runs while world time does.
        if (!Paused) animationTime += delta;
        var now = animationTime;
        var rebuilt = false;
        if (source.WeatherVersion != fieldVersion || !FieldCoversCamera() ||
            source.TileSize != fieldTileSize || (source.HasActiveWeather && now - fieldTime >= FieldRefreshSeconds))
        {
            RebuildField(now);
            rebuilt = true;
        }
        if (!Paused)
        {
            var target = TargetCloudiness();
            cloudiness += (target - cloudiness) * (float)Math.Min(1, delta * 0.5);
        }
        if (!source.HasActiveWeather && !CloudsEnabled) return;
        // A paused frame only needs redrawing when the view itself changes.
        if (!Paused || rebuilt || source.VisibleTiles != drawnCamera || source.TileSize != drawnTileSize ||
            Math.Abs(cloudiness - drawnCloudiness) > 0.005f)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (source is null || fieldWidth == 0) return;
        var time = animationTime;
        drawnCamera = source.VisibleTiles;
        drawnTileSize = source.TileSize;
        drawnCloudiness = cloudiness;
        var stride = source.Stride;
        var fieldRect = new Rect2(fieldLeft * stride, fieldTop * stride,
            fieldWidth * fieldResolution * stride, fieldHeight * fieldResolution * stride);
        // Washes stop at the map's edge instead of tinting the space around it.
        var shown = fieldRect.Intersection(MapRect(stride));
        var fieldSource = new Rect2((shown.Position - fieldRect.Position) / (fieldResolution * stride),
            shown.Size / (fieldResolution * stride));
        if (source.HasActiveWeather && tintTexture is not null && shown.HasArea())
            DrawTextureRectRegion(tintTexture, shown, fieldSource);
        if (source.HasActiveWeather) DrawPrecipitation(time, stride);
        if (source.HasActiveWeather && LightningEnabled && stormTexture is not null && shown.HasArea())
        {
            var flash = Paused ? 0 : LightningFlash(time);
            if (flash > 0) DrawTextureRectRegion(stormTexture, shown, fieldSource, new Color(0.86f, 0.9f, 1f, flash));
        }
        if (CloudsEnabled) DrawClouds(time, stride);
    }

    /// <summary>The map's area in layer pixels; wrapped worlds continue east and west without end.</summary>
    private Rect2 MapRect(int stride)
    {
        if (source?.World is not { } world) return new Rect2();
        return source.WrapsEastWest
            ? new Rect2(-1e7f, 0, 2e7f, world.Height * stride)
            : new Rect2(0, 0, world.Width * stride, world.Height * stride);
    }

    /// <summary>Reuse the padded world-space field while a small camera pan stays inside it.</summary>
    private bool FieldCoversCamera()
    {
        if (source is null || fieldWidth == 0) return false;
        var camera = source.VisibleTiles;
        var cushion = FieldMargin / 2f;
        return camera.Position.X >= fieldLeft + cushion &&
            camera.Position.Y >= fieldTop + cushion &&
            camera.End.X <= fieldLeft + fieldWidth * fieldResolution - cushion &&
            camera.End.Y <= fieldTop + fieldHeight * fieldResolution - cushion;
    }

    private void RebuildField(double now)
    {
        if (source?.World is not { } world) return;
        fieldVersion = source.WeatherVersion;
        fieldCamera = source.VisibleTiles;
        fieldTileSize = source.TileSize;
        fieldTime = now;
        // Zoomed out, one field cell spans several tiles to keep the work
        // bounded by the camera rather than by the map.
        fieldResolution = Math.Max(1, 16 / source.TileSize);
        fieldLeft = AlignDown((int)MathF.Floor(fieldCamera.Position.X) - FieldMargin, fieldResolution);
        fieldTop = AlignDown((int)MathF.Floor(fieldCamera.Position.Y) - FieldMargin, fieldResolution);
        var right = (int)MathF.Ceiling(fieldCamera.End.X) + FieldMargin;
        var bottom = (int)MathF.Ceiling(fieldCamera.End.Y) + FieldMargin;
        fieldWidth = Math.Max(1, (right - fieldLeft + fieldResolution - 1) / fieldResolution);
        fieldHeight = Math.Max(1, (bottom - fieldTop + fieldResolution - 1) / fieldResolution);
        var length = fieldWidth * fieldHeight;
        if (rain.Length != length)
        {
            rain = new float[length];
            storm = new float[length];
            snow = new float[length];
            cloudy = new float[length];
        }
        Array.Clear(rain);
        Array.Clear(storm);
        Array.Clear(snow);
        Array.Clear(cloudy);

        // Each cell reads its weather from a point pushed around by smooth
        // noise, so a square region's edge becomes a wandering coastline of
        // weather that drifts a little over time.
        var reach = Math.Max(6f, source.WeatherRegionSize * 0.45f);
        var drift = (float)(now * 0.004);
        var broad = world.Width % 16 == 0 && source.WrapsEastWest ? world.Width / 16 : 0;
        var fine = world.Width % 8 == 0 && source.WrapsEastWest ? world.Width / 8 : 0;
        for (var row = 0; row < fieldHeight; row++)
            for (var column = 0; column < fieldWidth; column++)
            {
                var x = fieldLeft + column * fieldResolution + fieldResolution * 0.5f;
                var y = fieldTop + row * fieldResolution + fieldResolution * 0.5f;
                // A broad sway plus finer wiggles; raw gradient noise rarely
                // passes ±0.6, so it is scaled up toward the full reach.
                var wx = (WeatherNoise.At(x / 16f + drift, y / 16f, 11, broad) +
                    WeatherNoise.At(x / 8f - drift, y / 8f, 13, fine) * 0.4f) * 1.6f * reach;
                var wy = (WeatherNoise.At(x / 16f, y / 16f - drift, 23, broad) +
                    WeatherNoise.At(x / 8f, y / 8f + drift, 29, fine) * 0.4f) * 1.6f * reach;
                var index = row * fieldWidth + column;
                switch (source.WeatherAt((int)MathF.Floor(x + wx), (int)MathF.Floor(y + wy)))
                {
                    case "rain": rain[index] = 1; break;
                    case "storm": storm[index] = 1; break;
                    case "snow": snow[index] = 1; break;
                    case "cloudy": cloudy[index] = 1; break;
                }
            }
        var radius = Math.Max(1, 3 / fieldResolution);
        foreach (var field in new[] { rain, storm, snow, cloudy })
            Blur(field, radius);
        for (var index = 0; index < length; index++)
        {
            // Soften the outer edge of all weather together, then share it out
            // by kind, so a storm running into rain has no pale seam between.
            var total = rain[index] + storm[index] + snow[index] + cloudy[index];
            if (total <= 0.001f) continue;
            var edge = SmoothStep(0.12f, 0.88f, Math.Min(1, total)) / total;
            rain[index] *= edge;
            storm[index] *= edge;
            snow[index] *= edge;
            cloudy[index] *= edge;
        }
        PaintWashes();
    }

    private void PaintWashes()
    {
        if (tintImage is null || tintImage.GetWidth() != fieldWidth || tintImage.GetHeight() != fieldHeight)
        {
            tintImage = Image.CreateEmpty(fieldWidth, fieldHeight, false, Image.Format.Rgba8);
            stormImage = Image.CreateEmpty(fieldWidth, fieldHeight, false, Image.Format.Rgba8);
            tintTexture = null;
            stormTexture = null;
        }
        for (var row = 0; row < fieldHeight; row++)
            for (var column = 0; column < fieldWidth; column++)
            {
                var index = row * fieldWidth + column;
                // Storms darken the sky most, rain leaves the ground a little
                // wetter-looking, snow lays a faint pale veil.
                var stormAlpha = 0.34f * storm[index];
                var rainAlpha = 0.1f * rain[index];
                var snowAlpha = 0.07f * snow[index];
                var total = stormAlpha + rainAlpha + snowAlpha;
                var color = new Color(0, 0, 0, 0);
                if (total > 0.001f)
                {
                    var r = (0.02f * stormAlpha + 0.05f * rainAlpha + 0.95f * snowAlpha) / total;
                    var g = (0.04f * stormAlpha + 0.1f * rainAlpha + 0.97f * snowAlpha) / total;
                    var b = (0.1f * stormAlpha + 0.18f * rainAlpha + 1f * snowAlpha) / total;
                    color = new Color(r, g, b, 1 - (1 - stormAlpha) * (1 - rainAlpha) * (1 - snowAlpha));
                }
                tintImage.SetPixel(column, row, color);
                stormImage!.SetPixel(column, row, new Color(1, 1, 1, storm[index]));
            }
        if (tintTexture is null)
        {
            tintTexture = ImageTexture.CreateFromImage(tintImage);
            stormTexture = ImageTexture.CreateFromImage(stormImage);
        }
        else
        {
            tintTexture.Update(tintImage);
            stormTexture!.Update(stormImage);
        }
    }

    private void DrawPrecipitation(double time, int stride)
    {
        if (source?.World is not { } world) return;
        var size = source.TileSize;
        var camera = source.VisibleTiles;
        var left = (int)MathF.Floor(camera.Position.X * stride / DropCell) - 1;
        var top = (int)MathF.Floor(camera.Position.Y * stride / DropCell) - 1;
        var right = (int)MathF.Ceiling(camera.End.X * stride / DropCell) + 1;
        var bottom = (int)MathF.Ceiling(camera.End.Y * stride / DropCell) + 1;
        var drop = new Color(0.87f, 0.93f, 1f, 0.8f);
        var streak = new Color(0.8f, 0.87f, 0.97f, 0.62f);
        var flake = new Color(0.97f, 0.98f, 1f, 0.9f);
        var dropLength = Math.Clamp(size * 0.16f, 3f, 8f);
        var dropWidth = size >= 24 ? 2f : 1f;
        var streakLength = Math.Clamp(size * 0.36f, 5f, 14f);
        var ring = Math.Clamp(size * 0.15f, 3f, 6f);
        for (var cy = top; cy <= bottom; cy++)
            for (var cx = left; cx <= right; cx++)
            {
                // The cell's softened coverage decides which weather it shows,
                // so drops thin out toward a region's wandering edge.
                var tileX = (int)MathF.Floor((cx + 0.5f) * DropCell / stride);
                var tileY = (int)MathF.Floor((cy + 0.5f) * DropCell / stride);
                var stormHere = Math.Max(0, CoverageAt(tileX, tileY, "storm"));
                var rainHere = Math.Max(0, CoverageAt(tileX, tileY, "rain"));
                var snowHere = Math.Max(0, CoverageAt(tileX, tileY, "snow"));
                if (stormHere <= 0 && rainHere <= 0 && snowHere <= 0) continue;
                if (tileY < 0 || tileY >= world.Height || (!source.WrapsEastWest && (tileX < 0 || tileX >= world.Width)))
                    continue;
                for (var slot = 0; slot < 2; slot++)
                {
                    var seed = PixelArt.Hash(cx, cy, slot * 977 + 3);
                    var threshold = (seed & 1023) / 1023f;
                    var kind = stormHere > threshold ? 's' : slot > 0 ? ' ' :
                        rainHere > threshold ? 'r' : snowHere > threshold ? 'n' : ' ';
                    if (kind == ' ') continue;
                    var spread = ((seed >> 10) & 255) / 255.0;
                    var period = kind switch
                    {
                        's' => 0.45 + spread * 0.25,
                        'r' => 0.85 + spread * 0.5,
                        _ => 3.0 + spread * 1.6,
                    };
                    var phase = ((seed >> 18) & 255) / 255.0 * period;
                    var cycle = (long)Math.Floor((time + phase) / period);
                    var progress = (float)((time + phase) / period - cycle);
                    var landing = PixelArt.Hash(cx * 31 + (int)(cycle & 0xFFFF), cy * 17 + slot, (int)(cycle >> 16) + 41);
                    var px = (cx + (landing & 255) / 255f) * DropCell;
                    var py = (cy + ((landing >> 8) & 255) / 255f) * DropCell;
                    switch (kind)
                    {
                        case 's':
                            // Storms: faster, longer slanted streaks and a few splashes.
                            var fall = size * 2.2f;
                            var sx = px + fall * 0.4f * (1 - progress);
                            var sy = py - fall * (1 - progress) - streakLength;
                            DrawLine(new Vector2(sx, sy), new Vector2(sx - streakLength * 0.4f, sy + streakLength), streak, 1);
                            if (slot == 0 && ((landing >> 16) & 3) == 0 && progress > 0.75f)
                                DrawSplash(new Vector2(px, py), (progress - 0.75f) / 0.25f, ring, drop);
                            break;
                        case 'r':
                            DrawRainDrop(new Vector2(px, py), progress, size * 1.6f, dropLength, dropWidth, ring, drop);
                            break;
                        default:
                            var sway = MathF.Sin((progress * 2 + (seed >> 26) / 64f) * MathF.Tau) * Math.Max(2f, size * 0.08f);
                            var fx = px + sway;
                            var fy = py - DropCell * 1.3f * (1 - progress);
                            var fade = Math.Min(1, Math.Min(progress / 0.12f, (1 - progress) / 0.2f));
                            var flakeSize = size >= 12 ? 2f : 1f;
                            DrawRect(new Rect2(MathF.Round(fx), MathF.Round(fy), flakeSize, flakeSize),
                                flake with { A = flake.A * fade });
                            break;
                    }
                }
            }
    }

    /// <summary>A short drop falls for most of its cycle, then lands as an expanding ring.</summary>
    private void DrawRainDrop(Vector2 landing, float progress, float fall, float length, float width, float ring, Color color)
    {
        const float LandsAt = 0.62f;
        if (progress < LandsAt)
        {
            var left = 1 - progress / LandsAt;
            var fade = Math.Min(1, progress / 0.08f);
            var x = MathF.Round(landing.X + fall * 0.12f * left);
            var y = MathF.Round(landing.Y - fall * left - length);
            DrawRect(new Rect2(x, y, width, length), color with { A = color.A * fade });
            return;
        }
        DrawSplash(landing, (progress - LandsAt) / (1 - LandsAt), ring, color);
    }

    /// <summary>A flat pixel ring, wider than tall, growing and fading.</summary>
    private void DrawSplash(Vector2 center, float progress, float radius, Color color)
    {
        var r = 1 + progress * radius;
        var alpha = color.A * (1 - progress);
        for (var point = 0; point < 8; point++)
        {
            var angle = point * MathF.Tau / 8;
            var x = MathF.Round(center.X + MathF.Cos(angle) * r);
            var y = MathF.Round(center.Y + MathF.Sin(angle) * r * 0.45f);
            DrawRect(new Rect2(x, y, 1, 1), color with { A = alpha });
        }
    }

    /// <summary>
    /// Soft, single brightenings a few seconds apart, never a rapid strobe:
    /// each flash rises and fades within half a second with one small echo.
    /// </summary>
    internal static float LightningFlash(double time)
    {
        const double Window = 9.0;
        var window = Math.Floor(time / Window);
        var at = PixelArt.Hash((int)window, 5, 101) % 1000 / 1000.0 * (Window - 1);
        var since = time - window * Window - at;
        if (since < 0 || since > 0.5) return 0;
        var main = since < 0.06 ? since / 0.06 : Math.Max(0, 1 - (since - 0.06) / 0.18);
        var echo = since is > 0.2 and < 0.5 ? 0.5 * Math.Sin((since - 0.2) / 0.3 * Math.PI) : 0;
        return (float)(0.3 * Math.Max(main, echo));
    }

    private void DrawClouds(double time, int stride)
    {
        if (source is null || cloudiness <= 0.01f) return;
        var texture = cloudTexture ??= BuildCloudTexture();
        var camera = source.VisibleTiles;
        // The haze drifts slowly east and a little south, anchored to the
        // ground so it pans with the map.
        var drift = new Vector2((float)(time * 0.22), (float)(time * 0.06));
        var shown = new Rect2(camera.Position * stride, camera.Size * stride).Intersection(MapRect(stride));
        if (!shown.HasArea()) return;
        var tiles = new Rect2(shown.Position / stride, shown.Size / stride);
        DrawTextureRectRegion(texture, shown,
            new Rect2((tiles.Position - drift) / CloudTileScale, tiles.Size / CloudTileScale), new Color(1, 1, 1, cloudiness));
    }

    /// <summary>More haze over cloudy, rainy or snowy skies, only wisps over clear ones.</summary>
    private float TargetCloudiness()
    {
        if (fieldWidth == 0) return 0.45f;
        var center = fieldWidth * (fieldHeight / 2) + fieldWidth / 2;
        var overcast = Math.Max(Math.Max(cloudy[center], rain[center]), Math.Max(storm[center], snow[center]));
        return 0.45f + 0.55f * overcast;
    }

    /// <summary>A repeating pattern of pale veils with wide gaps, so clear sky shows most of the time.</summary>
    private static ImageTexture BuildCloudTexture()
    {
        var image = Image.CreateEmpty(CloudTexels, CloudTexels, false, Image.Format.Rgba8);
        for (var y = 0; y < CloudTexels; y++)
            for (var x = 0; x < CloudTexels; x++)
            {
                var value = 0.5f +
                    WeatherNoise.At(x / 32f, y / 32f, 7, 4) * 0.6f +
                    WeatherNoise.At(x / 16f, y / 16f, 8, 8) * 0.28f +
                    WeatherNoise.At(x / 8f, y / 8f, 9, 16) * 0.12f;
                var alpha = SmoothStep(0.56f, 0.8f, value) * 0.15f;
                image.SetPixel(x, y, new Color(0.96f, 0.98f, 1f, alpha));
            }
        return ImageTexture.CreateFromImage(image);
    }

    private void Blur(float[] field, int radius)
    {
        var scratch = new float[field.Length];
        for (var row = 0; row < fieldHeight; row++)
            for (var column = 0; column < fieldWidth; column++)
            {
                var sum = 0f;
                for (var offset = -radius; offset <= radius; offset++)
                    sum += field[row * fieldWidth + Math.Clamp(column + offset, 0, fieldWidth - 1)];
                scratch[row * fieldWidth + column] = sum / (radius * 2 + 1);
            }
        for (var row = 0; row < fieldHeight; row++)
            for (var column = 0; column < fieldWidth; column++)
            {
                var sum = 0f;
                for (var offset = -radius; offset <= radius; offset++)
                    sum += scratch[Math.Clamp(row + offset, 0, fieldHeight - 1) * fieldWidth + column];
                field[row * fieldWidth + column] = sum / (radius * 2 + 1);
            }
    }

    private static int AlignDown(int value, int step) => (int)MathF.Floor(value / (float)step) * step;

    private static float SmoothStep(float from, float to, float value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>Smooth gradient noise for weather shapes, optionally repeating every <c>periodCells</c> across x and y.</summary>
internal static class WeatherNoise
{
    public static float At(float x, float y, int salt, int periodCells)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = x - x0;
        var ty = y - y0;
        var top = Lerp(Gradient(x0, y0, tx, ty, salt, periodCells), Gradient(x0 + 1, y0, tx - 1, ty, salt, periodCells), Fade(tx));
        var bottom = Lerp(Gradient(x0, y0 + 1, tx, ty - 1, salt, periodCells),
            Gradient(x0 + 1, y0 + 1, tx - 1, ty - 1, salt, periodCells), Fade(tx));
        return Lerp(top, bottom, Fade(ty));
    }

    private static float Gradient(int cx, int cy, float dx, float dy, int salt, int periodCells)
    {
        if (periodCells > 0)
        {
            cx = ((cx % periodCells) + periodCells) % periodCells;
            cy = ((cy % periodCells) + periodCells) % periodCells;
        }
        var angle = PixelArt.Hash(cx, cy, salt) % 1024 / 1024f * MathF.Tau;
        return MathF.Cos(angle) * dx + MathF.Sin(angle) * dy;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
}
