using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly PanelContainer modLibraryPanel = new();
    private readonly VBoxContainer modLibraryCards = new();

    private void BuildModLibrary(VBoxContainer menuBody)
    {
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(0, 250) };
        body.AddThemeConstantOverride("separation", 8);
        body.AddChild(new Label
        {
            Text = "Add-ons in this world, such as new crafts agents have proposed. Your own library, importing and exporting come later.",
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        modLibraryCards.AddThemeConstantOverride("separation", 6);
        body.AddChild(modLibraryCards);
        AddPanelContents(modLibraryPanel, body);
        modLibraryPanel.ThemeTypeVariation = "InsetPanel";
        modLibraryPanel.Hide();
        menuBody.AddChild(modLibraryPanel);
    }

    private void ShowModLibrary()
    {
        settingsPanel.Hide();
        developerScroll.Hide();
        modLibraryPanel.Show();
        if (observationSession.Current?.Baseline.Snapshot is { } snapshot)
            RenderModLibrary(snapshot);
        ShowPauseMenuPage("Mod Library");
        ApplyResponsiveLayout();
    }

    /// <summary>
    /// One card per add-on: its name, who proposed it with their portrait,
    /// its version and whether it is in use. The internal package name stays
    /// out of sight.
    /// </summary>
    private void RenderModLibrary(OwnerWorldSnapshot snapshot)
    {
        foreach (var child in modLibraryCards.GetChildren())
        {
            modLibraryCards.RemoveChild(child);
            child.QueueFree();
        }
        var p = UiTheme.Current;
        var wood = p.Name == "dark" ? new Color("D89A5A") : new Color("B8733A");
        if (snapshot.ContentPackages.Count == 0)
        {
            var empty = new HBoxContainer();
            empty.AddThemeConstantOverride("separation", 10);
            empty.AddChild(new TextureRect { Texture = PixelIcons.Texture(PixelGlyph.Box, p.InkFaint, p.InkFaint, 2), StretchMode = TextureRect.StretchModeEnum.KeepCentered });
            empty.AddChild(new Label { Text = "Nothing has been added to this world yet.", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            modLibraryCards.AddChild(empty);
            return;
        }
        foreach (var package in snapshot.ContentPackages)
        {
            var active = package.Lifecycle.Equals("active", StringComparison.OrdinalIgnoreCase);
            var card = new PanelContainer { ThemeTypeVariation = "InsetRow" };
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(new TextureRect
            {
                Texture = PixelIcons.Texture(PixelGlyph.Box, p.Ink, active ? p.Primary : wood, 2),
                StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 2);
            text.AddChild(new Label
            {
                Text = package.DisplayName ?? GameUiText.HumanizeIdentifier(package.PackageId),
                ThemeTypeVariation = "HeadingLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(200, 0),
            });
            var meta = new HFlowContainer();
            meta.AddThemeConstantOverride("h_separation", 6);
            meta.AddThemeConstantOverride("v_separation", 2);
            if (package.ProposedByInhabitantId is { } id)
            {
                var proposer = snapshot.Inhabitants.FirstOrDefault(person => person.Id == id);
                if (proposer is not null)
                    meta.AddChild(new TextureRect { Texture = AgentPortrait(proposer, IsLiving(proposer)), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                meta.AddChild(new Label { Text = $"Proposed by {proposer?.DisplayName ?? "an agent"} ·", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            }
            meta.AddChild(new Label { Text = $"Version {package.Version}", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            text.AddChild(meta);
            row.AddChild(text);
            row.AddChild(new Label
            {
                Text = active ? "IN USE" : GameUiText.HumanizeIdentifier(package.Lifecycle).ToUpperInvariant(),
                ThemeTypeVariation = active ? "TagLabel" : "TagNoteLabel",
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            card.AddChild(row);
            modLibraryCards.AddChild(card);
        }
    }

    /// <summary>Every label in the Mod Library, for checks and assistive reading.</summary>
    private string ModLibraryText() => string.Join("\n",
        modLibraryCards.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text));
}
