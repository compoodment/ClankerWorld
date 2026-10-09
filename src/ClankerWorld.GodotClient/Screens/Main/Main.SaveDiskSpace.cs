using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly PanelContainer saveDiskWarningPanel = new();
    private readonly Label saveDiskWarningLabel = new();
    private readonly Label manualSaveDiskWarning = new();
    private long lastDiskSpaceReadMsec = -5000;
    private bool diskSpaceReadPending;

    private void BuildSaveDiskWarning(Control content)
    {
        saveDiskWarningLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        saveDiskWarningLabel.CustomMinimumSize = new Vector2(420, 0);
        AddPanelContents(saveDiskWarningPanel, saveDiskWarningLabel);
        saveDiskWarningPanel.ZIndex = 251;
        saveDiskWarningPanel.MouseFilter = MouseFilterEnum.Ignore;
        saveDiskWarningLabel.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(saveDiskWarningPanel);
        foreach (var child in saveDiskWarningPanel.FindChildren("*", nameof(Control), recursive: true, owned: false).OfType<Control>())
            child.MouseFilter = MouseFilterEnum.Ignore;
        saveDiskWarningPanel.Hide();
    }

    private void RenderSaveDiskSpace(SaveDiskSpaceStatus status)
    {
        var text = status.State switch
        {
            "low" => "Disk space is low on the server. Make room soon; saving may fail, but automatic recovery keeps trying.",
            "unknown" => "Free disk space could not be checked. Saving and automatic recovery will still be attempted.",
            "ok" => string.Empty,
            _ => "Free disk space could not be checked. Saving and automatic recovery will still be attempted.",
        };
        saveDiskWarningLabel.Text = text;
        manualSaveDiskWarning.Text = text;
        manualSaveDiskWarning.Visible = text.Length > 0;
        startupRecoveryDiskWarning.Text = text;
        startupRecoveryDiskWarning.Visible = text.Length > 0;
        saveDiskWarningPanel.Visible = text.Length > 0 && !manualSaveOverlay.Visible;
        saveDiskWarningPanel.ResetSize();
        ApplyResponsiveLayout();
    }

    private async Task RefreshSaveDiskSpaceAsync(bool force = false)
    {
        if (diskSpaceReadPending && !force) return;
        while (diskSpaceReadPending)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!TryGetRegisteredOwner(out var authority, out var deviceId, out var signer)) return;
        var now = (long)Time.GetTicksMsec();
        if (!force && now - lastDiskSpaceReadMsec < 5000) return;
        lastDiskSpaceReadMsec = now;
        diskSpaceReadPending = true;
        var readRegistration = registration;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var status = await ownerApi.GetSaveDiskSpaceAsync(ResolveWorldUri(), authority, deviceId, signer, timeout.Token);
            if (ReferenceEquals(readRegistration, registration)) RenderSaveDiskSpace(status);
        }
        catch (Exception)
        {
            // An unavailable advisory must never prevent a save or recovery request.
            if (ReferenceEquals(readRegistration, registration))
                RenderSaveDiskSpace(new("unknown", null, 0, null));
        }
        finally { diskSpaceReadPending = false; }
    }
}
