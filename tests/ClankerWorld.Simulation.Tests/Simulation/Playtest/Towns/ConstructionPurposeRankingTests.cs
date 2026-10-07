using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ConstructionPurposeRankingTests
{
    [Fact]
    public void PurposeReasonsKeepTownTagDistanceAndIdentityTieRulesAcrossQueries()
    {
        var map = new SeededMap(40, 20, 0,
            (from y in Enumerable.Range(0, 20)
             from x in Enumerable.Range(0, 40)
             select new TerrainTile(new(x, y), TerrainKind.Meadow)).ToArray(), [], [], "fixture")
        { WrapsEastWest = true };
        var anchor = new GridPoint(39, 10);
        var town = new TownRuntimeState("town:test", "Test Town", "founded", 0, [], [], [anchor]);
        var costs = new Dictionary<GridPoint, int> { [anchor] = 100 };
        var target = Definition("target", ["smith", "craft"]);
        var craftOnly = Definition("craft-only", ["craft"]);
        var smith = Definition("smith", ["smith"]);
        var craft = Definition("craft", ["craft"]);
        var unrelated = Definition("unrelated", ["farm"]);
        TownLayoutContext Context(IEnumerable<TownLayoutBuilding> buildings) => new(map, town, [], costs, [], buildings);
        var plain = Assert.Single(TownLayoutService.RankConstructionSites(Context([]), target));

        foreach (var distance in new[] { 2, 3, 5, 6 })
        {
            var site = map.WrapColumn(new GridPoint(anchor.X + distance, anchor.Y));
            TownLayoutBuilding Building(string id, BuildingDefinition definition, GridPoint position, string townId) =>
                new(new(id, definition.CanonicalId, position, 0, townId), definition);
            var a = Building("a-smith", smith, site, town.Id);
            var b = Building("b-craft", craft, site, town.Id);
            var ignored = new[] { Building("foreign", smith, anchor, "town:other"),
                Building("unrelated", unrelated, anchor, town.Id) };
            var context = Context(ignored.Concat([b, a]));
            var chosen = Assert.Single(TownLayoutService.RankConstructionSites(context, target));
            Assert.Equal(plain.Score + (distance <= 2 ? 10 : distance <= 5 ? 5 : 0), chosen.Score);
            var reasons = chosen.Reasons.Where(reason => reason.Code == TownConstructionSiteReasonCodes.PurposeCluster);
            if (distance <= 5)
            {
                Assert.Equal("Near Town's existing smith building.", Assert.Single(reasons).Description);
                Assert.Equal("Near Town's existing craft building.", Assert.Single(
                    Assert.Single(TownLayoutService.RankConstructionSites(context, craftOnly)).Reasons,
                    reason => reason.Code == TownConstructionSiteReasonCodes.PurposeCluster).Description);
                Assert.Equal("Near Town's existing craft building.", Assert.Single(
                    Assert.Single(TownLayoutService.RankConstructionSites(Context(ignored.Append(b)), target)).Reasons,
                    reason => reason.Code == TownConstructionSiteReasonCodes.PurposeCluster).Description);
            }
            else
                Assert.Empty(reasons);
            Assert.DoesNotContain(Assert.Single(TownLayoutService.RankConstructionSites(Context(ignored), target)).Reasons,
                reason => reason.Code == TownConstructionSiteReasonCodes.PurposeCluster);
        }
    }

    private static BuildingDefinition Definition(string id, string[] tags) => new("sha256:" + new string('a', 64), id,
        ContentVersion.Parse("1.0.0"), id, 1, 1, 1, tags: tags);
}
