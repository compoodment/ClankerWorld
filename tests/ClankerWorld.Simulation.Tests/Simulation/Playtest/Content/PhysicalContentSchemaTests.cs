using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PhysicalContentSchemaTests
{
    [Fact]
    public void Native37RoundTripsAndPreservesRejected36EvenWithoutPhysicalOrCivicRecords()
    {
        var geography = new GeographyOptions("physical-content-format", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var state = world.ExportState();
        Assert.Equal(37, state.SchemaVersion);
        Assert.Empty(state.Inhabitants);
        Assert.Empty(state.TownCouncils!);
        Assert.Empty(state.Fields!);
        Assert.Empty(state.WorldSimulation!.Carts ?? []);
        Assert.Null(state.BoatTransport);
        Assert.Null(state.Livestock);
        Assert.Empty(state.BusinessTrade!.Listings);
        Assert.Empty(state.BusinessTrade.Offers);
        Assert.Empty(state.BusinessTrade.ToolOrders);
        Assert.Empty(state.Knowledge!.Artifacts);
        Assert.DoesNotContain(state.Society.Society.Inventory.Lots,
            lot => lot.ContainerCapacity > 0 || lot.ContainerLotId is not null);

        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(state.Map.ManifestDigest, restored.ExportState().Map.ManifestDigest);
        Assert.Equal(state.Map.Resources, restored.ExportState().Map.Resources);

        var preceding = state with { SchemaVersion = 36 };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(preceding));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(preceding));
        var document = JsonNode.Parse(bytes)!;
        document["state"]!["schemaVersion"] = 36;
        var olderBytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(olderBytes));
        Assert.Contains("minimum supported schema 37", error.Message, StringComparison.Ordinal);

        var directory = Directory.CreateTempSubdirectory("physical-content-schema-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            File.WriteAllBytes(path, olderBytes);
            var saved = new PrivateWorldStateFile(path);
            Assert.Throws<InvalidDataException>(() => saved.LoadOrCreate(geography.Seed));
            Assert.Equal(olderBytes, File.ReadAllBytes(path));
            File.WriteAllBytes(path, bytes);
            using var current = saved.LoadOrCreate(geography.Seed);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(current.ExportState()));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { directory.Delete(recursive: true); }
    }
}
