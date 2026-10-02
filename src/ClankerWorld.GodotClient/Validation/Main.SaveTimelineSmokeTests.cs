using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Load Save and Save World draw a world's branches as a timeline: each
    /// branch under the one it grew from, the running world marked where the
    /// host says it continues, and a card for the chosen save. The list stays
    /// one switch away.
    /// </summary>
    private async Task VerifySaveTimelineAsync()
    {
        const int Day = 360;
        var start = DateTimeOffset.UnixEpoch;
        var first = new SaveBranch("a", 1);
        var second = new SaveBranch("b", 2, "flood", "Before the flood", 2 * Day);
        var third = new SaveBranch("c", 3, "camp", "First camp", Day);
        ManualWorldSave old = new("old", "Old save", start, Day / 2);
        ManualWorldSave camp = new("camp", "First camp", start.AddMinutes(1), Day, false, first, BranchPosition: 1);
        ManualWorldSave flood = new("flood", "Before the flood", start.AddMinutes(2), 2 * Day, false, first, BranchPosition: 2);
        ManualWorldSave harvest = new("harvest", "Big harvest", start.AddMinutes(3), 9 * Day, false, first, BranchPosition: 3);
        ManualWorldSave winter = new("winter", "Hungry winter", start.AddMinutes(4), 6 * Day, false, second, BranchPosition: 1);
        ManualWorldSave autosave = new("auto", "Autosave", start.AddMinutes(5), 4 * Day, true, third, BranchPosition: 1);
        ManualWorldSave[] saves = [old, camp, flood, harvest, winter, autosave];
        var forking = new SaveTimelinePosition("flood", "a", true);

        // Saves from before branches on top, then branch 1, each branch under the save it grew from:
        // the latest fork closest, and the running world's new branch closest of all.
        var lanes = SaveTimelineLayout.Lanes(saves, forking);
        var keys = string.Join(",", lanes.Select(lane => lane.Key));
        if (keys != ",a,unsaved,b,c" || lanes[2].ForkSave?.Id != "flood" || lanes[2].Parent?.Key != "a" ||
            lanes[2].ColorNumber != 4 || lanes[4].ForkSave?.Id != "camp")
            throw new InvalidOperationException($"Branches must sit under the save they grew from, latest fork closest: {keys}.");
        if (SaveTimelineLayout.NowLane(lanes, forking) != lanes[2] ||
            SaveTimelineLayout.NowLane(lanes, new("harvest", "a", false)) != lanes[1] ||
            SaveTimelineLayout.NowLane(lanes, null) is not null ||
            SaveTimelineLayout.Lanes(saves, null).Any(lane => lane.IsUnsaved))
            throw new InvalidOperationException("You are here must follow where the host says the world continues, and nothing without it.");
        if (!lanes[1].IsLatest(harvest) || lanes[1].IsLatest(flood) || lanes[0].IsLatest(old) ||
            SaveTimelineLayout.BranchesFrom(lanes, flood) != 1)
            throw new InvalidOperationException("Only a branch's newest save may carry its banner, and only real branches count as grown from a save.");
        // Damaged records could name each other as starts; both branches still get a row.
        ManualWorldSave looped1 = new("x1", "Loop one", start, Day, false, new SaveBranch("x", 1, "y1"), BranchPosition: 1);
        ManualWorldSave looped2 = new("y1", "Loop two", start, Day, false, new SaveBranch("y", 2, "x1"), BranchPosition: 1);
        if (SaveTimelineLayout.Lanes([looped1, looped2], null).Length != 2)
            throw new InvalidOperationException("Branches that name each other as their start must both still be drawn.");
        var calendar = SaveTimelineCalendar.From(new OwnerWorldCalendarPace(Day, 40, 10, 10, 10, 10));
        var olderHost = SaveTimelineCalendar.From(new OwnerWorldCalendarPace(Day, 40));
        if (calendar.DateOf(25) != (2, 5, 10, 1) || calendar.DateOf(41) != (0, 1, 10, 2) ||
            olderHost.DateOf(39) != (3, 9, 10, 1))
            throw new InvalidOperationException("The season bar must count seasons the way the world does.");
        if (SaveTimelineLayout.Shorten("A save with a long name", 60, text => text.Length * 6) != "A save..." ||
            SaveTimelineLayout.Shorten("Winter", 60, text => text.Length * 6) != "Winter")
            throw new InvalidOperationException("Long save names must be shortened with three full stops.");

        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousPace = observedCalendarPace;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            observedCalendarPace = new OwnerWorldCalendarPace(Day, 40, 10, 10, 10, 10);
            var handshake = new OwnerWorldHandshake(new(1, 1),
                ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                 "owner-control.request.v1", "paused-authoring.request.v1"], []);
            observationSession.ResetAfterLoad();
            var snapshot = new OwnerWorldSnapshot("timeline-world", 7 * Day, "timeline-map", [new(0, 0, "meadow")], [], [], null, 0)
            {
                Authoring = new(true, 0, 0, 0, "timeline-map", "timeline-map", "clear", "spring", []),
            };
            if (!observationSession.TryAccept(new(handshake, new(snapshot, new(snapshot.WorldTick, 0, []))), 0, out var failure))
                throw new InvalidOperationException("Save-timeline observation fixture was refused: " + failure);
            string Details() => string.Join(" | ", manualSaveDetails.FindChildren("*", nameof(Label), true, false)
                .OfType<Label>().Select(label => label.Text));
            var rows = manualSaveTimeline.Rows;
            void Click(string id, bool twice = false) => rows._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                DoubleClick = twice,
                Position = rows.PointPosition(id) ?? throw new InvalidOperationException($"Save {id} must be drawn on the timeline."),
            });
            string? Chosen() => SelectedManualSave()?.Id;

            manualSaveShowsList = false;
            await OpenManualSavesAsync(true, _ => Task.FromResult(saves), _ => Task.FromResult<SaveTimelinePosition?>(forking));
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!manualSaveTimeline.Visible || manualSaveList.Visible || !manualSaveViewRow.Visible || !manualSaveKey.Visible ||
                manualSaveViewChoice.Selected != 0 || manualSaveTimeline.Lanes.Count != 5 || manualSaveTimeline.NowLane?.IsUnsaved != true)
                throw new InvalidOperationException("Load Save must open on the timeline, with its key and a row for the new branch.");
            if (!Details().Contains("You are here", StringComparison.Ordinal) ||
                !Details().Contains("Playing on from ‘Before the flood’. Your next save starts Branch 4", StringComparison.Ordinal) ||
                !manualSaveLoadButton.Disabled)
                throw new InvalidOperationException($"With nothing chosen, the card must say where the world continues: {Details()}.");
            var card = manualSaveCard.GetGlobalRect();
            var view = GetViewport().GetVisibleRect();
            if (!view.Grow(1).Encloses(card))
                throw new InvalidOperationException($"Load Save's timeline must fit on screen: {card} in {view}.");

            Click("flood");
            if (Chosen() != "flood" || manualSaveTimeline.SelectedId != "flood" || manualSaveLoadButton.Disabled ||
                !Details().Contains("BRANCH 1", StringComparison.Ordinal) ||
                !Details().Contains("Another branch grew from here. Playing on from it starts a new branch", StringComparison.Ordinal))
                throw new InvalidOperationException($"Clicking a save must choose it and describe it: {Details()}.");
            Click("harvest", twice: true);
            if (Chosen() != "harvest" || !manualSaveLoadConfirmation.Visible ||
                !manualSaveLoadConfirmation.DialogText.Contains("Big harvest", StringComparison.Ordinal) ||
                manualSaveLoadConfirmation.DialogText.Contains("new branch", StringComparison.Ordinal) ||
                !Details().Contains("LATEST", StringComparison.Ordinal))
                throw new InvalidOperationException("Double-clicking a branch's newest save must ask to load it, continuing its branch.");
            manualSaveLoadConfirmation.Hide();
            rows._GuiInput(new InputEventAction { Action = "ui_left", Pressed = true });
            if (Chosen() != "winter")
                throw new InvalidOperationException($"The left arrow must choose the save before it in time, not {Chosen()}.");

            manualSaveViewChoice.Select(1);
            manualSaveViewChoice.EmitSignal(SegmentedChoice.SignalName.ItemSelected, 1L);
            if (!manualSaveList.Visible || manualSaveTimeline.Visible || manualSaveDetails.Visible || manualSaveKey.Visible ||
                !manualSaveViewLabel.Visible || Chosen() != "winter" || manualSaveLoadButton.Disabled)
                throw new InvalidOperationException("The List switch must show the list with the same save chosen.");
            manualSaveList.Select(0);
            manualSaveList.EmitSignal(SlotList.SignalName.ItemSelected, 0L);
            manualSaveViewChoice.Select(0);
            manualSaveViewChoice.EmitSignal(SegmentedChoice.SignalName.ItemSelected, 0L);
            if (!manualSaveTimeline.Visible || manualSaveTimeline.SelectedId != listedManualSaves[0].Id)
                throw new InvalidOperationException("Back on the timeline, the save chosen in the list must stay chosen.");
            manualSaveOverlay.Hide();

            // An older host cannot say where the world continues: the timeline marks nothing.
            await OpenManualSavesAsync(true, _ => Task.FromResult(saves),
                _ => Task.FromException<SaveTimelinePosition?>(new HttpRequestException("Controlled older host.")));
            if (!manualSaveTimeline.Visible || manualSaveTimeline.NowLane is not null ||
                manualSaveTimeline.Lanes.Any(lane => lane.IsUnsaved) || Details().Contains("You are here", StringComparison.Ordinal) ||
                !Details().Contains("Choose a save to see it here", StringComparison.Ordinal))
                throw new InvalidOperationException($"Without the host's answer the timeline must draw no marker: {Details()}.");
            manualSaveOverlay.Hide();

            // Save World draws the same timeline under its New save box. Autosaves show where the
            // world has been but cannot be overwritten, so only named saves can be chosen.
            await OpenManualSavesAsync(false, _ => Task.FromResult(saves),
                _ => Task.FromResult<SaveTimelinePosition?>(new("harvest", "a", false)));
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!manualSaveTimeline.Visible || !manualSaveNewBox.Visible || manualSaveList.Visible ||
                manualSaveTimeline.Lanes.Count != 4 || manualSaveTimeline.NowLane?.Key != "a" ||
                !Details().Contains("Playing on from ‘Big harvest’. Your new save continues Branch 1.", StringComparison.Ordinal))
                throw new InvalidOperationException($"Save World must draw the timeline and say where the new save goes: {Details()}.");
            Click("auto");
            if (Chosen() is not null || !manualSaveOverwriteButton.Disabled)
                throw new InvalidOperationException("Save World must not offer an autosave to overwrite.");
            Click("flood");
            if (Chosen() != "flood" || manualSaveOverwriteButton.Disabled || manualSaveCreateButton.Disabled ||
                !Details().Contains("Overwrite replaces it with the world as it is now", StringComparison.Ordinal))
                throw new InvalidOperationException($"Choosing a save in Save World must offer to overwrite it: {Details()}.");
            manualSaveOverwriteButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!manualSaveOverwriteConfirmation.Visible || pendingOverwriteSaveId != "flood")
                throw new InvalidOperationException("Overwrite must confirm the save chosen on the timeline.");
            manualSaveOverwriteConfirmation.Hide();
            pendingOverwriteSaveId = null;
            card = manualSaveCard.GetGlobalRect();
            if (!view.Grow(1).Encloses(card))
                throw new InvalidOperationException($"Save World's timeline must fit on screen: {card} in {view}.");
            manualSaveOverlay.Hide();

            // Load Save without saves shows only the list's placeholder.
            await OpenManualSavesAsync(true, _ => Task.FromResult<ManualWorldSave[]>([]));
            if (manualSaveTimeline.Visible || manualSaveViewRow.Visible || !manualSaveList.Visible ||
                manualSaveList.Placeholder != "No saves yet.")
                throw new InvalidOperationException("Load Save with no saves must show the list's placeholder only.");
            manualSaveOverlay.Hide();
        }
        finally
        {
            manualSaveOverlay.Hide();
            manualSaveLoadConfirmation.Hide();
            manualSaveOverwriteConfirmation.Hide();
            pendingOverwriteSaveId = null;
            manualSaveShowsList = false;
            listedTimelinePosition = null;
            listedManualSaves = [];
            allListedManualSaves = [];
            manualSaveList.Clear();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observedCalendarPace = previousPace;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            System.Environment.SetEnvironmentVariable("CI", previousCi);
        }
    }
}
