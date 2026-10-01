using ClankerWorld.GodotClient.Pairing;
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
        VerifyBuildingManagementRefresh(baseMap);
    }

    private void VerifyBuildingManagementRefresh(OwnerWorldSnapshot baseMap)
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousInvalid = registeredEndpointInvalid;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        try
        {
            registration = new(new OwnerAuthorityIdentity("building-smoke", baseMap.WorldId),
                "building-smoke-device", signer.PublicKeyFingerprint, "http://127.0.0.1/");
            deviceKey = signer;
            registeredEndpointInvalid = false;
            var workshop = new OwnerWorldPlacedBuilding("choice-workshop", "test/workshop", new(1, 1), 0,
                "Workshop", ["workshop"], TownId: "town:first", HouseholdId: "household:one")
            { AllowsHouseholdOwner = true };
            var second = workshop with { InstanceId = "second-workshop", Position = new(2, 1) };
            var map = baseMap with
            {
                PlacedBuildings = [workshop, second],
                Stockpiles = [new("household:one", "Current", []), new("household:two", "Alpha", []),
                    new("household:three", "Beta", [])],
                ProductionJobs = [],
            };
            ClearBuildingSelection();
            RenderMap(map);
            SelectBuilding(workshop.InstanceId);
            OpenBuildingDetails();
            if (!buildingManagementSection.Visible || buildingManagementChoice.ItemCount != 3)
                throw new InvalidOperationException("A paired owner must receive the host's household choices and the unowned option.");
            buildingManagementChoice.Select(1);
            RenderBuildingCard(map with { WorldTick = map.WorldTick + 1 });
            if (ChosenOwner() != "household:three")
                throw new InvalidOperationException("An observation refresh must preserve the owner's chosen household.");
            var reordered = map with
            {
                Stockpiles = [new("household:one", "Current", []), new("household:two", "Zed", []),
                    new("household:three", "Aaron", [])],
            };
            RenderBuildingCard(reordered);
            if (ChosenOwner() != "household:three" || buildingManagementChoice.Selected != 0)
                throw new InvalidOperationException("Owner choices must survive reordered display names by household ID.");
            buildingManagementChoice.Select(2);
            RenderBuildingCard(map);
            if (ChosenOwner() != string.Empty)
                throw new InvalidOperationException("The explicit no-household choice must survive a refresh.");
            SelectBuilding(second.InstanceId);
            if (ChosenOwner() != "household:two")
                throw new InvalidOperationException("Choosing another building must reset its owner choice.");
            buildingManagementChoice.Select(1);
            RenderBuildingCard(map with { Stockpiles = map.Stockpiles.Where(item => item.OwnerId != "household:three").ToArray() });
            if (ChosenOwner() != "household:two")
                throw new InvalidOperationException("A removed owner choice must fall back to a current host-provided option.");
            RenderBuildingCard(map with
            {
                PlacedBuildings = [second with { Tags = ["house"], HouseholdId = null }],
                Stockpiles = [],
            });
            if (buildingManagementChoice.ItemCount != 0 || buildingManagementApply.Visible)
                throw new InvalidOperationException("A building without current owner options must not offer reassignment.");
            RenderBuildingCard(map);
            if (ChosenOwner() != "household:two")
                throw new InvalidOperationException("Returning owner options must start with a current choice.");

            buildingRemoveButton.EmitSignal(BaseButton.SignalName.Pressed);
            RenderBuildingCard(map with
            {
                PlacedBuildings = [workshop, second with { HouseholdId = "household:three" }],
            });
            if (!buildingRemoveConfirmation.Visible || pendingBuildingRemoval is not
                { InstanceId: "second-workshop", ExpectedTownId: "town:first", ExpectedHouseholdId: "household:one" } || pendingBuildingRemoval.WorldId != map.WorldId)
                throw new InvalidOperationException("Removal must retain the owner record originally confirmed so the host can reject a stale change.");
            buildingRemoveConfirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            if (pendingBuildingRemoval is not null || pendingBuildingRemovalWorldId is not null)
                throw new InvalidOperationException("Canceling removal must clear its retained action.");
            buildingRemoveButton.EmitSignal(BaseButton.SignalName.Pressed);
            RenderBuildingCard(map with { WorldId = "different-building-world" });
            if (pendingBuildingRemoval is not null || buildingRemoveConfirmation.Visible || ChosenOwner() != "household:two")
                throw new InvalidOperationException("A world change must cancel removal and reset its owner choice.");
            RenderBuildingCard(map);
            buildingRemoveButton.EmitSignal(BaseButton.SignalName.Pressed);
            RenderBuildingCard(map with { PlacedBuildings = [workshop] });
            if (pendingBuildingRemoval is not null || selectedBuildingId is not null || buildingRemoveConfirmation.Visible)
                throw new InvalidOperationException("A disappeared building must cancel its removal confirmation.");
        }
        finally
        {
            ClearBuildingSelection();
            registration = previousRegistration;
            deviceKey = previousKey;
            registeredEndpointInvalid = previousInvalid;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
        }

        string? ChosenOwner() => buildingManagementChoice.Selected < 0 ? null :
            buildingManagementChoice.GetItemMetadata(buildingManagementChoice.Selected).AsString();
    }
}
