using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldTerrainChunkTests
{
    private static readonly JsonSerializerOptions RawStateJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void GeneratedMapRoundTripsThroughCurrentCompactChunks()
    {
        var geography = new GeographyOptions("chunked-private-world", WorldSizePreset.Small,
            WrapEastWest: true);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var state = world.ExportState();
        var newBytes = PrivateWorldRuntimeCodec.Encode(state);
        var rawTerrainBytes = JsonSerializer.SerializeToUtf8Bytes(state.Map.Tiles, RawStateJsonOptions);
        var newText = Encoding.UTF8.GetString(newBytes);

        Assert.DoesNotContain("\"tiles\"", newText, StringComparison.Ordinal);
        Assert.Contains("\"terrainEncoding\":\"terrain-chunks/v1\"", newText, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(newBytes);
        var terrainChunkBytes = Encoding.UTF8.GetByteCount(document.RootElement.GetProperty("state")
            .GetProperty("map").GetProperty("terrainChunks").GetRawText());
        Assert.True(terrainChunkBytes < rawTerrainBytes.Length / 4,
            $"Chunked terrain should materially shrink the raw terrain: {terrainChunkBytes} to {rawTerrainBytes.Length} bytes.");
        var decoded = PrivateWorldRuntimeCodec.Decode(newBytes);
        Assert.Equal(state.Map.ManifestDigest, decoded.Map.ManifestDigest);
        Assert.Equal(state.Map.Tiles, decoded.Map.Tiles);
        Assert.Equal(state.Map.ClimateZones, decoded.Map.ClimateZones);
        Assert.Equal(state.Map.ElevationLevels, decoded.Map.ElevationLevels);
        Assert.Equal(state.Map.HydrologyKinds, decoded.Map.HydrologyKinds);
        Assert.Equal(state.Map.SurfaceKinds, decoded.Map.SurfaceKinds);
        Assert.Equal(state.Map.VegetationKinds, decoded.Map.VegetationKinds);
        Assert.Equal(newBytes, PrivateWorldRuntimeCodec.Encode(decoded));
    }

    [Fact]
    public void OlderAlphaCheckpointIsRefusedAtTheCurrentSchemaFloor()
    {
        var geography = new GeographyOptions("below-current-save-schema", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var state = world.ExportState();
        var unsupportedState = state with
        {
            SchemaVersion = PrivateWorldRuntime.StateSchemaVersion - 1,
            Bridges = null,
            BridgeTraffic = null,
        };

        var encodedError = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(unsupportedState));
        Assert.Contains($"minimum supported schema {PrivateWorldRuntime.StateSchemaVersion}",
            encodedError.Message, StringComparison.Ordinal);
        var restoreError = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(unsupportedState));
        Assert.Contains($"minimum supported schema {PrivateWorldRuntime.StateSchemaVersion}",
            restoreError.Message, StringComparison.Ordinal);

        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!.AsObject();
        var savedState = document["state"]!.AsObject();
        savedState["schemaVersion"] = PrivateWorldRuntime.StateSchemaVersion - 1;
        savedState.Remove("bridges");
        savedState.Remove("bridgeTraffic");
        var unsupportedBytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        var decodeError = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(unsupportedBytes));
        Assert.Contains($"minimum supported schema {PrivateWorldRuntime.StateSchemaVersion}",
            decodeError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentGeographyWithoutItsSavedLayersIsRefusedAndPreserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "clankerworld-missing-map-layers-" + Guid.NewGuid().ToString("N"));
        try
        {
            var geography = new GeographyOptions("missing-current-map-layers", WorldSizePreset.Small);
            var file = new PrivateWorldStateFile(Path.Combine(directory, "world.json"),
                newWorldPace: WorldStartPace.FounderSetup, newWorldGeography: geography);
            using var created = file.LoadOrCreate(geography.Seed);
            var state = created.ExportState();
            var missingLayersState = state with
            {
                Map = state.Map with
                {
                    ElevationLevels = null,
                    HydrologyKinds = null,
                    SurfaceKinds = null,
                    VegetationKinds = null,
                },
            };
            var encodeError = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(missingLayersState));
            Assert.Contains("missing required map layers", encodeError.Message, StringComparison.Ordinal);

            var document = JsonNode.Parse(File.ReadAllBytes(file.Path))!.AsObject();
            var map = document["state"]!["map"]!.AsObject();
            map["elevationLevels"] = null;
            map["hydrologyKinds"] = null;
            map["surfaceKinds"] = null;
            map["vegetationKinds"] = null;
            map["mapLayersSha256"] = null;
            var missingLayersBytes = Encoding.UTF8.GetBytes(document.ToJsonString());
            File.WriteAllBytes(file.Path, missingLayersBytes);

            var decodeError = Assert.Throws<InvalidDataException>(() => file.LoadOrCreate(geography.Seed));
            Assert.Contains("missing required map layers", decodeError.Message, StringComparison.Ordinal);
            Assert.Equal(missingLayersBytes, File.ReadAllBytes(file.Path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecomputedLayerChecksumCannotReplaceDeterministicCurrentGeography()
    {
        var geography = new GeographyOptions("changed-current-map-layer", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var state = world.ExportState();
        var elevation = state.Map.ElevationLevels!.ToArray();
        elevation[0] ^= 1;
        var tamperedState = state with { Map = state.Map with { ElevationLevels = elevation } };

        Assert.Equal(state.Map.ManifestDigest, tamperedState.Map.ManifestDigest);
        var decoded = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(tamperedState));
        Assert.NotEqual(MapLayerManifestCodec.Digest(state.Map), MapLayerManifestCodec.Digest(decoded.Map));
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(decoded));
        Assert.Contains("map does not match deterministic regeneration", error.Message, StringComparison.Ordinal);
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
