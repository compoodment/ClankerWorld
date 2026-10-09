using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly VBoxContainer generationSettingsRows = new();
    private readonly Label generationSize = new();
    private readonly Label generationClimate = new();
    private readonly Label generationWrapping = new();
    private readonly Label generationSeed = new();
    private readonly Label generationSettingsHint = new()
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        ThemeTypeVariation = "DimLabel",
    };

    private void BuildGenerationSettings()
    {
        generationSettingsRows.AddChild(MetricRow("Size", generationSize));
        generationSettingsRows.AddChild(MetricRow("Climate", generationClimate));
        generationSettingsRows.AddChild(MetricRow("Wrapping", generationWrapping));
        generationSettingsRows.AddChild(MetricRow("Seed", generationSeed));
        var body = new VBoxContainer();
        body.AddChild(generationSettingsRows);
        body.AddChild(generationSettingsHint);
        worldSettingsContent.AddChild(NewPanel("World generation", body));
        RenderGenerationSettings(null);
    }

    private void RenderGenerationSettings(OwnerWorldSnapshot? snapshot)
    {
        var generation = snapshot?.Generation;
        generationSettingsRows.Visible = generation is not null;
        generationSettingsHint.Text = generation is null
            ? "Generation choices are not available for this world from the connected host."
            : "These choices were set when the world was created and cannot be changed.";
        // Clear the previous world's values even when the new host reports none.
        generationSize.Text = generation?.Size ?? string.Empty;
        generationSeed.Text = generation?.Seed ?? string.Empty;
        generationWrapping.Text = generation is null ? string.Empty
            : generation.WrapEastWest ? "East/west only" : "Off";
        generationClimate.Text = generation is null ? string.Empty
            : (generation.ClimateMode == "Balanced" ? "Balanced"
                : $"{generation.ClimateMode} · {generation.SelectedClimate}") +
                (generation.LatitudeCooling ? " · Colder toward the poles" : " · Latitude cooling off");
    }
}
