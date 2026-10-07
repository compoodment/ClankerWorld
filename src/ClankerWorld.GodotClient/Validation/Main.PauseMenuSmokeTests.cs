using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyPauseMenuResumeAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousInWorld = isInWorld;
        var previousMainMenu = mainMenuOverlay.Visible;
        var previousReturnToMainMenu = returnToMainMenu;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        try
        {
            // Check the ordinary running and deliberate-pause controls before the delayed case.
            foreach (var (initiallyPaused, delayResume) in new[] { (false, false), (true, false), (false, true) })
            {
                using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
                var handshake = new OwnerWorldHandshake(new(1, 1),
                    ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                     "owner-control.request.v1", "paused-authoring.request.v1"], []);
                var snapshot = new OwnerWorldSnapshot("pause-menu-world", 0, "pause-menu-map", [new(0, 0, "meadow")],
                    [], [], null, 0)
                {
                    FounderSetup = new(4, 4, true),
                    Authoring = new(initiallyPaused, 0, 0, 0, "pause-menu-map", "pause-menu-map", "clear", "spring", []),
                };
                OwnerWorldReconnect Reply(bool paused) => new(handshake, new(snapshot with
                {
                    Authoring = snapshot.Authoring! with { IsPaused = paused },
                }, new(0, 0, [])));
                var resumeReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseResume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var resumes = 0;
                var pausedAtHost = initiallyPaused;
                host.Reconnect = Reply(initiallyPaused);
                host.ControlHandler = async paused =>
                {
                    if (!paused && Interlocked.Increment(ref resumes) == 1 && delayResume)
                    {
                        resumeReceived.TrySetResult();
                        await releaseResume.Task.ConfigureAwait(false);
                    }
                    pausedAtHost = paused;
                    host.Reconnect = Reply(paused);
                    return new OwnerControlReceipt(paused ? "pause" : "resume", true, paused, 0, 0, 0);
                };
                registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
                deviceKey = signer;
                worldUrlInput.Text = host.Address;
                observationSession.ResetAfterLoad();
                isInWorld = true;
                mainMenuOverlay.Hide();
                returnToMainMenu = false;
                CloseGameMenu();
                await RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
                if (observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused != initiallyPaused)
                    throw new InvalidOperationException("The pause-menu fixture must first display its signed initial observation.");
                Task? firstClosing = null;
                Task? secondOpening = null;
                try
                {
                    await ToggleGameMenuAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    if (!gameMenuPanel.Visible || !pausedAtHost || menuPausedWorld == initiallyPaused)
                        throw new InvalidOperationException("Opening the first menu must pause and remember whether the world was running.");
                    firstClosing = CloseGameMenuAsync();
                    if (delayResume)
                    {
                        await resumeReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        secondOpening = ToggleGameMenuAsync();
                        if (!gameMenuPanel.Visible || host.PauseCount != 1)
                            throw new InvalidOperationException("The reopened menu must queue its pause behind the held Resume.");
                        releaseResume.TrySetResult();
                        await Task.WhenAll(firstClosing, secondOpening).WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    else
                    {
                        await firstClosing.WaitAsync(TimeSpan.FromSeconds(5));
                        secondOpening = ToggleGameMenuAsync();
                        await secondOpening.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    await CloseGameMenuAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    if (gameMenuPanel.Visible || pausedAtHost != initiallyPaused || host.PauseCount != 2 ||
                        resumes != (initiallyPaused ? 0 : 2) ||
                        observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused != initiallyPaused)
                        throw new InvalidOperationException($"Closing both menus must restore the initial pause state: delayed={delayResume}, initiallyPaused={initiallyPaused}, finalPaused={pausedAtHost}, pauses={host.PauseCount}, resumes={resumes}.");
                }
                finally
                {
                    releaseResume.TrySetResult();
                    if (firstClosing is not null) await firstClosing.WaitAsync(TimeSpan.FromSeconds(5));
                    if (secondOpening is not null) await secondOpening.WaitAsync(TimeSpan.FromSeconds(5));
                    CloseGameMenu();
                }
            }
        }
        finally
        {
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            isInWorld = previousInWorld;
            mainMenuOverlay.Visible = previousMainMenu;
            returnToMainMenu = previousReturnToMainMenu;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            RefreshMainMenuAvailability();
            statusToast.Hide();
        }
    }
}
