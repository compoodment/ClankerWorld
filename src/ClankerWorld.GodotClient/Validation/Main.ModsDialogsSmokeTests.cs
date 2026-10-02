using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// The Mod Library shows each add-on as a card with its proposer and a
    /// status tag; confirmations centre their message, keep their buttons
    /// together and frame their close button; status messages carry a tick or
    /// a warning sign in plain ink and fit their message.
    /// </summary>
    private void VerifyModsDialogsAndStatus(OwnerWorldSnapshot sample)
    {
        var proposer = PanelSmokeAgent("mods-smoke-mira", "Mira", new OwnerWorldPosition(0, 0));
        RenderModLibrary(sample with
        {
            Inhabitants = [proposer],
            ContentPackages = [new OwnerWorldContentPackage("riverbend.pottery", "1.0.0", "sha256:package", "active",
                null, null, null, 1, "sha256:manifest", "Riverbend pottery", proposer.Id)],
        });
        var card = modLibraryCards.GetChildOrNull<PanelContainer>(0);
        var text = ModLibraryText();
        if (card is null || card.ThemeTypeVariation != "InsetRow" || !text.Contains("Riverbend pottery", StringComparison.Ordinal) ||
            !text.Contains("Proposed by Mira", StringComparison.Ordinal) || !text.Contains("Version 1.0.0", StringComparison.Ordinal) ||
            !text.Contains("IN USE", StringComparison.Ordinal) || text.Contains("riverbend.pottery", StringComparison.Ordinal) ||
            card.FindChildren("*", nameof(TextureRect), recursive: true, owned: false).Count < 2)
            throw new InvalidOperationException($"Mod Library must show each add-on as a card with its proposer's portrait and a status tag: {text.ReplaceLineEndings(" / ")}");
        RenderModLibrary(sample);

        var factor = uiLayer.Factor;
        foreach (var dialog in new[] { quitGameConfirmation, quitToMenuConfirmation, manualSaveLoadConfirmation, manualSaveOverwriteConfirmation, deletionConfirmation })
        {
            var row = dialog.GetOkButton().GetParent() as HBoxContainer;
            if (dialog.GetLabel().HorizontalAlignment != HorizontalAlignment.Center || row?.Alignment != BoxContainer.AlignmentMode.Center ||
                row.GetChildren().OfType<Control>().Any(child => child is not Button && child.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.Expand)) ||
                dialog.GetThemeIcon("close").GetSize() != new Vector2(22, 22) * factor ||
                dialog.GetThemeConstant("close_h_offset") <= dialog.GetThemeIcon("close").GetWidth())
                throw new InvalidOperationException($"{dialog.Title} must centre its message, keep its buttons together and frame its close button inside the frame.");
        }

        SetStatus("Mira's model is ready.", good: true);
        var shortWidth = statusToast.GetCombinedMinimumSize().X;
        if (statusIcon.Texture != PixelIcons.Texture(PixelGlyph.Check, UiTheme.Current.Good, UiTheme.Current.Good, 1) ||
            statusLabel.ThemeTypeVariation != string.Empty || statusLabel.AutowrapMode != TextServer.AutowrapMode.Off || shortWidth > 300)
            throw new InvalidOperationException($"Good news must show a tick in plain ink, in a box that fits it: width={shortWidth}.");
        SetStatus("Couldn't reach the server. Your world is safe on the host; the game will keep trying to reconnect in the background.", good: false);
        if (statusIcon.Texture == PixelIcons.Texture(PixelGlyph.Check, UiTheme.Current.Good, UiTheme.Current.Good, 1) ||
            statusLabel.AutowrapMode == TextServer.AutowrapMode.Off || statusLabel.CustomMinimumSize.X != StatusTextWidth)
            throw new InvalidOperationException("A long problem must show a warning sign and wrap at a readable width.");
        statusToast.Hide();
    }
}
