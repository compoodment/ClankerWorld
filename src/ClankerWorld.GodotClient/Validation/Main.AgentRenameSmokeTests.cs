using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// A rename the host refuses because another agent holds the full name
    /// keeps the player's attempt in the Profile field through an ordinary
    /// signed refresh with the field unfocused, including one that lands while
    /// the host is still deciding, while the Profile keeps showing the host's
    /// name. Another agent or another world drops it.
    /// </summary>
    private async Task VerifyRefusedAgentRenameAsync()
    {
        const string asterId = "rename-ui-aster";
        const string rowanId = "rename-ui-rowan";
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousSelection = selectedInhabitantId;
        var previousProfileRequested = agentProfileRequested;
        var previousProfileVisible = agentProfilePanel.Visible;
        var previousQuickCardVisible = selectedInhabitantCard.Visible;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            var position = new OwnerWorldPosition(1, 1);
            OwnerWorldInhabitant Agent(string id, string name) => new(id, name, "active", position, 8_000, [], [],
                new OwnerWorldRoute("idle", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(position, [position], [position]), false);
            OwnerWorldReconnect World(string worldId, long tick, string rowanName = "Rowan Lake") => new(
                new OwnerWorldHandshake(new(1, 1),
                    ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                     "owner-control.request.v1", "paused-authoring.request.v1"], []),
                new(new OwnerWorldSnapshot(worldId, tick, "rename-ui-map",
                    Enumerable.Range(0, 9).Select(index => new OwnerWorldTile(index % 3, index / 3, "meadow")).ToArray(),
                    [], [], null, 0)
                {
                    PackedTerrain = new OwnerWorldPackedTerrain(3, 3, "terrain-kind-v1", Convert.ToBase64String(new byte[9])),
                    Inhabitants = [Agent(asterId, "Aster Vale"), Agent(rowanId, rowanName)],
                }, new(tick, 0, [])));
            async Task RefreshFromHostAsync(OwnerWorldReconnect next)
            {
                host.Reconnect = next;
                await RefreshAsync();
                if (observationSession.Current?.Baseline.Snapshot is not { } shown ||
                    shown.WorldId != next.Baseline.Snapshot.WorldId || shown.WorldTick != next.Baseline.Snapshot.WorldTick)
                    throw new InvalidOperationException("The rename check must apply the host's next snapshot through an ordinary refresh.");
            }
            async Task RefuseTakenNameAsync(OwnerWorldReconnect? refreshWhileDeciding = null)
            {
                renameAgentInput.Text = "Aster Vale";
                renameAgentInput.EmitSignal(LineEdit.SignalName.TextChanged, renameAgentInput.Text);
                // Pressing Rename takes keyboard focus away from the field.
                GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
                if (refreshWhileDeciding is null)
                {
                    await RenameSelectedAgentAsync();
                }
                else
                {
                    host.RenameReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = host.ReleaseRename = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var renaming = RenameSelectedAgentAsync();
                    await Task.WhenAny(host.RenameReceived.Task, renaming).WaitAsync(TimeSpan.FromSeconds(5));
                    if (!host.RenameReceived.Task.IsCompleted)
                        throw new InvalidOperationException("The rename request must reach the host before the refresh check.");
                    // The periodic refresh does not wait for owner actions.
                    await RefreshFromHostAsync(refreshWhileDeciding);
                    if (renameAgentInput.Text != "Aster Vale")
                        throw new InvalidOperationException($"A refresh while the host decides must keep the attempted name: field={renameAgentInput.Text}.");
                    host.ReleaseRename = null;
                    release.SetResult();
                    await renaming.WaitAsync(TimeSpan.FromSeconds(5));
                }
                if (!host.RenameRequests.TryDequeue(out var sent) || sent != new OwnerAgentRenameAction(rowanId, "Aster Vale") ||
                    !renameRow.Visible || renameAgentInput.Text != "Aster Vale" || renameAgentInput.HasFocus() ||
                    selectedActorNameLabel.Text != "Rowan Lake" ||
                    !statusLabel.Text.Contains("belongs to another agent", StringComparison.Ordinal))
                    throw new InvalidOperationException($"A refused rename must keep the attempt in the open field and explain that the name is taken: field={renameAgentInput.Text}, status={statusLabel.Text}.");
            }

            host.TakenAgentNames.Add("Aster Vale");
            if (!observationSession.TryAccept(World("rename-ui-smoke", 1), 0, out var failure))
                throw new InvalidOperationException("Rename observation fixture was refused: " + failure);
            Render(observationSession.Current!.Baseline.Snapshot, []);
            selectedInhabitantId = null;
            SelectInhabitant(rowanId);
            OpenAgentProfile(speak: false);
            renameToggleButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!agentProfilePanel.Visible || !renameRow.Visible || renameAgentInput.Text != "Rowan Lake" || !renameAgentInput.Editable)
                throw new InvalidOperationException("The Profile's rename field must open with the agent's current name.");

            await RefuseTakenNameAsync(refreshWhileDeciding: World("rename-ui-smoke", 2));
            await RefreshFromHostAsync(World("rename-ui-smoke", 3));
            if (!renameRow.Visible || renameAgentInput.Text != "Aster Vale" || selectedActorNameLabel.Text != "Rowan Lake" ||
                quickCardNameLabel.Text != "Rowan Lake")
                throw new InvalidOperationException($"An ordinary refresh with the field unfocused must keep the refused name for the player to change, while the Profile shows the name the host holds: field={renameAgentInput.Text}.");

            SelectInhabitant(asterId);
            SelectInhabitant(rowanId);
            await RefreshFromHostAsync(World("rename-ui-smoke", 4));
            if (renameAgentInput.Text != "Rowan Lake")
                throw new InvalidOperationException("Choosing another agent must drop the refused name.");

            await RefuseTakenNameAsync();
            await RefreshFromHostAsync(World("rename-ui-other", 5));
            if (renameAgentInput.Text != "Rowan Lake")
                throw new InvalidOperationException("Opening another world must drop the old world's refused name.");

            host.Reconnect = World("rename-ui-other", 6, rowanName: "Rowan Hill");
            renameAgentInput.Text = "Rowan Hill";
            renameAgentInput.EmitSignal(LineEdit.SignalName.TextChanged, renameAgentInput.Text);
            await RenameSelectedAgentAsync();
            if (!host.RenameRequests.TryDequeue(out var accepted) || accepted != new OwnerAgentRenameAction(rowanId, "Rowan Hill") ||
                renameRow.Visible || selectedActorNameLabel.Text != "Rowan Hill" ||
                observationSession.Current?.Baseline.Snapshot.WorldTick != 6)
                throw new InvalidOperationException("A different name must rename the agent, close the field and show the host's new name.");
        }
        finally
        {
            host.ReleaseRename?.TrySetResult();
            // A hidden field forgets any refused name at the next render.
            renameRow.Hide();
            renamingAgentId = null;
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            selectedInhabitantId = previousSelection;
            agentProfileRequested = previousProfileRequested;
            agentProfilePanel.Visible = previousProfileVisible;
            selectedInhabitantCard.Visible = previousQuickCardVisible;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            statusToast.Hide();
        }
    }
}
