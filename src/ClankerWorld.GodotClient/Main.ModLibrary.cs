using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly PanelContainer modLibraryPanel = new();
    private readonly RichTextLabel modLibraryContents = new();

    private void BuildModLibrary(VBoxContainer menuBody)
    {
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(0, 250) };
        var note = new Label
        {
            Text = "Content added to this world. Your personal library, imports and exports are not available yet.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddChild(note);
        ConfigureTextPanel(modLibraryContents, 190);
        body.AddChild(modLibraryContents);
        AddPanelContents(modLibraryPanel, "Mod Library", body);
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
        ApplyResponsiveLayout();
    }

    private void RenderModLibrary(OwnerWorldSnapshot snapshot)
    {
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
