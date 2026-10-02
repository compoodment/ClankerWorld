using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Button filtersButton = new();
    private readonly PanelContainer filtersPanel = new();
    private readonly CheckButton townBorderFilter = new();
    private readonly CheckButton householdPropertyFilter = new();
    private readonly CheckButton townLandTitleFilter = new();
    private readonly CheckButton householdLandUseFilter = new();
    private readonly CheckButton disputedLandFilter = new();

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
        townBorderFilter.Text = "Town borders";
        townBorderFilter.TooltipText = "Show Town borders as a dashed line.";
        townBorderFilter.Toggled += _ => ApplyMapFiltersFromCurrentSnapshot();
        body.AddChild(townBorderFilter);

        householdPropertyFilter.Text = "Household property";
        householdPropertyFilter.TooltipText = "Tint buildings and fields that belong to a household.";
        householdPropertyFilter.Toggled += _ => ApplyMapFiltersFromCurrentSnapshot();
        body.AddChild(householdPropertyFilter);

        townLandTitleFilter.Text = "Town land title";
        townLandTitleFilter.TooltipText = "Show land formally titled to each Town.";
        townLandTitleFilter.Toggled += _ => ApplyMapFiltersFromCurrentSnapshot();
        body.AddChild(townLandTitleFilter);

        householdLandUseFilter.Text = "Household land use";
        householdLandUseFilter.TooltipText = "Show recorded household use rights and pending requests.";
        householdLandUseFilter.Toggled += _ => ApplyMapFiltersFromCurrentSnapshot();
        body.AddChild(householdLandUseFilter);

        disputedLandFilter.Text = "Disputed land";
        disputedLandFilter.TooltipText = "Stripe land with conflicting household claims.";
        disputedLandFilter.Toggled += _ => ApplyMapFiltersFromCurrentSnapshot();
        body.AddChild(disputedLandFilter);

        var note = new Label
        {
            Text = "Town title, household use and disputes are separate from building and field ownership.",
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

    /// <summary>
    /// Placing a founder or an added agent shows recorded Town and household
    /// claims, so the owner can see where the agent will belong without
    /// switching the Filters on.
    /// </summary>
    private bool ShowsPlacementOverlays => founderSetupPanel.Visible;

    private void ApplyMapFilters(OwnerWorldSnapshot snapshot)
    {
        terrainLayer.SetTownBorders(townBorderFilter.ButtonPressed || ShowsPlacementOverlays ? snapshot.Towns : []);
        terrainLayer.SetHouseholdProperties(householdPropertyFilter.ButtonPressed || ShowsPlacementOverlays
            ? snapshot.PlacedBuildings : [], householdPropertyFilter.ButtonPressed || ShowsPlacementOverlays
            ? snapshot.Fields : []);
        terrainLayer.SetTownLandTitles(townLandTitleFilter.ButtonPressed || ShowsPlacementOverlays
            ? snapshot.TownLandTitles : []);
        terrainLayer.SetHouseholdLandUses(householdLandUseFilter.ButtonPressed || ShowsPlacementOverlays
            ? snapshot.HouseholdLandUseRights : [], householdLandUseFilter.ButtonPressed || ShowsPlacementOverlays
            ? snapshot.HouseholdLandUseRequests : []);
        terrainLayer.SetDisputedLand(disputedLandFilter.ButtonPressed || ShowsPlacementOverlays
            ? snapshot.HouseholdLandUseRequests : []);
    }
}
