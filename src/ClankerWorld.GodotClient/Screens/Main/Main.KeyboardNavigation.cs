using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private bool keyboardNavigation;
    private Key keyboardNavigationKey;
    private readonly List<(Control Panel, Control? Opener)> keyboardPanels = [];

    private void BuildKeyboardNavigation()
    {
        foreach (var scroll in FindChildren("*", nameof(ScrollContainer), recursive: true, owned: false).OfType<ScrollContainer>())
        {
            scroll.FocusMode = FocusModeEnum.All;
            foreach (var bar in new ScrollBar[] { scroll.GetVScrollBar(), scroll.GetHScrollBar() })
                ConfigureKeyboardScrollBar(bar);
        }
        foreach (var panel in new Control[]
        {
            mainMenuOverlay, gameMenuPanel, settingsPanel, modLibraryPanel, worldMenuOverlay, manualSaveOverlay,
            controlsPanel, founderSetupPanel, rosterPanel, eventsPanel, worldInfoPanel, filtersPanel, worldOverviewPanel,
            selectedInhabitantCard, agentProfilePanel, familyTreePanel, memoriesPanel, thoughtsPanel, ordersPanel,
            conversationPanel, selectedTilePanel, buildingQuickCard, buildingDetailsPanel, developerPanel,
        })
        {
            panel.VisibilityChanged += () =>
            {
                var old = keyboardPanels.FindLast(item => item.Panel == panel);
                keyboardPanels.RemoveAll(item => item.Panel == panel);
                if (panel.IsVisibleInTree())
                {
                    keyboardPanels.Add((panel, GetViewport().GuiGetFocusOwner()));
                    if (keyboardNavigation && keyboardNavigationKey is not (Key.Up or Key.Down or Key.Left or Key.Right))
                        Callable.From(() => FocusKeyboardPanel(panel)).CallDeferred();
                }
                else if (keyboardNavigation && old.Panel is not null)
                {
                    Callable.From(() =>
                    {
                        if (old.Opener is { } opener && IsKeyboardFocusable(opener)) opener.GrabFocus();
                        else FocusKeyboardPanel(CurrentKeyboardPanel());
                    }).CallDeferred();
                }
            };
        }
    }

    private static void ConfigureKeyboardScrollBar(ScrollBar bar)
    {
        bar.FocusMode = FocusModeEnum.All;
        // ScrollContainer uses Step=0 for smooth pointer scrolling.
        // Give arrows a separate increment without quantizing dragging.
        if (bar.Step <= 0 && bar.CustomStep <= 0) bar.CustomStep = 16;
        // Godot scrolls on arrows without consuming their focus navigation.
        // Keep the scrolling axis here; Tab still reaches the next control.
        if (bar is VScrollBar) bar.FocusNeighborTop = bar.FocusNeighborBottom = ".";
        else bar.FocusNeighborLeft = bar.FocusNeighborRight = ".";
    }

    private Control CurrentKeyboardPanel()
    {
        if (manualSaveOverlay.IsVisibleInTree()) return manualSaveOverlay;
        if (worldMenuOverlay.IsVisibleInTree()) return worldMenuOverlay;
        if (gameMenuPanel.IsVisibleInTree()) return gameMenuPanel;
        if (mainMenuOverlay.IsVisibleInTree()) return mainMenuOverlay;
        var focused = GetViewport().GuiGetFocusOwner();
        if (focused == mapCanvas) return this;
        var browsing = keyboardPanels.LastOrDefault(item => item.Panel.IsVisibleInTree() &&
            focused is not null && (focused == item.Panel || item.Panel.IsAncestorOf(focused))).Panel;
        if (browsing is not null) return browsing;
        return keyboardPanels.LastOrDefault(item => item.Panel.IsVisibleInTree()).Panel ?? this;
    }

    private static bool IsKeyboardFocusable(Control control) => IsInstanceValid(control) &&
        control.IsInsideTree() && control.IsVisibleInTree() && control.FocusMode == FocusModeEnum.All &&
        control is not BaseButton { Disabled: true };

    private static IEnumerable<Control> KeyboardDescendants(Node node)
    {
        // SpinBox's editable field and other built-in controls are internal
        // children. Native popup windows own a separate focus/keyboard flow.
        if (node is Window) yield break;
        if (node is Control control) yield return control;
        foreach (var child in node.GetChildren(includeInternal: true))
            foreach (var descendant in KeyboardDescendants(child)) yield return descendant;
    }

    private static Control[] KeyboardControls(Control panel) =>
        KeyboardDescendants(panel).Where(IsKeyboardFocusable).ToArray();

    private void FocusKeyboardPanel(Control panel)
    {
        if (!IsInstanceValid(panel) || !panel.IsVisibleInTree() ||
            GetViewport().GetEmbeddedSubwindows().Any(window => window.Visible)) return;
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus is not null && (focus == panel || panel.IsAncestorOf(focus))) return;
        // Lists take arrows immediately; all other controls remain available by Tab.
        var controls = KeyboardControls(panel);
        var first = controls.FirstOrDefault(control => control is UI.SlotList) ?? controls.FirstOrDefault();
        first?.GrabFocus();
    }

    private bool HandleKeyboardNavigationInput(InputEvent input)
    {
        if (input is InputEventMouseMotion motion && motion.Relative != Vector2.Zero && mapCanvas.HasFocus())
        {
            keyboardNavigation = false;
            mapCanvas.ReleaseFocus();
        }
        if (input is InputEventMouseButton { Pressed: true }) keyboardNavigation = false;
        if (input is not InputEventKey { Pressed: true } key) return false;
        keyboardNavigation = true;
        keyboardNavigationKey = key.Keycode;
        if (key.Keycode != Key.Tab || key.CtrlPressed || key.AltPressed || key.MetaPressed ||
            GetViewport().GetEmbeddedSubwindows().Any(window => window.Visible)) return false;
        var panel = CurrentKeyboardPanel();
        var controls = KeyboardControls(panel);
        if (controls.Length == 0) return false;
        var index = Array.IndexOf(controls, GetViewport().GuiGetFocusOwner());
        var next = index < 0 ? (key.ShiftPressed ? controls.Length - 1 : 0)
            : (index + (key.ShiftPressed ? -1 : 1) + controls.Length) % controls.Length;
        controls[next].GrabFocus();
        for (var parent = controls[next].GetParent(); parent is not null && parent != panel; parent = parent.GetParent())
            if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(controls[next]);
        GetViewport().SetInputAsHandled();
        return true;
    }
}
