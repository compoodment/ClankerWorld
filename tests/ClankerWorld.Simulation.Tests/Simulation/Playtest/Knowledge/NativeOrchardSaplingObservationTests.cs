using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;
using ClientSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class NativeOrchardObservationTests
{
    [Fact]
    public async Task PlantedOrchardPublishesItsSavedSaplingAfterWireRoundTripAndReload()
    {
        var (state, actor, target, _) = await ObserveEmptyTile();
        using var world = Restore(state, actor);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var beforeJson = JsonSerializer.Serialize(new OwnerWorldObservationStore(world).GetSnapshot(), options);
        var result = world.PlantTree(actor, TreeGrowthRules.Orchard, "orchard-observation-seed", target);
        Assert.True(result.Planted, result.Message);
        world.Validate();
        var saved = world.ExportState();
        var day = WorldCalendarRules.FromTick(world.WorldTick, world.WorldSystems.Config).DayIndex;
        Assert.DoesNotContain(saved.Society.Society.Inventory.Lots, lot => lot.Id == "orchard-observation-seed");
        var ecology = world.WorldSystems.Ecology.GetResource(result.TreeId!);
        Assert.True(ecology.IsPlanted);
        Assert.Equal(day + TreeGrowthRules.SaplingGrowthDays, ecology.NextRegenerationDay);
        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        loaded.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        string? afterJson = null;
        foreach (var current in new[] { world, loaded })
        {
            afterJson = JsonSerializer.Serialize(new OwnerWorldObservationStore(current).GetSnapshot(), options);
            var packet = JsonSerializer.Deserialize<ClientSnapshot>(afterJson, options)!;
            var tree = Assert.Single(packet.Resources, resource => resource.Id == result.TreeId);
            Assert.Equal("orchard", tree.TreeKind);
            Assert.Equal("sapling", tree.TreeStage);
            Assert.True(tree.IsPlanted);
            Assert.Equal(target.X, tree.Position.X);
            Assert.Equal(target.Y, tree.Position.Y);
            Assert.Single(packet.Resources, resource => resource.Position == tree.Position);
            Assert.Equal(state.Map.Resources.Count + 1, packet.Resources.Count);
        }
        output.WriteLine($"Native planted orchard {result.TreeId}: sapling at {target}, seed consumed, exact codec reload, saved growth day {ecology.NextRegenerationDay}.");
        if (Environment.GetEnvironmentVariable("CLANKER_ORCHARD_SAPLING_CAPTURE") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "before.json"), beforeJson);
            await File.WriteAllTextAsync(Path.Combine(folder, "after.json"), afterJson!);
        }
    }
}
