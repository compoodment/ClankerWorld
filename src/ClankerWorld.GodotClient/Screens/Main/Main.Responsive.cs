using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>The area the interface lays out in, in unscaled interface pixels.</summary>
    private Vector2 UiSize => uiLayer.Size;

    private void ApplyResponsiveLayout()
    {
        var viewport = UiSize;
        if (viewport.X <= 0 || viewport.Y <= 0)
        {
            return;
        }

        climateBox.Visible = viewport.X >= 1100 && renderedMapSnapshot?.Authoring is not null;
        CompactHud(viewport.X < 1280);
        float panelWidth(int width) => Math.Min(width, Math.Max(1, viewport.X - 28));
        rosterPanel.CustomMinimumSize = new Vector2(panelWidth(410), 0);
        eventsPanel.CustomMinimumSize = new Vector2(panelWidth(390), 0);
        worldInfoPanel.CustomMinimumSize = new Vector2(panelWidth(420), 0);
        selectedTilePanel.CustomMinimumSize = new Vector2(panelWidth(315), 0);
        filtersPanel.CustomMinimumSize = new Vector2(panelWidth(305), 0);
        // Size the shared caption column from its widest caption at the
        // current font size, so no single long caption pushes its choice out.
        var captionWidth = settingCaptionLabels.Aggregate((float)SettingCaptionWidth,
            (widest, caption) => Math.Max(widest, caption.GetMinimumSize().X));
        foreach (var caption in settingCaptionLabels)
            caption.CustomMinimumSize = new Vector2(captionWidth, 0);
        mainMenuCard.CustomMinimumSize = new Vector2(panelWidth(440), 0);
        LayoutWorldMenu();
        manualSaveCard.CustomMinimumSize = new Vector2(panelWidth(470), 0);

        if (observationSession.Current?.Baseline.Snapshot is { } snapshot && HasMap(snapshot))
        {
            var previousTileSize = currentTileSize;
            UpdateMapGeometry(snapshot);
            if (previousTileSize != currentTileSize)
            {
                RenderMap(snapshot);
            }
            else
            {
                PositionSelectedInhabitantCard(snapshot);
                PositionBuildingQuickCard(snapshot);
            }
        }

        if (controlsPanel.Visible) PositionControlsPanel();
        PositionDeveloperTools();
        PositionAgentProfile();
        PositionBuildingDetails();
        PositionMapHud();
        // Panels open just below the floating HUD, under the button that opened them.
        var hudTop = HudTop;
        PlaceHudPanels();
        PositionSelectedTilePanel();
        PositionFamilyTreePanel(viewport, hudTop);
        FitHudLists();
        foreach (var reader in new[] { memoriesPanel, thoughtsPanel, ordersPanel, conversationPanel })
        {
            reader.CustomMinimumSize = new Vector2(Math.Clamp(viewport.X - 28, 320, ReaderWidth), 0);
            if (reader == memoriesPanel && reader.Visible) FitMemoryCards();
            PlaceReaderPanel(reader);
        }

        var menuWidth = panelWidth(560);
        gameMenuPanel.CustomMinimumSize = new Vector2(menuWidth, 0);
        FitMenuScrolls(viewport.Y);

        var toastSize = statusToast.GetCombinedMinimumSize();
        saveDiskWarningPanel.Position = new Vector2(
            Math.Max(14, (viewport.X - saveDiskWarningPanel.GetCombinedMinimumSize().X) / 2), 58);
        statusToast.Position = new Vector2(
            Math.Max(14, (viewport.X - toastSize.X) / 2),
            Math.Max(14, viewport.Y - toastSize.Y - 18));
    }

    private void PositionFamilyTreePanel(Vector2 viewport, float hudTop)
    {
        familyTreePanel.CustomMinimumSize = Vector2.Zero;
        familyTreeScroll.CustomMinimumSize = Vector2.Zero;

        // Measure the panel with the whole tree in view, then cap it to the
        // screen. Long or wide trees scroll inside this bounded area instead
        // of making the panel run off-screen.
        var tree = familyTreeView.GetCombinedMinimumSize();
        familyTreeScroll.CustomMinimumSize = new Vector2(Math.Max(0, tree.X), 0);
        var wanted = familyTreePanel.GetCombinedMinimumSize();
        var topLimit = Math.Min(Math.Max(14, hudTop), Math.Max(14, viewport.Y - 14));
        var availableHeight = Math.Max(1, viewport.Y - topLimit - 14);
        var panelWidth = Math.Max(1, Math.Min(Math.Max(320, wanted.X), viewport.X - 28));
        // The scroll area is measured at the tree's width but no height, so
        // everything else in the panel is the width beside it and the height above it.
        var chromeWidth = Math.Max(0, wanted.X - tree.X);
        var chromeHeight = wanted.Y;
        familyTreeScroll.CustomMinimumSize = new Vector2(
            Math.Max(0, Math.Min(tree.X, panelWidth - chromeWidth)),
            Math.Min(Math.Max(0, tree.Y), Math.Max(0, availableHeight - chromeHeight)));
        familyTreePanel.CustomMinimumSize = new Vector2(panelWidth, 0);

        var panelHeight = Math.Min(availableHeight, familyTreePanel.GetCombinedMinimumSize().Y);
        familyTreePanel.Size = new Vector2(panelWidth, panelHeight);
        // Beside the Profile it was opened from when there is room, so both stay readable.
        var beside = agentProfilePanel.Position.X + agentProfilePanel.Size.X + 12;
        if (agentProfilePanel.Visible && beside + panelWidth <= viewport.X - 14)
        {
            familyTreePanel.Position = new Vector2(beside, topLimit);
            return;
        }
        var maximumY = Math.Max(14, viewport.Y - panelHeight - 14);
        var minimumY = Math.Min(topLimit, maximumY);
        var centeredY = (viewport.Y - panelHeight) / 2;
        familyTreePanel.Position = new Vector2((viewport.X - panelWidth) / 2,
            Math.Clamp(centeredY, minimumY, maximumY));
    }

    private void PositionSelectedInhabitantCard(OwnerWorldSnapshot snapshot)
    {
        var ui = UiSize;
        if (!selectedInhabitantCard.Visible || ui.X <= 0 || ui.Y <= 0)
        {
            return;
        }

        var inhabitant = snapshot.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (inhabitant is null)
        {
            return;
        }

        selectedInhabitantCard.CustomMinimumSize = new Vector2(Math.Min(QuickCardWidth, Math.Max(1, ui.X - 24)), 0);
        // The map is drawn at screen resolution; the card lives in interface pixels.
        var stride = currentTileSize + TileGap;
        var tile = currentTileSize / (float)uiLayer.Factor;
        var target = inhabitantVisuals.TryGetValue(inhabitant.Id, out var marker) && marker.Visible
            ? new Rect2((mapStage.Position + marker.Position) / uiLayer.Factor, marker.Size / uiLayer.Factor)
            : new Rect2((mapStage.Position + new Vector2(inhabitant.Position.X * stride, inhabitant.Position.Y * stride)) / uiLayer.Factor,
                new Vector2(tile, tile));
        PlaceQuickCard(selectedInhabitantCard, target);
    }

    /// <summary>
    /// Places a quick card above what it describes, or below it when there is
    /// no room above, keeping clear of open top-bar panels. A tall card on a
    /// short screen moves beside its target rather than covering it.
    /// <paramref name="target"/> is in interface pixels.
    /// </summary>
    private void PlaceQuickCard(PanelContainer card, Rect2 target)
    {
        var ui = UiSize;
        var cardSize = card.GetCombinedMinimumSize();
        var cardWidth = cardSize.X;
        card.Size = cardSize;
        var center = target.GetCenter();
        // Keep clear of open top-bar panels, such as the Event Log, when there is room beside them.
        var (left, right) = (12f, ui.X - 12);
        foreach (var panel in HudPanels().Where(panel => panel.Visible))
        {
            if (panel.Position.X + panel.Size.X / 2 < ui.X / 2) left = Math.Max(left, panel.Position.X + panel.Size.X + 12);
            else right = Math.Min(right, panel.Position.X - 12);
        }
        if (right - left < cardWidth) (left, right) = (12f, ui.X - 12);
        var x = Math.Clamp(center.X - (cardWidth / 2), left, Math.Max(left, right - cardWidth));
        var y = target.Position.Y - cardSize.Y - 12;
        if (y < 12)
        {
            y = target.End.Y + 12;
        }

        y = Math.Clamp(y, CardTop(cardSize.Y), Math.Max(CardTop(cardSize.Y), ui.Y - cardSize.Y - 12));
        if (new Rect2(x, y, cardSize).Intersects(target))
        {
            var besideRight = target.End.X + 12;
            var besideLeft = target.Position.X - cardWidth - 12;
            if (besideRight + cardWidth <= right) x = besideRight;
            else if (besideLeft >= left) x = besideLeft;
            y = Math.Clamp(center.Y - cardSize.Y / 2, CardTop(cardSize.Y), Math.Max(CardTop(cardSize.Y), ui.Y - cardSize.Y - 12));
        }
        card.Position = new Vector2(x, y);
    }

    /// <summary>
    /// Settings scrolls inside the menu panel, as tall as its contents but
    /// never taller than the screen leaves room for.
    /// </summary>
    private void FitMenuScrolls(float viewportHeight)
    {
        // Game and World share one width, so switching between them does not resize the menu.
        settingsScroll.CustomMinimumSize = new Vector2(
            Math.Max(gameSettingsContent.GetCombinedMinimumSize().X, worldSettingsContent.GetCombinedMinimumSize().X) +
            SettingsScrollGap + settingsScroll.GetVScrollBar().GetCombinedMinimumSize().X, settingsScroll.CustomMinimumSize.Y);
        if (settingsScroll.IsVisibleInTree() && settingsScroll.GetChildCount() > 0 && settingsScroll.GetChild(0) is Control content)
        {
            settingsScroll.CustomMinimumSize = new Vector2(settingsScroll.CustomMinimumSize.X, 0);
            var around = gameMenuPanel.GetCombinedMinimumSize().Y;
            var height = Math.Clamp(content.GetCombinedMinimumSize().Y, 120, Math.Max(120, viewportHeight - 28 - around));
            settingsScroll.CustomMinimumSize = new Vector2(settingsScroll.CustomMinimumSize.X, height);
        }
        gameMenuPanel.Size = gameMenuPanel.GetCombinedMinimumSize();
    }

    /// <summary>The agent card sits below the HUD when it fits, and slides up over it only when it is taller than the room left.</summary>
    private float CardTop(float cardHeight) => Math.Max(12, Math.Min(HudTop, UiSize.Y - cardHeight - 12));

}
