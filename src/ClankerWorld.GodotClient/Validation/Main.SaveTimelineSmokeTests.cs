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
        ManualWorldSave winter = new("winter", "Hungry winter", start.AddMinutes(4), 6 * Day, false, second,
            ContinuedFromId: flood.Id, ContinuedFromCreatedUtc: flood.CreatedUtc, BranchPosition: 1);
        ManualWorldSave autosave = new("auto", "Autosave", start.AddMinutes(5), 4 * Day, true, third,
            ContinuedFromId: camp.Id, ContinuedFromCreatedUtc: camp.CreatedUtc, BranchPosition: 1);
        ManualWorldSave[] saves = [old, camp, flood, harvest, winter, autosave];
        var forking = new SaveTimelinePosition("flood", "a", true, NextBranchNumber: 4, ContinuedFromTick: 2 * Day);

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
        // A slot can be overwritten after another branch grew from its old
        // contents. That branch must not attach to the replacement checkpoint.
        var replacement = flood with { Name = "Replacement world", CreatedUtc = start.AddMinutes(20), WorldTick = 8 * Day, BranchPosition = 4 };
        var replacedOrigin = SaveTimelineLayout.Lanes([camp, replacement, winter], null).Single(lane => lane.Key == "b");
        if (replacedOrigin.Parent is not null || replacedOrigin.ForkSave is not null || replacedOrigin.OriginTick != 2 * Day)
            throw new InvalidOperationException("A branch must retain its original fork time without attaching to an overwritten source slot.");
        var calendar = SaveTimelineCalendar.From(new OwnerWorldCalendarPace(Day, 40, 10, 10, 10, 10));
        var olderHost = SaveTimelineCalendar.From(new OwnerWorldCalendarPace(Day, 40));
        if (calendar.DateOf(25) != (2, 5, 10, 1) || calendar.DateOf(41) != (0, 1, 10, 2) ||
            olderHost.DateOf(39) != (3, 9, 10, 1))
            throw new InvalidOperationException("The season bar must count seasons the way the world does.");
        var morning = SaveTimelineCalendar.From(new OwnerWorldCalendarPace(Day, 40, 10, 10, 10, 10, CalendarOffsetTicks: Day / 4));
        if (morning.Day(0) != 0.25f || morning.Day(3L * Day / 4) != 1f || calendar.Day(0) != 0f)
            throw new InvalidOperationException("A save in a world that starts in the morning must sit at its calendar time of day.");
        if (SaveTimelineLayout.Shorten("A save with a long name", 60, text => text.Length * 6) != "A save..." ||
            SaveTimelineLayout.Shorten("Winter", 60, text => text.Length * 6) != "Winter")
            throw new InvalidOperationException("Long save names must be shortened with three full stops.");

        var nameFont = manualSaveHeading.GetThemeFont("font", "Label");
        var nameSize = manualSaveHeading.GetThemeFontSize("font_size", "Label");
        var unicodeName = new string('W', 7) + "🌱" + new string('W', 35);
        var shortened = SaveTimelineLayout.Shorten(unicodeName, 170,
            text => nameFont.GetStringSize(text, HorizontalAlignment.Left, -1, nameSize).X);
        var shortenedPrefix = shortened[..^3];
        if (!unicodeName.StartsWith(shortenedPrefix, StringComparison.Ordinal) ||
            !System.Globalization.StringInfo.ParseCombiningCharacters(unicodeName).Contains(shortenedPrefix.Length) ||
            nameFont.GetStringSize(shortened, HorizontalAlignment.Left, -1, nameSize).X > 170)
            throw new InvalidOperationException("Native timeline names must fit without splitting a Unicode character.");

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
                !Details().Contains("Playing on from \"Before the flood\". Your next save starts Branch 4", StringComparison.Ordinal) ||
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
                !Details().Contains("Playing on from \"Big harvest\". Your new save continues Branch 1.", StringComparison.Ordinal))
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
            var createsBeforeValidation = host.SaveCreateCount;
            manualSaveName.Text = "   ";
            manualSaveCreateButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!statusToast.IsVisibleInTree() || statusToast.ZIndex <= manualSaveOverlay.ZIndex ||
                statusLabel.Text != "Give the save a name (1–80 characters)." ||
                host.SaveCreateCount != createsBeforeValidation || !manualSaveOverlay.Visible)
                throw new InvalidOperationException("A blank save name must show a visible remedy without sending a save or closing the timeline.");
            statusToast.Hide();
            card = manualSaveCard.GetGlobalRect();
            if (!view.Grow(1).Encloses(card))
                throw new InvalidOperationException($"Save World's timeline must fit on screen: {card} in {view}.");
            manualSaveOverlay.Hide();

            // A world's first checkpoints can all be autosaves. They still show its
            // history and current branch, while New save is the only save action.
            await OpenManualSavesAsync(false, _ => Task.FromResult<ManualWorldSave[]>([autosave]),
                _ => Task.FromResult<SaveTimelinePosition?>(new("auto", "c", false)));
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!manualSaveTimeline.Visible || !manualSaveViewRow.Visible || manualSaveList.Visible ||
                rows.PointPosition("auto") is null || manualSaveTimeline.NowLane?.Key != "c" ||
                !Details().Contains("Your new save continues Branch 3.", StringComparison.Ordinal) ||
                Chosen() is not null || !manualSaveOverwriteButton.Disabled || !manualSaveDeleteButton.Disabled ||
                manualSaveCreateButton.Disabled)
                throw new InvalidOperationException("Save World must draw autosave-only history and offer a new named save.");
            Click("auto");
            if (Chosen() is not null || manualSaveTimeline.SelectedId is not null ||
                !manualSaveOverwriteButton.Disabled || !manualSaveDeleteButton.Disabled)
                throw new InvalidOperationException("An autosave-only timeline must not select an autosave to overwrite or delete.");
            manualSaveViewChoice.Select(1);
            manualSaveViewChoice.EmitSignal(SegmentedChoice.SignalName.ItemSelected, 1L);
            if (!manualSaveList.Visible || manualSaveList.Placeholder != "No named saves yet." || manualSaveTimeline.Visible)
                throw new InvalidOperationException("The autosave-only history must retain the named-save list's empty placeholder.");
            manualSaveOverlay.Hide();
            await OpenManualSavesAsync(false, _ => Task.FromResult<ManualWorldSave[]>([autosave]));
            if (!manualSaveList.Visible || manualSaveTimeline.Visible || manualSaveViewChoice.Selected != 1)
                throw new InvalidOperationException("Reopening Save World must retain the player's List choice.");
            manualSaveOverlay.Hide();
            manualSaveShowsList = false;

            // Deleted checkpoints and a missing timeline record still leave a
            // new-branch marker. Its number comes from the host's retained history.
            foreach (var position in new SaveTimelinePosition[]
            {
                new("deleted-origin", "a", true, NextBranchNumber: 12, ContinuedFromTick: 2 * Day),
                new(null, null, true, NextBranchNumber: 12),
            })
            {
                await OpenManualSavesAsync(false, _ => Task.FromResult(saves),
                    _ => Task.FromResult<SaveTimelinePosition?>(position));
                for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (manualSaveTimeline.NowLane is not { IsUnsaved: true, ColorNumber: 12 } newLane ||
                    rows.NowPosition() is null || !Details().Contains("Your new save starts Branch 12", StringComparison.Ordinal) ||
                    (position.BranchId == "a" && (newLane.Parent?.Key != "a" || newLane.OriginTick != 2 * Day)))
                    throw new InvalidOperationException("A missing source must preserve the host's next branch number, origin and visible current-world marker.");
                manualSaveOverlay.Hide();
            }

            await OpenManualSavesAsync(false, _ => Task.FromResult(saves),
                _ => Task.FromResult<SaveTimelinePosition?>(new("flood", "a", true, ContinuedFromTick: 2 * Day)));
            if (!Details().Contains("Your new save starts a new branch", StringComparison.Ordinal) ||
                Details().Contains("starts Branch", StringComparison.Ordinal))
                throw new InvalidOperationException("Without an authoritative next number, the readout must describe a new branch without guessing its number.");
            manualSaveOverlay.Hide();

            // Deleting all points from the current branch does not move the
            // running world onto an unrelated branch or create a fake save.
            await OpenManualSavesAsync(false, _ => Task.FromResult<ManualWorldSave[]>([winter]),
                _ => Task.FromResult<SaveTimelinePosition?>(new("deleted-current", "a", false,
                    NextBranchNumber: 1, ContinuedFromTick: 2 * Day)));
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (manualSaveTimeline.NowLane is not { Key: "a", IsUnsaved: false, Branch.Number: 1 } continuing ||
                continuing.Points.Count != 0 || rows.NowPosition() is null || rows.PointPosition("deleted-current") is not null ||
                rows.PointPosition("winter") is null || !Details().Contains("Your new save continues Branch 1.", StringComparison.Ordinal))
                throw new InvalidOperationException("An empty current branch must keep its marker and number without inventing a checkpoint.");
            manualSaveOverlay.Hide();

            // Paused saves share a world time. The visible newest point must be
            // chosen, and displaced names must remain reachable by scrolling.
            var crowded = Enumerable.Range(1, 8).Select(index => new ManualWorldSave(
                "paused-" + index, new string('W', 80), start.AddMinutes(index), 4 * Day,
                false, first, BranchPosition: index)).ToArray();
            ManualWorldSave anchor = new("anchor", "Other branch", start.AddMinutes(9), 6 * Day,
                false, new SaveBranch("anchor-branch", 2), BranchPosition: 1);
            await OpenManualSavesAsync(true, _ => Task.FromResult<ManualWorldSave[]>([.. crowded, anchor]));
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Click("paused-8");
            if (Chosen() != "paused-8" || !Details().Contains("LATEST", StringComparison.Ordinal))
                throw new InvalidOperationException("Clicking coincident paused saves must choose the visible newest save.");
            foreach (var save in crowded)
            {
                var tag = rows.TagArea(save.Id) ?? throw new InvalidOperationException("Every named save must have a reachable name tag.");
                if (tag.Position.X < 0 || tag.End.X > rows.CustomMinimumSize.X)
                    throw new InvalidOperationException("Crowded save names must fit inside the scrollable timeline content.");
            }
            manualSaveTimeline.Select("paused-8");
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var lastTag = rows.TagArea("paused-8") ?? throw new InvalidOperationException("The selected save's tag must remain drawn.");
            if (lastTag.Position.X < rows.ViewLeft - 1 || lastTag.End.X > rows.ViewLeft + rows.ViewWidth + 1)
                throw new InvalidOperationException("Choosing a crowded save must scroll its complete name tag into view.");

            Click("anchor");
            var hiddenPoint = rows.PointPosition("paused-8") ?? throw new InvalidOperationException("The first branch must retain its save points.");
            var previousViewTop = rows.ViewTop;
            rows.ViewTop = hiddenPoint.Y - 24;
            rows.QueueRedraw();
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            rows._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                DoubleClick = true,
                Position = hiddenPoint,
            });
            if (Chosen() != "anchor" || manualSaveLoadConfirmation.Visible || rows._GetTooltip(hiddenPoint).Length > 0)
                throw new InvalidOperationException("The pinned season bar must hide save tooltips and intercept clicks above its lower edge.");
            rows.ViewTop = previousViewTop;
            rows.QueueRedraw();
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
