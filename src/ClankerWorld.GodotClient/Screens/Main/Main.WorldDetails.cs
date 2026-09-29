using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void RenderWorldDetails(OwnerWorldSnapshot snapshot)
    {
        var authoring = snapshot.Authoring;
        var lines = new List<TownLine>();
        if (authoring is not null)
            lines.Add(new(TownStyle.Note, $"{(authoring.IsPaused ? "Paused" : "Playing")} · {DisplayWorldClock(snapshot.WorldTick)} · " +
                $"{Pretty(authoring.Season)} · {Pretty(WeatherAtCamera(snapshot))} here"));
        lines.Add(new(TownStyle.Heading, "Shared stores"));
        if (snapshot.Stockpiles.Count == 0) lines.Add(new(TownStyle.Note, "No shared stores yet."));
        foreach (var stockpile in snapshot.Stockpiles)
        {
            lines.Add(new(TownStyle.Name, stockpile.Name));
            lines.Add(new(TownStyle.Detail, stockpile.Items.Count == 0 ? "empty" :
                string.Join(" · ", stockpile.Items.Select(item => $"{Pretty(item.Kind)} {item.Quantity}"))));
        }
        lines.Add(new(TownStyle.Heading, "Projects"));
        var workers = snapshot.Inhabitants.Where(person => person.Project is not null).ToArray();
        if (workers.Length == 0) lines.Add(new(TownStyle.Note, "No one is working on a project right now."));
        foreach (var person in workers)
        {
            lines.Add(new(TownStyle.Body, $"{person.DisplayName}: {person.Project!.Label} · {Pretty(person.Project.Stage)}"));
            if (person.Project.Blocker is { } blocker) lines.Add(new(TownStyle.Warning, blocker));
        }
        if (snapshot.Council is { } council)
        {
            lines.Add(new(TownStyle.Heading, "Household council"));
            lines.Add(new(TownStyle.Body, $"Steward: {council.StewardName ?? "awaiting a contributor"}"));
            lines.Add(new(TownStyle.Body, council.FoodPolicy == "essential_first" ? "Food reserve: hungry members first" : "Shared food: open access"));
            if (council.ProposedPolicy is not null)
                lines.Add(new(TownStyle.Body, $"Vote: {Pretty(council.ProposedPolicy)} · {council.Approvals} yes / {council.Rejections} no / {council.Voters} voters"));
        }
        lines.Add(new(TownStyle.Heading, "Social activity"));
        var notes = snapshot.Inhabitants.SelectMany(person => person.SocialNotes.Take(2).Select(note => $"{person.DisplayName}: {note}")).ToArray();
        if (notes.Length == 0) lines.Add(new(TownStyle.Note, "Nothing to report yet."));
        lines.AddRange(notes.Select(note => new TownLine(TownStyle.Body, note)));
        WriteTownPanel(lines);
    }

    private void RenderEventLog()
    {
        var snapshot = observationSession.Current?.Baseline.Snapshot;
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
        var content = newEventsAfter + "\n" +
            string.Join("\n", entries.Select(entry => $"{entry.EventId}|{entry.Located}|{entry.Clock}|{entry.Text}"));
        if (renderedEventLog == content) return;
        renderedEventLog = content;
        eventLog.Clear();
        if (entries.Length == 0)
        {
            eventLog.PushColor(DimText);
            eventLog.AddText("Nothing notable has happened yet.");
            eventLog.Pop();
            return;
        }

        // Newest first, grouped under each day so a time is enough per row.
        string? day = null;
        foreach (var entry in entries)
        {
            var (date, time) = SplitClock(entry.Clock);
            if (date != day)
            {
                if (day is not null) eventLog.Newline();
                eventLog.PushFontSize(13);
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
                eventLog.AddText(entry.Text + " ↗");
                eventLog.Pop();
                eventLog.Pop();
            }
            else eventLog.AddText(entry.Text);
            eventLog.Newline();
        }
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
            RenderInhabitantDetails(current.Baseline.Snapshot);
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
            RenderInhabitantDetails(current.Baseline.Snapshot);
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
        selectedInhabitantId = null;
        inhabitantList.DeselectAll();
        if (observationSession.Current is { } current)
        {
            RenderInhabitantDetails(current.Baseline.Snapshot);
            RenderSelectedInhabitantCard(current.Baseline.Snapshot);
            RenderMap(current.Baseline.Snapshot);
            RefreshControlAvailability();
        }
    }

}
