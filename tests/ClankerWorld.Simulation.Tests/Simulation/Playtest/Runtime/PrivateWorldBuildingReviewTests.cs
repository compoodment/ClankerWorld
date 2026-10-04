using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldExpansionOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryPaidRebuildingGetsANewIdentityAfterItsEarlierExpansionOrder(bool completed)
    {
        var state = Prepared();
        var actor = Actor(state);
        var original = Building(state, House);
        using var empty = Restore(WithInventory(state, state.Society.Society.Inventory with { Lots = [] }));
        Assert.True(empty.RemoveBuilding(House, original.TownId, Household).Applied);
        state = ReadyToPayForOrdinaryHouse(empty.ExportState(), actor, original.DefinitionId,
            original.Position, "first-ordinary-wood");
        using var first = Restore(state);
        using var firstReplay = Reload(first);
        await TickTogether(first, firstReplay);
        var house = Assert.Single(first.WorldSimulation.Buildings, building =>
            building.HouseholdId == Household && building.DefinitionId == original.DefinitionId);
        AssertOrdinaryHousePayment(first, actor, house, "first-ordinary-wood");
        var inventory = InventoryFixture.AddLot(first.Society.Inventory, "ordinary-expansion-wood", "wood",
            Household, 52, storageBuildingId: house.InstanceId);
        state = WithInventory(first.ExportState(), inventory);
        using var expanded = Restore(state);
        var receipt = Submit(expanded, actor, "ordinary-house-expansion", "expand my House");
        var job = await StartJob(expanded, receipt);
        if (completed) await Finish(expanded, receipt);
        else Assert.True(expanded.CancelOrder(new("cancel-ordinary-expansion", "owner:test",
            expanded.Society.WorldId, actor, receipt.InstructionId)).Changed);
        Assert.Equal(completed ? WorldProductionJobState.Completed : WorldProductionJobState.Cancelled,
            Job(expanded, job.JobId).State);
        var binding = Order(expanded, receipt).ExpansionBinding!;
        Assert.Equal(house.InstanceId, binding.BuildingInstanceId);
        var finalHouse = Building(expanded.ExportState(), house.InstanceId);
        inventory = expanded.Society.Inventory with
        {
            Lots = expanded.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId != house.InstanceId).ToArray(),
        };
        using var cleared = Restore(WithInventory(expanded.ExportState(), inventory));
        Assert.True(cleared.RemoveBuilding(house.InstanceId, finalHouse.TownId, Household).Applied);
        state = ReadyToPayForOrdinaryHouse(cleared.ExportState(), actor, house.DefinitionId,
            finalHouse.Position, "ordinary-rebuild-wood");
        // A fresh public placement proves the actual site, ownership and paid inputs are valid.
        using (var control = Restore(state))
        {
            var placement = control.PlaceBuilding("ordinary-rebuild-control", house.DefinitionId,
                finalHouse.Position, Household);
            Assert.True(placement.Applied, placement.Failure);
            using var valid = Reload(control);
        }
        using (var alias = Restore(state))
        {
            var before = PrivateWorldRuntimeCodec.Encode(alias.ExportState());
            Assert.False(alias.PlaceBuilding(house.InstanceId, house.DefinitionId,
                finalHouse.Position, Household).Applied);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(alias.ExportState()));
        }
        using var rebuild = Restore(state);
        using var replay = Reload(rebuild);
        await TickTogether(rebuild, replay);
        // The first tick replaces the finished/cancelled expansion intention.
        // Ordinary project continuation then runs through the normal native path.
        if (!rebuild.WorldSimulation.Buildings.Any(building =>
                building.HouseholdId == Household && building.DefinitionId == house.DefinitionId))
            await TickTogether(rebuild, replay);
        Assert.True(rebuild.WorldSimulation.Buildings.Any(building =>
                building.HouseholdId == Household && building.DefinitionId == house.DefinitionId),
            string.Join("\n", rebuild.ExportState().Events.TakeLast(20).Select(item => item.Kind + ":" + item.Detail)));
        var replacement = Assert.Single(rebuild.WorldSimulation.Buildings, building =>
            building.HouseholdId == Household && building.DefinitionId == house.DefinitionId);
        Assert.NotEqual(house.InstanceId, replacement.InstanceId);
        AssertOrdinaryHousePayment(rebuild, actor, replacement, "ordinary-rebuild-wood");
        Assert.Equal(binding, Order(rebuild, receipt).ExpansionBinding);
        Assert.Equal(completed ? 1 : 0, Order(rebuild, receipt).CompletedUnits);
        Assert.Equal(Job(cleared, job.JobId), Job(rebuild, job.JobId));
        Assert.Empty(rebuild.WorldSimulation.ConstructionReceipts ?? []);
    }

    private static PrivateWorldRuntimeState ReadyToPayForOrdinaryHouse(PrivateWorldRuntimeState state,
        string actor, string definition, GridPoint position, string materialId)
    {
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, materialId, "wood",
            Household, 8, groundPosition: new(position.X, position.Y));
        return WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = position,
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
                Project = new SettlementProject(TownConstructionCandidateIds.Building(definition, position),
                    "House", state.Society.Society.WorldTick, "working", WorkDone: 10, LastTransitionTick: state.Society.Society.WorldTick),
            } : person).ToArray(),
        };
    }

    private static void AssertOrdinaryHousePayment(PrivateWorldRuntime world, string actor,
        PlacedBuilding house, string materialId)
    {
        var project = world.Inhabitants.Single(person => person.InhabitantId == actor).Project!;
        Assert.Equal("completed", project.Stage);
        Assert.Null(project.OrderInstructionId);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == materialId);
        var payment = world.Society.Inventory.Reservations.Where(item => item.LotId == materialId).ToArray();
        Assert.NotEmpty(payment);
        Assert.All(payment, item =>
        {
            Assert.Equal("building:" + house.InstanceId, item.Purpose);
            Assert.Equal(InventoryReservationState.Completed, item.State);
        });
        Assert.Equal(8, payment.Sum(item => item.Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "build_completed" &&
            item.Detail.StartsWith(house.InstanceId + ":", StringComparison.Ordinal));
    }
}
