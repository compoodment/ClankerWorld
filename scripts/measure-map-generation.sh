#!/usr/bin/env bash
# Measure generation and write public map fixtures for Godot's CPU draw probe.
set -eu
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
output_dir=${1:-"${TMPDIR:-/tmp}/clankerworld-map-measurements"}
variant=${2:-current}
mkdir -p "$output_dir"
output_dir=$(cd "$output_dir" && pwd)
measurement_dir=$(mktemp -d)
trap 'rm -rf "$measurement_dir"' EXIT
cd "$repo_root"
dotnet new console --name MapGenerationMeasurement --output "$measurement_dir" --framework net10.0 --no-restore > /dev/null
dotnet add "$measurement_dir/MapGenerationMeasurement.csproj" reference "$repo_root/src/ClankerWorld.Simulation/ClankerWorld.Simulation.csproj" > /dev/null
cat > "$measurement_dir/Program.cs" <<'CSHARP'
using System.Diagnostics;
using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

var outputPrefix = args.Length > 0 ? args[0] : "before";
foreach (var options in new[] {
    new GeographyOptions("probe-a", WorldSizePreset.Small, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion),
    new GeographyOptions("probe-b", WorldSizePreset.Small, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion),
    new GeographyOptions("terrain-dry-cacti", WorldSizePreset.Small, ClimateMode: ClimateMode.Uniform,
        SelectedClimate: ClimateZone.Dry, LatitudeCooling: false, HydrologyVersion: GeographyGenerator.CurrentHydrologyVersion),
})
{
    _ = GeneratedCampMapGenerator.Generate(options);
    var times = new List<double>();
    SeededMap? map = null;
    for (var trial = 0; trial < 5; trial++)
    {
        var timer = Stopwatch.StartNew();
        map = GeneratedCampMapGenerator.Generate(options);
        timer.Stop();
        times.Add(timer.Elapsed.TotalMilliseconds);
    }
    var generated = map!;
    var trees = generated.Resources.Where(resource => resource.TreeKind is not null).Select(resource => resource.Position).ToHashSet();
    var forestGrass = generated.Tiles.Where(tile => generated.SurfaceKinds![tile.Position.Y * generated.Width + tile.Position.X] == (byte)SurfaceKind.Grass &&
        generated.VegetationKinds![tile.Position.Y * generated.Width + tile.Position.X] == (byte)VegetationCover.Forest).Select(tile => tile.Position).ToArray();
    var forestFloor = generated.Tiles.Where(tile => generated.SurfaceKinds![tile.Position.Y * generated.Width + tile.Position.X] == (byte)SurfaceKind.ForestFloor).Select(tile => tile.Position).ToArray();
    var shore = generated.Tiles.Where(tile => generated.HydrologyKinds![tile.Position.Y * generated.Width + tile.Position.X] == (byte)WaterKind.Land &&
        new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }.Any(offset => {
            var x = (tile.Position.X + offset.Item1 + generated.Width) % generated.Width;
            var y = tile.Position.Y + offset.Item2;
            return y >= 0 && y < generated.Height && generated.HydrologyKinds[y * generated.Width + x] == (byte)WaterKind.Ocean;
        })).Select(tile => tile.Position).ToArray();
    var cacti = generated.Tiles.Where(tile => generated.VegetationKinds![tile.Position.Y * generated.Width + tile.Position.X] == (byte)VegetationCover.Cactus).Select(tile => tile.Position).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new {
        options.Seed, generated.Width, generated.Height, WarmedRunsMs = times,
        MedianGenerationMs = times.Order().ElementAt(2), Resources = generated.Resources.Count,
        MaximumChunkResources = generated.Resources.GroupBy(resource => (resource.Position.X / GeographyGenerator.ChunkSize, resource.Position.Y / GeographyGenerator.ChunkSize)).Max(group => group.Count()),
        ConfigResourceBudget = WorldSystemsConfig.Default.MaxResourcesPerChunk,
        ForestGrassTiles = forestGrass.Length, GrassForestTrees = forestGrass.Count(trees.Contains),
        ForestFloorTiles = forestFloor.Length, FloorTrees = forestFloor.Count(trees.Contains),
        OceanShoreTiles = shore.Length, SandOnOceanShore = shore.Count(point => generated.SurfaceKinds![point.Y * generated.Width + point.X] == (byte)SurfaceKind.Sand),
        CactusTiles = cacti.Length, CactusOutsideDesertSand = cacti.Count(point => generated.SurfaceKinds![point.Y * generated.Width + point.X] != (byte)SurfaceKind.Sand || generated.ClimateZones![point.Y * generated.Width + point.X] != (byte)ClimateZone.Dry),
    }));
    File.WriteAllText(outputPrefix + "-" + options.Seed + ".json", JsonSerializer.Serialize(new {
        generated.Width, generated.Height, generated.WrapsEastWest,
        Terrain = Convert.ToBase64String(generated.Tiles.OrderBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.X).Select(tile => (byte)tile.Terrain).ToArray()),
        Climate = Convert.ToBase64String(generated.ClimateZones!), Elevation = Convert.ToBase64String(generated.ElevationLevels!),
        Hydrology = Convert.ToBase64String(generated.HydrologyKinds!), Surface = Convert.ToBase64String(generated.SurfaceKinds!),
        Vegetation = Convert.ToBase64String(generated.VegetationKinds!),
        Resources = generated.Resources.Select(resource => new { resource.Id, resource.Kind, resource.Position, resource.IsRenewable, State = "available", resource.TreeKind, resource.NaturalObjectKind }),
    }));
}
CSHARP
dotnet run --project "$measurement_dir/MapGenerationMeasurement.csproj" --configuration Release -- "$output_dir/$variant"
