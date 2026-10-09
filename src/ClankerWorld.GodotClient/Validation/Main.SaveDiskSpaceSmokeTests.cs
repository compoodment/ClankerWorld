using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifySaveDiskSpaceWarningAsync()
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
            var handshake = new OwnerWorldHandshake(new(1, 1),
                ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                 "owner-control.request.v1", "paused-authoring.request.v1", "owner-observation-timeline.v1"], []);
            var snapshot = new OwnerWorldSnapshot("disk-warning-world", 0, "disk-warning-map", [new(0, 0, "meadow")], [], [], null, 0)
            {
                Authoring = new(true, 0, 0, 0, "disk-warning-map", "disk-warning-map", "clear", "spring", []),
            };
            host.Reconnect = new(handshake, new(snapshot, new(0, 0, []), new("disk-warning-host", 1)));
            host.ManualSaves = [];
            if (!observationSession.TryAccept(host.Reconnect, 0, out var failure))
                throw new InvalidOperationException("Disk-warning observation was refused: " + failure);
            host.DiskSpace = new("low", 0, 1024L * 1024 * 1024, DateTimeOffset.UtcNow);
            await RefreshSaveDiskSpaceAsync(force: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!saveDiskWarningPanel.IsVisibleInTree() || !saveDiskWarningLabel.Text.Contains("Make room soon", StringComparison.Ordinal))
                throw new InvalidOperationException($"Low server disk space must remain visible while autosaves run: visible={saveDiskWarningPanel.Visible}, inTree={saveDiskWarningPanel.IsVisibleInTree()}, text={saveDiskWarningLabel.Text}, requests={string.Join(',', host.Requests)}.");
            var viewport = GetViewportRect();
            var warning = saveDiskWarningPanel.GetGlobalRect();
            if (!viewport.Encloses(warning) || saveDiskWarningPanel.MouseFilter != MouseFilterEnum.Ignore ||
                saveDiskWarningPanel.FindChildren("*", nameof(Control), true, false).OfType<Control>()
                    .Any(child => child.MouseFilter != MouseFilterEnum.Ignore))
                throw new InvalidOperationException("The disk warning must fit and leave controls usable.");
            await OpenManualSavesAsync(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!manualSaveDiskWarning.IsVisibleInTree() || saveDiskWarningPanel.Visible || manualSaveCreateButton.Disabled)
                throw new InvalidOperationException("Save World must show the advisory and continue offering Save.");
            manualSaveName.Text = "Still save with low space";
            await CreateManualSaveAsync();
            if (host.SaveCreateCount != 1 || manualSaveOverlay.Visible || !saveDiskWarningPanel.Visible)
                throw new InvalidOperationException("Low space must not stop a manual save or hide the continuing warning.");
            host.DiskSpace = host.DiskSpace with { State = "ok", AvailableBytes = 4L * 1024 * 1024 * 1024 };
            await RefreshSaveDiskSpaceAsync(force: true);
            if (saveDiskWarningPanel.Visible || manualSaveDiskWarning.Visible)
                throw new InvalidOperationException("The disk warning must clear after free space recovers.");
            host.DiskSpace = host.DiskSpace with { State = "unknown", AvailableBytes = null };
            await RefreshSaveDiskSpaceAsync(force: true);
            if (!saveDiskWarningLabel.Text.Contains("could not be checked", StringComparison.Ordinal))
                throw new InvalidOperationException("An unknown disk assessment must not imply that space is sufficient.");

            observationSession.ResetAfterLoad();
            if (!observationSession.AwaitingFreshBaseline || TryGetOwner(out _, out _, out _))
                throw new InvalidOperationException("The disk check must leave world actions gated during baseline recovery.");
            host.DiskSpace = host.DiskSpace with { State = "low", AvailableBytes = 0 };
            await RefreshSaveDiskSpaceAsync(force: true);
            if (!saveDiskWarningLabel.Text.Contains("Make room soon", StringComparison.Ordinal))
                throw new InvalidOperationException("The signed installation advisory must refresh while the world baseline is unavailable.");
            host.FailDiskSpace = true;
            await RefreshSaveDiskSpaceAsync(force: true);
            if (!saveDiskWarningLabel.Text.Contains("could not be checked", StringComparison.Ordinal) ||
                !observationSession.AwaitingFreshBaseline || TryGetOwner(out _, out _, out _))
                throw new InvalidOperationException("A failed advisory must become unknown without opening world actions.");
            host.FailDiskSpace = false;
            if (!observationSession.TryAccept(host.Reconnect, 0, out failure))
                throw new InvalidOperationException("The disk check's fresh baseline was refused: " + failure);
            await OpenManualSavesAsync(false);
            host.FailDiskSpace = true;
            manualSaveName.Text = "Save despite unavailable advisory";
            await CreateManualSaveAsync();
            if (host.SaveCreateCount != 2 || manualSaveOverlay.Visible)
                throw new InvalidOperationException("An unavailable advisory must still allow the signed manual save.");
        }
        finally
        {
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ReplaceRegistration(previousRegistration);
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            manualSaveOverlay.Hide();
            RenderSaveDiskSpace(new("ok", null, 0, null));
            System.Environment.SetEnvironmentVariable("CI", previousCi);
        }
    }
}
