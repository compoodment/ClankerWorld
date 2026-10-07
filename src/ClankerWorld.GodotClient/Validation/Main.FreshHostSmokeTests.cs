using System.Text.Json;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyPairingRecoveryMenuAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousPendingPairing = pendingPairing;
        var previousPairingOrigin = pendingPairingOrigin;
        var previousInWorld = isInWorld;
        var previousResume = resumeWorldOnContinue;
        var registrationPath = ProjectSettings.GlobalizePath("user://owner-device-registration.json");
        var previousRegistrationFile = File.Exists(registrationPath) ? File.ReadAllBytes(registrationPath) : null;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);
        host.PairingStart = new(host.Authority, "menu-pairing", "smoke-device", "fixture-code",
            signer.PublicKeyFingerprint, expiry, OwnerPairingProtocol.CreatePairingActivationCanonicalProof(
                host.Authority, "menu-pairing", "smoke-device", signer.PublicKeyFingerprint));
        var approved = new OwnerPairingStatus(host.Authority, "menu-pairing", "smoke-device",
            signer.PublicKeyFingerprint, OwnerPairingState.Approved, expiry);
        host.Reconnect = new(new(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []),
            new(new("paired-world", 0, "paired-map", [new(0, 0, "meadow")], [], [], null, 0), new(0, 0, [])));
        int Requests(string path) => host.Requests.Count(request => request == path);
        void AssertMenu()
        {
            if (registrationStore.TryLoad(signer.PublicKeyFingerprint) != registration || registration is null ||
                pendingPairing is not null || pendingPairingOrigin is not null || isInWorld ||
                !mainMenuOverlay.Visible || !mainMenuCard.IsVisibleInTree() || !mainMenuLogo.IsVisibleInTree() ||
                !mainMenuContinueButton.IsVisibleInTree() || mainMenuContinueButton.Disabled ||
                !mainMenuSettingsButton.IsVisibleInTree() || !quitGameButton.IsVisibleInTree() ||
                gameMenuPanel.Visible || pairingPanel.Visible)
                throw new InvalidOperationException("Successful pairing recovery must save registration and restore the Main Menu card, logo and usable Continue, Settings and Quit controls.");
        }
        try
        {
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            resumeWorldOnContinue = false;
            foreach (var loseReply in new[] { true, false })
            {
                registration = null;
                pendingPairing = null;
                registrationStore.Forget();
                observationSession.ResetAfterLoad();
                host.PairingStatus = approved;
                host.LoseActivationReply = loseReply;
                ShowMainMenu();
                await OpenMainMenuConnectionAsync();
                var reconnects = Requests(OwnerPairingEndpoints.OwnerReconnect);
                await PollPairingAsync();
                if (loseReply)
                {
                    if (registration is not null || pendingPairing is null || !gameMenuPanel.Visible)
                        throw new InvalidOperationException("A lost activation reply must leave the pairing available for recovery.");
                    await PollPairingAsync();
                }
                AssertMenu();
                if (Requests(OwnerPairingEndpoints.OwnerReconnect) != reconnects)
                    throw new InvalidOperationException("Pairing must wait for Continue before requesting the world, including after recovery.");
                mainMenuSettingsButton.EmitSignal(BaseButton.SignalName.Pressed);
                menuCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
                AssertMenu();
                // The same entry method used by Continue must perform a signed refresh and enter the world.
                await EnterWorldAsync();
                if (!isInWorld || mainMenuOverlay.Visible ||
                    observationSession.Current?.Baseline.Snapshot.WorldId != "paired-world" ||
                    Requests(OwnerPairingEndpoints.OwnerReconnect) != reconnects + 1)
                    throw new InvalidOperationException("Continue after pairing must enter the recovered world through signed HTTP.");
            }

            foreach (var mismatched in new[]
            {
                approved with { Authority = new("different-host", host.Authority.WorldId), State = OwnerPairingState.Active },
                approved with { DeviceId = "different-device", State = OwnerPairingState.Active },
                approved with { PublicKeyFingerprint = "different-key", State = OwnerPairingState.Active },
            })
            {
                registration = null;
                registrationStore.Forget();
                ShowMainMenu();
                await OpenMainMenuConnectionAsync();
                host.PairingStatus = mismatched;
                var activations = Requests(OwnerPairingEndpoints.PairingActivation);
                await PollPairingAsync();
                if (registration is not null || registrationStore.TryLoad(signer.PublicKeyFingerprint) is not null ||
                    pendingPairing is not null || !gameMenuPanel.Visible ||
                    Requests(OwnerPairingEndpoints.PairingActivation) != activations)
                    throw new InvalidOperationException("A mismatched active pairing must not save a registration or dismiss the connection screen.");
            }
            // Drain the normal two-frame pairing-panel reveal before restoring the surrounding fixture.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        finally
        {
            pendingPairing = previousPendingPairing;
            pendingPairingOrigin = previousPairingOrigin;
            registration = previousRegistration;
            if (previousRegistrationFile is null) registrationStore.Forget();
            else File.WriteAllBytes(registrationPath, previousRegistrationFile);
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            pairingPanel.Hide();
            settingsPanel.Hide();
            CloseGameMenu();
            ShowMainMenu();
            isInWorld = previousInWorld;
            resumeWorldOnContinue = previousResume;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            statusToast.Hide();
        }
    }

    /// <summary>Entering an untouched host starts the normal creation flow without exposing or resuming its old camp.</summary>
    private async Task VerifyFreshHostEntryAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousInWorld = isInWorld;
        var previousResume = resumeWorldOnContinue;
        var previousMenuVisible = mainMenuOverlay.Visible;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        var bootstrap = new OwnerWorldSnapshot("bootstrap", 0, "entry-map", [new(0, 0, "meadow")],
            [new("campfire", "cooking", new(0, 0))], [], null, 0)
        {
            FounderSetup = new(4, 0, false) { RequiresWorldCreation = true },
            Authoring = new(true, 0, 0, 0, "entry-map", "entry-map", "clear", "spring", []),
        };
        var generated = bootstrap with
        {
            WorldId = "generated",
            Objects = [],
            FounderSetup = new(4, 0, false) { CanChooseTownSite = true },
        };
        OwnerWorldReconnect Reply(OwnerWorldSnapshot snapshot) => new(handshake, new(snapshot, new(0, snapshot.WorldTick, [])));
        int Requests(string path) => host.Requests.Count(request => request == path);
        async Task WaitForPreviewAsync()
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (worldMenuBusy || previewedWorldResult is null)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                    throw new InvalidOperationException("Entering New World must finish its normal map preview.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            host.SupportedActionPayloads = [OwnerWorldActionPayload.WorldCreationPayloadDomain];
            host.Preview = new(new(1, 1, "terrain-kind-v1", "AA=="), new(0, 0), "created-manifest")
            {
                MapLayersDigest = "created-layers",
                Coverage = new(2, 1, 0, 0, 0, 0, 0, 0, 0, 0, false, false, true, true),
            };
            host.ReleasePause.TrySetResult();
            host.ReleaseSelect.TrySetResult();
            host.Reconnect = Reply(bootstrap);
            observationSession.ResetAfterLoad();
            ShowMainMenu();
            worldMenuOverlay.Hide();
            resumeWorldOnContinue = true;

            await EnterWorldAsync();
            await WaitForPreviewAsync();
            if (isInWorld || resumeWorldOnContinue || !mainMenuOverlay.Visible || !worldMenuOverlay.Visible ||
                worldMenuHeading.Text != "New World" || !worldMenuColumns.Visible || !worldPreview.Visible ||
                worldCreateButton.Disabled || mainMenuContinueButton.Disabled || mainMenuNewButton.Disabled ||
                mainMenuLoadButton.Disabled || Requests(OwnerPairingEndpoints.OwnerResume) != 0)
                throw new InvalidOperationException("Continue on an untouched host must keep the camp behind the menus and offer New World without resuming.");

            // Creation still submits the candidate the player actually previewed, then enters founder setup.
            host.CreatedWorld = new("created-entry", "New World", generated.WorldId, worldSeedInput.Text,
                DateTimeOffset.UnixEpoch, [], null, "compatible");
            host.CreatedObservation = Reply(generated);
            await CreateSelectedWorldAsync();
            if (!isInWorld || mainMenuOverlay.Visible || worldMenuOverlay.Visible || resumeWorldOnContinue ||
                observationSession.Current?.Baseline.Snapshot.WorldId != generated.WorldId ||
                host.WorldCreations.ToArray() is not [var creation] || creation.CandidateAttempt != 2 ||
                creation.ExpectedManifestDigest != "created-manifest" || creation.ExpectedMapLayersDigest != "created-layers" ||
                Requests(OwnerPairingEndpoints.OwnerResume) != 0)
                throw new InvalidOperationException("The fresh-host route must preserve preview-bound creation and enter the generated founder setup without resuming it.");

            // Selecting the still-untouched bootstrap from Load World uses the same entry boundary.
            ShowMainMenu();
            ShowWorldActionSmokeMenu();
            var selectedWorld = new CatalogWorld("bootstrap-entry", "Earlier host", bootstrap.WorldId, "bootstrap-seed",
                DateTimeOffset.UnixEpoch, [], null, "compatible");
            host.SelectedWorld = selectedWorld;
            host.Catalog = new("created-entry", [selectedWorld]);
            host.Reconnect = Reply(bootstrap);
            await RefreshWorldListAsync();
            ChooseWorldActionSmokeRow(0);
            await SelectListedWorldAsync();
            await WaitForPreviewAsync();
            if (isInWorld || !mainMenuOverlay.Visible || !worldMenuOverlay.Visible ||
                worldMenuHeading.Text != "New World" || resumeWorldOnContinue ||
                Requests(OwnerPairingEndpoints.OwnerResume) != 0)
                throw new InvalidOperationException("Load World must route an untouched bootstrap to New World too.");

            // A failed read must not act on the bootstrap flag in the previously held observation.
            worldMenuOverlay.Hide();
            var previewRequests = Requests(OwnerPairingEndpoints.OwnerWorldPreview);
            var refreshes = successfulRefreshCount;
            host.Reconnect = null;
            resumeWorldOnContinue = true;
            await EnterWorldAsync();
            if (isInWorld || worldMenuOverlay.Visible || !mainMenuOverlay.Visible || !resumeWorldOnContinue ||
                mainMenuContinueButton.Disabled || successfulRefreshCount != refreshes ||
                Requests(OwnerPairingEndpoints.OwnerWorldPreview) != previewRequests ||
                Requests(OwnerPairingEndpoints.OwnerResume) != 0 ||
                !mainMenuStatus.Text.StartsWith("Could not reach your world.", StringComparison.Ordinal))
                throw new InvalidOperationException("A failed Continue refresh must retain the menu and retry state without opening New World or resuming.");

            // Older hosts omit the new property; existing/progressed worlds also keep Continue's resume behavior.
            var olderSetup = JsonSerializer.Deserialize<OwnerFounderSetup>(
                "{\"required\":4,\"placed\":0,\"started\":false}", CompatibilitySmokeJsonOptions)!;
            foreach (var setup in new[] { olderSetup, new OwnerFounderSetup(4, 4, true) })
            {
                ShowMainMenu();
                observationSession.ResetAfterLoad();
                host.Reconnect = Reply(generated with { FounderSetup = setup });
                resumeWorldOnContinue = setup.Started;
                var resumes = Requests(OwnerPairingEndpoints.OwnerResume);
                await EnterWorldAsync();
                if (!isInWorld || mainMenuOverlay.Visible || worldMenuOverlay.Visible || resumeWorldOnContinue ||
                    Requests(OwnerPairingEndpoints.OwnerWorldPreview) != previewRequests ||
                    Requests(OwnerPairingEndpoints.OwnerResume) != resumes + (setup.Started ? 1 : 0))
                    throw new InvalidOperationException("A host without the new flag and an existing world must continue normally.");
            }
        }
        finally
        {
            worldListRequest.Cancel();
            worldMenuOverlay.Hide();
            InvalidateWorldPreview(refresh: false);
            listedWorlds = [];
            listedActiveWorldId = null;
            worldSelectionList.Clear();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            isInWorld = previousInWorld;
            resumeWorldOnContinue = previousResume;
            mainMenuOverlay.Visible = previousMenuVisible;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            RefreshMainMenuAvailability();
            statusToast.Hide();
        }
    }

    /// <summary>Late Continue replies cannot resume a world after menu navigation.</summary>
    private async Task VerifyContinueSettingsNavigationAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousInWorld = isInWorld;
        var previousResume = resumeWorldOnContinue;
        var previousMenuVisible = mainMenuOverlay.Visible;
        var previousReturnToMainMenu = returnToMainMenu;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        host.ReleasePause.TrySetResult();
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        var snapshot = new OwnerWorldSnapshot("continue-probe", 0, "continue-map", [new(0, 0, "meadow")],
            [], [], null, 0)
        {
            FounderSetup = new(4, 4, true),
            Authoring = new(true, 0, 0, 0, "continue-map", "continue-map", "clear", "spring", []),
        };
        host.Reconnect = new(handshake, new(snapshot, new(0, snapshot.WorldTick, [])));
        int Requests(string path) => host.Requests.Count(p => p == path);
        void Log(string stage) => GD.Print($"Continue navigation smoke {stage}: title={mainMenuOverlay.Visible && mainMenuCard.Visible}; settings={gameMenuPanel.Visible}; inWorld={isInWorld}; returnToMainMenu={returnToMainMenu}; hostPaused={host.HostPaused}; pauses={host.PauseCount}; resumes={Requests(OwnerPairingEndpoints.OwnerResume)}");
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            observationSession.ResetAfterLoad();
            ShowMainMenu();
            worldMenuOverlay.Hide();
            resumeWorldOnContinue = true;
            await SetPausedAsync(paused: true);
            mainMenuSettingsButton.EmitSignal(BaseButton.SignalName.Pressed);
            menuCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Log("settings-only-control");
            if (!mainMenuCard.Visible || isInWorld || !host.HostPaused)
                throw new InvalidOperationException("Settings-only control failed.");

            host.ReleaseReconnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
            host.ReconnectReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var enter = EnterWorldAsync();
            await host.ReconnectReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Log($"waiting ContinueDisabled={mainMenuContinueButton.Disabled} SettingsDisabled={mainMenuSettingsButton.Disabled}");
            host.ReleaseReconnect.TrySetResult();
            await enter;
            Log("continue-only-control");
            if (!isInWorld || host.HostPaused || mainMenuOverlay.Visible)
                throw new InvalidOperationException("Ordinary Continue control failed.");

            await SetPausedAsync(paused: true);
            ShowMainMenu();
            resumeWorldOnContinue = true;
            host.ReleaseReconnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
            host.ReconnectReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            enter = EnterWorldAsync();
            await host.ReconnectReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            mainMenuSettingsButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Log("settings-during-continue");
            host.ReleaseReconnect.TrySetResult();
            await enter;
            Log("late-continue");
            if (isInWorld || !gameMenuPanel.Visible || !host.HostPaused ||
                Requests(OwnerPairingEndpoints.OwnerResume) != 1)
                throw new InvalidOperationException("A Continue completed behind Settings must retain the confirmed pause.");
            var pausesBeforeBack = host.PauseCount;
            menuCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Log($"settings-back additionalPauses={host.PauseCount - pausesBeforeBack}");
            if (isInWorld || !mainMenuCard.Visible || !mainMenuOverlay.Visible || !host.HostPaused ||
                mainMenuContinueButton.Disabled || Requests(OwnerPairingEndpoints.OwnerResume) != 1)
                throw new InvalidOperationException("Settings Back must return to a usable paused Main Menu.");

            // Leaving Settings before the reply arrives must expire the old Continue too.
            host.ReleaseReconnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
            host.ReconnectReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            enter = EnterWorldAsync();
            await host.ReconnectReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            mainMenuSettingsButton.EmitSignal(BaseButton.SignalName.Pressed);
            menuCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            host.ReleaseReconnect.TrySetResult();
            await enter;
            if (isInWorld || !mainMenuOverlay.Visible || !mainMenuCard.Visible || !host.HostPaused ||
                Requests(OwnerPairingEndpoints.OwnerResume) != 1)
                throw new InvalidOperationException("Settings Back cannot reactivate a Continue from before navigation.");

            // A fresh Continue after Back still enters and resumes exactly once.
            await EnterWorldAsync();
            if (!isInWorld || mainMenuOverlay.Visible || host.HostPaused ||
                Requests(OwnerPairingEndpoints.OwnerResume) != 2)
                throw new InvalidOperationException("A fresh Continue after Settings Back must resume normally.");
        }
        finally
        {
            host.ReleaseReconnect?.TrySetResult();
            CloseGameMenu();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            isInWorld = previousInWorld;
            resumeWorldOnContinue = previousResume;
            returnToMainMenu = previousReturnToMainMenu;
            mainMenuOverlay.Visible = previousMenuVisible;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            RefreshMainMenuAvailability();
            statusToast.Hide();
        }
    }
}
