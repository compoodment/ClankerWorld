using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyWeatherFadeAsync(OwnerWorldSnapshot original)
    {
        var savedPause = weatherLayer.Paused;
        var savedCenter = cameraCenterTiles;
        var savedZoom = cameraZoom;
        var clouds = weatherLayer.CloudsEnabled;
        var lightning = weatherLayer.LightningEnabled;
        weatherLayer.SetProcess(false);
        try
        {
            // Drive the real native layer's process clock deterministically; no alternate painter.
            var regions = Enumerable.Range(0, 8).SelectMany(x => Enumerable.Range(0, 4)
                .Select(y => new OwnerWeatherRegion(x, y, "rain", 40))).ToArray();
            var map = original with
            {
                WorldId = "weather-fade-check",
                MapManifestDigest = "weather-fade-check",
                WeatherRegions = regions,
                Authoring = original.Authoring! with { IsPaused = false },
            };
            RenderMap(map);
            weatherLayer.Paused = true;
            weatherLayer._Process(0);
            var full = weatherLayer.CoverageAt(144, 80, "rain");
            if (full < 0.99f) throw new InvalidOperationException("Loading a world must show its existing weather immediately.");
            weatherLayer.CloudsEnabled = false;
            weatherLayer.LightningEnabled = false;
            foreach (var kind in new[] { "rain", "storm", "snow" })
            {
                map = map with { WeatherRegions = regions.Select(region => region with { Weather = kind }).ToArray() };
                RenderMap(map);
                weatherLayer.Paused = false;
                weatherLayer._Process(WeatherFade.Seconds);
                if (weatherLayer.CoverageAt(144, 80, kind) < 0.99f)
                    throw new InvalidOperationException($"The native {kind} overlay must reach full coverage.");
                // An empty region list means clear, including when the last precipitation stops.
                RenderMap(map with { WeatherRegions = [] });
                weatherLayer._Process(0);
                var initial = weatherLayer.CoverageAt(144, 80, kind);
                weatherLayer._Process(WeatherFade.Seconds / 2);
                var half = weatherLayer.CoverageAt(144, 80, kind);
                if (initial < 0.99f || Math.Abs(half - 0.5f) > 0.001f ||
                    weatherLayer.CoverageAt(144, 80, kind + "-support") < 0.99f)
                    throw new InvalidOperationException($"Outgoing {kind} must retain then fade its actual native field: {initial}, {half}.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (weatherLayer.DrawnParticleCount <= 0)
                    throw new InvalidOperationException($"Outgoing {kind} must still draw actual approved particles after the final active region clears.");
                weatherLayer.Paused = true;
                weatherLayer._Process(3);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (weatherLayer.CoverageAt(144, 80, kind) != half)
                    throw new InvalidOperationException($"Pause must freeze the partly faded {kind} overlay.");
                weatherLayer.Paused = false;
                // A sub-frame advance changes the clock but must not change the cached drawing.
                weatherLayer._Process(0.06);
                if (weatherLayer.CoverageAt(144, 80, kind) != half)
                    throw new InvalidOperationException($"A sub-frame {kind} advance must retain the displayed cached amount.");
                // Reverse halfway: the first new frame must retain exactly the displayed amount.
                RenderMap(map);
                weatherLayer._Process(0);
                if (weatherLayer.CoverageAt(144, 80, kind) != half)
                    throw new InvalidOperationException($"A rapid {kind} reversal must not jump to full coverage.");
                weatherLayer._Process(WeatherFade.Seconds / 2);
                if (Math.Abs(weatherLayer.CoverageAt(144, 80, kind) - 0.75f) > 0.001f)
                    throw new InvalidOperationException($"A reversed {kind} fade must start from the shown amount.");
                weatherLayer._Process(WeatherFade.Seconds);
                RenderMap(map with { WeatherRegions = [] });
                weatherLayer._Process(0);
                weatherLayer._Process(WeatherFade.Seconds);
                if (weatherLayer.CoverageAt(144, 80, kind) != 0 || weatherLayer.CloudsEnabled || weatherLayer.LightningEnabled)
                    throw new InvalidOperationException($"The last {kind} must disappear completely while preserving the effect settings.");
                // The next start rises from clear rather than appearing at once.
                RenderMap(map);
                weatherLayer._Process(0);
                if (weatherLayer.CoverageAt(144, 80, kind) != 0)
                    throw new InvalidOperationException($"Incoming {kind} must start at zero.");
                weatherLayer._Process(WeatherFade.Seconds / 2);
                if (Math.Abs(weatherLayer.CoverageAt(144, 80, kind) - 0.5f) > 0.001f)
                    throw new InvalidOperationException($"Incoming {kind} must follow the approved 1.2-second envelope.");
                weatherLayer._Process(WeatherFade.Seconds);
            }
            // Ending rain must not change the spatial admission of its unchanged storm neighbour.
            var boundary = map with
            {
                WorldId = "weather-boundary-check",
                MapManifestDigest = "weather-boundary-check",
                WeatherRegions = regions.Select(region => region with { Weather = region.X < 4 ? "rain" : "storm" }).ToArray(),
            };
            RenderMap(boundary);
            cameraCenterTiles = new(128, 80);
            cameraZoom = 1;
            UpdateMapGeometry(boundary);
            weatherLayer._Process(0);
            RenderMap(boundary with
            {
                WeatherRegions = boundary.WeatherRegions.Select(region =>
                region with { Weather = region.X < 4 ? "clear" : "storm" }).ToArray()
            });
            weatherLayer._Process(0);
            weatherLayer._Process(WeatherFade.Seconds - 0.000001);
            var edge = Enumerable.Range(96, 65).Select(x => (X: x, Storm: weatherLayer.CoverageAt(x, 80, "storm")))
                .Where(sample => sample.Storm > 0.05f && sample.Storm < 0.95f).ToArray();
            if (edge.Length == 0) throw new InvalidOperationException("The boundary fixture must contain actual partly covered storm cells.");
            weatherLayer._Process(0.000002);
            if (edge.Any(sample => Math.Abs(weatherLayer.CoverageAt(sample.X, 80, "storm") - sample.Storm) > 0.001f))
                throw new InvalidOperationException("Finishing adjacent rain must not make the unchanged storm boundary jump.");
            var junction = boundary with
            {
                WorldId = "weather-junction-check",
                MapManifestDigest = "weather-junction-check",
                WeatherRegions = regions.Select(region => region with
                {
                    Weather = region.X < 4
                    ? region.Y < 2 ? "rain" : "storm"
                    : region.Y < 2 ? "snow" : "cloudy"
                }).ToArray(),
            };
            RenderMap(junction);
            cameraCenterTiles = new(128, 64);
            UpdateMapGeometry(junction);
            weatherLayer._Process(0);
            var junctions = Enumerable.Range(96, 65).SelectMany(x => Enumerable.Range(32, 65).Select(y =>
                new[] { weatherLayer.CoverageAt(x, y, "rain"), weatherLayer.CoverageAt(x, y, "storm"), weatherLayer.CoverageAt(x, y, "snow"), weatherLayer.CoverageAt(x, y, "cloudy") }))
                .Where(amounts => amounts.Count(amount => amount > 0.01f) >= 3).ToArray();
            if (junctions.Length == 0) throw new InvalidOperationException("The weather fixture must contain a real multi-kind junction.");
            if (junctions.Any(amounts => Math.Abs(amounts.Sum() - 1) > 0.001f))
                throw new InvalidOperationException("Loaded weather kinds must keep full combined coverage at their regional junction.");
        }
        finally
        {
            RenderMap(original);
            cameraCenterTiles = savedCenter;
            cameraZoom = savedZoom;
            RenderMap(original);
            weatherLayer.Paused = savedPause;
            weatherLayer.CloudsEnabled = clouds;
            weatherLayer.LightningEnabled = lightning;
            weatherLayer.SetProcess(true);
        }
    }
}
