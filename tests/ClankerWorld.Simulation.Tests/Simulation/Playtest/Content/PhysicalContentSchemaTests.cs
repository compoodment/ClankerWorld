using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PhysicalContentSchemaTests
{
    [Theory]
    [InlineData("clothingLotId")]
    [InlineData("wornClothingLotId")]
    public void BothPrior37EquipmentShapesAreRefusedBeforeSelectedGearCanBeLost(string clothingProperty)
    {
        using var world = new PrivateWorldRuntime("equipment-format-boundary");
        var state = world.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "selected-garment", "clothing", actor, 1);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = new("selected-garment") } : person).ToArray(),
        };
        var currentBytes = PrivateWorldRuntimeCodec.Encode(state);
        var current = PrivateWorldRuntimeCodec.Decode(currentBytes);
        Assert.Equal("selected-garment", current.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.ClothingLotId);

        var document = JsonNode.Parse(currentBytes)!;
        document["state"]!["schemaVersion"] = 37;
        document["state"]!["inhabitants"]![0]!["equipment"] = new JsonObject
        {
            [clothingProperty] = "selected-garment",
            ["carryAidLotId"] = null,
        };
        var previousBytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(previousBytes));
        Assert.Contains("minimum supported schema 38", error.Message, StringComparison.Ordinal);
        Assert.Equal(currentBytes, PrivateWorldRuntimeCodec.Encode(current));
    }

    [Fact]
    public void Native38RoundTripsAndPreservesRejected37EvenWithoutPhysicalOrCivicRecords()
    {
        var geography = new GeographyOptions("physical-content-format", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        var state = world.ExportState();
        Assert.Equal(38, state.SchemaVersion);
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

        var preceding = state with { SchemaVersion = 37 };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(preceding));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(preceding));
        var document = JsonNode.Parse(bytes)!;
        document["state"]!["schemaVersion"] = 37;
        var olderBytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(olderBytes));
        Assert.Contains("minimum supported schema 38", error.Message, StringComparison.Ordinal);

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
