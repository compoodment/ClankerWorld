using Godot;

namespace ClankerWorld.GodotClient;

/// <summary>Confirmation dialogs that name their action; the window theme styles them like the game's panels.</summary>
public partial class Main
{
    /// <summary>
    /// Names the action on a confirmation's button, so "Quit to Menu" reads as
    /// what happens instead of OK.
    /// </summary>
    private static void StyleConfirmation(ConfirmationDialog dialog, string title, string confirm)
    {
        dialog.Title = title;
        dialog.OkButtonText = confirm;
        dialog.CancelButtonText = "Cancel";
        StyleButton(dialog.GetOkButton(), primary: true);
        StyleButton(dialog.GetCancelButton());
        dialog.GetOkButton().CustomMinimumSize = new Vector2(150, 36);
        dialog.GetCancelButton().CustomMinimumSize = new Vector2(110, 36);
        dialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
}
