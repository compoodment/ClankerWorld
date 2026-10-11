using ClankerWorld.GodotClient.Networking;
using ClankerWorld.GodotClient.Pairing;
using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>
/// The local game package: when the game folder carries its own host, the
/// game starts it, pairs with it and stops it, so the player never sees a
/// server address or comparison code (#1564).
/// </summary>
public partial class Main
{
    private readonly LocalHostCompanion? localHost;
    private readonly ConfirmationDialog localHostFailure = new();
    private readonly ConfirmationDialog localHostStopFailure = new();
    private bool isQuittingGame;
    private bool retryQuitToLauncher;

    private LocalHostCompanion? FindBundledLocalHost()
    {
        // An explicit server address, as used for the private server, wins.
        if (TryGetCommandLineWorldUrl(out _) || OS.HasFeature("editor")) return null;
        var layout = LocalHostLayout.FindBundled(OS.GetExecutablePath(),
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData));
        return layout is null ? null : new LocalHostCompanion(layout, httpClient);
    }

    private async Task StartLocalHostAsync()
    {
        if (localHost is null) return;
        GetTree().AutoAcceptQuit = false;
        if (localHostFailure.GetParent() is null)
        {
            StyleConfirmation(localHostFailure, "Couldn't start your world", "Try Again");
            localHostFailure.CancelButtonText = "Quit Game";
            localHostFailure.Confirmed += () => _ = StartLocalHostAsync();
            localHostFailure.Canceled += QuitGame;
            AddChild(localHostFailure);
        }
        SetStatus("Starting your world server…", good: true);
        LocalHostStart start;
        try
        {
            start = await localHost.StartAsync(BuildInformation.Version, BuildInformation.SourceRevision,
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            start = new LocalHostStart(LocalHostOutcome.CouldNotStart, exception.GetType().Name);
        }
        if (!start.IsReady)
        {
            SetStatus(string.Empty, good: false);
            localHostFailure.DialogText = start.PlayerMessage;
            PopupDialog(localHostFailure);
            return;
        }
        await InitializeAsync();
        // First launch: pair this game with its own host without asking the player.
        if (registration is null && deviceKey is not null) await StartPairingAsync();
    }

    /// <summary>False leaves the ordinary comparison-code flow on screen.</summary>
    private async Task<bool> ApproveLocalPairingAsync(OwnerPairingStart pairing)
    {
        try
        {
            if (localHost is null || !await localHost.ApprovePairingAsync(pairing.PairingId, pairing.PairingCode,
                    CancellationToken.None))
                return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
        SetStatus("Setting up this game with your world server…", good: true);
        return true;
    }

    /// <summary>Saves and stops the game's own host before the window closes.</summary>
    private async void QuitGame() => await QuitGameAsync(openLauncher: false);

    private async void QuitToLauncher() => await QuitGameAsync(openLauncher: true);

    private async Task QuitGameAsync(bool openLauncher)
    {
        if (isQuittingGame) return;
        isQuittingGame = true;
        try
        {
            if (localHost is not null)
            {
                SetStatus("Saving your world…", good: true);
                if (!await localHost.StopAsync(CancellationToken.None))
                {
                    ShowLocalHostStopFailure(openLauncher);
                    return;
                }
            }
            if (openLauncher && !TryOpenLauncher()) return;
            GetTree().Quit();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowLocalHostStopFailure(openLauncher);
        }
        finally
        {
            isQuittingGame = false;
        }
    }

    private void ShowLocalHostStopFailure(bool openLauncher)
    {
        retryQuitToLauncher = openLauncher;
        if (localHostStopFailure.GetParent() is null)
        {
            StyleConfirmation(localHostStopFailure, "Couldn't save and close your world", "Try Again");
            localHostStopFailure.CancelButtonText = "Keep Game Open";
            localHostStopFailure.Confirmed += () =>
            {
                if (retryQuitToLauncher) QuitToLauncher();
                else QuitGame();
            };
            AddChild(localHostStopFailure);
        }
        SetStatus("Your world server is still running. Try closing again after fixing the save problem.", good: false);
        localHostStopFailure.DialogText = "Your world server couldn't finish saving or stopping. The game will stay open so you can try again. Check your save folder and free disk space; if startup recovery is waiting, finish it first.";
        PopupDialog(localHostStopFailure);
    }
}
