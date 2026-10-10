using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ChildFoodCapacityRecoveryTests
{
    private static readonly Lazy<Task<byte[]>> Born = new(BornState);

    [Theory]
    [InlineData("child", 8, "loose", true, true)]
    [InlineData("child", 7, "loose", true, false)]
    [InlineData("adult", 8, "loose", true, true)]
    [InlineData("infant", 8, "loose", false, false)]
    [InlineData("child", 8, "reserved", false, false)]
    [InlineData("child", 8, "orchard", false, false)]
    [InlineData("child", 8, "delivery", false, false)]
    [InlineData("child", 8, "equipped", true, true)]
    public async Task HungryChildFreesOnlySpareCargoThenCollectsAndEatsHouseholdFood(string stage, int cargo, string protection, bool canEat, bool makesRoom)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Born.Value);
        var child = Assert.Single(state.Society.Society.Births).ChildId;
        var household = state.Society.Society.GetInhabitant(child).HouseholdId!;
        var adult = state.Society.Society.GetHousehold(household).MemberIds.First(id => id != child);
        var actor = stage == "adult" ? adult : child;
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == FoodCapacityTestFixture.House);
        if (stage == "infant") state = WithAge(state, child, 0, SocietyAgeBand.Infant);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (!InventoryContainerRules.IsFood(lot.ItemKind) || lot.OwnerId != household)).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "capacity-house-bread", "bread", household, 8, storageBuildingId: house.InstanceId);
        var cargoKind = protection == "orchard" ? TreeGrowthRules.OrchardSeedItem : "stone";
        inventory = InventoryFixture.AddLot(inventory, "capacity-stone", cargoKind, actor, cargo);
        if (protection == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "capacity-held", actor, "capacity-stone", cargo,
                "protected_work", inventory.WorldTick + 2_000);
        if (protection == "orchard")
            inventory = InventoryFixture.Reserve(inventory, "orchard-replant:capacity-stone", actor, "capacity-stone", cargo,
                "orchard_replanting", inventory.WorldTick + 2_000);
        if (protection == "delivery")
            inventory = inventory with
            { Lots = inventory.Lots.Select(lot => lot.Id == "capacity-stone" ? lot with { DeliveryBuildingId = house.InstanceId } : lot).ToArray() };
        if (protection == "equipped")
            inventory = InventoryFixture.AddLot(inventory, "capacity-cloak", "rain_cloak", actor, 1);
        state = FoodCapacityTestFixture.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? house.Position : person.Position,
                HungerBasisPoints = person.InhabitantId == actor ? 1_500 : 9_500,
                LastDecisionContext = null,
                Equipment = person.InhabitantId == actor ? protection == "equipped" ? new PersonalEquipment("capacity-cloak") : null : person.Equipment,
            }).ToArray(),
        };
        var choices = new FoodCapacityTestFixture.Choices("make_room_for_food", "collect_shared_food", "consume_food");
        using var world = FoodCapacityTestFixture.Restore(state, actor, choices);
        Assert.Equal(cargo, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor,
            world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment));
        var initialStone = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == cargoKind).Sum(lot => lot.Quantity);
        var initialHouseholdStone = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == cargoKind && lot.OwnerId == household).Sum(lot => lot.Quantity);
        var protectedLot = world.Society.Inventory.GetLot("capacity-stone");
        var initial = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = FoodCapacityTestFixture.Restore(PrivateWorldRuntimeCodec.Decode(initial), actor,
            new FoodCapacityTestFixture.Choices("make_room_for_food", "collect_shared_food", "consume_food"));
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 40; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        var meals = world.ExportState().Events.Count(item => item.Kind == "food_consumed" && item.Detail == actor);
        var carriedBread = world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "bread" &&
            PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory, lot, actor)).Sum(lot => lot.Quantity);
        Assert.Equal(canEat, meals > 0);
        Assert.InRange(carriedBread, 0, 1);
        Assert.Equal(8 - meals - carriedBread, world.Society.Inventory.GetLot("capacity-house-bread").Quantity);
        Assert.Equal(canEat, world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints > 1_500);
        Assert.Equal(makesRoom, choices.Offers.Any(offered => offered.Contains("make_room_for_food")));
        Assert.Equal(makesRoom ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "spare_cargo_stored" &&
            item.Detail == $"{actor}:{cargoKind}:1:{house.InstanceId}"));
        Assert.Equal(cargo - (makesRoom ? 1 : 0), world.Society.Inventory.GetLot("capacity-stone").Quantity);
        Assert.Equal(initialStone, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == cargoKind).Sum(lot => lot.Quantity));
        Assert.Equal(initialHouseholdStone + (makesRoom ? 1 : 0), world.Society.Inventory.Lots
            .Where(lot => lot.ItemKind == cargoKind && lot.OwnerId == household).Sum(lot => lot.Quantity));
        if (protection is "reserved" or "orchard" or "delivery")
        {
            var retained = world.Society.Inventory.GetLot("capacity-stone");
            Assert.Equal((protectedLot.OwnerId, protectedLot.Quantity, protectedLot.ConditionBasisPoints, protectedLot.DeliveryBuildingId,
                protectedLot.StorageBuildingId, protectedLot.GroundPosition, protectedLot.CarrierId),
                (retained.OwnerId, retained.Quantity, retained.ConditionBasisPoints, retained.DeliveryBuildingId,
                retained.StorageBuildingId, retained.GroundPosition, retained.CarrierId));
        }
        if (protection == "reserved")
            Assert.Equal((cargo, InventoryReservationState.Reserved), (world.Society.Inventory.GetReservation("capacity-held").Quantity,
                world.Society.Inventory.GetReservation("capacity-held").State));
        if (protection == "orchard")
            Assert.Equal((cargo, InventoryReservationState.Reserved), (world.Society.Inventory.GetReservation("orchard-replant:capacity-stone").Quantity,
                world.Society.Inventory.GetReservation("orchard-replant:capacity-stone").State));
        if (protection == "equipped")
        {
            Assert.Equal((actor, 1), (world.Society.Inventory.GetLot("capacity-cloak").OwnerId, world.Society.Inventory.GetLot("capacity-cloak").Quantity));
            Assert.Equal("capacity-cloak", world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.ClothingLotId);
        }
        if (stage != "infant") Assert.NotEmpty(choices.Offers);
    }

    private static async Task<byte[]> BornState()
    {
        var (state, parent, _) = await FoodCapacityTestFixture.Generated("child-carried-milk-audit");
        var checkpoint = state.Society.Society;
        var household = checkpoint.GetHousehold(checkpoint.GetInhabitant(parent).HouseholdId!);
        var parents = household.MemberIds.Take(2).ToArray();
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint, new("capacity-parents", 1,
            SocietyRelationshipType.Partnership, parents[0], parents[1], checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "capacity-parents", 1, parents[1]).Checkpoint;
        checkpoint = ChosenBirthNameTestFixture.NameParent(checkpoint, parents[0]);
        checkpoint = checkpoint with { Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "capacity-birth-food", "food", household.Id, 4) };
        var birth = SocietyFixture.CommitBirth(checkpoint, new($"family:{parents[0]}:{checkpoint.WorldTick}", 1,
            parents[0], parents[1], household.Id, household.MemberIds, parents, "capacity-birth-food", 4, checkpoint.WorldTick,
            ChildName: ChosenBirthNameTestFixture.ChildName(checkpoint, parents[0], "Cargo"), PrimaryCaregiverId: parents[0]));
        var child = Assert.IsType<string>(birth.CreatedId);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == FoodCapacityTestFixture.House);
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = birth.Checkpoint },
            Inhabitants = state.Inhabitants.Append(new(child, house.Position, 9_500, 0, "curious", "grow with the household")).ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(parent) ? town with
            { ResidentIds = town.ResidentIds.Append(child).Order(StringComparer.Ordinal).ToArray() } : town).ToArray(),
        };
        state = WithAge(state, child, 4, SocietyAgeBand.Child);
        using var world = PrivateWorldRuntime.Restore(state);
        world.Validate();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private static PrivateWorldRuntimeState WithAge(PrivateWorldRuntimeState state, string child, int age, SocietyAgeBand band)
    {
        var checkpoint = state.Society.Society;
        var born = checkpoint.LifeTickAt(checkpoint.WorldTick) - age * checkpoint.Config.TicksPerLifecycleAge;
        return state with
        {
            Society = state.Society with
            {
                Society = checkpoint with
                {
                    Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == child ? person with
                    { BirthTick = born, BirthLifeTick = checkpoint.LifeClock is null ? null : born, AgeBand = band, LastLifecycleYearChecked = age } : person).ToArray(),
                }
            },
        };
    }
}
