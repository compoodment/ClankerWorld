using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Clicking a building opens its quick card and outlines it; Details docks
    /// on the left with its facts, work, storage and people; Escape steps back;
    /// an agent's card replaces it; world updates refresh it; and Details stays
    /// inside the view.
    /// </summary>
    private async Task VerifyBuildingCardsAsync(OwnerWorldSnapshot baseMap)
    {
        var smith = new OwnerWorldInhabitant("agent:smith-ui-test", "Oren", "active", new(2, 2), 8_000, [], [],
            new OwnerWorldRoute("idle", null, null, [], string.Empty),
            new OwnerWorldSpatialKnowledge(new(2, 2), [new(2, 2)], [new(2, 2)]), false)
        {
            Relationships = [new OwnerWorldInhabitantRelationship("home:oren", "household:one",
                "household_membership", "accepted", "household", 1)],
        };
        var house = new OwnerWorldPlacedBuilding("test-house", "sha256:test/house", new(2, 2), 0, "House", ["house"], 2, 1,
            "town:first", "household:one", [new("wood", 4), new("bread", 2), new("never_an_item", 1)], new(2, 3));
        var buildingMap = baseMap with
        {
            WorldTick = 30,
            Inhabitants = [smith],
            PlacedBuildings = [.. baseMap.PlacedBuildings.Where(item => item.InstanceId != house.InstanceId), house],
            ProductionJobs = [new("job-ui-test", "sha256:test/wooden-axe", house.InstanceId, smith.Id, 10, 50, "running")],
        };
        RenderMap(buildingMap);
        HandleMapInput(new InputEventMouseButton
        {
            Position = mapStage.Position + new Vector2(currentTileSize * 3.5f, currentTileSize * 2.5f),
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        });
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var quickText = string.Join('\n', buildingQuickStatus.FindChildren("*", "Label", owned: false)
            .OfType<Label>().Select(label => label.Text));
        if (!buildingQuickCard.Visible || buildingDetailsPanel.Visible || selectedTilePanel.Visible ||
            terrainLayer.SelectedBuilding != new Rect2I(2, 2, 2, 1) ||
            buildingQuickHeader.NameLabel.Text != "House" ||
            buildingQuickHeader.OwnerLabel.Text != "Founder's household · First Town" ||
            !quickText.Contains("Wooden axe", StringComparison.Ordinal) ||
            !quickText.Contains("50%", StringComparison.Ordinal) ||
            !quickText.Contains("Oren · ", StringComparison.Ordinal) ||
            buildingQuickStorage.SlotCount != 3 || buildingQuickStorage.Summary != "7 items" ||
            !mapCanvas.GetGlobalRect().Grow(1).Encloses(buildingQuickCard.GetGlobalRect()))
            throw new InvalidOperationException($"Clicking a building must outline it and open its quick card with its owner, work and stored items: {quickText} / {buildingQuickHeader.OwnerLabel.Text} / {buildingQuickStorage.Summary}.");

        buildingDetailsButton.EmitSignal(BaseButton.SignalName.Pressed);
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
        if (!buildingDetailsPanel.Visible || buildingQuickCard.Visible ||
            !facts.Contains("Owner\nFounder's household", StringComparison.Ordinal) ||
            !facts.Contains("Used by\nFounder's household", StringComparison.Ordinal) ||
            !facts.Contains("Built\n", StringComparison.Ordinal) ||
            !facts.Contains("Door\nSouth side", StringComparison.Ordinal) ||
            !buildingWorkSection.Visible || buildingWorkRows.GetChildCount() != 1 ||
            buildingDetailsStorage.Summary != "3 kinds · 7 items" || buildingDetailsStorage.SlotCount != 3 ||
            !buildingPeopleText.Text.Contains("Inside: Oren", StringComparison.Ordinal) ||
            !buildingPeopleText.Text.Contains("Home of Founder's household: Oren", StringComparison.Ordinal) ||
            !mapCanvas.GetGlobalRect().Grow(1).Encloses(buildingDetailsPanel.GetGlobalRect()) ||
            buildingDetailsPanel.Position.X > 14.5f)
            throw new InvalidOperationException($"Details must dock on the left with the building's facts, work, storage and people: {facts} / {buildingPeopleText.Text} / {buildingDetailsPanel.GetGlobalRect()}.");

        // World updates refresh the open panel, which scrolls rather than running off the view.
        RenderBuildingCard(buildingMap with
        {
            PlacedBuildings = [.. buildingMap.PlacedBuildings.Where(item => item.InstanceId != house.InstanceId),
                house with
                {
                    StoredItems = [new("wood", 5), new("bread", 2), new("never_an_item", 1), new("fruit", 3)],
                    Width = 2, Height = 2, StorageCapacity = 256, StoredQuantity = 11, FootprintRevision = 2,
                    InvitedGuests = ["Lina"], ExpansionState = "completed",
                }],
            ProductionJobs = [],
        });
        if (buildingDetailsStorage.Summary != "4 kinds · 11 items" || buildingDetailsStorage.SlotCount != 4 ||
            buildingWorkSection.Visible)
            throw new InvalidOperationException("Building Details must follow the building's latest storage and work.");
        facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
        if (!facts.Contains("Footprint\n2 × 2 tiles", StringComparison.Ordinal) ||
            !facts.Contains("Storage\n11 / 256 items", StringComparison.Ordinal) ||
            !facts.Contains("Storm guests\nLina · shelter only", StringComparison.Ordinal))
            throw new InvalidOperationException("Building Details must show current expansion geometry, capacity and limited guest access.");
        RenderBuildingCard(buildingMap with
        {
            PlacedBuildings = [house with
            {
                DisplayName = "Store", Tags = ["store", "storage"],
                Trades = [new("trade-ui-test", "Lina", "wooden_axe", 1, "wood", 3, "open", null)],
            }],
        });
        facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
        quickText = string.Join('\n', buildingQuickStatus.FindChildren("*", "Label", owned: false)
            .OfType<Label>().Select(label => label.Text));
        if (!facts.Contains("1 Wooden axe for 3 Wood", StringComparison.Ordinal) ||
            !facts.Contains("waiting for both traders at the shop", StringComparison.Ordinal) ||
            !facts.Contains("household stock and other uses remain private", StringComparison.Ordinal) ||
            !quickText.Contains("1 customer exchange waiting", StringComparison.Ordinal) ||
            BuildingSprites.KindFor(["store", "storage"]) != BuildingKind.Store)
            throw new InvalidOperationException("A shop card must show exact terms, transaction progress and limited customer access.");
        ApplyResponsiveLayout();
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!GetViewportRect().Grow(1).Encloses(buildingDetailsPanel.GetGlobalRect()))
            throw new InvalidOperationException($"Building Details must stay inside the view: {buildingDetailsPanel.GetGlobalRect()}.");

        // Escape closes open top-bar panels first, so keep Filters out of the way.
        var filtersWereOpen = filtersPanel.Visible;
        filtersPanel.Hide();
        if (!HandleEscape() || buildingDetailsPanel.Visible || !buildingQuickCard.Visible)
            throw new InvalidOperationException("Escape in building Details must go back to the quick card.");
        if (!HandleEscape() || buildingQuickCard.Visible || terrainLayer.SelectedBuilding is not null)
            throw new InvalidOperationException("Escape on a building's quick card must close it and clear the outline.");

        filtersPanel.Visible = filtersWereOpen;

        SelectBuilding(house.InstanceId);
        selectedInhabitantId = smith.Id;
        RenderSelectedInhabitantCard(buildingMap);
        if (buildingQuickCard.Visible || selectedBuildingId is not null || !selectedInhabitantCard.Visible)
            throw new InvalidOperationException("Choosing an agent must replace a building's quick card with theirs.");
        ClearInhabitantSelection();
        SelectBuilding(house.InstanceId);
        RenderBuildingCard(baseMap with { PlacedBuildings = baseMap.PlacedBuildings.Where(item => item.InstanceId != house.InstanceId).ToArray() });
        if (buildingQuickCard.Visible || selectedBuildingId is not null)
            throw new InvalidOperationException("A building that is gone must close its card.");
        var fruitSlot = new ItemSlot { IconSize = 32, Named = true };
        fruitSlot.SetItem("fruit", 3, "Fruit");
        if (fruitSlot.TooltipText != "Fruit × 3" || fruitSlot.CustomMinimumSize.Y <= fruitSlot.CustomMinimumSize.X - 8)
            throw new InvalidOperationException("A named item slot must leave room for its name and say what it holds.");
        fruitSlot.Free();
    }
}
