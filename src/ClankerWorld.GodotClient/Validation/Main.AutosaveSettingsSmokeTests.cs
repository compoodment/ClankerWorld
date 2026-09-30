using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyAutosaveSettingsOwnershipAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        var previousMenuVisible = gameMenuPanel.Visible;
        var previousSettingsVisible = settingsPanel.Visible;
        var previousWorldSettingsVisible = worldSettingsContent.Visible;
        var previousGameSettingsVisible = gameSettingsContent.Visible;
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            gameMenuPanel.Show();
            settingsPanel.Show();
            gameSettingsContent.Hide();
            worldSettingsContent.Show();
            var handshake = new OwnerWorldHandshake(new(1, 1),
                ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                 "owner-control.request.v1", "paused-authoring.request.v1"], []);
            void AcceptWorld(string id)
            {
                observationSession.ResetAfterLoad();
                var snapshot = new OwnerWorldSnapshot(id, 0, "autosave-map", [new(0, 0, "meadow")], [], [], null, 0)
                {
                    Authoring = new(true, 0, 0, 0, "autosave-map", "autosave-map", "clear", "spring", []),
                };
                if (!observationSession.TryAccept(new(handshake, new(snapshot, new(0, 0, []))), 0, out var failure))
                    throw new InvalidOperationException("Autosave observation fixture was refused: " + failure);
                RefreshControlAvailability();
            }
            var settingsA = new WorldAutosaveSettings("autosave-world-A", true, 1, 3, DateTimeOffset.UnixEpoch, -1);
            var settingsB = new WorldAutosaveSettings("autosave-world-B", true, 10, 5, DateTimeOffset.UnixEpoch, -1);
            foreach (var scenario in new[] { "old-first", "old-last", "late-failure" })
            {
                AcceptWorld(settingsA.WorldId);
                var delayedA = new TaskCompletionSource<WorldAutosaveSettings>();
                CancellationToken tokenA = default;
                // Ignore cancellation deliberately to exercise a racing completion.
                var readA = RefreshAutosaveSettingsAsync(token => { tokenA = token; return delayedA.Task; });
                AcceptWorld(settingsB.WorldId);
                var delayedB = new TaskCompletionSource<WorldAutosaveSettings>();
                var readB = RefreshAutosaveSettingsAsync(_ => delayedB.Task);
                if (!tokenA.IsCancellationRequested || !autosaveApplyButton.Disabled)
                    throw new InvalidOperationException("Changing worlds must cancel the old autosave read and disable Apply.");
                if (scenario == "old-first")
                {
                    delayedA.SetResult(settingsA);
                    await readA;
                    if (autosaveSettingsLoaded || !autosaveApplyButton.Disabled)
                        throw new InvalidOperationException("An old autosave response must not enable Apply while the current world is loading.");
                }
                delayedB.SetResult(settingsB);
                await readB;
                var currentStatus = autosaveSettingsStatus.Text;
                if (scenario == "old-last") delayedA.SetResult(settingsA);
                if (scenario == "late-failure") delayedA.SetException(new IOException("Controlled old-world read failure."));
                await readA;
                if (!autosaveSettingsLoaded || autosaveApplyButton.Disabled ||
                    autosaveIntervalChoice.GetSelectedId() != 10 || autosaveRotationChoice.GetSelectedId() != 5 ||
                    autosaveSettingsStatus.Text != currentStatus)
                    throw new InvalidOperationException("An old world's autosave outcome must not replace the current world's controls or status.");
                var count = host.AutosaveConfigurations.Count;
                await ApplyAutosaveSettingsAsync();
                if (host.AutosaveConfigurations.Count != count + 1 ||
                    host.AutosaveConfigurations[^1] != new OwnerAutosaveConfigurationAction(true, 10, 5))
                    throw new InvalidOperationException("Signed Apply must send the displayed settings for the current world.");
            }
            var delayedOpening = new TaskCompletionSource<WorldAutosaveSettings>();
            CancellationToken openingToken = default;
            var opening = RefreshAutosaveSettingsAsync(token => { openingToken = token; return delayedOpening.Task; });
            settingsPanel.Hide();
            if (!openingToken.IsCancellationRequested || !autosaveApplyButton.Disabled)
                throw new InvalidOperationException("Closing Settings must cancel its autosave read and disable Apply.");
            settingsPanel.Show();
            await RefreshAutosaveSettingsAsync(_ => Task.FromResult(settingsB));
            delayedOpening.SetResult(settingsA with { WorldId = settingsB.WorldId });
            await opening;
            if (autosaveIntervalChoice.GetSelectedId() != 10 || autosaveApplyButton.Disabled)
                throw new InvalidOperationException("A reply from an earlier opening must not replace the reopened autosave settings.");

            var configureCount = host.AutosaveConfigurations.Count;
            // Apply checks context itself, even before the next availability refresh.
            observationSession.ResetAfterLoad();
            await ApplyAutosaveSettingsAsync();
            RefreshControlAvailability();
            if (host.AutosaveConfigurations.Count != configureCount || !autosaveApplyButton.Disabled)
                throw new InvalidOperationException("Settings retained after losing the world must not send an Apply request.");
            AcceptWorld(settingsB.WorldId);
            await RefreshAutosaveSettingsAsync(_ => Task.FromResult(settingsA));
            await ApplyAutosaveSettingsAsync();
            if (autosaveSettingsLoaded || !autosaveApplyButton.Disabled || host.AutosaveConfigurations.Count != configureCount)
                throw new InvalidOperationException("A reply naming a different world must leave Apply unavailable.");
        }
        finally
        {
            CancelAutosaveSettingsRead();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            gameMenuPanel.Visible = previousMenuVisible;
            settingsPanel.Visible = previousSettingsVisible;
            worldSettingsContent.Visible = previousWorldSettingsVisible;
            gameSettingsContent.Visible = previousGameSettingsVisible;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            statusToast.Hide();
        }
    }
}
