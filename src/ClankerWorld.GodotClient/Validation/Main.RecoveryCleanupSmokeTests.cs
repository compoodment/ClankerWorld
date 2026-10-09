using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyRecoveryCleanupAsync()
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
            observationSession.ReplaceRegistration(registration);
            var snapshot = new OwnerWorldSnapshot("cleanup-world", 0, "cleanup-map", [new(0, 0, "meadow")], [], [], null, 0)
            { Authoring = new(true, 0, 0, 0, "cleanup-map", "cleanup-map", "clear", "spring", []) };
            var handshake = new OwnerWorldHandshake(new(1, 1),
                ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                 "owner-control.request.v1", "paused-authoring.request.v1"], []);
            var reconnect = new OwnerWorldReconnect(handshake, new(snapshot, new(0, 0, [])));
            if (!observationSession.TryAccept(reconnect, 0, out var failure))
                throw new InvalidOperationException("Cleanup fixture refused: " + failure);
            host.Reconnect = reconnect;
            ManualWorldSave old = new("older-copy", "Before overwriting: First", DateTimeOffset.UnixEpoch, 1);
            ManualWorldSave keep = new("named-save", "First", DateTimeOffset.UnixEpoch, 2);
            host.ManualSaves = [old, keep];
            host.RecoveryPreview = new("cleanup-world", 3, new string('a', 64), [old], [keep]);
            await RefreshAsync();
            await OpenManualSavesAsync(false);
            if (!host.RecoveryCleanupRequests.IsEmpty || recoveryCleanupButton.Disabled)
                throw new InvalidOperationException("Opening Save World must offer cleanup without running it.");
            recoveryCleanupButton.EmitSignal(BaseButton.SignalName.Pressed);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            while (!recoveryCleanupConfirmation.Visible && System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!recoveryCleanupConfirmation.Visible || pendingRecoveryCleanup?.Digest != host.RecoveryPreview.Digest ||
                !recoveryCleanupSummary.Text.Contains(old.Id, StringComparison.Ordinal) ||
                !recoveryCleanupSummary.Text.Contains(keep.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Cleanup must preview exact removed and kept copies before confirmation.");
            if (isOwnerAction && !recoveryCleanupConfirmation.GetOkButton().Disabled)
                throw new InvalidOperationException("Cleanup confirmation must wait until its preview action finishes.");
            recoveryCleanupConfirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            recoveryCleanupConfirmation.Hide();
            await CleanConfirmedRecoveryHistoryAsync();
            if (pendingRecoveryCleanup is not null || host.RecoveryCleanupRequests.Any(action => action.Operation == "apply"))
                throw new InvalidOperationException("Cancelling the preview must never send cleanup.");
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            while (isOwnerAction && System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await PreviewRecoveryCleanupAsync();
            if (recoveryCleanupConfirmation.GetOkButton().Disabled || pendingRecoveryCleanup is null)
                throw new InvalidOperationException($"A completed preview must enable explicit confirmation: " +
                    $"pending={pendingRecoveryCleanup?.Digest}, busy={isOwnerAction}, panel={manualSaveOverlay.Visible}.");
            recoveryCleanupConfirmation.Hide();
            await CleanConfirmedRecoveryHistoryAsync();
            var applied = host.RecoveryCleanupRequests.Single(action => action.Operation == "apply");
            if (applied.ExpectedDigest != host.RecoveryPreview.Digest || applied.WorldId != "cleanup-world" ||
                applied.KeepCount != 3 || listedManualSaves.Any(save => save.Id == old.Id))
                throw new InvalidOperationException("Confirmed cleanup must bind the exact preview and refresh remaining saves.");
            ShowRecoveryCleanupPreview(host.RecoveryPreview with { Remove = [] });
            if (!recoveryCleanupConfirmation.GetOkButton().Disabled || pendingRecoveryCleanup is not null)
                throw new InvalidOperationException("An empty preview must never offer deletion.");
        }
        finally
        {
            manualSaveOverlay.Hide();
            recoveryCleanupConfirmation.Hide();
            pendingRecoveryCleanup = null;
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
