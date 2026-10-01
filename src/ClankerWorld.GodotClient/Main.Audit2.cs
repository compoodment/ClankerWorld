#pragma warning disable CA1859, CA1822
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

// LOCAL ONLY - never commit. Captures every panel for the UI audit.
public partial class Main
{
    private static OwnerWorldSnapshot AuditWorld(OwnerWorldSnapshot snapshot)
    {
        var tick = snapshot.WorldTick;
        OwnerWorldInhabitant Enrich(OwnerWorldInhabitant person) => person.Id switch
        {
            "mira" => person with
            {
                Inventory = [new("clay", 3)],
                Relationships =
                [
                    new("r1", "rowan", "partnership", "accepted", "public", 100),
                    new("p1", "pip", "biological_parentage", "accepted", "public", 455, "parent"),
                ],
                RecentMemories =
                [
                    new(tick - 200, "rowan", "Rowan", "Rowan shared the last of the bread with me when the berries ran out.", "private"),
                    new(tick - 520, "ash", "Ash", "Ash showed me how to shape a stone axe head.", "shared"),
                ],
                RecentBeliefs =
                [
                    new(tick - 90, "There is good clay along the river bank south of Riverbend.", "firsthand", 9000, null, null, null, null, false, null),
                    new(tick - 300, "Wren is saving seed for spring.", "hearsay", 6000, "ash", "Ash", null, "wren", false, null),
                ],
                RecentKnowledgeFacts =
                [
                    new(tick - 95, 12, 26, "river", ["clay"], "Mira", "firsthand", null),
                ],
                KnowledgeArtifacts =
                [
                    new("map-1", "field_map", "Berry bushes east of camp", tick - 400, "Mira",
                        [new(22, 14, "meadow", ["berries"], "Mira"), new(24, 12, "forest", ["berries", "wood"], "Mira")]),
                ],
            },
            "rowan" => person with
            {
                Survival = new OwnerWorldSurvival(5_600, 0, true, false, 6_100, null),
                PublicIntention = new("rest", "resting by the fire", "openai", tick),
                Relationships =
                [
                    new("r1", "mira", "partnership", "accepted", "public", 100),
                    new("p2", "pip", "biological_parentage", "accepted", "public", 455, "parent"),
                ],
            },
            "pip" => person with
            {
                Survival = new OwnerWorldSurvival(9_000, 0, true, false, 8_000, null),
                PublicIntention = new("play", "playing near the warehouse", "deterministic", tick),
                Relationships =
                [
                    new("p1", "mira", "biological_parentage", "accepted", "public", 455, "child"),
                    new("p2", "rowan", "biological_parentage", "accepted", "public", 455, "child"),
                ],
            },
            "ash" => person with
            {
                Survival = new OwnerWorldSurvival(7_800, 1_800, true, true, 5_200, null),
                PublicIntention = new("craft", "making a stone axe", "openai", tick),
                Project = new OwnerWorldProject("Kiln", "building", 36, 100, null, tick - 60),
                SocialNotes = ["Taught Mira how to shape an axe head."],
            },
            "wren" => person with
            {
                HungerBasisPoints = 2_400,
                Survival = new OwnerWorldSurvival(6_900, 0, true, false, 4_800, null),
                PublicIntention = new("forage", "looking for berries", "ollama-cloud", tick),
                Project = new OwnerWorldProject("Seed store", "gathering", 2, 10, "Needs 4 more clay before work can start.", tick - 30),
                SocialNotes = ["Shared berries with Pip."],
            },
            "tamsin" => person with
            {
                Survival = new OwnerWorldSurvival(3_100, 0, false, false, 6_600, null),
                PublicIntention = new("harvest", "harvesting wheat in the field", "openai", tick),
            },
            _ => person,
        };
        var buildings = snapshot.PlacedBuildings.Select(building => building.InstanceId switch
        {
            "house-a" => building with { DisplayName = "House", TownId = "town:first", HouseholdId = "household:ash", StoredItems = [new("berries", 6), new("bread", 2), new("wood", 4)], Entrance = new(10, 16) },
            "smith" => building with { DisplayName = "Blacksmith", TownId = "town:first", HouseholdId = "household:reed", StoredItems = [new("stone", 3), new("wood", 2)] },
            "warehouse" => building with { DisplayName = "Warehouse", TownId = "town:first", StoredItems = [new("wood", 14), new("stone", 6), new("clay", 3), new("fiber", 8), new("wooden_axe", 1)] },
            _ => building with { TownId = "town:first" },
        }).ToArray();
        return snapshot with
        {
            Inhabitants = snapshot.Inhabitants.Select(Enrich).ToArray(),
            PlacedBuildings = buildings,
            Stockpiles = [new("household:ash", "Ash household", [new("berries", 9), new("bread", 3), new("wood", 12), new("clay", 3)]), new("household:reed", "Reed household", [])],
            Authoring = new OwnerWorldAuthoringState(false, 1, 1, 1, "m", "m", "clear", "spring", []),
            ContentPackages =
            [
                new("riverbend.pottery", "1.0.0", "sha256:a", "active", null, 400, 410, 420, null, "Pottery and kilns", "ash"),
                new("riverbend.fishing", "0.2.0", "sha256:b", "proposed", null, null, null, null, null, "River fishing", "mira"),
            ],
        };
    }

    private async Task RunAudit2(OwnerWorldSnapshot baseSnapshot, OwnerWorldEvent[] events, string output)
    {
        var snapshot = AuditWorld(baseSnapshot);
        var border = new List<OwnerWorldPosition>();
        for (var y = 11; y <= 25; y++)
            for (var x = 6; x <= 22; x++)
                if (x is 6 or 22 || y is 11 or 25) border.Add(new OwnerWorldPosition(x, y));
        snapshot = snapshot with { Towns = snapshot.Towns.Select(town => town with { BorderTiles = border }).ToArray() };
        events = events.Select(item => item.Kind switch
        {
            "food_harvested" or "build_completed" or "build_started" or "recipe_completed" or "child_born" => item with { Position = new OwnerWorldPosition(12, 18) },
            _ => item,
        }).Append(new OwnerWorldEvent(13, 925, "inhabitant_removed", "old-tom")).ToArray();
        observedCalendarPace = snapshot.CalendarPace;
        selectedInhabitantId = null;
        Render(snapshot, events);
        statusToast.Hide();
        eventsPanel.Hide();
        Input.WarpMouse(new Vector2(4, GetViewport().GetVisibleRect().Size.Y - 4));

        void CloseAll()
        {
            foreach (var panel in HudPanels()) panel.Hide();
            memoriesPanel.Hide();
            thoughtsPanel.Hide();
            familyTreePanel.Hide();
            controlsPanel.Hide();
            selectedTilePanel.Hide();
            gameMenuPanel.Hide();
            menuShade.Hide();
            statusToast.Hide();
        }
        var only = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--only=", StringComparison.Ordinal))?["--only=".Length..].Split(',');
        async Task Shot(string name, Action open)
        {
            if (only is not null && !only.Contains(name)) return;
            CloseAll();
            try { open(); }
            catch (Exception exception) { GD.Print($"AUDIT2 {name} failed: {exception.Message}"); }
            ApplyResponsiveLayout();
            await MockFrames(8);
            GetViewport().GetTexture().GetImage().SavePng($"{output}-a2-{name}.png");
        }

        await Shot("roster", ToggleInhabitants);
        await Shot("towns", () => { ToggleWorldInfo(); ShowWorldInfoPage(true); });
        await Shot("world", () => { ToggleWorldInfo(); ShowWorldInfoPage(false); });
        await Shot("events", () => { ToggleEvents(); RenderEventLog(); });
        await Shot("map", () => mapButton.EmitSignal(BaseButton.SignalName.Pressed));
        await Shot("filters", ToggleMapFilters);
        await Shot("controls", ToggleControlsPanel);
        await Shot("tile", () =>
        {
            selectedTile = new Vector2I(20, 12);
            terrainLayer.SetSelectedTile(selectedTile);
            selectedTilePanel.Show();
            RenderTileInspection(snapshot);
        });
        await Shot("hover", () => UpdateHoverReadout(snapshot, new Vector2I(30, 22)));
        var tree = snapshot.Resources.FirstOrDefault(resource => resource.TreeKind is not null && resource.Position.X > 18 && resource.Position.X < 30);
        if (tree is not null)
            await Shot("tile-tree", () =>
            {
                selectedTile = new Vector2I(tree.Position.X, tree.Position.Y);
                terrainLayer.SetSelectedTile(selectedTile);
                selectedTilePanel.Show();
                RenderTileInspection(snapshot);
            });
        await Shot("house", () => SelectBuilding("house-a"));
        await Shot("house-details", () => { SelectBuilding("house-a"); OpenBuildingDetails(); });
        await Shot("warehouse-details", () => { SelectBuilding("warehouse"); OpenBuildingDetails(); });
        ClearBuildingSelection();
        selectedInhabitantId = "mira";
        Render(snapshot, []);
        await Shot("card", () => { });
        agentProfileRequested = true;
        Render(snapshot, []);
        foreach (var button in new[] { instructionSuggestButton, instructionOrderButton, submitInstructionButton, modelSettingsButton })
            button.Disabled = false;
        await Shot("profile", () => { });
        await Shot("memories", () => { RenderMemoryHistory(snapshot, snapshot.Inhabitants.First(person => person.Id == "mira")); OpenMemories(); });
        await Shot("family", () => ShowFamilyTree(snapshot, "mira"));
        await Shot("speak", () => OpenAgentProfile(speak: true));
        providerConfiguration = new OwnerProviderConfigurationStatus("openai", "deterministic", 1,
            [new("openai", "gpt-6-luna", true), new("ollama-cloud", "glm-5.3-flash:cloud", true)],
            [new InhabitantProviderAssignment("mira", "planning", "openai", "gpt-6-luna", "slot-home")],
            [new("slot-home", "openai", "Home account"), new("slot-work", "openai", "Work account")]);
        await Shot("model", () =>
        {
            cognitionTargetChoice.Clear();
            cognitionTargetChoice.AddItem("World defaults");
            cognitionTargetChoice.AddItem("Mira");
            cognitionTargetChoice.SetItemMetadata(1, "mira");
            cognitionTargetChoice.Select(1);
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            cognitionTargetChoice.Hide();
            cognitionSettingsPanel.Reparent(selectedAgentModelContent, keepGlobalTransform: false);
            selectedAgentOverview.Hide();
            renameRow.Hide();
            selectedAgentModelScroll.Show();
            RenderProviderConfiguration();
            cognitionModelPicker.ShowList([new("gpt-6.1-sol", true), new("gpt-6-sol", true), new("gpt-6-luna", true)], "gpt-6-luna");
            foreach (var control in cognitionSettingsPanel.FindChildren("*", "Control", true, false))
            {
                if (control is BaseButton button) button.Disabled = false;
                if (control is LineEdit edit) edit.Editable = true;
            }
        });
        agentProfileRequested = false;
        selectedInhabitantId = null;
        Render(snapshot, []);
        await Shot("addagent", () => { founderSetupPanel.Show(); ResetAddAgentPlacementHint(); });
        await Shot("toast", () => SetStatus("Mira's model is ready.", good: true));
        await Shot("toast-bad", () => SetStatus("Couldn't reach the server. Retrying…", good: false));
        void OpenPause()
        {
            menuHeadingLabel.Text = "Paused";
            StyleIconButton(menuCloseButton, PixelGlyph.Close);
            SetWorldMenuActionsVisible(true);
            menuResumeButton.Show();
            settingsPanel.Hide();
            modLibraryPanel.Hide();
            menuActions.Show();
            gameMenuPanel.Show();
            menuShade.Show();
        }
        await Shot("pause", OpenPause);
        await Shot("mods", () => { OpenPause(); ShowModLibrary(); RenderModLibrary(snapshot); });
        await Shot("devtools", () =>
        {
            OpenPause();
            ShowSettingsSection(worldSpecific: false);
            developerToggleButton.EmitSignal(BaseButton.SignalName.Pressed);
        });
        await Shot("devtools-new", () =>
        {
            var paused = snapshot with { Authoring = snapshot.Authoring! with { IsPaused = true } };
            Render(paused, []);
            var panel = MockDevTools();
            panel.Name = "MockDevTools";
            uiLayer.AddChild(panel);
            panel.Size = panel.GetCombinedMinimumSize();
            panel.Position = new Vector2(UiSize.X - panel.Size.X - 14, HudTop);
            // Wrapped hint text settles a frame later; fit to it then.
            Callable.From(() => panel.Size = new Vector2(panel.Size.X, 0)).CallDeferred();
            terrainLayer.SetHoveredTile(new Vector2I(22, 14));
            hoverReadoutLabel.Text = "Click to place a berry bush";
            hoverReadoutSwatch.Texture = DevSprite(NatureSprite.BerryBush);
            hoverReadout.Show();
            PositionMapHud();
        });
        foreach (var leftover in uiLayer.GetChildren().Where(child => child.Name == "MockDevTools")) leftover.QueueFree();
        hoverReadout.Hide();
        Render(snapshot, []);
        CloseAll();
        await Shot("quit", () => menuQuitToMainButton.EmitSignal(BaseButton.SignalName.Pressed));
        quitToMenuConfirmation.Hide();
        var redesigned = typeof(Main).GetMethod("RenderTileCard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) is not null;
        deletionConfirmation.DialogText = redesigned
            ? "Permanently delete \"Riverbend\" and all of its manual saves and autosaves? Your other worlds and account settings stay unchanged. There is no undo."
            : "Permanently delete ‘Riverbend’ and all of its manual saves and autosaves? Your other worlds and account settings stay unchanged. There is no undo.";
        await Shot("delete", () => PopupDialog(deletionConfirmation));
        deletionConfirmation.Hide();
    }
}
