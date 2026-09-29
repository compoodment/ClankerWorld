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
        familyTreeStatus.Text = familyTreeView.ParentEdgeCount + familyTreeView.PartnerEdgeCount == 0
            ? "No family links recorded yet. Housemates are not automatically relatives."
            : "Green: parent–child   ·   Pink: partnership   ·   Click a person to inspect";
    }

    private void SelectFromFamilyTree(string id)
    {
        familyTreePanel.Hide();
        selectedInhabitantId = id;
        if (observationSession.Current is not { } current) return;
        var snapshot = current.Baseline.Snapshot;
        RenderInhabitantList(snapshot);
        RenderInhabitantDetails(snapshot);
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
        returnToMainMenu = false;
        menuResumeButton.Text = "Resume";
        SetWorldMenuActionsVisible(true);
        menuHeadingLabel.Text = "Paused";
        worldInfoPanel.Hide();
        gameMenuPanel.Show();
        menuShade.Show();
        ApplyResponsiveLayout();

        var paused = observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused == true;
        menuPausedWorld = observationSession.Current is not null && !paused;
        menuPauseConfirmed = paused;
        if (menuPausedWorld)
        {
            menuPauseConfirmed = await SetPausedAsync(paused: true);
        }
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
        menuPausedWorld = false;
        menuPauseConfirmed = false;
        menuCloseButton.Text = "×";
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
            _ = CloseGameMenuAsync();
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
        foreach (var panel in new Control[] { familyTreePanel, memoriesPanel })
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
        if (selectedAgentModelScroll.Visible)
        {
            CloseAgentModelEditor();
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
