using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
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
                manualSaveList.EmitSignal(ItemList.SignalName.ItemSelected, 0L);
                manualSaveOverwriteButton.EmitSignal(BaseButton.SignalName.Pressed);
                if (!manualSaveOverwriteConfirmation.Visible || pendingOverwriteSaveId != "new-save")
                    throw new InvalidOperationException("The current selected save must open Overwrite confirmation.");
                manualSaveOverwriteConfirmation.Hide();
                var currentStatus = manualSaveStatus.Text;
                if (scenario == "late-failure") delayed.SetException(new IOException("Controlled stale save-list failure."));
                else delayed.SetResult(oldSaves);
                await opening;
                if (manualSaveList.ItemCount != 2 || listedManualSaves[0].Id != "new-save" ||
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
        }
        finally
        {
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
