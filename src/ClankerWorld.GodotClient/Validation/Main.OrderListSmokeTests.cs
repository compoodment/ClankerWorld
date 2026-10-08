using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Orders on the agent card at the sizes the game picks for a small, a
    /// 1080p and a 4K screen. The quick card and the Profile name the current
    /// order and why it is held up, each order state reads as the host reports
    /// it, and All orders lists every order the world keeps, beyond the four
    /// newest messages, with the agent's reply set apart from the game's status.
    /// </summary>
    private async Task VerifyOrderListAsync()
    {
        if (renderedMapSnapshot is not { } shown)
            throw new InvalidOperationException("The order list checks need a world on screen.");
        var ilya = PanelSmokeAgent("order-list-ilya", "Ilya", new OwnerWorldPosition(1, 1)) with
        {
            RecentPrivateThoughts = [new OwnerWorldPrivateThought(1, "I am too full to eat now.")],
        };
        OwnerWorldInstruction Order(int sequence, string text, string status, string action = "consume_food",
            int requested = 1, int completed = 0, string? reason = null, string? reply = null)
        {
            var open = status is "queued" or "waiting" or "doing" or "interrupted" or "blocked";
            return new OwnerWorldInstruction($"order-list-{sequence}", ilya.Id, "must_do", text, open ? "queued" : "completed",
                sequence, 0, sequence, reply is null ? null : sequence, reply,
                new OwnerWorldInstructionOrder(action, status, action == "unknown" ? 0 : requested, completed,
                    action == "unknown" ? "none" : "food_items", false, BlockedReason: reason));
        }
        var orders = new[]
        {
            Order(1, "Gather berries.", "cancelled", "harvest_food", reason: "Replaced by a newer order."),
            Order(2, "Eat the berries you carry.", "finished", completed: 1, reply: "That was good."),
            Order(3, "Biuld a house.", "not_understood", "unknown"),
            Order(4, "Eat three berries.", "blocked", requested: 3, reason: "Waiting until hungry enough to eat.",
                reply: "I am not hungry yet."),
            Order(5, "Go to the berries at 2, 1.", "queued", "seek_food"),
            Order(6, "Gather two berries.", "queued", "harvest_food", requested: 2),
            Order(7, "Go to the berry patch.", "queued", "seek_food"),
            Order(8, "Eat the fruit you carry.", "queued"),
        };
        var snapshot = shown with
        {
            Inhabitants = [ilya],
            Instructions = [.. orders, new OwnerWorldInstruction("order-list-suggestion", ilya.Id, "suggestive",
                "Rest by the fire tonight.", "queued", 9, 0, 9)],
        };
        var displayWindow = GetWindow();
        var originalWindowSize = displayWindow.Size;
        var originalRenderSize = displayWindow.ContentScaleSize;
        var originalScaleMode = displayWindow.ContentScaleMode;
        var originalScaleAspect = displayWindow.ContentScaleAspect;
        var originalZoom = cameraZoom;
        try
        {
            displayWindow.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            displayWindow.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            foreach (var (width, height, factor) in new[] { (1280, 720, 1), (1920, 1080, 2), (3840, 2160, 3) })
            {
                var size = $"{width}×{height} at {factor * 100}%";
                displayWindow.Size = new Vector2I(width, height);
                displayWindow.ContentScaleSize = new Vector2I(width, height);
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (uiLayer.Factor != factor)
                    throw new InvalidOperationException($"The order checks expected the interface the game picks for {size}, not {uiLayer.Factor * 100}%.");
                RenderMap(snapshot);
                selectedInhabitantId = ilya.Id;
                ordersPanel.Hide();
                // The Profile's width with no orders, so the order line and All orders can be checked not to widen it.
                agentProfileRequested = true;
                RenderSelectedInhabitantCard(snapshot with { Instructions = [] });
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                PositionAgentProfile();
                var profileWidth = agentProfilePanel.GetGlobalRect().Size.X;
                agentProfileRequested = false;
                RenderSelectedInhabitantCard(snapshot);
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                PositionSelectedInhabitantCard(snapshot);

                // The quick card names the order they are on now, why it is held up and what waits behind it.
                const string current = "Order: Blocked · Eating food · 0/3 food items · Waiting until hungry enough to eat.\n4 more orders queued";
                var card = selectedInhabitantCard.GetGlobalRect();
                if (quickCardOrderLabel.Text != current || quickCardOrderLabel.ThemeTypeVariation != "WarningLabel" ||
                    !quickCardOrderLabel.IsVisibleInTree() || !card.Grow(1).Encloses(quickCardOrderLabel.GetGlobalRect()) ||
                    !mapCanvas.GetGlobalRect().Grow(1).Encloses(card) || card.Size.X > (QuickCardWidth + 1) * factor)
                    throw new InvalidOperationException($"The quick card must show the current order and why it is held up, inside the map, at {size}: {quickCardOrderLabel.Text} card={card}.");

                quickCardProfileButton.EmitSignal(BaseButton.SignalName.Pressed);
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                PositionAgentProfile();
                var profile = agentProfilePanel.GetGlobalRect();
                agentOverviewScroll.EnsureControlVisible(allOrdersButton);
                for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                // A Profile taller than the screen scrolls, and its scroll bar and gap add the same width they always do.
                var scrollBar = agentOverviewScroll.GetVScrollBar();
                var scrollWidth = scrollBar.Visible ? scrollBar.GetGlobalRect().Size.X + SettingsScrollGap * factor : 0;
                if (profileOrderLabel.Text != current || !profileOrderLabel.IsVisibleInTree() || !allOrdersButton.IsVisibleInTree() ||
                    !GetViewportRect().Grow(1).Encloses(profile) || profile.Size.X > profileWidth + scrollWidth + factor ||
                    !profile.Grow(1).Encloses(allOrdersButton.GetGlobalRect()))
                    throw new InvalidOperationException($"The Profile must show the current order and an All orders button without widening, at {size}: {profileOrderLabel.Text} profile={profile} width without orders={profileWidth} scroll bar and gap={scrollWidth} button={allOrdersButton.GetGlobalRect()}.");

                allOrdersButton.GrabFocus();
                if (!allOrdersButton.HasFocus())
                    throw new InvalidOperationException("All orders must be reachable by keyboard in the Profile.");
                allOrdersButton.EmitSignal(BaseButton.SignalName.Pressed);
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var reader = ordersPanel.GetGlobalRect();
                var text = ordersReaderText.GetParsedText();
                int At(string part) => text.IndexOf(part, StringComparison.Ordinal);
                // Beyond the four newest messages: all eight orders, now first, then the queue in turn, then the closed ones newest first.
                var expected = new[]
                {
                    "Now", "Blocked · Eating food · 0/3 food items · Waiting until hungry enough to eat. · Heard by their personal model",
                    "You said: “Eat three berries.”", "Ilya replied: “I am not hungry yet.”",
                    "Queued", "You said: “Go to the berries at 2, 1.”", "You said: “Gather two berries.”",
                    "You said: “Go to the berry patch.”", "You said: “Eat the fruit you carry.”",
                    "Earlier", "Not understood\nYou said: “Biuld a house.”",
                    "Finished · Eating food · 1/1 food items · Heard by their personal model",
                    "Ilya replied: “That was good.”",
                    "Cancelled · Gathering food · Replaced by a newer order.\nYou said: “Gather berries.”",
                };
                var positions = expected.Select(At).ToArray();
                if (!ordersPanel.Visible || ordersReaderTitle.Text != "Ilya · orders" || positions.Any(position => position < 0) ||
                    !positions.SequenceEqual(positions.Order()) || text.Split("You said:").Length - 1 != orders.Length ||
                    text.Contains("Rest by the fire", StringComparison.Ordinal) ||
                    privateThoughtHistory.Text.Contains("not hungry yet", StringComparison.Ordinal))
                    throw new InvalidOperationException($"All orders must list every order the world keeps, in turn, with replies apart from status and thoughts, at {size}: {text}");
                if (!GetViewportRect().Grow(1).Encloses(reader) || ordersPanel.Position.Y < HudTop - 1 ||
                    reader.Position.X < profile.End.X && reader.Intersects(profile))
                    throw new InvalidOperationException($"All orders must open on screen beside the Profile or in the middle, at {size}: reader={reader} profile={profile}.");

                // The next snapshot, as after a reconnect or reload, replaces what the open reader and card show:
                // the host finished the eating order, so the first queued order is now current.
                var advanced = snapshot with
                {
                    Instructions = [.. snapshot.Instructions.Select(item => item.InstructionId switch
                    {
                        "order-list-4" => Order(4, "Eat three berries.", "finished", requested: 3, completed: 3, reply: "I am not hungry yet."),
                        "order-list-5" => Order(5, "Go to the berries at 2, 1.", "waiting", "seek_food"),
                        _ => item,
                    })],
                };
                RenderSelectedInhabitantCard(advanced);
                text = ordersReaderText.GetParsedText();
                var nowAt = At("Now\nWaiting · Going to a food site\nYou said: “Go to the berries at 2, 1.”");
                var finishedAt = At("Finished · Eating food · 3/3 food items · Heard by their personal model\nYou said: “Eat three berries.”");
                if (profileOrderLabel.Text != "Order: Waiting · Going to a food site\n3 more orders queued" || nowAt < 0 ||
                    finishedAt < At("Earlier") || At("Earlier") < At("Queued") || At("Queued") < nowAt ||
                    text.Contains("Blocked", StringComparison.Ordinal) || text.Split("You said:").Length - 1 != orders.Length)
                    throw new InvalidOperationException($"All orders and the order line must follow the host's next snapshot, at {size}: {profileOrderLabel.Text}\n{text}");
                RenderSelectedInhabitantCard(snapshot);
                _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                if (ordersPanel.Visible || !agentProfilePanel.Visible)
                    throw new InvalidOperationException("Escape must close All orders before the Profile.");
            }

            // Each state reads as the host reports it; completion shows only once the order finished.
            displayWindow.Size = new Vector2I(1280, 720);
            displayWindow.ContentScaleSize = new Vector2I(1280, 720);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            agentProfileRequested = false;
            foreach (var (status, reason, line) in new (string, string?, string)[]
            {
                ("waiting", null, "Order: Waiting · Eating food"),
                ("doing", null, "Order: Doing · Eating food · 1/3 food items"),
                ("interrupted", "Food or warmth needs come first.", "Order: Interrupted · Eating food · 1/3 food items · Food or warmth needs come first."),
                ("blocked", "No matching food is available to eat right now.", "Order: Blocked · Eating food · 1/3 food items · No matching food is available to eat right now."),
                ("finished", null, "Last order: Finished · Eating food · 3/3 food items"),
                ("cancelled", null, "Last order: Cancelled · Eating food"),
                ("not_understood", null, "Last order: Not understood"),
            })
            {
                var order = status == "not_understood"
                    ? Order(1, "Biuld a house.", status, "unknown")
                    : Order(1, "Eat three berries.", status, requested: 3, completed: status == "finished" ? 3 : 1, reason: reason);
                RenderSelectedInhabitantCard(snapshot with { Instructions = [order] });
                var style = status is "blocked" or "interrupted" ? "WarningLabel" : line.StartsWith("Last", StringComparison.Ordinal) ? "DimLabel" : "SoftLabel";
                if (quickCardOrderLabel.Text != line || quickCardOrderLabel.ThemeTypeVariation != style || !quickCardOrderLabel.Visible)
                    throw new InvalidOperationException($"A {status} order must read \"{line}\" on the quick card: {quickCardOrderLabel.Text}");
            }
            // Guardian orders now share the same host list, and need a useful task name too.
            var guardian = Order(1, "Become guardian for Lina.", "blocked", "accept_guardianship",
                requested: 1, reason: "Waiting to be asked by the child's guardian search.");
            guardian = guardian with { Order = guardian.Order! with { ProgressUnit = "guardianships" } };
            RenderSelectedInhabitantCard(snapshot with { Instructions = [guardian] });
            if (quickCardOrderLabel.Text != "Order: Blocked · Becoming a guardian · 0/1 care assignments · Waiting to be asked by the child's guardian search.")
                throw new InvalidOperationException($"Guardian orders need their own task name and progress wording: {quickCardOrderLabel.Text}");
            // So do orders to walk to an exact tile.
            var movement = Order(1, "Move to tile (12, 4).", "blocked", "move_to",
                requested: 1, reason: "No open walking route reaches the requested tile right now.");
            movement = movement with { Order = movement.Order! with { ProgressUnit = "arrivals", TargetX = 12, TargetY = 4 } };
            RenderSelectedInhabitantCard(snapshot with { Instructions = [movement] });
            if (quickCardOrderLabel.Text != "Order: Blocked · Going to a tile · 0/1 sites reached · No open walking route reaches the requested tile right now.")
                throw new InvalidOperationException($"Movement orders need their own task name: {quickCardOrderLabel.Text}");
            var boatOrder = movement with { Order = movement.Order! with { Action = "travel_by_boat",
                BlockedReason = "Waiting for dock space at the destination Port." } };
            RenderSelectedInhabitantCard(snapshot with { Instructions = [boatOrder] });
            if (quickCardOrderLabel.Text != "Order: Blocked · Traveling to a Port by boat · 0/1 sites reached · Waiting for dock space at the destination Port.")
                throw new InvalidOperationException($"Boat orders need their own task and waiting reason: {quickCardOrderLabel.Text}");
            RenderSelectedInhabitantCard(snapshot with { Instructions = [] });
            if (quickCardOrderLabel.Visible || allOrdersButton.Visible)
                throw new InvalidOperationException("An agent with no orders must show no order line and no All orders button.");
        }
        finally
        {
            ordersPanel.Hide();
            agentProfileRequested = false;
            selectedInhabitantId = null;
            displayWindow.Size = originalWindowSize;
            displayWindow.ContentScaleMode = originalScaleMode;
            displayWindow.ContentScaleAspect = originalScaleAspect;
            displayWindow.ContentScaleSize = originalRenderSize;
            ApplyUiScale();
            cameraZoom = originalZoom;
            RenderSelectedInhabitantCard(shown);
            RenderMap(shown);
        }
    }
}
