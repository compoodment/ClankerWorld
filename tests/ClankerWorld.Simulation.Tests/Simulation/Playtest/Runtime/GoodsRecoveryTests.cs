using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldCollectionOrderTests
{
    [Fact]
    public void RecoveryQueryKeepsPhysicalReservationsAndRechecksChangedCheckpointAndCarryRoom()
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "recover-free", "wood", actor, 3, storageBuildingId: House);
        inventory = InventoryFixture.Reserve(inventory, "recover-held", actor, "recover-free", 2, "work", long.MaxValue);
        inventory = InventoryFixture.AddLot(inventory, "recover-pot", "storage_pot", actor, 1, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "recover-pot-food", "fruit", actor, 2, storageBuildingId: House, containerLotId: "recover-pot");
        inventory = InventoryFixture.Reserve(inventory, "recover-held-food", actor, "recover-pot-food", 1, "work", long.MaxValue);
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "recover-free" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray(),
        };
        state = WithInventory(state, inventory);
        using var world = Restore(state);
        var request = new GoodsRequest(GoodsUse.Recover, actor, GoodsOwners.One(actor),
            new GoodsKinds(["wood", "storage_pot", "fruit"]), Explain: true);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var answer = world.FindGoods(request);
        var match = Assert.Single(answer.Matches);
        Assert.Equal(("recover-free", 1, 1), (match.Lot.Id, match.Quantity, match.MoveUnits));
        Assert.Contains(("recover-pot", GoodsReason.Reservation), answer.Excluded);
        Assert.Contains(("recover-pot-food", GoodsReason.Place), answer.Excluded);
        Assert.Equal(match, world.RecheckGoods(request, match.Lot.Id));
        Assert.Equal(answer.Matches, world.FindGoods(request with { Explain = false }).Matches);
        Assert.DoesNotContain(world.FindGoods(request with { Use = GoodsUse.Collect }).Matches, item => item.Lot.Id == "recover-free");
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        inventory = InventoryFixture.AddLot(inventory, "recover-ballast", "stone", actor,
            PersonalEquipmentRules.FreeCapacity(inventory, actor, null));
        using var full = Restore(WithInventory(state, inventory));
        Assert.Null(full.RecheckGoods(request, "recover-free"));
        Assert.Contains(("recover-free", GoodsReason.CarryRoom), full.FindGoods(request).Excluded);
        Assert.Equal(1, Assert.Single(full.FindGoods(request with { Use = GoodsUse.RecoveryHoldings }).Matches).Quantity);
        inventory = InventoryFixture.Relocate(inventory, "set-down-recovery-ballast", "recover-ballast", actor,
            inventory.GetLot("recover-ballast").Quantity, groundPosition: new(state.Inhabitants.Single(person => person.InhabitantId == actor).Position.X,
                state.Inhabitants.Single(person => person.InhabitantId == actor).Position.Y));
        inventory = InventoryFixture.Relocate(inventory, "take-recovered-unit", "recover-free", actor, 1, carrierId: actor);
        using var changed = Restore(WithInventory(state, inventory));
        Assert.Null(changed.RecheckGoods(request, "recover-free"));
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(answer.Matches, loaded.FindGoods(request).Matches);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        changed.Validate();
        full.Validate();
    }
}
