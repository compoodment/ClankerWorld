using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void VerifyAgentCardStyleRetention()
    {
        var palette = UiTheme.Current;
        var center = cameraCenterTiles;
        var factor = uiLayer.Factor;
        var buttons = new Button[] { readThoughtsButton, instructionSuggestButton, instructionOrderButton,
            instructionQueueToggle, instructionCancelButton, allOrdersButton, conversationHistoryButton };
        var states = new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" };
        StyleBox[] Styles() => [.. buttons.SelectMany(button => states.Select(state => button.GetThemeStylebox(state))),
            agentPortraitFrame.GetThemeStylebox("panel")];
        static bool Same(StyleBox[] first, StyleBox[] second) => first.Zip(second)
            .All(pair => pair.First.GetInstanceId() == pair.Second.GetInstanceId());
        try
        {
            foreach (var (choice, rebuild) in new[] { (UiTheme.Light, false), (UiTheme.Dark, false),
                (UiTheme.Light, false), (UiTheme.Light, true) })
            {
                var previous = Styles();
                var oldTheme = UiTheme.Theme;
                if (rebuild) GetTree().Root.Theme = null;
                UiTheme.Apply(GetTree().Root, choice);
                RefreshAgentCardIcons();
                var styled = Styles();
                if (!ReferenceEquals(oldTheme, UiTheme.Theme) && previous.Zip(styled).Any(pair => pair.First.GetInstanceId() == pair.Second.GetInstanceId()))
                    throw new InvalidOperationException("A changed theme must replace every compact button state and the portrait frame.");
                foreach (var button in buttons)
                {
                    foreach (var state in states)
                    {
                        var source = UiTheme.Theme.GetStylebox(state, "TabButton");
                        var compact = button.GetThemeStylebox(state);
                        if (compact.GetType() != source.GetType() || compact.ContentMarginTop != 3 || compact.ContentMarginBottom != 3 ||
                            compact.ContentMarginLeft != source.ContentMarginLeft || compact.ContentMarginRight != source.ContentMarginRight)
                            throw new InvalidOperationException("Compact buttons must keep the new theme's styles and their short margins in every state.");
                        if (source is StyleBoxFlat sourceFlat && compact is StyleBoxFlat compactFlat &&
                            (compactFlat.BgColor != sourceFlat.BgColor || compactFlat.BorderColor != sourceFlat.BorderColor))
                            throw new InvalidOperationException("Compact buttons must use the new theme's colours in every state.");
                        if (source is StyleBoxTexture sourceTexture && compact is StyleBoxTexture compactTexture &&
                            compactTexture.Texture != sourceTexture.Texture)
                            throw new InvalidOperationException("Compact buttons must use the new theme's artwork in every state.");
                    }
                    if (button.FocusMode != Control.FocusModeEnum.All || button.ThemeTypeVariation != "TabButton")
                        throw new InvalidOperationException("Compact buttons must retain their keyboard focus and tab behavior.");
                }
                var portrait = (StyleBoxFlat)agentPortraitFrame.GetThemeStylebox("panel");
                if (portrait.BorderColor != choice.WoodEdge || portrait.BgColor !=
                    (choice.Name == "dark" ? new Color("3E5A2E") : new Color("8FB06A")))
                    throw new InvalidOperationException("The portrait must follow the light and dark palettes.");
                // These are the real camera and HUD refresh paths, including a
                // scale change; their appearance needs no new style resources.
                PanCamera(new Vector2(0.25f, 0));
                RenderWorldHud(renderedMapSnapshot!);
                SetUiFactor(factor == 1 ? 2 : 1);
                RefreshAgentCardIcons();
                if (!Same(styled, Styles()))
                    throw new InvalidOperationException("Panning, unchanged-theme observations and scaling must retain agent-card styles.");
                SetUiFactor(factor);
            }
        }
        finally
        {
            UiTheme.Apply(GetTree().Root, palette);
            SetUiFactor(factor);
            CenterCameraAt(center);
        }
    }
}
