using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task<bool> SetPausedAsync(bool paused)
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            return false;
        }

        var accepted = false;
        var generation = observationSession.RequestGeneration;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await AwaitCurrentWorldResultAsync(ownerApi.SetPausedAsync(
                ResolveWorldUri(), authority, deviceId, paused, signer, CancellationToken.None));
            accepted = true;
            return receipt.Changed
                ? paused ? "World paused" : "World resumed"
                : $"The world was already {(paused ? "paused" : "running")}";
        }, waitForTurn: true);
        return accepted && IsCurrentWorldRequest(generation);
    }

    private async Task SubmitInstructionAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer) ||
            observationSession.Current is not { } current)
        {
            SetStatus("Wait for the world to load before giving an instruction.", good: false);
            return;
        }

        var selected = current.Baseline.Snapshot.Inhabitants
            .FirstOrDefault(inhabitant => string.Equals(inhabitant.Id, selectedInhabitantId, StringComparison.Ordinal));
        if (selected is null || selected.IsDraft)
        {
            SetStatus("Pick an agent first.", good: false);
            return;
        }
        if (selected.DecisionFactors.Any(factor => factor.Key == "age-band" && factor.Detail == "infant"))
        {
            SetStatus("Infants need an adult to look after them. They can't take work instructions.", good: false);
            return;
        }

        var text = instructionText.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Write something to say first.", good: false);
            return;
        }

        var action = new OwnerInstructionAction(
            $"instruction_{OwnerPairingProtocol.CreateRequestId()}",
            selected.Id,
            instructionOrderButton.ButtonPressed ? "must_do" : "suggestive",
            text,
            current.Baseline.Snapshot.WorldId,
            instructionOrderButton.ButtonPressed && instructionQueueToggle.ButtonPressed);
        if (!TryBeginPendingInstruction(action, out var pending))
        {
            return;
        }

        var completed = false;
        await RunOwnerActionAsync(async () =>
        {
            await AwaitCurrentWorldResultAsync(ownerApi.SubmitInstructionAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None));
            completed = true;
            instructionText.Text = string.Empty;
            return InstructionSubmissionResultText(action.Kind, action.Queue);
        });
        if (completed)
        {
            CompletePendingSubmission(pending);
        }
    }

    private async Task CancelSelectedOrderAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer) ||
            observationSession.Current is not { } current)
        {
            SetStatus("Wait for the world to load before cancelling an order.", good: false);
            return;
        }
        var order = PendingOrderToCancel(current.Baseline.Snapshot, selectedInhabitantId);
        if (order is null)
        {
            SetStatus("This agent has no waiting or active order to cancel.", good: false);
            return;
        }

        var action = new OwnerOrderCancelAction(
            $"cancel_order_{OwnerPairingProtocol.CreateRequestId()}",
            order.TargetInhabitantId,
            order.InstructionId,
            current.Baseline.Snapshot.WorldId);
        if (!TryBeginPendingOrderCancel(action, out var pending))
            return;
        var completed = false;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await AwaitCurrentWorldResultAsync(ownerApi.CancelOrderAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None));
            completed = true;
            return OrderCancellationResultText(receipt);
        });
        if (completed)
            CompletePendingSubmission(pending);
    }

    private static string InstructionSubmissionResultText(string kind, bool queue) => kind == "must_do"
        ? queue ? "Order added to the queue." : "Order sent."
        : "Suggestion sent.";

    private static string OrderCancellationResultText(OwnerOrderControlReceipt receipt)
    {
        if (receipt.Changed)
            return "Order cancelled.";

        return receipt.Status switch
        {
            "cancelled" => "That order was already cancelled.",
            "finished" => "That order had already finished.",
            "not_understood" => "The agent could not follow that order.",
            _ => "That order is no longer waiting or active.",
        };
    }

    private async Task SubmitAuthoringAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer) ||
            observationSession.Current?.Baseline.Snapshot.Authoring is not { IsPaused: true })
        {
            SetStatus("paused authoring is disabled until the server reports an atomic paused boundary", good: false);
            return;
        }

        var operation = new OwnerAuthoringOperationAction(
            SelectedAuthoringKind(),
            EmptyToNull(authoringId.Text),
            EmptyToNull(authoringValue.Text),
            EmptyToNull(authoringSecondaryValue.Text),
            checked((int)authoringX.Value),
            checked((int)authoringY.Value),
            authoringRenewable.ButtonPressed);
        var batch = new OwnerAuthoringBatchAction(
            $"authoring_{OwnerPairingProtocol.CreateRequestId()}",
            [operation]);
        if (!TryBeginPendingAuthoring(batch, out var pending))
        {
            return;
        }

        var completed = false;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await AwaitCurrentWorldResultAsync(ownerApi.SubmitAuthoringAsync(
                ResolveWorldUri(), authority, deviceId, batch, signer, CancellationToken.None));
            completed = true;
            return receipt.Applied
                ? $"applied {operation.Kind} at revision {receipt.Revision}"
                : $"authoring rejected · {receipt.Failure ?? "unknown validation failure"}";
        });
        if (completed)
        {
            CompletePendingSubmission(pending);
        }
    }

    private async Task ApprovePairingAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            return;
        }

        var pairingId = pairingApprovalId.Text.Trim();
        var pairingCode = pairingApprovalCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(pairingId) || string.IsNullOrWhiteSpace(pairingCode))
        {
            SetStatus("enter the pending pairing ID and comparison code", good: false);
            return;
        }

        var action = new OwnerPairingApprovalAction(pairingId, pairingCode);
        await RunOwnerActionAsync(async () =>
        {
            var approval = await AwaitCurrentWorldResultAsync(ownerApi.ApprovePairingAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None));
            pairingApprovalCode.Text = string.Empty;
            return $"approved pending device {approval.DeviceId}; it must still activate its own Windows key";
        });
    }

    private async Task RevokeDeviceAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            return;
        }

        var targetDeviceId = revokeDeviceId.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetDeviceId))
        {
            SetStatus("enter a paired device ID to revoke it", good: false);
            return;
        }

        if (string.Equals(targetDeviceId, deviceId, StringComparison.Ordinal))
        {
            SetStatus("this client will not revoke its own active key; use host-local recovery if that is intentional", good: false);
            return;
        }

        var action = new OwnerDeviceManagementAction(targetDeviceId);
        await RunOwnerActionAsync(async () =>
        {
            var revoked = await AwaitCurrentWorldResultAsync(ownerApi.RevokeDeviceAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None));
            revokeDeviceId.Text = string.Empty;
            return $"revoked device {revoked.DeviceId}";
        });
        await RefreshDeviceRegistryAsync();
    }

    private async Task RefreshDeviceRegistryAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            return;
        }

        await RunOwnerActionAsync(async () =>
        {
            pairedDevices = await AwaitCurrentWorldResultAsync(ownerApi.ListDevicesAsync(
                ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None));
            RenderPairedDevices();
            return $"loaded {pairedDevices.Length} paired device record(s)";
        });
    }

    private void RenderPairedDevices()
    {
        pairedDeviceList.Clear();
        foreach (var device in pairedDevices
            .OrderBy(device => device.State == OwnerDeviceState.Active ? 0 : 1)
            .ThenBy(device => device.DeviceId, StringComparer.Ordinal))
        {
            var self = string.Equals(device.DeviceId, registration?.DeviceId, StringComparison.Ordinal)
                ? " · this Windows device"
                : string.Empty;
            pairedDeviceList.AddItem($"{device.State.ToString().ToLowerInvariant()} · {device.DeviceId}{self}");
            pairedDeviceList.SetItemMetadata(pairedDeviceList.ItemCount - 1, device.DeviceId);
        }
    }

    private async Task SaveLifePaceAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer) ||
            observationSession.Current?.Baseline.Snapshot.WorldId is not { } worldId) return;
        var rate = lifePaceChoice.GetSelectedId();
        await RunOwnerActionAsync(async () =>
        {
            _ = await AwaitCurrentWorldResultAsync(ownerApi.SetLifePaceAsync(ResolveWorldUri(), authority, deviceId, rate, worldId, signer, CancellationToken.None));
            return "life pace saved; current ages preserved, future aging changed";
        });
    }

    private async Task SaveRoutineHelperAsync()
    {
        if (observationSession.Current?.Baseline.Snapshot is not { } snapshot ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var provider = SelectedRoutineHelper();
        var model = provider == "off" ? string.Empty : routineHelperModelPicker.Model;
        if (provider != "off" && (model.Length is < 1 or > 128 || model.Any(char.IsControl)))
        {
            SetStatus("Choose a model name of 128 characters or fewer.", good: false);
            return;
        }
        var action = new OwnerRoutineHelperAction(snapshot.WorldId, provider, model, provider == "decisions" ? SelectedRoutineHelperKey() : null);
        await RunOwnerActionAsync(async () =>
        {
            _ = await AwaitCurrentWorldResultAsync(ownerApi.SetRoutineHelperAsync(ResolveWorldUri(), authority, deviceId,
                action, signer, CancellationToken.None));
            return provider == "off" ? "Routine helper turned off for this world" : $"{ProviderDisplayName(provider)} will help in this world";
        });
    }

    private async Task RenameSelectedAgentAsync()
    {
        if (selectedInhabitantId is not { } agentId ||
            observationSession.Current?.Baseline.Snapshot is not { } snapshot ||
            snapshot.Inhabitants.All(person => person.Id != agentId))
            return;
        var attempted = renameAgentInput.Text;
        var name = attempted.Trim();
        if (name.Length is < 1 or > 48 || name.Any(char.IsControl))
        {
            SetStatus("Pick a name of 48 characters or fewer.", good: false);
            return;
        }
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var worldId = snapshot.WorldId;
        var generation = observationSession.RequestGeneration;
        await RunOwnerActionAsync(async () =>
        {
            // Hold the attempt while the host decides: a refresh in the meantime
            // must not reset the field before a refusal can keep it there.
            refusedAgentRename.Remember(worldId, agentId, attempted);
            try
            {
                var result = await AwaitCurrentWorldResultAsync(ownerApi.RenameAgentAsync(ResolveWorldUri(), authority, deviceId,
                    new OwnerAgentRenameAction(agentId, name), signer, CancellationToken.None));
                renamingAgentId = null;
                refusedAgentRename.Forget();
                renameRow.Hide();
                return result.Changed ? $"Renamed to {result.Name}" : "Name unchanged";
            }
            catch (OwnerAgentNameTakenException) when (selectedInhabitantId == agentId &&
                renamingAgentId == agentId && renameRow.Visible && renameAgentInput.Text == attempted &&
                IsCurrentWorldRequest(generation) &&
                observationSession.Current?.Baseline.Snapshot.WorldId == worldId)
            {
                // The field keeps the refused name until the player changes or
                // cancels it; refreshes still show the host's name above it.
                throw;
            }
            catch
            {
                if (IsCurrentWorldRequest(generation)) refusedAgentRename.Forget();
                throw;
            }
        });
    }

    private bool TryGetOwner(
        out OwnerAuthorityIdentity authority,
        out string deviceId,
        out IOwnerDeviceSigner signer)
    {
        if (!observationSession.AwaitingFreshBaseline && !registeredEndpointInvalid &&
            registration is not null && deviceKey is not null)
        {
            authority = registration.Authority;
            deviceId = registration.DeviceId;
            signer = deviceKey;
            return true;
        }

        authority = null!;
        deviceId = string.Empty;
        signer = null!;
        return false;
    }

    private void AddAuthoringKinds()
    {
        AddAuthoringKind("set_terrain", "Set terrain — value: meadow, water, or mountain; x/y required");
        AddAuthoringKind("place_resource", "Place resource — ID, value=kind, x/y, renewable required");
        AddAuthoringKind("remove_resource", "Remove resource — ID required");
        AddAuthoringKind("place_object", "Place object — ID, value=kind, x/y required");
        AddAuthoringKind("remove_object", "Remove object — ID required");
        AddAuthoringKind("place_building", "Place building — ID, value=building kind, x/y required");
        AddAuthoringKind("remove_building", "Remove building — ID required");
        AddAuthoringKind("place_plant", "Place plant — ID, value=plant kind, x/y required");
        AddAuthoringKind("remove_plant", "Remove plant — ID required");
        AddAuthoringKind("create_founder_draft", "Create founder draft — ID, value=display name, x/y required");
        AddAuthoringKind("remove_founder_draft", "Remove founder draft — ID required");
        AddAuthoringKind("set_weather", "Set weather — value required");
        AddAuthoringKind("set_season", "Set season — value: spring, summer, autumn, or winter");
        AddAuthoringKind("set_weather_season", "Set weather + season — value=weather, secondary value=season");
        AddAuthoringKind("add_approved_asset_reference", "Add approved asset reference — ID and exact lowercase sha256 digest must already exist in the host catalog");
        AddAuthoringKind("remove_approved_asset_reference", "Remove approved asset reference — ID required");
    }

    private void AddAuthoringKind(string kind, string description)
    {
        authoringKind.AddItem(kind);
        authoringKind.SetItemMetadata(authoringKind.ItemCount - 1, description);
    }

    private void UpdateAuthoringHint()
    {
        if (authoringKind.ItemCount == 0)
        {
            return;
        }

        authoringHintLabel.Text = authoringKind.GetItemMetadata(authoringKind.Selected).AsString();
    }

    private static void ConfigureCoordinate(SpinBox box, string placeholder)
    {
        box.MinValue = 0;
        box.MaxValue = 0;
        box.Step = 1;
        box.CustomMinimumSize = new Vector2(72, 0);
        box.TooltipText = placeholder;
    }

    private void RefreshControlAvailability()
    {
        RefreshManualSaveAvailability();
        RefreshWorldMenuAvailability();
        if (!IsCurrentAutosaveSettingsContext()) CancelAutosaveSettingsRead();
        var paired = !registeredEndpointInvalid && registration is not null && deviceKey is not null;
        worldSettingsCategoryButton.Disabled = !paired || !isInWorld || returnToMainMenu ||
            observationSession.AwaitingFreshBaseline;
        menuResumeButton.Disabled = observationSession.AwaitingFreshBaseline;
        menuSaveWorldButton.Disabled = observationSession.AwaitingFreshBaseline || isOwnerAction;
        menuQuitToMainButton.Disabled = observationSession.AwaitingFreshBaseline || isOwnerAction;
        var snapshot = observationSession.Current?.Baseline.Snapshot;
        var paused = snapshot?.Authoring?.IsPaused == true;
        var selected = snapshot?.Inhabitants.FirstOrDefault(item =>
            string.Equals(item.Id, selectedInhabitantId, StringComparison.Ordinal));
        var actionDisabled = !paired || isOwnerAction || pendingSubmission is not null ||
            observationSession.AwaitingFreshBaseline;
        buildingManagementApply.Disabled = actionDisabled || buildingManagementChoice.ItemCount == 0;
        buildingManagementChoice.Disabled = actionDisabled;
        buildingRemoveButton.Disabled = actionDisabled;
        saveApiKeyButton.Disabled = actionDisabled;
        apiKeyProviderChoice.Disabled = actionDisabled;
        apiKeyLabelInput.Editable = !actionDisabled;
        apiKeyInput.Editable = !actionDisabled;
        autosaveApplyButton.Disabled = actionDisabled || !paused || !autosaveSettingsLoaded;
        var supportsLifePace = snapshot?.LifePaceRate is not null;
        var supportsRoutineHelper = snapshot?.RoutineHelperProvider is not null &&
            observationSession.Current?.Handshake.ServerCapabilities.Contains("owner-routine-helper.v1", StringComparer.Ordinal) == true;
        routineHelperChoice.Disabled = actionDisabled || !paused || !supportsRoutineHelper;
        routineHelperModelPicker.Editable = !routineHelperChoice.Disabled;
        var helper = SelectedRoutineHelper();
        var helperModel = helper == "off" ? string.Empty : routineHelperModelPicker.Model;
        applyRoutineHelperButton.Disabled = routineHelperChoice.Disabled ||
            helper != "off" && (helperModel.Length is < 1 or > 128 || helperModel.Any(char.IsControl));
        routineHelperKeyChoice.Disabled = routineHelperChoice.Disabled;
        applyLifePaceButton.Disabled = actionDisabled || !paused || !supportsLifePace;
        lifePaceChoice.Disabled = actionDisabled || !paused || !supportsLifePace;
        applyLifePaceButton.TooltipText = !supportsLifePace ? "This host does not support life pacing." :
            !paused ? "Pause the world before changing life pace." : "Apply future aging speed; existing ages are preserved.";
        if (snapshot?.LifePaceRate is { } rate && (lastObservedLifePace != rate || lastLifePaceWorldId != snapshot.WorldId))
        {
            lifePaceChoice.Select(lifePaceChoice.GetItemIndex(rate));
            lastObservedLifePace = rate;
            lastLifePaceWorldId = snapshot.WorldId;
        }
        worldUrlInput.Editable = registration is null && pendingPairing is null && !isPairingOperation && !isOwnerAction && !isRefreshing;
        connectButton.Disabled = registeredEndpointInvalid || pendingPairing is not null || isPairingOperation || isOwnerAction || isRefreshing;
        pairAgainButton.Visible = registration is not null;
        pairAgainButton.Disabled = isPairingOperation || isOwnerAction || isRefreshing;
        pauseButton.Disabled = actionDisabled || snapshot is null;
        pauseButton.Visible = snapshot?.FounderSetup is not { Started: false };
        founderSetupButton.Disabled = actionDisabled || snapshot?.FounderSetup is not { Started: false };
        townSiteButton.Disabled = actionDisabled || snapshot?.FounderSetup is not { CanChooseTownSite: true };
        moveFounderButton.Disabled = actionDisabled || movingFounderId is null &&
            (snapshot?.FounderSetup is not { Started: false, Placed: > 0 } ||
             selected is null || !selected.Id.StartsWith("founder:", StringComparison.Ordinal));
        undoFounderButton.Disabled = actionDisabled || snapshot?.FounderSetup is not
        { Started: false, Placed: > 0, LastFounderId: not null };
        if (snapshot?.FounderSetup is { CanChooseTownSite: true, HasAcceptedTownSite: false })
            founderSetupButton.Disabled = true;
        addAgentButton.Disabled = actionDisabled || snapshot?.FounderSetup is not { Started: true };
        renameAgentButton.Disabled = actionDisabled || selected is null || selected.IsDraft;
        renameAgentInput.Editable = !actionDisabled && selected is { IsDraft: false };
        renameToggleButton.Disabled = !renameAgentInput.Editable;
        startWorldButton.Disabled = actionDisabled || snapshot?.FounderSetup is not { Started: false, Placed: 4 };
        founderProviderChoice.Disabled = actionDisabled;
        founderCredentialChoice.Disabled = actionDisabled;
        founderModelPicker.Editable = !actionDisabled;
        founderModelSetupCheckButton.Disabled = actionDisabled;
        founderApiKeyInput.Editable = !actionDisabled;
        founderKeyLabelInput.Editable = !actionDisabled;
        var infantSelected = selected?.DecisionFactors.Any(factor => factor.Key == "age-band" && factor.Detail == "infant") == true;
        var deceasedSelected = selected?.Lifecycle == "dead";
        submitInstructionButton.Disabled = actionDisabled || selected is null || selected.IsDraft || infantSelected || deceasedSelected;
        submitInstructionButton.TooltipText = deceasedSelected ? "Historical profiles cannot receive instructions." :
            infantSelected ? "Direct care through an adult caregiver." : "Send an instruction to this agent.";
        RenderDeveloperEdits(snapshot, actionDisabled);
        submitAuthoringButton.Disabled = actionDisabled || !paused;
        authoringKind.Disabled = actionDisabled || !paused;
        authoringId.Editable = !actionDisabled && paused;
        authoringValue.Editable = !actionDisabled && paused;
        authoringSecondaryValue.Editable = !actionDisabled && paused;
        authoringX.Editable = !actionDisabled && paused;
        authoringY.Editable = !actionDisabled && paused;
        authoringRenewable.Disabled = actionDisabled || !paused;
        instructionSuggestButton.Disabled = actionDisabled || deceasedSelected;
        instructionOrderButton.Disabled = actionDisabled || deceasedSelected;
        instructionQueueToggle.Disabled = actionDisabled || deceasedSelected || !instructionOrderButton.ButtonPressed;
        instructionCancelButton.Disabled = actionDisabled || deceasedSelected;
        instructionText.Editable = !actionDisabled && !deceasedSelected;
        RenderPendingSubmission();
        retryPendingSubmissionButton.Disabled = !paired || isOwnerAction || !CanRetryPendingSubmission(pendingSubmission);
        forgetPendingSubmissionButton.Disabled = isPairingOperation || isOwnerAction || isRefreshing;
        pairingApprovalId.Editable = !actionDisabled;
        pairingApprovalCode.Editable = !actionDisabled;
        approvePairingButton.Disabled = actionDisabled;
        refreshDevicesButton.Disabled = actionDisabled;
        revokeDeviceId.Editable = !actionDisabled;
        revokeDeviceButton.Disabled = actionDisabled;
        cognitionRoleChoice.Disabled = actionDisabled;
        cognitionProviderChoice.Disabled = actionDisabled;
        cognitionCredentialChoice.Disabled = actionDisabled;
        cognitionModelPicker.Editable = !actionDisabled && SelectedProviderId() != "deterministic";
        cognitionModelSetupCheckButton.Disabled = actionDisabled;
        cognitionApiKeyInput.Editable = !actionDisabled && SelectedProviderId() != "deterministic";
        cognitionCredentialLabelInput.Editable = !actionDisabled;
        refreshCognitionProviderButton.Disabled = actionDisabled;
        applyUsageLimitButton.Disabled = actionDisabled || usageStatus?.AccountingError is not null;
        grantUsageCallsButton.Disabled = actionDisabled || usageStatus?.LimitReached != true;
        refreshUsageButton.Disabled = actionDisabled;
        usageAttemptLimitInput.Editable = !actionDisabled && usageStatus?.AccountingError is null;
        var selectedProvider = SelectedProviderId();
        var selectedProviderStatus = providerConfiguration?.Providers.FirstOrDefault(item =>
            string.Equals(item.Provider, selectedProvider, StringComparison.Ordinal));
        saveCognitionProviderButton.Disabled = actionDisabled ||
            SelectedCognitionTarget() is not null && selectedProvider == "jev" ||
            SelectedCognitionTarget() is not null && selectedProviderStatus?.HasCredential != true &&
                selectedProvider is ("openai" or "ollama-cloud") && SelectedCredentialChoice() == "default";
        forgetCognitionCredentialButton.Disabled = actionDisabled || selectedProvider == "deterministic" ||
            selectedProviderStatus?.HasCredential != true;
        deleteCognitionCredentialSlotButton.Disabled = actionDisabled ||
            providerConfiguration?.Assignments?.Any(item =>
                item.CredentialSlotId == SelectedCredentialChoice() && item.SelectionReason is null) == true;
        // A public key can have only one pending server pairing. Keep the
        // visible comparison value stable until it expires or activates.
        pairButton.Disabled = isPairingOperation || deviceKey is null || pendingPairing is not null || registration is not null;
        forgetRegistrationButton.Disabled = isPairingOperation || registration is null;
    }

}
