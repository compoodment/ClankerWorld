using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly PanelContainer startupRecoveryCard = new();
    private readonly Label startupRecoveryExplanation = new()
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(400, 0),
    };
    private readonly Label startupRecoverySave = new()
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(400, 0),
    };
    private readonly Button startupRecoveryButton = new() { Text = "Recover autosave" };
    private readonly Label startupRecoveryDiskWarning = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button startupRecoveryRetry = new() { Text = "Check again" };
    private StartupRecoveryStatus? startupRecovery;
    private int startupRecoveryReadRevision;
    private bool startupRecoveryBusy;

    private void BuildStartupRecovery()
    {
        startupRecoveryCard.CustomMinimumSize = new Vector2(440, 0);
        startupRecoveryCard.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        mainMenuStack.AddChild(startupRecoveryCard);
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        body.AddThemeConstantOverride("separation", 12);
        var heading = new Label { Text = "Recover world" };
        heading.AddThemeFontSizeOverride("font_size", 24);
        body.AddChild(heading);
        body.AddChild(startupRecoveryExplanation);
        body.AddChild(startupRecoverySave);
        startupRecoveryDiskWarning.Hide();
        body.AddChild(startupRecoveryDiskWarning);
        StyleMenuChoice(startupRecoveryButton, primary: true);
        startupRecoveryButton.Pressed += () => _ = RecoverStartupAutosaveAsync();
        body.AddChild(startupRecoveryButton);
        StyleMenuChoice(startupRecoveryRetry);
        startupRecoveryRetry.Pressed += () => _ = CheckStartupRecoveryAsync();
        body.AddChild(startupRecoveryRetry);
        var quit = new Button { Text = "Quit Game" };
        StyleMenuChoice(quit);
        quit.Pressed += () => PopupDialog(quitGameConfirmation);
        body.AddChild(quit);
        AddPanelContents(startupRecoveryCard, body);
        startupRecoveryCard.Hide();
    }

    private void ShowStartupRecovery(StartupRecoveryStatus status)
    {
        startupRecovery = status;
        ShowMainMenu();
        resumeWorldOnContinue = false;
        menuPausedWorld = false;
        worldMenuOverlay.Hide();
        manualSaveOverlay.Hide();
        settingsPanel.Hide();
        mainMenuCard.Hide();
        mainMenuLogo.Hide();
        startupRecoveryExplanation.Text = status.Reason == "different_save_format"
            ? "This saved state uses a different save format that this alpha build cannot open. Your other saves are unchanged, and the refused file is kept."
            : "The latest saved state could not be opened. Your other saves are unchanged, and the damaged file is kept.";
        startupRecoverySave.Text = status.Autosave is { } save
            ? $"Last usable autosave: {save.CreatedUtc.ToLocalTime():g}.\nProgress since this autosave will be missing from the recovered world. The world will open paused."
            : "No usable autosave was found for this history. Quit and keep the saved files for recovery, or check again after restoring an autosave.";
        startupRecoveryButton.Disabled = startupRecoveryBusy || status.Autosave is null;
        startupRecoveryRetry.Disabled = startupRecoveryBusy;
        startupRecoveryCard.Show();
        _ = RefreshSaveDiskSpaceAsync(force: true);
        (status.Autosave is null ? startupRecoveryRetry : startupRecoveryButton).GrabFocus();
    }

    private async Task OpenWorldMenuAfterRecoveryCheckAsync(bool create)
    {
        if (!await CheckStartupRecoveryAsync()) OpenWorldMenu(create);
    }

    private async Task<bool> CheckStartupRecoveryAsync()
    {
        if (registration is null || deviceKey is null || registeredEndpointInvalid || startupRecoveryBusy) return true;
        var read = ++startupRecoveryReadRevision;
        var navigation = mainMenuNavigationRevision;
        var owner = registration;
        try
        {
            var status = await ownerApi.GetStartupRecoveryAsync(ResolveWorldUri(), owner.Authority, owner.DeviceId,
                deviceKey, CancellationToken.None);
            if (read != startupRecoveryReadRevision || navigation != mainMenuNavigationRevision || registration != owner) return true;
            if (status.Pending) { ShowStartupRecovery(status); return true; }
            startupRecovery = null;
            if (startupRecoveryCard.Visible) ShowMainMenu();
            return false;
        }
        catch (System.Net.Http.HttpRequestException exception)
            when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return read != startupRecoveryReadRevision || navigation != mainMenuNavigationRevision || registration != owner;
        }
        catch (Exception)
        {
            if (read == startupRecoveryReadRevision && navigation == mainMenuNavigationRevision)
            {
                RefreshMainMenuAvailability();
                if (startupRecoveryCard.Visible)
                    startupRecoverySave.Text = "Could not check recovery. Check your connection and choose Check again.";
                else SetMainMenuStatus("Could not check your saved world. Check your connection and try again.");
            }
            return true;
        }
    }

    private async Task RecoverStartupAutosaveAsync()
    {
        if (startupRecovery?.Autosave is not { } save || registration is null || deviceKey is null ||
            registeredEndpointInvalid || startupRecoveryBusy) return;
        var owner = registration;
        var signer = deviceKey;
        var server = ResolveWorldUri();
        await ownerActionGate.RunAsync(async () =>
        {
            startupRecoveryBusy = true;
            startupRecoveryReadRevision++;
            isOwnerAction = true;
            refreshCancellation?.Cancel();
            startupRecoveryButton.Disabled = true;
            startupRecoveryRetry.Disabled = true;
            try
            {
                await RefreshSaveDiskSpaceAsync(force: true);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!ReferenceEquals(owner, registration) || registeredEndpointInvalid)
                    throw new ObsoleteWorldRequestException();
                observationSession.ResetAfterLoad();
                await ownerApi.RecoverAutosaveAsync(server, owner.Authority, owner.DeviceId, save.Id,
                    signer, CancellationToken.None);
                startupRecovery = null;
                ShowMainMenu();
                var previousRefreshCount = successfulRefreshCount;
                await RefreshAsync();
                if (successfulRefreshCount != previousRefreshCount && !observationSession.AwaitingFreshBaseline)
                {
                    mainMenuOverlay.Hide();
                    isInWorld = true;
                    SetStatus("Recovered the autosave. The world is paused, and the damaged file is kept.", good: true);
                }
            }
            catch (Exception)
            {
                startupRecovery = null;
                startupRecoverySave.Text = "Could not confirm recovery. Choose Check again before trying recovery once more. Your saved files are kept.";
            }
            finally
            {
                startupRecoveryBusy = false;
                isOwnerAction = false;
                startupRecoveryRetry.Disabled = false;
                RefreshControlAvailability();
            }
        });
    }
}
