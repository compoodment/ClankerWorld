using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static readonly string[] ModelFieldCaptions = ["Provider", "API key", "Model", "Thinking"];
    private static readonly string[] ThinkingLevels = ["Model default", "Low", "Medium", "High"];

    /// <summary>
    /// Model choices are captioned Provider, API key, Model and Thinking in
    /// both the Agent model box and Add an agent, with the same thinking levels. On an agent's own page the world's
    /// summary, Refresh and Delete this key give way to a link to the model
    /// settings, and Add an agent says plainly to click on land.
    /// </summary>
    private void VerifyModelSettingsLayout()
    {
        static string[] Captions(Control panel) => panel.FindChildren("*", nameof(Label), recursive: true, owned: false)
            .OfType<Label>().Where(label => label.ThemeTypeVariation == "DimLabel").Select(label => label.Text).ToArray();
        foreach (var (name, panel) in new (string, Control)[] { ("Agent model", cognitionSettingsPanel), ("Add an agent", founderSetupPanel) })
        {
            var captions = Captions(panel);
            if (!ModelFieldCaptions.All(captions.Contains) ||
                panel.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Any(label => label.Text.Contains("Who decides", StringComparison.Ordinal)))
                throw new InvalidOperationException($"{name} must caption its fields Provider, API key, Model and Thinking: {string.Join(", ", captions)}");
        }
        foreach (var choice in new[] { cognitionThinkingChoice, founderThinkingChoice })
        {
            var levels = Enumerable.Range(0, choice.ItemCount).Select(index => choice.GetItemText(index)).ToArray();
            if (!levels.SequenceEqual(ThinkingLevels) || !choice.ClipText)
                throw new InvalidOperationException($"Thinking must offer {string.Join(", ", ThinkingLevels)}: {string.Join(", ", levels)}");
        }
        if (!cognitionProviderChoice.ClipText || !cognitionCredentialChoice.ClipText || !cognitionRoleChoice.ClipText ||
            deleteCognitionCredentialSlotButton.GetParent() is not HFlowContainer)
            throw new InvalidOperationException("Long model choices must be cut short and the Agent model buttons must wrap, so the box fits an agent's Profile.");

        var wasAgentPage = selectedAgentModelScroll.Visible;
        try
        {
            selectedAgentModelScroll.Show();
            RenderProviderConfiguration();
            if (refreshCognitionProviderButton.Visible || deleteCognitionCredentialSlotButton.Visible || !openModelSettingsButton.Visible ||
                openModelSettingsButton.Text != "Open model settings")
                throw new InvalidOperationException("An agent's model page must offer Open model settings instead of Refresh and Delete this key.");
            selectedAgentModelScroll.Hide();
            RenderProviderConfiguration();
            if (!refreshCognitionProviderButton.Visible || openModelSettingsButton.Visible)
                throw new InvalidOperationException("The world's Agent model box keeps Refresh and has no link to itself.");
        }
        finally
        {
            selectedAgentModelScroll.Visible = wasAgentPage;
            RenderProviderConfiguration();
        }

        var hint = founderSetupHint.Text;
        ResetAddAgentPlacementHint();
        var reset = founderSetupHint.Text;
        founderSetupHint.Text = hint;
        if (reset != "Click on land to place them. Point first to see which household and Town they would join." ||
            founderPlacementIcon.Texture != PixelIcons.Texture(PixelGlyph.Mouse, UiTheme.Current.Ink, UiTheme.Current.Primary, 1))
            throw new InvalidOperationException($"Add an agent must say to click on land, beside a mouse icon: {reset}");
    }
}
