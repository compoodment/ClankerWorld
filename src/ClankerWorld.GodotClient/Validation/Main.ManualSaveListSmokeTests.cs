using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>Saves from two versions of events are grouped apart, and each branch's latest point is clear.</summary>
    private void VerifySaveBranchList()
    {
        var start = DateTimeOffset.UnixEpoch;
        var first = new SaveBranch("a", 1);
        var second = new SaveBranch("b", 2, "flood", "Before the flood", 10);
        ManualWorldSave flood = new("flood", "Before the flood", start, 10, false, first, BranchPosition: 1);
        ManualWorldSave harvest = new("harvest", "Big harvest", start.AddMinutes(1), 90, false, first, "flood", start, BranchPosition: 2);
        ManualWorldSave winter = new("winter", "Hungry winter", start.AddMinutes(2), 60, false, second, "flood", start, BranchPosition: 1);
        ManualWorldSave old = new("old", "Old save", start.AddMinutes(-1), 5);
        ManualWorldSave[] saves = [old, flood, harvest, winter];
        var ordered = OrderSavesByBranch(saves).Select(save => save.Id).ToArray();
        if (!ordered.SequenceEqual(["winter", "harvest", "flood", "old"]))
            throw new InvalidOperationException("Saves must be grouped by branch, newest branch and newest point first: " +
                string.Join(", ", ordered));
        if (!IsLatestInBranch(harvest, saves) || !IsLatestInBranch(winter, saves) ||
            IsLatestInBranch(flood, saves) || IsLatestInBranch(old, saves))
            throw new InvalidOperationException("Only the newest point of a branch may continue it.");
        if (BranchLabel(second) != "Branch 2" || BranchLabel(null) != "Earlier saves")
            throw new InvalidOperationException("Branch labels must name the branch, or saves from before branches.");

        // The middle save was deleted, and a recovery copy of the first point
        // was made most recently. Equal ticks and creation dates cannot order history.
        ManualWorldSave pausedFirst = new("paused-first", "Before changing Jev", start.AddMinutes(3), 10,
            false, first, BranchPosition: 1);
        ManualWorldSave pausedLater = new("paused-later", "Jev off", start.AddMinutes(2), 10,
            false, first, "deleted-middle", start.AddMinutes(1), BranchPosition: 3);
        ManualWorldSave[] retainedPausedSaves = [pausedFirst, pausedLater];
        if (IsLatestInBranch(pausedFirst, retainedPausedSaves) || !IsLatestInBranch(pausedLater, retainedPausedSaves) ||
            !OrderSavesByBranch(retainedPausedSaves).Select(save => save.Id).SequenceEqual(["paused-later", "paused-first"]))
            throw new InvalidOperationException("Paused history must retain its latest position after an intermediate save is deleted.");

        allListedManualSaves = saves;
        listedManualSaves = OrderSavesByBranch(saves);
        RenderManualSaveList();
        var winterCard = manualSaveList.GetItemTitle(0);
        if (manualSaveList.ItemCount != 4 || winterCard != "Hungry winter")
            throw new InvalidOperationException("The save list must show every branch's saves.");
        allListedManualSaves = [];
        listedManualSaves = [];
        manualSaveList.Clear();
    }

    private async Task VerifyManualSaveListOwnershipAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            // A freshly paired launch has its catalog, but has not entered a
            // world or accepted an observation. Exercise both real buttons and
            // the signed catalog/save-list transport before any baseline.
            observationSession.ReplaceRegistration(registration);
            host.Catalog = WorldActionSmokeCatalog(["A", "B"]);
            host.ManualSaves =
            [new("title-named", "Title save", DateTimeOffset.UnixEpoch, 0),
             new("title-auto", "Autosave", DateTimeOffset.UnixEpoch.AddMinutes(1), 1, true)];
            OpenWorldMenu(create: false);
            await RefreshWorldListAsync();
            ChooseWorldActionSmokeRow(0);
            if (worldSavesButton.Disabled)
                throw new InvalidOperationException("A fresh title must offer its active catalog world's saves.");
            worldSavesButton.EmitSignal(BaseButton.SignalName.Pressed);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            while (listedManualSaves.Length != 2 && System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (observationSession.Current is not null || listedManualSaves.Length != 2 || !manualSaveOverlay.Visible)
                throw new InvalidOperationException("A fresh title must list the current world's saves without entering it.");
            for (var row = 0; row < listedManualSaves.Length; row++)
            {
                manualSaveList.Select(row);
                manualSaveList.EmitSignal(SlotList.SignalName.ItemSelected, (long)row);
                var save = listedManualSaves[row];
                if (manualSaveDeleteButton.Disabled)
                    throw new InvalidOperationException("A selected title-screen save must offer Delete Save.");
                manualSaveDeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (!deletionConfirmation.Visible || pendingDeletion is not { Kind: "save", WorldId: "world-A" } deletion ||
                    deletion.Id != save.Id || deletion.ExpectedCreatedUtc != save.CreatedUtc)
                    throw new InvalidOperationException("Delete Save on a fresh title must confirm the exact save and catalog world identity.");
                deletionConfirmation.Hide();
                pendingDeletion = null;
            }
            if (host.DeleteCount != 0)
                throw new InvalidOperationException("Opening and dismissing confirmation must not delete any save.");
            manualSaveOverlay.Hide();
            host.ManualSaves = null;
            var staleTitleSaves = new TaskCompletionSource<ManualWorldSave[]>();
            var oldTitleOpening = OpenManualSavesAsync(true, _ => staleTitleSaves.Task);
            host.Catalog = new("B", WorldActionSmokeCatalog(["A", "B"]).Worlds);
            await RefreshWorldListAsync();
            staleTitleSaves.SetResult([new("stale-title", "Earlier world save", DateTimeOffset.UnixEpoch, 0)]);
            await oldTitleOpening;
            if (listedManualSaves.Length != 0 || !manualSaveDeleteButton.Disabled || observationSession.Current is not null)
                throw new InvalidOperationException("A late title-screen save list must not publish after its catalog world changes.");
            manualSaveOverlay.Hide();
            await worldListRequest.RefreshAsync(_ => Task.FromException<WorldCatalogSnapshot>(
                new IOException("Controlled unavailable catalog.")));
            await OpenManualSavesAsync(true, _ => Task.FromResult<ManualWorldSave[]>(
                [new("unbound", "Unbound save", DateTimeOffset.UnixEpoch, 0)]));
            manualSaveList.Select(0);
            manualSaveList.EmitSignal(SlotList.SignalName.ItemSelected, 0L);
            if (!manualSaveDeleteButton.Disabled || listedSaveWorldId is not null)
                throw new InvalidOperationException("Delete Save must stay disabled without a known active world identity.");
            manualSaveOverlay.Hide();
            var handshake = new OwnerWorldHandshake(new(1, 1),
                ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                 "owner-control.request.v1", "paused-authoring.request.v1"], []);
            void AcceptWorld(string id)
            {
                observationSession.ResetAfterLoad();
                var snapshot = new OwnerWorldSnapshot(id, 0, "save-list-map", [new(0, 0, "meadow")], [], [], null, 0)
                {
                    Authoring = new(true, 0, 0, 0, "save-list-map", "save-list-map", "clear", "spring", []),
                };
                if (!observationSession.TryAccept(new(handshake, new(snapshot, new(0, 0, []))), 0, out var failure))
                    throw new InvalidOperationException("Save-list observation fixture was refused: " + failure);
            }
            ManualWorldSave[] oldSaves = [new("old-save", "Old save", DateTimeOffset.UnixEpoch, 0)];
            ManualWorldSave[] currentSaves = [new("new-save", "New current save", DateTimeOffset.UnixEpoch, 0), oldSaves[0]];
            AcceptWorld("world-A");
            host.Catalog = WorldActionSmokeCatalog(["A", "B"]);
            OpenWorldMenu(create: false);
            await RefreshWorldListAsync();
            ChooseWorldActionSmokeRow(1);
            if (!worldSavesButton.Visible || !worldSavesButton.Disabled)
                throw new InvalidOperationException("Another world's saves must wait until that world is opened.");
            ChooseWorldActionSmokeRow(0);
            if (worldSavesButton.Disabled)
                throw new InvalidOperationException("Load World must offer the current world's saves.");
            // Use the real button path. Escape must close the upper panel even
            // while its signed save-list read is pending, keeping Load World below.
            worldSavesButton.GrabFocus();
            worldSavesButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!manualSaveOverlay.Visible || !manualSaveLoadMode || !worldMenuOverlay.Visible)
                throw new InvalidOperationException("Load a save must open Load Save above Load World.");
            if (!HandleEscape() || manualSaveOverlay.Visible || !worldMenuOverlay.Visible ||
                worldSelectionList.GetSelectedItems() is not [0])
                throw new InvalidOperationException("Escape from Load Save must return to the same Load World selection.");
            worldSavesButton.GrabFocus();
            worldSavesButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!manualSaveOverlay.Visible || manualSaveBackButton.TooltipText != "Back to Load World")
                throw new InvalidOperationException("The save chooser must reopen with its Load World back action.");
            manualSaveBackButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (manualSaveOverlay.Visible || !worldMenuOverlay.Visible ||
                worldSelectionList.GetSelectedItems() is not [0])
                throw new InvalidOperationException("Back from a reopened Load Save must retain Load World and its selection.");
            if (!HandleEscape() || worldMenuOverlay.Visible)
                throw new InvalidOperationException("Escape from Load World must then close that parent panel.");
            foreach (var scenario in new[] { "create", "back", "late-failure" })
            {
                AcceptWorld("save-world-A");
                var delayed = new TaskCompletionSource<ManualWorldSave[]>();
                CancellationToken oldToken = default;
                // Ignore cancellation deliberately: transport completion can race
                // cancellation, and only the current opening may publish either outcome.
                var opening = OpenManualSavesAsync(false, token => { oldToken = token; return delayed.Task; });
                if (!manualSaveOverlay.Visible || manualSaveCreateButton.Disabled || !manualSaveOverwriteButton.Disabled)
                    throw new InvalidOperationException("Creating a save must stay usable while its list is slow.");
                if (!manualSaveNewBox.Visible || manualSaveName.Text != DisplayWorldClock(0) || manualSaveList.Placeholder != "Checking saves...")
                    throw new InvalidOperationException($"A new save must open named after the world's date while the list loads: {manualSaveName.Text}.");
                if (scenario == "create")
                {
                    manualSaveName.Text = "New current save";
                    await CreateManualSaveAsync();
                    if (host.SaveCreateCount != 1 || manualSaveOverlay.Visible)
                        throw new InvalidOperationException("The signed create must close its save dialog after success.");
                }
                else manualSaveBackButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (!oldToken.IsCancellationRequested)
                    throw new InvalidOperationException("Closing Save World must cancel its pending list.");
                await OpenManualSavesAsync(false, _ => Task.FromResult(currentSaves));
                manualSaveList.Select(0);
                manualSaveList.EmitSignal(SlotList.SignalName.ItemSelected, 0L);
                manualSaveOverwriteButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (!manualSaveOverwriteConfirmation.Visible || pendingOverwriteSaveId != "new-save")
                    throw new InvalidOperationException("The current selected save must open Overwrite confirmation.");
                manualSaveOverwriteConfirmation.Hide();
                var currentStatus = manualSaveStatus.Text;
                if (scenario == "late-failure") delayed.SetException(new IOException("Controlled stale save-list failure."));
                else delayed.SetResult(oldSaves);
                await opening;
                if (manualSaveList.ItemCount != 2 || listedManualSaves[0].Id != "new-save" ||
                    manualSaveList.GetItemTitle(0) != "New current save" ||
                    manualSaveList.GetSelectedItems() is not [0] || manualSaveOverwriteButton.Disabled ||
                    manualSaveDeleteButton.Disabled || manualSaveStatus.Text != currentStatus)
                    throw new InvalidOperationException("An earlier save-list reply must not replace the current rows, selection or status.");
                manualSaveOverwriteButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (!manualSaveOverwriteConfirmation.Visible || pendingOverwriteSaveId != "new-save")
                    throw new InvalidOperationException("Overwrite must still confirm the current selected save after a stale reply.");
                manualSaveOverwriteConfirmation.Hide();
                manualSaveList.Clear();
                RefreshControlAvailability();
                if (!manualSaveLoadButton.Disabled || !manualSaveOverwriteButton.Disabled || !manualSaveDeleteButton.Disabled)
                    throw new InvalidOperationException("Clearing save rows must disable every selected-save action.");
                manualSaveOverlay.Hide();
            }
            AcceptWorld("save-world-A");
            var oldWorldReply = new TaskCompletionSource<ManualWorldSave[]>();
            var oldWorldOpening = OpenManualSavesAsync(false, _ => oldWorldReply.Task);
            AcceptWorld("save-world-B");
            oldWorldReply.SetResult(oldSaves);
            await oldWorldOpening;
            if (manualSaveList.ItemCount != 0 || !manualSaveOverwriteButton.Disabled)
                throw new InvalidOperationException("A previous world's save list must not publish after changing worlds.");
            AcceptWorld("save-world-A");
            foreach (var loadMode in new[] { false, true })
            {
                await OpenManualSavesAsync(loadMode, _ => Task.FromException<ManualWorldSave[]>(
                    new IOException("Controlled current save-list failure.")));
                if (manualSaveList.Placeholder != "No saves to show." ||
                    !manualSaveStatus.Text.StartsWith("Could not list saves:", StringComparison.Ordinal) ||
                    !manualSaveLoadButton.Disabled || !manualSaveOverwriteButton.Disabled ||
                    !manualSaveDeleteButton.Disabled || manualSaveCreateButton.Disabled != loadMode)
                    throw new InvalidOperationException("A failed save-list read must end its checking placeholder and keep only creating a new save available.");
                manualSaveOverlay.Hide();
            }
        }
        finally
        {
            worldListRequest.Cancel();
            worldMenuOverlay.Hide();
            listedWorlds = [];
            listedActiveWorldId = null;
            worldSelectionList.Clear();
            manualSaveOverlay.Hide();
            manualSaveOverwriteConfirmation.Hide();
            pendingOverwriteSaveId = null;
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            System.Environment.SetEnvironmentVariable("CI", previousCi);
        }
    }

}
