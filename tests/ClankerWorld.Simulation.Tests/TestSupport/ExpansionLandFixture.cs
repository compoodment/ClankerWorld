using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

internal static class ExpansionLandFixture
{
    // Material, storage and construction fixtures begin with recorded permission.
    // HouseholdLandGrantTests exercises the actual personal request/vote/consent path.
    public static PrivateWorldRuntimeState WithRights(PrivateWorldRuntimeState state,
        PlacedBuilding building, IEnumerable<GridPoint> envelope)
    {
        var tiles = envelope.ToArray();
        var titles = state.TownLandTitles!.ToList();
        foreach (var plot in TownLandRightsRules.ConnectedPlots(state.Map,
                     tiles.Where(tile => !titles.Any(title => title.Tiles.Contains(tile)))))
            titles.Add(new("title:expansion-fixture:" + titles.Count, building.TownId!, plot, state.Society.Society.WorldTick));
        var rights = state.HouseholdLandUseRights!.ToList();
        if (building.HouseholdId is { } household)
            foreach (var plot in TownLandRightsRules.ConnectedPlots(state.Map,
                         tiles.Where(tile => !rights.Any(right => right.Tiles.Contains(tile)))))
                rights.Add(new("use:expansion-fixture:" + rights.Count, building.TownId!, household, plot,
                    state.Society.Society.WorldTick, "expansion_test_fixture"));
        return state with
        {
            TownLandTitles = titles.OrderBy(t => t.Id, StringComparer.Ordinal).ToArray(),
            HouseholdLandUseRights = rights.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray(),
            Towns = state.Towns!.Select(town => town.Id == building.TownId ? town with
            {
                BorderTiles = TownLandRightsRules.OrderTiles(town.BorderTiles.Concat(tiles).Distinct()),
            } : town).ToArray(),
        };
    }

}
