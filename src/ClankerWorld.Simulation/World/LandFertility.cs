using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.World;

/// <summary>Seeded land fertility; it needs no saved value per tile. Numbers are provisional.</summary>
public sealed class LandFertility
{
    private readonly SeededMap map;
    private readonly TerrainKind[] terrain;
    private readonly Func<int, int, float> rainfall;

    public LandFertility(SeededMap map, string worldSeed)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        this.map = map;
        terrain = new TerrainKind[checked(map.Width * map.Height)];
        foreach (var tile in map.Tiles) terrain[tile.Position.Y * map.Width + tile.Position.X] = tile.Terrain;
        rainfall = GeographyGenerator.LayerNoise(worldSeed, "rainfall", 0.018f, map.Width, map.WrapsEastWest);
    }

    public int At(GridPoint point)
    {
        if (!map.Contains(point)) throw new ArgumentOutOfRangeException(nameof(point));
        var surface = map.SurfaceAt(point) ?? (terrain[point.Y * map.Width + point.X] switch
        {
            TerrainKind.Meadow => SurfaceKind.Grass,
            TerrainKind.Forest => SurfaceKind.ForestFloor,
            TerrainKind.Sand => SurfaceKind.Sand,
            TerrainKind.Snow => SurfaceKind.Snow,
            TerrainKind.Mountain or TerrainKind.Peak => SurfaceKind.Rock,
            _ => SurfaceKind.Water,
        });
        if (!map.IsBuildable(point) || surface is SurfaceKind.Sand or SurfaceKind.Rock or SurfaceKind.Snow or SurfaceKind.Water)
            return 0;

        var rain = Math.Clamp((int)MathF.Round((rainfall(point.X, point.Y) + 1f) * 127.5f), 0, 255);
        if (surface == SurfaceKind.DryScrub) return Math.Clamp(8 + rain / 20, 1, 25);
        var climate = map.ClimateAt(point) ?? ClimateZone.Temperate;
        var score = 40 + rain / 6 + (climate switch
        {
            ClimateZone.Dry => -25,
            ClimateZone.Cold => -15,
            ClimateZone.Polar => -25,
            _ => 0,
        });
        if (NearFreshWater(point)) score += 15;
        return Math.Clamp(score, 1, 100);
    }

    public bool CanFarm(GridPoint point) => map.Contains(point) && At(point) > 0;

    public static string Description(int fertility) => fertility switch
    {
        0 => "Unfarmable",
        < 35 => "Poor",
        < 55 => "Fair",
        < 75 => "Good",
        _ => "Rich",
    };

    private bool NearFreshWater(GridPoint point)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            var y = point.Y + dy;
            if (y < 0 || y >= map.Height) continue;
            for (var dx = -2; dx <= 2; dx++)
            {
                var x = point.X + dx;
                if (map.WrapsEastWest) x = (x % map.Width + map.Width) % map.Width;
                var neighbor = new GridPoint(x, y);
                if (!map.Contains(neighbor)) continue;
                if (map.HydrologyAt(neighbor) is WaterKind.River or WaterKind.Lake ||
                    map.HydrologyAt(neighbor) is null && terrain[y * map.Width + x] is TerrainKind.River or TerrainKind.Lake)
                    return true;
            }
        }
        return false;
    }
}
