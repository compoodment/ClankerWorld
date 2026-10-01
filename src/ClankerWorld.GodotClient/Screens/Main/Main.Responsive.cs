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
        PositionAgentProfile();
        PositionBuildingDetails();
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
        foreach (var reader in new[] { memoriesPanel, thoughtsPanel, conversationPanel })
        {
            reader.CustomMinimumSize = new Vector2(Math.Clamp(viewport.X - 28, 320, ReaderWidth), 0);
            PlaceReaderPanel(reader);
        }

        var menuWidth = panelWidth(560);
        gameMenuPanel.CustomMinimumSize = new Vector2(menuWidth, 0);
        FitMenuScrolls(viewport.Y);

        var toastSize = statusToast.GetCombinedMinimumSize();
        statusToast.Position = new Vector2(
            Math.Max(14, (viewport.X - toastSize.X) / 2),
            Math.Max(14, viewport.Y - toastSize.Y - 18));
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
        var actorCorner = (mapStage.Position + new Vector2(inhabitant.Position.X * stride, inhabitant.Position.Y * stride)) / uiLayer.Factor;
        PlaceQuickCard(selectedInhabitantCard, new Rect2(actorCorner, new Vector2(tile, tile)));
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
    /// Settings and Developer tools scroll inside the menu panel, as tall as
    /// their contents but never taller than the screen leaves room for.
    /// </summary>
    private void FitMenuScrolls(float viewportHeight)
    {
        // Game and World share one width, so switching between them does not resize the menu.
        settingsScroll.CustomMinimumSize = new Vector2(
            Math.Max(gameSettingsContent.GetCombinedMinimumSize().X, worldSettingsContent.GetCombinedMinimumSize().X) +
            SettingsScrollGap + settingsScroll.GetVScrollBar().GetCombinedMinimumSize().X, settingsScroll.CustomMinimumSize.Y);
        foreach (var scroll in new[] { settingsScroll, developerScroll })
        {
            if (!scroll.IsVisibleInTree() || scroll.GetChildCount() == 0 || scroll.GetChild(0) is not Control content) continue;
            scroll.CustomMinimumSize = new Vector2(scroll.CustomMinimumSize.X, 0);
            var around = gameMenuPanel.GetCombinedMinimumSize().Y;
            var height = Math.Clamp(content.GetCombinedMinimumSize().Y, 120, Math.Max(120, viewportHeight - 28 - around));
            scroll.CustomMinimumSize = new Vector2(scroll.CustomMinimumSize.X, height);
        }
        gameMenuPanel.Size = gameMenuPanel.GetCombinedMinimumSize();
    }

    /// <summary>The agent card sits below the HUD when it fits, and slides up over it only when it is taller than the room left.</summary>
    private float CardTop(float cardHeight) => Math.Max(12, Math.Min(HudTop, UiSize.Y - cardHeight - 12));

}
