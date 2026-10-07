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
            foreach (var (initiallyPaused, delayResume, reopenAgain, switchWorld) in new[]
                { (false, false, false, false), (true, false, false, false), (false, true, false, false),
                  (false, true, true, false), (false, true, false, true) })
            {
                using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
                using var nextHost = switchWorld ? new WorldActionSmokeHost(signer.PublicKeySpkiBase64) : null;
                var handshake = new OwnerWorldHandshake(new(1, 1),
                    ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                     "owner-control.request.v1", "paused-authoring.request.v1"], []);
                var snapshot = new OwnerWorldSnapshot("pause-menu-world", 0, "pause-menu-map", [new(0, 0, "meadow")],
                    [], [], null, 0)
                {
                    FounderSetup = new(4, 4, true),
                    Authoring = new(initiallyPaused, 0, 0, 0, "pause-menu-map", "pause-menu-map", "clear", "spring", []),
                };
                OwnerWorldReconnect Reply(bool paused, string? worldId = null) => new(handshake, new(snapshot with
                {
                    WorldId = worldId ?? snapshot.WorldId,
                    Authoring = snapshot.Authoring! with { IsPaused = paused },
                }, new(0, 0, [])));
                var resumeReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseResume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var resumes = 0;
                var pausedAtHost = initiallyPaused;
                var nextResumes = 0;
                var nextPaused = true;
                if (nextHost is not null)
                {
                    nextHost.Reconnect = Reply(true, "next-pause-menu-world");
                    nextHost.ControlHandler = paused =>
                    {
                        if (!paused) nextResumes++;
                        nextPaused = paused;
                        nextHost.Reconnect = Reply(paused, "next-pause-menu-world");
                        return Task.FromResult(new OwnerControlReceipt(paused ? "pause" : "resume", true, paused, 0, 0, 0));
                    };
                }
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
                Task? secondClosing = null;
                Task? thirdOpening = null;
                try
                {
                    await ToggleGameMenuAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    if (!gameMenuPanel.Visible || !pausedAtHost || menuPausedWorld == initiallyPaused)
                        throw new InvalidOperationException("Opening the first menu must pause and remember whether the world was running.");
                    firstClosing = CloseGameMenuAsync();
                    if (delayResume)
                    {
                        await resumeReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        if (nextHost is not null)
                        {
                            registration = new(nextHost.Authority, "smoke-device", signer.PublicKeyFingerprint, nextHost.Address);
                            worldUrlInput.Text = nextHost.Address;
                            await RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
                            if (observationSession.Current?.Baseline.Snapshot.WorldId != "next-pause-menu-world")
                                throw new InvalidOperationException("The next deliberately paused world must be displayed before opening its menu.");
                        }
                        secondOpening = ToggleGameMenuAsync();
                        if (!gameMenuPanel.Visible || host.PauseCount != 1)
                            throw new InvalidOperationException("The reopened menu must queue its pause behind the held Resume.");
                        if (reopenAgain)
                        {
                            secondClosing = CloseGameMenuAsync();
                            thirdOpening = ToggleGameMenuAsync();
                            if (!gameMenuPanel.Visible || host.PauseCount != 1)
                                throw new InvalidOperationException("A third menu must also wait behind the held Resume.");
                        }
                        releaseResume.TrySetResult();
                        await Task.WhenAll(firstClosing, secondOpening).WaitAsync(TimeSpan.FromSeconds(5));
                        if (secondClosing is not null && thirdOpening is not null)
                            await Task.WhenAll(secondClosing, thirdOpening).WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    else
                    {
                        await firstClosing.WaitAsync(TimeSpan.FromSeconds(5));
                        secondOpening = ToggleGameMenuAsync();
                        await secondOpening.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    await CloseGameMenuAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    if (nextHost is not null)
                    {
                        if (gameMenuPanel.Visible || !nextPaused || nextHost.PauseCount != 1 || nextResumes != 0 ||
                            host.PauseCount != 1 || resumes != 1 ||
                            observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused != true)
                            throw new InvalidOperationException("A pending Resume from the old world must not resume the next deliberately paused world.");
                        continue;
                    }
                    var menuCount = reopenAgain ? 3 : 2;
                    if (gameMenuPanel.Visible || pausedAtHost != initiallyPaused || host.PauseCount != menuCount ||
                        resumes != (initiallyPaused ? 0 : menuCount) ||
                        observationSession.Current?.Baseline.Snapshot.Authoring?.IsPaused != initiallyPaused)
                        throw new InvalidOperationException($"Closing both menus must restore the initial pause state: delayed={delayResume}, initiallyPaused={initiallyPaused}, finalPaused={pausedAtHost}, pauses={host.PauseCount}, resumes={resumes}.");
                }
                finally
                {
                    releaseResume.TrySetResult();
                    if (firstClosing is not null) await firstClosing.WaitAsync(TimeSpan.FromSeconds(5));
                    if (secondOpening is not null) await secondOpening.WaitAsync(TimeSpan.FromSeconds(5));
                    if (secondClosing is not null) await secondClosing.WaitAsync(TimeSpan.FromSeconds(5));
                    if (thirdOpening is not null) await thirdOpening.WaitAsync(TimeSpan.FromSeconds(5));
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
