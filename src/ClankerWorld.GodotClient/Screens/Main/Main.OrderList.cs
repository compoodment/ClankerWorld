using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The player's orders to the selected agent, read from the host's snapshot:
/// one line on the quick card and the Profile for the order they are on now,
/// and a reader beside the Profile with every order the world keeps for them.
/// The game keeps no order queue of its own, so after a reconnect or a reload
/// both show exactly what the host holds.
/// </summary>
public partial class Main
{
    private readonly Button allOrdersButton = new();
    private readonly PanelContainer ordersPanel = new();
    private readonly Label ordersReaderTitle = new();
    private readonly RichTextLabel ordersReaderText = new();
    private string? renderedOrdersReader;

    private static void ConfigureOrderLabel(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        // A label shows its tooltip only when it takes the pointer; Pass still lets clicks reach the card.
        label.MouseFilter = Control.MouseFilterEnum.Pass;
        label.TooltipText = "All orders, in the Profile, lists their current, queued and recent orders.";
        label.Hide();
    }

    private static bool IsOpenOrder(OwnerWorldInstruction instruction) =>
        instruction.Order is { Status: "queued" or "waiting" or "doing" or "interrupted" or "blocked" };

    /// <summary>
    /// Every order for the agent in three groups: the order they are on now,
    /// the orders queued after it in the order they will be done, then the
    /// closed ones the world still keeps, newest first.
    /// </summary>
    private static (OwnerWorldInstruction? Now, OwnerWorldInstruction[] Queued, OwnerWorldInstruction[] Earlier) OrdersFor(
        OwnerWorldSnapshot snapshot, string inhabitantId)
    {
        var orders = snapshot.Instructions
            .Where(item => item.TargetInhabitantId == inhabitantId && item.Kind == "must_do")
            .ToArray();
        var now = PendingOrderToCancel(snapshot, inhabitantId);
        var queued = orders.Where(item => IsOpenOrder(item) && item.InstructionId != now?.InstructionId)
            .OrderBy(item => item.SubmissionSequence).ToArray();
        var earlier = orders.Where(item => !IsOpenOrder(item) && item.InstructionId != now?.InstructionId)
            .OrderByDescending(item => item.SubmissionSequence).ToArray();
        return (now, queued, earlier);
    }

    /// <summary>
    /// The order line on the card: the order they are on now, with why it is
    /// held up and how many wait behind it; otherwise how their latest order
    /// ended. Completion shows only once the host reports the task finished.
    /// </summary>
    private static string? OrderCardLine(OwnerWorldSnapshot snapshot, string inhabitantId)
    {
        var (now, queued, earlier) = OrdersFor(snapshot, inhabitantId);
        if (now is not null)
            return $"Order: {InstructionOrderSummary(now, includeHeard: false)}" +
                (queued.Length == 0 ? string.Empty : $"\n{queued.Length} more {(queued.Length == 1 ? "order" : "orders")} queued");
        return earlier.Length == 0 ? null : $"Last order: {InstructionOrderSummary(earlier[0], includeHeard: false)}";
    }

    private void RenderOrderSummary(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant inhabitant, bool isDeceased)
    {
        var line = isDeceased ? null : OrderCardLine(snapshot, inhabitant.Id);
        var heldUp = PendingOrderToCancel(snapshot, inhabitant.Id)?.Order?.Status is "blocked" or "interrupted";
        var style = heldUp ? "WarningLabel" : line?.StartsWith("Last order:", StringComparison.Ordinal) == true ? "DimLabel" : "SoftLabel";
        foreach (var label in new[] { quickCardOrderLabel, profileOrderLabel })
        {
            label.Text = GameUiText.PlainEllipses(line ?? string.Empty);
            label.ThemeTypeVariation = style;
            label.Visible = line is not null;
        }
        allOrdersButton.Visible = !isDeceased && snapshot.Instructions.Any(item =>
            item.TargetInhabitantId == inhabitant.Id && item.Kind == "must_do");
    }

    private void BuildOrdersReader()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        var heading = new HBoxContainer();
        ordersReaderTitle.ThemeTypeVariation = "HeadingLabel";
        ordersReaderTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        heading.AddChild(ordersReaderTitle);
        var close = CloseButton("Close orders (Esc)");
        close.Pressed += () => ordersPanel.Hide();
        heading.AddChild(close);
        body.AddChild(heading);
        body.AddChild(new Label
        {
            Text = "What the world reports for each order. Their replies are their own words.",
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        ConfigureTextPanel(ordersReaderText, 1000);
        ordersReaderText.AddThemeConstantOverride("paragraph_separation", 4);
        body.AddChild(ordersReaderText);
        AddPanelContents(ordersPanel, body);
        ordersPanel.ZIndex = 85;
        ordersPanel.Resized += () => PlaceReaderPanel(ordersPanel);
        ordersPanel.Hide();
    }

    private void OpenOrdersReader()
    {
        if (SelectedInhabitant() is not { } inhabitant || agentCardSnapshot is not { } snapshot) return;
        memoriesPanel.Hide();
        familyTreePanel.Hide();
        thoughtsPanel.Hide();
        conversationPanel.Hide();
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        ordersPanel.Show();
        renderedOrdersReader = null;
        RenderOrdersReader(snapshot, inhabitant);
        ApplyResponsiveLayout();
    }

    /// <summary>
    /// Every order the world keeps for this agent under Now, Queued and
    /// Earlier headings. Each shows its state, the player's words and any
    /// reply, which is set apart as the agent's own speech.
    /// </summary>
    private void RenderOrdersReader(OwnerWorldSnapshot snapshot, OwnerWorldInhabitant inhabitant)
    {
        // Only an open reader follows the world; opening it draws it afresh.
        if (!ordersPanel.Visible) return;
        var (now, queued, earlier) = OrdersFor(snapshot, inhabitant.Id);
        var groups = new (string Heading, OwnerWorldInstruction[] Orders)[]
        {
            ("Now", now is null ? [] : [now]),
            ("Queued", queued),
            ("Earlier", earlier),
        };
        var signature = string.Join("\n", groups.SelectMany(group => group.Orders.Select(item =>
                $"{group.Heading}|{item.InstructionId}|{InstructionOrderSummary(item)}|{item.Text}|{item.ObserverReply}"))
            .Prepend($"{inhabitant.Id}|{inhabitant.DisplayName}|{UiTheme.Current.Name}"));
        if (renderedOrdersReader == signature) return;
        renderedOrdersReader = signature;
        ordersReaderTitle.Text = $"{inhabitant.DisplayName} · orders";
        ordersReaderText.Clear();
        if (groups.All(group => group.Orders.Length == 0))
        {
            ordersReaderText.PushColor(DimText);
            ordersReaderText.AddText("No orders yet.");
            ordersReaderText.Pop();
        }
        var first = true;
        foreach (var (heading, orders) in groups.Where(group => group.Orders.Length > 0))
        {
            if (!first) ordersReaderText.Newline();
            first = false;
            ordersReaderText.PushFont(UiFonts.Headings, ordersReaderText.GetThemeFontSize("normal_font_size"));
            ordersReaderText.PushColor(HeadingText);
            ordersReaderText.AddText(heading);
            ordersReaderText.Pop();
            ordersReaderText.Pop();
            foreach (var order in orders)
            {
                ordersReaderText.Newline();
                var heldUp = order.Order?.Status is "blocked" or "interrupted";
                if (heldUp) ordersReaderText.PushColor(UiTheme.Current.Warning);
                ordersReaderText.AddText(GameUiText.PlainEllipses(InstructionOrderSummary(order)));
                if (heldUp) ordersReaderText.Pop();
                ordersReaderText.Newline();
                ordersReaderText.PushColor(DimText);
                ordersReaderText.AddText(GameUiText.PlainEllipses($"You said: “{order.Text}”"));
                ordersReaderText.Pop();
                if (order.ObserverReply is { } reply)
                {
                    ordersReaderText.Newline();
                    ordersReaderText.PushColor(UiTheme.Current.Link);
                    ordersReaderText.AddText(GameUiText.PlainEllipses($"{inhabitant.DisplayName} replied: “{reply}”"));
                    ordersReaderText.Pop();
                }
            }
        }
        FitTextPanel(ordersReaderText);
    }
}
