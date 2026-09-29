using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldTerrainChunkTests
{
    [Fact]
    public void GeneratedMapRoundTripsThroughCompactChunksAndLegacySaveMigrates()
    {
        var geography = new GeographyOptions("chunked-private-world", WorldSizePreset.Small,
            WrapEastWest: true);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var state = world.ExportState();
        var oldBytes = PrivateWorldRuntimeCodec.Encode(state with
        {
            SchemaVersion = 18,
            Map = state.Map with
            {
                ElevationLevels = null,
                HydrologyKinds = null,
                SurfaceKinds = null,
                VegetationKinds = null,
            },
        });
        var newBytes = PrivateWorldRuntimeCodec.Encode(state);
        var newText = Encoding.UTF8.GetString(newBytes);

        Assert.DoesNotContain("\"tiles\"", newText, StringComparison.Ordinal);
        Assert.Contains("\"terrainEncoding\":\"terrain-chunks/v1\"", newText, StringComparison.Ordinal);
        Assert.True(newBytes.Length < oldBytes.Length / 4,
            $"Chunked terrain should materially shrink this generated save: {oldBytes.Length} to {newBytes.Length} bytes.");
        var decoded = PrivateWorldRuntimeCodec.Decode(newBytes);
        Assert.Equal(state.Map.ManifestDigest, decoded.Map.ManifestDigest);
        Assert.Equal(state.Map.Tiles, decoded.Map.Tiles);
        Assert.Equal(state.Map.ClimateZones, decoded.Map.ClimateZones);
        Assert.Equal(state.Map.ElevationLevels, decoded.Map.ElevationLevels);
        Assert.Equal(state.Map.HydrologyKinds, decoded.Map.HydrologyKinds);
        Assert.Equal(state.Map.SurfaceKinds, decoded.Map.SurfaceKinds);
        Assert.Equal(state.Map.VegetationKinds, decoded.Map.VegetationKinds);
        Assert.Equal(newBytes, PrivateWorldRuntimeCodec.Encode(decoded));

        var old = PrivateWorldRuntimeCodec.Decode(oldBytes);
        using var migrated = PrivateWorldRuntime.Restore(old);
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, migrated.ExportState().SchemaVersion);
        Assert.Equal(state.Map.ManifestDigest, migrated.ExportState().Map.ManifestDigest);
        Assert.Equal(state.Map.ElevationLevels, migrated.ExportState().Map.ElevationLevels);
        Assert.Equal(state.Map.HydrologyKinds, migrated.ExportState().Map.HydrologyKinds);
        Assert.Equal(state.Map.SurfaceKinds, migrated.ExportState().Map.SurfaceKinds);
        Assert.Equal(state.Map.VegetationKinds, migrated.ExportState().Map.VegetationKinds);
        Assert.Contains("\"terrainEncoding\":\"terrain-chunks/v1\"",
            Encoding.UTF8.GetString(PrivateWorldRuntimeCodec.Encode(migrated.ExportState())), StringComparison.Ordinal);

        var oldChunked = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(
            state with
            {
                Map = state.Map with
                {
                    ElevationLevels = null,
                    HydrologyKinds = null,
                    SurfaceKinds = null,
                    VegetationKinds = null,
                }
            }));
        using var migratedChunked = PrivateWorldRuntime.Restore(oldChunked);
        Assert.Equal(state.Map.ManifestDigest, migratedChunked.ExportState().Map.ManifestDigest);
        Assert.Equal(state.Map.ElevationLevels, migratedChunked.ExportState().Map.ElevationLevels);
        Assert.Equal(state.Map.HydrologyKinds, migratedChunked.ExportState().Map.HydrologyKinds);
    }

    [Fact]
    public void DamagedChunkFailsClosedAndLeavesTheAtomicCheckpointUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "clankerworld-chunks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var geography = new GeographyOptions("damaged-chunk", WorldSizePreset.Small);
            var file = new PrivateWorldStateFile(Path.Combine(directory, "world.json"),
                newWorldPace: WorldStartPace.FounderSetup, newWorldGeography: geography);
            using var created = file.LoadOrCreate(geography.Seed);
            var bytes = File.ReadAllBytes(file.Path);
            var damaged = JsonNode.Parse(bytes)!.AsObject();
            var first = damaged["state"]!["map"]!["terrainChunks"]!.AsArray()[0]!;
            var payload = Convert.FromBase64String(first["data"]!.GetValue<string>());
            payload[0] ^= 1;
            first["data"] = Convert.ToBase64String(payload);
            var corruptBytes = Encoding.UTF8.GetBytes(damaged.ToJsonString());
            File.WriteAllBytes(file.Path, corruptBytes);

            Assert.Throws<InvalidDataException>(() => file.LoadOrCreate(geography.Seed));
            Assert.Equal(corruptBytes, File.ReadAllBytes(file.Path));
            Assert.NotEqual(bytes, corruptBytes);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DamagedButDecompressibleMapLayerFailsItsSavedIntegrityCheck()
    {
        var geography = new GeographyOptions("damaged-map-layer", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(world.ExportState()))!.AsObject();
        var map = document["state"]!["map"]!.AsObject();
        var packed = Convert.FromBase64String(map["elevationLevels"]!.GetValue<string>());
        var elevation = new byte[geography.Size == WorldSizePreset.Small ? 256 * 128 : 0];
        Assert.True(BrotliDecoder.TryDecompress(packed, elevation, out var written));
        Assert.Equal(elevation.Length, written);
        elevation[0]++;
        var corrupted = new byte[BrotliEncoder.GetMaxCompressedLength(elevation.Length)];
        Assert.True(BrotliEncoder.TryCompress(elevation, corrupted, out var compressedLength,
            quality: 4, window: 22));
        map["elevationLevels"] = Convert.ToBase64String(corrupted[..compressedLength]);

        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(
            Encoding.UTF8.GetBytes(document.ToJsonString())));
    }
}
