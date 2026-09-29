using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void ApplyResponsiveLayout()
    {
        var viewport = mapCanvas.Size;
        if (viewport.X <= 0 || viewport.Y <= 0)
        {
            return;
        }

        climateBox.Visible = Size.X >= 1100 && renderedMapSnapshot?.Authoring is not null;
        var uiScale = DisplayUiScalePolicy.ScaleFactor(displayPreferences.UiScalePercent);
        float panelWidth(int width) => Math.Min(width * uiScale, Math.Max(1, viewport.X - 28));
        rosterPanel.CustomMinimumSize = new Vector2(panelWidth(410), 0);
        eventsPanel.CustomMinimumSize = new Vector2(panelWidth(390), 360);
        worldInfoPanel.CustomMinimumSize = new Vector2(panelWidth(420), 380);
        selectedTilePanel.CustomMinimumSize = new Vector2(panelWidth(315), 0);
        filtersPanel.CustomMinimumSize = new Vector2(panelWidth(305), 0);
        // Size the shared caption column from its widest caption at the
        // current font size, so no single long caption pushes its choice out.
        var captionWidth = settingCaptionLabels.Aggregate(SettingCaptionWidth * uiScale,
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
            }
        }

        if (controlsPanel.Visible) PositionControlsPanel();
        PositionMapHud();
        // Panels open just below the floating HUD rather than under it.
        var hudTop = HudTop;
        rosterPanel.Position = new Vector2(14, hudTop);
        worldInfoPanel.Position = new Vector2(14, hudTop);
        worldOverviewPanel.Position = new Vector2(14, hudTop);
        founderSetupPanel.Position = new Vector2(founderSetupPanel.Position.X, Math.Max(founderSetupPanel.Position.Y, hudTop));
        filtersPanel.Position = new Vector2(
            Math.Max(14, viewport.X - Math.Max(filtersPanel.Size.X, filtersPanel.CustomMinimumSize.X) - 14),
            hudTop);
        PositionSelectedTilePanel();
        eventsPanel.Position = new Vector2(
            Math.Max(14, viewport.X - Math.Max(eventsPanel.Size.X, eventsPanel.CustomMinimumSize.X) - 14),
            hudTop);
        var familySize = new Vector2(Math.Clamp(viewport.X - 28, 320, 840 * uiScale),
            Math.Clamp(viewport.Y - 28, 280, 600));
        familyTreePanel.Size = familySize;
        familyTreePanel.Position = new Vector2(
            Math.Max(14, (viewport.X - familySize.X) / 2),
            Math.Max(hudTop, (viewport.Y - familySize.Y) / 2));
        var memoriesSize = new Vector2(Math.Clamp(viewport.X - 28, 320, 600 * uiScale),
            Math.Clamp(viewport.Y - 28, 280, 430));
        memoriesPanel.Size = memoriesSize;
        memoriesPanel.Position = new Vector2(
            Math.Max(14, (viewport.X - memoriesSize.X) / 2),
            Math.Max(hudTop, (viewport.Y - memoriesSize.Y) / 2));

        var menuWidth = panelWidth(560);
        gameMenuPanel.CustomMinimumSize = new Vector2(menuWidth, 0);

        var toastSize = statusToast.GetCombinedMinimumSize();
        statusToast.Position = new Vector2(
            Math.Max(14, (viewport.X - toastSize.X) / 2),
            Math.Max(14, viewport.Y - toastSize.Y - 18));
    }

    private void FitSelectedCardHeight()
    {
        var profileHeight = selectedAgentOverview.GetCombinedMinimumSize().Y;
        selectedAgentOverviewScroll.CustomMinimumSize = new Vector2(0, profileHeight);
        var excess = selectedInhabitantCard.GetCombinedMinimumSize().Y - (mapCanvas.Size.Y - 24);
        if (excess > 0)
            selectedAgentOverviewScroll.CustomMinimumSize = new Vector2(0, Math.Max(120, profileHeight - excess));
    }

    private void PositionSelectedInhabitantCard(OwnerWorldSnapshot snapshot)
    {
        if (!selectedInhabitantCard.Visible || mapCanvas.Size.X <= 0 || mapCanvas.Size.Y <= 0)
        {
            return;
        }

        var inhabitant = snapshot.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (inhabitant is null)
        {
            return;
        }

        var cardWidth = Math.Min(370 * DisplayUiScalePolicy.ScaleFactor(displayPreferences.UiScalePercent),
            Math.Max(300, mapCanvas.Size.X - 24));
        selectedInhabitantCard.CustomMinimumSize = new Vector2(cardWidth, 0);
        FitSelectedCardHeight();
        var cardSize = selectedInhabitantCard.GetCombinedMinimumSize();
        selectedInhabitantCard.Size = cardSize;
        if (string.Equals(inhabitant.Lifecycle, "dead", StringComparison.OrdinalIgnoreCase))
        {
            selectedInhabitantCard.Position = new Vector2(Math.Max(12, mapCanvas.Size.X - cardWidth - 12), CardTop(cardSize.Y));
            return;
        }
        var stride = currentTileSize + TileGap;
        var actorCenter = mapStage.Position + new Vector2(
            (inhabitant.Position.X * stride) + (currentTileSize / 2f),
            (inhabitant.Position.Y * stride) + (currentTileSize / 2f));
        var x = Math.Clamp(actorCenter.X - (cardWidth / 2), 12, Math.Max(12, mapCanvas.Size.X - cardWidth - 12));
        var y = actorCenter.Y - (currentTileSize / 2f) - cardSize.Y - 12;
        if (y < 12)
        {
            y = actorCenter.Y + (currentTileSize / 2f) + 12;
        }

        y = Math.Clamp(y, CardTop(cardSize.Y), Math.Max(CardTop(cardSize.Y), mapCanvas.Size.Y - cardSize.Y - 12));
        // A tall card on a short screen cannot fit above or below the agent,
        // so it moves beside them rather than covering the person it describes.
        var actorRect = new Rect2(actorCenter - new Vector2(currentTileSize, currentTileSize) / 2,
            new Vector2(currentTileSize, currentTileSize));
        if (new Rect2(x, y, cardSize).Intersects(actorRect))
        {
            var right = actorRect.End.X + 12;
            var left = actorRect.Position.X - cardWidth - 12;
            if (right + cardWidth <= mapCanvas.Size.X - 12) x = right;
            else if (left >= 12) x = left;
            y = Math.Clamp(actorCenter.Y - cardSize.Y / 2, CardTop(cardSize.Y), Math.Max(CardTop(cardSize.Y), mapCanvas.Size.Y - cardSize.Y - 12));
        }
        selectedInhabitantCard.Position = new Vector2(x, y);
    }

    /// <summary>The agent card sits below the HUD when it fits, and slides up over it only when it is taller than the room left.</summary>
    private float CardTop(float cardHeight) => Math.Max(12, Math.Min(HudTop, mapCanvas.Size.Y - cardHeight - 12));

}
