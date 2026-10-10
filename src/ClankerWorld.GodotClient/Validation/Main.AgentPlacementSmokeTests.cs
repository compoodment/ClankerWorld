using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyAcknowledgedAgentPlacementAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousConfiguration = providerConfiguration;
        var previousRefreshing = isRefreshing;
        var previousInWorld = isInWorld;
        var previousMainMenu = mainMenuOverlay.Visible;
        var previousKeyboard = keyboardNavigation;
        var previousCursor = keyboardMapTile;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        try
        {
            foreach (var boundary in new[] { "success", "settings-failure", "placement-refusal" })
            {
                using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64)
                {
                    SupportedActionPayloads = [OwnerWorldActionPayload.AgentPlacementPayloadDomain],
                    FailAgentPlacement = boundary == "placement-refusal",
                    FailPlacementProviderStatus = boundary == "settings-failure",
                };
                registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
                deviceKey = signer;
                worldUrlInput.Text = host.Address;
                observationSession.ResetAfterLoad();
                var snapshot = new OwnerWorldSnapshot("placement-ui-smoke", 0, "placement-map",
                    Enumerable.Range(0, 9).Select(index => new OwnerWorldTile(index % 3, index / 3, "meadow")).ToArray(),
                    [], [], null, 0)
                {
                    PackedTerrain = new(3, 3, "terrain-kind-v1", Convert.ToBase64String(new byte[9])),
                    FounderSetup = new(4, 4, true),
                    Towns = [new("keyboard-town", "Keyboard Town", "founded", 0, [], [], [new(0, 0)])],
                };
                var observation = new OwnerWorldReconnect(new OwnerWorldHandshake(new(1, 1),
                    ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                     "owner-control.request.v1", "paused-authoring.request.v1"], []), new(snapshot, new(0, 0, [])));
                if (!observationSession.TryAccept(observation, 0, out var failure))
                    throw new InvalidOperationException($"Placement baseline was refused: {failure}.");
                // Hold observation refresh to isolate acknowledgement from the separate settings read.
                isRefreshing = true;
                RenderMap(snapshot);
                gameMenuPanel.Hide();
                founderProviderChoice.Select(0);
                founderCredentialChoice.Clear();
                founderCredentialChoice.AddItem("Default key for this provider");
                founderCredentialChoice.SetItemMetadata(0, "default");
                founderCredentialChoice.Select(0);
                founderModelPicker.SetModel("placement-smoke-model");
                placingAddedAgent = true;
                founderSetupPanel.Show();
                if (boundary == "success")
                {
                    mainMenuOverlay.Hide();
                    isInWorld = true;
                    for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    GetViewport().GuiReleaseFocus();
                    await KeyboardKeyAsync(Key.K);
                    if (!mapCanvas.HasFocus()) throw new InvalidOperationException("Keyboard placement must enter the map.");
                    keyboardMapTile = new Vector2I(1, 0);
                    RefreshKeyboardMapSelection();
                    var previousPanelPosition = founderSetupPanel.Position;
                    try
                    {
                        var targetPoint = KeyboardMapCanvasPoint(new(0, 0));
                        founderSetupPanel.GlobalPosition = mapCanvas.GetGlobalTransform() * targetPoint - new Vector2(20, 20);
                        if (!founderSetupPanel.GetGlobalRect().HasPoint(mapCanvas.GetGlobalTransform() * targetPoint))
                            throw new InvalidOperationException("The keyboard placement fixture must put its target under Add Agent.");
                        UpdateHoverReadout(snapshot, new(1, 0));
                        PreviewAddAgentPlacement(snapshot, new(1, 0));
                        UpdateTileHover(targetPoint);
                        if (hoverReadoutTile != new Vector2I(1, 0))
                            throw new InvalidOperationException("Pointer hover behind Add Agent must preserve its previous preview.");
                        await KeyboardKeyAsync(Key.Left);
                        if (hoverReadoutTile != new Vector2I(0, 0) ||
                            !founderSetupHint.Text.Contains("They would join Keyboard Town without a household.", StringComparison.Ordinal))
                            throw new InvalidOperationException("Keyboard placement must preview the target's actual Town even behind Add Agent: " + founderSetupHint.Text);
                    }
                    finally { founderSetupPanel.Position = previousPanelPosition; }
                    for (var step = 0; step < 3; step++)
                    {
                        await KeyboardKeyAsync(Key.Left);
                        await KeyboardKeyAsync(Key.Up);
                    }
                    if (keyboardMapTile != new Vector2I(0, 0))
                        throw new InvalidOperationException("Placement arrows must reach the requested tile.");
                    await KeyboardKeyAsync(Key.Enter);
                    for (var frame = 0; frame < 600 && (host.AgentPlacements.IsEmpty || isOwnerAction); frame++)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                else await PlaceAgentAtAsync(new(0, 0));
                var refused = boundary == "placement-refusal";
                if (host.AgentPlacements.Count != 1 || placingAddedAgent != refused || founderSetupPanel.Visible != refused)
                    throw new InvalidOperationException($"An acknowledged placement must leave placement mode; a refused placement must keep it: boundary={boundary}, attempts={host.AgentPlacements.Count}, mode={placingAddedAgent}, panel={founderSetupPanel.Visible}, status={statusLabel.Text}.");
                var statusReads = host.Requests.Count(path => path == OwnerPairingEndpoints.OwnerProviderStatus);
                if (statusReads != (refused ? 0 : 1))
                    throw new InvalidOperationException("A refused placement must not refresh provider settings.");
                if (refused)
                {
                    if (!statusLabel.Text.Contains("The world host did not accept that request", StringComparison.Ordinal) ||
                        statusLabel.Text.Contains("Agent placed", StringComparison.Ordinal))
                        throw new InvalidOperationException("A placement refusal must remain visible.");
                    continue;
                }
                if (!statusLabel.Text.Contains("Agent placed", StringComparison.Ordinal) ||
                    (boundary == "settings-failure" && !statusLabel.Text.Contains("settings", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Placement success must survive a failed settings read: {statusLabel.Text}.");
                // Use the ordinary map dispatcher: another empty tile is not a new placement.
                using var click = new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    Position = mapStage.Position + new Vector2((currentTileSize + TileGap) * 1.5f, (currentTileSize + TileGap) * 1.5f),
                };
                if (TileAtCanvas(click.Position, snapshot) != new Vector2I(1, 1))
                    throw new InvalidOperationException("The second click must address a different empty tile.");
                HandleMapInput(click);
                for (var frame = 0; frame < 20; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (host.AgentPlacements.Count != 1 || isOwnerAction)
                    throw new InvalidOperationException("Clicking after an acknowledged placement must not submit another agent.");
                GD.Print($"Placement acknowledgement check: {boundary}, signed attempts=1, status reads={statusReads}, placement mode closed.");
            }
        }
        finally
        {
            founderSetupPanel.Hide();
            isInWorld = previousInWorld;
            mainMenuOverlay.Visible = previousMainMenu;
            keyboardNavigation = previousKeyboard;
            keyboardMapTile = previousCursor;
            GetViewport().GuiReleaseFocus();
            placingAddedAgent = false;
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            providerConfiguration = previousConfiguration;
            isRefreshing = previousRefreshing;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
            {
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
                RenderMap(previousObservation.Baseline.Snapshot);
            }
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            statusToast.Hide();
        }
    }
}
