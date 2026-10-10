using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyCameraMotionAsync(OwnerWorldSnapshot sample)
    {
        var original = renderedMapSnapshot!;
        var originalCenter = cameraCenterTiles;
        var originalZoom = cameraZoom;
        var originalAgent = selectedInhabitantId;
        var originalBuilding = selectedBuildingId;
        var originalProfile = agentProfileRequested;
        var position = new OwnerWorldPosition(180, 64);
        var person = new OwnerWorldInhabitant("camera-person", "Camera person", "active", position, 2_000, [], [],
            new OwnerWorldRoute("idle", null, null, [], string.Empty),
            new OwnerWorldSpatialKnowledge(position, [position], [position]), false);
        var fixture = sample with
        {
            WorldId = "ui-camera-motion",
            PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(Enumerable.Repeat((byte)1, 256 * 128).ToArray())),
            PackedMapLayers = null,
            MapLayersDigest = null,
            Tiles = [],
            Resources = [],
            Inhabitants = [person],
            Actor = null,
            Towns = [],
            RoadTiles = [],
            PlacedBuildings = [new("camera-house", "house", new(170, 70), 0, "Camera house", ["shelter"], 2, 2)],
            WrapsEastWest = false,
        };
        try
        {
            RenderMap(fixture);
            SetCameraAtImmediately(new(80, 64));
            selectedInhabitantId = person.Id;
            agentProfileRequested = false;
            RenderSelectedInhabitantCard(fixture);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var cardPosition = selectedInhabitantCard.Position;
            var start = cameraCenterTiles;
            profileFindButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!cameraGliding || cameraCenterTiles != start)
                throw new InvalidOperationException("Find must begin a glide from the shown camera without snapping.");
            AdvanceCameraMotion(CameraEasing.MoveSeconds / 2);
            var expected = start.Lerp(new(180.5f, 64.5f), 0.875f);
            if (cameraCenterTiles.DistanceTo(expected) > 0.01f || selectedInhabitantCard.Position != cardPosition)
                throw new InvalidOperationException("Find must use the approved cubic easing and hold the open card in place.");
            var retargetStart = cameraCenterTiles;
            CenterCameraAt(new(100, 64));
            if (cameraCenterTiles != retargetStart || cameraGlideFrom != retargetStart)
                throw new InvalidOperationException("A new Find must retarget from the shown position without jumping.");
            AdvanceCameraMotion(CameraEasing.MoveSeconds);
            if (CameraMoving || cameraCenterTiles.DistanceTo(new(100, 64)) > 0.01f)
                throw new InvalidOperationException("A retargeted glide must finish at its new destination.");

            var navigation = keyboardNavigation;
            var focusMode = mapCanvas.FocusMode;
            var cursor = keyboardMapTile;
            try
            {
                keyboardNavigation = true;
                mapCanvas.FocusMode = Control.FocusModeEnum.All;
                keyboardMapTile = new(80, 64);
                SetCameraAtImmediately(new(80, 64));
                mapCanvas.GrabFocus();
                CenterOnInhabitant(person.Id);
                AdvanceCameraMotion(CameraEasing.MoveSeconds / 2);
                if (!cameraGliding || cameraCenterTiles.DistanceTo(new Vector2(80, 64).Lerp(new(180.5f, 64.5f), 0.875f)) > 0.01f)
                    throw new InvalidOperationException("Focused-map Find must retain its glide while the destination cursor is still offscreen.");
                var focused = keyboardMapTile!.Value;
                HandleKeyboardMapInput(new InputEventKey { Keycode = Key.Right, Pressed = true }, fixture);
                if (CameraMoving || keyboardMapTile != new Vector2I(focused.X + 1, focused.Y))
                    throw new InvalidOperationException("Manual cursor movement must cancel Find motion and retain immediate tile navigation.");
            }
            finally
            {
                mapCanvas.ReleaseFocus();
                mapCanvas.FocusMode = focusMode;
                keyboardNavigation = navigation;
                keyboardMapTile = cursor;
            }

            selectedBuildingId = "camera-house";
            RenderBuildingCard(fixture);
            cardPosition = buildingQuickCard.Position;
            CenterOnSelectedBuilding();
            AdvanceCameraMotion(CameraEasing.MoveSeconds / 2);
            if (!cameraGliding || buildingQuickCard.Position != cardPosition)
                throw new InvalidOperationException("Building Find must glide while its quick card stays in place.");
            AdvanceCameraMotion(CameraEasing.MoveSeconds);
            if (cameraCenterTiles.DistanceTo(new(171, 71)) > 0.01f)
                throw new InvalidOperationException("Building Find must arrive at its footprint center.");

            fixture = fixture with { WorldId = "ui-camera-motion-wrap", WrapsEastWest = true };
            RenderMap(fixture);
            SetCameraAtImmediately(new(254, 64));
            CenterCameraAt(new(1, 64));
            if (cameraGlideTo.X != 257) throw new InvalidOperationException("Find must take the three-tile eastward route across the seam.");
            AdvanceCameraMotion(CameraEasing.MoveSeconds / 2);
            if (Math.Abs(PositiveMod(cameraCenterTiles.X - 254, 256) - 2.625f) > 0.01f)
                throw new InvalidOperationException("The wrapped glide must use the approved curve along the short route.");
            AdvanceCameraMotion(CameraEasing.MoveSeconds);
            if (cameraCenterTiles.DistanceTo(new(1, 64)) > 0.01f)
                throw new InvalidOperationException("A wrapped glide must finish at the canonical destination.");
            SetCameraAtImmediately(new(1, 64));
            CenterCameraAt(new(254, 64));
            if (cameraGlideTo.X != -2) throw new InvalidOperationException("The opposite wrapped Find must take the three-tile westward route.");
            AdvanceCameraMotion(CameraEasing.MoveSeconds);

            SetCameraAtImmediately(new(80, 64));
            CenterOnInhabitant(person.Id);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (cameraCenterTiles.X <= 80 || !CameraMoving)
                throw new InvalidOperationException("The actual native process frames must advance roster/agent camera travel.");
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            var beforeKey = cameraCenterTiles;
            PanCameraForFrame(new(1, 0), 0.1);
            if (CameraMoving || Math.Abs(cameraCenterTiles.X - beforeKey.X - 1.5f) > 0.01f)
                throw new InvalidOperationException("Keyboard panning must cancel a glide and remain immediate.");
            CenterCameraAt(new(180, 64));
            var beforeDrag = cameraCenterTiles;
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
            HandleMapInput(new InputEventMouseMotion { Relative = new(-3 * (currentTileSize + TileGap), 0), ButtonMask = MouseButtonMask.Middle });
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
            if (CameraMoving || Math.Abs(PositiveMod(cameraCenterTiles.X - beforeDrag.X, 256) - 3) > 0.01f)
                throw new InvalidOperationException("Middle dragging must cancel a glide and remain immediate.");

            cameraZoom = 1;
            RenderMap(fixture);
            SetCameraAtImmediately(new(128, 64));
            var pointer = mapCanvas.Size * new Vector2(0.35f, 0.4f);
            var anchor = (pointer - mapStage.Position) / (currentTileSize + TileGap);
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = pointer });
            if (!cameraZooming || cameraZoom != 1) throw new InvalidOperationException("Wheel zoom must begin at the shown zoom without snapping.");
            AdvanceCameraMotion(CameraEasing.ZoomSeconds / 2);
            if (Math.Abs(cameraZoom - 1.21875f) > 0.0001f ||
                anchor.DistanceTo((pointer - mapStage.Position) / (currentTileSize + TileGap)) > 0.01f)
                throw new InvalidOperationException("Zoom must use the approved curve and retain its cursor anchor during travel.");
            var shownZoom = cameraZoom;
            ZoomAt(pointer, zoomIn: true);
            if (cameraZoomFrom != shownZoom || cameraZoom != shownZoom || Math.Abs(cameraZoomTo - 1.5625f) > 0.0001f)
                throw new InvalidOperationException("Repeated zoom must retarget from the shown scale while accumulating the requested steps.");
            AdvanceCameraMotion(CameraEasing.ZoomSeconds);
            if (CameraMoving || Math.Abs(cameraZoom - 1.5625f) > 0.0001f ||
                anchor.DistanceTo((pointer - mapStage.Position) / (currentTileSize + TileGap)) > 0.01f)
                throw new InvalidOperationException("Zoom must finish at its requested scale without losing its pointer anchor.");
            cameraZoom = 1;
            RenderMap(fixture);
            SetCameraAtImmediately(new(128, 64));
            ZoomAt(pointer, zoomIn: true);
            ZoomAt(pointer, zoomIn: false);
            if (CameraMoving || cameraZoom != 1) throw new InvalidOperationException("Opposite queued zoom steps must cancel without a later jump.");

            CenterCameraAt(new(180, 64));
            RenderMap(fixture with { WorldId = "ui-camera-motion-reload" });
            if (CameraMoving) throw new InvalidOperationException("Opening another world must discard the previous world's camera motion.");
            GD.Print("NATIVE_CAMERA_EASING curve=cubic moveSeconds=0.7 zoomSeconds=0.35 findBuildingRosterRetargetWrapCardsPointerKeyboardDragReload=passed");
        }
        finally
        {
            CancelCameraMotion();
            selectedInhabitantId = originalAgent;
            selectedBuildingId = originalBuilding;
            agentProfileRequested = originalProfile;
            RenderMap(original);
            cameraZoom = originalZoom;
            SetCameraAtImmediately(originalCenter);
            RenderSelectedInhabitantCard(original);
            RenderBuildingCard(original);
        }
    }
}
