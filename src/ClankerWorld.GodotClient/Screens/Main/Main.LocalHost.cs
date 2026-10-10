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
    private bool isQuittingGame;

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
    private async void QuitGame()
    {
        if (localHost is null)
        {
            OpenLauncherIfAsked();
            GetTree().Quit();
            return;
        }
        if (isQuittingGame) return;
        isQuittingGame = true;
        SetStatus("Saving your world…", good: true);
        try
        {
            await localHost.StopAsync(CancellationToken.None);
        }
        finally
        {
            OpenLauncherIfAsked();
            GetTree().Quit();
        }
    }
}
