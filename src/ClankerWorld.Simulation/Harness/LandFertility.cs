using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Harness;

public enum LandFertility : byte { None, Poor, Fair, Good, Rich }

/// <summary>Derived land quality; never a resource object or saved tile mutation.</summary>
public static class LandFertilityRules
{
    public static bool IsFarmable(SeededMap map, GridPoint point) =>
        map.IsBuildable(point) && map.SurfaceAt(point) is not
            (SurfaceKind.Sand or SurfaceKind.Rock or SurfaceKind.Snow or SurfaceKind.Water) &&
        map.TerrainKindAt(point) is not (TerrainKind.Sand or TerrainKind.Snow) &&
        map.ClimateAt(point) is not (ClimateZone.Cold or ClimateZone.Polar);

    public static LandFertility At(SeededMap map, GridPoint point)
    {
        if (!map.IsLand(point)) return LandFertility.None;
        if (!IsFarmable(map, point)) return LandFertility.Poor;
        var rainfall = map.RainfallAt(point) ?? (map.ClimateAt(point) == ClimateZone.Dry ? 40 : 150);
        var hash = unchecked(map.FertilitySeed ^ (uint)(point.X * 73_856_093) ^ (uint)(point.Y * 19_349_663));
        hash = unchecked((hash ^ (hash >> 16)) * 0x7feb352d);
        var score = (map.SurfaceAt(point) == SurfaceKind.DryScrub ? 15 : 45) + rainfall / 7 + (int)(hash % 16);
        for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                var neighbor = map.WrapColumn(new GridPoint(point.X + dx, point.Y + dy));
                if (map.Contains(neighbor) && map.HydrologyAt(neighbor) is WaterKind.River or WaterKind.Lake)
                    return Classify(score + 15);
            }
        return Classify(score);
    }

    public static int YieldPercent(LandFertility fertility) => fertility switch
    {
        LandFertility.Poor => 60,
        LandFertility.Fair => 85,
        LandFertility.Good => 100,
        LandFertility.Rich => 125,
        _ => 0,
    };

    private static LandFertility Classify(int score) => score switch
    {
        < 40 => LandFertility.Poor,
        < 60 => LandFertility.Fair,
        < 80 => LandFertility.Good,
        _ => LandFertility.Rich,
    };
}
