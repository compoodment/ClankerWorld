using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Button recoveryCleanupButton = new();
    private readonly SpinBox recoveryKeepCount = new() { MinValue = 1, MaxValue = 10, Step = 1, Value = 3 };
    private readonly ConfirmationDialog recoveryCleanupConfirmation = new();
    private readonly Label recoveryCleanupSummary = new();
    private RecoveryCleanupPreview? pendingRecoveryCleanup;

    private void BuildRecoveryCleanup(VBoxContainer body)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Recovery copies per save", SizeFlagsVertical = SizeFlags.ShrinkCenter });
        row.AddChild(recoveryKeepCount);
        recoveryCleanupButton.Text = "Preview cleanup";
        recoveryCleanupButton.TooltipText = "Cleanup is off until you preview and confirm it. Manual saves are kept.";
        StyleButton(recoveryCleanupButton);
        recoveryCleanupButton.Pressed += () => _ = PreviewRecoveryCleanupAsync();
        row.AddChild(recoveryCleanupButton);
        body.AddChild(row);
        StyleConfirmation(recoveryCleanupConfirmation, "Clean up recovery history?", "Delete these copies");
        recoveryCleanupConfirmation.GetOkButton().ThemeTypeVariation = "DangerButton";
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(440, 260),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        recoveryCleanupSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        recoveryCleanupSummary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(recoveryCleanupSummary);
        recoveryCleanupConfirmation.AddChild(scroll);
        recoveryCleanupConfirmation.Confirmed += () => _ = CleanConfirmedRecoveryHistoryAsync();
        recoveryCleanupConfirmation.Canceled += () => pendingRecoveryCleanup = null;
        AddChild(recoveryCleanupConfirmation);
        manualSaveOverlay.VisibilityChanged += () =>
        {
            if (manualSaveOverlay.Visible) return;
            pendingRecoveryCleanup = null;
            recoveryCleanupConfirmation.Hide();
        };
    }

    private async Task PreviewRecoveryCleanupAsync()
    {
        if (isOwnerAction || listedSaveWorldId is not { } worldId ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        pendingRecoveryCleanup = null;
        var count = (int)recoveryKeepCount.Value;
        await RunOwnerActionAsync(async () =>
        {
            var preview = await AwaitCurrentWorldResultAsync(ownerApi.PreviewRecoveryCleanupAsync(ResolveWorldUri(), authority,
                deviceId, new("preview", worldId, count), signer, CancellationToken.None));
            if (!manualSaveOverlay.Visible || listedSaveWorldId != worldId || preview.WorldId != worldId)
                throw new ObsoleteWorldRequestException();
            ShowRecoveryCleanupPreview(preview);
            return preview.Remove.Count == 0 ? "No verified older recovery copies to remove." : "Review the recovery copies before deleting.";
        });
    }

    private void ShowRecoveryCleanupPreview(RecoveryCleanupPreview preview)
    {
        pendingRecoveryCleanup = preview.Remove.Count == 0 ? null : preview;
        recoveryCleanupSummary.Text = $"Keep {preview.KeepCount} verified recovery copies per save, including the latest. " +
            "Manual saves, older unclassified copies and unverifiable checkpoints are kept. " +
            "Cleanup runs only when you confirm. There is no undo.\n\nRemove:\n" +
            (preview.Remove.Count == 0 ? "None" : string.Join('\n', preview.Remove.Select(save =>
                $"• {save.Name} — {save.CreatedUtc.ToLocalTime():g} — {DisplayWorldClock(save.WorldTick)} — {save.Id}"))) +
            "\n\nKeep:\n" + string.Join('\n', preview.Keep.Select(save =>
                $"• {save.Name} — {save.CreatedUtc.ToLocalTime():g} — {DisplayWorldClock(save.WorldTick)} — {save.Id}"));
        recoveryCleanupConfirmation.GetOkButton().Disabled = pendingRecoveryCleanup is null || isOwnerAction;
        PopupDialog(recoveryCleanupConfirmation);
    }

    private async Task CleanConfirmedRecoveryHistoryAsync()
    {
        var preview = pendingRecoveryCleanup;
        pendingRecoveryCleanup = null;
        if (preview is null || preview.WorldId != CurrentManualSaveWorldId() ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            var receipt = await AwaitCurrentWorldResultAsync(ownerApi.CleanRecoveryHistoryAsync(ResolveWorldUri(), authority,
                deviceId, new("apply", preview.WorldId, preview.KeepCount, preview.Digest), signer, CancellationToken.None));
            await OpenManualSavesAsync(manualSaveLoadMode);
            return $"Removed {receipt.RemovedIds.Count} recovery copies.";
        }, waitForTurn: true);
    }
}
