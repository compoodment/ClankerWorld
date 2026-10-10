using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyGoldenHourAsync()
    {
        var previous = renderedMapSnapshot!;
        var previousObservation = observationSession.Current;
        var zoomBefore = cameraZoom;
        var centerBefore = cameraCenterTiles;
        var processingBefore = goldenHourLayer.IsProcessing();
        goldenHourLayer.SetProcess(false); // Advance the real native display under a controlled clock.
        var map = new OwnerWorldSnapshot("golden-hour-smoke", 0, "golden-hour-map", [], [], [], null, 0)
        {
            PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(new byte[256 * 128])),
            CalendarPace = new(1440, 120, 30, 30, 30, 30),
            Authoring = new(false, 0, 0, 0, "golden-hour-map", "golden-hour-map", "clear", "spring", []),
        };
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        void Show()
        {
            observationSession.ResetAfterLoad();
            if (!observationSession.TryAccept(new(handshake, new(map, new(map.WorldTick, 0, []))), 0, out var failure))
                throw new InvalidOperationException("Golden-hour UI observation fixture was refused: " + failure);
            Render(map, []);
            goldenHourLayer._Process(0); // Refresh camera commands without advancing the controlled colour clock.
        }
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        try
        {
            // Actual season-start twilight centres from the host's documented seasonal nights.
            foreach (var (day, dawnMinute, duskMinute) in new[] { (0, 288, 1152), (30, 216, 1224), (60, 288, 1152), (90, 360, 1080) })
            {
                foreach (var morning in new[] { true, false })
                {
                    map = map with { WorldTick = day * 1440L + (morning ? dawnMinute : duskMinute), DarknessBasisPoints = 5000 };
                    Show(); goldenHourLayer.Settle();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Check(goldenHourLayer.ShownStrength == 1 && goldenHourLayer.IsDawn == morning && goldenHourLayer.DrawnArea.HasArea(),
                        "The host's seasonal dawn/dusk readings must draw the correct full rose/gold glow.");
                    var multiplier = goldenHourLayer.CurrentMultiplier;
                    var expected = morning ? new Color(0.9810196f, 0.9206275f, 0.9180392f) : new Color(0.9887843f, 0.9327059f, 0.8628235f);
                    Check(Math.Abs(multiplier.R - expected.R) < 0.00001f && Math.Abs(multiplier.G - expected.G) < 0.00001f &&
                        Math.Abs(multiplier.B - expected.B) < 0.00001f && multiplier.A == 1,
                        "The real multiply must use the reviewed E9A3A0/F2B160 colours at 22%, rather than an alpha colour wash.");
                }
            }
            var layers = mapStage.GetChildren();
            Check(goldenHourLayer.Material is CanvasItemMaterial { BlendMode: CanvasItemMaterial.BlendModeEnum.Mul } &&
                goldenHourLayer.GetChildCount() == 0 && goldenHourLayer.MouseFilter == Control.MouseFilterEnum.Ignore &&
                layers.IndexOf(goldenHourLayer) == layers.IndexOf(nightLayer) + 1 &&
                layers.IndexOf(nightLightsLayer) == layers.IndexOf(goldenHourLayer) + 1 &&
                layers.IndexOf(weatherLayer) > layers.IndexOf(goldenHourLayer) && layers.IndexOf(entityLayer) > layers.IndexOf(goldenHourLayer),
                "Golden hour must be a single click-through multiply below night lights, weather and moving figures.");
            map = map with { WorldTick = map.WorldTick + 1, DarknessBasisPoints = 0 }; Show(); goldenHourLayer.Settle();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(goldenHourLayer.CurrentMultiplier == Colors.White && !goldenHourLayer.DrawnArea.HasArea(),
                "Full daylight must draw no golden-hour colour at all.");
            map = map with { WorldTick = map.WorldTick + 1, DarknessBasisPoints = 2500 }; Show();
            goldenHourLayer._Process(0.125);
            Check(goldenHourLayer.ShownStrength is > 0 and < 0.75f, "Changing twilight readings must ease between observations.");
            var halfway = goldenHourLayer.CurrentMultiplier;
            map = map with { Authoring = map.Authoring! with { IsPaused = true } }; Show(); goldenHourLayer._Process(10);
            Check(goldenHourLayer.CurrentMultiplier == halfway, "Pause must hold the exact in-progress golden-hour multiply.");
            map = map with { Authoring = map.Authoring! with { IsPaused = false } }; Show(); goldenHourLayer._Process(0.125);
            Check(goldenHourLayer.ShownStrength > 0.25f && goldenHourLayer.ShownStrength <= 0.75f,
                "Resume must continue the local transition from where it paused.");
            map = map with { WorldTick = map.WorldTick + 1, DarknessBasisPoints = 10000 }; Show(); goldenHourLayer.Settle(); nightLayer.Settle();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(goldenHourLayer.CurrentMultiplier == Colors.White && !goldenHourLayer.DrawnArea.HasArea() &&
                nightLayer.CurrentWash == (NightLayer.Wash with { A = NightLayer.FullNightAlpha }),
                "Full night must keep the existing night wash and have no leftover warm colour.");
            map = map with
            {
                WorldId = "golden-offset",
                WorldTick = 288,
                DarknessBasisPoints = 5000,
                CalendarPace = map.CalendarPace! with { CalendarOffsetTicks = 900 }
            }; Show();
            Check(!goldenHourLayer.IsDawn && goldenHourLayer.ShownStrength == 1,
                "Morning versus evening must use the saved clock offset, not raw elapsed ticks.");
            foreach (var detailed in new[] { false, true })
            {
                cameraZoom = detailed ? maximumCameraZoom : minimumCameraZoom;
                cameraCenterTiles = new(120, 60); Show();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var stride = terrainLayer.Stride;
                var visible = new Rect2(terrainLayer.VisibleTiles.Position * stride, terrainLayer.VisibleTiles.Size * stride)
                    .Intersection(new Rect2(0, 0, 256 * stride, 128 * stride));
                Check(goldenHourLayer.DrawnArea.Grow(0.5f).Encloses(visible) && visible.Grow(0.5f).Encloses(goldenHourLayer.DrawnArea),
                    "The native multiply must cover exactly the visible map at overview and close zoom.");
            }
            map = map with { WrapsEastWest = true }; cameraCenterTiles = new(0, 60); Show();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(goldenHourLayer.DrawnArea.Position.X < 0, "The glow must also cover the visible copy west of a wrapped seam.");
            var themeBefore = displayPreferences.Theme;
            var multiplierBefore = goldenHourLayer.CurrentMultiplier;
            var other = UiTheme.Current == UiTheme.Dark ? UiThemeChoice.Light : UiThemeChoice.Dark;
            themeChoice.Select((int)other); SetUiTheme((int)other);
            Check(goldenHourLayer.CurrentMultiplier == multiplierBefore && objectLayer.Modulate == Colors.White && entityLayer.Modulate == Colors.White,
                "Themes must not alter the approved multiply or tint map labels and figures.");
            themeChoice.Select((int)UiTheme.Parse(themeBefore)); SetUiTheme((int)UiTheme.Parse(themeBefore));
            map = map with { WorldTick = 1, DarknessBasisPoints = 0 }; Show();
            Check(goldenHourLayer.ShownStrength == 0, "A tick rewind must immediately clear stale twilight interpolation.");
            map = map with { WorldId = "golden-missing-darkness", DarknessBasisPoints = null }; Show();
            Check(goldenHourLayer.ShownStrength == 0, "Missing host darkness must not invent dawn or dusk.");
        }
        finally
        {
            goldenHourLayer.SetProcess(processingBefore);
            cameraZoom = zoomBefore; cameraCenterTiles = centerBefore;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null) observationSession.TryAccept(previousObservation, 0, out _);
            Render(previous, []);
        }
    }
}
