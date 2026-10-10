using System.Net;
using System.Text.Json;
using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyRefusedPendingSubmissionsAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousSelection = selectedInhabitantId;
        var previousText = instructionText.Text;
        var previousPending = pendingSubmission;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        const string worldId = "refused-request-world";
        const string agentId = "refused-request-agent";
        var position = new OwnerWorldPosition(1, 1);
        host.Reconnect = new(new(new(1, 1), ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
            "owner-control.request.v1", "paused-authoring.request.v1"], []), new(
            new(worldId, 0, "refused-request-map", [], [], [], null, 0)
            {
                PackedTerrain = new(3, 3, "terrain-kind-v1", Convert.ToBase64String(new byte[9])),
                Inhabitants = [new(agentId, "Rowan Lake", "active", position, 8_000, [], [],
                    new("idle", null, null, [], string.Empty), new(position, [position], [position]), false)],
            }, new(0, 0, [])));
        try
        {
            await WaitForWorldPreviewSmokeAsync(() => !isRefreshing && !isOwnerAction, "previous owner request cleanup");
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            pendingSubmission = null;
            if (!observationSession.TryAccept(host.Reconnect, 0, out var failure))
                throw new InvalidOperationException($"The refusal fixture needs a valid baseline: {failure}");
            Render(host.Reconnect.Baseline.Snapshot, []);
            selectedInhabitantId = agentId;
            instructionText.Text = "Remember the orchard.";
            await SubmitInstructionAsync();
            RequireCleared();
            if (!host.Requests.Contains(OwnerPairingEndpoints.OwnerInstructions) || instructionText.Text != "Remember the orchard.")
                throw new InvalidOperationException("A refused instruction must reach the host and preserve the player's unsent text.");

            if (!TryCreatePendingSubmissionBinding(out var binding))
                throw new InvalidOperationException("The refusal fixture needs a current paired world.");
            OwnerPendingSubmission[] requests =
            [
                OwnerPendingSubmission.ForInstruction(binding, new("retry-refused", agentId, "suggestive", "Rest.", worldId)),
                OwnerPendingSubmission.ForOrderCancel(binding, new("cancel-refused", agentId, "order", worldId)),
                OwnerPendingSubmission.ForAuthoring(binding, new("authoring-refused", [new("weather", null, "clear", null, 0, 0, false)])),
            ];
            foreach (var request in requests)
            {
                // An ambiguous result must keep the exact persisted request for retry.
                foreach (var status in new[] { HttpStatusCode.RequestTimeout, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK })
                {
                    if (!TryRetainPendingSubmission(request)) throw new InvalidOperationException("Could not retain the smoke request.");
                    host.SubmissionStatus = status;
                    await RetryPendingSubmissionAsync();
                    if (!ReferenceEquals(pendingSubmission, request) || JsonSerializer.Serialize(pendingSubmissionStore.TryLoadForRegistration(binding)) != JsonSerializer.Serialize(request) ||
                        !saveApiKeyButton.Disabled || !buildingRemoveButton.Disabled)
                        throw new InvalidOperationException($"An ambiguous {status} response must preserve the exact retry and block other owner actions.");
                    // OK has an invalid receipt, modelling loss of a usable success response.
                    host.SubmissionStatus = HttpStatusCode.BadRequest;
                    await RetryPendingSubmissionAsync();
                    RequireCleared();
                }
            }
        }
        finally
        {
            pendingSubmissionStore.TryForget();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            selectedInhabitantId = previousSelection;
            instructionText.Text = previousText;
            pendingSubmission = previousPending;
            RenderPendingSubmission();
            RefreshControlAvailability();
            System.Environment.SetEnvironmentVariable("CI", previousCi);
        }

        void RequireCleared()
        {
            if (pendingSubmission is not null || TryCreatePendingSubmissionBinding(out var currentBinding) && pendingSubmissionStore.TryLoad(currentBinding) is not null ||
                saveApiKeyButton.Disabled || buildingRemoveButton.Disabled || isOwnerAction)
                throw new InvalidOperationException("#1533: a definitive refusal must clear the disk retry and restore owner controls.");
        }
    }
}
