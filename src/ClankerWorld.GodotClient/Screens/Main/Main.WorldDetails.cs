using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void RenderEventLog()
    {
        var snapshot = observationSession.Current?.Baseline.Snapshot;
        var offersNewcomer = WorldEventText.OffersNewcomer(snapshot);
        var entries = knownEvents.Values
            .Where(worldEvent => GameUiText.IsPlayerFacingEvent(worldEvent.Kind))
            .OrderByDescending(worldEvent => worldEvent.EventId)
            .Take(30)
            .Select(worldEvent => (worldEvent.EventId, Located: worldEvent.Position is not null,
                Clock: DisplayWorldClock(worldEvent.WorldTick), Text: DescribeWorldEvent(worldEvent, snapshot)))
            .ToArray();
        // Rebuilding identical rows every refresh would reset the reader's
        // scroll position, so only a changed list is redrawn.
        UpdateUnreadEvents(snapshot?.WorldId ?? eventsWorldId);
        var content = newEventsAfter + "|" + offersNewcomer + "\n" +
            string.Join("\n", entries.Select(entry => $"{entry.EventId}|{entry.Located}|{entry.Clock}|{entry.Text}"));
        if (renderedEventLog == content) return;
        renderedEventLog = content;
        RenderEventRows(entries, offersNewcomer);
        eventLog.Clear();
        if (offersNewcomer)
        {
            // Keep this current offer above the history even after the original
            // rule-on event leaves the bounded history or the latest thirty rows.
            eventLog.PushColor(UiTheme.Current.Warning);
            eventLog.AddText(WorldEventText.ContinuityRisk + " ");
            eventLog.Pop();
            eventLog.PushMeta("add-newcomer");
            eventLog.PushColor(LinkText);
            eventLog.AddText("Add a newcomer");
            eventLog.Pop();
            eventLog.Pop();
            if (entries.Length > 0)
            {
                eventLog.Newline();
                eventLog.Newline();
            }
        }
        if (entries.Length == 0 && !offersNewcomer)
        {
            eventLog.PushColor(DimText);
            eventLog.AddText("Nothing notable has happened yet.");
            eventLog.Pop();
            FitTextPanel(eventLog);
            return;
        }

        // Newest first, grouped under each day so a time is enough per row.
        // Each row ends where the next begins, so no blank line trails the log.
        string? day = null;
        foreach (var entry in entries)
        {
            var (date, time) = SplitClock(entry.Clock);
            if (day is not null) eventLog.Newline();
            if (date != day)
            {
                if (day is not null) eventLog.Newline();
                eventLog.PushFont(UiFonts.Headings, eventLog.GetThemeFontSize("normal_font_size"));
                eventLog.PushColor(HeadingText);
                eventLog.AddText(date);
                eventLog.Pop();
                eventLog.Pop();
                eventLog.Newline();
                day = date;
            }
            // Rows that arrived since the log was last opened keep a small dot.
            if (entry.EventId > newEventsAfter)
            {
                eventLog.PushColor(UiTheme.Current.Warning);
                eventLog.AddText("● ");
                eventLog.Pop();
            }
            eventLog.PushColor(DimText);
            eventLog.AddText(time + "  ");
            eventLog.Pop();
            if (entry.Located)
            {
                eventLog.PushMeta(entry.EventId.ToString(CultureInfo.InvariantCulture));
                eventLog.PushColor(LinkText);
                eventLog.AddText(GameUiText.PlainEllipses(entry.Text) + " ↗");
                eventLog.Pop();
                eventLog.Pop();
            }
            else eventLog.AddText(GameUiText.PlainEllipses(entry.Text));
        }
        FitTextPanel(eventLog);
    }

    private async Task HandleEventLogActionAsync(string action)
    {
        if (action == "add-newcomer")
        {
            // A link from a held row must not reopen an offer that has ended.
            if (WorldEventText.OffersNewcomer(observationSession.Current?.Baseline.Snapshot))
                await OpenAddAgentAsync();
            return;
        }
        JumpToEvent(action);
    }

    private void JumpToEvent(string eventId)
    {
        if (!long.TryParse(eventId, CultureInfo.InvariantCulture, out var id) ||
            !knownEvents.TryGetValue(id, out var worldEvent) ||
            worldEvent.Position is not { } position)
            return;
        CenterCameraAt(new Vector2(position.X + 0.5f, position.Y + 0.5f));
        eventsPanel.Hide();
    }

    private void SelectInhabitantFromList(long index)
    {
        if (index < 0 || index >= inhabitantList.ItemCount)
        {
            return;
        }

        var inhabitantId = inhabitantList.GetItemMetadata((int)index).AsString();
        if (string.Equals(inhabitantId, selectedInhabitantId, StringComparison.Ordinal))
        {
            ClearInhabitantSelection();
            return;
        }

        selectedInhabitantId = inhabitantId;
        rosterPanel.Hide();
        // The roster promises to find the agent, so bring them into view.
        CenterOnInhabitant(inhabitantId);
        if (observationSession.Current is { } current)
        {
            RenderSelectedInhabitantCard(current.Baseline.Snapshot);
            RenderMap(current.Baseline.Snapshot);
            RefreshControlAvailability();
        }
    }

    private void SelectInhabitant(string inhabitantId)
    {
        if (string.Equals(inhabitantId, selectedInhabitantId, StringComparison.Ordinal))
        {
            ClearInhabitantSelection();
            return;
        }

        selectedInhabitantId = inhabitantId;
        for (var index = 0; index < inhabitantList.ItemCount; index++)
        {
            if (string.Equals(inhabitantList.GetItemMetadata(index).AsString(), inhabitantId, StringComparison.Ordinal))
            {
                inhabitantList.Select(index);
                break;
            }
        }

        if (observationSession.Current is { } current)
        {
            RenderSelectedInhabitantCard(current.Baseline.Snapshot);
            RenderMap(current.Baseline.Snapshot);
            RefreshControlAvailability();
        }
    }

    private void ClearInhabitantSelection()
    {
        CloseAgentModelEditor();
        familyTreePanel.Hide();
        memoriesPanel.Hide();
        thoughtsPanel.Hide();
        ordersPanel.Hide();
        conversationPanel.Hide();
        selectedInhabitantId = null;
        inhabitantList.DeselectAll();
        selectedInhabitantCard.Hide();
        agentProfilePanel.Hide();
        if (observationSession.Current is { } current)
        {
            RenderSelectedInhabitantCard(current.Baseline.Snapshot);
            RenderMap(current.Baseline.Snapshot);
            RefreshControlAvailability();
        }
    }

}
