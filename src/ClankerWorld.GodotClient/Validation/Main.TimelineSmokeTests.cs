using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifySameWorldTimelineUiRecoveryAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousSelection = selectedInhabitantId;
        var previousPendingSubmission = pendingSubmission;
        var previousProviderConfiguration = providerConfiguration;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        var previousMenuVisible = gameMenuPanel.Visible;
        var previousSettingsVisible = settingsPanel.Visible;
        var previousWorldSettingsVisible = worldSettingsContent.Visible;
        var previousGameSettingsVisible = gameSettingsContent.Visible;
        var previousInWorld = isInWorld;
        var previousMainMenuVisible = mainMenuOverlay.Visible;
        var previousWorldMenuVisible = worldMenuOverlay.Visible;
        var previousResumeOnContinue = resumeWorldOnContinue;
        var previousMenuPausedWorld = menuPausedWorld;
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        const string worldId = "timeline-ui-world";
        const string agentId = "timeline-ui-agent";
        const string listenerId = "timeline-ui-listener";
        const string turnId = "timeline-ui-turn";
        const string retainedKey = "timeline-ui-instruction-key";
        const string retainedText = "Remember the orchard.";
        OwnerWorldReconnect World(long generation, long tick, int eventCount)
        {
            var position = new OwnerWorldPosition(1, 1);
            OwnerWorldInhabitant Agent(string id, string name) => new(id, name, "active", position, 8_000, [], [],
                new("idle", null, null, [], string.Empty), new(position, [position], [position]), false);
            var snapshot = new OwnerWorldSnapshot(worldId, tick, "timeline-ui-map",
                Enumerable.Range(0, 9).Select(index => new OwnerWorldTile(index % 3, index / 3, "meadow")).ToArray(),
                [], [], null, eventCount)
            {
                PackedTerrain = new(3, 3, "terrain-kind-v1", Convert.ToBase64String(new byte[9])),
                Inhabitants = [Agent(agentId, "Rowan Lake"), Agent(listenerId, "Aster Vale")],
                Conversations = [new("timeline-ui-conversation", agentId, "Rowan Lake", listenerId, "Aster Vale",
                    "completed", null, null, 0, tick,
                    [new(turnId, agentId, "Rowan Lake", $"Timeline {generation} conversation.", tick, [listenerId], false)])],
                Authoring = new(false, 0, 0, 0, "timeline-ui-map", "timeline-ui-map", "clear", "spring", []),
            };
            var events = Enumerable.Range(1, eventCount)
                .Select(id => new OwnerWorldEvent(id, tick, "timeline_smoke", $"generation:{generation}"))
                .ToArray();
            return new(new(new(1, 1),
                    ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                     "owner-control.request.v1", "paused-authoring.request.v1", "owner-observation-timeline.v1"], []),
                new(snapshot, new(tick, 0, events), new("timeline-ui-host", generation)));
        }

        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            foreach (var lateFailure in new[] { false, true })
            {
                observationSession.ReplaceRegistration(registration);
                ResetDisplayedWorldContext();
                host.Reconnect = World(1, 20, 2);
                await RefreshAsync();
                if (observationSession.Timeline?.Generation != 1 || observationSession.Current?.Baseline.Snapshot.WorldTick != 20)
                    throw new InvalidOperationException("The timeline UI check must first render its signed initial baseline.");
                selectedInhabitantId = agentId;
                var retained = OwnerPendingSubmission.ForInstruction(
                    OwnerPendingSubmissionBinding.Create(host.Authority, "smoke-device", signer.PublicKeyFingerprint,
                        new Uri(host.Address), observationSession.Timeline, worldId),
                    new(retainedKey, agentId, "guidance", retainedText, worldId));
                pendingSubmission = retained;
                RenderPendingSubmission();
                RefreshControlAvailability();
                if (retryPendingSubmissionButton.Disabled)
                    throw new InvalidOperationException("A retained instruction must initially be retryable on its original timeline.");
                lastSeenEventId = 20;
                newEventsAfter = 19;
                unreadEvents = 4;
                conversationReadWorldId = worldId;
                locallyReadConversationTurns.Add(ConversationTurnKey(worldId, turnId));
                void VerifyRetainedInstruction()
                {
                    if (!retryPendingSubmissionButton.Disabled || !ReferenceEquals(pendingSubmission, retained) ||
                        pendingSubmission?.Instruction?.IdempotencyKey != retainedKey ||
                        pendingSubmission?.Instruction?.Text != retainedText || pendingSubmission?.Binding != retained.Binding)
                        throw new InvalidOperationException("A previous-timeline instruction must remain unchanged with Retry disabled.");
                }
                var heldTerrain = terrainMap;
                gameMenuPanel.Show();
                settingsPanel.Show();
                gameSettingsContent.Hide();
                worldSettingsContent.Show();

                // Leave both panels open and their reads pending. The world ID,
                // registration and local request identities stay the same.
                var oldSettings = new TaskCompletionSource<WorldAutosaveSettings>();
                var oldSaves = new TaskCompletionSource<ManualWorldSave[]>();
                var settingsRead = RefreshAutosaveSettingsAsync(_ => oldSettings.Task);
                var savesRead = OpenManualSavesAsync(true, _ => oldSaves.Task);
                var generation = observationSession.RequestGeneration;
                host.Reconnect = World(2, 5, 1);
                await RefreshAsync();
                VerifyRetainedInstruction();
                if (!observationSession.AwaitingFreshBaseline || observationSession.RequestGeneration == generation ||
                    observationSession.Current?.Baseline.Snapshot.WorldId != worldId ||
                    observationSession.Current?.Baseline.Snapshot.WorldTick != 20 || selectedInhabitantId != agentId ||
                    !knownEvents.ContainsKey(2) || !ReferenceEquals(terrainMap, heldTerrain))
                    throw new InvalidOperationException("A same-world rewind must hold the old display while invalidating its requests.");
                var heldSettingsStatus = autosaveSettingsStatus.Text;
                var heldSavesStatus = manualSaveStatus.Text;
                var heldSavePlaceholder = manualSaveList.Placeholder;
                if (lateFailure)
                {
                    oldSettings.SetException(new IOException("Controlled previous-timeline settings failure."));
                    oldSaves.SetException(new IOException("Controlled previous-timeline save-list failure."));
                }
                else
                {
                    oldSettings.SetResult(new(worldId, true, 1, 3, DateTimeOffset.UnixEpoch, -1));
                    oldSaves.SetResult([new("obsolete-save", "Previous timeline", DateTimeOffset.UnixEpoch, 20)]);
                }
                await Task.WhenAll(settingsRead, savesRead);
                if (autosaveSettingsLoaded || !autosaveApplyButton.Disabled || manualSaveList.ItemCount != 0 ||
                    autosaveSettingsStatus.Text != heldSettingsStatus || manualSaveStatus.Text != heldSavesStatus ||
                    manualSaveList.Placeholder != heldSavePlaceholder || !manualSaveLoadButton.Disabled)
                    throw new InvalidOperationException("A held response from the previous timeline must not publish settings, saves or failure labels.");

                // The next real reconnect supplies the complete replacement;
                // its unchanged world/map/agent IDs must not preserve old UI caches.
                await RefreshAsync();
                VerifyRetainedInstruction();
                if (observationSession.AwaitingFreshBaseline || observationSession.Timeline?.Generation != 2 ||
                    observationSession.Current?.Baseline.Snapshot.WorldTick != 5 || selectedInhabitantId is not null ||
                    knownEvents.ContainsKey(2) || knownEvents.Count != 1 || knownEvents[1].Detail != "generation:2" ||
                    ReferenceEquals(terrainMap, heldTerrain) || manualSaveOverlay.Visible)
                    throw new InvalidOperationException("Accepting a fresh same-world baseline must clear old events, selection and terrain caches.");
                if (lastSeenEventId > 1 || newEventsAfter != long.MaxValue || unreadEvents != 0 ||
                    locallyReadConversationTurns.Contains(ConversationTurnKey(worldId, turnId)))
                    throw new InvalidOperationException("A fresh timeline must reset event unread state and reused conversation-turn read markers.");

                await RefreshAutosaveSettingsAsync(_ => Task.FromResult(
                    new WorldAutosaveSettings(worldId, true, 10, 5, DateTimeOffset.UnixEpoch, -1)));
                await OpenManualSavesAsync(true, _ => Task.FromResult<ManualWorldSave[]>(
                    [new("current-save", "Current timeline", DateTimeOffset.UnixEpoch, 5)]));
                if (!autosaveSettingsLoaded || autosaveIntervalChoice.GetSelectedId() != 10 ||
                    autosaveRotationChoice.GetSelectedId() != 5 || manualSaveList.ItemCount != 1 ||
                    listedManualSaves[0].Id != "current-save" || manualSaveList.GetItemTitle(0) != "Current timeline")
                    throw new InvalidOperationException("New-generation settings and save-list reads must still populate their controls.");
                manualSaveOverlay.Hide();
            }

            // Loading owns its timeline transition. A normal pulse can observe
            // that transition before the signed load receipt reaches the client.
            pendingSubmission = null;
            RefreshControlAvailability();
            isInWorld = true;
            mainMenuOverlay.Show();
            worldMenuOverlay.Show();
            resumeWorldOnContinue = true;
            menuPausedWorld = true;
            await OpenManualSavesAsync(true, _ => Task.FromResult<ManualWorldSave[]>(
                [new("race-save", "Earlier world", DateTimeOffset.UnixEpoch, 2)]));
            manualSaveList.Select(0);
            host.ReleasePause.TrySetResult();
            host.LoadReceipt = new("race-save", "before-race-load", 2);
            var releaseLoad = host.ReleaseLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading = LoadSelectedManualSaveAsync();
            try
            {
                await Task.WhenAny(host.LoadReceived.Task, loading).WaitAsync(TimeSpan.FromSeconds(5));
                if (!host.LoadReceived.Task.IsCompleted || await host.LoadReceived.Task != "race-save" ||
                    !isOwnerAction || !observationSession.AwaitingFreshBaseline || loading.IsCompleted)
                    throw new InvalidOperationException("The manual-load race must hold a signed load receipt after starting its timeline transition.");
                var beforePulse = observationSession.RequestGeneration;
                host.Reconnect = World(3, 2, 0);
                await PulseAsync();
                if (!isOwnerAction || loading.IsCompleted || observationSession.AwaitingFreshBaseline ||
                    observationSession.RequestGeneration == beforePulse || observationSession.Timeline?.Generation != 3 ||
                    observationSession.Current?.Baseline.Snapshot.WorldTick != 2 ||
                    !mainMenuOverlay.Visible || !worldMenuOverlay.Visible)
                    throw new InvalidOperationException("A normal pulse must accept the loaded baseline while its load receipt is still held.");
                releaseLoad.SetResult();
                await loading.WaitAsync(TimeSpan.FromSeconds(5));
                if (!isInWorld || isOwnerAction || mainMenuOverlay.Visible || worldMenuOverlay.Visible || manualSaveOverlay.Visible ||
                    resumeWorldOnContinue || menuPausedWorld || observationSession.AwaitingFreshBaseline ||
                    observationSession.Timeline?.Generation != 3 || observationSession.Current?.Baseline.Snapshot.WorldTick != 2 ||
                    !statusLabel.Text.StartsWith("Loaded Earlier world.", StringComparison.Ordinal))
                    throw new InvalidOperationException("A delayed receipt for our own load must finish closing its menus and keep the accepted loaded world.");
            }
            finally
            {
                releaseLoad.TrySetResult();
                await loading.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            CancelAutosaveSettingsRead();
            CancelManualSaveListRead();
            manualSaveOverlay.Hide();
            allListedManualSaves = [];
            listedManualSaves = [];
            manualSaveList.Clear();
            ResetDisplayedWorldContext();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ReplaceRegistration(previousRegistration);
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            selectedInhabitantId = previousSelection;
            pendingSubmission = previousPendingSubmission;
            providerConfiguration = previousProviderConfiguration;
            RenderPendingSubmission();
            gameMenuPanel.Visible = previousMenuVisible;
            settingsPanel.Visible = previousSettingsVisible;
            worldSettingsContent.Visible = previousWorldSettingsVisible;
            gameSettingsContent.Visible = previousGameSettingsVisible;
            isInWorld = previousInWorld;
            mainMenuOverlay.Visible = previousMainMenuVisible;
            worldMenuOverlay.Visible = previousWorldMenuVisible;
            resumeWorldOnContinue = previousResumeOnContinue;
            menuPausedWorld = previousMenuPausedWorld;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            statusToast.Hide();
        }
    }
}
