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
        dialog.GetOkButton().CustomMinimumSize = new Vector2(150, 34);
        dialog.GetCancelButton().CustomMinimumSize = new Vector2(110, 34);
        dialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
        // Centered under the centered title, with the two buttons close together beneath.
        dialog.GetLabel().HorizontalAlignment = HorizontalAlignment.Center;
        // Godot spreads the buttons with stretching spacers; keep them together in the middle instead.
        if (dialog.GetOkButton().GetParent() is HBoxContainer row)
        {
            row.Alignment = BoxContainer.AlignmentMode.Center;
            foreach (var spacer in row.GetChildren().OfType<Control>().Where(child => child is not Button))
            {
                spacer.SizeFlagsHorizontal = Control.SizeFlags.Fill;
                spacer.CustomMinimumSize = new Vector2(6, 0);
            }
        }
    }
}
