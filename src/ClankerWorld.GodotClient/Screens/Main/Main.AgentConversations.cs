using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private const int ConversationPreviewCharacters = 150;
    private const int MaximumVisibleConversationTurns = 7;

    private readonly PanelContainer conversationPanel = new();
    private readonly Label conversationReaderTitle = new();
    private readonly Label conversationReaderStatus = new() { ThemeTypeVariation = "DimLabel" };
    private readonly Label conversationReaderSummary = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button conversationHistoryButton = new();
    private readonly RichTextLabel conversationHistoryText = new();
    private readonly HashSet<string> locallyReadConversationTurns = new(StringComparer.Ordinal);
    private string? conversationReadWorldId;
    private string? openConversationId;
    private string? openConversationAgentId;
    private bool conversationHistoryExpanded;

    private void BuildConversationReader()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        var heading = new HBoxContainer();
        conversationReaderTitle.ThemeTypeVariation = "HeadingLabel";
        conversationReaderTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        heading.AddChild(conversationReaderTitle);
        var close = CloseButton("Close conversation (Esc)");
        close.Pressed += () => conversationPanel.Hide();
        heading.AddChild(close);
        body.AddChild(heading);
        body.AddChild(conversationReaderStatus);
        body.AddChild(conversationReaderSummary);
        conversationHistoryButton.Text = "Show history";
        conversationHistoryButton.Pressed += ToggleConversationHistory;
        StyleCompactToggle(conversationHistoryButton);
        body.AddChild(conversationHistoryButton);
        ConfigureTextPanel(conversationHistoryText, 500);
        conversationHistoryText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        conversationHistoryText.AddThemeConstantOverride("paragraph_separation", 4);
        conversationHistoryText.Hide();
        body.AddChild(conversationHistoryText);
        AddPanelContents(conversationPanel, body);
        conversationPanel.ZIndex = 86;
        conversationPanel.Resized += () => PlaceReaderPanel(conversationPanel);
        conversationPanel.Hide();
    }

    private void OpenAgentConversation(string agentId)
    {
        var snapshot = renderedMapSnapshot;
        var conversation = snapshot is null ? null : LatestConversationFor(snapshot, agentId);
        if (snapshot is null || conversation is null) return;
        OpenConversationReader(snapshot, agentId, conversation.Id);
    }

    private void OpenConversationReader(OwnerWorldSnapshot snapshot, string agentId, string conversationId)
    {
        var conversation = snapshot.Conversations.FirstOrDefault(item => item.Id == conversationId);
        if (conversation is null) return;

        memoriesPanel.Hide();
        thoughtsPanel.Hide();
        familyTreePanel.Hide();
        rosterPanel.Hide();
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        worldInfoPanel.Hide();
        filtersPanel.Hide();
        buildingDetailsPanel.Hide();

        openConversationId = conversationId;
        openConversationAgentId = agentId;
        conversationHistoryExpanded = false;
        var unread = ConversationUnreadCount(snapshot.WorldId, conversation, agentId);
        MarkConversationRead(snapshot.WorldId, conversation, agentId);
        RenderConversationReader(snapshot, conversation, agentId, unread);
        conversationPanel.Show();
        RefreshConversationBadges(snapshot);
        ApplyResponsiveLayout();
    }

    private void ToggleConversationHistory()
    {
        conversationHistoryExpanded = !conversationHistoryExpanded;
        if (renderedMapSnapshot is { } snapshot && openConversationId is { } conversationId &&
            openConversationAgentId is { } agentId &&
            snapshot.Conversations.FirstOrDefault(item => item.Id == conversationId) is { } conversation)
            RenderConversationReader(snapshot, conversation, agentId, 0);
    }

    private void RenderAgentConversationReader(OwnerWorldSnapshot snapshot)
    {
        if (!string.Equals(conversationReadWorldId, snapshot.WorldId, StringComparison.Ordinal))
        {
            conversationReadWorldId = snapshot.WorldId;
            locallyReadConversationTurns.Clear();
            openConversationId = null;
            openConversationAgentId = null;
            conversationPanel.Hide();
        }
        var retainedTurnKeys = snapshot.Conversations
            .SelectMany(conversation => conversation.Turns)
            .Select(turn => ConversationTurnKey(snapshot.WorldId, turn.Id))
            .ToHashSet(StringComparer.Ordinal);
        locallyReadConversationTurns.RemoveWhere(key => !retainedTurnKeys.Contains(key));
        if (!conversationPanel.Visible || openConversationId is not { } conversationId ||
            openConversationAgentId is not { } agentId) return;
        if (snapshot.Conversations.FirstOrDefault(item => item.Id == conversationId) is not { } conversation)
        {
            conversationPanel.Hide();
            return;
        }
        var unread = ConversationUnreadCount(snapshot.WorldId, conversation, agentId);
        RenderConversationReader(snapshot, conversation, agentId, unread);
        MarkConversationRead(snapshot.WorldId, conversation, agentId);
        RefreshConversationBadges(snapshot);
    }

    private void RenderConversationReader(
        OwnerWorldSnapshot snapshot,
        OwnerWorldConversation conversation,
        string agentId,
        int unreadCount)
    {
        var isParticipant = conversation.InitiatorId == agentId || conversation.InviteeId == agentId;
        if (isParticipant)
        {
            var ownName = snapshot.Inhabitants.FirstOrDefault(item => item.Id == agentId)?.DisplayName ??
                (conversation.InitiatorId == agentId ? conversation.InitiatorName : conversation.InviteeName);
            var otherName = conversation.InitiatorId == agentId ? conversation.InviteeName : conversation.InitiatorName;
            conversationReaderTitle.Text = $"{ownName} and {otherName}";
        }
        else
        {
            conversationReaderTitle.Text = $"Nearby conversation · {conversation.InitiatorName} and {conversation.InviteeName}";
        }
        conversationReaderStatus.Text = ConversationStatusText(conversation);
        conversationReaderSummary.Text = ConversationSummary(conversation, agentId, unreadCount);

        var heardTurns = conversation.Turns
            .Where(turn => ConversationTurnWasHeardBy(turn, agentId))
            .TakeLast(MaximumVisibleConversationTurns)
            .ToArray();
        conversationHistoryButton.Visible = heardTurns.Length > 0;
        conversationHistoryButton.Text = conversationHistoryExpanded
            ? "Hide history"
            : $"Show history ({heardTurns.Length} public turn{(heardTurns.Length == 1 ? string.Empty : "s")})";
        conversationHistoryText.Visible = conversationHistoryExpanded && heardTurns.Length > 0;
        if (!conversationHistoryText.Visible)
        {
            SetPanelText(conversationHistoryText, string.Empty);
            return;
        }

        var history = string.Join("\n\n", heardTurns.Select(turn =>
            $"{turn.SpeakerName} · {DisplayWorldClock(turn.WorldTick)}" +
            (turn.IsWrapUp ? " · wrap-up" : string.Empty) + $"\n{turn.Text}"));
        SetPanelText(conversationHistoryText, history);
    }

    private static string ConversationSummary(OwnerWorldConversation conversation, string agentId, int unreadCount)
    {
        var heardTurns = conversation.Turns.Where(turn => ConversationTurnWasHeardBy(turn, agentId)).ToArray();
        if (heardTurns.Length == 0)
            return unreadCount > 0 ? "A conversation is waiting for a response." : "No public turns have been saved yet.";
        var latest = heardTurns[^1];
        var said = GameUiText.PlainEllipses(latest.Text);
        var preview = said.Length > ConversationPreviewCharacters
            ? said[..ConversationPreviewCharacters].TrimEnd() + "..."
            : said;
        var newTurns = unreadCount > 0 ? $"{unreadCount} new public turn{(unreadCount == 1 ? string.Empty : "s")}. " : string.Empty;
        return $"{newTurns}{latest.SpeakerName}: {preview}";
    }

    private static string ConversationStatusText(OwnerWorldConversation conversation) => conversation.Status switch
    {
        "proposed" => "Waiting for the other person to accept",
        "ready" or "awaiting_speaker" => "In progress",
        "wrap_up" => "Waiting for both people to accept or decline the same wrap-up",
        "suspended" => $"Interrupted · {ConversationInterruptionText(conversation.Interruption)}",
        "closed" => $"Closed · {ConversationOutcomeText(conversation.Outcome)}",
        _ => "Status unavailable",
    };

    private static string ConversationInterruptionText(string? interruption) => interruption switch
    {
        "owner_paused" => "world paused",
        "disconnected" => "people moved apart",
        "urgent_need" => "urgent need",
        "provider_unavailable" => "assigned model unavailable",
        "provider_timed_out" => "model timed out",
        "provider_rejected" => "model response rejected",
        "restored" => "world reloaded; resume requires both people",
        _ => "interrupted",
    };

    private static string ConversationOutcomeText(string? outcome) => outcome switch
    {
        "agreed" => "both accepted the wrap-up",
        "refused" => "invitation declined",
        "deadline" => "invitation expired",
        "daily_limit" => "daily limit reached",
        "withdrawn" => "one person ended the conversation",
        "disagreed" => "wrap-up declined",
        "participant_unavailable" => "a participant left",
        _ => "ended",
    };

    private static string ConversationTooltipSummary(string agentId, OwnerWorldConversation conversation)
    {
        var turn = conversation.Turns.LastOrDefault(item => ConversationTurnWasHeardBy(item, agentId));
        var status = ConversationStatusText(conversation);
        if (turn is null) return $"{status} · click to see the conversation.";
        var said = GameUiText.PlainEllipses(turn.Text);
        var preview = said.Length > 80 ? said[..80].TrimEnd() + "..." : said;
        return $"{status} · {turn.SpeakerName}: {preview} · click to see the conversation.";
    }

    private static OwnerWorldConversation? LatestConversationFor(OwnerWorldSnapshot snapshot, string agentId) =>
        snapshot.Conversations
            .Where(conversation => conversation.InitiatorId == agentId || conversation.InviteeId == agentId ||
                conversation.Turns.Any(turn => ConversationTurnWasHeardBy(turn, agentId)))
            .OrderByDescending(conversation => conversation.LastUpdatedTick)
            .ThenBy(conversation => conversation.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool ConversationTurnWasHeardBy(OwnerWorldConversationTurn turn, string agentId) =>
        turn.SpeakerId == agentId || turn.ListenerIds.Contains(agentId, StringComparer.Ordinal);

    private int ConversationUnreadCount(string worldId, OwnerWorldConversation conversation, string agentId) =>
        conversation.Turns.Count(turn => ConversationTurnWasHeardBy(turn, agentId) &&
            !locallyReadConversationTurns.Contains(ConversationTurnKey(worldId, turn.Id)));

    private void MarkConversationRead(string worldId, OwnerWorldConversation conversation, string agentId)
    {
        foreach (var turn in conversation.Turns.Where(turn => ConversationTurnWasHeardBy(turn, agentId)))
            locallyReadConversationTurns.Add(ConversationTurnKey(worldId, turn.Id));
    }

    private void RefreshConversationBadges(OwnerWorldSnapshot snapshot)
    {
        foreach (var inhabitant in snapshot.Inhabitants)
        {
            if (!inhabitantVisuals.TryGetValue(inhabitant.Id, out var marker)) continue;
            if (LatestConversationFor(snapshot, inhabitant.Id) is not { } conversation)
            {
                marker.ConversationBadgeVisible = false;
                marker.ConversationUnread = false;
                continue;
            }
            marker.ConversationBadgeVisible = true;
            marker.ConversationUnread = ConversationUnreadCount(snapshot.WorldId, conversation, inhabitant.Id) > 0;
            marker.TooltipText = $"{inhabitant.DisplayName} · {Pretty(inhabitant.Lifecycle)} · " +
                (inhabitant.PublicIntention?.Summary ?? "taking in the world") + "\n" +
                ConversationTooltipSummary(inhabitant.Id, conversation);
        }
    }

    private static string ConversationTurnKey(string worldId, string turnId) => $"{worldId}|{turnId}";
}
