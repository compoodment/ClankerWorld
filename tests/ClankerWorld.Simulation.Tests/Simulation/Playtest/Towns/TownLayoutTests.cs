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

    [Fact]
    public void FullMarketFootprintLegalityPrecedesRankedCandidateCap()
    {
        var map = MarketLayoutMeadowMap(40, 12);
        var definition = MarketContent.Hall2x2();
        var partialHallOrigins = Enumerable.Range(0, TownLayoutService.MaximumCandidateLimit)
            .Select(index => new GridPoint(2 + 3 * index, 2)).ToArray();
        var lawfulMarketOrigin = new GridPoint(30, 3);
        var lawfulMarketTiles = MarketContent.SiteTiles(lawfulMarketOrigin).ToHashSet();
        Assert.Equal(32, lawfulMarketTiles.Count);
        var titled = lawfulMarketTiles.Concat(partialHallOrigins.SelectMany(origin =>
            Enumerable.Range(0, 2).SelectMany(y => Enumerable.Range(0, 2)
                .Select(x => new GridPoint(origin.X + x, origin.Y + y))))).ToHashSet();
        var costs = map.Tiles.ToDictionary(tile => tile.Position, _ => 9_000);
        foreach (var origin in partialHallOrigins) costs[origin] = 0;
        var hallOnly = new TownLayoutContext(map, null, [], costs, [], [],
            requiredLandTiles: titled, requiredEntranceOffset: new(1, 2));
        var hallOnlyRank = TownLayoutService.RankConstructionSites(hallOnly, definition,
            TownLayoutService.MaximumCandidateLimit);

        // All eight short-route anchors are legal for just the 2x2 Hall and
        // outrank the farther full Market, exhausting the ranker's entire cap.
        Assert.Equal(partialHallOrigins, hallOnlyRank.Select(choice => choice.Position).ToArray());
        Assert.DoesNotContain(hallOnlyRank, choice => choice.Position == lawfulMarketOrigin);

        var completeMarket = new TownLayoutContext(map, null, [], costs, [], [],
            requiredLandTiles: titled, requiredEntranceOffset: new(1, 2),
            requiredFootprintOffsets: MarketContent.SiteTiles(new(0, 0)));
        var completeRank = TownLayoutService.RankConstructionSites(completeMarket, definition,
            TownLayoutService.MaximumCandidateLimit);

        // The full 32-tile title requirement must be applied before truncation.
        Assert.Equal(lawfulMarketOrigin, Assert.Single(completeRank).Position);
    }

    [Fact]
    public void MarketRoadsUsePlazaAislesAndActualDoorButNeverHallOrFutureStalls()
    {
        var map = MarketLayoutMeadowMap(12, 10);
        var definition = MarketContent.Hall2x2();
        var origin = new GridPoint(5, 2);
        var shape = MarketContent.SiteTiles(new(0, 0)).ToHashSet();
        var titled = MarketContent.SiteTiles(origin).ToHashSet();
        var costs = map.Tiles.ToDictionary(tile => tile.Position, _ => 100);
        var plannedStalls = Enumerable.Range(0, MarketContent.MaximumStalls)
            .Select(slot => MarketContent.StallSite(new(0, 0), slot)).ToHashSet();
        var aisleOffsets = MarketContent.PlazaTiles(new(0, 0)).Except(plannedStalls).ToHashSet();
        Assert.Equal(32, shape.Count);
        Assert.Equal(8, plannedStalls.Count);
        Assert.Equal(20, aisleOffsets.Count);
        var actualDoor = MarketContent.HallEntrance(origin);
        Assert.Equal(new GridPoint(origin.X + 1, origin.Y + 2), actualDoor);
        Assert.Contains(new GridPoint(1, 2), aisleOffsets);

        TownLayoutContext Context(IEnumerable<GridPoint> occupied, IEnumerable<GridPoint> roads,
            IReadOnlyDictionary<GridPoint, int> reachable) => new(map, null, occupied, reachable, [], [],
                roadTiles: roads, requiredLandTiles: titled, requiredEntranceOffset: new(1, 2),
                requiredFootprintOffsets: shape, permittedRoadOffsets: aisleOffsets);

        foreach (var offset in aisleOffsets)
        {
            var road = new GridPoint(origin.X + offset.X, origin.Y + offset.Y);
            Assert.True(TownLayoutService.TryEvaluateConstructionSite(Context([road], [road], costs),
                definition, origin, out _), $"A plaza aisle Road at offset {offset} must be allowed.");
        }
        var hallOffsets = Enumerable.Range(0, 2).SelectMany(y => Enumerable.Range(0, 2)
            .Select(x => new GridPoint(x, y)));
        foreach (var offset in hallOffsets.Concat(plannedStalls))
        {
            var road = new GridPoint(origin.X + offset.X, origin.Y + offset.Y);
            Assert.False(TownLayoutService.TryEvaluateConstructionSite(Context([road], [road], costs),
                definition, origin, out _), $"A Hall or future-stall Road at offset {offset} must be refused.");
        }
        // The Road exception does not let another occupant block the real door.
        Assert.False(TownLayoutService.TryEvaluateConstructionSite(Context([actualDoor], [], costs),
            definition, origin, out _));
        var missingActualDoor = costs.ToDictionary(item => item.Key, item => item.Value);
        missingActualDoor.Remove(actualDoor);
        Assert.Contains(new GridPoint(origin.X + 1, origin.Y + 4), missingActualDoor.Keys);
        Assert.False(TownLayoutService.TryEvaluateConstructionSite(Context([], [], missingActualDoor),
            definition, origin, out _));
    }

    private static SeededMap MarketLayoutMeadowMap(int width, int height) => new(width, height, 0,
        (from y in Enumerable.Range(0, height)
         from x in Enumerable.Range(0, width)
         select new TerrainTile(new GridPoint(x, y), TerrainKind.Meadow)).ToArray(),
        [], [], "market-layout-fixture");
}
