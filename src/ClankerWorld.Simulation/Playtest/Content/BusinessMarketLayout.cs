using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public static class BusinessMarketLayout
{
    /// <summary>Four bounded adjacent orientations, shared by ranked planning and final placement.</summary>
    public static bool TryFindPlot(SeededMap map, IReadOnlySet<GridPoint> occupied,
        GridPoint marketPosition, out GridPoint plotPosition)
    {
        var mainTiles = Enumerable.Range(0, 2).SelectMany(y => Enumerable.Range(0, 2)
            .Select(x => new GridPoint(marketPosition.X + x, marketPosition.Y + y))).ToHashSet();
        foreach (var candidate in new[]
                 {
                     new GridPoint(marketPosition.X + 2, marketPosition.Y - 5),
                     new GridPoint(marketPosition.X - 10, marketPosition.Y - 5),
                     new GridPoint(marketPosition.X - 4, marketPosition.Y + 2),
                     new GridPoint(marketPosition.X - 4, marketPosition.Y - 12),
                 })
            if (Enumerable.Range(0, BusinessContent.PlotHeight).SelectMany(y =>
                    Enumerable.Range(0, BusinessContent.PlotWidth).Select(x => new GridPoint(candidate.X + x, candidate.Y + y)))
                .All(point => map.IsBuildable(point) && !occupied.Contains(point) && !mainTiles.Contains(point)))
            {
                plotPosition = candidate;
                return true;
            }
        plotPosition = default;
        return false;
    }
}
