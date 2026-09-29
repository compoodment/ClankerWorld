using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Button filtersButton = new();
    private readonly PanelContainer filtersPanel = new();
    private readonly CheckButton townBorderFilter = new();
    private readonly CheckButton householdPropertyFilter = new();

    private void BuildFiltersButton()
    {
        filtersButton.Text = "Filters";
        filtersButton.TooltipText = "Show or hide map overlays.";
        StyleButton(filtersButton);
        filtersButton.Pressed += ToggleMapFilters;
        hudLeft.AddChild(filtersButton);
    }

    private void BuildMapFiltersPanel(Control canvas)
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);

        townBorderFilter.Text = "Town borders";
        townBorderFilter.TooltipText = "Outline Town borders in amber.";
        townBorderFilter.ButtonPressed = true;
        townBorderFilter.Toggled += _ => ApplyMapFiltersFromCurrentSnapshot();
        body.AddChild(townBorderFilter);

        householdPropertyFilter.Text = "Household property";
        householdPropertyFilter.TooltipText = "Tint buildings that belong to a household.";
        householdPropertyFilter.Toggled += _ => ApplyMapFiltersFromCurrentSnapshot();
        body.AddChild(householdPropertyFilter);

        var note = new Label
        {
            Text = "Only buildings that belong to a household are tinted. Unclaimed land is not marked.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(275, 0),
        };
        body.AddChild(note);
        AddClosablePanelContents(filtersPanel, "Map filters", body);
        filtersPanel.CustomMinimumSize = new Vector2(305, 0);
        filtersPanel.ZIndex = 85;
        filtersPanel.Hide();
        canvas.AddChild(filtersPanel);
    }

    private void ToggleMapFilters()
    {
        var show = !filtersPanel.Visible;
        eventsPanel.Hide();
        worldOverviewPanel.Hide();
        familyTreePanel.Hide();
        filtersPanel.Visible = show;
    }

    private void ApplyMapFiltersFromCurrentSnapshot()
    {
        if (renderedMapSnapshot is { } snapshot)
        {
            ApplyMapFilters(snapshot);
            RenderWorldInfo(snapshot);
        }
    }

    private void ApplyMapFilters(OwnerWorldSnapshot snapshot)
    {
        terrainLayer.SetTownBorders(townBorderFilter.ButtonPressed ? snapshot.Towns : []);
        terrainLayer.SetHouseholdProperties(householdPropertyFilter.ButtonPressed ? snapshot.PlacedBuildings : []);
    }
}
