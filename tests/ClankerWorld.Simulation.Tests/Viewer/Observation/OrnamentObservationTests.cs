using System.Text.Json;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using Client = ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed class OrnamentObservationTests
{
    [Fact]
    public void ActualWornOrnamentSurvivesSaveAndProjectsThroughTheClientWireWithoutChangingCargo()
    {
        const string lotId = "projection-gold-ornament";
        using var generated = NormalPathWorld.CreateGenerated("ornament-wire-projection",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            lotId, "gold_ornament", actor, 1);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        using var world = PrivateWorldRuntime.Restore(state);
        var before = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == actor).Equipment!;
        var worn = world.WearOrnament(actor, lotId);
        Assert.True(worn.Applied, worn.Failure);
        Assert.Equal(lotId, worn.LotId);
        Assert.Equal(lotId, world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.OrnamentLotId);
        Assert.Equal(inventory.GetLot(lotId), world.Society.Inventory.GetLot(lotId));

        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var snapshot = new OwnerWorldObservationStore(restored).GetSnapshot();
        var equipment = snapshot.Inhabitants.Single(person => person.Id == actor).Equipment!;
        Assert.Equal("gold_ornament", equipment.OrnamentKind);
        Assert.Equal(before.CarriedQuantity, equipment.CarriedQuantity);
        Assert.Equal(before.Capacity, equipment.Capacity);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<Client.OwnerWorldSnapshot>(
            JsonSerializer.Serialize(snapshot, options), options)!;
        var displayed = client.Inhabitants.Single(person => person.Id == actor).Equipment!;
        Assert.Equal("gold_ornament", displayed.OrnamentKind);
        Assert.Equal(equipment.CarriedQuantity, displayed.CarriedQuantity);
        Assert.Equal(equipment.Capacity, displayed.Capacity);

        var removed = restored.RemoveOrnament(actor);
        Assert.True(removed.Applied, removed.Failure);
        Assert.Equal(inventory.GetLot(lotId), restored.Society.Inventory.GetLot(lotId));
        var after = new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants.Single(person => person.Id == actor).Equipment!;
        Assert.Null(after.OrnamentKind);
        Assert.Equal(before.CarriedQuantity, after.CarriedQuantity);
        Assert.Equal(before.Capacity, after.Capacity);
    }
}
