using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// The hover label shows a picture of the ground, the World Map marks Towns
    /// and agents on parchment with a legend, and each map filter says what it draws.
    /// </summary>
    private void VerifyMapPanels()
    {
        if (renderedMapSnapshot is not { } shown || terrainMap is null)
            throw new InvalidOperationException("The map panel checks need a world on screen.");
        var snapshot = shown with { Inhabitants = [PanelSmokeAgent("map-smoke-mira", "Mira", new OwnerWorldPosition(1, 1))] };
        RenderMap(snapshot);
        try
        {
            VerifyMapPanels(snapshot);
        }
        finally
        {
            RenderMap(shown);
        }
    }

    private void VerifyMapPanels(OwnerWorldSnapshot snapshot)
    {
        UpdateHoverReadout(snapshot, new Vector2I(0, 0));
        if (hoverReadoutSwatch.Texture is null || hoverReadoutLabel.Text.Length == 0)
            throw new InvalidOperationException("The hover label must show a swatch of the ground beside its name.");
        UpdateHoverReadout(snapshot, null);

        var towns = snapshot.Towns.Count(town => town.BorderTiles.Count > 0);
        const int agents = 1;
        if (worldOverview.Backdrop != UiTheme.Current.Inset || worldOverview.TownMarkerCount != towns ||
            worldOverview.AgentMarkerCount != agents)
            throw new InvalidOperationException($"The World Map must sit on parchment and mark each Town and living agent: towns {worldOverview.TownMarkerCount}/{towns}, agents {worldOverview.AgentMarkerCount}/{agents}.");
        // A Town across the east-west seam is marked at the seam, not in the middle of the map.
        var seamTown = new OwnerWorldTown("town:seam", "Seam", "founded", 0, [], [], [new(0, 3), new(9, 3)]);
        var westOfSeam = seamTown with { BorderTiles = [new(0, 3), new(8, 3), new(9, 3)] };
        var inland = seamTown with { BorderTiles = [new(3, 3), new(4, 3), new(5, 3)] };
        if (TownMarkerTile(seamTown, 10, wrapsEastWest: true) is not { X: 0, Y: 3.5f } ||
            TownMarkerTile(seamTown, 10, wrapsEastWest: false) is not { X: 5, Y: 3.5f } ||
            TownMarkerTile(westOfSeam, 10, wrapsEastWest: true) is not { X: 9.5f } ||
            TownMarkerTile(inland, 10, wrapsEastWest: true) is not { X: 4.5f })
            throw new InvalidOperationException($"A Town's World Map mark must sit in the middle of its tiles, the short way round a wrapping map: {TownMarkerTile(seamTown, 10, wrapsEastWest: true)}.");
        var legend = worldOverviewPanel.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text).ToArray();
        if (!legend.Contains("Town") || !legend.Contains("Agent") || !legend.Contains("Your view"))
            throw new InvalidOperationException("The World Map must explain its marks with a legend.");

        var filterText = filtersPanel.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>().Select(label => label.Text).ToArray();
        string[] descriptions = ["A dashed line around each Town", "Tints the buildings and fields a household owns",
            "A blue edge around land titled to a Town", "Land each household may use, in its colour", "Red stripes where claims overlap or permission is under review"];
        if (descriptions.Any(line => !filterText.Contains(line)) ||
            filterText.Any(text => text.StartsWith("Only buildings", StringComparison.Ordinal) || text.Contains("separate from building", StringComparison.Ordinal)))
            throw new InvalidOperationException("Each map filter must say in one line what it draws.");
    }
}
