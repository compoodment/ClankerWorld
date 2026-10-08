using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldExpansionOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpansionKeepsDepartedWorkersCarriedMaterialsUntilTheirPhysicalReturn(bool returnFirst)
    {
        var state = Prepared();
        var first = Actor(state);
        var second = state.Society.Society.GetHousehold(Household).MemberIds.First(id => id != first);
        var house = Building(state, House);
        var source = state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsBuildable(point) &&
                state.Map.FootDistance(house.Position, point) >= 3 && state.Map.IsReachableOnFoot(house.Position, point) &&
                state.Inhabitants.All(person => person.Position != point) && !state.RoadTiles!.Contains(point) &&
                state.WorldSimulation!.Buildings.All(building => building.Position != point) &&
                state.Map.Resources.All(resource => resource.Position != point))
            .OrderBy(point => state.Map.FootDistance(house.Position, point)).First();
        var inventory = state.Society.Society.Inventory with
        {
            Lots = [],
            Reservations = [],
            Offers = [],
        };
        inventory = InventoryFixture.AddLot(inventory, "custody-house-grain", "grain", Household, 52, storageBuildingId: House);
        inventory = InventoryFixture.AddLot(inventory, "custody-expansion-wood", "wood", Household, 4,
            groundPosition: new InventoryGroundPosition(source.X, source.Y));
        var provider = new ExpansionCustodyChoices();
        using var preparing = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => provider);
        var firstOrder = Submit(preparing, first, "first-custody-expansion", "expand my House");
        for (var tick = 0; tick < 40 && !preparing.Society.Inventory.Lots.Any(lot => lot.ItemKind == "wood" &&
                 lot.OwnerId == first && lot.DeliveryBuildingId == House); tick++) await Tick(preparing);
        var carried = Assert.Single(preparing.Society.Inventory.Lots, lot => lot.ItemKind == "wood" &&
            lot.OwnerId == first && lot.DeliveryBuildingId == House);
        Assert.Equal(4, carried.Quantity);
        Assert.True(PersonalEquipmentRules.IsCarried(carried, first));
        Assert.Empty(preparing.WorldSimulation.BuildingExpansions ?? []);
        Assert.True(preparing.CancelOrder(new("cancel-carried-expansion", "owner:test", preparing.Society.WorldId,
            first, firstOrder.InstructionId)).Changed);
        provider.Wanted[first] = "household_leave";
        _ = preparing.SubmitInstruction(new OwnerInstructionRequest("leave-with-materials", "owner:test", first,
            OwnerInstructionKind.Suggestive, "Leave the household."));
        for (var tick = 0; tick < 32 && preparing.Society.GetInhabitant(first).HouseholdId is not null; tick++) await Tick(preparing);
        Assert.Null(preparing.Society.GetInhabitant(first).HouseholdId);
        provider.Wanted[first] = "safe_idle";
        var borrowed = preparing.Society.Inventory.GetLot(carried.Id);
        Assert.Equal((Household, first, 4), (borrowed.OwnerId, borrowed.CarrierId, borrowed.Quantity));
        Assert.Null(borrowed.DeliveryBuildingId);
        Assert.Equal("cancelled", Order(preparing, firstOrder).Status);
        if (returnFirst)
        {
            provider.Wanted[first] = "household_return:" + borrowed.Id;
            _ = preparing.SubmitInstruction(new OwnerInstructionRequest("return-expansion-materials", "owner:test", first,
                OwnerInstructionKind.Suggestive, "Return the household's wood to its House."));
            for (var tick = 0; tick < 64 && preparing.Society.Inventory.GetLot(borrowed.Id).StorageBuildingId != House; tick++)
                await Tick(preparing);
            var returned = preparing.Society.Inventory.GetLot(borrowed.Id);
            Assert.Equal((Household, House, 4), (returned.OwnerId, returned.StorageBuildingId, returned.Quantity));
            Assert.Null(returned.CarrierId);
            Assert.Contains(preparing.ExportState().Events, item => item.Kind == "borrowed_goods_returned");
            provider.Wanted[first] = "safe_idle";
        }
        state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(preparing.ExportState()));
        using var world = PrivateWorldRuntime.Restore(state, _ => new ExpansionCustodyChoices());
        using var paired = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new ExpansionCustodyChoices());
        var secondOrder = Submit(world, second, "second-custody-expansion", "expand my House");
        Assert.Equal(secondOrder, Submit(paired, second, "second-custody-expansion", "expand my House"));
        var beforeDiscard = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(beforeDiscard, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 48 && (!returnFirst || Order(world, secondOrder).Status != "finished"); tick++)
        {
            await TickTogether(world, paired);
            if (!returnFirst)
            {
                var retained = world.Society.Inventory.GetLot(borrowed.Id);
                Assert.Equal((Household, first, 4), (retained.OwnerId, retained.CarrierId, retained.Quantity));
                Assert.Null(retained.DeliveryBuildingId);
            }
        }
        if (returnFirst)
        {
            Assert.Equal(("finished", 1), (Order(world, secondOrder).Status, Order(world, secondOrder).CompletedUnits));
            Assert.Equal(0, Quantity(world, "wood"));
            Assert.Single(world.WorldSimulation.BuildingExpansions!, job => job.State == WorldProductionJobState.Completed);
        }
        else
        {
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "borrowed_goods_returned");
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "building_expansion_material_picked_up" &&
                item.Detail.StartsWith(second + ":" + borrowed.Id + ":", StringComparison.Ordinal));
        }
        world.Validate(); paired.Validate();
        using var restored = Reload(world);
        restored.Validate();
    }

    private sealed class ExpansionCustodyChoices : IDecisionProvider
    {
        public ConcurrentDictionary<string, string> Wanted { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var prefix = request.Observation.OperativeOrderInstructionId is not null ? "expand_building" :
                Wanted.GetValueOrDefault(request.Observation.InhabitantId, "safe_idle");
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}
