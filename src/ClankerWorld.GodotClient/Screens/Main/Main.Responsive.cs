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
            }
        }

        if (controlsPanel.Visible) PositionControlsPanel();
        PositionMapHud();
        // Panels open just below the floating HUD, under the button that opened them.
        var hudTop = HudTop;
        PlaceHudPanels();
        PositionSelectedTilePanel();
        var familySize = new Vector2(Math.Clamp(viewport.X - 28, 320, 840),
            Math.Clamp(viewport.Y - 28, 280, 600));
        familyTreePanel.Size = familySize;
        familyTreePanel.Position = new Vector2(
            Math.Max(14, (viewport.X - familySize.X) / 2),
            Math.Max(hudTop, (viewport.Y - familySize.Y) / 2));
        memoriesPanel.CustomMinimumSize = new Vector2(Math.Clamp(viewport.X - 28, 320, 600), 0);
        CenterMemoriesPanel();

        var menuWidth = panelWidth(560);
        gameMenuPanel.CustomMinimumSize = new Vector2(menuWidth, 0);

        var toastSize = statusToast.GetCombinedMinimumSize();
        statusToast.Position = new Vector2(
            Math.Max(14, (viewport.X - toastSize.X) / 2),
            Math.Max(14, viewport.Y - toastSize.Y - 18));
    }

    /// <summary>Memories fit their text and sit in the middle of the screen, below the top bar.</summary>
    private void CenterMemoriesPanel()
    {
        var size = memoriesPanel.GetCombinedMinimumSize();
        memoriesPanel.Size = size;
        memoriesPanel.Position = new Vector2(
            Math.Max(14, (UiSize.X - size.X) / 2),
            Math.Max(HudTop, (UiSize.Y - size.Y) / 2));
    }

    /// <summary>The card's profile scrolls when the whole card would not fit below the top bar.</summary>
    private void FitSelectedCardHeight()
    {
        var profileHeight = selectedAgentOverview.GetCombinedMinimumSize().Y;
        selectedAgentOverviewScroll.CustomMinimumSize = new Vector2(0, profileHeight);
        var excess = selectedInhabitantCard.GetCombinedMinimumSize().Y - (UiSize.Y - HudTop - 12);
        if (excess > 0)
            selectedAgentOverviewScroll.CustomMinimumSize = new Vector2(0, Math.Max(120, profileHeight - excess));
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

        var cardWidth = Math.Min(370, Math.Max(300, ui.X - 24));
        selectedInhabitantCard.CustomMinimumSize = new Vector2(cardWidth, 0);
        FitSelectedCardHeight();
        var cardSize = selectedInhabitantCard.GetCombinedMinimumSize();
        selectedInhabitantCard.Size = cardSize;
        if (string.Equals(inhabitant.Lifecycle, "dead", StringComparison.OrdinalIgnoreCase))
        {
            selectedInhabitantCard.Position = new Vector2(Math.Max(12, ui.X - cardWidth - 12), CardTop(cardSize.Y));
            return;
        }
        // The map is drawn at screen resolution; the card lives in interface pixels.
        var stride = currentTileSize + TileGap;
        var tile = currentTileSize / (float)uiLayer.Factor;
        var actorCenter = (mapStage.Position + new Vector2(
            (inhabitant.Position.X * stride) + (currentTileSize / 2f),
            (inhabitant.Position.Y * stride) + (currentTileSize / 2f))) / uiLayer.Factor;
        // Keep clear of open top-bar panels, such as the Event Log, when there is room beside them.
        var (left, right) = (12f, ui.X - 12);
        foreach (var panel in HudPanels().Where(panel => panel.Visible))
        {
            if (panel.Position.X + panel.Size.X / 2 < ui.X / 2) left = Math.Max(left, panel.Position.X + panel.Size.X + 12);
            else right = Math.Min(right, panel.Position.X - 12);
        }
        if (right - left < cardWidth) (left, right) = (12f, ui.X - 12);
        var x = Math.Clamp(actorCenter.X - (cardWidth / 2), left, Math.Max(left, right - cardWidth));
        var y = actorCenter.Y - (tile / 2f) - cardSize.Y - 12;
        if (y < 12)
        {
            y = actorCenter.Y + (tile / 2f) + 12;
        }

        y = Math.Clamp(y, CardTop(cardSize.Y), Math.Max(CardTop(cardSize.Y), ui.Y - cardSize.Y - 12));
        // A tall card on a short screen cannot fit above or below the agent,
        // so it moves beside them rather than covering the person it describes.
        var actorRect = new Rect2(actorCenter - new Vector2(tile, tile) / 2, new Vector2(tile, tile));
        if (new Rect2(x, y, cardSize).Intersects(actorRect))
        {
            var besideRight = actorRect.End.X + 12;
            var besideLeft = actorRect.Position.X - cardWidth - 12;
            if (besideRight + cardWidth <= right) x = besideRight;
            else if (besideLeft >= left) x = besideLeft;
            y = Math.Clamp(actorCenter.Y - cardSize.Y / 2, CardTop(cardSize.Y), Math.Max(CardTop(cardSize.Y), ui.Y - cardSize.Y - 12));
        }
        selectedInhabitantCard.Position = new Vector2(x, y);
    }

    /// <summary>The agent card sits below the HUD when it fits, and slides up over it only when it is taller than the room left.</summary>
    private float CardTop(float cardHeight) => Math.Max(12, Math.Min(HudTop, UiSize.Y - cardHeight - 12));

}
