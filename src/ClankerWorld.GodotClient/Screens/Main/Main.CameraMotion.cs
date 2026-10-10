using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private bool cameraGliding;
    private Vector2 cameraGlideFrom;
    private Vector2 cameraGlideTo;
    private double cameraGlideElapsed;
    private bool cameraZooming;
    private float cameraZoomFrom;
    private float cameraZoomTo;
    private double cameraZoomElapsed;
    private Vector2 cameraZoomAnchor;
    private Vector2 cameraZoomPoint;
    private readonly HashSet<PanelContainer> cameraPinnedCards = [];
    private bool CameraMoving => cameraGliding || cameraZooming;

    private void BeginCameraMotion()
    {
        cameraPinnedCards.Clear();
        if (selectedInhabitantCard.Visible) cameraPinnedCards.Add(selectedInhabitantCard);
        if (buildingQuickCard.Visible) cameraPinnedCards.Add(buildingQuickCard);
    }

    private void CancelCameraMotion()
    {
        cameraGliding = false;
        cameraZooming = false;
        cameraPinnedCards.Clear();
    }

    private void SetCameraAtImmediately(Vector2 tileCenter)
    {
        if (renderedMapSnapshot is not { } snapshot || !HasMap(snapshot)) return;
        CancelCameraMotion();
        cameraCenterTiles = tileCenter;
        UpdateMapGeometry(snapshot);
        PositionSelectedInhabitantCard(snapshot);
        PositionBuildingQuickCard(snapshot);
    }

    private void AdvanceCameraMotion(double delta)
    {
        if (!CameraMoving || renderedMapSnapshot is not { } snapshot || !HasMap(snapshot)) return;
        if (cameraGliding)
        {
            cameraGlideElapsed += Math.Max(0, delta);
            cameraCenterTiles = cameraGlideFrom.Lerp(cameraGlideTo,
                CameraEasing.Amount((float)(cameraGlideElapsed / CameraEasing.MoveSeconds)));
            UpdateMapGeometry(snapshot);
            if (cameraGlideElapsed >= CameraEasing.MoveSeconds) cameraGliding = false;
        }
        else if (cameraZooming)
        {
            cameraZoomElapsed += Math.Max(0, delta);
            var amount = CameraEasing.Amount((float)(cameraZoomElapsed / CameraEasing.ZoomSeconds));
            cameraZoom = cameraZoomFrom + (cameraZoomTo - cameraZoomFrom) * amount;
            var previousSize = currentTileSize;
            UpdateMapGeometry(snapshot);
            cameraCenterTiles = cameraZoomAnchor - (cameraZoomPoint - mapCanvas.Size / 2) / (currentTileSize + TileGap);
            // Sprite sizes change only when the integer tile size does. Reuse
            // existing map nodes instead of creating animation nodes or a host request.
            if (previousSize != currentTileSize) RenderMap(snapshot);
            else UpdateMapGeometry(snapshot);
            if (cameraZoomElapsed >= CameraEasing.ZoomSeconds) cameraZooming = false;
        }
        if (!CameraMoving)
        {
            cameraPinnedCards.Clear();
            PositionSelectedInhabitantCard(snapshot);
            PositionBuildingQuickCard(snapshot);
        }
    }
}
