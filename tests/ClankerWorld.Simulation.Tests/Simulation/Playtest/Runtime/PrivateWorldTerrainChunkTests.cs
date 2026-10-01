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
    public void GeneratedMapRoundTripsThroughCurrentCompactChunks()
    {
        var geography = new GeographyOptions("chunked-private-world", WorldSizePreset.Small,
            WrapEastWest: true);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var state = world.ExportState();
        var newBytes = PrivateWorldRuntimeCodec.Encode(state);
        var newText = Encoding.UTF8.GetString(newBytes);

        Assert.DoesNotContain("\"tiles\"", newText, StringComparison.Ordinal);
        Assert.Contains("\"terrainEncoding\":\"terrain-chunks/v1\"", newText, StringComparison.Ordinal);
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
            SchemaVersion = PrivateWorldRuntime.MinimumSupportedStateSchemaVersion - 1,
            Bridges = null,
            BridgeTraffic = null,
        };

        var encodedError = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(unsupportedState));
        Assert.Contains($"minimum supported schema {PrivateWorldRuntime.MinimumSupportedStateSchemaVersion}",
            encodedError.Message, StringComparison.Ordinal);

        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!.AsObject();
        var savedState = document["state"]!.AsObject();
        savedState["schemaVersion"] = PrivateWorldRuntime.MinimumSupportedStateSchemaVersion - 1;
        savedState.Remove("bridges");
        savedState.Remove("bridgeTraffic");
        var unsupportedBytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        var decodeError = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(unsupportedBytes));
        Assert.Contains($"minimum supported schema {PrivateWorldRuntime.MinimumSupportedStateSchemaVersion}",
            decodeError.Message, StringComparison.Ordinal);
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
