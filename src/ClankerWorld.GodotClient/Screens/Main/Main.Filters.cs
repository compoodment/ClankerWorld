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

        // Filters start off, so the map starts clean.
        townBorderFilter.TooltipText = "Show Town borders as a dashed line.";
        townBorderFilter.Toggled += _ => ApplyMapFiltersFromCurrentSnapshot();
        body.AddChild(FilterRow(townBorderFilter, border: true, "Town borders", "A dashed line around each Town"));

        householdPropertyFilter.TooltipText = "Tint buildings and fields that belong to a household.";
        householdPropertyFilter.Toggled += _ => ApplyMapFiltersFromCurrentSnapshot();
        body.AddChild(FilterRow(householdPropertyFilter, border: false, "Household property", "Tints the buildings and fields a household owns"));
        AddClosablePanelContents(filtersPanel, "Map filters", body);
        filtersPanel.CustomMinimumSize = new Vector2(320, 0);
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

    /// <summary>
    /// Placing a founder or an added agent shows Town borders and household
    /// property, so the owner can see where the agent will belong, without
    /// switching the Filters on.
    /// </summary>
    private bool ShowsPlacementOverlays => founderSetupPanel.Visible;

    private void ApplyMapFilters(OwnerWorldSnapshot snapshot)
    {
        terrainLayer.SetTownBorders(townBorderFilter.ButtonPressed || ShowsPlacementOverlays ? snapshot.Towns : []);
        terrainLayer.SetHouseholdProperties(householdPropertyFilter.ButtonPressed || ShowsPlacementOverlays
            ? snapshot.PlacedBuildings : [], householdPropertyFilter.ButtonPressed || ShowsPlacementOverlays
            ? snapshot.Fields : []);
    }
}
