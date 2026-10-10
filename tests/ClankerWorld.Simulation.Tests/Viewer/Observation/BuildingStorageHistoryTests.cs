using System.Text;
using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class BuildingStorageHistoryTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RecordedStorageChangesSurviveCompactionReconnectAndReplayWithoutTeachingAgents()
    {
        var state = GeographyGeneratorTests.StartedGeneratedWorld(new("recorded-storage-history", WorldSizePreset.Small));
        var house = state.WorldSimulation!.Buildings.First(building => state.WorldContent!.Buildings
            .Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var owner = house.HouseholdId!;
        var carrier = state.Society.Society.Inhabitants.First(person => person.HouseholdId == owner).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "history-stock", "wood", owner, 1,
            storageBuildingId: house.InstanceId);
        for (var index = 0; index <= PrivateWorldHistory.CompactionThreshold; index++)
            inventory = InventoryFixture.Relocate(inventory, "history-move:" + index, "history-stock", owner, 1,
                carrierId: index % 2 == 0 ? carrier : null,
                storageBuildingId: index % 2 == 0 ? null : house.InstanceId);
        using var world = PrivateWorldRuntime.Restore(FarmFieldTests.WithInventory(state, inventory));
        var beforeInspection = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var store = new OwnerWorldObservationStore(world);
        var snapshot = store.GetSnapshot();
        var changes = snapshot.PlacedBuildings.Single(building => building.InstanceId == house.InstanceId).RecentStorageChanges!;
        Assert.Equal(10, changes.Count);
        Assert.Equal(inventory.Events.TakeLast(10).Reverse().Select(item => item.EventId), changes.Select(item => item.EventId));
        Assert.Equal(new long[] { -1, 1, -1, 1, -1, 1, -1, 1, -1, 1 }, changes.Select(item => item.QuantityChange));
        Assert.All(changes, change => Assert.Equal("wood", change.ItemKind));
        var client = JsonSerializer.Deserialize<OwnerWorldSnapshot>(JsonSerializer.Serialize(snapshot, WireJson), WireJson)!;
        Assert.Equal(changes.Select(item => (item.EventId, item.WorldTick, item.ItemKind, item.QuantityChange)),
            client.PlacedBuildings.Single(building => building.InstanceId == house.InstanceId).RecentStorageChanges!
                .Select(item => (item.EventId, item.WorldTick, item.ItemKind, item.QuantityChange)));
        store.GetReconnectBaseline(0);
        Assert.Equal(beforeInspection, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        var directory = Directory.CreateTempSubdirectory("building-storage-history-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            file.Save(world);
            var compacted = world.ExportState();
            Assert.Equal(PrivateWorldHistory.RecentEventLimit, compacted.Society.Society.Inventory.Events.Count);
            Assert.True(compacted.Society.Society.Inventory.EventHistoryFloor > 0);
            var segment = JsonSerializer.Deserialize<PrivateWorldHistorySegment>(File.ReadAllBytes(
                Path.Combine(file.Path + ".history", compacted.HistoryArchiveHead! + ".json")))!;
            var archived = segment.Streams.Single(stream => stream.Name == "inventory").Events
                .Deserialize<InventoryEvent[]>()!;
            Assert.Contains(archived, item => item.StorageChanges?.Any(change =>
                change.BuildingId == house.InstanceId && change.ItemKind == "wood") == true);
            using var loaded = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(compacted), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
            Assert.Equal(changes, new OwnerWorldObservationStore(loaded).GetReconnectBaseline(0).Snapshot
                .PlacedBuildings.Single(building => building.InstanceId == house.InstanceId).RecentStorageChanges);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void StorageHistoryRejectsDamagedQuantitiesAtTheCheckpointBoundary()
    {
        var inventory = InventoryFixture.AddLot(InventoryFixture.CreateGenesis([]), "stock", "wood", "owner", 1,
            storageBuildingId: "house");
        var encoded = Encoding.UTF8.GetString(InventoryCheckpointCodec.Encode(inventory));
        Assert.Contains("\"quantityChange\":1", encoded);
        foreach (var invalid in new[] { "0", long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            Assert.Throws<InvalidDataException>(() => InventoryCheckpointCodec.Decode(Encoding.UTF8.GetBytes(
                encoded.Replace("\"quantityChange\":1", "\"quantityChange\":" + invalid, StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => InventoryCheckpointCodec.Decode(Encoding.UTF8.GetBytes(
            encoded.Replace("\"buildingId\":\"house\"", "\"buildingId\":\" house \"", StringComparison.Ordinal))));
    }
}
