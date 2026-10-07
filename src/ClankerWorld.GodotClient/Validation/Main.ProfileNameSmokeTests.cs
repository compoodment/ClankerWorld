using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyLongProfileNamesAsync()
    {
        var window = GetWindow();
        var previousSize = window.Size;
        var previousRenderSize = window.ContentScaleSize;
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousSelection = selectedInhabitantId;
        var previousCardSnapshot = agentCardSnapshot;
        var previousProfileRequested = agentProfileRequested;
        var previousProfileVisible = agentProfilePanel.Visible;
        var previousQuickCardVisible = selectedInhabitantCard.Visible;
        var previousMainMenuVisible = mainMenuOverlay.Visible;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        const string agentId = "long-profile-agent";
        const string shorter = "Short Vale";
        var tick = 0L;
        OwnerWorldReconnect World(string name) => new(new(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []),
            new(new OwnerWorldSnapshot("long-profile-world", ++tick, "long-profile-map", [new(0, 0, "meadow")],
                [], [], null, 0)
            {
                Inhabitants = [new(agentId, name, "active", new(0, 0), 8_000, [], [],
                    new("idle", null, null, [], string.Empty), new(new(0, 0), [new(0, 0)], [new(0, 0)]), false)],
            }, new(tick, 0, [])));
        async Task SettleAsync()
        {
            for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ApplyResponsiveLayout();
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        void Fits(string name, params Control[] controls)
        {
            foreach (var control in controls)
                if (!control.IsVisibleInTree() || !GetViewportRect().Grow(1).Encloses(control.GetGlobalRect()))
                    throw new InvalidOperationException($"Profile actions must fit for a {name.Length}-character name at {window.Size}: control={control.GetType().Name}, rect={control.GetGlobalRect()}, profile={agentProfilePanel.GetGlobalRect()}, name={selectedActorNameLabel.GetRect()}, summary={selectedActorSummaryLabel.GetRect()}, scroll={agentOverviewScroll.GetRect()}, scrollMinimum={agentOverviewScroll.CustomMinimumSize}, overviewMinimum={selectedAgentOverview.GetCombinedMinimumSize()}, ui={UiSize}.");
        }
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            mainMenuOverlay.Hide();
            // Both short-name sizes are controls before either wide name is checked.
            foreach (var name in new[] { "Rowan Lake", new string('界', 48), new string('W', 48) })
                foreach (var size in new[] { new Vector2I(1920, 1080), new Vector2I(1280, 720) })
                {
                    window.Size = size;
                    window.ContentScaleSize = size;
                    ApplyUiScale();
                    host.Reconnect = World(name);
                    await RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    selectedInhabitantId = null;
                    SelectInhabitant(agentId);
                    OpenAgentProfile(speak: false);
                    await SettleAsync();
                    if (uiLayer.Factor != (size.X == 1920 ? 2 : 1))
                        throw new InvalidOperationException("The Profile name check must use the window's automatic UI scale.");
                    Fits(name, agentProfilePanel, renameToggleButton, profileCloseButton);
                    if (renameToggleButton.Disabled)
                        throw new InvalidOperationException("The paired active agent's Rename button must be usable.");
                    renameToggleButton.EmitSignal(BaseButton.SignalName.Pressed);
                    await SettleAsync();
                    Fits(name, renameAgentInput, renameAgentButton, profileCloseButton);
                    if (!renameRow.Visible || renameAgentInput.Text != name)
                        throw new InvalidOperationException("Opening Rename must retain the complete accepted name in its field.");
                    host.Reconnect = World(shorter);
                    host.RenameReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    renameAgentInput.Text = shorter;
                    renameAgentInput.EmitSignal(LineEdit.SignalName.TextChanged, shorter);
                    renameAgentButton.EmitSignal(BaseButton.SignalName.Pressed);
                    await host.RenameReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                    while (DateTime.UtcNow < deadline && (renameRow.Visible || selectedActorNameLabel.Text != shorter))
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await SettleAsync();
                    if (!host.RenameRequests.TryDequeue(out var sent) || sent != new OwnerAgentRenameAction(agentId, shorter) ||
                        renameRow.Visible || selectedActorNameLabel.Text != shorter)
                        throw new InvalidOperationException($"The visible Rename button must submit the shorter name and display the accepted result: fieldOpen={renameRow.Visible}, shown={selectedActorNameLabel.Text}, status={statusLabel.Text}.");
                    Fits(shorter, agentProfilePanel, renameToggleButton, profileCloseButton);
                    profileCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
                    await SettleAsync();
                    if (agentProfilePanel.Visible || !selectedInhabitantCard.Visible || selectedInhabitantId != agentId)
                        throw new InvalidOperationException("The Profile's visible Back button must return to the selected agent's quick card.");
                }
        }
        finally
        {
            renameRow.Hide();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            selectedInhabitantId = previousSelection;
            agentCardSnapshot = previousCardSnapshot;
            agentProfileRequested = previousProfileRequested;
            agentProfilePanel.Visible = previousProfileVisible;
            selectedInhabitantCard.Visible = previousQuickCardVisible;
            mainMenuOverlay.Visible = previousMainMenuVisible;
            window.Size = previousSize;
            window.ContentScaleSize = previousRenderSize;
            ApplyUiScale();
            ApplyResponsiveLayout();
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            statusToast.Hide();
        }
    }
}
