using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class HouseToolsBehaviorTests
{
    [Fact]
    public async Task TreeOrderFetchesARealSeedDeliveredIntoHouseholdStock()
    {
        var (state, actor, _) = PreparedTreeOrder();
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var house = Assert.Single(state.WorldSimulation!.Buildings, building => building.HouseholdId == household &&
            state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        // Transfer the actually harvested seeds to shared ground stock as setup;
        // the normal haul order must then pick them up and deposit them physically.
        var inventory = state.Society.Society.Inventory;
        foreach (var seed in inventory.Lots.Where(lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem).ToArray())
            inventory = InventoryFixture.Transfer(inventory, "share-" + seed.Id, actor, household, seed.Id, seed.Quantity,
                "test_shared_seed_stock", destinationGroundPosition: new(house.Position.X, house.Position.Y));
        state = WithInventory(state, inventory);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position } : person).ToArray(),
        };
        using var delivering = Restore(state);
        var delivery = delivering.SubmitInstruction(new("share-native-seeds", "owner:test", actor, OwnerInstructionKind.MustDo,
            "haul two tree seeds to my House"));
        for (var tick = 0; tick < 12 && Order(delivering, delivery).Status != "finished"; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(delivering, delivery).Status);
        Assert.Equal(0, PersonalQuantity(delivering, actor, TreeGrowthRules.TreeSeedItem));
        Assert.Equal(2, delivering.Society.Inventory.Lots.Where(lot => lot.OwnerId == household &&
            lot.ItemKind == TreeGrowthRules.TreeSeedItem && lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
        var (plantingState, _, target) = PreparedTreeOrder(delivering.ExportState());
        using var world = Restore(plantingState);
        var receipt = world.SubmitInstruction(new("plant-shared-seed", "owner:test", actor, OwnerInstructionKind.MustDo,
            $"plant a conifer tree at {target.X},{target.Y}"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 20 && Order(world, receipt).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        }
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(1, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem).Sum(lot => lot.Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "equipment_collected" && item.Detail == actor + ":tree_seed");
        world.Validate();
    }

    [Fact]
    public async Task OrchardOrderPlantsTheReservedSeedProducedByARealFruitHarvest()
    {
        var (state, actor, _) = PreparedTreeOrder();
        var source = state.Map.Resources.Where(resource => resource.TreeKind == TreeGrowthRules.Orchard &&
                FreeNeighbors(state, actor, resource).Any()).OrderBy(resource => resource.Id, StringComparer.Ordinal).First();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = FreeNeighbors(state, actor, source).First(),
                TravelCooldownTicks = 0,
                LastDecisionContext = null,
            } : person).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == source.Id
                        ? TreeGrowthAndPlantingTests.InFruitingSeason(resource, state) : resource).ToArray(),
                },
            },
        };
        using var harvesting = Restore(state);
        var harvest = Gather(harvesting, actor, source, "fruit", "native-orchard-seed");
        for (var tick = 0; tick < 12 && Order(harvesting, harvest).Status != "finished"; tick++)
            Assert.True((await harvesting.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(harvesting, harvest).Status);
        var seed = Assert.Single(harvesting.Society.Inventory.Lots, lot => lot.ItemKind == TreeGrowthRules.OrchardSeedItem && lot.OwnerId == actor);
        Assert.Equal(1, seed.Quantity);
        var reservation = Assert.Single(harvesting.Society.Inventory.Reservations,
            item => item.LotId == seed.Id && item.Purpose == "orchard_replanting" && item.State == InventoryReservationState.Reserved);
        var (plantingState, _, target) = PreparedTreeOrder(harvesting.ExportState());
        using var world = Restore(plantingState);
        var receipt = world.SubmitInstruction(new("native-orchard-order", "owner:test", actor, OwnerInstructionKind.MustDo,
            $"plant an orchard tree at {target.X},{target.Y}"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(2, PersonalQuantity(world, actor, TreeGrowthRules.TreeSeedItem));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == seed.Id);
        Assert.Equal(InventoryReservationState.Released, world.Society.Inventory.GetReservation(reservation.Id).State);
        var tree = Assert.Single(world.ExportState().Map.Resources,
            resource => resource.Id.StartsWith(TreeGrowthRules.PlantedTreeIdPrefix, StringComparison.Ordinal));
        Assert.Equal((target, TreeGrowthRules.Orchard), (tree.Position, tree.TreeKind));
        world.Validate();
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }

    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, true)]
    public async Task TreeOrderNeedsPickupSpaceButCanPlantAnAlreadyCarriedSeedAtFullCapacity(bool stored, int room, bool plants)
    {
        var (state, actor, _) = PreparedTreeOrder();
        if (stored)
        {
            var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
            var house = Assert.Single(state.WorldSimulation!.Buildings, building => building.HouseholdId == household &&
                state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house"));
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { Position = house.Position } : person).ToArray(),
            };
            using var storing = Restore(state);
            var storage = storing.SubmitInstruction(new("store-capacity-seeds", "owner:test", actor, OwnerInstructionKind.MustDo,
                "store two tree seeds"));
            for (var tick = 0; tick < 8 && Order(storing, storage).Status != "finished"; tick++)
                Assert.True((await storing.AdvanceOneTickAsync()).Advanced);
            Assert.Equal("finished", Order(storing, storage).Status);
            Assert.All(storing.Society.Inventory.Lots.Where(lot => lot.ItemKind == TreeGrowthRules.TreeSeedItem),
                lot => Assert.Equal(house.InstanceId, lot.StorageBuildingId));
            state = storing.ExportState();
        }
        var free = PersonalEquipmentRules.FreeCapacity(state.Society.Society.Inventory, actor,
            state.Inhabitants.Single(person => person.InhabitantId == actor).Equipment);
        Assert.True(free > room);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "full-planting-load", "stone", actor, free - room));
        var (plantingState, _, target) = PreparedTreeOrder(state);
        using var world = Restore(plantingState);
        Assert.Equal(room, PersonalEquipmentRules.FreeCapacity(world.Society.Inventory, actor,
            world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment));
        var receipt = world.SubmitInstruction(new("plant-with-load", "owner:test", actor, OwnerInstructionKind.MustDo,
            $"plant a conifer tree at {target.X},{target.Y}"));
        for (var tick = 0; tick < (plants ? 20 : 1) && Order(world, receipt).Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(plants ? ("finished", 1) : ("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(plants ? 1 : 2, PersonalQuantity(world, actor, TreeGrowthRules.TreeSeedItem));
        Assert.Equal(free - room, world.Society.Inventory.GetLot("full-planting-load").Quantity);
        if (!plants) Assert.Contains("carrying space", Order(world, receipt).BlockedReason, StringComparison.Ordinal);
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        loaded.Validate();
    }
}
