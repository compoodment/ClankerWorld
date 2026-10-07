using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void VerifyYardAnimalInspection()
    {
        var yard = new OwnerWorldPlacedBuilding("inspection-yard", "sha256:inspection/animal-yard", new(120, 60), 0,
            "Animal yard", ["animal-yard", "animal-care"], 2, 2, null, "home", [], new(120, 62),
            StorageCapacity: 16, StoredQuantity: 0);
        foreach (var inside in new[] { false, true })
        {
            var position = new OwnerWorldPosition(inside ? 120 : 122, 60);
            var cow = new OwnerWorldAnimal("inspection-cow", "Moss", "cow", "female", 40, "adult", position,
                "home", "Cedar household", "cared", "milk", 2, null, null, null, null, false, ["Linden"], []);
            var map = new OwnerWorldSnapshot("yard-inspection-smoke", 1, "yard-inspection-map", [], [], [], null, 0)
            {
                Animals = [cow],
                PlacedBuildings = [yard],
                PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(new byte[256 * 128])),
            };
            ClearBuildingSelection();
            ClearTileSelection();
            cameraZoom = 3;
            cameraCenterTiles = new(120, 60);
            RenderMap(map);
            var click = mapStage.Position + new Vector2((position.X + .5f) * (currentTileSize + TileGap),
                (position.Y + .5f) * (currentTileSize + TileGap));
            HandleMapInput(new InputEventMouseButton { Position = click, ButtonIndex = MouseButton.Left, Pressed = true });
            string text;
            if (inside)
            {
                if (!buildingQuickCard.Visible || buildingQuickHeader.NameLabel.Text != "Animal yard")
                    throw new InvalidOperationException("The yard must keep its real building card accessible.");
                buildingDetailsButton.EmitSignal(BaseButton.SignalName.Pressed);
                text = string.Join('\n', buildingDetailsContent.FindChildren("*", nameof(Label), owned: false)
                    .OfType<Label>().Select(label => label.Text));
                if (!buildingDetailsPanel.Visible) throw new InvalidOperationException("Yard Details must remain open for inspection.");
            }
            else
            {
                if (!selectedTilePanel.Visible || buildingQuickCard.Visible)
                    throw new InvalidOperationException("An animal on open ground must retain its normal click inspection.");
                text = TileCardText();
            }
            var description = GameUiText.AnimalDescription(cow);
            var inspected = text.Contains(description, StringComparison.Ordinal);
            GD.Print($"YARD_ANIMAL_INSPECTION inside={inside} inspected={inspected} tile={selectedTilePanel.Visible} building={buildingDetailsPanel.Visible}");
            if (!inspected)
                throw new InvalidOperationException("Click inspection must expose Moss's name, care, ready milk and named care permission, inside or outside a yard.");
            UpdateTileHover(mapStage.Position + new Vector2(125.5f, 65.5f) * (currentTileSize + TileGap));
            if (inside && !buildingDetailsPanel.Visible || !inside && !selectedTilePanel.Visible)
                throw new InvalidOperationException("Moving the pointer must not dismiss animal inspection.");
            if (inside)
            {
                RenderMap(map with { Animals = [] });
                text = string.Join('\n', buildingDetailsContent.FindChildren("*", nameof(Label), owned: false)
                    .OfType<Label>().Select(label => label.Text));
                if (text.Contains("Moss", StringComparison.Ordinal))
                    throw new InvalidOperationException("Updated yard inspection must remove animals that are no longer there.");
            }
        }
        ClearBuildingSelection();
        ClearTileSelection();
    }
}
