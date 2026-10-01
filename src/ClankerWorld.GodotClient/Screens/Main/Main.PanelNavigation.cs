using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void ToggleInhabitants()
    {
        var show = !rosterPanel.Visible;
        filtersPanel.Hide();
        familyTreePanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        rosterPanel.Visible = show;
    }

    private void ToggleEvents()
    {
        var show = !eventsPanel.Visible;
        filtersPanel.Hide();
        familyTreePanel.Hide();
        rosterPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        eventsPanel.Visible = show;
        if (show) MarkEventsSeen();
    }

    private void ToggleWorldInfo()
    {
        var show = !worldInfoPanel.Visible;
        filtersPanel.Hide();
        familyTreePanel.Hide();
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Visible = show;
    }

    private void OpenFamilyTree()
    {
        if (selectedInhabitantId is not { } id || observationSession.Current is not { } current)
            return;
        ShowFamilyTree(current.Baseline.Snapshot, id);
    }

    private void ShowFamilyTree(OwnerWorldSnapshot snapshot, string id)
    {
        memoriesPanel.Hide();
        thoughtsPanel.Hide();
        conversationPanel.Hide();
        familyTreeView.SetPeople(snapshot.WorldId, snapshot.Inhabitants, id);
        UpdateFamilyTreeStatus();
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        filtersPanel.Hide();
        familyTreePanel.Show();
        ApplyResponsiveLayout();
    }

    private void UpdateFamilyTreeStatus()
    {
        // The key explains lines that are there; with none, say why the tree is just them.
        var empty = familyTreeView.ParentEdgeCount + familyTreeView.PartnerEdgeCount == 0;
        familyTreeStatus.Visible = empty;
        familyLegend.Visible = !empty;
    }

    private void SelectFromFamilyTree(string id)
    {
        familyTreePanel.Hide();
        selectedInhabitantId = id;
        if (observationSession.Current is not { } current) return;
        var snapshot = current.Baseline.Snapshot;
        RenderInhabitantList(snapshot);
        RenderSelectedInhabitantCard(snapshot);
        RenderMap(snapshot);
    }

    private void OpenMemories()
    {
        if (selectedInhabitantId is null) return;
        familyTreePanel.Hide();
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        thoughtsPanel.Hide();
        conversationPanel.Hide();
        memoriesPanel.Show();
        ApplyResponsiveLayout();
    }

    private async Task TogglePauseAsync()
    {
        var paused = observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused == true;
        await SetPausedAsync(!paused);
    }

    private async Task ToggleGameMenuAsync()
    {
        if (gameMenuPanel.Visible)
        {
            await CloseGameMenuAsync();
            return;
        }

        rosterPanel.Hide();
        eventsPanel.Hide();
        familyTreePanel.Hide();
        memoriesPanel.Hide();
        thoughtsPanel.Hide();
        conversationPanel.Hide();
        returnToMainMenu = false;
        menuResumeButton.Text = "Resume";
        SetWorldMenuActionsVisible(true);
        settingsPanel.Hide();
        modLibraryPanel.Hide();
        menuActions.Show();
        menuHeadingLabel.Text = "Paused";
        StyleIconButton(menuCloseButton, PixelGlyph.Close);
        menuCloseButton.TooltipText = "Return to the world";
        worldInfoPanel.Hide();
        gameMenuPanel.Show();
        menuShade.Show();
        ApplyResponsiveLayout();

        var paused = observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused == true;
        menuPausedWorld = observationSession.Current is not null && !paused;
        // A paused observation may follow a failed checkpoint write. Only a
        // successful pause receipt proves the menu's durable exit boundary.
        menuPauseConfirmed = false;
        if (observationSession.Current is not null)
        {
            menuPauseConfirmed = await SetPausedAsync(paused: true);
        }
    }

    /// <summary>Whether Settings or the Mod Library covers the pause menu's buttons.</summary>
    private bool PauseMenuPageOpen => !returnToMainMenu && gameMenuPanel.Visible && !menuActions.Visible;

    /// <summary>
    /// Settings and the Mod Library open in place of the pause menu's buttons,
    /// with a back arrow, so the menu stays one screen tall like Main Menu
    /// Settings.
    /// </summary>
    private void ShowPauseMenuPage(string title)
    {
        if (returnToMainMenu || !gameMenuPanel.Visible) return;
        menuActions.Hide();
        menuHeadingLabel.Text = title;
        StyleIconButton(menuCloseButton, PixelGlyph.Back);
        menuCloseButton.TooltipText = "Back (Esc)";
    }

    private void ShowPauseMenuButtons()
    {
        CloseAgentModelEditor();
        settingsPanel.Hide();
        modLibraryPanel.Hide();
        developerScroll.Hide();
        menuActions.Show();
        menuHeadingLabel.Text = "Paused";
        StyleIconButton(menuCloseButton, PixelGlyph.Close);
        menuCloseButton.TooltipText = "Return to the world";
        ApplyResponsiveLayout();
    }

    private async Task CloseGameMenuAsync()
    {
        if (returnToMainMenu)
        {
            CloseGameMenu();
            ShowMainMenu();
            return;
        }
        var resumeWorld = menuPausedWorld;
        CloseGameMenu();
        if (resumeWorld)
        {
            await SetPausedAsync(paused: false);
        }
    }

    private void CloseGameMenu()
    {
        cognitionApiKeyInput.Text = string.Empty;
        gameMenuPanel.Hide();
        menuShade.Hide();
        settingsPanel.Hide();
        modLibraryPanel.Hide();
        developerScroll.Hide();
        menuActions.Show();
        menuPausedWorld = false;
        menuPauseConfirmed = false;
        StyleIconButton(menuCloseButton, PixelGlyph.Close);
        menuCloseButton.TooltipText = "Return to the world";
    }

    private void OpenMenuForSetup()
    {
        // Connection and pairing share Main Menu Settings' presentation: the
        // title backdrop stays up and one compact header button goes back.
        OpenMainMenuSettings();
        menuPausedWorld = false;
        menuHeadingLabel.Text = "Connect this device";
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            if (HandleEscape()) GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is not InputEventKey { Pressed: true } key || mainMenuOverlay.Visible || gameMenuPanel.Visible ||
            GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
        {
            return;
        }
        if (HandleShortcutKey(key))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode is Key.W or Key.Up or Key.A or Key.Left or Key.S or Key.Down or Key.D or Key.Right)
            GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!GetWindow().HasFocus()) return;
        var direction = new Vector2(
            (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) -
            (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0),
            (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) -
            (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0));
        PanCameraForFrame(direction, delta);
    }

    private void PanCameraForFrame(Vector2 direction, double delta)
    {
        if (direction == Vector2.Zero || mainMenuOverlay.Visible || gameMenuPanel.Visible ||
            worldMenuOverlay.Visible || GetViewport().GuiGetFocusOwner() is not null) return;
        PanCamera(direction.Normalized() * (float)(15 * Math.Clamp(delta, 0, 0.1)));
    }

    /// <summary>
    /// Escape backs out one step at a time: a focused text field, a modal
    /// menu, a map-click mode, the newest open panel, the selected agent, and
    /// finally opens the Pause Menu. It never commits a world change itself.
    /// </summary>
    private bool HandleEscape()
    {
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
        {
            GetViewport().GuiGetFocusOwner()!.ReleaseFocus();
            return true;
        }
        if (worldMenuOverlay.Visible)
        {
            if (!worldMenuBusy) worldMenuOverlay.Hide();
            return true;
        }
        if (manualSaveOverlay.Visible)
        {
            manualSaveOverlay.Hide();
            return true;
        }
        if (gameMenuPanel.Visible)
        {
            if (PauseMenuPageOpen) ShowPauseMenuButtons();
            else _ = CloseGameMenuAsync();
            return true;
        }
        if (mainMenuOverlay.Visible || !isInWorld) return false;
        if (controlsPanel.Visible)
        {
            controlsPanel.Hide();
            return true;
        }
        if (choosingFirstTownSite)
        {
            CancelFirstTownSiteSelection();
            return true;
        }
        if (movingFounderId is not null)
        {
            ToggleMoveFounder();
            return true;
        }
        if (founderSetupPanel.Visible)
        {
            founderApiKeyInput.Text = string.Empty;
            founderSetupPanel.Hide();
            placingAddedAgent = false;
            return true;
        }
        foreach (var panel in new Control[] { conversationPanel, familyTreePanel, memoriesPanel, thoughtsPanel })
        {
            if (!panel.Visible) continue;
            panel.Hide();
            return true;
        }
        if (selectedTilePanel.Visible)
        {
            ClearTileSelection();
            return true;
        }
        var overlays = new Control[] { rosterPanel, eventsPanel, worldInfoPanel, filtersPanel, worldOverviewPanel };
        if (overlays.Any(panel => panel.Visible))
        {
            foreach (var panel in overlays) panel.Hide();
            return true;
        }
        if (buildingDetailsPanel.Visible)
        {
            BuildingDetailsBack();
            return true;
        }
        if (buildingQuickCard.Visible)
        {
            ClearBuildingSelection();
            return true;
        }
        if (agentProfilePanel.Visible)
        {
            AgentProfileBack();
            return true;
        }
        if (selectedInhabitantCard.Visible)
        {
            ClearInhabitantSelection();
            return true;
        }
        _ = ToggleGameMenuAsync();
        return true;
    }

    /// <summary>
    /// Shows the whole agent profile when it fits; on a short view the
    /// profile scrolls inside the card so Speak and Send stay reachable.
    /// </summary>
}
