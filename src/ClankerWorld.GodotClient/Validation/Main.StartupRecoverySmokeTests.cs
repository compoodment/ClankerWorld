using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyStartupRecoveryAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousInWorld = isInWorld;
        var previousResume = resumeWorldOnContinue;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        var save = new ManualWorldSave(new string('a', 32), "Autosave", DateTimeOffset.UnixEpoch, 0, true);
        var snapshot = new OwnerWorldSnapshot("recovery-world", 0, "recovery-map", [new(0, 0, "meadow")], [], [], null, 0)
        {
            Authoring = new(true, 0, 0, 0, "recovery-map", "recovery-map", "clear", "spring", []),
        };
        host.Reconnect = new(new(new(1, 1), ["owner-observation.read.v1", "inhabitant-inspection.read.v1",
            "spatial-knowledge.read.v1", "owner-control.request.v1", "paused-authoring.request.v1"], []),
            new(snapshot, new(0, 0, [])));
        int Requests(string path) => host.Requests.Count(request => request == path);
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            ShowMainMenu();
            resumeWorldOnContinue = true;
            host.StartupRecovery = new(true, snapshot.WorldId, save);
            await EnterWorldAsync();
            for (var frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!startupRecoveryCard.IsVisibleInTree() || mainMenuCard.Visible || mainMenuLogo.Visible || isInWorld ||
                resumeWorldOnContinue || startupRecoveryButton.Disabled ||
                !startupRecoveryExplanation.Text.Contains("other saves are unchanged", StringComparison.Ordinal) ||
                !startupRecoveryExplanation.Text.Contains("damaged file is kept", StringComparison.Ordinal) ||
                !startupRecoverySave.Text.Contains("Progress since this autosave", StringComparison.Ordinal) ||
                Requests(OwnerPairingEndpoints.OwnerReconnect) != 0 || Requests(OwnerPairingEndpoints.OwnerResume) != 0)
                throw new InvalidOperationException("Recovery must precede world entry, explain the retained files and lost later progress, and offer an explicit autosave choice.");
            var bounds = startupRecoveryCard.GetGlobalRect();
            if (!GetViewportRect().Encloses(bounds))
                throw new InvalidOperationException($"The recovery card must fit the viewport: {bounds}.");

            host.StartupRecovery = new(true, snapshot.WorldId, null);
            await CheckStartupRecoveryAsync();
            await RecoverStartupAutosaveAsync();
            if (!startupRecoveryButton.Disabled || !startupRecoverySave.Text.Contains("No usable autosave", StringComparison.Ordinal) ||
                Requests("/api/v1/owner/recovery/restore") != 0)
                throw new InvalidOperationException("No autosave must leave the recovery explanation and quit/check controls usable without fabricating a recovered world.");

            host.StartupRecovery = new(true, snapshot.WorldId, save);
            await CheckStartupRecoveryAsync();
            host.ReleaseRecovery = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var recovering = RecoverStartupAutosaveAsync();
            await host.RecoveryReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await RecoverStartupAutosaveAsync();
            if (!startupRecoveryButton.Disabled || !startupRecoveryRetry.Disabled)
                throw new InvalidOperationException("A pending recovery must guard both action and retry controls.");
            host.ReleaseRecovery!.TrySetResult();
            await recovering;
            if (startupRecoveryCard.Visible || mainMenuOverlay.Visible || !isInWorld ||
                observationSession.Current?.Baseline.Snapshot.WorldId != snapshot.WorldId ||
                observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused != true ||
                Requests("/api/v1/owner/recovery/restore") != 1 || Requests(OwnerPairingEndpoints.OwnerResume) != 0)
                throw new InvalidOperationException("Recovery must send one signed choice and enter its fresh paused baseline without resuming it.");

            ShowMainMenu();
            host.StartupRecovery = new(true, snapshot.WorldId, save);
            await CheckStartupRecoveryAsync();
            host.LoseRecoveryReply = true;
            await RecoverStartupAutosaveAsync();
            if (!startupRecoveryCard.Visible || !startupRecoveryButton.Disabled ||
                !startupRecoverySave.Text.Contains("Could not confirm recovery", StringComparison.Ordinal))
                throw new InvalidOperationException("A lost recovery receipt must keep a check-again explanation and prevent an automatic repeat.");
            await CheckStartupRecoveryAsync();
            if (startupRecoveryCard.Visible || !mainMenuCard.IsVisibleInTree() ||
                Requests("/api/v1/owner/recovery/restore") != 2)
                throw new InvalidOperationException("Check again must recognize a completed recovery without submitting it again.");
        }
        finally
        {
            host.ReleaseRecovery?.TrySetResult();
            startupRecovery = null;
            startupRecoveryBusy = false;
            startupRecoveryCard.Hide();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null &&
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _))
            {
                Render(previousObservation.Baseline.Snapshot, []);
            }
            ShowMainMenu();
            isInWorld = previousInWorld;
            resumeWorldOnContinue = previousResume;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
        }
    }
}
