using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly PanelContainer modLibraryPanel = new();
    private readonly RichTextLabel modLibraryContents = new();

    private readonly VBoxContainer modLibraryCards = new();

    private void BuildModLibrary(VBoxContainer menuBody)
    {
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(0, 250) };
        body.AddThemeConstantOverride("separation", 8);
        var note = new Label
        {
            Text = "Add-ons in this world, such as new crafts agents have proposed. Your own library, importing and exporting come later.",
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddChild(note);
        ConfigureTextPanel(modLibraryContents, 190);
        modLibraryContents.Hide();
        body.AddChild(modLibraryContents);
        modLibraryCards.AddThemeConstantOverride("separation", 6);
        body.AddChild(modLibraryCards);
        AddPanelContents(modLibraryPanel, body);
        modLibraryPanel.ThemeTypeVariation = "InsetPanel";
        modLibraryPanel.Hide();
        menuBody.AddChild(modLibraryPanel);
    }

    /// <summary>One add-on: its name, whether it is in use, who proposed it, and its version.</summary>
    private void RenderModLibraryCards(OwnerWorldSnapshot snapshot)
    {
        foreach (var child in modLibraryCards.GetChildren())
        {
            modLibraryCards.RemoveChild(child);
            child.QueueFree();
        }
        if (snapshot.ContentPackages.Count == 0)
        {
            var empty = new HBoxContainer();
            empty.AddThemeConstantOverride("separation", 10);
            empty.AddChild(new TextureRect { Texture = PixelIcons.Texture(PixelGlyph.Box, UiTheme.Current.InkFaint, UiTheme.Current.InkFaint, 2), StretchMode = TextureRect.StretchModeEnum.KeepCentered });
            empty.AddChild(new Label { Text = "Nothing has been added to this world yet.", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            modLibraryCards.AddChild(empty);
            return;
        }
        foreach (var package in snapshot.ContentPackages)
        {
            var card = new PanelContainer { ThemeTypeVariation = "InsetRow", TooltipText = $"Package {package.PackageId}" };
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var active = package.Lifecycle.Equals("active", StringComparison.OrdinalIgnoreCase);
            row.AddChild(new TextureRect
            {
                Texture = PixelIcons.Texture(PixelGlyph.Box, UiTheme.Current.Ink, active ? AccentGreen : AccentWood, 2),
                StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 2);
            text.AddChild(new Label { Text = package.DisplayName ?? GameUiText.HumanizeIdentifier(package.PackageId), ThemeTypeVariation = "HeadingLabel" });
            var meta = new HBoxContainer();
            meta.AddThemeConstantOverride("separation", 6);
            if (package.ProposedByInhabitantId is { } id && snapshot.Inhabitants.FirstOrDefault(person => person.Id == id) is { } proposer)
            {
                meta.AddChild(new TextureRect { Texture = RosterPortrait(proposer, IsLiving(proposer)), StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                meta.AddChild(new Label { Text = $"Proposed by {proposer.DisplayName} ·", ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
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

    private void RenderModLibrary(OwnerWorldSnapshot snapshot)
    {
        RenderModLibraryCards(snapshot);
        var packages = snapshot.ContentPackages;
        modLibraryContents.Text = packages.Count == 0
            ? "Nothing has been added to this world yet."
            : string.Join("\n\n", packages.Select(package =>
            {
                var proposer = package.ProposedByInhabitantId is { } id
                    ? snapshot.Inhabitants.FirstOrDefault(inhabitant => inhabitant.Id == id)?.DisplayName ?? id
                    : null;
                return $"{package.DisplayName ?? package.PackageId} · {GameUiText.HumanizeIdentifier(package.Lifecycle)}" +
                    (proposer is null ? string.Empty : $" · proposed by {proposer}") +
                    $"\nPackage: {package.PackageId} · Version: {package.Version}";
            }));
    }
}
