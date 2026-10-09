using ClankerWorld.GodotClient.UI;
using Godot;
using System.Diagnostics;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyWeatherStreaksAsync()
    {
        foreach (var shape in Enum.GetValues<WeatherStreakShape>())
        {
            var texture = WeatherLayer.ParticleTexture(shape);
            if (texture.TextureFilter != CanvasItem.TextureFilterEnum.Nearest ||
                !ReferenceEquals(texture, WeatherLayer.ParticleTexture(shape)))
                throw new InvalidOperationException("Weather pixels must use cached nearest-filtered textures.");
            using var image = texture.DiffuseTexture.GetImage();
            var ink = new List<(int X, int Y)>();
            for (var y = 0; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    var pixel = image.GetPixel(x, y);
                    if (pixel.A <= 0) continue;
                    if (pixel.A >= 1) throw new InvalidOperationException("Weather shapes must remain transparent.");
                    ink.Add((x, y));
                }
            (int, int)[] expected = shape switch
            {
                WeatherStreakShape.RainClose => [(0, 0), (0, 1), (0, 2), (0, 3), (0, 4)],
                WeatherStreakShape.RainMid => [(0, 0), (0, 1), (0, 2)],
                WeatherStreakShape.Burst => [(0, 0), (2, 0), (1, 1)],
                WeatherStreakShape.StormClose => [(6, 0), (5, 1), (5, 2), (4, 3), (4, 4), (3, 5), (3, 6), (2, 7), (2, 8), (1, 9), (1, 10), (0, 11)],
                WeatherStreakShape.StormMid => [(4, 0), (3, 1), (3, 2), (2, 3), (2, 4), (1, 5), (1, 6), (0, 7)],
                WeatherStreakShape.Cross => [(1, 0), (0, 1), (1, 1), (2, 1), (1, 2)],
                _ => [(0, 0)],
            };
            if (!ink.SequenceEqual(expected)) throw new InvalidOperationException($"Native {shape} pixels differ from the approved streak/burst/cross.");
            if (shape is WeatherStreakShape.RainClose or WeatherStreakShape.RainMid &&
                (Math.Abs(image.GetPixel(0, 0).A - 0.35f) > 1f / 255 || Math.Abs(image.GetPixel(0, 1).A - 0.78f) > 1f / 255))
                throw new InvalidOperationException("Rain must retain its softer top pixel and stronger lower pixels.");
        }
        var falling = WeatherStreaks.Frame('r', new(0.4f, new(5, 40), 0), 32);
        var burst = WeatherStreaks.Frame('r', new(0.9f, new(5, 40), 0), 32);
        var storm = WeatherStreaks.Frame('s', new(0.5f, new(5, 40), 0), 32);
        var cross = WeatherStreaks.Frame('n', new(0.25f, new(100, 100), 0), 32);
        if (falling != new WeatherStreakFrame(WeatherStreakShape.RainClose, 5, 12, 1) ||
            burst.Shape != WeatherStreakShape.Burst || (burst.X, burst.Y) != (4, 39) || Math.Abs(burst.Opacity - 0.5f) > 0.00001f ||
            storm != new WeatherStreakFrame(WeatherStreakShape.StormClose, 24, -10, 1) ||
            cross != new WeatherStreakFrame(WeatherStreakShape.Cross, 85, 72, 1) ||
            WeatherStreaks.Frame('n', new(0.25f, new(100, 100), 1), 16).Shape != WeatherStreakShape.Dot)
            throw new InvalidOperationException("Weather must retain its approved landing timing, slope and sideways snow drift at both zooms.");
        var gust = WeatherStreaks.Gust(31, 32, 0);
        var east = WeatherStreaks.Gust(31 + (float)(32 * 6 * 2 * Math.PI / 3), 32, 1);
        if (Math.Abs(gust - east) > 0.00001f) throw new InvalidOperationException("Storm gusts must sweep east.");
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var index = 0; index < 100_000; index++)
        {
            var particle = WeatherStreaks.ParticleAt(index % 100, index % 70, index % 3, index * 0.017, 's');
            var frame = WeatherStreaks.Frame('s', particle, 32);
            checksum += frame.X + frame.Y + (int)(WeatherStreaks.Gust(particle.Landing.X, 32, index * 0.017) * 1_000);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        GD.Print($"NATIVE_WEATHER_GEOMETRY samples=100000 allocatedBytes={bytes} elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} checksum={checksum} cachedTextures=7");
        if (bytes != 0) throw new InvalidOperationException("Warmed weather geometry must not allocate per particle.");

        var original = renderedMapSnapshot!;
        var center = cameraCenterTiles;
        var zoom = cameraZoom;
        var paused = weatherLayer.Paused;
        try
        {
            var (width, height) = MapDimensions(original);
            var fullStorm = original with
            {
                WeatherRegionSize = 32,
                WeatherRegions = Enumerable.Range(0, (width + 31) / 32).SelectMany(x =>
                    Enumerable.Range(0, (height + 31) / 32).Select(y => new OwnerWeatherRegion(x, y, "storm", 40))).ToArray(),
            };
            foreach (var scale in new[] { 1f, 0.5f, 0.25f })
            {
                RenderMap(fullStorm);
                cameraZoom = scale;
                UpdateMapGeometry(fullStorm);
                weatherLayer.Paused = false;
                for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var samples = new List<double>();
                var maximumParticles = 0;
                for (var frame = 0; frame < 24; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    samples.Add(weatherLayer.PrecipitationMilliseconds);
                    maximumParticles = Math.Max(maximumParticles, weatherLayer.DrawnParticleCount);
                }
                samples.Sort();
                var visiblePixels = terrainLayer.VisibleTiles.Size * terrainLayer.Stride;
                var bound = 3 * ((int)Math.Ceiling(visiblePixels.X / WeatherStreaks.Cell) + 5) * ((int)Math.Ceiling(visiblePixels.Y / WeatherStreaks.Cell) + 5);
                if (maximumParticles <= 0 || maximumParticles > bound || weatherLayer.GetChildCount() != 0)
                    throw new InvalidOperationException("A full storm must draw actual bounded particles without creating nodes.");
                GD.Print($"NATIVE_WEATHER_STORM zoom={scale} samples=24 medianMs={samples[12]:F3} p95Ms={samples[22]:F3} maximumParticles={maximumParticles} cachedTextures=7");
            }
        }
        finally
        {
            RenderMap(original);
            cameraCenterTiles = center;
            cameraZoom = zoom;
            UpdateMapGeometry(original);
            weatherLayer.Paused = paused;
        }
    }
}
