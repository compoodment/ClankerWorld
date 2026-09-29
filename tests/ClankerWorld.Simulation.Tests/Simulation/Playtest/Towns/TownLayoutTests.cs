using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLayoutTests
{
    [Fact]
    public void RankedChoicesAreLegalDistinctAndExplainTerrainAndTownFit()
    {
        var map = new SeededMap(5, 5, 0,
            (from y in Enumerable.Range(0, 5)
             from x in Enumerable.Range(0, 5)
             select new TerrainTile(new GridPoint(x, y),
                 x == 1 && y == 1 ? TerrainKind.Forest : TerrainKind.Meadow)).ToArray(),
            [], [], "fixture");
        var town = new TownRuntimeState("town:test", "Test Town", "founded", 0, [], [],
            [new GridPoint(1, 1), new GridPoint(2, 1), new GridPoint(1, 2), new GridPoint(2, 2)]);
        var costs = map.Tiles.ToDictionary(tile => tile.Position, _ => 100);
        var occupied = new GridPoint(2, 2);
        var context = new TownLayoutContext(map, town, [occupied], costs, [], []);
        var building = new BuildingDefinition("sha256:" + new string('a', 64), "test-house",
            ContentVersion.Parse("1.0.0"), "Test house", 1, 1, 1);

        var choices = TownLayoutService.RankConstructionSites(context, building);

        Assert.Equal(5, choices.Count);
        Assert.Equal(choices.Count, choices.Select(choice => choice.Position).Distinct().Count());
        Assert.DoesNotContain(choices, choice => choice.Position == occupied);
        Assert.All(choices, choice =>
        {
            Assert.True(map.IsBuildable(choice.Position));
            Assert.Contains(choice.Reasons, reason => reason.Code == TownConstructionSiteReasonCodes.FootAccess);
        });
        Assert.True(TownLayoutService.TryEvaluateConstructionSite(context, building, new GridPoint(1, 1), out var forest));
        Assert.True(TownLayoutService.TryEvaluateConstructionSite(context, building, new GridPoint(2, 1), out var meadow));
        Assert.NotNull(forest);
        Assert.NotNull(meadow);
        Assert.True(meadow.Score > forest.Score);
        Assert.Contains(forest.Reasons, reason => reason.Code == TownConstructionSiteReasonCodes.ForestPreservation);
        Assert.Contains(meadow.Reasons, reason => reason.Code == TownConstructionSiteReasonCodes.OpenMeadow);
        Assert.False(TownLayoutService.TryEvaluateConstructionSite(context, building, occupied, out _));

        var selectedId = TownConstructionCandidateIds.Building(building.CanonicalId, meadow.Position);
        Assert.True(TownConstructionCandidateIds.TryParse(selectedId, out var selected));
        Assert.Equal(building.CanonicalId, selected.DefinitionId);
        Assert.Equal(meadow.Position, selected.SitePosition);
        Assert.False(TownConstructionCandidateIds.TryParse(selectedId + ",invalid", out _));
    }
}
