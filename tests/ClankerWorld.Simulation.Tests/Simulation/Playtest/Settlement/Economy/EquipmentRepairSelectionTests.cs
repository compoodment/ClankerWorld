using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class EquipmentRepairSelectionTests
{
    [Theory]
    [InlineData(3_000, false, "selection-coat")]
    [InlineData(5_000, false, "selection-coat")]
    [InlineData(3_000, true, "selection-basket")]
    public async Task OrdinaryRepairSelectsAFeasibleItemAndKeepsBasketPreference(int basketCondition, bool basketMaterials, string target)
    {
        var (state, shopId) = TailorTestWorld.Create("repair-shadowed-by-basket", 0);
        var shop = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == shopId);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == shop.HouseholdId).Id;
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.ItemKind == "rope").ToArray())
        {
            var available = PersonalEquipmentRules.AvailableQuantity(inventory, lot);
            if (available > 0) inventory = InventoryFixture.Reserve(inventory, "unavailable:" + lot.Id,
                lot.OwnerId, lot.Id, available, "other_work", state.Society.Society.WorldTick + 1000);
        }
        inventory = InventoryFixture.WearSingleUnit(InventoryFixture.AddLot(inventory, "selection-basket", "basket", actor, 1), "selection-basket", 10_000 - basketCondition);
        inventory = InventoryFixture.WearSingleUnit(InventoryFixture.AddLot(inventory, "selection-coat", "padded_coat", actor, 1), "selection-coat", 7_000);
        inventory = InventoryFixture.AddLot(inventory, "selection-cloth", "cloth", actor, 1);
        if (basketMaterials)
        {
            inventory = InventoryFixture.AddLot(inventory, "selection-fiber", "fiber", actor, 1);
            inventory = InventoryFixture.AddLot(inventory, "selection-rope", "rope", actor, 1);
        }
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = shop.Position,
                HungerBasisPoints = 10_000,
                Survival = new(),
                Equipment = new(ClothingLotId: "selection-coat", CarryAidLotId: "selection-basket"),
            } : person with
            {
                HungerBasisPoints = 10_000,
                Survival = new(),
            }).ToArray(),
        };
        IDecisionProvider Provider(string id) => new ChooseRepair(id == actor);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Provider);
        world.Validate();
        for (var tick = 0; tick < 30 && world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair is null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var work = Assert.IsType<EquipmentRepairWork>(world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair);
        Assert.Equal(target, work.LotId);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair!.WorkDone);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), Provider);
        for (var tick = 0; tick < 10 && restored.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair is not null; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.InRange(restored.Society.Inventory.GetLot(target).ConditionBasisPoints, 8_900, 9_000);
        if (target == "selection-coat")
        {
            Assert.DoesNotContain(restored.Society.Inventory.Lots, lot => lot.Id == "selection-cloth");
            if (basketMaterials)
            {
                Assert.Equal(1, restored.Society.Inventory.GetLot("selection-fiber").Quantity);
                Assert.Equal(1, restored.Society.Inventory.GetLot("selection-rope").Quantity);
            }
        }
        else
        {
            Assert.Equal(1, restored.Society.Inventory.GetLot("selection-cloth").Quantity);
            Assert.DoesNotContain(restored.Society.Inventory.Lots, lot => lot.Id is "selection-fiber" or "selection-rope");
        }
        var equipment = restored.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!;
        Assert.Equal("selection-coat", equipment.ClothingLotId);
        Assert.Equal("selection-basket", equipment.CarryAidLotId);
        Assert.All(work.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Completed, restored.Society.Inventory.GetReservation(id).State));
        restored.Validate();
    }

    private sealed class ChooseRepair(bool repair) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = repair && observation.Candidates.Any(candidate => candidate.Id == "repair_equipment") ? "repair_equipment" : "safe_idle";
            return new(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, request.ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected, 1,
                new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
